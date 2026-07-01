using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Network;

public sealed partial class ScopeIPAddressDialog : UserControl
{
    private readonly ObservableCollection<string> _localAddresses = [];
    private readonly ObservableCollection<string> _remoteAddresses = [];
    private readonly Action<ElementTheme> _themeChangedHandler;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public IReadOnlyList<string> LocalAddresses => _localAddresses.ToList();
    public IReadOnlyList<string> RemoteAddresses => _remoteAddresses.ToList();
    public string LocalAddressExpression => LocalAnyRadio.IsChecked == true ? "*" : string.Join(",", _localAddresses);
    public string RemoteAddressExpression => RemoteAnyRadio.IsChecked == true ? "*" : string.Join(",", _remoteAddresses);

    public ScopeIPAddressDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += ScopeIPAddressDialog_Loaded;
        Unloaded += ScopeIPAddressDialog_Unloaded;

        LocalAddressListView.ItemsSource = _localAddresses;
        RemoteAddressListView.ItemsSource = _remoteAddresses;

        LocalSpecificRadio.Checked += (_, _) => SetLocalControlsEnabled(true);
        LocalAnyRadio.Checked += (_, _) => SetLocalControlsEnabled(false);
        RemoteSpecificRadio.Checked += (_, _) => SetRemoteControlsEnabled(true);
        RemoteAnyRadio.Checked += (_, _) => SetRemoteControlsEnabled(false);
    }

    public void ApplyAddressExpressions(string localAddressExpression, string remoteAddressExpression)
    {
        ApplyAddressExpression(localAddressExpression, LocalAnyRadio, LocalSpecificRadio, _localAddresses, SetLocalControlsEnabled);
        ApplyAddressExpression(remoteAddressExpression, RemoteAnyRadio, RemoteSpecificRadio, _remoteAddresses, SetRemoteControlsEnabled);
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_ManageIPAddresses_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 920,
            Height = 700
        });

        return modalWindow.ShowDialogAsync();
    }

    private void ScopeIPAddressDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void ScopeIPAddressDialog_Unloaded(object sender, RoutedEventArgs e)
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

    private void SetLocalControlsEnabled(bool enabled)
    {
        LocalAddressListView.IsEnabled = enabled;
        LocalAddButton.IsEnabled = enabled;
        LocalEditButton.IsEnabled = enabled;
        LocalDeleteButton.IsEnabled = enabled;
    }

    private void SetRemoteControlsEnabled(bool enabled)
    {
        RemoteAddressListView.IsEnabled = enabled;
        RemoteAddButton.IsEnabled = enabled;
        RemoteEditButton.IsEnabled = enabled;
        RemoteDeleteButton.IsEnabled = enabled;
    }

    private async void LocalAddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IPAddressEntryDialog { ShowPredefined = false };
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
            _localAddresses.Add(dialog.ResultValue);
    }

    private async void LocalEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (LocalAddressListView.SelectedItem is not string selected) return;
        var dialog = new IPAddressEntryDialog { ShowPredefined = false };
        dialog.SetExistingValue(selected);
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            var idx = _localAddresses.IndexOf(selected);
            if (idx >= 0) _localAddresses[idx] = dialog.ResultValue;
        }
    }

    private void LocalDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (LocalAddressListView.SelectedItem is string selected)
            _localAddresses.Remove(selected);
    }

    private async void RemoteAddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
            _remoteAddresses.Add(dialog.ResultValue);
    }

    private async void RemoteEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteAddressListView.SelectedItem is not string selected) return;
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        dialog.SetExistingValue(selected);
        var result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            var idx = _remoteAddresses.IndexOf(selected);
            if (idx >= 0) _remoteAddresses[idx] = dialog.ResultValue;
        }
    }

    private void RemoteDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteAddressListView.SelectedItem is string selected)
            _remoteAddresses.Remove(selected);
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
