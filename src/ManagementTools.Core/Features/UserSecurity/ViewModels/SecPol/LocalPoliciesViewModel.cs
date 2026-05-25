using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Localization;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol
{
    /// <summary>
    /// ViewModel for the Local Policies page (Audit Policy, User Rights Assignment, Security Options).
    /// Reads and writes policy values from/to the system via SecurityPolicyService.
    /// </summary>
    public sealed partial class LocalPoliciesViewModel : ObservableObject
    {
        private readonly SecurityPolicyService _policyService;
        private readonly ILogger<LocalPoliciesViewModel> _logger;
        private readonly IAdminService _adminService;

        /// <summary>
        /// Raised when a write operation fails due to insufficient administrator privileges.
        /// </summary>
        public event EventHandler? AdminPermissionRequired;

        /// <summary>Available categories within Local Policies.</summary>
        public ObservableCollection<PolicyCategoryItem> Categories { get; } = new();

        /// <summary>Policies displayed for the currently selected category.</summary>
        public ObservableCollection<SecurityPolicyValue> CurrentPolicies { get; } = new();

        [ObservableProperty]
        public partial PolicyCategoryItem? SelectedCategory { get; set; }

        [ObservableProperty]
        public partial SecurityPolicyValue? SelectedPolicy { get; set; }

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string FilterText { get; set; } = string.Empty;

        private List<SecurityPolicyValue> _allPolicies = new();

        public LocalPoliciesViewModel(
            SecurityPolicyService policyService,
            ILogger<LocalPoliciesViewModel> logger,
            IAdminService adminService)
        {
            _policyService = policyService;
            _logger = logger;
            _adminService = adminService;
            _logger.LogDebug("[LocalPoliciesViewModel] Initializing");

            var L = LocalizationProvider.Current;
            Categories.Add(new PolicyCategoryItem(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_AuditPolicy"), SecurityPolicyCategory.AuditPolicy));
            Categories.Add(new PolicyCategoryItem(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_UserRightsAssignment"), SecurityPolicyCategory.UserRightsAssignment));
            Categories.Add(new PolicyCategoryItem(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_SecurityOptions"), SecurityPolicyCategory.SecurityOptions));

            // Select first category by default
            if (Categories.Count > 0)
            {
                SelectedCategory = Categories[0];
            }
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            CurrentPolicies.Clear();
            var filtered = string.IsNullOrWhiteSpace(FilterText)
                ? _allPolicies
                : _allPolicies.Where(p => !string.IsNullOrEmpty(p?.Definition?.DisplayName)
                    && p.Definition.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var policy in filtered)
                CurrentPolicies.Add(policy);
        }

        partial void OnSelectedCategoryChanged(PolicyCategoryItem? value)
        {
            if (value != null)
            {
                _logger.LogDebug("[LocalPoliciesViewModel] Category changed to: {CategoryDisplayName}", value.DisplayName);
                LoadPoliciesCommand.Execute(null);
            }
        }

        /// <summary>Loads policies from the system for the currently selected category.</summary>
        [RelayCommand]
        private async Task LoadPoliciesAsync()
        {
            if (SelectedCategory == null) return;

            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            SelectedPolicy = null;

            _logger.LogDebug("[LocalPoliciesViewModel] Loading policies for: {CategoryDisplayName}", SelectedCategory.DisplayName);

            try
            {
                var policies = await Task.Run(() => _policyService.ReadPolicies(SelectedCategory.Category));

                _allPolicies = new List<SecurityPolicyValue>(policies);
                ApplyFilter();

                _logger.LogInformation("[LocalPoliciesViewModel] Loaded {PolicyCount} policies", _allPolicies.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LocalPoliciesViewModel] Error loading policies.");
                HasError = true;
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>Refreshes the current policy list from the system.</summary>
        [RelayCommand]
        private async Task RefreshAsync()
        {
            _logger.LogDebug("[LocalPoliciesViewModel] Refreshing policies");
            await LoadPoliciesAsync();
        }

        /// <summary>Saves a modified policy value to the system.</summary>
        public async Task<bool> SavePolicyAsync(SecurityPolicyValue policyValue)
        {
            _logger.LogDebug("[LocalPoliciesViewModel] Saving policy: {PolicyKey}", policyValue.Definition.Key);

            try
            {
                await Task.Run(() => _policyService.WritePolicy(policyValue));
                _logger.LogInformation("[LocalPoliciesViewModel] Policy saved successfully: {PolicyKey}", policyValue.Definition.Key);

                // Refresh to show updated values
                await LoadPoliciesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LocalPoliciesViewModel] Error saving policy: {PolicyKey}", policyValue.Definition.Key);
                if (_adminService.IsPermissionError(ex))
                {
                    ErrorMessage = LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
                    AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ErrorMessage = ex.Message;
                }
                HasError = true;
                return false;
            }
        }
    }
}


