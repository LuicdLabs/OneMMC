using System;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace OneMMC.Views.Dialogs.ConnectionSecurity;

public sealed partial class ConfigureIPsecDialog : UserControl
{
    private bool _isLocalEdit;
    private Flyout? _specificAddressFlyout;

    public bool UseIpsecTunnel => UseTunnelCheckBox.IsChecked == true;

    public bool ApplyAuthorization => ApplyAuthorizationCheckBox.IsChecked == true;

    public bool ExemptIpsecProtectedConnections => ExemptIpsecCheckBox.IsChecked == true;

    public string LocalIpv4Address => LocalIPv4Box.Text.Trim();

    public string LocalIpv6Address => LocalIPv6Box.Text.Trim();

    public string RemoteIpv4Address => RemoteIPv4Box.Text.Trim();

    public string RemoteIpv6Address => RemoteIPv6Box.Text.Trim();

    public string LocalTunnelEndpointExpression => BuildEndpointExpression(LocalIpv4Address, LocalIpv6Address);

    public string RemoteTunnelEndpointExpression => BuildEndpointExpression(RemoteIpv4Address, RemoteIpv6Address);

    public ConfigureIPsecDialog()
    {
        InitializeComponent();
        _specificAddressFlyout = (Flyout)Resources["SpecificAddressFlyout"];
    }

    public void ApplyFromRule(ConnectionSecurityRuleModel rule)
    {
        bool useTunnel = rule.Mode == ConnectionSecurityMode.Tunnel;
        UseTunnelCheckBox.IsChecked = useTunnel;
        SetTunnelMode(useTunnel);

        ApplyAuthorizationCheckBox.IsChecked = rule.RequireAuthorization;
        ExemptIpsecCheckBox.IsChecked = rule.BypassTunnelIfEncrypted;

        ApplyEndpointExpression(rule.LocalTunnelEndpoint, LocalIPv4Box, LocalIPv6Box);
        ApplyEndpointExpression(rule.RemoteTunnelEndpoint, RemoteIPv4Box, RemoteIPv6Box);
    }

    private void SetTunnelMode(bool enabled)
    {
        ApplyAuthorizationCheckBox.IsEnabled = enabled;
        ExemptIpsecCheckBox.IsEnabled = enabled;
        LocalIPv4Box.IsEnabled = enabled;
        LocalIPv6Box.IsEnabled = enabled;
        LocalEditButton.IsEnabled = enabled;
        RemoteIPv4Box.IsEnabled = enabled;
        RemoteIPv6Box.IsEnabled = enabled;
        RemoteEditButton.IsEnabled = enabled;
    }

    private void UseTunnelCheckBox_Checked(object sender, RoutedEventArgs e) => SetTunnelMode(true);
    private void UseTunnelCheckBox_Unchecked(object sender, RoutedEventArgs e) => SetTunnelMode(false);

    private void LocalAnyAddress_Click(object sender, RoutedEventArgs e)
    {
        LocalIPv4Box.Text = string.Empty;
        LocalIPv6Box.Text = string.Empty;
    }

    private void LocalSpecificAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_specificAddressFlyout is null) return;

        _isLocalEdit = true;
        FlyoutIPv4Box.Text = LocalIPv4Box.Text;
        FlyoutIPv6Box.Text = LocalIPv6Box.Text;
        _specificAddressFlyout.ShowAt(LocalEditButton);
    }

    private void RemoteAnyAddress_Click(object sender, RoutedEventArgs e)
    {
        RemoteIPv4Box.Text = string.Empty;
        RemoteIPv6Box.Text = string.Empty;
    }

    private void RemoteSpecificAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_specificAddressFlyout is null) return;

        _isLocalEdit = false;
        FlyoutIPv4Box.Text = RemoteIPv4Box.Text;
        FlyoutIPv6Box.Text = RemoteIPv6Box.Text;
        _specificAddressFlyout.ShowAt(RemoteEditButton);
    }

    private void FlyoutOk_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocalEdit)
        {
            LocalIPv4Box.Text = FlyoutIPv4Box.Text;
            LocalIPv6Box.Text = FlyoutIPv6Box.Text;
        }
        else
        {
            RemoteIPv4Box.Text = FlyoutIPv4Box.Text;
            RemoteIPv6Box.Text = FlyoutIPv6Box.Text;
        }
        _specificAddressFlyout?.Hide();
    }

    private void FlyoutCancel_Click(object sender, RoutedEventArgs e)
    {
        _specificAddressFlyout?.Hide();
    }

    private static string BuildEndpointExpression(string ipv4, string ipv6)
    {
        var values = new[] { ipv4, ipv6 }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return values.Length == 0 ? string.Empty : string.Join(",", values);
    }

    private static void ApplyEndpointExpression(string expression, TextBox ipv4Box, TextBox ipv6Box)
    {
        ipv4Box.Text = string.Empty;
        ipv6Box.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(expression) ||
            string.Equals(expression, "Any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expression, "*", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string value in expression
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (value.Contains(':', StringComparison.Ordinal))
            {
                ipv6Box.Text = string.IsNullOrWhiteSpace(ipv6Box.Text)
                    ? value
                    : string.Join(",", ipv6Box.Text, value);
            }
            else
            {
                ipv4Box.Text = string.IsNullOrWhiteSpace(ipv4Box.Text)
                    ? value
                    : string.Join(",", ipv4Box.Text, value);
            }
        }
    }
}
