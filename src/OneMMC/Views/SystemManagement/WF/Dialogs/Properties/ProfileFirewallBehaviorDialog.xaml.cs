using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.WFProperties;

public sealed partial class ProfileFirewallBehaviorDialog : ContentDialog
{
    public FirewallProfileModel Profile { get; }
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ProfileFirewallBehaviorDialog(string profileName, FirewallProfileModel profile)
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += ProfileFirewallBehaviorDialog_Unloaded;
        PrimaryButtonClick += ProfileFirewallBehaviorDialog_PrimaryButtonClick;

        Profile = profile;
        Title = string.Format(System.Globalization.CultureInfo.CurrentCulture, LocalizedStrings.WF_ProfileFirewallBehavior_TitleFormat, profileName);
        NotificationComboBox.SelectedIndex = profile.NotificationsDisabled ? 1 : 0;
        UnicastResponseComboBox.SelectedIndex = profile.UnicastResponsesToMulticastBroadcastDisabled ? 1 : 0;
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void ProfileFirewallBehaviorDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }

    private void ProfileFirewallBehaviorDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Profile.NotificationsDisabled = NotificationComboBox.SelectedIndex == 1;
        Profile.UnicastResponsesToMulticastBroadcastDisabled = UnicastResponseComboBox.SelectedIndex == 1;
    }
}
