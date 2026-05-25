// ============================================================================
// PolicyUsersDialog.xaml.cs
// 
// Policy Users Dialog - For managing Policy Administrators, Readers, and Delegated Users
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Core.Features.UserSecurity.Services.AzMan;
using ManagementTools.Localization;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Policy user type
/// </summary>
public enum PolicyUserType
{
    Administrators,
    Readers,
    Delegated
}

/// <summary>
/// Policy Users Dialog
/// </summary>
public sealed partial class PolicyUsersDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly AzManService _service;
    private readonly string _storePath;
    private readonly string? _appName; // null for store-level, non-null for application-level
    private PolicyUserType _currentType = PolicyUserType.Administrators;
    private readonly ObservableCollection<string> _users = [];

    /// <summary>
    /// Whether changes were made
    /// </summary>
    public bool HasChanges { get; private set; }

    /// <summary>
    /// Create dialog for store-level policy users
    /// </summary>
    public PolicyUsersDialog(AzManService service, string storePath, AzAuthorizationStoreInfo storeInfo)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _service = service;
        _storePath = storePath;
        _appName = null;

        Title = string.Format(LocalizedStrings.PolicyUsersDialog_TitleWithName, storeInfo.Name);
        UsersListView.ItemsSource = _users;
        UsersListView.SelectionChanged += OnUserSelectionChanged;

        // Select first tab
        PolicyTypeNav.SelectedItem = PolicyTypeNav.MenuItems[0];
    }

    /// <summary>
    /// Create dialog for application-level policy users
    /// </summary>
    public PolicyUsersDialog(AzManService service, string storePath, string appName, AzApplicationInfo appInfo)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _service = service;
        _storePath = storePath;
        _appName = appName;

        Title = string.Format(LocalizedStrings.PolicyUsersDialog_TitleWithName, appInfo.Name);
        UsersListView.ItemsSource = _users;
        UsersListView.SelectionChanged += OnUserSelectionChanged;

        // Select first tab
        PolicyTypeNav.SelectedItem = PolicyTypeNav.MenuItems[0];
    }

    /// <summary>
    /// Policy type selection changed
    /// </summary>
    private async void OnPolicyTypeChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            _currentType = tag switch
            {
                "Readers" => PolicyUserType.Readers,
                "Delegated" => PolicyUserType.Delegated,
                _ => PolicyUserType.Administrators
            };

            await LoadUsersAsync();
        }
    }

    /// <summary>
    /// User selection changed
    /// </summary>
    private void OnUserSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveButton.IsEnabled = UsersListView.SelectedItem != null;
    }

    /// <summary>
    /// Load users for current type
    /// </summary>
    private async Task LoadUsersAsync()
    {
        try
        {
            LoadingRing.IsActive = true;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            _users.Clear();

            // Refresh store/app data
            List<string> userList;

            if (_appName == null)
            {
                // Store-level
                var store = await _service.RefreshStoreAsync(_storePath);
                if (store == null) return;

                userList = _currentType switch
                {
                    PolicyUserType.Readers => store.PolicyReaders,
                    PolicyUserType.Delegated => store.DelegatedPolicyUsers,
                    _ => store.PolicyAdministrators
                };
            }
            else
            {
                // Application-level
                var app = await _service.GetApplicationAsync(_storePath, _appName);
                if (app == null) return;

                userList = _currentType switch
                {
                    PolicyUserType.Readers => app.PolicyReaders,
                    PolicyUserType.Delegated => app.DelegatedPolicyUsers,
                    _ => app.PolicyAdministrators
                };
            }

            foreach (var user in userList)
            {
                _users.Add(user);
            }

            EmptyStatePanel.Visibility = _users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.PolicyUsersDialog_Message_LoadFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    /// <summary>
    /// Add user button click
    /// </summary>
    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            ObjectPickerTypes.UsersAndGroups,
            multiSelect: true);

        if (selections is { Count: > 0 })
        {
            foreach (var obj in selections)
            {
                await AddUserAsync(obj.Name);
            }
        }
    }

    /// <summary>
    /// Add a user
    /// </summary>
    private async Task AddUserAsync(string userName)
    {
        try
        {
            LoadingRing.IsActive = true;

            if (_appName == null)
            {
                // Store-level
                switch (_currentType)
                {
                    case PolicyUserType.Administrators:
                        await _service.AddPolicyAdministratorAsync(_storePath, userName);
                        break;
                    case PolicyUserType.Readers:
                        await _service.AddPolicyReaderAsync(_storePath, userName);
                        break;
                    case PolicyUserType.Delegated:
                        await _service.AddDelegatedPolicyUserAsync(_storePath, userName);
                        break;
                }
            }
            else
            {
                // Application-level
                switch (_currentType)
                {
                    case PolicyUserType.Administrators:
                        await _service.AddApplicationPolicyAdministratorAsync(_storePath, _appName, userName);
                        break;
                    case PolicyUserType.Readers:
                        await _service.AddApplicationPolicyReaderAsync(_storePath, _appName, userName);
                        break;
                    case PolicyUserType.Delegated:
                        await _service.AddApplicationDelegatedPolicyUserAsync(_storePath, _appName, userName);
                        break;
                }
            }

            HasChanges = true;
            await LoadUsersAsync();
            ShowMessage(string.Format(LocalizedStrings.PolicyUsersDialog_Message_UserAdded, userName), LocalizedStrings.PolicyUsersDialog_Title_UserAdded, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.PolicyUsersDialog_Message_AddUserFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    /// <summary>
    /// Remove user button click
    /// </summary>
    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        var selectedUser = UsersListView.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedUser)) return;

        // Confirm removal
        var confirmDialog = new ContentDialog
        {
            Title = LocalizedStrings.PolicyUsersDialog_RemoveUser_Title,
            Content = string.Format(LocalizedStrings.PolicyUsersDialog_RemoveUser_Content, selectedUser, GetCurrentTypeName()),
            PrimaryButtonText = LocalizedStrings.Common_RemoveButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = this.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await RemoveUserAsync(selectedUser);
        }
    }

    /// <summary>
    /// Remove a user
    /// </summary>
    private async Task RemoveUserAsync(string userName)
    {
        try
        {
            LoadingRing.IsActive = true;

            if (_appName == null)
            {
                // Store-level
                switch (_currentType)
                {
                    case PolicyUserType.Administrators:
                        await _service.RemovePolicyAdministratorAsync(_storePath, userName);
                        break;
                    case PolicyUserType.Readers:
                        await _service.RemovePolicyReaderAsync(_storePath, userName);
                        break;
                    case PolicyUserType.Delegated:
                        await _service.RemoveDelegatedPolicyUserAsync(_storePath, userName);
                        break;
                }
            }
            else
            {
                // Application-level
                switch (_currentType)
                {
                    case PolicyUserType.Administrators:
                        await _service.RemoveApplicationPolicyAdministratorAsync(_storePath, _appName, userName);
                        break;
                    case PolicyUserType.Readers:
                        await _service.RemoveApplicationPolicyReaderAsync(_storePath, _appName, userName);
                        break;
                    case PolicyUserType.Delegated:
                        await _service.RemoveApplicationDelegatedPolicyUserAsync(_storePath, _appName, userName);
                        break;
                }
            }

            HasChanges = true;
            await LoadUsersAsync();
            ShowMessage(string.Format(LocalizedStrings.PolicyUsersDialog_Message_UserRemoved, userName), LocalizedStrings.PolicyUsersDialog_Title_UserRemoved, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.PolicyUsersDialog_Message_RemoveUserFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    /// <summary>
    /// Get current type display name
    /// </summary>
    private string GetCurrentTypeName()
    {
        return _currentType switch
        {
            PolicyUserType.Readers => LocalizedStrings.PolicyUsersDialog_Type_Readers,
            PolicyUserType.Delegated => LocalizedStrings.PolicyUsersDialog_Type_Delegated,
            _ => LocalizedStrings.PolicyUsersDialog_Type_Administrators
        };
    }

    /// <summary>
    /// Show message in InfoBar
    /// </summary>
    private void ShowMessage(string message, string title, InfoBarSeverity severity)
    {
        MessageInfoBar.Title = title;
        MessageInfoBar.Message = message;
        MessageInfoBar.Severity = severity;
        MessageInfoBar.IsOpen = true;
    }
}
