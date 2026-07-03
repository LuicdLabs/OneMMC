// ============================================================================
// AzMan Service - Scope Management
// ============================================================================
// Scope management functions: create, delete, update scopes
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class ScopeManagement
{
    private readonly AzManService _service;

    public ScopeManagement(AzManService service)
    {
        _service = service;
    }

    private ILogger<AzManService> _logger => _service.Logger;

    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<IAzApplication, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<IAzApplication> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task<T> RunScopeReadAsync<T>(string storePath, string appName, string scopeName, Func<IAzScope, T> func, string errorMessage)
        => _service.RunScopeReadAsync(storePath, appName, scopeName, func, errorMessage);
    private Task RunScopeWriteAsync(string storePath, string appName, string scopeName, Action<IAzScope> action, string errorMessage, string? debugMessage = null, bool submitScope = true, bool submitApp = false)
        => _service.RunScopeWriteAsync(storePath, appName, scopeName, action, errorMessage, debugMessage, submitScope, submitApp);
    private AzScopeInfo? ReadScopeInfo(IAzScope scope) => _service.ReadScopeInfo(scope);

    /// <summary>
    /// Opens the named group in the scope, runs <paramref name="action"/>, submits the group and
    /// releases it. Shared by every scope-group mutation below.
    /// </summary>
    private static void WithScopeGroup(IAzScope scope, string groupName, Action<IAzApplicationGroup2> action)
    {
        scope.OpenApplicationGroup(groupName, Variant.Missing, out IAzApplicationGroup2 group);
        try
        {
            action(group);
            group.Submit(0, Variant.Missing);
        }
        finally
        {
            AzRolesCom.Release(group);
        }
    }

    /// <summary>
    /// Opens the named role in the scope, runs <paramref name="action"/>, submits the role and
    /// releases it. Shared by every scope-role mutation below.
    /// </summary>
    private static void WithScopeRole(IAzScope scope, string roleName, Action<IAzRole> action)
    {
        scope.OpenRole(roleName, Variant.Missing, out IAzRole role);
        try
        {
            action(role);
            role.Submit(0, Variant.Missing);
        }
        finally
        {
            AzRolesCom.Release(role);
        }
    }

    /// <summary>
    /// Opens the named task in the scope, runs <paramref name="action"/>, submits the task and
    /// releases it. Shared by every scope-task mutation below.
    /// </summary>
    private static void WithScopeTask(IAzScope scope, string taskName, Action<IAzTask> action)
    {
        scope.OpenTask(taskName, Variant.Missing, out IAzTask task);
        try
        {
            action(task);
            task.Submit(0, Variant.Missing);
        }
        finally
        {
            AzRolesCom.Release(task);
        }
    }

    /// <summary>Throws when the group is not a Basic group (only Basic groups have editable member lists).</summary>
    private static void EnsureBasicGroup(IAzApplicationGroup2 group, string groupName)
    {
        if (group.get_Type() != AzManService.AZ_GROUPTYPE_BASIC)
        {
            throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
        }
    }

    #region Scope Management

    /// <summary>
    /// Create a scope
    /// </summary>
    public async Task<AzScopeInfo> CreateScopeAsync(
        string storePath,
        string appName,
        string name,
        string description = "")
    {
        return await RunApplicationReadAsync(
            storePath,
            appName,
            app =>
            {
                app.CreateScope(name, Variant.Missing, out IAzScope scope);
                try
                {
                    scope.put_Description(description);
                    scope.Submit(0, Variant.Missing);
                    app.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(scope);
                }

                return new AzScopeInfo
                {
                    Name = name,
                    Description = description
                };
            },
            "Failed to create scope");
    }

    /// <summary>
    /// Update a scope
    /// </summary>
    public async Task UpdateScopeAsync(
        string storePath,
        string appName,
        string name,
        string description)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            name,
            scope => scope.put_Description(description),
            "Failed to update scope",
            submitScope: true,
            submitApp: true);
    }

    /// <summary>
    /// Delete a scope
    /// </summary>
    public async Task DeleteScopeAsync(string storePath, string appName, string scopeName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeleteScope(scopeName, Variant.Missing),
            "Failed to delete scope");
    }

    /// <summary>
    /// Get a scope with its contents
    /// </summary>
    public async Task<AzScopeInfo> GetScopeAsync(string storePath, string appName, string scopeName)
    {
        return await RunScopeReadAsync(
            storePath,
            appName,
            scopeName,
            scope => ReadScopeInfo(scope)!,
            "Failed to get scope");
    }

    #endregion

    #region Scope Group Management

    /// <summary>
    /// Create a group in a scope
    /// </summary>
    public async Task<AzApplicationGroupInfo> CreateScopeGroupAsync(
        string storePath, string appName, string scopeName,
        string name, AzGroupType groupType, string description = "", string ldapQuery = "")
    {
        return await RunScopeReadAsync(
            storePath,
            appName,
            scopeName,
            scope =>
            {
                scope.CreateApplicationGroup(name, Variant.Missing, out IAzApplicationGroup2 group);
                try
                {
                    group.put_Type((int)groupType);
                    group.put_Description(description);
                    if (groupType == AzGroupType.LdapQuery && !string.IsNullOrEmpty(ldapQuery))
                    {
                        group.put_LdapQuery(ldapQuery);
                    }
                    group.Submit(0, Variant.Missing);
                    scope.Submit(0, Variant.Missing);
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
            "Failed to create scope group");
    }

    /// <summary>
    /// Add a member to a scope group
    /// </summary>
    public async Task AddScopeGroupMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeGroup(scope, groupName, group =>
            {
                EnsureBasicGroup(group, groupName);
                group.AddMember(memberSid, Variant.Missing);
            }),
            "Failed to add scope group member",
            submitScope: false);
    }

    /// <summary>
    /// Remove a member from a scope group
    /// </summary>
    public async Task RemoveScopeGroupMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeGroup(scope, groupName, group =>
            {
                EnsureBasicGroup(group, groupName);
                group.DeleteMember(memberSid, Variant.Missing);
            }),
            "Failed to remove scope group member",
            submitScope: false);
    }

    /// <summary>
    /// Add a non-member to a scope group
    /// </summary>
    public async Task AddScopeGroupNonMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeGroup(scope, groupName, group =>
            {
                EnsureBasicGroup(group, groupName);
                group.AddNonMember(memberSid, Variant.Missing);
            }),
            "Failed to add scope group non-member",
            submitScope: false);
    }

    /// <summary>
    /// Remove a non-member from a scope group
    /// </summary>
    public async Task RemoveScopeGroupNonMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeGroup(scope, groupName, group =>
            {
                EnsureBasicGroup(group, groupName);
                group.DeleteNonMember(memberSid, Variant.Missing);
            }),
            "Failed to remove scope group non-member",
            submitScope: false);
    }

    /// <summary>
    /// Delete a group from a scope
    /// </summary>
    public async Task DeleteScopeGroupAsync(string storePath, string appName, string scopeName, string groupName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => scope.DeleteApplicationGroup(groupName, Variant.Missing),
            "Failed to delete scope group",
            submitScope: true);
    }

    /// <summary>
    /// Set business rule script for a scope group.
    /// </summary>
    public async Task SetScopeGroupBizRuleAsync(string storePath, string appName, string scopeName, string groupName, string bizRule, string bizRuleLanguage)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeGroup(scope, groupName, group =>
            {
                group.put_BizRuleLanguage(bizRuleLanguage);
                group.put_BizRule(bizRule);
            }),
            "Failed to set scope group business rule",
            submitScope: false);
    }

    #endregion

    #region Scope Role Assignment Management

    /// <summary>
    /// Create a role assignment in a scope
    /// </summary>
    public async Task<AzRoleAssignmentInfo> CreateScopeRoleAssignmentAsync(
        string storePath, string appName, string scopeName, string name, string description = "")
    {
        return await RunScopeReadAsync(
            storePath,
            appName,
            scopeName,
            scope =>
            {
                scope.CreateRole(name, Variant.Missing, out IAzRole role);
                try
                {
                    role.put_Description(description);
                    role.Submit(0, Variant.Missing);
                    scope.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(role);
                }

                return new AzRoleAssignmentInfo
                {
                    Name = name,
                    Description = description
                };
            },
            "Failed to create scope role assignment");
    }

    /// <summary>
    /// Add a member to a scope role assignment
    /// </summary>
    public async Task AddScopeRoleAssignmentMemberAsync(string storePath, string appName, string scopeName, string roleName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.AddMember(memberSid, Variant.Missing)),
            "Failed to add scope role member",
            submitScope: false);
    }

    /// <summary>
    /// Delete a role assignment from a scope
    /// </summary>
    public async Task DeleteScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => scope.DeleteRole(roleName, Variant.Missing),
            "Failed to delete scope role assignment",
            submitScope: true);
    }

    #endregion

    #region Scope Role Definition Management

    /// <summary>
    /// Create a role definition in a scope
    /// </summary>
    public async Task<AzRoleDefinitionInfo> CreateScopeRoleDefinitionAsync(
        string storePath, string appName, string scopeName, string name, string description = "")
    {
        return await RunScopeReadAsync(
            storePath,
            appName,
            scopeName,
            scope =>
            {
                scope.CreateTask(name, Variant.Missing, out IAzTask task);
                try
                {
                    task.put_Description(description);
                    task.put_IsRoleDefinition(AzRolesCom.FromBool(true));
                    task.Submit(0, Variant.Missing);
                    scope.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(task);
                }

                return new AzRoleDefinitionInfo
                {
                    Name = name,
                    Description = description
                };
            },
            "Failed to create scope role definition");
    }

    /// <summary>
    /// Update a role definition in a scope
    /// </summary>
    public async Task UpdateScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string name, string description)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, name, task => task.put_Description(description)),
            "Failed to update scope role definition",
            submitScope: false);
    }

    /// <summary>
    /// Delete a role definition from a scope
    /// </summary>
    public async Task DeleteScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string name)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => scope.DeleteTask(name, Variant.Missing),
            "Failed to delete scope role definition",
            submitScope: true);
    }

    #endregion

    #region Scope Task Management

    /// <summary>
    /// Create a task in a scope
    /// </summary>
    public async Task<AzTaskInfo> CreateScopeTaskAsync(
        string storePath, string appName, string scopeName, string name, string description = "")
    {
        return await RunScopeReadAsync(
            storePath,
            appName,
            scopeName,
            scope =>
            {
                scope.CreateTask(name, Variant.Missing, out IAzTask task);
                try
                {
                    task.put_Description(description);
                    task.Submit(0, Variant.Missing);
                    scope.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(task);
                }

                return new AzTaskInfo
                {
                    Name = name,
                    Description = description
                };
            },
            "Failed to create scope task");
    }

    /// <summary>
    /// Update a task in a scope
    /// </summary>
    public async Task UpdateScopeTaskAsync(string storePath, string appName, string scopeName, string name, string description)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, name, task => task.put_Description(description)),
            "Failed to update scope task",
            submitScope: false);
    }

    /// <summary>
    /// Delete a task from a scope
    /// </summary>
    public async Task DeleteScopeTaskAsync(string storePath, string appName, string scopeName, string name)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => scope.DeleteTask(name, Variant.Missing),
            "Failed to delete scope task",
            submitScope: true);
    }

    /// <summary>
    /// Add an operation to a scope task
    /// </summary>
    public async Task AddOperationToScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string operationName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, taskName, task => task.AddOperation(operationName, Variant.Missing)),
            "Failed to add operation to scope task",
            submitScope: true);
    }

    /// <summary>
    /// Remove an operation from a scope task
    /// </summary>
    public async Task RemoveOperationFromScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string operationName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, taskName, task => task.DeleteOperation(operationName, Variant.Missing)),
            "Failed to remove operation from scope task",
            submitScope: true);
    }

    /// <summary>
    /// Add a task link to a scope task.
    /// </summary>
    public async Task AddTaskLinkToScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string linkedTaskName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, taskName, task => task.AddTask(linkedTaskName, Variant.Missing)),
            "Failed to add task link to scope task",
            submitScope: true);
    }

    /// <summary>
    /// Remove a task link from a scope task.
    /// </summary>
    public async Task RemoveTaskLinkFromScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string linkedTaskName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, taskName, task => task.DeleteTask(linkedTaskName, Variant.Missing)),
            "Failed to remove task link from scope task",
            submitScope: true);
    }

    /// <summary>
    /// Set business rule on a scope task.
    /// </summary>
    public async Task SetScopeTaskBizRuleAsync(string storePath, string appName, string scopeName, string taskName, string bizRule, string bizRuleLanguage)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, taskName, task =>
            {
                task.put_BizRuleLanguage(bizRuleLanguage);
                task.put_BizRule(bizRule);
            }),
            "Failed to set business rule on scope task",
            submitScope: false);
    }

    /// <summary>
    /// Clear business rule on a scope task.
    /// </summary>
    public async Task ClearScopeTaskBizRuleAsync(string storePath, string appName, string scopeName, string taskName)
    {
        await SetScopeTaskBizRuleAsync(storePath, appName, scopeName, taskName, string.Empty, string.Empty);
    }

    /// <summary>
    /// Set business rule on a scope role definition.
    /// </summary>
    public async Task SetScopeRoleDefinitionBizRuleAsync(string storePath, string appName, string scopeName, string roleDefName, string bizRule, string bizRuleLanguage)
    {
        await SetScopeTaskBizRuleAsync(storePath, appName, scopeName, roleDefName, bizRule, bizRuleLanguage);
    }

    /// <summary>
    /// Clear business rule on a scope role definition.
    /// </summary>
    public async Task ClearScopeRoleDefinitionBizRuleAsync(string storePath, string appName, string scopeName, string roleDefName)
    {
        await ClearScopeTaskBizRuleAsync(storePath, appName, scopeName, roleDefName);
    }

    /// <summary>
    /// Import business rule from file for a scope task.
    /// </summary>
    public async Task ImportScopeTaskBizRuleAsync(string storePath, string appName, string scopeName, string taskName, string filePath, string bizRuleLanguage)
    {
        string bizRule = System.IO.File.ReadAllText(filePath);

        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, taskName, task =>
            {
                task.put_BizRuleLanguage(bizRuleLanguage);
                task.put_BizRule(bizRule);
                task.put_BizRuleImportedPath(filePath);
            }),
            "Failed to import business rule into scope task",
            submitScope: false);
    }

    /// <summary>
    /// Import business rule from file for a scope role definition.
    /// </summary>
    public async Task ImportScopeRoleDefinitionBizRuleAsync(string storePath, string appName, string scopeName, string roleDefName, string filePath, string bizRuleLanguage)
    {
        await ImportScopeTaskBizRuleAsync(storePath, appName, scopeName, roleDefName, filePath, bizRuleLanguage);
    }

    #endregion

    #region Scope Role Assignment Extended Management

    /// <summary>
    /// Remove a member from a scope role assignment
    /// </summary>
    public async Task RemoveScopeRoleAssignmentMemberAsync(string storePath, string appName, string scopeName, string roleName, string memberSid)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.DeleteMember(memberSid, Variant.Missing)),
            "Failed to remove scope role member",
            submitScope: false);
    }

    /// <summary>
    /// Add a task to a scope role assignment
    /// </summary>
    public async Task AddTaskToScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string taskName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.AddTask(taskName, Variant.Missing)),
            "Failed to add task to scope role assignment",
            submitScope: true);
    }

    /// <summary>
    /// Remove a task from a scope role assignment
    /// </summary>
    public async Task RemoveTaskFromScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string taskName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.DeleteTask(taskName, Variant.Missing)),
            "Failed to remove task from scope role assignment",
            submitScope: true);
    }

    /// <summary>
    /// Add an operation to a scope role assignment
    /// </summary>
    public async Task AddOperationToScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string operationName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.AddOperation(operationName, Variant.Missing)),
            "Failed to add operation to scope role assignment",
            submitScope: true);
    }

    /// <summary>
    /// Remove an operation from a scope role assignment
    /// </summary>
    public async Task RemoveOperationFromScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string operationName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.DeleteOperation(operationName, Variant.Missing)),
            "Failed to remove operation from scope role assignment",
            submitScope: true);
    }

    /// <summary>
    /// Add an application group member to a scope role assignment
    /// </summary>
    public async Task AddAppMemberToScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string appGroupName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.AddAppMember(appGroupName, Variant.Missing)),
            "Failed to add app member to scope role assignment",
            submitScope: true);
    }

    /// <summary>
    /// Remove an application group member from a scope role assignment
    /// </summary>
    public async Task RemoveAppMemberFromScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string appGroupName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.DeleteAppMember(appGroupName, Variant.Missing)),
            "Failed to remove app member from scope role assignment",
            submitScope: true);
    }

    /// <summary>
    /// Update a scope role assignment
    /// </summary>
    public async Task UpdateScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string description)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeRole(scope, roleName, role => role.put_Description(description)),
            "Failed to update scope role assignment",
            submitScope: true);
    }

    #endregion

    #region Scope Role Definition Extended Management

    /// <summary>
    /// Add a task to a scope role definition
    /// </summary>
    public async Task AddTaskToScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string taskName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, roleDefName, task => task.AddTask(taskName, Variant.Missing)),
            "Failed to add task to scope role definition",
            submitScope: true);
    }

    /// <summary>
    /// Remove a task from a scope role definition
    /// </summary>
    public async Task RemoveTaskFromScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string taskName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, roleDefName, task => task.DeleteTask(taskName, Variant.Missing)),
            "Failed to remove task from scope role definition",
            submitScope: true);
    }

    /// <summary>
    /// Add an operation to a scope role definition
    /// </summary>
    public async Task AddOperationToScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string operationName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, roleDefName, task => task.AddOperation(operationName, Variant.Missing)),
            "Failed to add operation to scope role definition",
            submitScope: true);
    }

    /// <summary>
    /// Remove an operation from a scope role definition
    /// </summary>
    public async Task RemoveOperationFromScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string operationName)
    {
        await RunScopeWriteAsync(
            storePath,
            appName,
            scopeName,
            scope => WithScopeTask(scope, roleDefName, task => task.DeleteOperation(operationName, Variant.Missing)),
            "Failed to remove operation from scope role definition",
            submitScope: true);
    }

    #endregion

    /// <summary>
    /// Validates that a SID string is non-empty and represents a valid security identifier.
    /// </summary>
    private static void ValidateSid(string memberSid)
    {
        if (string.IsNullOrWhiteSpace(memberSid))
        {
            throw new AzManException("Cannot add member: the security identifier (SID) is empty. The account may not have been resolved correctly.");
        }

        try
        {
            _ = new SecurityIdentifier(memberSid);
        }
        catch (ArgumentException)
        {
            throw new AzManException($"Cannot add member: '{memberSid}' is not a valid security identifier (SID).");
        }
    }
}
