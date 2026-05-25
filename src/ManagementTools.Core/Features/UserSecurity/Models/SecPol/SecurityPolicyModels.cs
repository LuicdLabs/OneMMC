using System;
using System.Collections.Generic;
using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.UserSecurity.Models.SecPol
{
    /// <summary>
    /// Represents the type of a security policy setting.
    /// </summary>
    public enum SecurityPolicyType
    {
        /// <summary>Numeric value (e.g., password length, lockout threshold).</summary>
        Numeric,

        /// <summary>Boolean (Enabled/Disabled).</summary>
        Boolean,

        /// <summary>String value (e.g., account rename).</summary>
        String,

        /// <summary>Audit setting (Success, Failure, Both, None).</summary>
        Audit,

        /// <summary>User rights assignment (list of account SIDs/names).</summary>
        UserRightsAssignment,

        /// <summary>Dropdown/enum selection from a predefined list.</summary>
        Dropdown,

        /// <summary>Bitmask flags selection using multiple checkboxes.</summary>
        BitmaskFlags,

        /// <summary>Multi-string value (e.g., named pipes, registry paths).</summary>
        MultiString
    }

    /// <summary>
    /// Represents the category a security policy belongs to.
    /// </summary>
    public enum SecurityPolicyCategory
    {
        PasswordPolicy,
        AccountLockoutPolicy,
        KerberosPolicy,
        AuditPolicy,
        UserRightsAssignment,
        SecurityOptions
    }

    /// <summary>
    /// Audit policy flags for Success/Failure auditing.
    /// </summary>
    [Flags]
    public enum AuditPolicyFlags
    {
        None = 0,
        Success = 1,
        Failure = 2,
        SuccessAndFailure = Success | Failure
    }

    /// <summary>
    /// Indicates where a policy definition was sourced from.
    /// </summary>
    public enum PolicyDataSource
    {
        /// <summary>Hard-coded in application code (fallback).</summary>
        Hardcoded,

        /// <summary>Parsed dynamically from %SystemRoot%\inf\sceregvl.inf.</summary>
        SceRegVl,

        /// <summary>Discovered at runtime via system API enumeration.</summary>
        Dynamic
    }

    /// <summary>
    /// Represents a dropdown option for a security policy setting.
    /// </summary>
    public sealed class PolicyDropdownOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public object Value { get; set; } = 0;
    }

    /// <summary>
    /// Metadata definition for a security policy setting.
    /// Describes how to read and write the policy, its type, valid ranges, etc.
    /// </summary>
    public sealed class SecurityPolicyDefinition
    {
        /// <summary>Unique key identifying this policy (e.g., "MinimumPasswordLength").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Display name for the policy.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Description or explanation of the policy.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Category this policy belongs to.</summary>
        public SecurityPolicyCategory Category { get; set; }

        /// <summary>Type of the policy value.</summary>
        public SecurityPolicyType PolicyType { get; set; }

        /// <summary>Minimum value for numeric policies.</summary>
        public long MinValue { get; set; }

        /// <summary>Maximum value for numeric policies.</summary>
        public long MaxValue { get; set; } = long.MaxValue;

        /// <summary>Unit suffix for display (e.g., "days", "minutes", "characters").</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>Available options for Dropdown policies.</summary>
        public List<PolicyDropdownOption> DropdownOptions { get; set; } = new();

        /// <summary>
        /// The registry key path (for SecurityOptions policies stored in registry).
        /// </summary>
        public string RegistryKeyPath { get; set; } = string.Empty;

        /// <summary>
        /// The registry value name (for SecurityOptions policies stored in registry).
        /// </summary>
        public string RegistryValueName { get; set; } = string.Empty;

        /// <summary>
        /// The privilege constant name (for UserRightsAssignment policies, e.g., "SeBackupPrivilege").
        /// </summary>
        public string PrivilegeConstant { get; set; } = string.Empty;

        /// <summary>
        /// The audit category GUID (for AuditPolicy policies).
        /// </summary>
        public Guid AuditCategoryGuid { get; set; } = Guid.Empty;

        /// <summary>
        /// The audit subcategory index within the category (for basic audit policy mapping).
        /// </summary>
        public int AuditEventIndex { get; set; } = -1;

        /// <summary>Whether undefined/not-configured is a valid state for this policy.</summary>
        public bool AllowNotDefined { get; set; }

        /// <summary>
        /// Resource ID in wsecedit.dll for the full Explain text (localized).
        /// If set to a positive value, the explain text will be loaded from the DLL at runtime.
        /// </summary>
        public int ExplainResourceId { get; set; }

        /// <summary>
        /// Resource ID in wsecedit.dll for the localized display name.
        /// If set, the display name will be loaded from the DLL at runtime,
        /// falling back to <see cref="DisplayName"/> if the resource is unavailable.
        /// </summary>
        public int DisplayNameResourceId { get; set; }

        /// <summary>
        /// Indicates where this definition was sourced from.
        /// Definitions from <see cref="PolicyDataSource.SceRegVl"/> track OS-updated policies;
        /// <see cref="PolicyDataSource.Hardcoded"/> definitions serve as fallbacks.
        /// </summary>
        public PolicyDataSource DataSource { get; set; } = PolicyDataSource.Hardcoded;
    }

    /// <summary>
    /// Represents a security policy's current value read from the system.
    /// </summary>
    public sealed class SecurityPolicyValue
    {
        /// <summary>The definition this value is associated with.</summary>
        public SecurityPolicyDefinition Definition { get; set; } = new();

        /// <summary>The current numeric value (for Numeric, Boolean, Audit, Dropdown).</summary>
        public long NumericValue { get; set; }

        /// <summary>The current string value (for String/MultiString policies).</summary>
        public string StringValue { get; set; } = string.Empty;

        /// <summary>The current list of accounts (for UserRightsAssignment).</summary>
        public List<string> AccountList { get; set; } = new();

        /// <summary>Whether the value is defined/configured on the system.</summary>
        public bool IsDefined { get; set; }

        /// <summary>Gets a formatted display string for the current value.</summary>
        public string DisplaySetting
        {
            get
            {
                if (!IsDefined)
                    return Localized(SecPolKeys.ValueNotDefined, "Not Defined");

                return Definition.PolicyType switch
                {
                    SecurityPolicyType.Numeric => FormatNumericValue(),
                    SecurityPolicyType.Boolean => NumericValue != 0
                        ? Localized(SecPolKeys.ValueEnabled, "Enabled")
                        : Localized(SecPolKeys.ValueDisabled, "Disabled"),
                    SecurityPolicyType.String => string.IsNullOrEmpty(StringValue)
                        ? Localized(SecPolKeys.ValueEmpty, "(empty)")
                        : StringValue,
                    SecurityPolicyType.Audit => FormatAuditValue(),
                    SecurityPolicyType.UserRightsAssignment => AccountList.Count > 0
                        ? string.Join(", ", AccountList)
                        : Localized(SecPolKeys.ValueEmpty, "(empty)"),
                    SecurityPolicyType.Dropdown => FormatDropdownValue(),
                    SecurityPolicyType.BitmaskFlags => FormatBitmaskFlagsValue(),
                    SecurityPolicyType.MultiString => string.IsNullOrEmpty(StringValue)
                        ? Localized(SecPolKeys.ValueEmpty, "(empty)")
                        : StringValue,
                    _ => StringValue
                };
            }
        }

        private string FormatNumericValue()
        {
            string unit = Definition.Unit;
            if (!string.IsNullOrEmpty(unit))
                return $"{NumericValue} {LocalizeUnit(unit)}";
            return NumericValue.ToString();
        }

        private string FormatAuditValue()
        {
            var flags = (AuditPolicyFlags)NumericValue;
            if (flags == AuditPolicyFlags.None)
                return Localized(SecPolKeys.AuditNoAuditing, "No auditing");
            var parts = new List<string>();
            if (flags.HasFlag(AuditPolicyFlags.Success))
                parts.Add(Localized(SecPolKeys.AuditSuccess, "Success"));
            if (flags.HasFlag(AuditPolicyFlags.Failure))
                parts.Add(Localized(SecPolKeys.AuditFailure, "Failure"));
            return string.Join(", ", parts);
        }

        private string FormatDropdownValue()
        {
            foreach (var option in Definition.DropdownOptions)
            {
                if (option.Value is long lv && lv == NumericValue)
                    return option.DisplayName;
                if (option.Value is int iv && iv == NumericValue)
                    return option.DisplayName;
            }

            if (TryFormatKnownDropdownFallback(out string knownDisplay))
                return knownDisplay;

            return NumericValue.ToString();
        }

        private bool TryFormatKnownDropdownFallback(out string display)
        {
            display = string.Empty;

            switch (Definition.Key)
            {
                case "ScRemoveOption":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownScRemoveOptionNone, "No Action"),
                        1 => Localized(SecPolKeys.DropdownScRemoveOptionLock, "Lock Workstation"),
                        2 => Localized(SecPolKeys.DropdownScRemoveOptionLogoff, "Force Logoff"),
                        3 => Localized(SecPolKeys.DropdownScRemoveOptionDisconnect, "Disconnect if a Remote Desktop Services session"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "ConsentPromptBehaviorAdmin":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownConsentAdmin0, "Elevate without prompting"),
                        1 => Localized(SecPolKeys.DropdownConsentAdmin1, "Prompt for credentials on the secure desktop"),
                        2 => Localized(SecPolKeys.DropdownConsentAdmin2, "Prompt for consent on the secure desktop"),
                        3 => Localized(SecPolKeys.DropdownConsentAdmin3, "Prompt for credentials"),
                        4 => Localized(SecPolKeys.DropdownConsentAdmin4, "Prompt for consent"),
                        5 => Localized(SecPolKeys.DropdownConsentAdmin5, "Prompt for consent for non-Windows binaries"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "ConsentPromptBehaviorUser":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownConsentUser0, "Automatically deny elevation requests"),
                        1 => Localized(SecPolKeys.DropdownConsentUser1, "Prompt for credentials on the secure desktop"),
                        3 => Localized(SecPolKeys.DropdownConsentUser3, "Prompt for credentials"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "ForceGuest":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownForceGuest0, "Classic - Local users authenticate as themselves"),
                        1 => Localized(SecPolKeys.DropdownForceGuest1, "Guest only - Local users authenticate as Guest"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "LDAPClientIntegrity":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownLdapClientIntegrity0, "None"),
                        1 => Localized(SecPolKeys.DropdownLdapClientIntegrity1, "Negotiate signing"),
                        2 => Localized(SecPolKeys.DropdownLdapClientIntegrity2, "Require signing"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "LmCompatibilityLevel":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownLmCompatibilityLevel0, "Send LM & NTLM responses"),
                        1 => Localized(SecPolKeys.DropdownLmCompatibilityLevel1, "Send LM & NTLM - use NTLMv2 session security if negotiated"),
                        2 => Localized(SecPolKeys.DropdownLmCompatibilityLevel2, "Send NTLM response only"),
                        3 => Localized(SecPolKeys.DropdownLmCompatibilityLevel3, "Send NTLMv2 response only"),
                        4 => Localized(SecPolKeys.DropdownLmCompatibilityLevel4, "Send NTLMv2 response only. Refuse LM"),
                        5 => Localized(SecPolKeys.DropdownLmCompatibilityLevel5, "Send NTLMv2 response only. Refuse LM & NTLM"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "AllocateDASD":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownAllocateDASD0, "Administrators"),
                        1 => Localized(SecPolKeys.DropdownAllocateDASD1, "Administrators and Power Users"),
                        2 => Localized(SecPolKeys.DropdownAllocateDASD2, "Administrators and Interactive Users"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "TypeOfAdminApprovalMode":
                    display = NumericValue switch
                    {
                        1 => Localized(SecPolKeys.DropdownTypeOfAdminApprovalMode1, "Admin Approval Mode"),
                        2 => Localized(SecPolKeys.DropdownTypeOfAdminApprovalMode2, "Admin Approval Mode with enhanced privilege protection"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "BlockMicrosoftAccounts":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownBlockMicrosoftAccounts0, "This policy is disabled"),
                        1 => Localized(SecPolKeys.DropdownBlockMicrosoftAccounts1, "Users can't add Microsoft accounts"),
                        3 => Localized(SecPolKeys.DropdownBlockMicrosoftAccounts3, "Users can't add or log on with Microsoft accounts"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "DontDisplayLockedUserId":
                    display = NumericValue switch
                    {
                        1 => Localized(SecPolKeys.DropdownDontDisplayLockedUserId1, "User display name, domain and user names"),
                        2 => Localized(SecPolKeys.DropdownDontDisplayLockedUserId2, "User display name only"),
                        3 => Localized(SecPolKeys.DropdownDontDisplayLockedUserId3, "Do not display user information"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "LDAPClientConfidentiality":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownLdapClientConfidentiality0, "None"),
                        1 => Localized(SecPolKeys.DropdownLdapClientConfidentiality1, "Negotiate encryption"),
                        2 => Localized(SecPolKeys.DropdownLdapClientConfidentiality2, "Require encryption"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "ConsentPromptBehaviorAdminAP":
                    display = NumericValue switch
                    {
                        1 => Localized(SecPolKeys.DropdownConsentAdminAP1, "Prompt for credentials on the secure desktop"),
                        2 => Localized(SecPolKeys.DropdownConsentAdminAP2, "Prompt for consent on the secure desktop"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "ForceKeyProtection":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownForceKeyProtection0, "User input is not required when new keys are stored and used"),
                        1 => Localized(SecPolKeys.DropdownForceKeyProtection1, "User is prompted when the key is first used"),
                        2 => Localized(SecPolKeys.DropdownForceKeyProtection2, "User must enter a password each time they use a key"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "LDAPServerIntegrity":
                    display = NumericValue switch
                    {
                        1 => Localized(SecPolKeys.DropdownLdapServerIntegrity1, "None"),
                        2 => Localized(SecPolKeys.DropdownLdapServerIntegrity2, "Require signing"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "LDAPServerIntegrityEnforced":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownLdapServerIntegrityEnforced0, "Disabled"),
                        1 => Localized(SecPolKeys.DropdownLdapServerIntegrityEnforced1, "Enabled (compatibility mode)"),
                        2 => Localized(SecPolKeys.DropdownLdapServerIntegrityEnforced2, "Enabled"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "LDAPEnforceChannelBinding":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownLdapEnforceChannelBinding0, "Never"),
                        1 => Localized(SecPolKeys.DropdownLdapEnforceChannelBinding1, "When supported"),
                        2 => Localized(SecPolKeys.DropdownLdapEnforceChannelBinding2, "Always"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "SPNTargetNameValidationLevel":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownSPNTargetNameValidation0, "Off"),
                        1 => Localized(SecPolKeys.DropdownSPNTargetNameValidation1, "Accept if provided by client"),
                        2 => Localized(SecPolKeys.DropdownSPNTargetNameValidation2, "Required from client"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "S4U2SelfFlags":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownS4U2SelfFlags0, "Default"),
                        1 => Localized(SecPolKeys.DropdownS4U2SelfFlags1, "Enabled"),
                        2 => Localized(SecPolKeys.DropdownS4U2SelfFlags2, "Disabled"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "RestrictSendingNTLMTraffic":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownRestrictSendingNTLM0, "Allow all"),
                        1 => Localized(SecPolKeys.DropdownRestrictSendingNTLM1, "Deny all domain accounts"),
                        2 => Localized(SecPolKeys.DropdownRestrictSendingNTLM2, "Deny all accounts"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "RestrictNTLMInDomain":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownRestrictNTLMInDomain0, "Disable"),
                        1 => Localized(SecPolKeys.DropdownRestrictNTLMInDomain1, "Deny for domain accounts to domain servers"),
                        3 => Localized(SecPolKeys.DropdownRestrictNTLMInDomain3, "Deny for domain accounts"),
                        5 => Localized(SecPolKeys.DropdownRestrictNTLMInDomain5, "Deny for domain servers"),
                        7 => Localized(SecPolKeys.DropdownRestrictNTLMInDomain7, "Deny all"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "RestrictReceivingNTLMTraffic":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownRestrictReceivingNTLM0, "Allow all"),
                        1 => Localized(SecPolKeys.DropdownRestrictReceivingNTLM1, "Deny all domain accounts"),
                        2 => Localized(SecPolKeys.DropdownRestrictReceivingNTLM2, "Deny all accounts"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "AuditNTLMInDomain":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownAuditNTLMInDomain0, "Disable"),
                        1 => Localized(SecPolKeys.DropdownAuditNTLMInDomain1, "Enable for domain accounts to domain servers"),
                        3 => Localized(SecPolKeys.DropdownAuditNTLMInDomain3, "Enable for domain accounts"),
                        5 => Localized(SecPolKeys.DropdownAuditNTLMInDomain5, "Enable for domain servers"),
                        7 => Localized(SecPolKeys.DropdownAuditNTLMInDomain7, "Enable all"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                case "AuditReceivingNTLMTraffic":
                    display = NumericValue switch
                    {
                        0 => Localized(SecPolKeys.DropdownAuditReceivingNTLM0, "Disable"),
                        1 => Localized(SecPolKeys.DropdownAuditReceivingNTLM1, "Enable for domain accounts"),
                        2 => Localized(SecPolKeys.DropdownAuditReceivingNTLM2, "Enable for all accounts"),
                        _ => string.Empty
                    };
                    return !string.IsNullOrEmpty(display);

                default:
                    return false;
            }
        }

        private static string LocalizeUnit(string unit)
        {
            if (unit.Equals("days", StringComparison.OrdinalIgnoreCase))
                return Localized(SecPolKeys.UnitDays, "days");

            if (unit.Equals("minutes", StringComparison.OrdinalIgnoreCase))
                return Localized(SecPolKeys.UnitMinutes, "minutes");

            if (unit.Equals("seconds", StringComparison.OrdinalIgnoreCase))
                return Localized(SecPolKeys.UnitSeconds, "seconds");

            if (unit.Equals("logons", StringComparison.OrdinalIgnoreCase))
                return Localized(SecPolKeys.UnitLogons, "logons");

            if (unit.Equals("characters", StringComparison.OrdinalIgnoreCase))
                return Localized(SecPolKeys.UnitCharacters, "characters");

            if (unit.Equals("passwords remembered", StringComparison.OrdinalIgnoreCase))
                return Localized(SecPolKeys.UnitPasswordsRemembered, "passwords remembered");

            if (unit.Equals("invalid logon attempts", StringComparison.OrdinalIgnoreCase))
                return Localized(SecPolKeys.UnitInvalidLogonAttempts, "invalid logon attempts");

            return unit;
        }

        private static string Localized(string key, string fallback)
        {
            string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                return fallback;

            return value;
        }

        private string FormatBitmaskFlagsValue()
        {
            if (Definition.DropdownOptions.Count == 0)
                return NumericValue.ToString();

            // Value 0 means no flags are selected â€” display as "No minimum"
            if (NumericValue == 0)
            {
                return Localized(SecPolKeys.BitmaskNoMinimum, "No minimum");
            }

            var selected = new List<string>();
            long remaining = NumericValue;

            // Compute the "known" bitmask (OR of all defined flag values)
            // to support catch-all flags like "Future encryption types".
            long knownMask = 0;
            foreach (var option in Definition.DropdownOptions)
            {
                long flagValue;
                if (option.Value is long lv) flagValue = lv;
                else if (option.Value is int iv) flagValue = iv;
                else if (!long.TryParse(option.Value?.ToString(), out flagValue)) continue;
                knownMask |= flagValue;
            }

            foreach (var option in Definition.DropdownOptions)
            {
                long flagValue;
                if (option.Value is long lv)
                    flagValue = lv;
                else if (option.Value is int iv)
                    flagValue = iv;
                else if (!long.TryParse(option.Value?.ToString(), out flagValue))
                    continue;

                if (flagValue != 0 && (NumericValue & flagValue) == flagValue)
                {
                    selected.Add(option.DisplayName);
                    remaining &= ~flagValue;

                    // If this flag is a catch-all (i.e., its value is beyond
                    // all other defined flags), absorb all remaining non-known bits.
                    // This handles the case where secpol.msc sets additional
                    // higher bits for "Future encryption types".
                    long otherFlagsMask = knownMask & ~flagValue;
                    if (flagValue > 0 && (flagValue & otherFlagsMask) == 0 && flagValue > otherFlagsMask)
                    {
                        remaining &= otherFlagsMask;
                    }
                }
            }

            if (selected.Count == 0)
                return NumericValue.ToString();

            if (remaining != 0)
                selected.Add($"0x{remaining:X}");

            return string.Join(", ", selected);
        }
    }
}


