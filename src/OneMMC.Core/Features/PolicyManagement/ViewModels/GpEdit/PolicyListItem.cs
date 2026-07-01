using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;

namespace OneMMC.Core.Features.PolicyManagement.ViewModels.GpEdit
{
    /// <summary>
    /// Represents a policy item displayed in the policy list.
    /// </summary>
    public sealed partial class PolicyListItem : ObservableObject
    {
        /// <summary>
        /// Gets or sets the underlying policy.
        /// </summary>
        public PolicyManagerPolicy Policy { get; set; }

        /// <summary>
        /// Gets the display name of the policy.
        /// </summary>
        public string DisplayName => Policy.DisplayName;

        private PolicyState _state;

        /// <summary>
        /// Gets or sets the current state of the policy.
        /// </summary>
        public PolicyState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(StateString));
                }
            }
        }

        /// <summary>
        /// Gets the localized string representation of the policy state.
        /// </summary>
        public string StateString => _state switch
        {
            PolicyState.NotConfigured => LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.StateNotConfigured),
            PolicyState.Enabled => LocalizationProvider.Current.GetString(ResourceFileNames.Common, "Common_Enabled"),
            PolicyState.Disabled => LocalizationProvider.Current.GetString(ResourceFileNames.Common, "Common_Disabled"),
            _ => LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.StateUnknown)
        };

        /// <summary>
        /// Creates a new PolicyListItem.
        /// </summary>
        /// <param name="policy">The policy to represent.</param>
        /// <param name="state">The current state of the policy.</param>
        public PolicyListItem(PolicyManagerPolicy policy, PolicyState state)
        {
            Policy = policy;
            _state = state;
        }
    }
}


