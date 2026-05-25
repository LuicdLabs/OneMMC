using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ManagementTools.Core.Features.PCManagement.Models.LusrMgr;

namespace ManagementTools.Core.Features.PCManagement.Services.LusrMgr;

/// <summary>
/// Service/Manager class for handling local users and groups using Directory Services.
/// </summary>
public class LocalUserGroupManager
{
    private readonly ILogger<LocalUserGroupManager> _logger;

    public LocalUserGroupManager()
        : this(NullLogger<LocalUserGroupManager>.Instance)
    {
    }

    public LocalUserGroupManager(ILogger<LocalUserGroupManager> logger)
    {
        _logger = logger;
    }

    // ADS_USER_FLAG constants
    private const int ADS_UF_ACCOUNTDISABLE = 2; // the account is disabled
    private const int ADS_UF_PASSWD_CANT_CHANGE = 64; // the user cannot change their password
    private const int ADS_UF_DONT_EXPIRE_PASSWD = 65536; // the password never expires
    private const int ADS_UF_PASSWD_NOTREQD = 32; // the user does not require a password

    private static bool GetUserFlag(int flags, int flag)
    {
        return (flags & flag) != 0; // true if the flag is set
    }

    private static void SetUserFlag(ref int flags, int flag, bool value)
    {
        if (value)
        {
            flags |= flag; // set the flag
        }
        else
        {
            flags &= ~flag; // clear the flag
        }
    }
    /// <summary>
    /// Retrieves a list of all local users.
    /// </summary>
    /// <returns>A list of <see cref="LocalUser"/> objects.</returns>
    public List<LocalUser> GetUsers()
    {
        var users = new List<LocalUser>();
        try
        {
            // Get all local users
            using var context = new PrincipalContext(ContextType.Machine);
            using var searcher = new PrincipalSearcher(new UserPrincipal(context));
            foreach (var result in searcher.FindAll())
            {
                if (result is UserPrincipal user)
                {
                    // Create a new LocalUser object
                    var lu = new LocalUser
                    {
                        Name = user.Name,
                        FullName = user.DisplayName,
                        Description = user.Description,
                        IsEnabled = user.Enabled ?? true,
                        PasswordRequired = user.PasswordNotRequired == false,
                        UserCannotChangePassword = user.UserCannotChangePassword,
                        PasswordExpires = user.PasswordNeverExpires == false,
                        PasswordExpired = false
                    };
                    try
                    {
                        if (user.GetUnderlyingObject() is DirectoryEntry de)
                        {
                            // PasswordExpired is an integer value, 0 means not expired, 1 means expired
                            var val = de.Properties.Contains("PasswordExpired") ? de.Properties["PasswordExpired"].Value : null;
                            if (val != null)
                            {
                                if (int.TryParse(val.ToString(), out var iv))
                                {
                                    lu.PasswordExpired = iv != 0;
                                }
                                else if (bool.TryParse(val.ToString(), out var bv))
                                {
                                    lu.PasswordExpired = bv;
                                }
                            }

                            // Read UserFlags
                            if (de.Properties.Contains("UserFlags") && de.Properties["UserFlags"].Value is int userFlags)
                            {
                                lu.IsEnabled = !GetUserFlag(userFlags, ADS_UF_ACCOUNTDISABLE);
                                lu.UserCannotChangePassword = GetUserFlag(userFlags, ADS_UF_PASSWD_CANT_CHANGE);
                                lu.PasswordExpires = !GetUserFlag(userFlags, ADS_UF_DONT_EXPIRE_PASSWD);
                                lu.PasswordRequired = !GetUserFlag(userFlags, ADS_UF_PASSWD_NOTREQD);
                            }
                        }
                    }
                    catch (Exception ex)  { _logger.LogDebug($"Error getting user {user.Name}: {ex.Message}"); }
                    users.Add(lu); // Add the user to the list
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Error getting users: {ex.Message}");
        }
        return users.OrderBy(u => u.Name).ToList();
    }

    /// <summary>
    /// Retrieves a list of all local groups.
    /// </summary>
    /// <returns>A list of <see cref="LocalGroup"/> objects.</returns>
    public List<LocalGroup> GetGroups()
    {
        var groups = new List<LocalGroup>();
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var searcher = new PrincipalSearcher(new GroupPrincipal(context));
            foreach (var result in searcher.FindAll())
            {
                if (result is GroupPrincipal group)
                {
                    groups.Add(new LocalGroup
                    {
                        Name = group.Name,
                        Description = group.Description
                    });
                }
            }
        }
        catch (Exception ex)
        {
             _logger.LogDebug($"Error getting groups: {ex.Message}");
        }
        return groups.OrderBy(g => g.Name).ToList();
    }

    /// <summary>
    /// Creates a new local user.
    /// </summary>
    /// <param name="username">The username for the new user.</param>
    /// <param name="password">The password for the new user.</param>
    /// <param name="fullName">The full name of the user.</param>
    /// <param name="description">The description of the user.</param>
    /// <param name="userCannotChangePassword">If true, the user cannot change their password.</param>
    /// <param name="passwordNeverExpires">If true, the password never expires.</param>
    /// <param name="accountDisabled">If true, the account is disabled.</param>
    /// <param name="userMustChangePassword">If true, the user must change password at next logon.</param>
    public void CreateUser(string username, string password, string fullName, string description, bool userCannotChangePassword, bool passwordNeverExpires, bool accountDisabled, bool userMustChangePassword = false)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var user = new UserPrincipal(context);
        user.Name = username;
        user.SetPassword(password);
        user.DisplayName = fullName;
        user.Description = description;
        user.Save();

        if (user.GetUnderlyingObject() is DirectoryEntry de)
        {
            // Set UserFlags
            var userFlagsValue = de.Properties["UserFlags"].Value;
            int userFlags = userFlagsValue != null ? (int)userFlagsValue : 0;
            SetUserFlag(ref userFlags, ADS_UF_ACCOUNTDISABLE, accountDisabled);
            SetUserFlag(ref userFlags, ADS_UF_PASSWD_CANT_CHANGE, userCannotChangePassword);
            SetUserFlag(ref userFlags, ADS_UF_DONT_EXPIRE_PASSWD, passwordNeverExpires);
            de.Properties["UserFlags"].Value = userFlags;

            // Set PasswordExpired
            de.Properties["PasswordExpired"].Value = userMustChangePassword ? 1 : 0;

            de.CommitChanges();
        }
    }

    /// <summary>
    /// Creates a new local group.
    /// </summary>
    /// <param name="groupName">The name of the new group.</param>
    /// <param name="description">The description of the group.</param>
    public void CreateGroup(string groupName, string description)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var group = new GroupPrincipal(context);
        group.Name = groupName;
        group.Description = description;
        group.Save();
    }

    /// <summary>
    /// Deletes a local user.
    /// </summary>
    /// <param name="username">The username of the user to delete.</param>
    public void DeleteUser(string username)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var user = UserPrincipal.FindByIdentity(context, username);
        user?.Delete();
    }
    
    /// <summary>
    /// Deletes a local group.
    /// </summary>
    /// <param name="groupName">The name of the group to delete.</param>
    public void DeleteGroup(string groupName)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var group = GroupPrincipal.FindByIdentity(context, groupName);
        group?.Delete();
    }

    /// <summary>
    /// Sets a new password for the specified user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="newPassword">The new password.</param>
    public void SetPassword(string username, string newPassword)
    {
         using var context = new PrincipalContext(ContextType.Machine);
         using var user = UserPrincipal.FindByIdentity(context, username);
         user?.SetPassword(newPassword);
    }

    /// <summary>
    /// Updates an existing local user's properties.
    /// </summary>
    /// <param name="username">The username identifying the user.</param>
    /// <param name="fullName">The new full name.</param>
    /// <param name="description">The new description.</param>
    /// <param name="userCannotChangePassword">If true, the user cannot change their password.</param>
    /// <param name="passwordNeverExpires">If true, the password never expires.</param>
    /// <param name="accountDisabled">If true, the account is disabled.</param>
    /// <param name="userMustChangePassword">If true, the user must change password at next logon.</param>
    public void UpdateUser(string username, string fullName, string description, bool userCannotChangePassword, bool passwordNeverExpires, bool accountDisabled, bool userMustChangePassword = false)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var user = UserPrincipal.FindByIdentity(context, username);
        if (user != null)
        {
            user.DisplayName = fullName;
            user.Description = description;
            user.Save();

            if (user.GetUnderlyingObject() is DirectoryEntry de)
            {
                // Set UserFlags
                var userFlagsValue = de.Properties["UserFlags"].Value;
                int userFlags = userFlagsValue != null ? (int)userFlagsValue : 0;
                SetUserFlag(ref userFlags, ADS_UF_ACCOUNTDISABLE, accountDisabled);
                SetUserFlag(ref userFlags, ADS_UF_PASSWD_CANT_CHANGE, userCannotChangePassword);
                SetUserFlag(ref userFlags, ADS_UF_DONT_EXPIRE_PASSWD, passwordNeverExpires);
                de.Properties["UserFlags"].Value = userFlags;

                // Set PasswordExpired
                de.Properties["PasswordExpired"].Value = userMustChangePassword ? 1 : 0;

                de.CommitChanges();
            }
        }
    }

    /// <summary>
    /// Updates an existing local group's description.
    /// </summary>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="description">The new description.</param>
    public void UpdateGroup(string groupName, string description)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var group = GroupPrincipal.FindByIdentity(context, groupName);
        if (group != null)
        {
            group.Description = description;
            group.Save();
        }
    }

    /// <summary>
    /// Asynchronously updates an existing local group's description.
    /// </summary>
    public async Task UpdateGroupAsync(string groupName, string description)
    {
        await Task.Run(() => UpdateGroup(groupName, description));
    }

    /// <summary>
    /// Asynchronously retrieves the list of groups a user belongs to.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <returns>A list of group names.</returns>
    public async Task<List<string>> GetUserGroupsAsync(string username)
    {
        return await Task.Run(() =>
        {
            var groups = new List<string>();
            try
            {
                // Use DirectoryEntry to get user groups directly via the WinNT provider.
                // The original System.DirectoryServices.AccountManagement (PrincipalContext + UserPrincipal)
                // introduces extra abstraction layers and recursive queries when enumerating groups, leading to slower loading speeds.
                // Using DirectoryEntry.Invoke("Groups") directly calls the underlying API,
                // reducing unnecessary overhead and significantly improving the loading speed of User Properties / Group Properties dialogs.
                using var user = new DirectoryEntry($"WinNT://./{username},user");
                var groupsObj = user.Invoke("Groups");
                if (groupsObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (object groupObj in enumerable)
                    {
                        using var groupEntry = new DirectoryEntry(groupObj);
                        groups.Add(groupEntry.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error getting user groups: {ex.Message}");
            }
            // Sort group names for consistent display results
            return groups.OrderBy(g => g).ToList();
        });
    }

    /// <summary>
    /// Asynchronously retrieves the list of members in a group.
    /// </summary>
    /// <param name="groupName">The group name.</param>
    /// <returns>A list of member names.</returns>
    public async Task<List<string>> GetGroupMembersAsync(string groupName)
    {
        return await Task.Run(() =>
        {
            var members = new List<string>();
            try
            {
                using var group = new DirectoryEntry($"WinNT://./{groupName},group");
                var membersObj = group.Invoke("Members");
                if (membersObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (object memberObj in enumerable)
                    {
                        using var memberEntry = new DirectoryEntry(memberObj);
                        members.Add(memberEntry.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error getting group members: {ex.Message}");
            }
            return members.OrderBy(m => m).ToList();
        });
    }

    /// <summary>
    /// Adds a user to a group.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="groupName">The group name.</param>
    public void AddUserToGroup(string username, string groupName)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var group = GroupPrincipal.FindByIdentity(context, groupName);
        using var user = UserPrincipal.FindByIdentity(context, username);
        
        if (group != null && user != null)
        {
            if (!group.Members.Contains(user))
            {
                group.Members.Add(user);
                group.Save();
            }
        }
    }

    /// <summary>
    /// Removes a user from a group.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="groupName">The group name.</param>
    public void RemoveUserFromGroup(string username, string groupName)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var group = GroupPrincipal.FindByIdentity(context, groupName);
        using var user = UserPrincipal.FindByIdentity(context, username);

        if (group != null && user != null)
        {
             if (group.Members.Contains(user))
             {
                 group.Members.Remove(user);
                 group.Save();
             }
        }
    }
}


