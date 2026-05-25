// ============================================================================
// AzMan Service - Scope Management
// ============================================================================
// Scope management functions: create, delete, update scopes
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

internal sealed class ScopeManagement
{
    private readonly AzManService _service;

    public ScopeManagement(AzManService service)
    {
        _service = service;
    }

    private ILogger<AzManService> _logger => _service.Logger;

    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<object, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task<T> RunScopeReadAsync<T>(string storePath, string appName, string scopeName, Func<object, T> func, string errorMessage)
        => _service.RunScopeReadAsync(storePath, appName, scopeName, func, errorMessage);
    private Task RunScopeWriteAsync(string storePath, string appName, string scopeName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitScope = true, bool submitApp = false)
        => _service.RunScopeWriteAsync(storePath, appName, scopeName, action, errorMessage, debugMessage, submitScope, submitApp);
    private AzApplicationGroupInfo? ReadGroupInfo(object group) => _service.ReadGroupInfo(group);
    private AzRoleAssignmentInfo? ReadRoleAssignmentInfo(object role) => _service.ReadRoleAssignmentInfo(role);
    private AzRoleDefinitionInfo? ReadRoleDefinitionFromTask(object task) => _service.ReadRoleDefinitionFromTask(task);
    private AzTaskInfo? ReadTaskInfo(object task) => _service.ReadTaskInfo(task);

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
            appObj =>
            {
                dynamic app = appObj;
                dynamic scope = app.CreateScope(name);
                try
                {
                    scope.Description = description;
                    scope.Submit();
                    app.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(scope);
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
            scope => scope.Description = description,
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
            app => app.DeleteScope(scopeName),
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
            scopeObj =>
            {
                var scopeInfo = new AzScopeInfo
                {
                    Name = ComPropertyAccessor.GetString(scopeObj, "Name"),
                    Description = ComPropertyAccessor.GetString(scopeObj, "Description"),
                    ApplicationData = ComPropertyAccessor.GetString(scopeObj, "ApplicationData"),
                    IsWritable = ComPropertyAccessor.GetBool(scopeObj, "Writable")
                };

                // Read groups within scope
                scopeInfo.Groups = ComPropertyAccessor.GetCollection(scopeObj, "ApplicationGroups", obj => ReadGroupInfo(obj), true);

                // Read role assignments within scope
                scopeInfo.RoleAssignments = ComPropertyAccessor.GetCollection(scopeObj, "Roles", obj => ReadRoleAssignmentInfo(obj), true);

                // Read tasks and role definitions within scope
                var allTasks = ComPropertyAccessor.GetCollection(scopeObj, "Tasks", (object task) =>
                {
                    bool isRoleDef = ComPropertyAccessor.GetBool(task, "IsRoleDefinition");
                    return new { Task = task, IsRoleDefinition = isRoleDef };
                });

                foreach (var item in allTasks)
                {
                    try
                    {
                        if (item.IsRoleDefinition)
                        {
                            var roleDef = ReadRoleDefinitionFromTask(item.Task);
                            if (roleDef != null)
                            {
                                scopeInfo.Roles.Add(roleDef);
                            }
                        }
                        else
                        {
                            var taskInfo = ReadTaskInfo(item.Task);
                            if (taskInfo != null)
                            {
                                scopeInfo.Tasks.Add(taskInfo);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[AzManService] Failed to read scope task/role: {ex.Message}");
                    }
                    finally
                    {
                        ComPropertyAccessor.ReleaseComObject(item.Task);
                    }
                }

                return scopeInfo;
            },
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
            scopeObj =>
            {
                dynamic scope = scopeObj;
                dynamic group = scope.CreateApplicationGroup(name);
                try
                {
                    group.Type = (int)groupType;
                    group.Description = description;
                    if (groupType == AzGroupType.LdapQuery && !string.IsNullOrEmpty(ldapQuery))
                    {
                        group.LdapQuery = ldapQuery;
                    }
                    group.Submit();
                    scope.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(group);
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
            scope =>
            {
                dynamic group = scope.OpenApplicationGroup(groupName);
                try
                {
                    if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                    {
                        throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                    }
                    group.AddMember(memberSid);
                    group.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(group);
                }
            },
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
            scope =>
            {
                dynamic group = scope.OpenApplicationGroup(groupName);
                try
                {
                    if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                    {
                        throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                    }
                    group.DeleteMember(memberSid);
                    group.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(group);
                }
            },
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
            scope =>
            {
                dynamic group = scope.OpenApplicationGroup(groupName);
                try
                {
                    if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                    {
                        throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                    }
                    group.AddNonMember(memberSid);
                    group.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(group);
                }
            },
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
            scope =>
            {
                dynamic group = scope.OpenApplicationGroup(groupName);
                try
                {
                    if ((int)group.Type != AzManService.AZ_GROUPTYPE_BASIC)
                    {
                        throw new AzManException($"Cannot modify members of group '{groupName}' because it is not a Basic group.");
                    }
                    group.DeleteNonMember(memberSid);
                    group.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(group);
                }
            },
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
            scope => scope.DeleteApplicationGroup(groupName),
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
            scope =>
            {
                dynamic group = scope.OpenApplicationGroup(groupName);
                try
                {
                    group.BizRuleLanguage = bizRuleLanguage;
                    group.BizRule = bizRule;
                    group.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(group);
                }
            },
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
            scopeObj =>
            {
                dynamic scope = scopeObj;
                dynamic role = scope.CreateRole(name);
                try
                {
                    role.Description = description;
                    role.Submit();
                    scope.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.AddMember(memberSid);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope => scope.DeleteRole(roleName),
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
            scopeObj =>
            {
                dynamic scope = scopeObj;
                dynamic task = scope.CreateTask(name);
                try
                {
                    task.Description = description;
                    task.IsRoleDefinition = 1; // COM API uses int: 1 = true
                    task.Submit();
                    scope.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
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
            scope =>
            {
                dynamic task = scope.OpenTask(name);
                try
                {
                    task.Description = description;
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope => scope.DeleteTask(name),
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
            scopeObj =>
            {
                dynamic scope = scopeObj;
                dynamic task = scope.CreateTask(name);
                try
                {
                    task.Description = description;
                    task.Submit();
                    scope.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
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
            scope =>
            {
                dynamic task = scope.OpenTask(name);
                try
                {
                    task.Description = description;
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope => scope.DeleteTask(name),
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
            scope =>
            {
                dynamic task = scope.OpenTask(taskName);
                try
                {
                    task.AddOperation(operationName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(taskName);
                try
                {
                    task.DeleteOperation(operationName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(taskName);
                try
                {
                    task.AddTask(linkedTaskName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(taskName);
                try
                {
                    task.DeleteTask(linkedTaskName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(taskName);
                try
                {
                    task.BizRuleLanguage = bizRuleLanguage;
                    task.BizRule = bizRule;
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(taskName);
                try
                {
                    task.BizRuleLanguage = bizRuleLanguage;
                    task.BizRule = bizRule;
                    task.BizRuleImportedPath = filePath;
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.DeleteMember(memberSid);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.AddTask(taskName);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.DeleteTask(taskName);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.AddOperation(operationName);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.DeleteOperation(operationName);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.AddAppMember(appGroupName);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.DeleteAppMember(appGroupName);
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic role = scope.OpenRole(roleName);
                try
                {
                    role.Description = description;
                    role.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(role);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(roleDefName);
                try
                {
                    task.AddTask(taskName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(roleDefName);
                try
                {
                    task.DeleteTask(taskName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(roleDefName);
                try
                {
                    task.AddOperation(operationName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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
            scope =>
            {
                dynamic task = scope.OpenTask(roleDefName);
                try
                {
                    task.DeleteOperation(operationName);
                    task.Submit();
                }
                finally
                {
                    ComPropertyAccessor.ReleaseComObject(task);
                }
            },
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



