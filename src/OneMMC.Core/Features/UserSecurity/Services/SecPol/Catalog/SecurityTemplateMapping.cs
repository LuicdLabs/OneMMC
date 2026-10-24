using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>One line of a security template: a key in a section, with a value or a request to remove it.</summary>
    internal readonly struct SecurityTemplateEdit
    {
        internal SecurityTemplateEdit(string section, string key, string? value)
        {
            Section = section;
            Key = key;
            Value = value;
        }

        internal string Section { get; }

        internal string Key { get; }

        /// <summary>The value to set, or null to remove the key (make the setting Not Defined).</summary>
        internal string? Value { get; }

        internal bool IsRemoval => Value is null;
    }

    /// <summary>
    /// Expresses a single Account/Local policy value as the security template line the Security
    /// Configuration Engine reads for it: a key in <c>[System Access]</c>, <c>[Event Audit]</c>,
    /// <c>[Privilege Rights]</c> or <c>[Registry Values]</c>. Key names and value encodings match the
    /// engine's own export (<c>secedit /export</c>) line for line.
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
        /// Maps a policy value to its template line. Returns false for a value the template cannot carry,
        /// in which case the caller writes it directly instead.
        /// </summary>
        internal static bool TryMap(SecurityPolicyValue value, out SecurityTemplateEdit edit)
        {
            edit = default;
            SecurityPolicyDefinition definition = value.Definition;

            switch (definition.Category)
            {
                case SecurityPolicyCategory.UserRightsAssignment:
                    edit = new SecurityTemplateEdit(
                        SecurityTemplateSections.PrivilegeRights,
                        definition.PrivilegeConstant,
                        FormatPrivilegeHolders(value.AccountList));
                    return true;

                case SecurityPolicyCategory.AuditPolicy:
                    if (!EventAuditKeys.TryGetValue(definition.Key, out string? auditKey))
                        return false;
                    edit = Scalar(SecurityTemplateSections.EventAudit, auditKey, value, Number(value.NumericValue));
                    return true;

                case SecurityPolicyCategory.PasswordPolicy:
                case SecurityPolicyCategory.AccountLockoutPolicy:
                case SecurityPolicyCategory.SecurityOptions:
                    return TryMapSystemAccessOrRegistry(value, out edit);

                default:
                    return false;
            }
        }

        private static bool TryMapSystemAccessOrRegistry(SecurityPolicyValue value, out SecurityTemplateEdit edit)
        {
            edit = default;
            SecurityPolicyDefinition definition = value.Definition;

            // Account options whose value is a quoted name.
            if (SystemAccessNameKeys.TryGetValue(definition.Key, out string? nameKey))
            {
                edit = Scalar(SecurityTemplateSections.SystemAccess, nameKey, value, Quote(value.StringValue));
                return true;
            }

            // Password, lockout and the on/off account options. These are [System Access] settings even
            // when the application reads one back from a registry value (LSAAnonymousNameLookup), because
            // the engine applies them through the SAM and LSA, not as registry values.
            if (SystemAccessKeys.TryGetValue(definition.Key, out string? systemAccessKey))
            {
                edit = Scalar(SecurityTemplateSections.SystemAccess, systemAccessKey, value, Number(value.NumericValue));
                return true;
            }

            // Everything else in Security Options is a registry value.
            if (!string.IsNullOrEmpty(definition.RegistryKeyPath) && !string.IsNullOrEmpty(definition.RegistryValueName))
            {
                string key = $@"MACHINE\{definition.RegistryKeyPath}\{definition.RegistryValueName}";
                string formatted = FormatRegistryValue(value);
                edit = Scalar(SecurityTemplateSections.RegistryValues, key, value, formatted);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds the edit, substituting removal when the policy is being returned to Not Defined.
        /// </summary>
        private static SecurityTemplateEdit Scalar(string section, string key, SecurityPolicyValue value, string formatted)
        {
            bool remove = !value.IsDefined && value.Definition.AllowNotDefined;
            return new SecurityTemplateEdit(section, key, remove ? null : formatted);
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
                REG_SZ or REG_EXPAND_SZ => $"{REG_SZ},{Quote(IsTextValue(value) ? value.StringValue : Number(value.NumericValue))}",
                REG_MULTI_SZ => $"{REG_MULTI_SZ},{FormatMultiString(value.StringValue)}",
                REG_BINARY => $"{REG_BINARY},{FormatBinary(value.NumericValue)}",
                _ => $"{REG_DWORD},{unchecked((uint)value.NumericValue).ToString(CultureInfo.InvariantCulture)}"
            };
        }

        /// <summary>
        /// Whether a policy's value lives in <see cref="SecurityPolicyValue.StringValue"/>. A numeric,
        /// Boolean or choice policy edits <see cref="SecurityPolicyValue.NumericValue"/> even when Windows
        /// stores it as REG_SZ (as for <c>CachedLogonsCount</c>), so only text policies, and a choice whose
        /// options are strings, are written from the string.
        /// </summary>
        private static bool IsTextValue(SecurityPolicyValue value)
        {
            SecurityPolicyDefinition definition = value.Definition;
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
                parts.Add(sid is not null ? $"*{sid}" : account);
            }

            return string.Join(",", parts);
        }

        private static string FormatMultiString(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(",", lines);
        }

        private static string FormatBinary(long value) => unchecked((byte)value).ToString(CultureInfo.InvariantCulture);

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Wraps a string in the double quotes the template uses for REG_SZ data and account names.</summary>
        private static string Quote(string? value)
        {
            var builder = new StringBuilder();
            builder.Append('"');
            builder.Append(value ?? string.Empty);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
