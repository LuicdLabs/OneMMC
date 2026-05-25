using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Rules;
using ManagementTools.Localization;
using ManagementTools.Views.Dialogs.WFProperties;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views;

public sealed partial class WindowsFirewallPropertiesPage : Page
{
    private readonly WindowsFirewallProfileService _firewallProfileService;
    private readonly ILogger<WindowsFirewallPropertiesPage> _logger;
    private readonly Dictionary<FirewallProfileType, FirewallProfileModel> _profiles = [];
    private readonly SemaphoreSlim _dialogSemaphore = new(1, 1);
    private IDisposable? _firewallChangeSubscription;
    private bool _isPageInitialized;
    private bool _isApplyingSystemState;
    private bool _isRefreshingFromSystemChange;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public WindowsFirewallPropertiesPage()
    {
        _firewallProfileService = App.GetRequiredService<WindowsFirewallProfileService>();
        _logger = App.GetRequiredService<ILogger<WindowsFirewallPropertiesPage>>();

        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += WindowsFirewallPropertiesPage_Unloaded;
        _firewallChangeSubscription = App.GetRequiredService<WindowsFirewallRuleChangeService>()
            .Subscribe(OnFirewallConfigurationChanged);

        ApplySystemState(CreateSnapshot());
        _isPageInitialized = true;
    }

    private async void ProtectedNetworkCustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryParseProfile(button.Tag?.ToString(), out FirewallProfileType profileType))
        {
            return;
        }

        FirewallProfileModel profile = _profiles[profileType];
        List<NetworkConnectionItem> connections = BuildNetworkConnectionList(profile);
        var dialog = new ProtectedNetworkConnectionsDialog(profileType.ToString(), connections);
        if (await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            profile.ProtectedNetworkConnections.Clear();
            foreach (NetworkConnectionItem connection in dialog.Connections)
            {
                profile.ProtectedNetworkConnections.Add(new NetworkConnectionItem
                {
                    Name = connection.Name,
                    IsSelected = connection.IsSelected
                });
            }

            _firewallProfileService.UpdateProfile(profile);
        }
    }

    private async void FirewallBehaviorSpecifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryParseProfile(button.Tag?.ToString(), out FirewallProfileType profileType))
        {
            return;
        }

        FirewallProfileModel profile = CloneProfile(_profiles[profileType]);
        var dialog = new ProfileFirewallBehaviorDialog(profileType.ToString(), profile);
        if (await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            _profiles[profileType].NotificationsDisabled = profile.NotificationsDisabled;
            _profiles[profileType].UnicastResponsesToMulticastBroadcastDisabled = profile.UnicastResponsesToMulticastBroadcastDisabled;
            _firewallProfileService.UpdateProfile(_profiles[profileType]);
        }
    }

    private async void LoggingSpecifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryParseProfile(button.Tag?.ToString(), out FirewallProfileType profileType))
        {
            return;
        }

        var workingCopy = new FirewallLoggingSettings
        {
            FileName = _profiles[profileType].LoggingSettings.FileName,
            MaxFileSizeKilobytes = _profiles[profileType].LoggingSettings.MaxFileSizeKilobytes,
            LogDroppedPackets = _profiles[profileType].LoggingSettings.LogDroppedPackets,
            LogSuccessfulConnections = _profiles[profileType].LoggingSettings.LogSuccessfulConnections
        };

        var dialog = new ProfileLoggingSettingsDialog(profileType.ToString(), workingCopy);
        if (await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            _profiles[profileType].LoggingSettings = workingCopy;
            _firewallProfileService.UpdateProfile(_profiles[profileType]);
        }
    }

    private async void CustomizeIpsecDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IpsecDefaultsModel defaults = _firewallProfileService.GetIpsecDefaults();
            var dialog = new CustomizeIpsecDefaultsDialog(defaults);
            if (await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary)
            {
                _firewallProfileService.UpdateIpsecDefaults(defaults);
                LoadIpsecSettings();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateIpsecDefaults, ex.Message);
            LoadIpsecSettings();
        }
    }

    private async void TunnelAuthorizationCustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FirewallTunnelAuthorizationSettings settings = _firewallProfileService.GetTunnelAuthorizationSettings();
            if (await ShowTunnelAuthorizationModeDialogAsync(settings) == ContentDialogResult.Primary)
            {
                _firewallProfileService.UpdateTunnelAuthorizationSettings(settings);
                LoadIpsecSettings();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateIpsecTunnelAuthorization, ex.Message);
            LoadIpsecSettings();
        }
    }

    private void DomainProfileToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateProfileEnabled(FirewallProfileType.Domain, DomainProfileToggleSwitch.IsOn);
    }

    private void PrivateProfileToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateProfileEnabled(FirewallProfileType.Private, PrivateProfileToggleSwitch.IsOn);
    }

    private void PublicProfileToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateProfileEnabled(FirewallProfileType.Public, PublicProfileToggleSwitch.IsOn);
    }

    private void ProfileActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isPageInitialized || _isApplyingSystemState || sender is not ComboBox comboBox || comboBox.Tag is not string tag)
        {
            return;
        }

        string[] parts = tag.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseProfile(parts[0], out FirewallProfileType profileType))
        {
            return;
        }

        FirewallProfileModel profile = _profiles[profileType];
        if (string.Equals(parts[1], "Inbound", StringComparison.OrdinalIgnoreCase))
        {
            profile.DefaultInboundAction = comboBox.SelectedIndex == 2
                ? FirewallDefaultAction.Allow
                : FirewallDefaultAction.Block;
            profile.BlockAllInboundTraffic = comboBox.SelectedIndex == 1;
        }
        else
        {
            bool isBlock = comboBox.SelectedIndex == 1;
            profile.DefaultOutboundAction = isBlock ? FirewallDefaultAction.Block : FirewallDefaultAction.Allow;
        }

        _firewallProfileService.UpdateProfile(profile);
    }

    private async void IcmpExemptionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isPageInitialized || _isApplyingSystemState)
        {
            return;
        }

        try
        {
            IpsecDefaultsModel actualDefaults = _firewallProfileService.UpdateIcmpExemption(IcmpExemptionComboBox.SelectedIndex == 0);
            bool wasApplyingSystemState = _isApplyingSystemState;
            _isApplyingSystemState = true;
            try
            {
                ApplyIpsecSettings(actualDefaults, _firewallProfileService.GetTunnelAuthorizationSettings());
            }
            finally
            {
                _isApplyingSystemState = wasApplyingSystemState;
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateIpsecIcmpExemption, ex.Message);
            LoadIpsecSettings();
        }
    }

    private async System.Threading.Tasks.Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
    {
        await _dialogSemaphore.WaitAsync();
        try
        {
            dialog.XamlRoot = XamlRoot;
            dialog.RequestedTheme = App.CurrentTheme;
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogSemaphore.Release();
        }
    }

    private void LoadProfiles()
    {
        bool wasApplyingSystemState = _isApplyingSystemState;
        _isApplyingSystemState = true;
        try
        {
            ApplyProfiles(_firewallProfileService.GetProfiles());
        }
        finally
        {
            _isApplyingSystemState = wasApplyingSystemState;
        }
    }

    private void ApplyProfiles(IReadOnlyList<FirewallProfileModel> profiles)
    {
        _profiles.Clear();
        foreach (FirewallProfileModel profile in profiles)
        {
            _profiles[profile.ProfileType] = profile;
            ApplyProfileToUi(profile);
        }
    }

    private void LoadIpsecSettings()
    {
        bool wasApplyingSystemState = _isApplyingSystemState;
        _isApplyingSystemState = true;
        try
        {
            ApplyIpsecSettings(
                _firewallProfileService.GetIpsecDefaults(),
                _firewallProfileService.GetTunnelAuthorizationSettings());
        }
        finally
        {
            _isApplyingSystemState = wasApplyingSystemState;
        }
    }

    private void ApplyIpsecSettings(IpsecDefaultsModel defaults, FirewallTunnelAuthorizationSettings tunnelAuthorization)
    {
        IcmpExemptionComboBox.SelectedIndex = defaults.IcmpExemptionEnabled ? 0 : 1;
        string tunnelAuthorizationMode = tunnelAuthorization.Mode == TunnelAuthorizationMode.None
            ? LocalizedStrings.WF_Common_None
            : LocalizedStrings.WF_Tab_Advanced;
        TunnelAuthorizationSettingsCard.Description = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            LocalizedStrings.WF_CurrentSetting_Format,
            tunnelAuthorizationMode);
    }

    private void ApplyProfileToUi(FirewallProfileModel profile)
    {
        switch (profile.ProfileType)
        {
            case FirewallProfileType.Domain:
                DomainProfileToggleSwitch.IsOn = profile.IsEnabled;
                DomainInboundComboBox.SelectedIndex = profile.DefaultInboundAction == FirewallDefaultAction.Allow
                    ? 2
                    : profile.BlockAllInboundTraffic ? 1 : 0;
                DomainOutboundComboBox.SelectedIndex = profile.DefaultOutboundAction == FirewallDefaultAction.Block ? 1 : 0;
                SetProfileMode(FirewallProfileType.Domain, profile.IsEnabled);
                break;
            case FirewallProfileType.Private:
                PrivateProfileToggleSwitch.IsOn = profile.IsEnabled;
                PrivateInboundComboBox.SelectedIndex = profile.DefaultInboundAction == FirewallDefaultAction.Allow
                    ? 2
                    : profile.BlockAllInboundTraffic ? 1 : 0;
                PrivateOutboundComboBox.SelectedIndex = profile.DefaultOutboundAction == FirewallDefaultAction.Block ? 1 : 0;
                SetProfileMode(FirewallProfileType.Private, profile.IsEnabled);
                break;
            case FirewallProfileType.Public:
                PublicProfileToggleSwitch.IsOn = profile.IsEnabled;
                PublicInboundComboBox.SelectedIndex = profile.DefaultInboundAction == FirewallDefaultAction.Allow
                    ? 2
                    : profile.BlockAllInboundTraffic ? 1 : 0;
                PublicOutboundComboBox.SelectedIndex = profile.DefaultOutboundAction == FirewallDefaultAction.Block ? 1 : 0;
                SetProfileMode(FirewallProfileType.Public, profile.IsEnabled);
                break;
        }
    }

    private void UpdateProfileEnabled(FirewallProfileType profileType, bool enabled)
    {
        if (!_isPageInitialized || _isApplyingSystemState || !_profiles.TryGetValue(profileType, out FirewallProfileModel? profile))
        {
            return;
        }

        profile.IsEnabled = enabled;
        SetProfileMode(profileType, enabled);
        _firewallProfileService.UpdateProfile(profile);
    }

    private void SetProfileMode(FirewallProfileType profileType, bool enabled)
    {
        switch (profileType)
        {
            case FirewallProfileType.Domain:
                DomainInboundComboBox.IsEnabled = enabled;
                DomainOutboundComboBox.IsEnabled = enabled;
                DomainProtectedNetworkButton.IsEnabled = enabled;
                DomainBehaviorButton.IsEnabled = enabled;
                DomainLoggingButton.IsEnabled = enabled;
                break;
            case FirewallProfileType.Private:
                PrivateInboundComboBox.IsEnabled = enabled;
                PrivateOutboundComboBox.IsEnabled = enabled;
                PrivateProtectedNetworkButton.IsEnabled = enabled;
                PrivateBehaviorButton.IsEnabled = enabled;
                PrivateLoggingButton.IsEnabled = enabled;
                break;
            case FirewallProfileType.Public:
                PublicInboundComboBox.IsEnabled = enabled;
                PublicOutboundComboBox.IsEnabled = enabled;
                PublicProtectedNetworkButton.IsEnabled = enabled;
                PublicBehaviorButton.IsEnabled = enabled;
                PublicLoggingButton.IsEnabled = enabled;
                break;
        }
    }

    private async Task<ContentDialogResult> ShowTunnelAuthorizationModeDialogAsync(FirewallTunnelAuthorizationSettings settings)
    {
        var noneRadioButton = new RadioButton
        {
            Content = LocalizedStrings.WF_Common_None,
            GroupName = "TunnelAuthorizationMode",
            IsChecked = settings.Mode == TunnelAuthorizationMode.None
        };

        var advancedRadioButton = new RadioButton
        {
            Content = LocalizedStrings.WF_Tab_Advanced,
            GroupName = "TunnelAuthorizationMode",
            IsChecked = settings.Mode != TunnelAuthorizationMode.None
        };

        var customizeButton = new Button
        {
            Content = LocalizedStrings.WF_Button_Customize,
            MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = advancedRadioButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed
        };

        void UpdateCustomizeButtonVisibility()
        {
            customizeButton.Visibility = advancedRadioButton.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        noneRadioButton.Checked += (_, _) => UpdateCustomizeButtonVisibility();
        advancedRadioButton.Checked += (_, _) => UpdateCustomizeButtonVisibility();
        customizeButton.Click += async (_, _) =>
        {
            advancedRadioButton.IsChecked = true;
            var authorizationsDialog = new IpsecTunnelAuthorizationsDialog(settings);
            await authorizationsDialog.ShowDialogAsync(XamlRoot);
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                noneRadioButton,
                advancedRadioButton,
                customizeButton
            }
        };

        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.WF_Common_IPsecTunnelAuthorization,
            Content = content,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton
        };

        ContentDialogResult result = await ShowContentDialogAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            settings.Mode = advancedRadioButton.IsChecked == true
                ? TunnelAuthorizationMode.Advanced
                : TunnelAuthorizationMode.None;
        }

        return result;
    }

    private static bool TryParseProfile(string? profileName, out FirewallProfileType profileType)
        => Enum.TryParse(profileName, ignoreCase: true, out profileType);

    private static List<NetworkConnectionItem> BuildNetworkConnectionList(FirewallProfileModel profile)
        => profile.ProtectedNetworkConnections
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(connection => new NetworkConnectionItem
            {
                Name = connection.Name,
                IsSelected = connection.IsSelected
            })
            .ToList();

    private static FirewallProfileModel CloneProfile(FirewallProfileModel profile)
    {
        var clone = new FirewallProfileModel
        {
            ProfileType = profile.ProfileType,
            DisplayName = profile.DisplayName,
            IsActive = profile.IsActive,
            IsEnabled = profile.IsEnabled,
            DefaultInboundAction = profile.DefaultInboundAction,
            DefaultOutboundAction = profile.DefaultOutboundAction,
            BlockAllInboundTraffic = profile.BlockAllInboundTraffic,
            NotificationsDisabled = profile.NotificationsDisabled,
            UnicastResponsesToMulticastBroadcastDisabled = profile.UnicastResponsesToMulticastBroadcastDisabled,
            PolicyModifyState = profile.PolicyModifyState,
            LoggingSettings = new FirewallLoggingSettings
            {
                FileName = profile.LoggingSettings.FileName,
                MaxFileSizeKilobytes = profile.LoggingSettings.MaxFileSizeKilobytes,
                LogDroppedPackets = profile.LoggingSettings.LogDroppedPackets,
                LogSuccessfulConnections = profile.LoggingSettings.LogSuccessfulConnections
            }
        };

        foreach (NetworkConnectionItem connection in profile.ProtectedNetworkConnections)
        {
            clone.ProtectedNetworkConnections.Add(new NetworkConnectionItem
            {
                Name = connection.Name,
                IsSelected = connection.IsSelected
            });
        }

        return clone;
    }

    private async System.Threading.Tasks.Task ShowErrorDialogAsync(string title, string message)
    {
        await _dialogSemaphore.WaitAsync();
        try
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
        finally
        {
            _dialogSemaphore.Release();
        }
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void WindowsFirewallPropertiesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        _firewallChangeSubscription?.Dispose();
        _firewallChangeSubscription = null;
        Unloaded -= WindowsFirewallPropertiesPage_Unloaded;
    }

    private FirewallPropertiesSnapshot CreateSnapshot()
        => new(
            _firewallProfileService.GetProfiles(),
            _firewallProfileService.GetIpsecDefaults(),
            _firewallProfileService.GetTunnelAuthorizationSettings());

    private void ApplySystemState(FirewallPropertiesSnapshot snapshot)
    {
        bool wasApplyingSystemState = _isApplyingSystemState;
        _isApplyingSystemState = true;
        try
        {
            ApplyProfiles(snapshot.Profiles);
            ApplyIpsecSettings(snapshot.IpsecDefaults, snapshot.TunnelAuthorization);
        }
        finally
        {
            _isApplyingSystemState = wasApplyingSystemState;
        }
    }

    private void OnFirewallConfigurationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await RefreshFromSystemChangeAsync();
        });
    }

    private async Task RefreshFromSystemChangeAsync()
    {
        if (!_isPageInitialized || _isRefreshingFromSystemChange || _dialogSemaphore.CurrentCount == 0)
        {
            return;
        }

        _isRefreshingFromSystemChange = true;
        try
        {
            FirewallPropertiesSnapshot snapshot = await Task.Run(() => CreateSnapshot());
            ApplySystemState(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh Windows Firewall properties after a system change.");
        }
        finally
        {
            _isRefreshingFromSystemChange = false;
        }
    }

    private sealed record FirewallPropertiesSnapshot(
        IReadOnlyList<FirewallProfileModel> Profiles,
        IpsecDefaultsModel IpsecDefaults,
        FirewallTunnelAuthorizationSettings TunnelAuthorization);
}
