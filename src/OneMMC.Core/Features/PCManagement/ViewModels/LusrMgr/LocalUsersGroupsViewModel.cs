using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.PCManagement.Models.LusrMgr;
using OneMMC.Core.Features.PCManagement.Services.LusrMgr;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace OneMMC.Core.Features.PCManagement.ViewModels.LusrMgr;

/// <summary>
/// ViewModel for the Local Users and Groups page.
/// Manages the list of users and groups, and handles search/filter logic.
/// </summary>
public partial class LocalUsersGroupsViewModel : ObservableObject
{
    private readonly LocalUserGroupManager _manager;
    private List<LocalUser> _allUsers = new();
    private List<LocalGroup> _allGroups = new();

    [ObservableProperty]
    public partial ObservableCollection<LocalUser> Users { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<LocalGroup> Groups { get; set; } = new();

    [ObservableProperty]
    public partial string UserSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GroupSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int UserCount { get; set; }

    [ObservableProperty]
    public partial int GroupCount { get; set; }

    public LocalUsersGroupsViewModel()
    {
        _manager = new LocalUserGroupManager();
        LoadData();
    }

    // Public command properties can be assigned by the UI layer to provide dialog behavior.
    public IRelayCommand? AddUserCommand { get; set; }
    public IRelayCommand? AddGroupCommand { get; set; }
    public IRelayCommand? EditUserCommand { get; set; }
    public IRelayCommand? EditGroupCommand { get; set; }
    public IRelayCommand? DeleteUserCommand { get; set; }
    public IRelayCommand? DeleteGroupCommand { get; set; }

    private void LoadData()
    {
        _allUsers = _manager.GetUsers() ?? new List<LocalUser>();
        _allGroups = _manager.GetGroups() ?? new List<LocalGroup>();
        RefreshUserList();
        RefreshGroupList();
    }

    public void Reload()
    {
        LoadData();
    }

    public void ClearCachedData()
    {
        _allUsers.Clear();
        _allGroups.Clear();
        Users.Clear();
        Groups.Clear();
        UserSearchText = string.Empty;
        GroupSearchText = string.Empty;
        UserCount = 0;
        GroupCount = 0;
    }

    partial void OnUserSearchTextChanged(string value)
    {
        RefreshUserList();
    }

    partial void OnGroupSearchTextChanged(string value)
    {
        RefreshGroupList();
    }

    /// <summary>
    /// Creates a new user via the manager and refreshes the list.
    /// </summary>
    public void CreateUser(string userName, string password, string fullName, string description, bool userCannotChangePassword, bool passwordNeverExpires, bool accountDisabled, bool userMustChangePassword = false)
    {
        _manager.CreateUser(userName, password, fullName, description, userCannotChangePassword, passwordNeverExpires, accountDisabled, userMustChangePassword);
        Refresh();
    }

    /// <summary>
    /// Creates a new group via the manager and refreshes the list.
    /// </summary>
    public void CreateGroup(string groupName, string description, IEnumerable<string>? members = null)
    {
        _manager.CreateGroup(groupName, description);
        if (members != null)
        {
            foreach (var m in members)
            {
                _manager.AddUserToGroup(m, groupName);
            }
        }
        Refresh();
    }

    /// <summary>
    /// Edits an existing user asynchronously.
    /// </summary>
    public async Task EditUserAsync(string userName, string fullName, string description, bool userCannotChangePassword, bool passwordNeverExpires, bool accountDisabled, bool userMustChangePassword = false, IEnumerable<string>? newGroups = null)
    {
        _manager.UpdateUser(userName, fullName, description, userCannotChangePassword, passwordNeverExpires, accountDisabled, userMustChangePassword);
        if (newGroups != null)
        {
            var original = await _manager.GetUserGroupsAsync(userName);
            // update group membership
            foreach (var g in newGroups)
            {
                if (!original.Contains(g)) _manager.AddUserToGroup(userName, g);
            }
            // remove user from groups that are no longer in the list
            foreach (var g in original)
            {
                if (!newGroups.Contains(g)) _manager.RemoveUserFromGroup(userName, g);
            }
        }
        Refresh();
    }

    /// <summary>
    /// Edits an existing group asynchronously.
    /// </summary>
    public async Task EditGroupAsync(string groupName, string description, IEnumerable<string>? members = null)
    {
        await _manager.UpdateGroupAsync(groupName, description);
        if (members != null)
        {
            var original = await _manager.GetGroupMembersAsync(groupName);
            // update group membership
            foreach (var m in members)
            {
                if (!original.Contains(m)) _manager.AddUserToGroup(m, groupName);
            }
            // remove user from groups that are no longer in the list
            foreach (var m in original)
            {
                if (!members.Contains(m)) _manager.RemoveUserFromGroup(m, groupName);
            }
        }
        Refresh();
    }

    /// <summary>
    /// Deletes a user.
    /// </summary>
    public void DeleteUser(string userName)
    {
        _manager.DeleteUser(userName);
        Refresh();
    }

    /// <summary>
    /// Deletes a group.
    /// </summary>
    public void DeleteGroup(string groupName)
    {
        _manager.DeleteGroup(groupName);
        Refresh();
    }

    private void RefreshUserList()
    {
        List<LocalUser> filtered;
        if (string.IsNullOrWhiteSpace(UserSearchText))
        {
            filtered = _allUsers;
        }
        else
        {
            // filter users by name or full name
            filtered = _allUsers.Where(u =>
                (u.Name?.Contains(UserSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.FullName?.Contains(UserSearchText, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();
        }
        Users = new ObservableCollection<LocalUser>(filtered);
        UserCount = Users.Count;
    }

    private void RefreshGroupList()
    {
        List<LocalGroup> filtered;
        if (string.IsNullOrWhiteSpace(GroupSearchText))
        {
            filtered = _allGroups;
        }
        else
        {
            filtered = _allGroups.Where(g => g.Name.Contains(GroupSearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Groups = new ObservableCollection<LocalGroup>(filtered);
        GroupCount = Groups.Count;
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadData();
    }
}


