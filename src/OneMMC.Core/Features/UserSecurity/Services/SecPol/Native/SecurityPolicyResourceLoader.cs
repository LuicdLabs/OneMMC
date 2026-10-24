using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Loads localized security policy text from the Windows modules that own it:
    /// <c>wsecedit.dll</c> (the Security Settings snap-in) and <c>scecli.dll</c>
    /// (the Security Configuration Engine client).
    /// <para>
    /// Two string shapes are read, both exactly as <c>secpol.msc</c> reads them:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Item strings</b> ??<c>DisplayName[\r\n Unit]</c>. The snap-in loads these for
    ///     the policies that are not registry values (password, lockout and audit policy,
    ///     and the account options), where the second line carries the numeric unit.
    ///   </item>
    ///   <item>
    ///     <b>Explain strings</b> ??<c>DisplayName\r\n\r\nExplainText</c>. These supply the
    ///     Explain tab. They are matched to a policy by display name through
    ///     <see cref="SecurityPolicyExplainIndex"/> rather than by hard-coded resource ID.
    ///   </item>
    /// </list>
    /// <para>
    /// Resources are read from the MUI that matches the application's UI culture where one
    /// exists, falling back to letting Windows pick, so policy names and explain text stay
    /// in one language.
    /// </para>
    /// </summary>
    public sealed partial class SecurityPolicyResourceLoader : IDisposable
    {
        /// <summary>The Security Settings snap-in, which owns most policy text.</summary>
        internal const string SnapInModuleName = "wsecedit.dll";

        /// <summary>The Security Configuration Engine client, which owns the UAC policy text.</summary>
        internal const string EngineModuleName = "scecli.dll";

        /// <summary>
        /// Highest string resource ID probed when enumerating a module. The snap-in's
        /// display-name strings live just below 60000 (<c>@wsecedit.dll,-59xxx</c>).
        /// </summary>
        private const int MaxStringResourceId = 60000;

        private const int ResourceBufferLength = 16384;

        /// <summary>
        /// Culture whose resources are used as the stable key when pairing a policy with
        /// its explain text. Windows ships this MUI on every edition that has any MUI,
        /// and the identifiers in it do not change between languages.
        /// </summary>
        private const string InvariantCultureName = "en-US";

        private static ILogger<SecurityPolicyResourceLoader> _logger = NullLogger<SecurityPolicyResourceLoader>.Instance;

        private readonly Dictionary<string, IntPtr> _modules = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _disposed;

        /// <summary>Gets the singleton instance of the resource loader.</summary>
        public static SecurityPolicyResourceLoader Instance { get; } = new();

        private SecurityPolicyResourceLoader() { }

        public void SetLogger(ILogger<SecurityPolicyResourceLoader> logger)
        {
            _logger = logger ?? NullLogger<SecurityPolicyResourceLoader>.Instance;
        }

        #region Raw string resources

        /// <summary>
        /// Loads a string resource from one of the security policy modules.
        /// Returns null when the module or the resource is unavailable.
        /// </summary>
        internal string? LoadString(string moduleName, int resourceId)
            => LoadString(moduleName, resourceId, invariant: false);

        /// <summary>
        /// Loads a string from the module's invariant (<c>en-US</c>) resources, or null
        /// when that MUI is not installed.
        /// </summary>
        internal string? LoadInvariantString(string moduleName, int resourceId)
            => LoadString(moduleName, resourceId, invariant: true);

        private string? LoadString(string moduleName, int resourceId, bool invariant)
        {
            if (resourceId <= 0)
                return null;

            IntPtr module = GetModule(moduleName, invariant);
            if (module == IntPtr.Zero)
                return null;

            try
            {
                char[] buffer = new char[ResourceBufferLength];
                int length = SecurityPolicyNativeMethods.LoadString(module, (uint)resourceId, buffer, buffer.Length);
                return length > 0 ? new string(buffer, 0, length) : null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[SecurityPolicyResourceLoader] Failed to load resource {ResourceId} from {ModuleName}", resourceId, moduleName);
                return null;
            }
        }

        /// <summary>
        /// Loads the raw snap-in string resource. The value is either an item string
        /// (<c>Name[\r\nUnit]</c>) or an explain string (<c>Name\r\n\r\nExplainText</c>).
        /// </summary>
        public string? LoadRawResource(int resourceId, bool logNotFound = true)
        {
            string? value = LoadString(SnapInModuleName, resourceId);
            if (value is null && logNotFound)
                _logger.LogDebug("[SecurityPolicyResourceLoader] Resource {ResourceId} not found", resourceId);

            return value;
        }

        /// <summary>
        /// Enumerates every string resource in a module as (id, value) pairs.
        /// Used to build the explain-text index; the result is not cached here.
        /// </summary>
        internal IEnumerable<KeyValuePair<int, string>> EnumerateStrings(string moduleName, bool invariant = false)
        {
            IntPtr module = GetModule(moduleName, invariant);
            if (module == IntPtr.Zero)
                yield break;

            char[] buffer = new char[ResourceBufferLength];
            for (int resourceId = 1; resourceId <= MaxStringResourceId; resourceId++)
            {
                int length = SecurityPolicyNativeMethods.LoadString(module, (uint)resourceId, buffer, buffer.Length);
                if (length > 0)
                    yield return new KeyValuePair<int, string>(resourceId, new string(buffer, 0, length));
            }
        }

        #endregion

        #region Item strings (name + unit)

        /// <summary>
        /// Reads an item string: the first line is the policy's display name and the
        /// optional second line is the unit shown next to a numeric value
        /// (for example "days" or "passwords remembered").
        /// </summary>
        internal PolicyItemText LoadItemText(int itemId) => LoadItemText(itemId, invariant: false);

        /// <summary>
        /// Reads an item string from the invariant resources, for use as a stable key.
        /// </summary>
        internal PolicyItemText LoadInvariantItemText(int itemId) => LoadItemText(itemId, invariant: true);

        private PolicyItemText LoadItemText(int itemId, bool invariant)
        {
            string? raw = LoadString(SnapInModuleName, itemId, invariant);
            if (string.IsNullOrEmpty(raw))
            {
                _logger.LogDebug("[SecurityPolicyResourceLoader] Item string {ItemId} not found", itemId);
                return PolicyItemText.Empty;
            }

            // An explain string would be mistaken for a name plus unit, so stop at the
            // blank line that separates a display name from its explain text.
            string normalized = NormalizeLineEndings(raw);
            int explainSeparator = normalized.IndexOf("\n\n", StringComparison.Ordinal);
            string text = explainSeparator >= 0 ? normalized.Substring(0, explainSeparator) : normalized;

            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return PolicyItemText.Empty;

            string name = lines[0].Trim();
            string unit = lines.Length > 1 ? lines[1].Trim() : string.Empty;
            return new PolicyItemText(name, unit);
        }

        #endregion

        #region Explain text

        /// <summary>
        /// Splits an explain string into its display name and explain text.
        /// Returns false when the value is not an explain string.
        /// </summary>
        internal static bool TrySplitExplainString(string raw, out string displayName, out string explainText)
        {
            displayName = string.Empty;
            explainText = string.Empty;

            if (string.IsNullOrEmpty(raw))
                return false;

            string normalized = NormalizeLineEndings(raw);
            int separator = normalized.IndexOf("\n\n", StringComparison.Ordinal);
            if (separator <= 0)
                return false;

            displayName = normalized.Substring(0, separator).Trim();
            explainText = normalized.Substring(separator + 2).Trim();
            return displayName.Length > 0 && explainText.Length > 0;
        }

        /// <summary>
        /// Reduces every line-break form these resources use to a single newline.
        /// The separator between a display name and its explain text is not written the
        /// same way in every language: some translations use <c>\r\r\n</c> where the
        /// English resources use <c>\r\n\r\n</c>.
        /// </summary>
        private static string NormalizeLineEndings(string value)
        {
            return value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
        }

        /// <summary>
        /// Gets the localized explain text for a policy, matched to the Windows explain
        /// string that carries the same display name.
        /// Falls back to the definition's own description when no match exists.
        /// </summary>
        public string GetExplainText(Models.SecPol.SecurityPolicyDefinition definition)
        {
            if (definition is null)
                return string.Empty;

            string explainText = SecurityPolicyExplainIndex.Instance.GetExplainText(definition);
            return string.IsNullOrEmpty(explainText) ? definition.Description : explainText;
        }

        #endregion

        #region Indirect strings

        /// <summary>
        /// Resolves an indirect string reference such as <c>@wsecedit.dll,-59001</c>.
        /// <para>
        /// The referenced module's own resource is preferred so that names come from the
        /// same MUI as the explain text; <c>SHLoadIndirectString</c> is the fallback for
        /// modules this loader does not own.
        /// </para>
        /// If the input does not start with <c>@</c>, it is returned as-is.
        /// </summary>
        public static string ResolveIndirectString(string input, bool suppressFailureLog = false)
        {
            if (string.IsNullOrEmpty(input) || !input.StartsWith('@'))
                return input;

            try
            {
                // Only the two security modules are opened and cached here; a reference
                // to any other module named in the registry goes through the shell.
                if (TryParseResourceReference(input, out string moduleName, out int resourceId) &&
                    IsOwnedModule(moduleName))
                {
                    string? value = Instance.LoadString(moduleName, resourceId);
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }

                if (TryResolveIndirectStringCore(input, out string? resolved))
                    return resolved!;

                string normalizedInput = NormalizeIndirectStringSource(input);
                if (!string.Equals(normalizedInput, input, StringComparison.OrdinalIgnoreCase) &&
                    TryResolveIndirectStringCore(normalizedInput, out resolved))
                {
                    return resolved!;
                }

                if (!suppressFailureLog)
                    _logger.LogDebug("[SecurityPolicyResourceLoader] Could not resolve indirect string '{Input}'", input);
            }
            catch (Exception ex)
            {
                if (!suppressFailureLog)
                    _logger.LogDebug(ex, "[SecurityPolicyResourceLoader] Error resolving indirect string '{Input}'", input);
            }

            return input;
        }

        private static bool TryResolveIndirectStringCore(string source, out string? resolved)
        {
            char[] buffer = new char[1024];
            int hr = SecurityPolicyNativeMethods.SHLoadIndirectString(source, buffer, (uint)buffer.Length, IntPtr.Zero);
            if (hr == 0)
            {
                int end = Array.IndexOf(buffer, '\0');
                string result = new string(buffer, 0, end >= 0 ? end : buffer.Length);
                if (!string.IsNullOrEmpty(result))
                {
                    resolved = result;
                    return true;
                }
            }

            resolved = null;
            return false;
        }

        private static string NormalizeIndirectStringSource(string input)
        {
            if (!TryParseResourceReference(input, out string moduleName, out int resourceId))
                return input;

            if (moduleName.Contains('\\') || moduleName.Contains('/') || moduleName.Contains('%'))
                return input;

            return $"@%SystemRoot%\\System32\\{moduleName},-{resourceId}";
        }

        /// <summary>
        /// Splits an indirect string reference such as <c>@wsecedit.dll,-59001</c> into
        /// the module file name and the string resource ID.
        /// </summary>
        internal static bool TryParseResourceReference(string input, out string moduleName, out int resourceId)
        {
            moduleName = string.Empty;
            resourceId = 0;

            if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('@'))
                return false;

            int commaIndex = input.LastIndexOf(',');
            if (commaIndex <= 1 || commaIndex >= input.Length - 1)
                return false;

            moduleName = Path.GetFileName(input.Substring(1, commaIndex - 1).Trim());
            string idPart = input.Substring(commaIndex + 1).Trim();
            if (idPart.StartsWith('-'))
                idPart = idPart.Substring(1);

            if (!int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out resourceId) || resourceId <= 0)
                return false;

            return !string.IsNullOrEmpty(moduleName);
        }

        #endregion

        #region Privilege display names

        /// <summary>
        /// Resolves the localized display name of a privilege or logon-right constant
        /// (for example <c>SeBackupPrivilege</c>) via Win32. Returns null if resolution fails.
        /// </summary>
        public string? ResolvePrivilegeDisplayName(string privilegeConstant)
            => ResolvePrivilegeDisplayName(privilegeConstant, invariant: false);

        /// <summary>
        /// Resolves a privilege's display name in the invariant language, for use as the
        /// key that pairs it with its explain text. Win32 selects the language from the
        /// calling thread's UI language, so that is set for the duration of the call and
        /// restored afterwards. Returns null when no name is available.
        /// </summary>
        internal string? ResolveInvariantPrivilegeDisplayName(string privilegeConstant)
            => ResolvePrivilegeDisplayName(privilegeConstant, invariant: true);

        private string? ResolvePrivilegeDisplayName(string privilegeConstant, bool invariant)
        {
            if (!invariant)
                return ResolvePrivilegeDisplayNameCore(privilegeConstant);

            ushort previousLanguage = SecurityPolicyNativeMethods.GetThreadUILanguage();
            SecurityPolicyNativeMethods.SetThreadUILanguage(SecurityPolicyNativeMethods.LANG_EN_US);
            try
            {
                return ResolvePrivilegeDisplayNameCore(privilegeConstant);
            }
            finally
            {
                SecurityPolicyNativeMethods.SetThreadUILanguage(previousLanguage);
            }
        }

        private string? ResolvePrivilegeDisplayNameCore(string privilegeConstant)
        {
            if (string.IsNullOrWhiteSpace(privilegeConstant))
                return null;

            int bufferLength = 0;
            Span<char> probe = stackalloc char[1];
            _ = SecurityPolicyNativeMethods.LookupPrivilegeDisplayName(
                null,
                privilegeConstant,
                probe,
                ref bufferLength,
                out _);

            int lastError = Marshal.GetLastWin32Error();
            if (lastError != SecurityPolicyNativeMethods.ERROR_INSUFFICIENT_BUFFER || bufferLength <= 0)
                return null;

            char[] buffer = new char[bufferLength + 1];
            if (!SecurityPolicyNativeMethods.LookupPrivilegeDisplayName(
                null,
                privilegeConstant,
                buffer,
                ref bufferLength,
                out _))
            {
                return null;
            }

            var displayName = new string(buffer, 0, bufferLength).Trim();
            return string.IsNullOrEmpty(displayName) ? null : displayName;
        }

        #endregion

        #region Module loading

        private static bool IsOwnedModule(string moduleName)
            => string.Equals(moduleName, SnapInModuleName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(moduleName, EngineModuleName, StringComparison.OrdinalIgnoreCase);

        private IntPtr GetModule(string moduleName, bool invariant)
        {
            if (!IsOwnedModule(moduleName))
            {
                _logger.LogDebug("[SecurityPolicyResourceLoader] Refusing to load unexpected module {ModuleName}", moduleName);
                return IntPtr.Zero;
            }

            string cacheKey = invariant ? $"{InvariantCultureName}\\{moduleName}" : moduleName;

            lock (_lock)
            {
                if (_disposed)
                    return IntPtr.Zero;

                if (_modules.TryGetValue(cacheKey, out IntPtr cached))
                    return cached;

                IntPtr module = LoadModule(moduleName, invariant);
                _modules[cacheKey] = module;
                return module;
            }
        }

        private static IntPtr LoadModule(string moduleName, bool invariant)
        {
            foreach (string candidate in GetModuleCandidatePaths(moduleName, invariant))
            {
                if (!File.Exists(candidate))
                    continue;

                IntPtr module = SecurityPolicyNativeMethods.LoadLibraryEx(
                    candidate,
                    IntPtr.Zero,
                    SecurityPolicyNativeMethods.LOAD_LIBRARY_AS_DATAFILE | SecurityPolicyNativeMethods.LOAD_LIBRARY_AS_IMAGE_RESOURCE);

                if (module != IntPtr.Zero)
                {
                    _logger.LogDebug("[SecurityPolicyResourceLoader] Loaded resources from {Candidate}", candidate);
                    return module;
                }

                _logger.LogDebug(
                    "[SecurityPolicyResourceLoader] Failed to load {Candidate}: error {Error}",
                    candidate,
                    Marshal.GetLastWin32Error());
            }

            _logger.LogDebug("[SecurityPolicyResourceLoader] No resource module available for {ModuleName}", moduleName);
            return IntPtr.Zero;
        }

        /// <summary>
        /// Yields the MUI for the application's UI culture (and its parents) before the
        /// neutral module, which lets Windows pick the system's own language. The MUI
        /// carries the strings; the neutral module holds no string table of its own.
        /// <para>
        /// For the invariant request only the <c>en-US</c> MUI qualifies: the neutral
        /// module would resolve to the system's language and so would not be a stable key.
        /// </para>
        /// </summary>
        private static IEnumerable<string> GetModuleCandidatePaths(string moduleName, bool invariant)
        {
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);

            if (invariant)
            {
                yield return Path.Combine(systemDirectory, InvariantCultureName, $"{moduleName}.mui");
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CultureInfo culture = CultureInfo.CurrentUICulture;
            while (!string.IsNullOrWhiteSpace(culture.Name))
            {
                string path = Path.Combine(systemDirectory, culture.Name, $"{moduleName}.mui");
                if (seen.Add(path))
                    yield return path;

                culture = culture.Parent;
            }

            string neutral = Path.Combine(systemDirectory, moduleName);
            if (seen.Add(neutral))
                yield return neutral;
        }

        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                foreach (IntPtr module in _modules.Values)
                {
                    if (module != IntPtr.Zero)
                        SecurityPolicyNativeMethods.FreeLibrary(module);
                }

                _modules.Clear();
                _logger.LogDebug("[SecurityPolicyResourceLoader] Resource modules unloaded");
            }
        }
    }

    /// <summary>
    /// The display name and optional numeric unit carried by a snap-in item string.
    /// </summary>
    internal readonly struct PolicyItemText
    {
        internal static PolicyItemText Empty => new(string.Empty, string.Empty);

        internal PolicyItemText(string name, string unit)
        {
            Name = name;
            Unit = unit;
        }

        internal string Name { get; }

        internal string Unit { get; }

        internal bool HasName => !string.IsNullOrEmpty(Name);
    }
}
