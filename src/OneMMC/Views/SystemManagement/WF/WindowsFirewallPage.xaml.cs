using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace OneMMC.Views;

public sealed partial class WindowsFirewallPage : Page
{
    private readonly WindowsFirewallProfileService _firewallProfileService;
    private readonly WindowsFirewallService _firewallService;
    private readonly IAdminService _adminService;
    private readonly ILogger<WindowsFirewallPage> _logger;
    private IDisposable? _firewallChangeSubscription;
    private bool _isLoadingOverview;
    private bool _isUnloaded;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public WindowsFirewallPage()
    {
        _firewallProfileService = App.GetRequiredService<WindowsFirewallProfileService>();
        _firewallService = App.GetRequiredService<WindowsFirewallService>();
        _adminService = App.GetRequiredService<IAdminService>();
        _logger = App.GetRequiredService<ILogger<WindowsFirewallPage>>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await Task.Yield();
        await LoadOverviewAsync();
        await SubscribeToFirewallChangesAsync();
    }

    private void FirewallPropertiesCard_Click(object sender, RoutedEventArgs e)
    {
        BreadcrumbNavigationService.AddBreadcrumb(
            LocalizedStrings.WF_Common_WindowsDefenderFirewallProperties,
            typeof(WindowsFirewallPropertiesPage));

        Frame.Navigate(
            typeof(WindowsFirewallPropertiesPage),
            null,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void InboundRulesCard_Click(object sender, RoutedEventArgs e)
    {
        BreadcrumbNavigationService.AddBreadcrumb(
            LocalizedStrings.WF_InboundRules_PageTitle,
            typeof(FirewallRuleEditorPage),
            FirewallRuleDirection.Inbound);

        Frame.Navigate(
            typeof(FirewallRuleEditorPage),
            FirewallRuleDirection.Inbound,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void OutboundRulesCard_Click(object sender, RoutedEventArgs e)
    {
        BreadcrumbNavigationService.AddBreadcrumb(
            LocalizedStrings.WF_OutboundRules_PageTitle,
            typeof(FirewallRuleEditorPage),
            FirewallRuleDirection.Outbound);

        Frame.Navigate(
            typeof(FirewallRuleEditorPage),
            FirewallRuleDirection.Outbound,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void ConnectionSecurityRulesCard_Click(object sender, RoutedEventArgs e)
    {
        BreadcrumbNavigationService.AddBreadcrumb(
            LocalizedStrings.WF_ConnectionSecurityRules_PageTitle,
            typeof(ConnectionSecurityRulesPage));

        Frame.Navigate(
            typeof(ConnectionSecurityRulesPage),
            null,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void MonitoringCard_Click(object sender, RoutedEventArgs e)
    {
        BreadcrumbNavigationService.AddBreadcrumb(
            LocalizedStrings.WF_Monitoring_PageTitle,
            typeof(FirewallMonitoringPage));

        Frame.Navigate(
            typeof(FirewallMonitoringPage),
            null,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void OpenLegacyFirewallItem_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("wf.msc") { UseShellExecute = true });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadOverviewAsync();
    }

    private async void RestoreDefaultsItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_adminService.IsRunningAsAdmin)
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
            return;
        }

        var confirmDialog = new ContentDialog
        {
            Title = LocalizedStrings.WF_RestoreLocalDefaults_Title,
            Content = LocalizedStrings.WF_RestoreLocalDefaults_Message,
            PrimaryButtonText = LocalizedStrings.WF_RestoreLocalDefaults_Button,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Task.Run(() => _firewallService.RestoreLocalFirewallDefaults());
        await LoadOverviewAsync();
    }

    private async Task LoadOverviewAsync()
    {
        if (_isLoadingOverview)
        {
            return;
        }

        _isLoadingOverview = true;
        try
        {
            IReadOnlyList<FirewallProfileModel> profiles = await Task.Run(() => _firewallProfileService.GetProfiles());
            ApplyOverview(profiles);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh the Windows Firewall overview.");
        }
        finally
        {
            _isLoadingOverview = false;
        }
    }

    private void ApplyOverview(IReadOnlyList<FirewallProfileModel> profiles)
    {
        foreach (FirewallProfileModel profile in profiles)
        {
            string profileName = GetProfileDisplayName(profile.ProfileType);
            string header = profile.IsActive
                ? string.Format(CultureInfo.CurrentCulture, LocalizedStrings.WF_ProfileHeaderActive_Format, profileName)
                : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.WF_ProfileHeader_Format, profileName);
            string status = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.WF_FirewallStatus_Format,
                profile.IsEnabled ? LocalizedStrings.WF_State_On : LocalizedStrings.WF_State_Off);
            string inbound = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.WF_InboundOverview_Format,
                profile.DefaultInboundAction == FirewallDefaultAction.Block
                    ? LocalizedStrings.WF_Traffic_Blocked
                    : LocalizedStrings.WF_Traffic_Allowed);
            string outbound = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.WF_OutboundOverview_Format,
                profile.DefaultOutboundAction == FirewallDefaultAction.Block
                    ? LocalizedStrings.WF_Traffic_Blocked
                    : LocalizedStrings.WF_Traffic_Allowed);

            switch (profile.ProfileType)
            {
                case FirewallProfileType.Domain:
                    DomainProfileCard.Header = header;
                    DomainStatusText.Text = status;
                    DomainInboundText.Text = inbound;
                    DomainOutboundText.Text = outbound;
                    break;
                case FirewallProfileType.Private:
                    PrivateProfileCard.Header = header;
                    PrivateStatusText.Text = status;
                    PrivateInboundText.Text = inbound;
                    PrivateOutboundText.Text = outbound;
                    break;
                case FirewallProfileType.Public:
                    PublicProfileCard.Header = header;
                    PublicStatusText.Text = status;
                    PublicInboundText.Text = inbound;
                    PublicOutboundText.Text = outbound;
                    break;
            }
        }
    }

    private string GetProfileDisplayName(FirewallProfileType profileType)
        => profileType switch
        {
            FirewallProfileType.Domain => LocalizedStrings.WF_Profile_Domain,
            FirewallProfileType.Private => LocalizedStrings.WF_Profile_Private,
            FirewallProfileType.Public => LocalizedStrings.WF_Profile_Public,
            _ => profileType.ToString()
        };

    private void OnFirewallConfigurationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await LoadOverviewAsync();
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _firewallChangeSubscription?.Dispose();
        _firewallChangeSubscription = null;
        Unloaded -= OnUnloaded;
    }

    private async Task SubscribeToFirewallChangesAsync()
    {
        try
        {
            IDisposable subscription = await Task.Run(() =>
                App.GetRequiredService<WindowsFirewallRuleChangeService>()
                    .Subscribe(OnFirewallConfigurationChanged));

            if (_isUnloaded)
            {
                subscription.Dispose();
                return;
            }

            _firewallChangeSubscription = subscription;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to Windows Firewall overview changes.");
        }
    }
}
