using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Network;

public sealed partial class InterfaceTypesDialog : UserControl
{
    private readonly Action<ElementTheme> _themeChangedHandler;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public bool IsLan => LanCheckBox.IsChecked == true;
    public bool IsWireless => WirelessCheckBox.IsChecked == true;
    public bool IsRemoteAccess => RemoteAccessCheckBox.IsChecked == true;
    public bool IsAllInterfaces => AllInterfacesRadio.IsChecked == true;

    public string SelectedInterfaceTypes
    {
        get
        {
            if (IsAllInterfaces)
            {
                return "All";
            }

            List<string> values = [];
            if (IsLan)
            {
                values.Add("Lan");
            }

            if (IsWireless)
            {
                values.Add("Wireless");
            }

            if (IsRemoteAccess)
            {
                values.Add("RemoteAccess");
            }

            return values.Count == 0 ? "All" : string.Join(",", values);
        }
    }

    public InterfaceTypesDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += InterfaceTypesDialog_Loaded;
        Unloaded += InterfaceTypesDialog_Unloaded;

        SpecificInterfacesRadio.Checked += (_, _) => SetInterfaceCheckboxes(true);
        AllInterfacesRadio.Checked += (_, _) => SetInterfaceCheckboxes(false);
        SetInterfaceCheckboxes(false);
    }

    public void ApplyInterfaceTypes(string interfaceTypes)
    {
        string[] values = interfaceTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (values.Length == 0 || values.Any(value => string.Equals(value, "All", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase)))
        {
            AllInterfacesRadio.IsChecked = true;
            SetInterfaceCheckboxes(false);
            return;
        }

        SpecificInterfacesRadio.IsChecked = true;
        SetInterfaceCheckboxes(true);
        LanCheckBox.IsChecked = values.Any(value => string.Equals(value, "LAN", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(value, "Lan", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(value, "Wired", StringComparison.OrdinalIgnoreCase));
        WirelessCheckBox.IsChecked = values.Any(value => string.Equals(value, "Wireless", StringComparison.OrdinalIgnoreCase));
        RemoteAccessCheckBox.IsChecked = values.Any(value => string.Equals(value, "RemoteAccess", StringComparison.OrdinalIgnoreCase));
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_InterfaceTypes_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 520,
            Height = 360
        });

        return modalWindow.ShowDialogAsync();
    }

    private void InterfaceTypesDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void InterfaceTypesDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private void SetInterfaceCheckboxes(bool enabled)
    {
        LanCheckBox.IsEnabled = enabled;
        WirelessCheckBox.IsEnabled = enabled;
        RemoteAccessCheckBox.IsEnabled = enabled;
    }
}
