using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace OneMMC.Core.Features.SystemManagement.ViewModels.WF.Rules
{
    /// <summary>
    /// ViewModel for the Firewall Rules Editor page.
    /// Shared between Inbound and Outbound rule lists.
    /// </summary>
    public partial class FirewallRuleViewModel : ObservableObject
    {
        private readonly WindowsFirewallService _firewallService;
        private readonly OneMMC.Core.Abstractions.Services.IAdminService _adminService;
        private CancellationTokenSource? _loadRulesCancellationTokenSource;
        private int _visibleLoadCount;

        public FirewallRuleViewModel(WindowsFirewallService firewallService, OneMMC.Core.Abstractions.Services.IAdminService adminService)
        {
            _firewallService = firewallService;
            _adminService = adminService;
        }

        [ObservableProperty]
        public partial FirewallRuleDirection Direction { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<FirewallRuleModel> Rules { get; set; } = [];

        [ObservableProperty]
        public partial FirewallRuleModel? SelectedRule { get; set; }

        [ObservableProperty]
        public partial string FilterText { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        public partial bool IsLoading { get; set; }

        public bool IsInbound => Direction == FirewallRuleDirection.Inbound;
        public bool IsConnectionSecurity => Direction == FirewallRuleDirection.ConnectionSecurity;

        [ObservableProperty]
        public partial ObservableCollection<FirewallRuleModel> FilteredRules { get; set; } = [];

        /// <summary>
        /// Clears the loaded firewall rules when the owning page leaves the visual tree.
        /// </summary>
        /// <remarks>
        /// A stock Windows install has hundreds to thousands of rules, and every
        /// <see cref="FirewallRuleModel"/> is an observable object with a wide property surface. Releasing
        /// them on unload keeps the cost of visiting a rules page from lingering until the next GC.
        /// Mirrors <c>CertificateStoresViewModelBase.ClearCachedData()</c>.
        /// </remarks>
        public void ClearCachedData()
        {
            Rules.Clear();
            FilteredRules.Clear();
            SelectedRule = null;
            FilterText = string.Empty;
            IsLoading = false;
        }

        partial void OnDirectionChanged(FirewallRuleDirection value)
        {
            OnPropertyChanged(nameof(IsInbound));
            OnPropertyChanged(nameof(IsConnectionSecurity));
        }

        partial void OnFilterTextChanged(string value)
            => ApplyFilter();

        partial void OnRulesChanged(ObservableCollection<FirewallRuleModel> value)
            => ApplyFilter();

        public event System.EventHandler? AdminPermissionRequired;

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
            => await LoadRulesAsync(showLoading: true);

        [RelayCommand]
        private void DeleteRule()
        {
            if (!_adminService.IsRunningAsAdmin)
            {
                AdminPermissionRequired?.Invoke(this, System.EventArgs.Empty);
                return;
            }

            if (SelectedRule is not null)
            {
                _firewallService.DeleteRule(SelectedRule.Name);
                Rules.Remove(SelectedRule);
            }
        }

        [RelayCommand]
        private async Task ToggleRuleAsync()
        {
            if (!_adminService.IsRunningAsAdmin)
            {
                AdminPermissionRequired?.Invoke(this, System.EventArgs.Empty);
                return;
            }

            if (SelectedRule is not null)
            {
                bool enabled = !SelectedRule.Enabled;
                await SetRuleEnabledAsync(SelectedRule, enabled);
            }
        }

        /// <summary>
        /// Initializes the view model for the specified firewall rule direction.
        /// </summary>
        /// <param name="direction">The rule direction to display.</param>
        public async Task InitializeAsync(FirewallRuleDirection direction)
        {
            Direction = direction;
            await LoadRulesAsync(showLoading: true);
        }

        /// <summary>
        /// Refreshes rules after Windows reports an external firewall rule change.
        /// </summary>
        public async Task RefreshFromExternalChangeAsync()
        {
            if (IsLoading)
            {
                return;
            }

            await LoadRulesAsync(showLoading: false);
        }

        /// <summary>
        /// Updates a rule's enabled state without blocking the UI thread.
        /// </summary>
        /// <param name="rule">The rule to update.</param>
        /// <param name="enabled">The requested enabled state.</param>
        public async Task SetRuleEnabledAsync(FirewallRuleModel rule, bool enabled)
        {
            if (rule.Enabled == enabled)
            {
                return;
            }

            bool previousEnabled = rule.Enabled;
            rule.Enabled = enabled;

            try
            {
                await Task.Run(() => _firewallService.SetRuleEnabled(rule.OriginalName, enabled));
                rule.IsRuleGroupEnabled = enabled;
            }
            catch
            {
                rule.Enabled = previousEnabled;
                throw;
            }

            if (rule.IsPredefined)
            {
                await LoadRulesAsync(showLoading: false);
            }
        }

        /// <summary>
        /// Cancels any pending rule load.
        /// </summary>
        public void CancelPendingLoad()
            => _loadRulesCancellationTokenSource?.Cancel();

        private bool CanRefresh()
            => !IsLoading;

        private async Task LoadRulesAsync(bool showLoading)
        {
            _loadRulesCancellationTokenSource?.Cancel();
            var cancellationTokenSource = new CancellationTokenSource();
            _loadRulesCancellationTokenSource = cancellationTokenSource;

            if (showLoading)
            {
                _visibleLoadCount++;
                IsLoading = true;
            }

            try
            {
                FirewallRuleDirection direction = Direction;
                await Task.Yield();

                // The token is deliberately not passed to Task.Run: the rule enumeration is a synchronous
                // WMI walk that cannot be interrupted once started, so the token would only fault the task
                // when a newer load supersedes this one. Being superseded is the expected outcome of every
                // external-change refresh, so the stale result is discarded with a plain check instead of
                // through an OperationCanceledException.
                IReadOnlyList<FirewallRuleModel> systemRules = await Task.Run(() => _firewallService.GetRules(direction));

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                Rules = new ObservableCollection<FirewallRuleModel>(systemRules);
            }
            finally
            {
                if (ReferenceEquals(_loadRulesCancellationTokenSource, cancellationTokenSource))
                {
                    _loadRulesCancellationTokenSource = null;
                }

                // Clearing the busy state is counted rather than tied to "am I still the current load":
                // a visible load that gets superseded by a silent one would otherwise strand IsLoading at
                // true, permanently disabling Refresh and every external-change refresh.
                if (showLoading && --_visibleLoadCount == 0)
                {
                    IsLoading = false;
                }

                cancellationTokenSource.Dispose();
            }
        }

        private void ApplyFilter()
        {
            var query = FilterText?.Trim() ?? string.Empty;
            IEnumerable<FirewallRuleModel> filteredRules = string.IsNullOrEmpty(query)
                ? Rules
                : Rules.Where(rule =>
                    rule.DisplayName.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
                    rule.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
                    rule.Description.Contains(query, System.StringComparison.OrdinalIgnoreCase));

            FilteredRules = new ObservableCollection<FirewallRuleModel>(filteredRules);
        }
    }
}


