// ============================================================================
// AzMan Service - Role Management
// ============================================================================
// Role management functions: role definition and role assignment creation, deletion, member management
// 
// Important notes:
// - Role Definition is actually a Task with IsRoleDefinition = true
// - Role Assignment is the actual Role object
// ============================================================================

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

internal sealed class RoleManagement
{
    private readonly AzManService _service;

    public RoleManagement(AzManService service)
    {
        _service = service;
    }

    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<object, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunRoleWriteAsync(string storePath, string appName, string roleName, Action<dynamic> action, string errorMessage, string? debugMessage = null)
        => _service.RunRoleWriteAsync(storePath, appName, roleName, action, errorMessage, debugMessage);
    private Task RunTaskWriteAsync(string storePath, string appName, string taskName, Action<dynamic> action, string errorMessage, string? debugMessage = null)
        => _service.RunTaskWriteAsync(storePath, appName, taskName, action, errorMessage, debugMessage);

    #region Role Definition Management

    /// <summary>
    /// Create a role definition (as a task with IsRoleDefinition = true)
    /// </summary>
    public async Task<AzRoleDefinitionInfo> CreateRoleDefinitionAsync(
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
                dynamic task = app.CreateTask(name);
                task.Description = description;
                task.IsRoleDefinition = true;
                task.Submit();
                app.Submit();

                return new AzRoleDefinitionInfo
                {
                    Name = name,
                    Description = description
                };
            },
            "Failed to create role definition");
    }

    /// <summary>
    /// Delete a role definition
    /// </summary>
    public async Task DeleteRoleDefinitionAsync(string storePath, string appName, string roleName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeleteTask(roleName),
            "Failed to delete role definition");
    }

    /// <summary>
    /// Add a task to a role definition (role definition is actually a Task with IsRoleDefinition=true)
    /// </summary>
    public async Task AddTaskToRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string taskName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            roleDefinitionName,
            task => task.AddTask(taskName),
            "Failed to add task to role definition");
    }

    /// <summary>
    /// Remove a task from a role definition
    /// </summary>
    public async Task RemoveTaskFromRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string taskName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            roleDefinitionName,
            task => task.DeleteTask(taskName),
            "Failed to remove task from role definition");
    }

    #endregion

    #region Role Assignment Management

    /// <summary>
    /// Create a role assignment
    /// </summary>
    public async Task<AzRoleAssignmentInfo> CreateRoleAssignmentAsync(
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
                dynamic role = app.CreateRole(name);
                role.Description = description;
                role.Submit();
                app.Submit();

                return new AzRoleAssignmentInfo
                {
                    Name = name,
                    Description = description
                };
            },
            "Failed to create role assignment");
    }

    /// <summary>
    /// Delete a role assignment
    /// </summary>
    public async Task DeleteRoleAssignmentAsync(string storePath, string appName, string roleName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeleteRole(roleName),
            "Failed to delete role assignment");
    }

    /// <summary>
    /// Add a member to a role assignment
    /// </summary>
    public async Task AddRoleMemberAsync(string storePath, string appName, string roleName, string memberSid)
    {
        ValidateSid(memberSid);

        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.AddMember(memberSid),
            "Failed to add role member");
    }

    /// <summary>
    /// Add a task to a role assignment
    /// </summary>
    public async Task AddTaskToRoleAssignmentAsync(string storePath, string appName, string roleName, string taskName)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.AddTask(taskName),
            "Failed to add task to role assignment");
    }

    /// <summary>
    /// Remove a task from a role assignment
    /// </summary>
    public async Task RemoveTaskFromRoleAssignmentAsync(string storePath, string appName, string roleName, string taskName)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.DeleteTask(taskName),
            "Failed to remove task from role assignment");
    }

    /// <summary>
    /// Add an operation to a role assignment
    /// </summary>
    public async Task AddOperationToRoleAssignmentAsync(string storePath, string appName, string roleName, string operationName)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.AddOperation(operationName),
            "Failed to add operation to role assignment",
            $"[AzManService] Added operation '{operationName}' to role assignment '{roleName}'");
    }

    /// <summary>
    /// Remove an operation from a role assignment
    /// </summary>
    public async Task RemoveOperationFromRoleAssignmentAsync(string storePath, string appName, string roleName, string operationName)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.DeleteOperation(operationName),
            "Failed to remove operation from role assignment",
            $"[AzManService] Removed operation '{operationName}' from role assignment '{roleName}'");
    }

    /// <summary>
    /// Remove a member from a role assignment
    /// </summary>
    public async Task RemoveRoleMemberAsync(string storePath, string appName, string roleName, string memberSid)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.DeleteMember(memberSid),
            "Failed to remove role member",
            $"[AzManService] Removed member from role assignment '{roleName}'");
    }

    /// <summary>
    /// Add an application group member link to a role assignment
    /// </summary>
    public async Task AddAppMemberToRoleAssignmentAsync(string storePath, string appName, string roleName, string appGroupName)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.AddAppMember(appGroupName),
            "Failed to add app member to role assignment",
            $"[AzManService] Added app member '{appGroupName}' to role assignment '{roleName}'");
    }

    /// <summary>
    /// Remove an application group member link from a role assignment
    /// </summary>
    public async Task RemoveAppMemberFromRoleAssignmentAsync(string storePath, string appName, string roleName, string appGroupName)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.DeleteAppMember(appGroupName),
            "Failed to remove app member from role assignment",
            $"[AzManService] Removed app member '{appGroupName}' from role assignment '{roleName}'");
    }

    /// <summary>
    /// Add an operation to a role definition
    /// </summary>
    public async Task AddOperationToRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string operationName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            roleDefinitionName,
            task => task.AddOperation(operationName),
            "Failed to add operation to role definition",
            $"[AzManService] Added operation '{operationName}' to role definition '{roleDefinitionName}'");
    }

    /// <summary>
    /// Remove an operation from a role definition
    /// </summary>
    public async Task RemoveOperationFromRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string operationName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            roleDefinitionName,
            task => task.DeleteOperation(operationName),
            "Failed to remove operation from role definition",
            $"[AzManService] Removed operation '{operationName}' from role definition '{roleDefinitionName}'");
    }

    /// <summary>
    /// Update role definition properties
    /// </summary>
    public async Task UpdateRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string description)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            roleDefinitionName,
            task => task.Description = description,
            "Failed to update role definition",
            $"[AzManService] Updated role definition '{roleDefinitionName}'");
    }

    /// <summary>
    /// Update role assignment properties
    /// </summary>
    public async Task UpdateRoleAssignmentAsync(string storePath, string appName, string roleName, string description)
    {
        await RunRoleWriteAsync(
            storePath,
            appName,
            roleName,
            role => role.Description = description,
            "Failed to update role assignment",
            $"[AzManService] Updated role assignment '{roleName}'");
    }

    #endregion

    #region Legacy Role Methods (Obsolete)

    /// <summary>
    /// Add a task to a role (legacy method name for backward compatibility, internally calls new methods)
    /// </summary>
    [Obsolete("Please use AddTaskToRoleDefinitionAsync or AddTaskToRoleAssignmentAsync")]
    public async Task AddTaskToRoleAsync(string storePath, string appName, string roleName, string taskName)
    {
        // Try as role definition first, if it fails try as role assignment
        try
        {
            await AddTaskToRoleDefinitionAsync(storePath, appName, roleName, taskName);
        }
        catch (AzManException)
        {
            // If it fails as role definition, try as role assignment
            await AddTaskToRoleAssignmentAsync(storePath, appName, roleName, taskName);
        }
    }

    /// <summary>
    /// Remove a task from a role (legacy method name for backward compatibility, internally calls new methods)
    /// </summary>
    [Obsolete("Please use RemoveTaskFromRoleDefinitionAsync or RemoveTaskFromRoleAssignmentAsync")]
    public async Task RemoveTaskFromRoleAsync(string storePath, string appName, string roleName, string taskName)
    {
        // Try as role definition first, if it fails try as role assignment
        try
        {
            await RemoveTaskFromRoleDefinitionAsync(storePath, appName, roleName, taskName);
        }
        catch (AzManException)
        {
            // If it fails as role definition, try as role assignment
            await RemoveTaskFromRoleAssignmentAsync(storePath, appName, roleName, taskName);
        }
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


