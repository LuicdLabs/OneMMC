// ============================================================================
// AzMan Service - Group Management
// ============================================================================
// Group management functions: create, delete, update groups, manage group members
// ============================================================================

using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class GroupManagement
{
    private readonly AzManService _service;

    public GroupManagement(AzManService service)
    {
        _service = service;
    }

    private ILogger<AzManService> _logger => _service.Logger;

    private Task<T> RunStoreReadAsync<T>(string storePath, Func<IAzAuthorizationStore3, T> func, string errorMessage)
        => _service.RunStoreReadAsync(storePath, func, errorMessage);
    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<IAzApplication, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunStoreWriteAsync(string storePath, Action<IAzAuthorizationStore3> action, string errorMessage, string? debugMessage = null)
        => _service.RunStoreWriteAsync(storePath, action, errorMessage, debugMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<IAzApplication> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunStoreGroupWriteAsync(string storePath, string groupName, Action<IAzApplicationGroup2> action, string errorMessage, string? debugMessage = null, bool submitStore = true)
        => _service.RunStoreGroupWriteAsync(storePath, groupName, action, errorMessage, debugMessage, submitStore);
    private Task RunAppGroupWriteAsync(string storePath, string appName, string groupName, Action<IAzApplicationGroup2> action, string errorMessage, string? debugMessage = null, bool submitApp = true)
        => _service.RunAppGroupWriteAsync(storePath, appName, groupName, action, errorMessage, debugMessage, submitApp);

    /// <summary>Throws when the group is not a Basic group (only Basic groups have editable member lists).</summary>
    private static void EnsureBasicGroup(IAzApplicationGroup2 group, string groupName)
    {
        if (group.get_Type() != AzManService.AZ_GROUPTYPE_BASIC)
        {
            throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
        }
    }

    #region Group Management

    /// <summary>
    /// Create an application group (store level)
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="name">Group name</param>
    /// <param name="groupType">Group type</param>
    /// <param name="description">Description</param>
    /// <param name="ldapQuery">LDAP query (for LDAP query groups only)</param>
    /// <returns>Created group information</returns>
    public async Task<AzApplicationGroupInfo> CreateStoreGroupAsync(
        string storePath,
        string name,
        AzGroupType groupType,
        string description = "",
        string ldapQuery = "")
    {
        return await RunStoreReadAsync(
            storePath,
            store =>
            {
                store.CreateApplicationGroup(name, Variant.Missing, out IAzApplicationGroup2 group);
                try
                {
                    // Set group type using COM API values (enum values match COM constants)
                    group.put_Type((int)groupType);

                    if (!string.IsNullOrEmpty(description))
                    {
                        group.put_Description(description);
                    }

                    if (groupType == AzGroupType.LdapQuery && !string.IsNullOrEmpty(ldapQuery))
                    {
                        group.put_LdapQuery(ldapQuery);
                    }

                    group.Submit(0, Variant.Missing);
                    store.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(group);
                }

                _logger.LogInformation("Successfully created store group: {GroupName}", name);
                return new AzApplicationGroupInfo
                {
                    Name = name,
                    Description = description,
                    GroupType = groupType,
                    LdapQuery = ldapQuery
                };
            },
            "Failed to create group");
    }

    /// <summary>
    /// Create an application group (application level)
    /// </summary>
    public async Task<AzApplicationGroupInfo> CreateAppGroupAsync(
        string storePath,
        string appName,
        string name,
        AzGroupType groupType,
        string description = "",
        string ldapQuery = "")
    {
        return await RunApplicationReadAsync(
            storePath,
            appName,
            app =>
            {
                app.CreateApplicationGroup(name, Variant.Missing, out IAzApplicationGroup2 group);
                try
                {
                    // Set group type using COM API values (enum values match COM constants)
                    group.put_Type((int)groupType);

                    if (!string.IsNullOrEmpty(description))
                    {
                        group.put_Description(description);
                    }

                    if (groupType == AzGroupType.LdapQuery && !string.IsNullOrEmpty(ldapQuery))
                    {
                        group.put_LdapQuery(ldapQuery);
                    }

                    group.Submit(0, Variant.Missing);
                    app.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(group);
                }

                return new AzApplicationGroupInfo
                {
                    Name = name,
                    Description = description,
                    GroupType = groupType,
                    LdapQuery = ldapQuery
                };
            },
            "Failed to create application group");
    }

    /// <summary>
    /// Delete a store-level group
    /// </summary>
    public async Task DeleteStoreGroupAsync(string storePath, string groupName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.DeleteApplicationGroup(groupName, Variant.Missing),
            "Failed to delete group",
            $"[AzManService] Successfully deleted store group: {groupName}");
    }

    /// <summary>
    /// Delete an application-level group
    /// </summary>
    public async Task DeleteAppGroupAsync(string storePath, string appName, string groupName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeleteApplicationGroup(groupName, Variant.Missing),
            "Failed to delete application group",
            $"[AzManService] Successfully deleted application group: {groupName}");
    }

    /// <summary>
    /// Update group information
    /// </summary>
    public async Task UpdateStoreGroupAsync(string storePath, string groupName, string description, string ldapQuery = "")
    {
        await RunStoreGroupWriteAsync(
            storePath,
            groupName,
            group =>
            {
                group.put_Description(description);

                // Only LDAP query groups can set LdapQuery
                if (!string.IsNullOrEmpty(ldapQuery) && group.get_Type() == AzManService.AZ_GROUPTYPE_LDAP_QUERY)
                {
                    group.put_LdapQuery(ldapQuery);
                }
            },
            "Failed to update group");
    }

    /// <summary>
    /// Set business rule script for a store-level group.
    /// </summary>
    public async Task SetStoreGroupBizRuleAsync(string storePath, string groupName, string bizRule, string bizRuleLanguage)
    {
        await RunStoreGroupWriteAsync(
            storePath,
            groupName,
            group =>
            {
                group.put_BizRuleLanguage(bizRuleLanguage);
                group.put_BizRule(bizRule);
            },
            "Failed to set group business rule");
    }

    /// <summary>
    /// Add a member to a group (store level)
    /// </summary>
    public async Task AddGroupMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false)
    {
        if (!isAppGroup)
        {
            ValidateSid(memberSid);
        }

        await RunStoreGroupWriteAsync(
            storePath,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);

                if (isAppGroup)
                {
                    group.AddAppMember(memberSid, Variant.Missing);
                }
                else
                {
                    group.AddMember(memberSid, Variant.Missing);
                }
            },
            $"Failed to add group member (SID: {memberSid})");
    }

    /// <summary>
    /// Remove a member from a group (store level)
    /// </summary>
    public async Task RemoveGroupMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false)
    {
        if (!isAppGroup)
        {
            ValidateSid(memberSid);
        }

        await RunStoreGroupWriteAsync(
            storePath,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);

                if (isAppGroup)
                {
                    group.DeleteAppMember(memberSid, Variant.Missing);
                }
                else
                {
                    group.DeleteMember(memberSid, Variant.Missing);
                }
            },
            "Failed to remove group member");
    }

    /// <summary>
    /// Add a member to a group (application level)
    /// </summary>
    public async Task AddGroupMemberAsync(string storePath, string appName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.AddMember(memberSid, Variant.Missing);
            },
            $"Failed to add group member (SID: {memberSid})");
    }

    /// <summary>
    /// Remove a member from a group (application level)
    /// </summary>
    public async Task RemoveGroupMemberAsync(string storePath, string appName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.DeleteMember(memberSid, Variant.Missing);
            },
            "Failed to remove group member");
    }

    /// <summary>
    /// Add an application group link as member (application level)
    /// </summary>
    public async Task AddAppMemberToGroupAsync(string storePath, string appName, string groupName, string appGroupName)
    {
        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.AddAppMember(appGroupName, Variant.Missing);
            },
            "Failed to add app member to group",
            $"[AzManService] Added app member '{appGroupName}' to group '{groupName}'");
    }

    /// <summary>
    /// Remove an application group link from members (application level)
    /// </summary>
    public async Task RemoveAppMemberFromGroupAsync(string storePath, string appName, string groupName, string appGroupName)
    {
        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.DeleteAppMember(appGroupName, Variant.Missing);
            },
            "Failed to remove app member from group",
            $"[AzManService] Removed app member '{appGroupName}' from group '{groupName}'");
    }

    /// <summary>
    /// Add a non-member to a group (store level)
    /// </summary>
    public async Task AddGroupNonMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false)
    {
        if (!isAppGroup)
        {
            ValidateSid(memberSid);
        }

        await RunStoreGroupWriteAsync(
            storePath,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);

                if (isAppGroup)
                {
                    group.AddAppNonMember(memberSid, Variant.Missing);
                }
                else
                {
                    group.AddNonMember(memberSid, Variant.Missing);
                }
            },
            "Failed to add group non-member",
            $"[AzManService] Added non-member to store group '{groupName}'");
    }

    /// <summary>
    /// Remove a non-member from a group (store level)
    /// </summary>
    public async Task RemoveGroupNonMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false)
    {
        if (!isAppGroup)
        {
            ValidateSid(memberSid);
        }

        await RunStoreGroupWriteAsync(
            storePath,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);

                if (isAppGroup)
                {
                    group.DeleteAppNonMember(memberSid, Variant.Missing);
                }
                else
                {
                    group.DeleteNonMember(memberSid, Variant.Missing);
                }
            },
            "Failed to remove group non-member",
            $"[AzManService] Removed non-member from store group '{groupName}'");
    }

    /// <summary>
    /// Add a non-member to a group (application level)
    /// </summary>
    public async Task AddGroupNonMemberAsync(string storePath, string appName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.AddNonMember(memberSid, Variant.Missing);
            },
            "Failed to add group non-member",
            $"[AzManService] Added non-member to app group '{groupName}'");
    }

    /// <summary>
    /// Remove a non-member from a group (application level)
    /// </summary>
    public async Task RemoveGroupNonMemberAsync(string storePath, string appName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.DeleteNonMember(memberSid, Variant.Missing);
            },
            "Failed to remove group non-member",
            $"[AzManService] Removed non-member from app group '{groupName}'");
    }

    /// <summary>
    /// Add an application group link as non-member (application level)
    /// </summary>
    public async Task AddAppNonMemberToGroupAsync(string storePath, string appName, string groupName, string appGroupName)
    {
        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.AddAppNonMember(appGroupName, Variant.Missing);
            },
            "Failed to add app non-member to group",
            $"[AzManService] Added app non-member '{appGroupName}' to group '{groupName}'");
    }

    /// <summary>
    /// Remove an application group link from non-members (application level)
    /// </summary>
    public async Task RemoveAppNonMemberFromGroupAsync(string storePath, string appName, string groupName, string appGroupName)
    {
        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                EnsureBasicGroup(group, groupName);
                group.DeleteAppNonMember(appGroupName, Variant.Missing);
            },
            "Failed to remove app non-member from group",
            $"[AzManService] Removed app non-member '{appGroupName}' from group '{groupName}'");
    }

    /// <summary>
    /// Update application-level group information
    /// </summary>
    public async Task UpdateAppGroupAsync(string storePath, string appName, string groupName, string description, string ldapQuery = "")
    {
        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                group.put_Description(description);

                // Only LDAP query groups can set LdapQuery
                if (!string.IsNullOrEmpty(ldapQuery) && group.get_Type() == AzManService.AZ_GROUPTYPE_LDAP_QUERY)
                {
                    group.put_LdapQuery(ldapQuery);
                }
            },
            "Failed to update application group",
            $"[AzManService] Updated app group '{groupName}'");
    }

    /// <summary>
    /// Set business rule script for an application-level group.
    /// </summary>
    public async Task SetAppGroupBizRuleAsync(string storePath, string appName, string groupName, string bizRule, string bizRuleLanguage)
    {
        await RunAppGroupWriteAsync(
            storePath,
            appName,
            groupName,
            group =>
            {
                group.put_BizRuleLanguage(bizRuleLanguage);
                group.put_BizRule(bizRule);
            },
            "Failed to set application group business rule",
            $"[AzManService] Updated business rule for app group '{groupName}'");
    }

    #endregion

    /// <summary>
    /// Validates that a SID string is non-empty and represents a valid security identifier.
    /// </summary>
    private static void ValidateSid(string memberSid)
    {
        if (string.IsNullOrWhiteSpace(memberSid))
        {
            throw new AzManException("The security identifier (SID) is empty. The account may not have been resolved correctly.");
        }

        try
        {
            _ = new SecurityIdentifier(memberSid);
        }
        catch (ArgumentException)
        {
            throw new AzManException($"'{memberSid}' is not a valid security identifier (SID).");
        }
    }
}
