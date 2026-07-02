using System;
using System.Collections.ObjectModel;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.ConnectionSecurity;

public sealed partial class ConnectionSecurityRulePropertiesDialog : ContentDialog
{
    private readonly Action<ElementTheme> _themeChangedHandler;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ConnectionSecurityRulePropertiesDialog()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += ConnectionSecurityRulePropertiesDialog_Loaded;
        Unloaded += ConnectionSecurityRulePropertiesDialog_Unloaded;

        // Select General tab by default
        TabBar.SelectedItem = GeneralTab;

        FirstAuthListView.ItemsSource = new ObservableCollection<AuthMethodItem>();
        SecondAuthListView.ItemsSource = new ObservableCollection<AuthMethodItem>();
    }

    private void ConnectionSecurityRulePropertiesDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void ConnectionSecurityRulePropertiesDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    /// <summary>
    /// Populate the dialog from a rule model.
    /// </summary>
    public void LoadRule(ConnectionSecurityRuleModel rule)
    {
        RuleNameHeader.Text = rule.Name;
        Ep1IpValue.Text = rule.Endpoint1Expression;
        Ep1PortValue.Text = rule.LocalPort;
        Ep2IpValue.Text = rule.Endpoint2Expression;
        Ep2PortValue.Text = rule.RemotePort;
        ProtocolValue.Text = rule.Protocol;
        ProfileValue.Text = rule.ProfileDisplay;
        RequirementsValue.Text = $"{GetSecurityRequirementDisplay(rule.InboundSecurity)}/{GetSecurityRequirementDisplay(rule.OutboundSecurity)}";
        LocalTunnelValue.Text = rule.LocalTunnelEndpoint;
        RemoteTunnelValue.Text = rule.RemoteTunnelEndpoint;
        InterfaceTypesValue.Text = rule.InterfaceTypes;
        ApplyAuthValue.Text = rule.RequireAuthorization ? LocalizedStrings.WF_Common_Yes : LocalizedStrings.WF_Common_No;
        ExemptIpsecValue.Text = rule.BypassTunnelIfEncrypted ? LocalizedStrings.WF_Common_Yes : LocalizedStrings.WF_Common_No;

        FirstAuthListView.ItemsSource = new ObservableCollection<AuthMethodItem>(
        [
            .. rule.FirstAuthMethods.Select(item => new AuthMethodItem
            {
                Name = item.Method,
                Description = item.Details
            })
        ]);
        SecondAuthListView.ItemsSource = new ObservableCollection<AuthMethodItem>(
        [
            .. rule.SecondAuthMethods.Select(item => new AuthMethodItem
            {
                Name = item.Method,
                Description = item.Details
            })
        ]);
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        GeneralPanel.Visibility = Visibility.Collapsed;
        AuthenticationPanel.Visibility = Visibility.Collapsed;
        AdvancedPanel.Visibility = Visibility.Collapsed;

        if (sender.SelectedItem == GeneralTab)
            GeneralPanel.Visibility = Visibility.Visible;
        else if (sender.SelectedItem == AuthenticationTab)
            AuthenticationPanel.Visibility = Visibility.Visible;
        else if (sender.SelectedItem == AdvancedTab)
            AdvancedPanel.Visibility = Visibility.Visible;
    }

    private string GetSecurityRequirementDisplay(ConnectionSecurityRequirement requirement)
        => requirement switch
        {
            ConnectionSecurityRequirement.Request => LocalizedStrings.WF_SecurityRequirement_Request,
            ConnectionSecurityRequirement.Require => LocalizedStrings.WF_SecurityRequirement_Require,
            _ => LocalizedStrings.WF_SecurityRequirement_None
        };

}

/// <summary>
/// Auth method row shown in the rule properties dialog. Namespace-level so it can be
/// referenced from XAML via x:DataType for compiled (AOT-safe) bindings.
/// </summary>
public class AuthMethodItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
