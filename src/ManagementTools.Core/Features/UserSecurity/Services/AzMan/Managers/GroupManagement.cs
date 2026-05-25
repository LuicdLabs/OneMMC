// ============================================================================
// AzMan Service - Group Management
// ============================================================================
// Group management functions: create, delete, update groups, manage group members
// ============================================================================

using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

internal sealed class GroupManagement
{
    private readonly AzManService _service;

    public GroupManagement(AzManService service)
    {
        _service = service;
    }

    private ILogger<AzManService> _logger => _service.Logger;

    private Task<T> RunStoreReadAsync<T>(string storePath, Func<object, T> func, string errorMessage)
        => _service.RunStoreReadAsync(storePath, func, errorMessage);
    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<object, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunStoreWriteAsync(string storePath, Action<dynamic> action, string errorMessage, string? debugMessage = null)
        => _service.RunStoreWriteAsync(storePath, action, errorMessage, debugMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunStoreGroupWriteAsync(string storePath, string groupName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitStore = true)
        => _service.RunStoreGroupWriteAsync(storePath, groupName, action, errorMessage, debugMessage, submitStore);
    private Task RunAppGroupWriteAsync(string storePath, string appName, string groupName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitApp = true)
        => _service.RunAppGroupWriteAsync(storePath, appName, groupName, action, errorMessage, debugMessage, submitApp);

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
            storeObj =>
            {
                dynamic store = storeObj;
                dynamic group = store.CreateApplicationGroup(name);

                // Set group type using COM API values (enum values match COM constants)
                group.Type = (int)groupType;

                if (!string.IsNullOrEmpty(description))
                {
                    group.Description = description;
                }

                if (groupType == AzGroupType.LdapQuery && !string.IsNullOrEmpty(ldapQuery))
                {
                    group.LdapQuery = ldapQuery;
                }

                group.Submit();
                store.Submit();

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
            appObj =>
            {
                dynamic app = appObj;
                dynamic group = app.CreateApplicationGroup(name);

                // Set group type using COM API values (enum values match COM constants)
                group.Type = (int)groupType;

                if (!string.IsNullOrEmpty(description))
                {
                    group.Description = description;
                }

                if (groupType == AzGroupType.LdapQuery && !string.IsNullOrEmpty(ldapQuery))
                {
                    group.LdapQuery = ldapQuery;
                }

                group.Submit();
                app.Submit();

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
            store => store.DeleteApplicationGroup(groupName),
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
            app => app.DeleteApplicationGroup(groupName),
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
                group.Description = description;

                // Only LDAP query groups can set LdapQuery
                if (!string.IsNullOrEmpty(ldapQuery) && (int)group.Type == AzManService.AZ_GROUPTYPE_LDAP_QUERY)
                {
                    group.LdapQuery = ldapQuery;
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
                group.BizRuleLanguage = bizRuleLanguage;
                group.BizRule = bizRule;
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }

                if (isAppGroup)
                {
                    group.AddAppMember(memberSid);
                }
                else
                {
                    group.AddMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }

                if (isAppGroup)
                {
                    group.DeleteAppMember(memberSid);
                }
                else
                {
                    group.DeleteMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.AddMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.DeleteMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.AddAppMember(appGroupName);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.DeleteAppMember(appGroupName);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }

                if (isAppGroup)
                {
                    group.AddAppNonMember(memberSid);
                }
                else
                {
                    group.AddNonMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }

                if (isAppGroup)
                {
                    group.DeleteAppNonMember(memberSid);
                }
                else
                {
                    group.DeleteNonMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.AddNonMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.DeleteNonMember(memberSid);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.AddAppNonMember(appGroupName);
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
                if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                {
                    throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                }
                group.DeleteAppNonMember(appGroupName);
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
                group.Description = description;

                // Only LDAP query groups can set LdapQuery
                if (!string.IsNullOrEmpty(ldapQuery) && (int)group.Type == AzManService.AZ_GROUPTYPE_LDAP_QUERY)
                {
                    group.LdapQuery = ldapQuery;
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
                group.BizRuleLanguage = bizRuleLanguage;
                group.BizRule = bizRule;
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


