using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Rules;
using ManagementTools.Core.Features.SystemManagement.ViewModels.WF.Rules;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using ManagementTools.Services;
using ManagementTools.Views.Dialogs.NewRule;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace ManagementTools.Views;

/// <summary>
/// Shared Rules Editor page for both Inbound and Outbound firewall rules.
/// Receives a <see cref="FirewallRuleDirection"/> parameter via navigation.
/// </summary>
public sealed partial class FirewallRuleEditorPage : Page
{
    private readonly ILogger<FirewallRuleEditorPage> _logger;
    private readonly HashSet<ToggleSwitch> _trackedRuleToggles = [];
    private readonly HashSet<ToggleSwitch> _userInitiatedRuleToggles = [];
    private IDisposable? _firewallChangeSubscription;
    private bool _isRefreshingFromSystemChange;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public FirewallRuleViewModel ViewModel { get; }

    public FirewallRuleEditorPage()
    {
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
        await ManagementTools.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is FirewallRuleDirection direction)
        {
            try
            {
                await ViewModel.InitializeAsync(direction);
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(LocalizedStrings.Common_ErrorTitle, LocalizedStrings.WF_Error_LoadRules);
                _logger.LogWarning(ex, "Failed to load Windows Firewall rules.");
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
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        _firewallChangeSubscription?.Dispose();
        _firewallChangeSubscription = null;
        _trackedRuleToggles.Clear();
        _userInitiatedRuleToggles.Clear();
        Unloaded -= OnUnloaded;
    }

    private void OnFirewallRulesChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_isRefreshingFromSystemChange || ViewModel.IsLoading)
            {
                return;
            }

            _isRefreshingFromSystemChange = true;
            try
            {
                await ViewModel.RefreshFromExternalChangeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh Windows Firewall rules after a system change.");
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

        var adminService = App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            await ManagementTools.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
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
            BreadcrumbNavigationService.AddBreadcrumb(rule.DisplayName, typeof(FirewallRuleInfoPage), rule);
            Frame.Navigate(
                typeof(FirewallRuleInfoPage),
                rule,
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
        _userInitiatedRuleToggles.Add(toggleSwitch);

        var expirationTimer = DispatcherQueue.CreateTimer();
        expirationTimer.Interval = TimeSpan.FromSeconds(3);
        expirationTimer.Tick += (_, _) =>
        {
            _userInitiatedRuleToggles.Remove(toggleSwitch);
            expirationTimer.Stop();
        };
        expirationTimer.Start();
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

        if (rule.Enabled == toggleSwitch.IsOn)
        {
            return;
        }

        var adminService = App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            ResetToggleSwitch(toggleSwitch, rule.Enabled);
            await ManagementTools.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return;
        }

        bool previousEnabled = rule.Enabled;
        try
        {
            await ViewModel.SetRuleEnabledAsync(rule, toggleSwitch.IsOn);
        }
        catch (Exception ex)
        {
            ResetToggleSwitch(toggleSwitch, previousEnabled);
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateRule, ex.Message);
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
