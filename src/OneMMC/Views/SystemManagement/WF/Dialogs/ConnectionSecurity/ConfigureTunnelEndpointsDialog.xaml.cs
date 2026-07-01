using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.Dialogs.Network;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.ConnectionSecurity;

public sealed partial class ConfigureTunnelEndpointsDialog : UserControl
{
    private readonly ObservableCollection<string> _endpoint1Computers = [];
    private readonly ObservableCollection<string> _endpoint2Computers = [];
    private readonly Action<ElementTheme> _themeChangedHandler;

    private string _tunnelMode = "CustomConfiguration";
    private string _requirementsTag = string.Empty;

    public IReadOnlyList<string> Endpoint1Computers => _endpoint1Computers.ToList();
    public IReadOnlyList<string> Endpoint2Computers => _endpoint2Computers.ToList();
    public string LocalTunnelIpv4 => LocalTunnelIpv4TextBox.Text.Trim();
    public string LocalTunnelIpv6 => LocalTunnelIpv6TextBox.Text.Trim();
    public string RemoteTunnelIpv4 => RemoteTunnelIpv4TextBox.Text.Trim();
    public string RemoteTunnelIpv6 => RemoteTunnelIpv6TextBox.Text.Trim();
    public bool ApplyIpsecAuthorization => ApplyIpsecAuthorizationCheckBox.IsChecked == true;
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ConfigureTunnelEndpointsDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += ConfigureTunnelEndpointsDialog_Loaded;
        Unloaded += ConfigureTunnelEndpointsDialog_Unloaded;

        Endpoint1ComputersListView.ItemsSource = _endpoint1Computers;
        Endpoint2ComputersListView.ItemsSource = _endpoint2Computers;
    }

    public void ApplyState(
        IEnumerable<string> endpoint1Computers,
        IEnumerable<string> endpoint2Computers,
        string localTunnelIpv4,
        string localTunnelIpv6,
        string remoteTunnelIpv4,
        string remoteTunnelIpv6,
        bool applyIpsecAuthorization,
        string tunnelMode,
        string requirementsTag)
    {
        _endpoint1Computers.Clear();
        foreach (string endpoint1Computer in endpoint1Computers.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            _endpoint1Computers.Add(endpoint1Computer.Trim());
        }

        _endpoint2Computers.Clear();
        foreach (string endpoint2Computer in endpoint2Computers.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            _endpoint2Computers.Add(endpoint2Computer.Trim());
        }

        LocalTunnelIpv4TextBox.Text = localTunnelIpv4;
        LocalTunnelIpv6TextBox.Text = localTunnelIpv6;
        RemoteTunnelIpv4TextBox.Text = remoteTunnelIpv4;
        RemoteTunnelIpv6TextBox.Text = remoteTunnelIpv6;
        ApplyIpsecAuthorizationCheckBox.IsChecked = applyIpsecAuthorization;

        _tunnelMode = tunnelMode;
        _requirementsTag = requirementsTag;
        ApplyTunnelModeLayout();
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_Common_ConfigureTunnelEndpoints,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 950,
            Height = 700
        });

        return modalWindow.ShowDialogAsync();
    }

    private void ApplyTunnelModeLayout()
    {
        bool doNotAuthenticate = string.Equals(_requirementsTag, "DoNotAuthenticate", StringComparison.Ordinal);
        SetLocalTunnelEndpointEnabled(true);
        SetRemoteTunnelEndpointEnabled(true);

        switch (_tunnelMode)
        {
            case "ClientToGateway":
                // Client-to-gateway: ClientTop → RemoteTunnel → Endpoint2 (as "remote endpoints")
                DescriptionTextBlock.Text = LocalizedStrings.WF_TunnelEndpoints_ClientToGatewayDescription;
                ClientTopSection.Visibility = Visibility.Visible;
                Endpoint1Section.Visibility = Visibility.Collapsed;
                LocalTunnelSection.Visibility = Visibility.Collapsed;
                IpsecAuthorizationSection.Visibility = Visibility.Collapsed;
                RemoteTunnelSection.Visibility = Visibility.Visible;
                Endpoint2Section.Visibility = Visibility.Visible;
                Endpoint2HeaderTextBlock.Text = LocalizedStrings.WF_TunnelEndpoints_ClientToGatewayRemoteHeader;
                ClientBottomSection.Visibility = Visibility.Collapsed;
                SetRemoteTunnelEndpointEnabled(!doNotAuthenticate);
                break;

            case "GatewayToClient":
                // Gateway-to-client: Endpoint1 (as "local endpoints") → LocalTunnel → IPsecAuth → ClientBottom
                DescriptionTextBlock.Text = LocalizedStrings.WF_TunnelEndpoints_GatewayToClientDescription;
                ClientTopSection.Visibility = Visibility.Collapsed;
                Endpoint1Section.Visibility = Visibility.Visible;
                Endpoint1HeaderTextBlock.Text = LocalizedStrings.WF_TunnelEndpoints_GatewayToClientLocalHeader;
                LocalTunnelSection.Visibility = Visibility.Visible;
                IpsecAuthorizationSection.Visibility = Visibility.Visible;
                RemoteTunnelSection.Visibility = Visibility.Collapsed;
                Endpoint2Section.Visibility = Visibility.Collapsed;
                ClientBottomSection.Visibility = Visibility.Visible;
                break;

            default:
                // CustomConfiguration: full layout
                DescriptionTextBlock.Text = LocalizedStrings.WF_TunnelEndpoints_DefaultDescription;
                ClientTopSection.Visibility = Visibility.Collapsed;
                Endpoint1Section.Visibility = Visibility.Visible;
                Endpoint1HeaderTextBlock.Text = LocalizedStrings.WF_TunnelEndpoints_Endpoint1Header;
                LocalTunnelSection.Visibility = Visibility.Visible;
                IpsecAuthorizationSection.Visibility = Visibility.Visible;
                RemoteTunnelSection.Visibility = Visibility.Visible;
                Endpoint2Section.Visibility = Visibility.Visible;
                Endpoint2HeaderTextBlock.Text = LocalizedStrings.WF_TunnelEndpoints_Endpoint2Header;
                ClientBottomSection.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void SetLocalTunnelEndpointEnabled(bool enabled)
    {
        LocalTunnelIpv4TextBox.IsEnabled = enabled;
        LocalTunnelIpv6TextBox.IsEnabled = enabled;
        EditLocalTunnelButton.IsEnabled = enabled;
    }

    private void SetRemoteTunnelEndpointEnabled(bool enabled)
    {
        RemoteTunnelIpv4TextBox.IsEnabled = enabled;
        RemoteTunnelIpv6TextBox.IsEnabled = enabled;
        EditRemoteTunnelButton.IsEnabled = enabled;

        if (!enabled)
        {
            RemoteTunnelIpv4TextBox.Text = string.Empty;
            RemoteTunnelIpv6TextBox.Text = string.Empty;
        }
    }

    private void ConfigureTunnelEndpointsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void ConfigureTunnelEndpointsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private Task<WindowDialogResult> ShowIpAddressDialogAsync(IPAddressEntryDialog dialog)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_IPAddress_Title,
            Content = dialog,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 620,
            Height = 560,
            OnPrimaryButtonClick = () =>
            {
                dialog.CommitResult();
                return true;
            }
        });

        return modalWindow.ShowDialogAsync();
    }

    private async Task EditEndpointAddressPairAsync(string title, TextBox ipv4TextBox, TextBox ipv6TextBox)
    {
        var editor = new StackPanel { MinWidth = 460, Spacing = 10 };
        editor.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.WF_TunnelEndpoints_DialogDescription,
            TextWrapping = TextWrapping.Wrap
        });

        editor.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.WF_TunnelEndpoints_AuthModeNote,
            TextWrapping = TextWrapping.Wrap
        });

        var anyAddressRadio = new RadioButton
        {
            Content = LocalizedStrings.WF_Common_AnyAddressForDynamicTunnelConfiguration,
            GroupName = "TunnelEndpointAddressMode"
        };

        var specificAddressRadio = new RadioButton
        {
            Content = LocalizedStrings.WF_Common_SpecificAddress,
            GroupName = "TunnelEndpointAddressMode"
        };

        var ipv4Label = new TextBlock { Text = LocalizedStrings.WF_Common_IPv4Address };
        var ipv4Input = new TextBox { Text = ipv4TextBox.Text };
        var ipv6Label = new TextBlock { Text = LocalizedStrings.WF_Common_IPv6Address };
        var ipv6Input = new TextBox { Text = ipv6TextBox.Text };

        bool hasSpecificAddress = !IsAnyTunnelEndpointValue(ipv4TextBox.Text) ||
                                  !IsAnyTunnelEndpointValue(ipv6TextBox.Text);
        anyAddressRadio.IsChecked = !hasSpecificAddress;
        specificAddressRadio.IsChecked = hasSpecificAddress;

        void UpdateAddressInputs(bool enabled)
        {
            ipv4Input.IsEnabled = enabled;
            ipv6Input.IsEnabled = enabled;
        }

        anyAddressRadio.Checked += (_, _) => UpdateAddressInputs(false);
        specificAddressRadio.Checked += (_, _) => UpdateAddressInputs(true);

        editor.Children.Add(anyAddressRadio);
        editor.Children.Add(specificAddressRadio);
        editor.Children.Add(ipv4Label);
        editor.Children.Add(ipv4Input);
        editor.Children.Add(ipv6Label);
        editor.Children.Add(ipv6Input);
        UpdateAddressInputs(hasSpecificAddress);

        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 760,
            Height = 420
        });

        WindowDialogResult result = await modalWindow.ShowDialogAsync();
        if (result == WindowDialogResult.Primary)
        {
            bool useSpecificAddress = specificAddressRadio.IsChecked == true;
            ipv4TextBox.Text = useSpecificAddress ? NormalizeTunnelEndpointText(ipv4Input.Text) : string.Empty;
            ipv6TextBox.Text = useSpecificAddress ? NormalizeTunnelEndpointText(ipv6Input.Text) : string.Empty;
        }
    }

    private static bool IsAnyTunnelEndpointValue(string value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "*", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTunnelEndpointText(string value)
        => IsAnyTunnelEndpointValue(value) ? string.Empty : value.Trim();

    private async void Endpoint1AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        WindowDialogResult result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            _endpoint1Computers.Add(dialog.ResultValue);
        }
    }

    private async void Endpoint1EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint1ComputersListView.SelectedItem is not string selected)
        {
            return;
        }

        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        dialog.SetExistingValue(selected);
        WindowDialogResult result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            int index = _endpoint1Computers.IndexOf(selected);
            if (index >= 0)
            {
                _endpoint1Computers[index] = dialog.ResultValue;
            }
        }
    }

    private void Endpoint1RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint1ComputersListView.SelectedItem is string selected)
        {
            _endpoint1Computers.Remove(selected);
        }
    }

    private async void Endpoint2AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        WindowDialogResult result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            _endpoint2Computers.Add(dialog.ResultValue);
        }
    }

    private async void Endpoint2EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint2ComputersListView.SelectedItem is not string selected)
        {
            return;
        }

        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        dialog.SetExistingValue(selected);
        WindowDialogResult result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            int index = _endpoint2Computers.IndexOf(selected);
            if (index >= 0)
            {
                _endpoint2Computers[index] = dialog.ResultValue;
            }
        }
    }

    private void Endpoint2RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint2ComputersListView.SelectedItem is string selected)
        {
            _endpoint2Computers.Remove(selected);
        }
    }

    private async void EditLocalTunnelButton_Click(object sender, RoutedEventArgs e)
    {
        await EditEndpointAddressPairAsync(LocalizedStrings.WF_TunnelEndpoints_LocalEndpointTitle, LocalTunnelIpv4TextBox, LocalTunnelIpv6TextBox);
    }

    private async void EditRemoteTunnelButton_Click(object sender, RoutedEventArgs e)
    {
        await EditEndpointAddressPairAsync(LocalizedStrings.WF_TunnelEndpoints_RemoteEndpointTitle, RemoteTunnelIpv4TextBox, RemoteTunnelIpv6TextBox);
    }
}
