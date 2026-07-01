using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
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
using Microsoft.UI.Xaml.Controls.Primitives;

namespace OneMMC.Views.Dialogs.Scope;

public sealed partial class PrincipalsDialog : ContentDialog
{
    public enum PrincipalsMode { LocalPrincipals, RemoteUsers }

    private readonly PrincipalsMode _mode;
    private readonly Action<ElementTheme> _themeChangedHandler;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public string AuthorizedSddl { get; private set; } = string.Empty;
    public string ExceptionSddl { get; private set; } = string.Empty;

    public PrincipalsDialog(PrincipalsMode mode, bool isSecureConnection = true)
    {
        InitializeComponent();
        _mode = mode;
        this.RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += PrincipalsDialog_Loaded;
        Unloaded += PrincipalsDialog_Unloaded;
        PrimaryButtonClick += PrincipalsDialog_PrimaryButtonClick;

        if (mode == PrincipalsMode.LocalPrincipals)
        {
            Title = LocalizedStrings.WF_Principals_LocalTitle;
            DescriptionText.Text = LocalizedStrings.WF_Principals_LocalDescription;
            AuthorizedLabel.Text = LocalizedStrings.WF_Principals_LocalAuthorized;
            ExceptionLabel.Text = LocalizedStrings.WF_Principals_LocalException;
            AuthAddDropDown.Visibility = Visibility.Visible;
            AuthAddButton.Visibility = Visibility.Collapsed;
            ExcAddDropDown.Visibility = Visibility.Visible;
            ExcAddButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            Title = LocalizedStrings.WF_Principals_RemoteTitle;
            DescriptionText.Text = LocalizedStrings.WF_Principals_RemoteDescription;
            AuthorizedLabel.Text = LocalizedStrings.WF_Principals_RemoteAuthorized;
            ExceptionLabel.Text = LocalizedStrings.WF_Principals_RemoteException;
            AuthAddDropDown.Visibility = Visibility.Collapsed;
            AuthAddButton.Visibility = Visibility.Visible;
            ExcAddDropDown.Visibility = Visibility.Collapsed;
            ExcAddButton.Visibility = Visibility.Visible;
        }

        SetSectionEnabled(AuthorizedSection, false);
        SetSectionEnabled(ExceptionSection, false);

        if (mode == PrincipalsMode.RemoteUsers && !isSecureConnection)
        {
            SecureConnectionInfoBar.IsOpen = true;
            OnlyAllowCheckBox.IsEnabled = false;
            SkipRuleCheckBox.IsEnabled = false;
            AuthAddButton.IsEnabled = false;
            AuthAddDropDown.IsEnabled = false;
            AuthDeleteButton.IsEnabled = false;
            ExcAddButton.IsEnabled = false;
            ExcAddDropDown.IsEnabled = false;
            ExcDeleteButton.IsEnabled = false;
        }
        else
        {
            SecureConnectionInfoBar.IsOpen = false;
        }
    }

    /// <summary>
    /// Populates the authorized and exception lists from SDDL strings.
    /// </summary>
    public void ApplySddl(string authorizedSddl, string exceptionSddl)
    {
        AuthorizedSddl = authorizedSddl;
        ExceptionSddl = exceptionSddl;

        var authorizedItems = WindowsFirewallSupport.ParseAuthorizationSddl(authorizedSddl, allowEntries: true);
        if (authorizedItems.Count > 0)
        {
            OnlyAllowCheckBox.IsChecked = true;
            SetSectionEnabled(AuthorizedSection, true);
            foreach (var item in authorizedItems)
            {
                AddItem(AuthorizedListView, item);
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
            SkipRuleCheckBox.IsChecked = true;
            SetSectionEnabled(ExceptionSection, true);
            foreach (var item in exceptionItems)
            {
                AddItem(ExceptionListView, item);
            }
        }
    }

    private void PrincipalsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            CommitResults();
        }
        catch (ArgumentException)
        {
            args.Cancel = true;
            Title = LocalizedStrings.WF_Principals_InvalidAccountTitle;
        }
    }

    private void CommitResults()
    {
        var authorizedItems = BuildItemList(AuthorizedListView, OnlyAllowCheckBox.IsChecked == true);
        var exceptionItems = BuildItemList(ExceptionListView, SkipRuleCheckBox.IsChecked == true);

        try
        {
            AuthorizedSddl = WindowsFirewallSupport.BuildAuthorizationSddl(authorizedItems, null);
            ExceptionSddl = WindowsFirewallSupport.BuildAuthorizationSddl(null, exceptionItems);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.Principal.IdentityNotMappedException)
        {
            throw new ArgumentException(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LocalizedStrings.WF_Principals_ResolveAccountsFailedFormat,
                ex.Message), ex);
        }
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
            if (item is TunnelAuthorizationItem authItem)
            {
                if (!string.IsNullOrWhiteSpace(authItem.Name))
                {
                    items.Add(authItem);
                }
                continue;
            }

            if (item is ListViewItem { Content: TunnelAuthorizationItem lviAuthItem })
            {
                if (!string.IsNullOrWhiteSpace(lviAuthItem.Name))
                {
                    items.Add(lviAuthItem);
                }
                continue;
            }

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

    private void PrincipalsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void PrincipalsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private static void SetSectionEnabled(Border section, bool enabled)
    {
        section.Opacity = enabled ? 1.0 : 0.4;
        section.IsHitTestVisible = enabled;
    }

    private void OnlyAllowCheckBox_Changed(object sender, RoutedEventArgs e)
        => SetSectionEnabled(AuthorizedSection, OnlyAllowCheckBox.IsChecked == true);

    private void SkipRuleCheckBox_Changed(object sender, RoutedEventArgs e)
        => SetSectionEnabled(ExceptionSection, SkipRuleCheckBox.IsChecked == true);

    // ── Authorized section ──────────────────────────────────────────────────

    private void AuthAddButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(AuthorizedListView);
    }

    private void AuthAddLocalUserItem_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(AuthorizedListView);
    }

    private void AuthAddAppPackageItem_Click(object sender, RoutedEventArgs e)
    {
        ShowCapabilitiesFlyout(AuthAddDropDown, AuthorizedListView);
    }

    private void AuthDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedItem(AuthorizedListView);
    }

    // ── Exception section ───────────────────────────────────────────────────

    private void ExcAddButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(ExceptionListView);
    }

    private void ExcAddLocalUserItem_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(ExceptionListView);
    }

    private void ExcAddAppPackageItem_Click(object sender, RoutedEventArgs e)
    {
        ShowCapabilitiesFlyout(ExcAddDropDown, ExceptionListView);
    }

    private void ExcDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedItem(ExceptionListView);
    }

    // ── Capabilities Flyout ─────────────────────────────────────────────────

    private void ShowCapabilitiesFlyout(FrameworkElement anchor, ListView targetList)
    {
        var content = new CapabilitiesFlyoutContent();
        var flyout = new Flyout { Content = content };

        content.CapabilitiesConfirmed += (_, capabilities) =>
        {
            foreach (var cap in capabilities)
            {
                AddItem(targetList, new TunnelAuthorizationItem { Name = cap.Name, Sid = cap.Sid });
            }

            flyout.Hide();
        };
        content.Cancelled += (_, _) => flyout.Hide();

        flyout.ShowAt(anchor, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Bottom
        });
    }

    private static void AddDirectoryObjects(ListView targetList)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        List<DirectoryObject>? selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            ObjectPickerTypes.UsersAndGroups,
            multiSelect: true);

        if (selections is not { Count: > 0 })
        {
            return;
        }

        foreach (DirectoryObject selection in selections)
        {
            string resolvedName = string.IsNullOrWhiteSpace(selection.Name)
                ? selection.Sid?.Trim() ?? string.Empty
                : selection.Name.Trim();

            AddItem(targetList, new TunnelAuthorizationItem
            {
                Name = resolvedName,
                Sid = selection.Sid?.Trim() ?? string.Empty
            });
        }
    }

    private static void AddItem(ListView targetList, TunnelAuthorizationItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        bool exists = targetList.Items.Cast<object>()
            .Select(existingItem => existingItem switch
            {
                TunnelAuthorizationItem t => t.Name,
                string stringItem => stringItem,
                ListViewItem listViewItem => (listViewItem.Content as TunnelAuthorizationItem)?.Name ?? listViewItem.Content?.ToString() ?? string.Empty,
                _ => existingItem?.ToString() ?? string.Empty
            })
            .Any(existing => string.Equals(existing, item.Name, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            targetList.Items.Add(item);
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

