using System;
using System.Collections.Generic;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.UserSecurity.Models.SecPol.SystemAudit
{
    /// <summary>
    /// Identifies the type of audit category shown in the System Audit page.
    /// </summary>
    public enum SystemAuditCategoryKind
    {
        Standard,
        GlobalObjectAccessAuditing
    }

    /// <summary>
    /// Identifies the type of audit item shown in the System Audit page.
    /// </summary>
    public enum SystemAuditItemKind
    {
        AuditSubcategory,
        GlobalObjectAccessPolicy
    }

    /// <summary>
    /// Identifies the resource manager targeted by a Global Object Access Auditing policy.
    /// </summary>
    public enum SystemAuditResourceType
    {
        None,
        FileSystem,
        Registry
    }

    /// <summary>
    /// Represents an audit category shown in the System Audit page.
    /// </summary>
    public sealed class AuditCategoryItem
    {
        /// <summary>
        /// Gets the localized display name of the category.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the category GUID returned by the audit policy API.
        /// </summary>
        public Guid CategoryGuid { get; }

        /// <summary>
        /// Gets the category kind.
        /// </summary>
        public SystemAuditCategoryKind Kind { get; }

        /// <summary>
        /// Gets whether this category represents Global Object Access Auditing.
        /// </summary>
        public bool IsGlobalObjectAccessAuditing => Kind == SystemAuditCategoryKind.GlobalObjectAccessAuditing;

        /// <summary>
        /// Initializes a new instance of the <see cref="AuditCategoryItem"/> class.
        /// </summary>
        public AuditCategoryItem(string displayName, Guid categoryGuid, SystemAuditCategoryKind kind = SystemAuditCategoryKind.Standard)
        {
            DisplayName = displayName ?? string.Empty;
            CategoryGuid = categoryGuid;
            Kind = kind;
        }
    }

    /// <summary>
    /// Represents an audit subcategory or a Global Object Access Auditing item.
    /// </summary>
    public sealed class AuditSubcategoryValue
    {
        /// <summary>
        /// Gets or sets the subcategory GUID.
        /// </summary>
        public Guid SubcategoryGuid { get; set; }

        /// <summary>
        /// Gets or sets the parent audit category GUID.
        /// </summary>
        public Guid AuditCategoryGuid { get; set; }

        /// <summary>
        /// Gets or sets the localized display name.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the audit flags for the subcategory.
        /// </summary>
        public AuditPolicyFlags Flags { get; set; }

        /// <summary>
        /// Gets or sets whether the policy is defined in the Local Group Policy Object.
        /// </summary>
        public bool IsDefined { get; set; }

        /// <summary>
        /// Gets or sets the item kind.
        /// </summary>
        public SystemAuditItemKind ItemKind { get; set; } = SystemAuditItemKind.AuditSubcategory;

        /// <summary>
        /// Gets or sets the Global Object Access resource type.
        /// </summary>
        public SystemAuditResourceType ResourceType { get; set; }

        /// <summary>
        /// Gets or sets the explain text shown on the Explain tab.
        /// </summary>
        public string ExplainText { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the raw ACL bytes for a Global Object Access Auditing policy.
        /// </summary>
        public byte[]? GlobalSaclBinary { get; set; }

        /// <summary>
        /// Gets or sets the SDDL string for a Global Object Access Auditing policy.
        /// </summary>
        public string GlobalSaclSddl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether the policy state could be read successfully.
        /// </summary>
        public bool HasReadableState { get; set; } = true;

        /// <summary>
        /// Gets or sets the message to show when the policy state could not be read.
        /// </summary>
        public string StateReadErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Gets whether this item is a Global Object Access Auditing policy.
        /// </summary>
        public bool IsGlobalObjectAccessPolicy => ItemKind == SystemAuditItemKind.GlobalObjectAccessPolicy;

        /// <summary>
        /// Gets whether explain text is available.
        /// </summary>
        public bool HasExplainText => !string.IsNullOrWhiteSpace(ExplainText);

        /// <summary>
        /// Gets whether a Global Object Access SACL is configured.
        /// </summary>
        public bool GlobalSaclConfigured => IsDefined && !string.IsNullOrWhiteSpace(GlobalSaclSddl);

        /// <summary>
        /// Gets a human-readable description of the current policy state.
        /// </summary>
        public string DisplaySetting
        {
            get
            {
                if (!HasReadableState && !string.IsNullOrWhiteSpace(StateReadErrorMessage))
                    return StateReadErrorMessage;

                if (IsGlobalObjectAccessPolicy)
                {
                    return IsDefined
                        ? Localized(SecPolKeys.SystemAuditConfigured, "Configured")
                        : Localized(SecPolKeys.AuditNotConfigured, "Not configured");
                }

                if (!IsDefined)
                    return Localized(SecPolKeys.AuditNotConfigured, "Not configured");

                if (Flags == AuditPolicyFlags.None)
                    return Localized(SecPolKeys.AuditNoAuditing, "No auditing");

                var parts = new List<string>();
                if (Flags.HasFlag(AuditPolicyFlags.Success))
                    parts.Add(Localized(SecPolKeys.AuditSuccess, "Success"));
                if (Flags.HasFlag(AuditPolicyFlags.Failure))
                    parts.Add(Localized(SecPolKeys.AuditFailure, "Failure"));
                return string.Join(", ", parts);
            }
        }

        /// <summary>
        /// Creates a detached copy of the current item.
        /// </summary>
        public AuditSubcategoryValue Clone()
        {
            return new AuditSubcategoryValue
            {
                SubcategoryGuid = SubcategoryGuid,
                AuditCategoryGuid = AuditCategoryGuid,
                DisplayName = DisplayName,
                Flags = Flags,
                IsDefined = IsDefined,
                ItemKind = ItemKind,
                ResourceType = ResourceType,
                ExplainText = ExplainText,
                GlobalSaclBinary = GlobalSaclBinary is null ? null : (byte[])GlobalSaclBinary.Clone(),
                GlobalSaclSddl = GlobalSaclSddl,
                HasReadableState = HasReadableState,
                StateReadErrorMessage = StateReadErrorMessage
            };
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
    }
}
