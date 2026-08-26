using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using OneMMC.Core.Features.SystemManagement.ViewModels.WF.Rules;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Services;
using OneMMC.Views.Dialogs.NewRule;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
// Disambiguate DispatcherQueueTimer: both Microsoft.UI.Dispatching and Windows.System
// declare it; the WinUI 3 timer returned by DispatcherQueue.CreateTimer() is the former.
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace OneMMC.Views;

/// <summary>
/// Shared Rules Editor page for both Inbound and Outbound firewall rules.
/// Receives a <see cref="FirewallRuleDirection"/> parameter via navigation.
/// </summary>
public sealed partial class FirewallRuleEditorPage : Page
{
    private readonly ILogger<FirewallRuleEditorPage> _logger;
    private readonly CancellationTokenSource _pageLifetimeCancellation = new();
    private readonly CancellationToken _pageLifetimeToken;
    private readonly HashSet<ToggleSwitch> _trackedRuleToggles = [];
    private readonly HashSet<ToggleSwitch> _userInitiatedRuleToggles = [];
    private readonly Dictionary<DispatcherQueueTimer, ToggleSwitch> _toggleExpirationTimers = [];
    private IDisposable? _firewallChangeSubscription;
    private bool _isRefreshingFromSystemChange;
    private int _pageLifetimeEnded;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public FirewallRuleViewModel ViewModel { get; }

    public FirewallRuleEditorPage()
    {
        _pageLifetimeToken = _pageLifetimeCancellation.Token;
        _logger = App.GetRequiredService<ILogger<FirewallRuleEditorPage>>();
        ViewModel = App.GetRequiredService<FirewallRuleViewModel>();
        InitializeComponent();

        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
        _firewallChangeSubscription = App.GetRequiredService<WindowsFirewallRuleChangeService>()
            .Subscribe(OnFirewallRulesChanged);
        Unloaded += OnUnloaded;
    }

    private async void OnAdminPermissionRequired(object? sender, System.EventArgs e)
    {
        await OneMMC.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is FirewallRuleDirection direction)
        {
            try
            {
                await ViewModel.InitializeAsync(direction).WaitAsync(_pageLifetimeToken);
                _pageLifetimeToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                if (_pageLifetimeToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Loading Windows Firewall rules was canceled because the page unloaded.");
                }
                else
                {
                    await ShowErrorDialogAsync(LocalizedStrings.Common_ErrorTitle, LocalizedStrings.WF_Error_LoadRules);
                    _logger.LogWarning(ex, "Failed to load Windows Firewall rules.");
                }
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelPendingLoad();
        base.OnNavigatedFrom(e);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        EndPageLifetime();
        ViewModel.CancelPendingLoad();
        StopToggleExpirationTimers();
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        _firewallChangeSubscription?.Dispose();
        _firewallChangeSubscription = null;
        _trackedRuleToggles.Clear();
        _userInitiatedRuleToggles.Clear();
        Unloaded -= OnUnloaded;
        _pageLifetimeCancellation.Dispose();
    }

    private void OnFirewallRulesChanged(object? sender, EventArgs e)
    {
        CancellationToken cancellationToken = _pageLifetimeToken;
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            if (cancellationToken.IsCancellationRequested ||
                _isRefreshingFromSystemChange ||
                ViewModel.IsLoading)
            {
                return;
            }

            _isRefreshingFromSystemChange = true;
            try
            {
                await ViewModel.RefreshFromExternalChangeAsync().WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Refreshing Windows Firewall rules was canceled because the page unloaded.");
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to refresh Windows Firewall rules after a system change.");
                }
            }
            finally
            {
                _isRefreshingFromSystemChange = false;
            }
        });
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewFirewallRuleDialog(ViewModel.Direction, XamlRoot);
        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var adminService = App.GetRequiredService<OneMMC.Core.Abstractions.Services.IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            await OneMMC.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return;
        }

        var firewallService = App.GetRequiredService<WindowsFirewallService>();
        var firewallRuleService = App.GetRequiredService<WindowsFirewallRuleService>();
        foreach (var createdRule in dialog.CreatedRules)
        {
            if (createdRule.IsPredefined)
            {
                firewallService.UpdateRule(createdRule);
            }
            else
            {
                firewallService.AddRule(createdRule);
            }

            bool overrideBlockRules = createdRule.ConnectionAction == FirewallConnectionAction.AllowIfSecure &&
                                      createdRule.OverrideBlockRules;
            firewallRuleService.SetOverrideBlockRules(createdRule.Name, overrideBlockRules);
        }

        await ViewModel.InitializeAsync(ViewModel.Direction);
    }

    private void RuleCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.SettingsCard card &&
            card.Tag is FirewallRuleModel rule)
        {
            FirewallRuleNavigationParameter navigationParameter =
                FirewallRuleNavigationParameter.ForFirewallRule(rule);
            BreadcrumbNavigationService.AddBreadcrumb(
                rule.DisplayName,
                typeof(FirewallRuleInfoPage),
                navigationParameter);
            Frame.Navigate(
                typeof(FirewallRuleInfoPage),
                navigationParameter,
                new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo
                {
                    Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight
                });
        }
    }

    private void RuleToggle_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch || !_trackedRuleToggles.Add(toggleSwitch))
        {
            return;
        }

        toggleSwitch.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RuleToggle_PointerPressed), true);
        toggleSwitch.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RuleToggle_KeyDown), true);
    }

    private void RuleToggle_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch || !_trackedRuleToggles.Remove(toggleSwitch))
        {
            return;
        }

        toggleSwitch.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RuleToggle_PointerPressed));
        toggleSwitch.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(RuleToggle_KeyDown));
        StopToggleExpirationTimers(toggleSwitch);
        _userInitiatedRuleToggles.Remove(toggleSwitch);
    }

    private void RuleToggle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            MarkUserInitiatedToggle(toggleSwitch);
        }
    }

    private void RuleToggle_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && e.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            MarkUserInitiatedToggle(toggleSwitch);
        }
    }

    private void MarkUserInitiatedToggle(ToggleSwitch toggleSwitch)
    {
        if (!IsPageLifetimeActive)
        {
            return;
        }

        StopToggleExpirationTimers(toggleSwitch);
        _userInitiatedRuleToggles.Add(toggleSwitch);

        DispatcherQueueTimer expirationTimer = DispatcherQueue.CreateTimer();
        expirationTimer.Interval = TimeSpan.FromSeconds(3);
        expirationTimer.Tick += RuleToggleExpirationTimer_Tick;
        _toggleExpirationTimers.Add(expirationTimer, toggleSwitch);
        expirationTimer.Start();
    }

    private void RuleToggleExpirationTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_toggleExpirationTimers.Remove(sender, out ToggleSwitch? toggleSwitch) &&
            IsPageLifetimeActive)
        {
            _userInitiatedRuleToggles.Remove(toggleSwitch);
        }

        sender.Tick -= RuleToggleExpirationTimer_Tick;
        sender.Stop();
    }

    private void StopToggleExpirationTimers(ToggleSwitch? toggleSwitch = null)
    {
        List<DispatcherQueueTimer> timersToStop = [];
        foreach ((DispatcherQueueTimer timer, ToggleSwitch trackedToggle) in _toggleExpirationTimers)
        {
            if (toggleSwitch is null || ReferenceEquals(toggleSwitch, trackedToggle))
            {
                timersToStop.Add(timer);
            }
        }

        foreach (DispatcherQueueTimer timer in timersToStop)
        {
            _toggleExpirationTimers.Remove(timer);
            timer.Tick -= RuleToggleExpirationTimer_Tick;
            timer.Stop();
        }
    }

    private async void RuleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch || toggleSwitch.Tag is not FirewallRuleModel rule)
        {
            return;
        }

        if (!_userInitiatedRuleToggles.Remove(toggleSwitch))
        {
            return;
        }

        StopToggleExpirationTimers(toggleSwitch);

        bool requestedEnabled = toggleSwitch.IsOn;
        if (rule.Enabled == requestedEnabled)
        {
            return;
        }

        var adminService = App.GetRequiredService<OneMMC.Core.Abstractions.Services.IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            ResetToggleSwitch(toggleSwitch, rule.Enabled);
            await OneMMC.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return;
        }

        bool previousEnabled = rule.Enabled;
        CancellationToken cancellationToken = _pageLifetimeToken;
        try
        {
            await ViewModel.SetRuleEnabledAsync(rule, requestedEnabled).WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(
                    "Updating Windows Firewall rule {RuleName} was canceled because the page unloaded.",
                    rule.Name);
            }
            else
            {
                ResetToggleSwitch(toggleSwitch, previousEnabled);
                await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateRule, ex.Message);
            }
        }
    }

    private bool IsPageLifetimeActive
        => Volatile.Read(ref _pageLifetimeEnded) == 0;

    private void EndPageLifetime()
    {
        if (Interlocked.Exchange(ref _pageLifetimeEnded, 1) == 0)
        {
            _pageLifetimeCancellation.Cancel();
        }
    }

    private void ResetToggleSwitch(ToggleSwitch toggleSwitch, bool enabled)
    {
        toggleSwitch.Toggled -= RuleToggle_Toggled;
        toggleSwitch.IsOn = enabled;
        toggleSwitch.Toggled += RuleToggle_Toggled;
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = LocalizedStrings.Common_OKButton,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }
}
