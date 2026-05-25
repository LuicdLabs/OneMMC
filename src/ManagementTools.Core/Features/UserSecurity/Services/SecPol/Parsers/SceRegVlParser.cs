using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Parses the Windows-maintained <c>sceregvl.inf</c> file to dynamically discover
    /// registry-based security option definitions. This eliminates the need to hard-code
    /// Security Options and automatically supports new policies added by Windows updates.
    /// <para>
    /// File location: <c>%SystemRoot%\inf\sceregvl.inf</c>
    /// </para>
    /// <para>
    /// Format (per line in [Register Registry Values]):
    /// <c>MACHINE\RegistryKeyPath\ValueName,RegType,%DisplayStringKey%,DisplayType[|%OptionKey%,OptionValue|...]</c>
    /// </para>
    /// </summary>
    internal sealed class SceRegVlParser
    {
        private static readonly string InfFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"..\inf\sceregvl.inf");

        private static readonly Lazy<SceRegVlParser> _instance = new(() => new SceRegVlParser());
        public static SceRegVlParser Instance => _instance.Value;

        private List<SecurityPolicyDefinition>? _cachedDefinitions;
        private readonly object _lock = new();
        private static ILogger<SceRegVlParser> _logger = NullLogger<SceRegVlParser>.Instance;

        private SceRegVlParser() { }

        public void SetLogger(ILogger<SceRegVlParser> logger)
        {
            _logger = logger ?? NullLogger<SceRegVlParser>.Instance;
        }

        /// <summary>
        /// Returns the parsed list of Security Options definitions from sceregvl.inf.
        /// The result is cached after the first successful parse.
        /// </summary>
        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions()
        {
            if (_cachedDefinitions != null)
                return _cachedDefinitions;

            lock (_lock)
            {
                if (_cachedDefinitions != null)
                    return _cachedDefinitions;

                _cachedDefinitions = ParseInfFile();
                _logger.LogDebug("[SceRegVlParser] Parsed {DefinitionCount} security option definitions from sceregvl.inf", _cachedDefinitions.Count);
                return _cachedDefinitions;
            }
        }

        /// <summary>
        /// Forces a re-parse of the INF file, clearing the cache.
        /// Useful after a Windows update that may have modified the file.
        /// </summary>
        public void InvalidateCache()
        {
            lock (_lock)
            {
                _cachedDefinitions = null;
                _logger.LogDebug("[SceRegVlParser] Cache invalidated");
            }
        }

        private List<SecurityPolicyDefinition> ParseInfFile()
        {
            var definitions = new List<SecurityPolicyDefinition>();

            string resolvedPath = Path.GetFullPath(InfFilePath);
            if (!File.Exists(resolvedPath))
            {
                _logger.LogDebug("[SceRegVlParser] sceregvl.inf not found at: {ResolvedPath}", resolvedPath);
                return definitions;
            }

            _logger.LogDebug("[SceRegVlParser] Parsing: {ResolvedPath}", resolvedPath);

            string[] lines;
            try
            {
                // Auto-detect encoding from BOM; sceregvl.inf is often UTF-16 LE
                lines = File.ReadAllLines(resolvedPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[SceRegVlParser] Failed to read file");
                return definitions;
            }

            var strings = ParseStringsSection(lines);
            bool inRegisterSection = false;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                    continue;

                // Track sections
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inRegisterSection = line.Equals("[Register Registry Values]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inRegisterSection)
                    continue;

                // Only process MACHINE\ entries (HKLM)
                if (!line.StartsWith("MACHINE\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var definition = ParseRegistryLine(line, strings);
                    if (definition != null)
                    {
                        definitions.Add(definition);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[SceRegVlParser] Error parsing line: '{Line}'", line);
                }
            }

            return definitions;
        }

        /// <summary>
        /// Parses the [Strings] section of the INF file.
        /// Returns a dictionary mapping string keys to their values.
        /// On localized Windows installations, these strings may be in the system language.
        /// </summary>
        private static Dictionary<string, string> ParseStringsSection(string[] lines)
        {
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inStringsSection = false;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inStringsSection = line.Equals("[Strings]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inStringsSection)
                    continue;

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                string key = line.Substring(0, equalsIndex).Trim();
                string value = line.Substring(equalsIndex + 1).Trim();

                // Remove surrounding quotes
                if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                strings[key] = value;

                // Windows sceregvl.inf may reference keys that differ from [Strings] entries
                // on some builds (e.g., LDAPClientConfidentiality vs LDAPClientIntegrity).
                if (key.Equals("LDAPClientIntegrity", StringComparison.OrdinalIgnoreCase) &&
                    !strings.ContainsKey("LDAPClientConfidentiality"))
                {
                    strings["LDAPClientConfidentiality"] = value;
                }
            }

            _logger.LogDebug($"[SceRegVlParser] Parsed {strings.Count} string entries from [Strings] section");
            return strings;
        }

        /// <summary>
        /// Parses a single line from the [Register Registry Values] section.
        /// Format: MACHINE\Path\ValueName,RegType,%DisplayKey%,DisplayType[|%OptKey%,OptVal|...]
        /// </summary>
        private static SecurityPolicyDefinition? ParseRegistryLine(string line, Dictionary<string, string> strings)
        {
            // Split on the first comma to separate path from the rest
            // Format: MACHINE\...\ValueName,RegType,%DisplayKey%,DisplayType[|options...]
            int firstComma = line.IndexOf(',');
            if (firstComma < 0)
                return null;

            string fullPath = line.Substring(0, firstComma).Trim();
            string remainder = line.Substring(firstComma + 1).Trim();

            // Extract registry key path and value name from MACHINE\...\ValueName
            if (!fullPath.StartsWith("MACHINE\\", StringComparison.OrdinalIgnoreCase))
                return null;

            string registryFullPath = fullPath.Substring("MACHINE\\".Length);

            int lastBackslash = registryFullPath.LastIndexOf('\\');
            if (lastBackslash < 0)
                return null;

            string registryKeyPath = registryFullPath.Substring(0, lastBackslash);
            string registryValueName = registryFullPath.Substring(lastBackslash + 1);

            // Parse: RegType,%DisplayKey%,DisplayType[|options...]
            // Split remainder by comma, but be careful with dropdown options which use |
            var parts = SplitRegisterFields(remainder);
            if (parts.Count < 3)
                return null;

            // Parse registry type
            if (!int.TryParse(parts[0], out int regType))
                return null;

            // Parse display string key
            string displayKey = ResolveStringReference(parts[1], strings);

            // Parse display type and dropdown options.
            // Format: DisplayType[,Value0|%Name0%,Value1|%Name1%,...]
            // Options are comma-separated, each in Value|%Name% format.
            string displayTypePart = parts[2];
            var displayParts = displayTypePart.Split(',');

            if (!int.TryParse(displayParts[0].Trim(), out int displayType))
                return null;

            var dropdownOptions = new List<PolicyDropdownOption>();
            for (int i = 1; i < displayParts.Length; i++)
            {
                var optionStr = displayParts[i].Trim();
                if (string.IsNullOrWhiteSpace(optionStr))
                    continue;

                int pipeIndex = optionStr.IndexOf('|');
                if (pipeIndex < 0)
                    continue;

                string valueStr = optionStr.Substring(0, pipeIndex).Trim();
                string displayRef = optionStr.Substring(pipeIndex + 1).Trim();

                string optionDisplayName = ResolveStringReference(displayRef, strings);

                object value;
                if (long.TryParse(valueStr, out long longVal))
                    value = longVal;
                else
                    value = valueStr;

                dropdownOptions.Add(new PolicyDropdownOption
                {
                    DisplayName = optionDisplayName,
                    Value = value
                });
            }

            // Map display type to SecurityPolicyType
            var policyType = MapDisplayTypeToSecurityPolicyType(displayType, regType, dropdownOptions.Count > 0);

            // Generate a stable key from the registry path
            string key = GenerateKey(registryKeyPath, registryValueName);

            var definition = new SecurityPolicyDefinition
            {
                Key = key,
                DisplayName = displayKey,
                Category = SecurityPolicyCategory.SecurityOptions,
                PolicyType = policyType,
                RegistryKeyPath = registryKeyPath,
                RegistryValueName = registryValueName,
                DropdownOptions = dropdownOptions,
                DataSource = PolicyDataSource.SceRegVl
            };

            // Set reasonable defaults for numeric types
            if (policyType == SecurityPolicyType.Numeric)
            {
                definition.MinValue = 0;
                definition.MaxValue = regType == 4 ? uint.MaxValue : long.MaxValue; // DWORD max for REG_DWORD
            }

            return definition;
        }

        /// <summary>
        /// Splits the fields after the registry path, respecting the fact that
        /// dropdowns embed | characters in the third field.
        /// Returns [RegType, %DisplayKey%, DisplayType|options...]
        /// </summary>
        private static List<string> SplitRegisterFields(string remainder)
        {
            var result = new List<string>();
            int commaCount = 0;
            int start = 0;

            for (int i = 0; i < remainder.Length; i++)
            {
                if (remainder[i] == ',' && commaCount < 2)
                {
                    result.Add(remainder.Substring(start, i - start).Trim());
                    start = i + 1;
                    commaCount++;
                }
            }

            // Add the rest (includes display type and any dropdown options)
            if (start < remainder.Length)
            {
                result.Add(remainder.Substring(start).Trim());
            }

            return result;
        }

        /// <summary>
        /// Resolves a %StringKey% reference using the [Strings] dictionary,
        /// then resolves any indirect string references (<c>@wsecedit.dll,-59001</c>)
        /// via <see cref="SecurityPolicyResourceLoader.ResolveIndirectString"/>.
        /// </summary>
        private static string ResolveStringReference(string reference, Dictionary<string, string> strings)
        {
            reference = reference.Trim();

            string resolved;
            if (reference.StartsWith("%") && reference.EndsWith("%") && reference.Length > 2)
            {
                string key = reference.Substring(1, reference.Length - 2);
                if (strings.TryGetValue(key, out string? value))
                {
                    resolved = value;
                }
                else if (TryResolveKnownStringAlias(key, strings, out value))
                {
                    resolved = value;
                }
                else
                {
                    _logger.LogDebug($"[SceRegVlParser] Unresolved string reference: {reference}");
                    return key; // Return key without % as fallback
                }
            }
            else
            {
                resolved = reference;
            }

            // Resolve indirect string references like @wsecedit.dll,-59001
            if (resolved.StartsWith("@"))
            {
                string indirect = SecurityPolicyResourceLoader.ResolveIndirectString(resolved, suppressFailureLog: true);
                if (string.IsNullOrWhiteSpace(indirect) || indirect.StartsWith("@", StringComparison.Ordinal))
                {
                    // Keep display name readable even when a particular resource ID
                    // is not resolvable on this Windows build.
                    return reference.StartsWith("%") && reference.EndsWith("%")
                        ? reference.Substring(1, reference.Length - 2)
                        : resolved;
                }

                resolved = indirect;
            }

            return resolved;
        }

        private static bool TryResolveKnownStringAlias(string key, Dictionary<string, string> strings, out string value)
        {
            value = string.Empty;

            static bool TryAlias(Dictionary<string, string> stringsMap, string aliasKey, out string aliasValue)
                => stringsMap.TryGetValue(aliasKey, out aliasValue!);

            if (key.Equals("LDAPClientConfidentiality", StringComparison.OrdinalIgnoreCase) &&
                TryAlias(strings, "LDAPClientIntegrity", out value))
                return true;

            if (key.Equals("RefusePasswordChange", StringComparison.OrdinalIgnoreCase) &&
                TryAlias(strings, "RefusePWChange", out value))
                return true;

            if (key.Equals("DontDisplayUserName", StringComparison.OrdinalIgnoreCase) &&
                TryAlias(strings, "DontDisplayLockedUserId", out value))
                return true;

            if (key.Equals("NullSessionShares", StringComparison.OrdinalIgnoreCase) &&
                TryAlias(strings, "NullShares", out value))
                return true;

            if (key.Equals("AddPrinterDrivers", StringComparison.OrdinalIgnoreCase) &&
                TryAlias(strings, "AddPrintDrivers", out value))
                return true;

            return false;
        }

        /// <summary>
        /// Maps the INF displayType code to our <see cref="SecurityPolicyType"/>.
        /// <para>INF display type codes:</para>
        /// <list type="bullet">
        ///   <item>0 ??Boolean (Enabled/Disabled)</item>
        ///   <item>1 ??Numeric</item>
        ///   <item>2 ??String (REG_SZ) or MultiString (REG_MULTI_SZ)</item>
        ///   <item>3 ??Dropdown (enum selection, may include options)</item>
        ///   <item>4 ??MultiString / multi-line text display</item>
        ///   <item>5 ??Bitmask flags</item>
        /// </list>
        /// </summary>
        private static SecurityPolicyType MapDisplayTypeToSecurityPolicyType(int displayType, int regType, bool hasDropdownOptions)
        {
            // Display type 5 = bitmask flags (e.g., NTLMMinClientSec)
            if (displayType == 5)
                return SecurityPolicyType.BitmaskFlags;

            // Display type 4 = multi-line text (e.g., LegalNoticeText)
            if (displayType == 4)
                return SecurityPolicyType.MultiString;

            if (regType == 7 && (displayType == 2 || displayType == 3) && !hasDropdownOptions)
                return SecurityPolicyType.MultiString;

            // If dropdown options are present, it's a dropdown regardless of display type
            if (hasDropdownOptions || displayType == 3)
                return SecurityPolicyType.Dropdown;

            return displayType switch
            {
                0 => SecurityPolicyType.Boolean,
                1 => SecurityPolicyType.Numeric,
                2 => regType == 7 ? SecurityPolicyType.MultiString : SecurityPolicyType.String,
                _ => SecurityPolicyType.Numeric
            };
        }

        /// <summary>
        /// Generates a stable, unique key from the registry path and value name.
        /// Uses the value name as the primary key, prefixed with a shortened path
        /// component if needed to avoid collisions.
        /// </summary>
        private static string GenerateKey(string keyPath, string valueName)
        {
            // Use the value name as primary key, with path hash suffix for uniqueness
            // This matches the convention used by existing hardcoded definitions
            return $"{keyPath}\\{valueName}";
        }
    }
}




