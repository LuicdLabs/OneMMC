using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Loads localized security policy resources from Windows system DLLs (wsecedit.dll).
    /// <para>
    /// Each resource string in wsecedit.dll has the format:
    /// <c>DisplayName\r\n\r\nExplainText</c>, allowing both the display name and
    /// explain text to be extracted from a single resource ID.
    /// </para>
    /// <para>
    /// Also provides <see cref="ResolveIndirectString"/> to resolve MUI indirect
    /// string references (<c>@wsecedit.dll,-59001</c>) used in <c>sceregvl.inf</c>.
    /// </para>
    /// </summary>
    public sealed partial class SecurityPolicyResourceLoader : IDisposable
    {
        private IntPtr _wseceditHandle = IntPtr.Zero;
        private bool _disposed;
        private readonly object _lock = new();
        private static ILogger<SecurityPolicyResourceLoader> _logger = NullLogger<SecurityPolicyResourceLoader>.Instance;

        /// <summary>
        /// Default path to wsecedit.dll which contains security policy explain text.
        /// </summary>
        private static readonly string WseceditDllPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wsecedit.dll");

        /// <summary>
        /// Gets the singleton instance of the resource loader.
        /// </summary>
        public static SecurityPolicyResourceLoader Instance { get; } = new();

        private SecurityPolicyResourceLoader() { }

        public void SetLogger(ILogger<SecurityPolicyResourceLoader> logger)
        {
            _logger = logger ?? NullLogger<SecurityPolicyResourceLoader>.Instance;
        }

        /// <summary>
        /// Loads the raw resource string from wsecedit.dll.
        /// The string typically has the format: <c>DisplayName\r\n\r\nExplainText</c>.
        /// Returns null if the resource cannot be loaded.
        /// </summary>
        /// <param name="resourceId">The string resource ID in wsecedit.dll.</param>
        /// <returns>The raw resource string, or null if not found.</returns>
        public string? LoadRawResource(int resourceId, bool logNotFound = true)
        {
            if (resourceId <= 0)
                return null;

            try
            {
                EnsureDllLoaded();

                if (_wseceditHandle == IntPtr.Zero)
                    return null;

                var sb = new StringBuilder(8192);
                int length = SecurityPolicyNativeMethods.LoadString(
                    _wseceditHandle,
                    (uint)resourceId,
                    sb,
                    sb.Capacity);

                if (length > 0)
                {
                    return sb.ToString();
                }

                if (logNotFound)
                    _logger.LogDebug($"[SecurityPolicyResourceLoader] Resource {resourceId} not found");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[SecurityPolicyResourceLoader] Error loading resource {resourceId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads only the explain text portion from wsecedit.dll.
        /// Extracts the text after the first double-newline separator.
        /// Returns null if the resource cannot be loaded.
        /// </summary>
        public string? LoadExplainText(int resourceId)
        {
            string? raw = LoadRawResource(resourceId);
            if (raw == null) return null;

            // Resource format: "DisplayName\r\n\r\nExplainText..."
            int separatorIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (separatorIdx < 0)
                separatorIdx = raw.IndexOf("\n\n", StringComparison.Ordinal);

            if (separatorIdx >= 0)
            {
                // Skip past the separator
                int skipLen = raw[separatorIdx] == '\r' ? 4 : 2;
                string explainText = raw.Substring(separatorIdx + skipLen).Trim();
                return string.IsNullOrEmpty(explainText) ? raw : explainText;
            }

            // No separator found ??return the whole string as explain text
            return raw;
        }

        /// <summary>
        /// Loads only the display name portion from wsecedit.dll.
        /// Extracts the text before the first double-newline separator.
        /// Returns null if the resource cannot be loaded or the format does not contain
        /// an explicit display-name segment.
        /// </summary>
        public string? LoadDisplayName(int resourceId)
        {
            return LoadDisplayNameInternal(resourceId, logNotFound: true);
        }

        internal string? LoadDisplayNameSilently(int resourceId)
        {
            return LoadDisplayNameInternal(resourceId, logNotFound: false);
        }

        private string? LoadDisplayNameInternal(int resourceId, bool logNotFound)
        {
            string? raw = LoadRawResource(resourceId, logNotFound);
            if (raw == null) return null;

            // Resource format: "DisplayName\r\n\r\nExplainText..."
            int separatorIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (separatorIdx < 0)
                separatorIdx = raw.IndexOf("\n\n", StringComparison.Ordinal);

            if (separatorIdx >= 0)
            {
                string displayName = raw.Substring(0, separatorIdx).Trim();
                return string.IsNullOrEmpty(displayName) ? null : displayName;
            }

            return null;
        }

        private static int GetDisplayNameResourceId(Models.SecPol.SecurityPolicyDefinition definition)
        {
            return definition.DisplayNameResourceId > 0
                ? definition.DisplayNameResourceId
                : definition.ExplainResourceId;
        }

        /// <summary>
        /// Resolves an indirect string reference such as <c>@wsecedit.dll,-59001</c>
        /// to its localized value using <c>SHLoadIndirectString</c>.
        /// <para>
        /// If the input does not start with <c>@</c>, it is returned as-is.
        /// </para>
        /// </summary>
        /// <param name="input">The string to resolve (may or may not be an indirect reference).</param>
        /// <returns>The resolved localized string, or the original input if resolution fails.</returns>
        public static string ResolveIndirectString(string input, bool suppressFailureLog = false)
        {
            if (string.IsNullOrEmpty(input) || !input.StartsWith("@"))
                return input;

            try
            {
                if (TryResolveIndirectStringCore(input, out string? resolved))
                {
                    return resolved!;
                }

                string normalizedInput = NormalizeIndirectStringSource(input);
                if (!string.Equals(normalizedInput, input, StringComparison.OrdinalIgnoreCase) &&
                    TryResolveIndirectStringCore(normalizedInput, out resolved))
                {
                    return resolved!;
                }

                if (TryResolveKnownDllResource(normalizedInput, out resolved))
                {
                    return resolved!;
                }

                if (!suppressFailureLog)
                    _logger.LogDebug($"[SecurityPolicyResourceLoader] SHLoadIndirectString fallback failed for '{input}'");
            }
            catch (Exception ex)
            {
                if (!suppressFailureLog)
                    _logger.LogDebug($"[SecurityPolicyResourceLoader] Error resolving indirect string '{input}': {ex.Message}");
            }

            return input;
        }

        private static bool TryResolveIndirectStringCore(string source, out string? resolved)
        {
            var sb = new StringBuilder(1024);
            int hr = SecurityPolicyNativeMethods.SHLoadIndirectString(source, sb, (uint)sb.Capacity, IntPtr.Zero);
            if (hr == 0)
            {
                string result = sb.ToString();
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
            if (!TryParseResourceReference(input, out string dllName, out int resourceId))
                return input;

            if (dllName.Contains('\\') || dllName.Contains('/') || dllName.Contains('%'))
                return input;

            return $"@%SystemRoot%\\System32\\{dllName},-{resourceId}";
        }

        private static bool TryResolveKnownDllResource(string input, out string? resolved)
        {
            resolved = null;
            if (!TryParseResourceReference(input, out string dllName, out int resourceId))
                return false;

            string normalizedDllName = Path.GetFileName(dllName);
            if (!string.Equals(normalizedDllName, "wsecedit.dll", StringComparison.OrdinalIgnoreCase))
                return false;

            resolved = Instance.LoadDisplayNameSilently(resourceId);
            return !string.IsNullOrEmpty(resolved);
        }

        private static bool TryParseResourceReference(string input, out string dllName, out int resourceId)
        {
            dllName = string.Empty;
            resourceId = 0;

            if (string.IsNullOrWhiteSpace(input) || !input.StartsWith("@", StringComparison.Ordinal))
                return false;

            int commaIndex = input.LastIndexOf(',');
            if (commaIndex <= 1 || commaIndex >= input.Length - 1)
                return false;

            dllName = input.Substring(1, commaIndex - 1).Trim();
            string idPart = input.Substring(commaIndex + 1).Trim();
            if (idPart.StartsWith("-", StringComparison.Ordinal))
                idPart = idPart.Substring(1);

            if (!int.TryParse(idPart, out resourceId) || resourceId <= 0)
                return false;

            return !string.IsNullOrEmpty(dllName);
        }

        /// <summary>
        /// Ensures the wsecedit.dll is loaded. Thread-safe.
        /// </summary>
        private void EnsureDllLoaded()
        {
            if (_wseceditHandle != IntPtr.Zero)
                return;

            lock (_lock)
            {
                if (_wseceditHandle != IntPtr.Zero)
                    return;

                if (!File.Exists(WseceditDllPath))
                {
                    _logger.LogDebug($"[SecurityPolicyResourceLoader] DLL not found: {WseceditDllPath}");
                    return;
                }

                _wseceditHandle = SecurityPolicyNativeMethods.LoadLibraryEx(
                    WseceditDllPath,
                    IntPtr.Zero,
                    SecurityPolicyNativeMethods.LOAD_LIBRARY_AS_DATAFILE);

                if (_wseceditHandle == IntPtr.Zero)
                {
                    _logger.LogDebug($"[SecurityPolicyResourceLoader] Failed to load DLL: {WseceditDllPath}");
                }
                else
                {
                    _logger.LogDebug($"[SecurityPolicyResourceLoader] Loaded DLL: {WseceditDllPath}");
                }
            }
        }

        /// <summary>
        /// Gets the localized explain text for a policy definition.
        /// Loads from wsecedit.dll and extracts the text portion after the display name.
        /// Falls back to the hardcoded Description if resource loading fails.
        /// </summary>
        public string GetExplainText(Models.SecPol.SecurityPolicyDefinition definition)
        {
            if (definition.ExplainResourceId > 0)
            {
                var explainText = LoadExplainText(definition.ExplainResourceId);
                if (!string.IsNullOrEmpty(explainText))
                    return explainText;
            }

            return definition.Description;
        }

        /// <summary>
        /// Gets the localized display name for a policy definition.
        /// Extracts the display name from the wsecedit.dll resource at
        /// <see cref="Models.SecPol.SecurityPolicyDefinition.ExplainResourceId"/>.
        /// Falls back to the definition's <see cref="Models.SecPol.SecurityPolicyDefinition.DisplayName"/>.
        /// </summary>
        public string GetDisplayName(Models.SecPol.SecurityPolicyDefinition definition)
        {
            int resourceId = GetDisplayNameResourceId(definition);
            if (resourceId > 0)
            {
                var localizedName = LoadDisplayName(resourceId);
                if (!string.IsNullOrEmpty(localizedName))
                    return localizedName;
            }

            return definition.DisplayName;
        }

        /// <summary>
        /// Resolves the localized display name of a privilege constant
        /// (for example <c>SeBackupPrivilege</c>) via Win32.
        /// Returns null if resolution fails.
        /// </summary>
        public string? ResolvePrivilegeDisplayName(string privilegeConstant)
        {
            if (string.IsNullOrWhiteSpace(privilegeConstant))
                return null;

            int bufferLength = 0;
            _ = SecurityPolicyNativeMethods.LookupPrivilegeDisplayName(
                null,
                privilegeConstant,
                new StringBuilder(1),
                ref bufferLength,
                out _);

            int lastError = Marshal.GetLastWin32Error();
            if (lastError != SecurityPolicyNativeMethods.ERROR_INSUFFICIENT_BUFFER || bufferLength <= 0)
                return null;

            var buffer = new StringBuilder(bufferLength + 1);
            if (!SecurityPolicyNativeMethods.LookupPrivilegeDisplayName(
                null,
                privilegeConstant,
                buffer,
                ref bufferLength,
                out _))
            {
                return null;
            }

            var displayName = buffer.ToString().Trim();
            return string.IsNullOrEmpty(displayName) ? null : displayName;
        }

        /// <summary>
        /// Resolves the display name for a definition, populating the
        /// <see cref="Models.SecPol.SecurityPolicyDefinition.DisplayName"/> property
        /// with the localized value from wsecedit.dll.
        /// <para>
        /// Call this after creating a definition to ensure the DisplayName is localized.
        /// If wsecedit.dll loading fails, the existing DisplayName value is preserved.
        /// </para>
        /// </summary>
        public void ResolveDefinitionDisplayName(Models.SecPol.SecurityPolicyDefinition definition)
        {
            int resourceId = GetDisplayNameResourceId(definition);
            if (resourceId > 0)
            {
                var localizedName = LoadDisplayName(resourceId);
                if (!string.IsNullOrEmpty(localizedName))
                {
                    definition.DisplayName = localizedName;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_wseceditHandle != IntPtr.Zero)
            {
                SecurityPolicyNativeMethods.FreeLibrary(_wseceditHandle);
                _wseceditHandle = IntPtr.Zero;
                _logger.LogDebug("[SecurityPolicyResourceLoader] DLL unloaded");
            }
        }
    }
}




