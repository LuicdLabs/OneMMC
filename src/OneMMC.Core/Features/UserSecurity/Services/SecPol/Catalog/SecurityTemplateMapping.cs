using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Localization;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>One line of a security template: a key in a section and its value.</summary>
    internal readonly struct SecurityTemplateEdit
    {
        internal SecurityTemplateEdit(string section, string key, string value)
        {
            Section = section;
            Key = key;
            Value = value;
        }

        internal string Section { get; }

        internal string Key { get; }

        internal string Value { get; }
    }

    /// <summary>
    /// Translates between Account/Local policy values and the security template lines the Security
    /// Configuration Engine reads and writes for them: a key in <c>[System Access]</c>,
    /// <c>[Event Audit]</c>, <c>[Privilege Rights]</c> or <c>[Registry Values]</c>. Key names and value
    /// encodings match the engine's own export (<c>secedit /export</c>) line for line.
    /// </summary>
    internal static class SecurityTemplateMapping
    {
        /// <summary>
        /// The <c>[System Access]</c> key for each password/lockout policy and non-registry account
        /// option, keyed by the application's own policy key.
        /// </summary>
        private static readonly Dictionary<string, string> SystemAccessKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PasswordHistoryLength"] = "PasswordHistorySize",
            ["MaxPasswordAge"] = "MaximumPasswordAge",
            ["MinPasswordAge"] = "MinimumPasswordAge",
            ["MinPasswordLength"] = "MinimumPasswordLength",
            ["PasswordComplexity"] = "PasswordComplexity",
            ["ClearTextPassword"] = "ClearTextPassword",
            ["LockoutThreshold"] = "LockoutBadCount",
            ["LockoutDuration"] = "LockoutDuration",
            ["LockoutObservationWindow"] = "ResetLockoutCount",
            ["AllowAdminLockout"] = "AllowAdministratorLockout",
            ["AdminAccountStatus"] = "EnableAdminAccount",
            ["GuestAccountStatus"] = "EnableGuestAccount",
            ["ForceLogoffWhenHourExpire"] = "ForceLogoffWhenHourExpire",
            ["LSAAnonymousNameLookup"] = "LSAAnonymousNameLookup"
        };

        /// <summary>The <c>[System Access]</c> key for the account-rename options, whose value is a quoted name.</summary>
        private static readonly Dictionary<string, string> SystemAccessNameKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["RenameAdministratorAccount"] = "NewAdministratorName",
            ["RenameGuestAccount"] = "NewGuestName"
        };

        /// <summary>The <c>[Event Audit]</c> key for each basic audit category.</summary>
        private static readonly Dictionary<string, string> EventAuditKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AuditSystemEvents"] = "AuditSystemEvents",
            ["AuditLogonEvents"] = "AuditLogonEvents",
            ["AuditObjectAccess"] = "AuditObjectAccess",
            ["AuditPrivilegeUse"] = "AuditPrivilegeUse",
            ["AuditPolicyChange"] = "AuditPolicyChange",
            ["AuditAccountManagement"] = "AuditAccountManage",
            ["AuditProcessTracking"] = "AuditProcessTracking",
            ["AuditDirectoryServiceAccess"] = "AuditDSAccess",
            ["AuditAccountLogon"] = "AuditAccountLogon"
        };

        /// <summary>
        /// The engine's value for "never" (a password that never expires, a lockout that lasts until an
        /// administrator unlocks the account). The application, like secpol.msc, shows it as 0; the engine
        /// rejects 0 for these keys with SCESTATUS_INVALID_PARAMETER.
        /// </summary>
        private const long ForeverValue = -1;

        /// <summary>The <c>[System Access]</c> keys whose 0 in the application is the engine's <see cref="ForeverValue"/>.</summary>
        private static readonly HashSet<string> ForeverKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "MaximumPasswordAge",
            "LockoutDuration"
        };

        /// <summary>
        /// Registry value name of the Kerberos encryption-types policy, matched on the value name because
        /// a definition's key is its full registry path.
        /// </summary>
        private const string SupportedEncryptionTypesValueName = "SupportedEncryptionTypes";
        private const long KerberosLowFlagsMask = 0x1F; // 1|2|4|8|16
        private const long KerberosLegacyFutureBit = 0x20;
        private const long KerberosFutureMask = 0x7FFFFFE0;

        /// <summary>The engine area holding a category's settings.</summary>
        internal static uint AreaFor(SecurityPolicyCategory category)
            => category == SecurityPolicyCategory.UserRightsAssignment ? AREA_PRIVILEGES : AREA_SECURITY_POLICY;

        /// <summary>The engine area holding a template section.</summary>
        internal static uint AreaFor(string section)
            => section.Equals(SecurityTemplateSections.PrivilegeRights, StringComparison.OrdinalIgnoreCase) ? AREA_PRIVILEGES : AREA_SECURITY_POLICY;

        #region Write

        /// <summary>
        /// Maps a policy value to its template line. Returns false for a definition the template has no
        /// line for.
        /// </summary>
        internal static bool TryMap(SecurityPolicyValue value, out SecurityTemplateEdit edit)
        {
            edit = default;
            SecurityPolicyDefinition definition = value.Definition;
            if (!TryGetLocation(definition, out string section, out string key))
                return false;

            string formatted;
            if (section == SecurityTemplateSections.PrivilegeRights)
            {
                formatted = FormatPrivilegeHolders(value.AccountList);
            }
            else if (section == SecurityTemplateSections.RegistryValues)
            {
                formatted = FormatRegistryValue(value);
            }
            else if (SystemAccessNameKeys.ContainsKey(definition.Key))
            {
                formatted = Quote(value.StringValue);
            }
            else
            {
                long number = value.NumericValue;
                if (number == 0 && ForeverKeys.Contains(key))
                    number = ForeverValue;
                formatted = Number(number);
            }

            edit = new SecurityTemplateEdit(section, key, formatted);
            return true;
        }

        private static string FormatRegistryValue(SecurityPolicyValue value)
        {
            int type = value.Definition.RegistryValueType;
            if (type is not (REG_SZ or REG_EXPAND_SZ or REG_BINARY or REG_DWORD or REG_MULTI_SZ))
            {
                // No declared type: fall back to the editor shape.
                type = value.Definition.PolicyType switch
                {
                    SecurityPolicyType.String => REG_SZ,
                    SecurityPolicyType.MultiString => REG_MULTI_SZ,
                    _ => REG_DWORD
                };
            }

            return type switch
            {
                REG_SZ or REG_EXPAND_SZ => $"{type},{Quote(IsTextValue(value.Definition) ? value.StringValue : Number(value.NumericValue))}",
                REG_MULTI_SZ => $"{REG_MULTI_SZ},{FormatMultiString(value.StringValue)}",
                REG_BINARY => $"{REG_BINARY},{unchecked((byte)value.NumericValue).ToString("x2", CultureInfo.InvariantCulture)}",
                _ => $"{REG_DWORD},{unchecked((uint)value.NumericValue).ToString(CultureInfo.InvariantCulture)}"
            };
        }

        /// <summary>
        /// Formats the holders of a user right as the engine stores them: each account's string SID with
        /// a <c>*</c> prefix where it resolves, otherwise the name verbatim. An empty list clears the right.
        /// </summary>
        private static string FormatPrivilegeHolders(IReadOnlyList<string> accounts)
        {
            if (accounts is null || accounts.Count == 0)
                return string.Empty;

            var parts = new List<string>(accounts.Count);
            foreach (string account in accounts)
            {
                if (string.IsNullOrWhiteSpace(account))
                    continue;

                string? sid = PolicyNativeHelpers.ResolveAccountSidString(account);
                parts.Add(sid is not null ? $"*{sid}" : Quote(account));
            }

            return string.Join(",", parts);
        }

        private static string FormatMultiString(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(",", lines.Select(Quote));
        }

        #endregion

        #region Read

        /// <summary>
        /// Reads a policy's value from the engine's view of the system. A setting the template does not
        /// carry is not defined, as in secpol.msc: an absent registry value is Not Defined, and a
        /// <c>[System Access]</c> setting the engine leaves out is Not Applicable (the lockout duration,
        /// reset window and administrator lockout while no lockout threshold is set). A user right nobody
        /// holds is defined and empty.
        /// </summary>
        internal static SecurityPolicyValue ReadValue(SecurityPolicyDefinition definition, SecurityTemplate template)
        {
            var value = new SecurityPolicyValue { Definition = definition };
            if (!TryGetLocation(definition, out string section, out string key))
                return value;

            if (section == SecurityTemplateSections.RegistryValues)
            {
                ReadRegistryValue(value);
                return value;
            }

            if (!template.TryGetValue(section, key, out string data))
            {
                value.IsDefined = section == SecurityTemplateSections.PrivilegeRights;
                value.IsNotApplicable = section == SecurityTemplateSections.SystemAccess;
                return value;
            }

            value.IsDefined = true;
            if (section == SecurityTemplateSections.PrivilegeRights)
            {
                value.AccountList = ParsePrivilegeHolders(data);
            }
            else if (SystemAccessNameKeys.ContainsKey(definition.Key))
            {
                value.StringValue = Unquote(data);
            }
            else if (long.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            {
                value.NumericValue = number == ForeverValue && ForeverKeys.Contains(key) ? 0 : number;
            }
            else
            {
                throw InvalidPolicyData(definition);
            }

            return value;
        }

        /// <summary>
        /// Reads registry payloads directly because SCE exports multi-strings without escaping commas
        /// or preserving item boundaries. Parsing that export can silently change the value on save.
        /// </summary>
        private static void ReadRegistryValue(SecurityPolicyValue value)
        {
            SecurityPolicyDefinition definition = value.Definition;
            if (!PolicyNativeHelpers.TryOpenRegistryKey(definition.RegistryKeyPath, KEY_READ, out var key, out int error))
            {
                if (error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND)
                    return;
                throw new Win32Exception(error);
            }

            using var keyScope = key;
            if (!PolicyNativeHelpers.TryReadRegistryValue(key.Handle, definition.RegistryValueName, out var buffer, out error))
            {
                if (error == ERROR_FILE_NOT_FOUND)
                    return;
                throw new Win32Exception(error);
            }

            using var valueBuffer = buffer;
            switch (buffer.RegistryType)
            {
                case REG_DWORD:
                    if (!PolicyNativeHelpers.TryReadRegistryDword(buffer, out uint dword))
                        throw InvalidPolicyData(definition);
                    value.NumericValue = dword;
                    break;

                case REG_SZ:
                case REG_EXPAND_SZ:
                    value.StringValue = PolicyNativeHelpers.ReadRegistryString(buffer);
                    if (!IsTextValue(definition))
                    {
                        if (!long.TryParse(value.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                            throw InvalidPolicyData(definition);
                        value.NumericValue = parsed;
                    }
                    break;

                case REG_MULTI_SZ:
                    value.StringValue = PolicyNativeHelpers.ReadMultiString(buffer.Pointer, buffer.Size);
                    break;

                case REG_BINARY:
                    if (buffer.Size == 0)
                        throw InvalidPolicyData(definition);
                    value.NumericValue = Marshal.ReadByte(buffer.Pointer);
                    break;

                default:
                    throw InvalidPolicyData(definition);
            }

            value.IsDefined = true;
            if (definition.RegistryValueName.Equals(SupportedEncryptionTypesValueName, StringComparison.OrdinalIgnoreCase))
                value.NumericValue = NormalizeKerberosEncryptionTypes(value.NumericValue);
        }

        /// <summary>
        /// Resolves the holders of a user right to account names. The engine lists an account it could
        /// not name as <c>*SID</c>, and one it could by name.
        /// </summary>
        private static List<string> ParsePrivilegeHolders(string data)
        {
            var accounts = new List<string>();
            foreach (string entry in data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                accounts.Add(entry.StartsWith('*') ? PolicyNativeHelpers.LookupSidString(entry[1..]) : entry);
            }

            return accounts;
        }

        private static long NormalizeKerberosEncryptionTypes(long rawValue)
        {
            // Compatibility repair for earlier builds that incorrectly encoded
            // "Future encryption types" as 0x20 instead of the canonical mask.
            // If only low bits are present and bit 0x20 is set, expand it.
            if ((rawValue & KerberosLegacyFutureBit) != 0 && (rawValue & ~0x3F) == 0)
            {
                long lowFlags = rawValue & KerberosLowFlagsMask;
                return lowFlags | KerberosFutureMask;
            }

            return rawValue;
        }

        #endregion

        /// <summary>Finds the template section and key that carry a policy.</summary>
        private static bool TryGetLocation(SecurityPolicyDefinition definition, out string section, out string key)
        {
            section = string.Empty;
            key = string.Empty;
            string? mapped;

            switch (definition.Category)
            {
                case SecurityPolicyCategory.UserRightsAssignment:
                    section = SecurityTemplateSections.PrivilegeRights;
                    key = definition.PrivilegeConstant;
                    return key.Length > 0;

                case SecurityPolicyCategory.AuditPolicy:
                    section = SecurityTemplateSections.EventAudit;
                    if (!EventAuditKeys.TryGetValue(definition.Key, out mapped))
                        return false;
                    key = mapped;
                    return true;

                case SecurityPolicyCategory.PasswordPolicy:
                case SecurityPolicyCategory.AccountLockoutPolicy:
                case SecurityPolicyCategory.SecurityOptions:
                    // Password, lockout and the account options are [System Access] settings even when one
                    // has a registry location (LSAAnonymousNameLookup), because the engine applies them
                    // through the SAM and LSA, not as registry values.
                    if (SystemAccessNameKeys.TryGetValue(definition.Key, out mapped)
                        || SystemAccessKeys.TryGetValue(definition.Key, out mapped))
                    {
                        section = SecurityTemplateSections.SystemAccess;
                        key = mapped;
                        return true;
                    }

                    // Everything else is a registry value.
                    if (definition.RegistryKeyPath.Length > 0 && definition.RegistryValueName.Length > 0)
                    {
                        section = SecurityTemplateSections.RegistryValues;
                        key = $@"MACHINE\{definition.RegistryKeyPath}\{definition.RegistryValueName}";
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a policy's value lives in <see cref="SecurityPolicyValue.StringValue"/>. A numeric,
        /// Boolean or choice policy edits <see cref="SecurityPolicyValue.NumericValue"/> even when Windows
        /// stores it as REG_SZ (as for <c>CachedLogonsCount</c>), so only text policies, and a choice whose
        /// options are strings, are carried as text.
        /// </summary>
        private static bool IsTextValue(SecurityPolicyDefinition definition)
        {
            if (definition.PolicyType is SecurityPolicyType.String or SecurityPolicyType.MultiString)
                return true;

            if (definition.PolicyType == SecurityPolicyType.Dropdown)
            {
                foreach (PolicyDropdownOption option in definition.DropdownOptions)
                {
                    if (option.Value is string)
                        return true;
                }
            }

            return false;
        }

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static InvalidDataException InvalidPolicyData(SecurityPolicyDefinition definition)
            => new(string.Format(CultureInfo.CurrentCulture,
                LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, SecPolKeys.ErrorInvalidPolicyValue),
                definition.DisplayName));

        /// <summary>Quotes an INF string, escaping embedded quotes so the engine preserves them.</summary>
        private static string Quote(string? value)
        {
            if (value is not null && value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new ArgumentException(LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, SecPolKeys.ErrorTemplateStringCharacters));

            return $"\"{value?.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        private static string Unquote(string value)
            => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }
}
