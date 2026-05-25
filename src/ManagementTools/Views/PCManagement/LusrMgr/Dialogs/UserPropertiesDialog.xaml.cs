using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PCManagement.Models.LusrMgr;
using ManagementTools.Core.Features.PCManagement.Services.LusrMgr;

namespace ManagementTools.Views.LusrMgr;

/// <summary>
/// Dialog for editing properties of a local user.
/// </summary>
public sealed partial class UserPropertiesDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly LocalUser _user;
    private readonly LocalUserGroupManager _manager;
    private ObservableCollection<string> _currentGroups = new();
    private List<string> _originalGroups = new();

    public UserPropertiesDialog(LocalUser user)
    {
        this.InitializeComponent();
        _user = user;
        _manager = new LocalUserGroupManager();
        
        LoadData();
        this.Closing += UserPropertiesDialog_Closing;
    }

    // Expose selected values to callers
    public string FullName => FullNameBox.Text;
    public string Description => DescriptionBox.Text;
    public bool UserMustChangePassword => UserMustChangePasswordCheck.IsChecked ?? false;
    public bool UserCannotChangePassword => UserCannotChangePasswordCheck.IsChecked ?? false;
    public bool PasswordNeverExpires => PasswordNeverExpiresCheck.IsChecked ?? false;
    public bool AccountDisabled => AccountDisabledCheck.IsChecked ?? false;

private async void LoadData()
{
    UserNameTitle.Text = _user.Name;
    UserTitle.Text = LocalizedStrings.LusrMgr_UserProperties_Title;
    FullNameBox.Text = _user.FullName;
    DescriptionBox.Text = _user.Description;
    UserCannotChangePasswordCheck.IsChecked = _user.UserCannotChangePassword;
    PasswordNeverExpiresCheck.IsChecked = !_user.PasswordExpires;
    UserMustChangePasswordCheck.IsChecked = _user.PasswordExpired;
    AccountDisabledCheck.IsChecked = !_user.IsEnabled;

    // Unify processing of password option enabled/disabled states.
    UpdatePasswordOptionsState();

    _originalGroups = await _manager.GetUserGroupsAsync(_user.Name);
    _currentGroups = new ObservableCollection<string>(_originalGroups);
    MemberOfList.ItemsSource = _currentGroups;
}

private void OnPasswordOptionChanged(object sender, RoutedEventArgs e)
{
    UpdatePasswordOptionsState();
}

private void UpdatePasswordOptionsState()
{
    bool mustChange = UserMustChangePasswordCheck.IsChecked == true;
    bool neverExpires = PasswordNeverExpiresCheck.IsChecked == true;
    bool cannotChange = UserCannotChangePasswordCheck.IsChecked == true;

    // Handle mutual exclusion.
    if (mustChange)
    {
        // "User must change password" is mutually exclusive with the other two options.
        UserCannotChangePasswordCheck.IsChecked = false;
        PasswordNeverExpiresCheck.IsChecked = false;
        UserCannotChangePasswordCheck.IsEnabled = false;
        PasswordNeverExpiresCheck.IsEnabled = false;
    }
    else
    {
        // Enable other options.
        UserCannotChangePasswordCheck.IsEnabled = true;
        PasswordNeverExpiresCheck.IsEnabled = true;
    }

    // "User must change password" enabled state depends on the other two options.
    UserMustChangePasswordCheck.IsEnabled = !neverExpires && !cannotChange;

    // Show conflict warning (theoretically shouldn't happen as mutual exclusion is handled).
    bool conflict = (mustChange && neverExpires) || (mustChange && cannotChange);
    PasswordConflictInfo.IsOpen = conflict;
}

    private void UserPropertiesSelectorBar_SelectionChanged(Microsoft.UI.Xaml.Controls.SelectorBar sender, Microsoft.UI.Xaml.Controls.SelectorBarSelectionChangedEventArgs args)
    {
        // Hide all pages
        GeneralPage.Visibility = Visibility.Collapsed;
        MemberOfPage.Visibility = Visibility.Collapsed;

        // Show selected page
        if (sender.SelectedItem is Microsoft.UI.Xaml.Controls.SelectorBarItem selectedItem && selectedItem.Tag is string tag)
        {
            switch (tag)
            {
                case "General":
                    GeneralPage.Visibility = Visibility.Visible;
                    break;
                case "MemberOf":
                    MemberOfPage.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private void OnAddGroupClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            ObjectPickerTypes.Groups,
            multiSelect: false);

        if (selections is { Count: > 0 })
        {
            var name = selections[0].Name;
            if (!string.IsNullOrEmpty(name) && !_currentGroups.Contains(name))
            {
                _currentGroups.Add(name);
            }
        }
    }

    private void OnRemoveGroupClick(object sender, RoutedEventArgs e)
    {
        // Remove selected group from the current groups list
        if (MemberOfList.SelectedItem is string groupName)
        {
            _currentGroups.Remove(groupName);
        }
    }

    private void UserPropertiesDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            try
            {
                // update user properties
                _manager.UpdateUser(_user.Name, FullNameBox.Text, DescriptionBox.Text, 
                                    UserCannotChangePasswordCheck.IsChecked ?? false,
                                    PasswordNeverExpiresCheck.IsChecked ?? false, 
                                    AccountDisabledCheck.IsChecked ?? false);

                // update group membership
                foreach (var g in _currentGroups)
                {
                    if (!_originalGroups.Contains(g))
                    {
                        _manager.AddUserToGroup(_user.Name, g);
                    }
                }

                // remove user from groups that are no longer in the list
                foreach (var g in _originalGroups)
                {
                    if (!_currentGroups.Contains(g))
                    {
                        _manager.RemoveUserFromGroup(_user.Name, g);
                    }
                }
            }
            catch (Exception ex)
            {
               ManagementTools.Services.Logging.UiLogger.LogDebug($"Error saving user: {ex.Message}");
            }
        }
    }
}


