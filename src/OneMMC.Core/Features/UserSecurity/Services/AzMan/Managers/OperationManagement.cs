// ============================================================================
// AzMan Service - Operation Management
// ============================================================================
// Operation management functions: create, delete, update operations
// ============================================================================

using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class OperationManagement
{
    private readonly AzManService _service;

    public OperationManagement(AzManService service)
    {
        _service = service;
    }

    private ILogger<AzManService> _logger => _service.Logger;

    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<object, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunOperationWriteAsync(string storePath, string appName, string operationName, Action<dynamic> action, string errorMessage, string? debugMessage = null)
        => _service.RunOperationWriteAsync(storePath, appName, operationName, action, errorMessage, debugMessage);

    #region Operation Management

    /// <summary>
    /// Create an operation
    /// </summary>
    public async Task<AzOperationInfo> CreateOperationAsync(
        string storePath,
        string appName,
        string name,
        int operationId,
        string description = "")
    {
        return await RunApplicationReadAsync(
            storePath,
            appName,
            appObj =>
            {
                dynamic app = appObj;
                dynamic operation = app.CreateOperation(name);
                operation.Description = description;
                operation.OperationID = operationId;
                operation.Submit();
                app.Submit();

                _logger.LogInformation("Created operation {OperationName} with ID {OperationId}", name, operationId);
                return new AzOperationInfo
                {
                    Name = name,
                    Description = description,
                    OperationId = operationId
                };
            },
            "Failed to create operation");
    }

    /// <summary>
    /// Delete an operation
    /// </summary>
    public async Task DeleteOperationAsync(string storePath, string appName, string operationName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeleteOperation(operationName),
            "Failed to delete operation",
            $"[AzManService] Deleted operation '{operationName}'");
    }

    /// <summary>
    /// Update operation properties
    /// </summary>
    public async Task UpdateOperationAsync(string storePath, string appName, string operationName, string description, string? applicationData = null, int? operationId = null)
    {
        await RunOperationWriteAsync(
            storePath,
            appName,
            operationName,
            operation =>
            {
                operation.Description = description;
                if (applicationData != null)
                {
                    operation.ApplicationData = applicationData;
                }
                if (operationId.HasValue)
                {
                    operation.OperationID = operationId.Value;
                }
            },
            "Failed to update operation",
            $"[AzManService] Updated operation '{operationName}'");
    }

    /// <summary>
    /// Get operation information
    /// </summary>
    public async Task<AzOperationInfo> GetOperationAsync(string storePath, string appName, string operationName)
    {
        return await RunApplicationReadAsync(
            storePath,
            appName,
            appObj =>
            {
                dynamic app = appObj;
                dynamic operation = app.OpenOperation(operationName);

                return new AzOperationInfo
                {
                    Name = ComPropertyAccessor.GetString(operation, "Name"),
                    Description = ComPropertyAccessor.GetString(operation, "Description"),
                    OperationId = ComPropertyAccessor.GetInt(operation, "OperationID"),
                    ApplicationData = ComPropertyAccessor.GetString(operation, "ApplicationData")
                };
            },
            "Failed to get operation");
    }

    /// <summary>
    /// Get the next available operation ID
    /// </summary>
    public async Task<int> GetNextOperationIdAsync(string storePath, string appName)
    {
        return await RunApplicationReadAsync(
            storePath,
            appName,
            appObj =>
            {
                int maxId = 0;
                var operations = ComPropertyAccessor.GetCollection(appObj, "Operations", (object op) =>
                {
                    return new { Id = ComPropertyAccessor.GetInt(op, "OperationID") };
                });

                foreach (var op in operations)
                {
                    if (op.Id > maxId)
                    {
                        maxId = op.Id;
                    }
                }

                return maxId + 1;
            },
            "Failed to get operation ID");
    }

    /// <summary>
    /// Check if an operation ID is already in use
    /// </summary>
    public async Task<bool> IsOperationIdInUseAsync(string storePath, string appName, int operationId)
    {
        return await RunApplicationReadAsync(
            storePath,
            appName,
            appObj =>
            {
                var operations = ComPropertyAccessor.GetCollection(appObj, "Operations", (object op) =>
                {
                    return new { Id = ComPropertyAccessor.GetInt(op, "OperationID") };
                });

                foreach (var op in operations)
                {
                    if (op.Id == operationId)
                    {
                        return true;
                    }
                }
                return false;
            },
            "Failed to check operation ID");
    }

    #endregion
}


