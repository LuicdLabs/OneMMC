using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Principal;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.WFProperties;

public sealed partial class IpsecTunnelAuthorizationsDialog : UserControl
{
    public ObservableCollection<TunnelAuthorizationItem> AllowedComputers { get; } = [];
    public ObservableCollection<TunnelAuthorizationItem> DeniedComputers { get; } = [];
    public ObservableCollection<TunnelAuthorizationItem> AllowedUsers { get; } = [];
    public ObservableCollection<TunnelAuthorizationItem> DeniedUsers { get; } = [];

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public FirewallTunnelAuthorizationSettings Settings { get; }

    public IpsecTunnelAuthorizationsDialog(FirewallTunnelAuthorizationSettings settings)
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += IpsecTunnelAuthorizationsDialog_Unloaded;

        Settings = settings;

        AllowedComputersListView.ItemsSource = AllowedComputers;
        DeniedComputersListView.ItemsSource = DeniedComputers;
        AllowedUsersListView.ItemsSource = AllowedUsers;
        DeniedUsersListView.ItemsSource = DeniedUsers;

        LoadSettings(settings);
        TabBar.SelectedItem = ComputersTab;
        UpdateComputerEditingState();
        UpdateUserEditingState();
    }

    public System.Threading.Tasks.Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_CustomizeIpsecTunnelAuthorizations_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 760,
            Height = 700,
            OnPrimaryButtonClick = CommitSettings
        });

        return modalWindow.ShowDialogAsync();
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void IpsecTunnelAuthorizationsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool isComputerTab = sender.SelectedItem == ComputersTab;
        ComputersPanel.Visibility = isComputerTab ? Visibility.Visible : Visibility.Collapsed;
        UsersPanel.Visibility = isComputerTab ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AllowComputersCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateComputerEditingState();
    }

    private void DenyComputersCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateComputerEditingState();
    }

    private void AllowUsersCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateUserEditingState();
    }

    private void DenyUsersCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateUserEditingState();
    }

    private void AddAllowedComputerButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(TunnelAuthorizationPrincipalScope.Computer, AllowedComputers, AllowComputersCheckBox);
    }

    private void RemoveAllowedComputerButton_Click(object sender, RoutedEventArgs e)
    {
        if (AllowedComputersListView.SelectedItem is TunnelAuthorizationItem item)
        {
            AllowedComputers.Remove(item);
        }
    }

    private void AddDeniedComputerButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(TunnelAuthorizationPrincipalScope.Computer, DeniedComputers, DenyComputersCheckBox);
    }

    private void RemoveDeniedComputerButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeniedComputersListView.SelectedItem is TunnelAuthorizationItem item)
        {
            DeniedComputers.Remove(item);
        }
    }

    private void AddAllowedUserButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(TunnelAuthorizationPrincipalScope.User, AllowedUsers, AllowUsersCheckBox);
    }

    private void RemoveAllowedUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (AllowedUsersListView.SelectedItem is TunnelAuthorizationItem item)
        {
            AllowedUsers.Remove(item);
        }
    }

    private void AddDeniedUserButton_Click(object sender, RoutedEventArgs e)
    {
        AddDirectoryObjects(TunnelAuthorizationPrincipalScope.User, DeniedUsers, DenyUsersCheckBox);
    }

    private void RemoveDeniedUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeniedUsersListView.SelectedItem is TunnelAuthorizationItem item)
        {
            DeniedUsers.Remove(item);
        }
    }

    private void UpdateComputerEditingState()
    {
        bool allowEnabled = AllowComputersCheckBox.IsChecked == true;
        bool denyEnabled = DenyComputersCheckBox.IsChecked == true;

        AllowedComputersListView.IsEnabled = allowEnabled;
        AddAllowedComputerButton.IsEnabled = allowEnabled;
        RemoveAllowedComputerButton.IsEnabled = allowEnabled;

        DeniedComputersListView.IsEnabled = denyEnabled;
        AddDeniedComputerButton.IsEnabled = denyEnabled;
        RemoveDeniedComputerButton.IsEnabled = denyEnabled;
    }

    private void UpdateUserEditingState()
    {
        bool allowEnabled = AllowUsersCheckBox.IsChecked == true;
        bool denyEnabled = DenyUsersCheckBox.IsChecked == true;

        AllowedUsersListView.IsEnabled = allowEnabled;
        AddAllowedUserButton.IsEnabled = allowEnabled;
        RemoveAllowedUserButton.IsEnabled = allowEnabled;

        DeniedUsersListView.IsEnabled = denyEnabled;
        AddDeniedUserButton.IsEnabled = denyEnabled;
        RemoveDeniedUserButton.IsEnabled = denyEnabled;
    }

    private void LoadSettings(FirewallTunnelAuthorizationSettings settings)
    {
        CopyItems(settings.AllowedComputers, AllowedComputers);
        CopyItems(settings.DeniedComputers, DeniedComputers);
        CopyItems(settings.AllowedUsers, AllowedUsers);
        CopyItems(settings.DeniedUsers, DeniedUsers);

        AllowComputersCheckBox.IsChecked = AllowedComputers.Count > 0;
        DenyComputersCheckBox.IsChecked = DeniedComputers.Count > 0;
        AllowUsersCheckBox.IsChecked = AllowedUsers.Count > 0;
        DenyUsersCheckBox.IsChecked = DeniedUsers.Count > 0;
    }

    public bool CommitSettings()
    {
        if (!ValidateTunnelAuthorizationItems())
        {
            ValidationTextBlock.Text = LocalizedStrings.WF_IpsecTunnelAuth_RemoveInvalidAccounts;
            ValidationTextBlock.Visibility = Visibility.Visible;
            return false;
        }

        ValidationTextBlock.Visibility = Visibility.Collapsed;
        Settings.AllowedComputers.Clear();
        Settings.DeniedComputers.Clear();
        Settings.AllowedUsers.Clear();
        Settings.DeniedUsers.Clear();

        if (AllowComputersCheckBox.IsChecked == true)
        {
            CopyItems(AllowedComputers, Settings.AllowedComputers);
        }

        if (DenyComputersCheckBox.IsChecked == true)
        {
            CopyItems(DeniedComputers, Settings.DeniedComputers);
        }

        if (AllowUsersCheckBox.IsChecked == true)
        {
            CopyItems(AllowedUsers, Settings.AllowedUsers);
        }

        if (DenyUsersCheckBox.IsChecked == true)
        {
            CopyItems(DeniedUsers, Settings.DeniedUsers);
        }

        return true;
    }

    private static void AddDirectoryObjects(
        TunnelAuthorizationPrincipalScope scope,
        ObservableCollection<TunnelAuthorizationItem> target,
        CheckBox ownerCheckBox)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        System.Collections.Generic.List<DirectoryObject>? selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            CreatePickerOptions(scope));

        if (selections is not { Count: > 0 })
        {
            return;
        }

        foreach (DirectoryObject selection in selections)
        {
            string name = selection.Name?.Trim() ?? string.Empty;
            string sid = selection.Sid?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(sid))
            {
                continue;
            }

            string resolvedName = string.IsNullOrWhiteSpace(name) ? sid : name;
            var newItem = new TunnelAuthorizationItem
            {
                Name = resolvedName,
                Sid = sid
            };

            if (!IsValidTunnelAuthorizationItem(newItem))
            {
                continue;
            }

            bool alreadyExists = target.Any(existingItem =>
                (!string.IsNullOrWhiteSpace(sid) && string.Equals(existingItem.Sid, sid, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(existingItem.Name, resolvedName, StringComparison.OrdinalIgnoreCase));
            if (alreadyExists)
            {
                continue;
            }

            target.Add(newItem);
            ownerCheckBox.IsChecked = true;
        }
    }

    private static DirectoryObjectPickerOptions CreatePickerOptions(TunnelAuthorizationPrincipalScope scope)
        => new()
        {
            Types = scope == TunnelAuthorizationPrincipalScope.Computer
                ? ObjectPickerTypes.Groups
                : ObjectPickerTypes.UsersAndGroups,
            MultiSelect = true,
            IncludeLocalComputerScope = true,
            IncludeDomainScopes = true,
            IncludeWorkgroupScope = false,
            IncludeUserEnteredScopes = false,
            IncludeWellKnownPrincipals = true,
            IncludeDownlevelWellKnownPrincipals = true,
            BuiltInPrincipalsOnly = scope == TunnelAuthorizationPrincipalScope.Computer
        };

    private bool ValidateTunnelAuthorizationItems()
    {
        return AllowedComputers.All(IsValidTunnelAuthorizationItem)
            && DeniedComputers.All(IsValidTunnelAuthorizationItem)
            && AllowedUsers.All(IsValidTunnelAuthorizationItem)
            && DeniedUsers.All(IsValidTunnelAuthorizationItem);
    }

    private static bool IsValidTunnelAuthorizationItem(TunnelAuthorizationItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Sid))
        {
            return TryCreateSecurityIdentifier(item.Sid, out _);
        }

        string name = item.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return TryCreateSecurityIdentifier(name, out _);
    }

    private static bool TryCreateSecurityIdentifier(string accountOrSid, out SecurityIdentifier? sid)
    {
        sid = null;
        string normalizedValue = accountOrSid.Trim();

        try
        {
            sid = normalizedValue.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
                ? new SecurityIdentifier(normalizedValue)
                : (SecurityIdentifier)new NTAccount(normalizedValue).Translate(typeof(SecurityIdentifier));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IdentityNotMappedException)
        {
            return false;
        }
        catch (SystemException)
        {
            return false;
        }
    }

    private static void CopyItems(
        System.Collections.Generic.IEnumerable<TunnelAuthorizationItem> source,
        ObservableCollection<TunnelAuthorizationItem> target)
    {
        foreach (TunnelAuthorizationItem item in source)
        {
            target.Add(new TunnelAuthorizationItem
            {
                Name = item.Name,
                Sid = item.Sid
            });
        }
    }

    private enum TunnelAuthorizationPrincipalScope
    {
        Computer,
        User
    }
}

