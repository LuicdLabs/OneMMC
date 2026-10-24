using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Builds the Security Options catalog from the Security Configuration Engine's own
    /// registry-value metadata under
    /// <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SeCEdit\Reg Values</c>.
    /// <para>
    /// This is the same source <c>secpol.msc</c> reads. Each subkey is named after the
    /// policy's registry location with backslashes replaced by forward slashes
    /// (<c>MACHINE/System/CurrentControlSet/Control/Lsa/LimitBlankPasswordUse</c>) and
    /// carries the values below. Windows itself populates and services this key from
    /// <c>sceregvl.inf</c>, so policies added by a Windows update appear here with no
    /// change to this application.
    /// </para>
    /// <list type="table">
    ///   <item><term>ValueType</term><description>REG_* type of the policy value.</description></item>
    ///   <item><term>DisplayName</term><description>Indirect string reference for the policy name.</description></item>
    ///   <item><term>DisplayType</term><description>Editor to present (see <see cref="MapPolicyType"/>).</description></item>
    ///   <item><term>DisplayUnit</term><description>Indirect string for a numeric policy's unit.</description></item>
    ///   <item><term>DisplayChoices</term><description><c>value|@module,-id</c> entries for a dropdown.</description></item>
    ///   <item><term>DisplayFlags</term><description><c>bit|@module,-id</c> entries for a bitmask.</description></item>
    /// </list>
    /// </summary>
    internal static class SecurityOptionsCatalog
    {
        private const string RegValuesKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SeCEdit\Reg Values";
        private const string MachinePrefix = "MACHINE\\";

        private const string ValueTypeName = "ValueType";
        private const string DisplayNameName = "DisplayName";
        private const string DisplayTypeName = "DisplayType";
        private const string DisplayUnitName = "DisplayUnit";
        private const string DisplayChoicesName = "DisplayChoices";
        private const string DisplayFlagsName = "DisplayFlags";

        /// <summary>
        /// Default numeric bounds the snap-in applies to a registry policy before any
        /// policy-specific override.
        /// </summary>
        private const long DefaultNumericMaximum = 999;

        /// <summary>
        /// The numeric ranges the snap-in hard-codes per policy. These bounds are part of
        /// the snap-in rather than of the registry metadata, so they are mirrored here by
        /// registry path, matching the values <c>secpol.msc</c> enforces.
        /// </summary>
        private static readonly Dictionary<string, long> NumericMaximums = new(StringComparer.OrdinalIgnoreCase)
        {
            [@"SYSTEM\CurrentControlSet\Services\LanManServer\Parameters\AutoDisconnect"] = 99999,
            [@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\CachedLogonsCount"] = 50,
            [@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\PasswordExpiryWarning"] = 999,
            [@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\InactivityTimeoutSecs"] = 599940,
            [@"SYSTEM\CurrentControlSet\Control\SAM\MinimumPasswordLengthAudit"] = 128
        };

        private static ILogger _logger = NullLogger.Instance;

        internal static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Reads every registry-based Security Option the running system declares.
        /// Returns an empty list when the catalog key is unavailable, which lets the
        /// caller fall back to parsing <c>sceregvl.inf</c>.
        /// </summary>
        internal static List<SecurityPolicyDefinition> GetDefinitions()
        {
            var definitions = new List<SecurityPolicyDefinition>();

            if (!PolicyNativeHelpers.TryOpenRegistryKey(RegValuesKeyPath, KEY_READ, out PolicyNativeHelpers.RegistryKeyScope rootScope, out int result))
            {
                _logger.LogDebug("[SecurityOptionsCatalog] Could not open '{KeyPath}': error {Result}", RegValuesKeyPath, result);
                return definitions;
            }

            using (rootScope)
            {
                foreach (string subKeyName in EnumerateSubKeyNames(rootScope.Handle))
                {
                    try
                    {
                        SecurityPolicyDefinition? definition = ReadDefinition(subKeyName);
                        if (definition is not null)
                            definitions.Add(definition);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[SecurityOptionsCatalog] Failed to read catalog entry '{SubKeyName}'", subKeyName);
                    }
                }
            }

            _logger.LogDebug("[SecurityOptionsCatalog] Read {Count} registry policy definitions", definitions.Count);
            return definitions;
        }

        #region Entry parsing

        private static SecurityPolicyDefinition? ReadDefinition(string subKeyName)
        {
            if (!TrySplitRegistryPath(subKeyName, out string registryKeyPath, out string registryValueName))
            {
                _logger.LogDebug("[SecurityOptionsCatalog] Skipping entry with unexpected name '{SubKeyName}'", subKeyName);
                return null;
            }

            string entryKeyPath = $@"{RegValuesKeyPath}\{subKeyName}";
            if (!PolicyNativeHelpers.TryOpenRegistryKey(entryKeyPath, KEY_READ, out PolicyNativeHelpers.RegistryKeyScope entryScope, out int result))
            {
                _logger.LogDebug("[SecurityOptionsCatalog] Could not open entry '{SubKeyName}': error {Result}", subKeyName, result);
                return null;
            }

            using (entryScope)
            {
                int valueType = ReadDword(entryScope.Handle, ValueTypeName) ?? REG_DWORD;
                int displayType = ReadDword(entryScope.Handle, DisplayTypeName) ?? 0;

                var dropdownOptions = new List<PolicyDropdownOption>();
                AppendOptions(entryScope.Handle, DisplayChoicesName, dropdownOptions);
                AppendOptions(entryScope.Handle, DisplayFlagsName, dropdownOptions);

                SecurityPolicyType policyType = MapPolicyType(displayType, valueType, dropdownOptions.Count > 0);
                string fullPath = $@"{registryKeyPath}\{registryValueName}";
                string? displayNameReference = ReadString(entryScope.Handle, DisplayNameName);

                var definition = new SecurityPolicyDefinition
                {
                    Key = fullPath,
                    DisplayName = ResolveDisplayName(displayNameReference) ?? fullPath,
                    DisplayNameReference = displayNameReference ?? string.Empty,
                    Category = SecurityPolicyCategory.SecurityOptions,
                    PolicyType = policyType,
                    RegistryKeyPath = registryKeyPath,
                    RegistryValueName = registryValueName,
                    DropdownOptions = dropdownOptions,
                    Unit = ResolveDisplayName(ReadString(entryScope.Handle, DisplayUnitName)) ?? string.Empty,

                    // A registry policy is configured only while its value exists, so
                    // every entry can be returned to Not Defined by deleting it.
                    AllowNotDefined = true,
                    DataSource = PolicyDataSource.SceRegValues
                };

                ApplyValueRange(definition, policyType, dropdownOptions);
                return definition;
            }
        }

        /// <summary>
        /// Maps the catalog's <c>DisplayType</c> to the editor this application shows.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        ///   <item>0 ??Enabled/Disabled</item>
        ///   <item>1 ??numeric with a unit</item>
        ///   <item>2 ??text</item>
        ///   <item>3 ??selection from <c>DisplayChoices</c></item>
        ///   <item>4 ??multi-line text</item>
        ///   <item>5 ??checkboxes from <c>DisplayFlags</c></item>
        /// </list>
        /// A REG_MULTI_SZ value is always edited as multi-line text, whatever display
        /// type the catalog declares, because a single line cannot represent it.
        /// </remarks>
        private static SecurityPolicyType MapPolicyType(int displayType, int valueType, bool hasOptions)
        {
            if (displayType == 5)
                return SecurityPolicyType.BitmaskFlags;

            if (displayType == 4)
                return SecurityPolicyType.MultiString;

            if (valueType == REG_MULTI_SZ && !hasOptions)
                return SecurityPolicyType.MultiString;

            if (hasOptions || displayType == 3)
                return SecurityPolicyType.Dropdown;

            return displayType switch
            {
                0 => SecurityPolicyType.Boolean,
                1 => SecurityPolicyType.Numeric,
                2 => SecurityPolicyType.String,
                _ => SecurityPolicyType.Numeric
            };
        }

        private static void ApplyValueRange(
            SecurityPolicyDefinition definition,
            SecurityPolicyType policyType,
            List<PolicyDropdownOption> options)
        {
            if (policyType == SecurityPolicyType.BitmaskFlags)
            {
                // The editable range of a flags policy is the union of its own flags.
                long mask = 0;
                foreach (PolicyDropdownOption option in options)
                {
                    if (option.Value is long flag)
                        mask |= flag;
                }

                definition.MinValue = 0;
                definition.MaxValue = mask == 0 ? uint.MaxValue : mask;
                return;
            }

            if (policyType != SecurityPolicyType.Numeric)
                return;

            definition.MinValue = 0;
            definition.MaxValue = NumericMaximums.TryGetValue(definition.Key, out long maximum)
                ? maximum
                : DefaultNumericMaximum;
        }

        /// <summary>
        /// Splits a catalog subkey name such as
        /// <c>MACHINE/System/CurrentControlSet/Control/Lsa/LimitBlankPasswordUse</c>
        /// into the registry key path and the value name.
        /// </summary>
        private static bool TrySplitRegistryPath(string subKeyName, out string registryKeyPath, out string registryValueName)
        {
            registryKeyPath = string.Empty;
            registryValueName = string.Empty;

            string path = subKeyName.Replace('/', '\\');
            if (!path.StartsWith(MachinePrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            path = path.Substring(MachinePrefix.Length);
            int lastSeparator = path.LastIndexOf('\\');
            if (lastSeparator <= 0 || lastSeparator == path.Length - 1)
                return false;

            registryKeyPath = path.Substring(0, lastSeparator);
            registryValueName = path.Substring(lastSeparator + 1);
            return true;
        }

        #endregion

        #region Value readers

        private static IEnumerable<string> EnumerateSubKeyNames(IntPtr keyHandle)
        {
            // Registry key names are limited to 255 characters, but catalog entries embed
            // a full policy path, so allow the maximum a key name can hold.
            const int maxKeyNameLength = 512;

            for (int index = 0; ; index++)
            {
                char[] buffer = new char[maxKeyNameLength];
                int length = buffer.Length;
                int result = RegEnumKeyEx(keyHandle, index, buffer, ref length, IntPtr.Zero, [], IntPtr.Zero, IntPtr.Zero);

                if (result == ERROR_NO_MORE_ITEMS)
                    break;

                if (result != ERROR_SUCCESS)
                {
                    _logger.LogDebug("[SecurityOptionsCatalog] RegEnumKeyEx failed at index {Index}: error {Result}", index, result);
                    break;
                }

                if (length > 0)
                    yield return new string(buffer, 0, length);
            }
        }

        private static int? ReadDword(IntPtr keyHandle, string valueName)
        {
            if (!PolicyNativeHelpers.TryReadRegistryValue(keyHandle, valueName, out PolicyNativeHelpers.RegistryValueBuffer buffer, out _))
                return null;

            using (buffer)
            {
                if (buffer.RegistryType == REG_DWORD)
                    return Marshal.ReadInt32(buffer.Pointer);

                string? text = Marshal.PtrToStringUni(buffer.Pointer);
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
            }
        }

        private static string? ReadString(IntPtr keyHandle, string valueName)
        {
            if (!PolicyNativeHelpers.TryReadRegistryValue(keyHandle, valueName, out PolicyNativeHelpers.RegistryValueBuffer buffer, out _))
                return null;

            using (buffer)
            {
                string? value = Marshal.PtrToStringUni(buffer.Pointer);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        /// <summary>
        /// Resolves an indirect string reference to display text. Returns null when the
        /// reference cannot be resolved, which happens for policies Windows still lists
        /// but no longer has a name for; the caller then shows the registry path, as the
        /// snap-in does.
        /// </summary>
        private static string? ResolveDisplayName(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            string resolved = SecurityPolicyResourceLoader.ResolveIndirectString(reference, suppressFailureLog: true).Trim();
            return resolved.Length == 0 || resolved.StartsWith('@') ? null : resolved;
        }

        /// <summary>
        /// Appends the options held in a REG_MULTI_SZ catalog value. Each entry is
        /// <c>value|@module,-id</c>; the label is an indirect string reference.
        /// </summary>
        private static void AppendOptions(IntPtr keyHandle, string valueName, List<PolicyDropdownOption> options)
        {
            if (!PolicyNativeHelpers.TryReadRegistryValue(keyHandle, valueName, out PolicyNativeHelpers.RegistryValueBuffer buffer, out _))
                return;

            using (buffer)
            {
                if (buffer.RegistryType != REG_MULTI_SZ)
                    return;

                string multiString = PolicyNativeHelpers.ReadMultiString(buffer.Pointer, buffer.Size);
                foreach (string entry in multiString.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    int separator = entry.IndexOf('|');
                    if (separator <= 0)
                        continue;

                    if (!long.TryParse(
                            entry.AsSpan(0, separator).Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out long value))
                    {
                        continue;
                    }

                    string label = SecurityPolicyResourceLoader.ResolveIndirectString(
                        entry.Substring(separator + 1).Trim(),
                        suppressFailureLog: true);

                    if (string.IsNullOrWhiteSpace(label) || label.StartsWith('@'))
                        continue;

                    options.Add(new PolicyDropdownOption { DisplayName = label, Value = value });
                }
            }
        }

        #endregion
    }
}
