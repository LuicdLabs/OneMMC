using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// DTO for deserializing Security Options policy definitions from
    /// the embedded <c>SecurityOptionsDefinitions.json</c> resource.
    /// <para>
    /// Each entry represents either a registry-based policy (with
    /// <see cref="RegistryKeyPath"/>/<see cref="RegistryValueName"/>)
    /// or a special non-registry policy (with <see cref="SpecialHandler"/>).
    /// </para>
    /// <para>
    /// Display names are never stored here â€” they are loaded at runtime
    /// from <c>wsecedit.dll</c> via <see cref="SecurityPolicyResourceLoader"/>
    /// using <see cref="ExplainResourceId"/>.
    /// </para>
    /// </summary>
    internal sealed class PolicyDefinitionDto
    {
        /// <summary>
        /// Stable unique key identifying this policy (e.g., "LimitBlankPasswordUse").
        /// </summary>
        [JsonPropertyName("Key")]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Policy type as a string: "Boolean", "Numeric", "String", "Dropdown",
        /// "BitmaskFlags", "MultiString".
        /// </summary>
        [JsonPropertyName("PolicyType")]
        public string PolicyType { get; set; } = string.Empty;

        /// <summary>
        /// Registry key path under HKLM. Null/empty for special (non-registry) policies.
        /// </summary>
        [JsonPropertyName("RegistryKeyPath")]
        public string? RegistryKeyPath { get; set; }

        /// <summary>
        /// Registry value name. Null/empty for special (non-registry) policies.
        /// </summary>
        [JsonPropertyName("RegistryValueName")]
        public string? RegistryValueName { get; set; }

        /// <summary>
        /// Resource ID in <c>wsecedit.dll</c> containing both display name and
        /// explain text in the format <c>"DisplayName\r\n\r\nExplainText"</c>.
        /// </summary>
        [JsonPropertyName("ExplainResourceId")]
        public int ExplainResourceId { get; set; }

        /// <summary>
        /// If set, identifies the <see cref="ISpecialPolicyHandler"/> implementation
        /// to use for reading/writing this policy (e.g., "AdminAccountStatus").
        /// Null/empty for registry-based policies.
        /// </summary>
        [JsonPropertyName("SpecialHandler")]
        public string? SpecialHandler { get; set; }

        /// <summary>
        /// Minimum valid numeric value (default: 0).
        /// </summary>
        [JsonPropertyName("MinValue")]
        public long MinValue { get; set; }

        /// <summary>
        /// Maximum valid numeric value.
        /// A value of 0 indicates "use <c>long.MaxValue</c>" as the default.
        /// </summary>
        [JsonPropertyName("MaxValue")]
        public long MaxValue { get; set; }

        /// <summary>
        /// Unit suffix for display (e.g., "days", "minutes", "logons").
        /// </summary>
        [JsonPropertyName("Unit")]
        public string? Unit { get; set; }

        /// <summary>
        /// Whether "Not Defined" is a valid state for this policy.
        /// </summary>
        [JsonPropertyName("AllowNotDefined")]
        public bool AllowNotDefined { get; set; }

        /// <summary>
        /// Dropdown or bitmask flag options (fallback; primary source is <c>sceregvl.inf</c>).
        /// </summary>
        [JsonPropertyName("DropdownOptions")]
        public List<PolicyDropdownOptionDto>? DropdownOptions { get; set; }

        /// <summary>
        /// Returns the full registry path in the form <c>"RegistryKeyPath\RegistryValueName"</c>,
        /// used as a merge key against <c>sceregvl.inf</c> entries.
        /// </summary>
        internal string? GetRegistryFullPath()
        {
            if (string.IsNullOrEmpty(RegistryKeyPath) || string.IsNullOrEmpty(RegistryValueName))
                return null;
            return $"{RegistryKeyPath}\\{RegistryValueName}";
        }

        /// <summary>
        /// Whether this entry represents a special (non-registry) policy.
        /// </summary>
        internal bool IsSpecial => !string.IsNullOrEmpty(SpecialHandler);
    }

    /// <summary>
    /// DTO for dropdown/bitmask option entries in JSON.
    /// Supports both resource-IDâ€“based and fallback display-nameâ€“based localization.
    /// </summary>
    internal sealed class PolicyDropdownOptionDto
    {
        /// <summary>The numeric value for this option.</summary>
        [JsonPropertyName("Value")]
        public long Value { get; set; }

        /// <summary>
        /// Fallback display name (used when <see cref="DisplayResourceId"/> is
        /// unavailable or the resource cannot be loaded).
        /// </summary>
        [JsonPropertyName("DisplayName")]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Resource ID in <c>wsecedit.dll</c> for the localized display name.
        /// If positive, takes precedence over <see cref="DisplayName"/>.
        /// </summary>
        [JsonPropertyName("DisplayResourceId")]
        public int DisplayResourceId { get; set; }
    }
}


