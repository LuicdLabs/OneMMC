using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.Services.SecPol;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.SecPol
{
    /// <summary>
    /// Represents a policy category item for display in the tree view.
    /// </summary>
    public sealed class PolicyCategoryItem
    {
        public string DisplayName { get; }
        public SecurityPolicyCategory Category { get; }

        public PolicyCategoryItem(string displayName, SecurityPolicyCategory category)
        {
            DisplayName = displayName;
            Category = category;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// ViewModel for the Account Policies page (Password Policy, Account Lockout Policy).
    /// Reads and writes policy values from/to the system via SecurityPolicyService.
    /// </summary>
    public sealed partial class AccountPoliciesViewModel : ObservableObject
    {
        private readonly SecurityPolicyService _policyService;
        private readonly ILogger<AccountPoliciesViewModel> _logger;
        private readonly IAdminService _adminService;

        /// <summary>
        /// Raised when a write operation fails due to insufficient administrator privileges.
        /// </summary>
        public event EventHandler? AdminPermissionRequired;

        /// <summary>Available categories within Account Policies.</summary>
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

        public AccountPoliciesViewModel(
            SecurityPolicyService policyService,
            ILogger<AccountPoliciesViewModel> logger,
            IAdminService adminService)
        {
            _policyService = policyService;
            _logger = logger;
            _adminService = adminService;
            _logger.LogDebug("[AccountPoliciesViewModel] Initializing");

            var L = LocalizationProvider.Current;
            Categories.Add(new PolicyCategoryItem(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_PasswordPolicy"), SecurityPolicyCategory.PasswordPolicy));
            Categories.Add(new PolicyCategoryItem(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_AccountLockoutPolicy"), SecurityPolicyCategory.AccountLockoutPolicy));

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
                _logger.LogDebug("[AccountPoliciesViewModel] Category changed to: {CategoryDisplayName}", value.DisplayName);
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

            _logger.LogDebug("[AccountPoliciesViewModel] Loading policies for: {CategoryDisplayName}", SelectedCategory.DisplayName);

            try
            {
                var policies = await Task.Run(() => _policyService.ReadPolicies(SelectedCategory.Category));

                _allPolicies = new List<SecurityPolicyValue>(policies);
                ApplyFilter();

                _logger.LogInformation("[AccountPoliciesViewModel] Loaded {PolicyCount} policies", _allPolicies.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AccountPoliciesViewModel] Error loading policies.");
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
            _logger.LogDebug("[AccountPoliciesViewModel] Refreshing policies");
            await LoadPoliciesAsync();
        }

        /// <summary>Saves a modified policy value to the system.</summary>
        public async Task<bool> SavePolicyAsync(SecurityPolicyValue policyValue)
        {
            _logger.LogDebug("[AccountPoliciesViewModel] Saving policy: {PolicyKey}", policyValue.Definition.Key);

            try
            {
                await Task.Run(() => _policyService.WritePolicy(policyValue));
                _logger.LogInformation("[AccountPoliciesViewModel] Policy saved successfully: {PolicyKey}", policyValue.Definition.Key);

                // Refresh to show updated values
                await LoadPoliciesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AccountPoliciesViewModel] Error saving policy: {PolicyKey}", policyValue.Definition.Key);
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


