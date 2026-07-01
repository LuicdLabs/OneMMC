using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.Dialogs.Network;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.ConnectionSecurity;

public sealed partial class RemoteEndpointsDialog : UserControl
{
    private readonly ObservableCollection<string> _endpoint1Addresses = [];
    private readonly ObservableCollection<string> _endpoint2Addresses = [];
    private readonly Action<ElementTheme> _themeChangedHandler;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public IReadOnlyList<string> Endpoint1Addresses => _endpoint1Addresses.ToList();
    public IReadOnlyList<string> Endpoint2Addresses => _endpoint2Addresses.ToList();
    public string Endpoint1AddressExpression => Endpoint1AnyRadio.IsChecked == true ? "*" : string.Join(",", _endpoint1Addresses);
    public string Endpoint2AddressExpression => Endpoint2AnyRadio.IsChecked == true ? "*" : string.Join(",", _endpoint2Addresses);

    public RemoteEndpointsDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += RemoteEndpointsDialog_Loaded;
        Unloaded += RemoteEndpointsDialog_Unloaded;

        Endpoint1ListView.ItemsSource = _endpoint1Addresses;
        Endpoint2ListView.ItemsSource = _endpoint2Addresses;

        Endpoint1SpecificRadio.Checked += (_, _) => SetEndpoint1Enabled(true);
        Endpoint1AnyRadio.Checked += (_, _) => SetEndpoint1Enabled(false);
        Endpoint2SpecificRadio.Checked += (_, _) => SetEndpoint2Enabled(true);
        Endpoint2AnyRadio.Checked += (_, _) => SetEndpoint2Enabled(false);
    }

    public void ApplyAddressExpressions(string endpoint1AddressExpression, string endpoint2AddressExpression)
    {
        ApplyAddressExpression(endpoint1AddressExpression, Endpoint1AnyRadio, Endpoint1SpecificRadio, _endpoint1Addresses, SetEndpoint1Enabled);
        ApplyAddressExpression(endpoint2AddressExpression, Endpoint2AnyRadio, Endpoint2SpecificRadio, _endpoint2Addresses, SetEndpoint2Enabled);
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_ManageRemoteEndpoints_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 550,
            Height = 700
        });

        return modalWindow.ShowDialogAsync();
    }

    private void RemoteEndpointsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void RemoteEndpointsDialog_Unloaded(object sender, RoutedEventArgs e)
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

    private void SetEndpoint1Enabled(bool enabled)
    {
        Endpoint1ListView.IsEnabled = enabled;
        Endpoint1AddButton.IsEnabled = enabled;
        Endpoint1EditButton.IsEnabled = enabled;
        Endpoint1DeleteButton.IsEnabled = enabled;
    }

    private void SetEndpoint2Enabled(bool enabled)
    {
        Endpoint2ListView.IsEnabled = enabled;
        Endpoint2AddButton.IsEnabled = enabled;
        Endpoint2EditButton.IsEnabled = enabled;
        Endpoint2DeleteButton.IsEnabled = enabled;
    }

    private async void Endpoint1AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
            _endpoint1Addresses.Add(dialog.ResultValue);
    }

    private async void Endpoint1EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint1ListView.SelectedItem is not string selected) return;
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        dialog.SetExistingValue(selected);
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            var idx = _endpoint1Addresses.IndexOf(selected);
            if (idx >= 0) _endpoint1Addresses[idx] = dialog.ResultValue;
        }
    }

    private void Endpoint1DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint1ListView.SelectedItem is string selected)
            _endpoint1Addresses.Remove(selected);
    }

    private async void Endpoint2AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
            _endpoint2Addresses.Add(dialog.ResultValue);
    }

    private async void Endpoint2EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint2ListView.SelectedItem is not string selected) return;
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        dialog.SetExistingValue(selected);
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            var idx = _endpoint2Addresses.IndexOf(selected);
            if (idx >= 0) _endpoint2Addresses[idx] = dialog.ResultValue;
        }
    }

    private void Endpoint2DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Endpoint2ListView.SelectedItem is string selected)
            _endpoint2Addresses.Remove(selected);
    }

    private static void ApplyAddressExpression(
        string expression,
        RadioButton anyRadio,
        RadioButton specificRadio,
        ObservableCollection<string> target,
        Action<bool> setControlsEnabled)
    {
        target.Clear();

        if (IsAnyExpression(expression))
        {
            anyRadio.IsChecked = true;
            setControlsEnabled(false);
            return;
        }

        foreach (string value in expression
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }

        specificRadio.IsChecked = true;
        setControlsEnabled(true);
    }

    private static bool IsAnyExpression(string expression)
        => string.IsNullOrWhiteSpace(expression) ||
           string.Equals(expression, "Any", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(expression, "*", StringComparison.OrdinalIgnoreCase);
}
