using System;
using System.Collections.Generic;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.UserSecurity.Models.SecPol
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

        /// <summary>
        /// Read from the Security Configuration Engine's registry-value catalog under
        /// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SeCEdit\Reg Values,
        /// which is the source secpol.msc itself uses.
        /// </summary>
        SceRegValues,

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
        /// Item string ID in wsecedit.dll for the policies that are not registry values
        /// (password, lockout and audit policy, and the account options). The snap-in
        /// stores the display name on the first line and the numeric unit on the second,
        /// and both are loaded from there at runtime. Zero when the policy's name comes
        /// from elsewhere, such as the registry catalog or a privilege constant.
        /// </summary>
        public int ItemId { get; set; }

        /// <summary>
        /// The indirect string reference the system published for this policy's display
        /// name, such as <c>@wsecedit.dll,-59001</c>. Carried through from the registry
        /// catalog so the same name can be resolved in another language when pairing the
        /// policy with its explain text.
        /// </summary>
        public string DisplayNameReference { get; set; } = string.Empty;

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
            // The unit text comes from the Windows policy metadata, already localized.
            string unit = Definition.Unit;
            if (!string.IsNullOrEmpty(unit))
                return $"{NumericValue} {unit}";
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

            return NumericValue.ToString();
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


