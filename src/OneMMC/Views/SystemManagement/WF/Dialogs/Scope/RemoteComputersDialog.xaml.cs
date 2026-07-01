using System;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Scope;

public sealed partial class RemoteComputersDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly bool _isSecureConnection;
    private readonly Action<ElementTheme> _themeChangedHandler;

    public string AuthorizedSddl { get; private set; } = string.Empty;
    public string ExceptionSddl { get; private set; } = string.Empty;

    /// <summary>
    /// When true, the dialog title shows "Manage Remote Endpoints" (CSR mode).
    /// When false, shows "Manage Authorized Computers" (standard rule mode).
    /// </summary>
    public RemoteComputersDialog(bool isCsrMode = false, bool isSecureConnection = false)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += RemoteComputersDialog_Loaded;
        Unloaded += RemoteComputersDialog_Unloaded;
        PrimaryButtonClick += RemoteComputersDialog_PrimaryButtonClick;

        Title = isCsrMode ? LocalizedStrings.WF_CSR_RemoteEndpoints_Manage : LocalizedStrings.WF_RemoteComputersDialog_Title;
        _isSecureConnection = isSecureConnection;

        // Show InfoBar and disable checkboxes when secure connection is not enabled
        if (!_isSecureConnection)
        {
            SecureConnectionInfoBar.IsOpen = true;
            AuthorizedCheckBox.IsEnabled = false;
            ExceptionCheckBox.IsEnabled = false;
        }
        else
        {
            SecureConnectionInfoBar.IsOpen = false;
            AuthorizedCheckBox.IsEnabled = true;
            ExceptionCheckBox.IsEnabled = true;
        }
    }

    private void RemoteComputersDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    public void ApplySddl(string authorizedSddl, string exceptionSddl)
    {
        AuthorizedSddl = authorizedSddl;
        ExceptionSddl = exceptionSddl;

        var authorizedItems = WindowsFirewallSupport.ParseAuthorizationSddl(authorizedSddl, allowEntries: true);
        if (authorizedItems.Count > 0)
        {
            AuthorizedCheckBox.IsChecked = true;
            foreach (var item in authorizedItems)
            {
                AddItem(AuthorizedListView, item.Name);
            }
        }

        var exceptionItems = WindowsFirewallSupport.ParseAuthorizationSddl(exceptionSddl, allowEntries: false);
        if (exceptionItems.Count == 0 && !string.IsNullOrWhiteSpace(exceptionSddl) &&
            !string.Equals(exceptionSddl, "None", StringComparison.OrdinalIgnoreCase))
        {
            exceptionItems = WindowsFirewallSupport.ParseAuthorizationSddl(exceptionSddl, allowEntries: true);
        }

        if (exceptionItems.Count > 0)
        {
            ExceptionCheckBox.IsChecked = true;
            foreach (var item in exceptionItems)
            {
                AddItem(ExceptionListView, item.Name);
            }
        }
    }

    private void RemoteComputersDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if ((AuthorizedCheckBox.IsChecked == true && AuthorizedListView.Items.Count == 0) ||
            (ExceptionCheckBox.IsChecked == true && ExceptionListView.Items.Count == 0))
        {
            ValidationInfoBar.Message = LocalizedStrings.WF_RemoteComputersDialog_SelectionRequired;
            ValidationInfoBar.IsOpen = true;
            args.Cancel = true;
            return;
        }

        ValidationInfoBar.IsOpen = false;
        var authorizedItems = BuildItemList(AuthorizedListView, AuthorizedCheckBox.IsChecked == true);
        var exceptionItems = BuildItemList(ExceptionListView, ExceptionCheckBox.IsChecked == true);

        AuthorizedSddl = WindowsFirewallSupport.BuildAuthorizationSddl(authorizedItems, null);
        ExceptionSddl = WindowsFirewallSupport.BuildAuthorizationSddl(null, exceptionItems);
    }

    private static List<TunnelAuthorizationItem>? BuildItemList(ListView listView, bool isActive)
    {
        if (!isActive)
        {
            return null;
        }

        List<TunnelAuthorizationItem> items = [];
        foreach (object item in listView.Items)
        {
            string value = item switch
            {
                string s => s,
                ListViewItem lvi => lvi.Content?.ToString() ?? string.Empty,
                _ => item?.ToString() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            items.Add(new TunnelAuthorizationItem { Name = value });
        }

        return items;
    }

    private void RemoteComputersDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private void AuthorizedCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        ValidationInfoBar.IsOpen = false;
        AuthorizedListView.IsEnabled = true;
        AuthAddButton.IsEnabled = true;
        AuthDeleteButton.IsEnabled = true;
    }

    private void AuthorizedCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        ValidationInfoBar.IsOpen = false;
        AuthorizedListView.IsEnabled = false;
        AuthAddButton.IsEnabled = false;
        AuthDeleteButton.IsEnabled = false;
    }

    private void ExceptionCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        ValidationInfoBar.IsOpen = false;
        ExceptionListView.IsEnabled = true;
        ExcAddButton.IsEnabled = true;
        ExcDeleteButton.IsEnabled = true;
    }

    private void ExceptionCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        ValidationInfoBar.IsOpen = false;
        ExceptionListView.IsEnabled = false;
        ExcAddButton.IsEnabled = false;
        ExcDeleteButton.IsEnabled = false;
    }

    private void AuthAddButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(AuthorizedListView);
        ValidationInfoBar.IsOpen = false;
    }

    private void AuthDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedItem(AuthorizedListView);
        ValidationInfoBar.IsOpen = false;
    }

    private void ExcAddButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(ExceptionListView);
        ValidationInfoBar.IsOpen = false;
    }

    private void ExcDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedItem(ExceptionListView);
        ValidationInfoBar.IsOpen = false;
    }

    private static void AddDirectoryObjects(ListView targetList)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        List<DirectoryObject>? selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            new DirectoryObjectPickerOptions
            {
                Types = ObjectPickerTypes.Groups,
                MultiSelect = true,
                IncludeLocalComputerScope = true,
                IncludeDomainScopes = true,
                IncludeWorkgroupScope = false,
                IncludeUserEnteredScopes = false,
                IncludeWellKnownPrincipals = true,
                IncludeDownlevelWellKnownPrincipals = true,
                BuiltInPrincipalsOnly = true
            });

        if (selections is not { Count: > 0 })
        {
            return;
        }

        foreach (DirectoryObject selection in selections)
        {
            string resolvedName = string.IsNullOrWhiteSpace(selection.Name)
                ? selection.Sid?.Trim() ?? string.Empty
                : selection.Name.Trim();

            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                continue;
            }

            bool exists = targetList.Items.Cast<object>()
                .Select(item => item switch
                {
                    string stringItem => stringItem,
                    ListViewItem listViewItem => listViewItem.Content?.ToString() ?? string.Empty,
                    _ => item?.ToString() ?? string.Empty
                })
                .Any(item => string.Equals(item, resolvedName, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                targetList.Items.Add(resolvedName);
            }
        }
    }

    private static void AddItem(ListView targetList, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        bool exists = targetList.Items.Cast<object>()
            .Select(item => item switch
            {
                string stringItem => stringItem,
                ListViewItem listViewItem => listViewItem.Content?.ToString() ?? string.Empty,
                _ => item?.ToString() ?? string.Empty
            })
            .Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            targetList.Items.Add(value);
        }
    }

    private static void RemoveSelectedItem(ListView listView)
    {
        if (listView.SelectedItem is object selectedItem)
        {
            listView.Items.Remove(selectedItem);
        }
    }
}

