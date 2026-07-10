using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.PCManagement.Models.LusrMgr;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.NetManagement;
using Windows.Win32.Security;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.LusrMgr;

/// <summary>
/// Service/Manager class for handling local users and groups via the NetAPI32 SAM
/// account-management functions (CsWin32, marshal-free) — the Native-AOT-compatible
/// replacement for <c>System.DirectoryServices.AccountManagement</c>.
/// </summary>
/// <remarks>
/// Error contract (parity with the previous Directory Services implementation):
/// read methods swallow failures, log at Debug, and return empty results; write methods throw
/// <see cref="Win32Exception"/> on failure so <c>IAdminService.IsPermissionError</c> recognizes
/// <c>ERROR_ACCESS_DENIED</c>, except that "target not found" and "membership already in the
/// requested state" degrade to silent no-ops exactly as the old
/// <c>FindByIdentity(...)?.X()</c> / <c>Members.Contains(...)</c> patterns did.
/// </remarks>
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

    // NET_API_STATUS / Win32 error codes with dedicated no-op semantics (lmerr.h / winerror.h).
    private const uint NerrSuccess = 0;
    private const uint ErrorMoreData = 234;               // ERROR_MORE_DATA - continue enumeration
    private const uint ErrorNoSuchAlias = 1376;           // ERROR_NO_SUCH_ALIAS - local group missing
    private const uint ErrorMemberNotInAlias = 1377;      // ERROR_MEMBER_NOT_IN_ALIAS - remove: already absent
    private const uint ErrorMemberInAlias = 1378;         // ERROR_MEMBER_IN_ALIAS - add: already present
    private const uint ErrorNoSuchMember = 1387;          // ERROR_NO_SUCH_MEMBER - account missing
    private const uint ErrorInvalidMember = 1388;         // ERROR_INVALID_MEMBER - member type not allowed
    private const uint NerrGroupNotFound = 2220;          // NERR_GroupNotFound
    private const uint NerrUserNotFound = 2221;           // NERR_UserNotFound

    // SAM account-control bits (lmaccess.h UF_*; numerically identical to the ADS_UF_* values the
    // previous Directory Services implementation used, so model semantics are unchanged).
    private const uint UF_SCRIPT = 0x0001;
    private const uint UF_ACCOUNTDISABLE = 0x0002;
    private const uint UF_PASSWD_NOTREQD = 0x0020;
    private const uint UF_PASSWD_CANT_CHANGE = 0x0040;
    private const uint UF_NORMAL_ACCOUNT = 0x0200;
    private const uint UF_DONT_EXPIRE_PASSWD = 0x10000;

    private const uint UserPrivUser = 1;                  // USER_PRIV_USER
    private const uint MaxPreferredLength = 0xFFFFFFFF;   // MAX_PREFERRED_LENGTH - let NetAPI size the buffer

    /// <summary>
    /// Retrieves a list of all local users.
    /// </summary>
    /// <returns>A list of <see cref="LocalUser"/> objects.</returns>
    public unsafe List<LocalUser> GetUsers()
    {
        var users = new List<LocalUser>();
        try
        {
            uint resumeHandle = 0;
            uint status;
            do
            {
                byte* buffer = null;
                status = Win32PInvoke.NetUserEnum(
                    servername: default,
                    3,
                    NET_USER_ENUM_FILTER_FLAGS.FILTER_NORMAL_ACCOUNT,
                    out buffer,
                    MaxPreferredLength,
                    out uint entriesRead,
                    out _,
                    ref resumeHandle);

                try
                {
                    if (status != NerrSuccess && status != ErrorMoreData)
                    {
                        throw new Win32Exception(unchecked((int)status));
                    }

                    var entries = (USER_INFO_3*)buffer;
                    for (uint index = 0; index < entriesRead; index++)
                    {
                        USER_INFO_3* info = entries + index;
                        uint flags = (uint)info->usri3_flags;
                        users.Add(new LocalUser
                        {
                            Name = ReadString(info->usri3_name),
                            FullName = ReadString(info->usri3_full_name),
                            Description = ReadString(info->usri3_comment),
                            IsEnabled = (flags & UF_ACCOUNTDISABLE) == 0,
                            PasswordRequired = (flags & UF_PASSWD_NOTREQD) == 0,
                            UserCannotChangePassword = (flags & UF_PASSWD_CANT_CHANGE) != 0,
                            PasswordExpires = (flags & UF_DONT_EXPIRE_PASSWD) == 0,
                            PasswordExpired = info->usri3_password_expired != 0
                        });
                    }
                }
                finally
                {
                    FreeNetApiBuffer(buffer);
                }
            }
            while (status == ErrorMoreData);
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
    public unsafe List<LocalGroup> GetGroups()
    {
        var groups = new List<LocalGroup>();
        try
        {
            nuint resumeHandle = 0;
            uint status;
            do
            {
                byte* buffer = null;
                status = Win32PInvoke.NetLocalGroupEnum(
                    servername: default,
                    1,
                    out buffer,
                    MaxPreferredLength,
                    out uint entriesRead,
                    out _,
                    ref resumeHandle);

                try
                {
                    if (status != NerrSuccess && status != ErrorMoreData)
                    {
                        throw new Win32Exception(unchecked((int)status));
                    }

                    var entries = (LOCALGROUP_INFO_1*)buffer;
                    for (uint index = 0; index < entriesRead; index++)
                    {
                        LOCALGROUP_INFO_1* info = entries + index;
                        groups.Add(new LocalGroup
                        {
                            Name = ReadString(info->lgrpi1_name),
                            Description = ReadString(info->lgrpi1_comment)
                        });
                    }
                }
                finally
                {
                    FreeNetApiBuffer(buffer);
                }
            }
            while (status == ErrorMoreData);
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
    public unsafe void CreateUser(string username, string password, string fullName, string description, bool userCannotChangePassword, bool passwordNeverExpires, bool accountDisabled, bool userMustChangePassword = false)
    {
        uint flags = UF_SCRIPT | UF_NORMAL_ACCOUNT;
        SetUserFlag(ref flags, UF_ACCOUNTDISABLE, accountDisabled);
        SetUserFlag(ref flags, UF_PASSWD_CANT_CHANGE, userCannotChangePassword);
        SetUserFlag(ref flags, UF_DONT_EXPIRE_PASSWD, passwordNeverExpires);

        fixed (char* pName = username)
        fixed (char* pPassword = password)
        fixed (char* pComment = description)
        {
            var info = new USER_INFO_1
            {
                usri1_name = pName,
                usri1_password = pPassword,
                usri1_priv = (USER_PRIV)UserPrivUser,
                usri1_comment = pComment,
                usri1_flags = (USER_ACCOUNT_FLAGS)flags
            };

            uint status = Win32PInvoke.NetUserAdd(default, 1, (byte*)&info, null);
            ThrowOnError(status, "NetUserAdd");
        }

        SetUserFullName(username, fullName);

        if (userMustChangePassword)
        {
            SetPasswordExpired(username, mustChangePassword: true);
        }
    }

    /// <summary>
    /// Creates a new local group.
    /// </summary>
    /// <param name="groupName">The name of the new group.</param>
    /// <param name="description">The description of the group.</param>
    public unsafe void CreateGroup(string groupName, string description)
    {
        fixed (char* pName = groupName)
        fixed (char* pComment = description)
        {
            var info = new LOCALGROUP_INFO_1
            {
                lgrpi1_name = pName,
                lgrpi1_comment = pComment
            };

            uint status = Win32PInvoke.NetLocalGroupAdd(default, 1, (byte*)&info, null);
            ThrowOnError(status, "NetLocalGroupAdd");
        }
    }

    /// <summary>
    /// Deletes a local user.
    /// </summary>
    /// <param name="username">The username of the user to delete.</param>
    public unsafe void DeleteUser(string username)
    {
        fixed (char* pName = username)
        {
            uint status = Win32PInvoke.NetUserDel(default, pName);
            if (status is NerrSuccess or NerrUserNotFound)
            {
                return; // Missing user degrades to a no-op (FindByIdentity-null parity).
            }
            ThrowOnError(status, "NetUserDel");
        }
    }

    /// <summary>
    /// Deletes a local group.
    /// </summary>
    /// <param name="groupName">The name of the group to delete.</param>
    public unsafe void DeleteGroup(string groupName)
    {
        fixed (char* pName = groupName)
        {
            uint status = Win32PInvoke.NetLocalGroupDel(default, pName);
            if (status is NerrSuccess or NerrGroupNotFound or ErrorNoSuchAlias)
            {
                return; // Missing group degrades to a no-op (FindByIdentity-null parity).
            }
            ThrowOnError(status, "NetLocalGroupDel");
        }
    }

    /// <summary>
    /// Sets a new password for the specified user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="newPassword">The new password.</param>
    public unsafe void SetPassword(string username, string newPassword)
    {
        fixed (char* pName = username)
        fixed (char* pPassword = newPassword)
        {
            var info = new USER_INFO_1003 { usri1003_password = pPassword };
            uint status = Win32PInvoke.NetUserSetInfo(default, pName, 1003, (byte*)&info, null);
            if (status is NerrSuccess or NerrUserNotFound)
            {
                return;
            }
            ThrowOnError(status, "NetUserSetInfo(1003)");
        }
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
    public unsafe void UpdateUser(string username, string fullName, string description, bool userCannotChangePassword, bool passwordNeverExpires, bool accountDisabled, bool userMustChangePassword = false)
    {
        // Read the current flags first; a missing user degrades to a no-op (FindByIdentity-null parity)
        // and the read-modify-write preserves unrelated bits exactly as the old UserFlags update did.
        byte* buffer = null;
        uint flags;
        fixed (char* pName = username)
        {
            uint status = Win32PInvoke.NetUserGetInfo(default, pName, 1, &buffer);
            if (status == NerrUserNotFound)
            {
                return;
            }
            ThrowOnError(status, "NetUserGetInfo(1)");
        }

        try
        {
            flags = (uint)((USER_INFO_1*)buffer)->usri1_flags;
        }
        finally
        {
            FreeNetApiBuffer(buffer);
        }

        SetUserFlag(ref flags, UF_ACCOUNTDISABLE, accountDisabled);
        SetUserFlag(ref flags, UF_PASSWD_CANT_CHANGE, userCannotChangePassword);
        SetUserFlag(ref flags, UF_DONT_EXPIRE_PASSWD, passwordNeverExpires);

        SetUserFullName(username, fullName);
        SetUserDescription(username, description);
        SetUserFlags(username, flags);
        SetPasswordExpired(username, userMustChangePassword);
    }

    /// <summary>
    /// Updates an existing local group's description.
    /// </summary>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="description">The new description.</param>
    public unsafe void UpdateGroup(string groupName, string description)
    {
        fixed (char* pName = groupName)
        fixed (char* pComment = description)
        {
            var info = new LOCALGROUP_INFO_1002 { lgrpi1002_comment = pComment };
            uint status = Win32PInvoke.NetLocalGroupSetInfo(default, pName, 1002, (byte*)&info, null);
            if (status is NerrSuccess or NerrGroupNotFound or ErrorNoSuchAlias)
            {
                return;
            }
            ThrowOnError(status, "NetLocalGroupSetInfo(1002)");
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
                unsafe
                {
                    // Level 0 with flags 0 returns the local groups the user is a DIRECT member
                    // of - the same set the old WinNT provider "Groups" enumeration produced
                    // (LG_INCLUDE_INDIRECT would add membership through global groups).
                    uint status = Win32PInvoke.NetUserGetLocalGroups(
                        servername: default,
                        username,
                        0,
                        0,
                        out byte* buffer,
                        MaxPreferredLength,
                        out uint entriesRead,
                        out _);

                    try
                    {
                        if (status != NerrSuccess)
                        {
                            throw new Win32Exception(unchecked((int)status));
                        }

                        var entries = (LOCALGROUP_USERS_INFO_0*)buffer;
                        for (uint index = 0; index < entriesRead; index++)
                        {
                            string name = ReadString(entries[index].lgrui0_name);
                            if (name.Length > 0)
                            {
                                groups.Add(name);
                            }
                        }
                    }
                    finally
                    {
                        FreeNetApiBuffer(buffer);
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
                unsafe
                {
                    nuint resumeHandle = 0;
                    uint status;
                    do
                    {
                        byte* buffer = null;
                        status = Win32PInvoke.NetLocalGroupGetMembers(
                            servername: default,
                            groupName,
                            1,
                            out buffer,
                            MaxPreferredLength,
                            out uint entriesRead,
                            out _,
                            ref resumeHandle);

                        try
                        {
                            if (status != NerrSuccess && status != ErrorMoreData)
                            {
                                throw new Win32Exception(unchecked((int)status));
                            }

                            var entries = (LOCALGROUP_MEMBERS_INFO_1*)buffer;
                            for (uint index = 0; index < entriesRead; index++)
                            {
                                LOCALGROUP_MEMBERS_INFO_1* info = entries + index;
                                string name = ReadString(info->lgrmi1_name);
                                if (name.Length == 0)
                                {
                                    // Orphaned SIDs have no resolvable name; surface the SID string
                                    // like the old WinNT member enumeration did.
                                    name = ConvertSidToString(info->lgrmi1_sid);
                                }
                                if (name.Length > 0)
                                {
                                    members.Add(name);
                                }
                            }
                        }
                        finally
                        {
                            FreeNetApiBuffer(buffer);
                        }
                    }
                    while (status == ErrorMoreData);
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
    public unsafe void AddUserToGroup(string username, string groupName)
    {
        fixed (char* pGroup = groupName)
        fixed (char* pUser = username)
        {
            var member = new LOCALGROUP_MEMBERS_INFO_3 { lgrmi3_domainandname = pUser };
            uint status = Win32PInvoke.NetLocalGroupAddMembers(default, pGroup, 3, (byte*)&member, 1);
            if (status is NerrSuccess or ErrorMemberInAlias // already a member (Members.Contains parity)
                or NerrGroupNotFound or ErrorNoSuchAlias or NerrUserNotFound or ErrorNoSuchMember
                or ErrorInvalidMember)                      // member kind SAM rejects (old code silently skipped non-user principals)
            {
                return;
            }
            ThrowOnError(status, "NetLocalGroupAddMembers");
        }
    }

    /// <summary>
    /// Removes a user from a group.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="groupName">The group name.</param>
    public unsafe void RemoveUserFromGroup(string username, string groupName)
    {
        fixed (char* pGroup = groupName)
        fixed (char* pUser = username)
        {
            var member = new LOCALGROUP_MEMBERS_INFO_3 { lgrmi3_domainandname = pUser };
            uint status = Win32PInvoke.NetLocalGroupDelMembers(default, pGroup, 3, (byte*)&member, 1);
            if (status is NerrSuccess or ErrorMemberNotInAlias // not a member (Members.Contains parity)
                or NerrGroupNotFound or ErrorNoSuchAlias or NerrUserNotFound or ErrorNoSuchMember)
            {
                return;
            }
            ThrowOnError(status, "NetLocalGroupDelMembers");
        }
    }

    // ====================================================================
    // NetUserSetInfo helpers (single-element information levels)
    // ====================================================================

    /// <summary>Sets the full name (info level 1011); a missing user is a no-op.</summary>
    private unsafe void SetUserFullName(string username, string fullName)
    {
        fixed (char* pName = username)
        fixed (char* pFullName = fullName)
        {
            var info = new USER_INFO_1011 { usri1011_full_name = pFullName };
            uint status = Win32PInvoke.NetUserSetInfo(default, pName, 1011, (byte*)&info, null);
            if (status is NerrSuccess or NerrUserNotFound)
            {
                return;
            }
            ThrowOnError(status, "NetUserSetInfo(1011)");
        }
    }

    /// <summary>Sets the description/comment (info level 1007); a missing user is a no-op.</summary>
    private unsafe void SetUserDescription(string username, string description)
    {
        fixed (char* pName = username)
        fixed (char* pComment = description)
        {
            var info = new USER_INFO_1007 { usri1007_comment = pComment };
            uint status = Win32PInvoke.NetUserSetInfo(default, pName, 1007, (byte*)&info, null);
            if (status is NerrSuccess or NerrUserNotFound)
            {
                return;
            }
            ThrowOnError(status, "NetUserSetInfo(1007)");
        }
    }

    /// <summary>Writes the full UF_* flag word (info level 1008); a missing user is a no-op.</summary>
    private unsafe void SetUserFlags(string username, uint flags)
    {
        fixed (char* pName = username)
        {
            var info = new USER_INFO_1008 { usri1008_flags = (USER_ACCOUNT_FLAGS)flags };
            uint status = Win32PInvoke.NetUserSetInfo(default, pName, 1008, (byte*)&info, null);
            if (status is NerrSuccess or NerrUserNotFound)
            {
                return;
            }
            ThrowOnError(status, "NetUserSetInfo(1008)");
        }
    }

    /// <summary>
    /// Sets or clears "user must change password at next logon". There is no 10xx information
    /// level for <c>password_expired</c>, so this is the documented level-4 read-modify-write
    /// (the fetched <c>usri4_password</c> is null, which NetUserSetInfo treats as "unchanged").
    /// Writing 0 cannot un-expire a genuinely expired password - same limitation the old WinNT
    /// <c>PasswordExpired</c> property had. A missing user is a no-op.
    /// </summary>
    private unsafe void SetPasswordExpired(string username, bool mustChangePassword)
    {
        fixed (char* pName = username)
        {
            byte* buffer = null;
            uint status = Win32PInvoke.NetUserGetInfo(default, pName, 4, &buffer);
            if (status == NerrUserNotFound)
            {
                return;
            }
            ThrowOnError(status, "NetUserGetInfo(4)");

            try
            {
                ((USER_INFO_4*)buffer)->usri4_password_expired = mustChangePassword ? 1u : 0u;
                status = Win32PInvoke.NetUserSetInfo(default, pName, 4, buffer, null);
                ThrowOnError(status, "NetUserSetInfo(4)");
            }
            finally
            {
                FreeNetApiBuffer(buffer);
            }
        }
    }

    // ====================================================================
    // Shared helpers
    // ====================================================================

    private static void SetUserFlag(ref uint flags, uint flag, bool value)
    {
        if (value)
        {
            flags |= flag;
        }
        else
        {
            flags &= ~flag;
        }
    }

    private static unsafe string ReadString(PWSTR value) =>
        value.Value is null ? string.Empty : value.ToString();

    private static unsafe string ConvertSidToString(PSID sid)
    {
        if (sid.Value is null || !Win32PInvoke.ConvertSidToStringSid(sid, out PWSTR sidString))
        {
            return string.Empty;
        }

        try
        {
            return sidString.ToString();
        }
        finally
        {
            Win32PInvoke.LocalFree(new HLOCAL(sidString.Value));
        }
    }

    private static unsafe void FreeNetApiBuffer(byte* buffer)
    {
        if (buffer is not null)
        {
            _ = Win32PInvoke.NetApiBufferFree(buffer);
        }
    }

    private static void ThrowOnError(uint status, string operationName)
    {
        if (status == NerrSuccess)
        {
            return;
        }

        var exception = new Win32Exception(unchecked((int)status));
        exception.Data["OperationName"] = operationName;
        throw exception;
    }
}
