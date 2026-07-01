using OneMMC.Core.Localization;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.RSoP;

namespace OneMMC.Core.Features.PolicyManagement.ViewModels.RSoP
{
    /// <summary>
    /// Represents a Resultant Set of Policy (RSoP) item displayed in the policy list.
    /// Shows the effective policy state as read from the registry/pol file.
    /// </summary>
    public sealed class RSoPPolicyItem
    {
        /// <summary>
        /// Gets the display name of the policy.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the description of the policy.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the current state of the policy.
        /// </summary>
        public PolicyState State { get; }

        /// <summary>
        /// Gets the localized string representation of the policy state.
        /// </summary>
        public string StateString { get; }

        /// <summary>
        /// Gets the source GPO that applied this policy.
        /// For non-domain machines, this is always "Local Policy".
        /// </summary>
        public string SourceGPO { get; }

        /// <summary>
        /// Gets the registry key path associated with this policy.
        /// </summary>
        public string RegistryKeyPath { get; }

        /// <summary>
        /// Gets the registry value name associated with this policy.
        /// </summary>
        public string RegistryValueName { get; }

        /// <summary>
        /// Gets the human-readable category path.
        /// </summary>
        public string CategoryPath { get; }

        /// <summary>
        /// Gets the supported-on information.
        /// </summary>
        public string SupportedOn { get; }

        /// <summary>
        /// Gets whether this is a computer policy (vs user policy).
        /// </summary>
        public bool IsComputerPolicy { get; }

        /// <summary>
        /// Gets the underlying policy result for detail lookups.
        /// </summary>
        public RSoPPolicyResult UnderlyingResult { get; }

        /// <summary>
        /// Creates a new RSoPPolicyItem from an RSoPPolicyResult.
        /// </summary>
        public RSoPPolicyItem(RSoPPolicyResult result)
        {
            UnderlyingResult = result;
            DisplayName = result.DisplayName;
            Description = result.Description;
            State = result.State;
            RegistryKeyPath = result.RegistryKey;
            RegistryValueName = result.RegistryValue;
            CategoryPath = result.CategoryPath;
            SupportedOn = result.SupportedOn;
            IsComputerPolicy = result.IsComputerPolicy;

            // Source GPO â€” for local-only operations, always "Local Policy"
            SourceGPO = LocalizationProvider.Current.GetString(
                ResourceFileNames.Policy, RSoPKeys.SourceLocalPolicy);

            // Localized state string
            StateString = State switch
            {
                PolicyState.NotConfigured => LocalizationProvider.Current.GetString(
                    ResourceFileNames.Policy, PolicyKeys.StateNotConfigured),
                PolicyState.Enabled => LocalizationProvider.Current.GetString(
                    ResourceFileNames.Common, "Common_Enabled"),
                PolicyState.Disabled => LocalizationProvider.Current.GetString(
                    ResourceFileNames.Common, "Common_Disabled"),
                _ => LocalizationProvider.Current.GetString(
                    ResourceFileNames.Policy, PolicyKeys.StateUnknown)
            };
        }
    }
}


