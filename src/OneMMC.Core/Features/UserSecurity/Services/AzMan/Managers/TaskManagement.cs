// ============================================================================
// AzMan Service - Task Management
// ============================================================================
// Task management functions: create, delete tasks, manage task operations
// ============================================================================

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class TaskManagement
{
    private readonly AzManService _service;

    public TaskManagement(AzManService service)
    {
        _service = service;
    }

    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<object, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunTaskWriteAsync(string storePath, string appName, string taskName, Action<dynamic> action, string errorMessage, string? debugMessage = null)
        => _service.RunTaskWriteAsync(storePath, appName, taskName, action, errorMessage, debugMessage);

    #region Task Management

    /// <summary>
    /// Create a task
    /// </summary>
    public async Task<AzTaskInfo> CreateTaskAsync(
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
                task.IsRoleDefinition = false;
                task.Submit();
                app.Submit();

                return new AzTaskInfo
                {
                    Name = name,
                    Description = description,
                    IsRoleDefinition = false
                };
            },
            "Failed to create task");
    }

    /// <summary>
    /// Delete a task
    /// </summary>
    public async Task DeleteTaskAsync(string storePath, string appName, string taskName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeleteTask(taskName),
            "Failed to delete task");
    }

    /// <summary>
    /// Add an operation to a task
    /// </summary>
    public async Task AddOperationToTaskAsync(string storePath, string appName, string taskName, string operationName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task => task.AddOperation(operationName),
            "Failed to add operation to task");
    }

    /// <summary>
    /// Remove an operation from a task
    /// </summary>
    public async Task RemoveOperationFromTaskAsync(string storePath, string appName, string taskName, string operationName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task => task.DeleteOperation(operationName),
            "Failed to remove operation from task");
    }

    /// <summary>
    /// Update task properties
    /// </summary>
    public async Task UpdateTaskAsync(string storePath, string appName, string taskName, string description, string applicationData = "")
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task =>
            {
                task.Description = description;
                if (!string.IsNullOrEmpty(applicationData))
                {
                    task.ApplicationData = applicationData;
                }
            },
            "Failed to update task",
            $"[AzManService] Updated task '{taskName}'");
    }

    /// <summary>
    /// Add a task link to a task (nested task)
    /// </summary>
    public async Task AddTaskLinkAsync(string storePath, string appName, string taskName, string linkedTaskName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task => task.AddTask(linkedTaskName),
            "Failed to add task link",
            $"[AzManService] Added task link '{linkedTaskName}' to task '{taskName}'");
    }

    /// <summary>
    /// Remove a task link from a task
    /// </summary>
    public async Task RemoveTaskLinkAsync(string storePath, string appName, string taskName, string linkedTaskName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task => task.DeleteTask(linkedTaskName),
            "Failed to remove task link",
            $"[AzManService] Removed task link '{linkedTaskName}' from task '{taskName}'");
    }

    #endregion

    #region Business Rule Management

    /// <summary>
    /// Set business rule for a task
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="appName">Application name</param>
    /// <param name="taskName">Task name</param>
    /// <param name="bizRule">Business rule script (VBScript or JScript)</param>
    /// <param name="bizRuleLanguage">Script language ("VBScript" or "JScript")</param>
    public async Task SetTaskBizRuleAsync(string storePath, string appName, string taskName, string bizRule, string bizRuleLanguage)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task =>
            {
                // Set language first, then script
                task.BizRuleLanguage = bizRuleLanguage;
                task.BizRule = bizRule;
            },
            "Failed to set task business rule",
            $"[AzManService] Set business rule for task '{taskName}'");
    }

    /// <summary>
    /// Clear business rule from a task
    /// </summary>
    public async Task ClearTaskBizRuleAsync(string storePath, string appName, string taskName)
    {
        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task =>
            {
                // Clear script first, then language
                task.BizRule = "";
                task.BizRuleLanguage = "";
            },
            "Failed to clear task business rule",
            $"[AzManService] Cleared business rule for task '{taskName}'");
    }

    /// <summary>
    /// Import business rule from file for a task
    /// </summary>
    public async Task ImportTaskBizRuleAsync(string storePath, string appName, string taskName, string filePath, string bizRuleLanguage)
    {
        // Read script from file before COM operations
        if (!System.IO.File.Exists(filePath))
        {
            throw new System.IO.FileNotFoundException($"Business rule file not found: {filePath}");
        }

        string bizRule = System.IO.File.ReadAllText(filePath);

        await RunTaskWriteAsync(
            storePath,
            appName,
            taskName,
            task =>
            {
                task.BizRuleLanguage = bizRuleLanguage;
                task.BizRule = bizRule;
                task.BizRuleImportedPath = filePath;
            },
            "Failed to import task business rule",
            $"[AzManService] Imported business rule for task '{taskName}' from '{filePath}'");
    }

    /// <summary>
    /// Set business rule for a role definition
    /// </summary>
    public async Task SetRoleDefinitionBizRuleAsync(string storePath, string appName, string roleDefName, string bizRule, string bizRuleLanguage)
    {
        // Role definition is a task with IsRoleDefinition = true
        await SetTaskBizRuleAsync(storePath, appName, roleDefName, bizRule, bizRuleLanguage);
    }

    /// <summary>
    /// Clear business rule from a role definition
    /// </summary>
    public async Task ClearRoleDefinitionBizRuleAsync(string storePath, string appName, string roleDefName)
    {
        await ClearTaskBizRuleAsync(storePath, appName, roleDefName);
    }

    /// <summary>
    /// Import business rule from file for a role definition.
    /// </summary>
    public async Task ImportRoleDefinitionBizRuleAsync(string storePath, string appName, string roleDefName, string filePath, string bizRuleLanguage)
    {
        await ImportTaskBizRuleAsync(storePath, appName, roleDefName, filePath, bizRuleLanguage);
    }

    #endregion
}


