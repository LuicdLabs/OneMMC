// ============================================================================
// AzMan Service - Operation Management
// ============================================================================
// Operation management functions: create, delete, update operations
// ============================================================================

using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;
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

    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<IAzApplication, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<IAzApplication> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunOperationWriteAsync(string storePath, string appName, string operationName, Action<IAzOperation> action, string errorMessage, string? debugMessage = null)
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
            app =>
            {
                app.CreateOperation(name, Variant.Missing, out IAzOperation operation);
                try
                {
                    operation.put_Description(description);
                    operation.put_OperationID(operationId);
                    operation.Submit(0, Variant.Missing);
                    app.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(operation);
                }

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
            app => app.DeleteOperation(operationName, Variant.Missing),
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
                operation.put_Description(description);
                if (applicationData != null)
                {
                    operation.put_ApplicationData(applicationData);
                }
                if (operationId.HasValue)
                {
                    operation.put_OperationID(operationId.Value);
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
            app =>
            {
                app.OpenOperation(operationName, Variant.Missing, out IAzOperation operation);
                try
                {
                    return new AzOperationInfo
                    {
                        Name = operation.get_Name() ?? string.Empty,
                        Description = operation.get_Description() ?? string.Empty,
                        OperationId = operation.get_OperationID(),
                        ApplicationData = operation.get_ApplicationData() ?? string.Empty
                    };
                }
                finally
                {
                    AzRolesCom.Release(operation);
                }
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
            app =>
            {
                int maxId = 0;
                foreach (int id in ReadOperationIds(app))
                {
                    if (id > maxId)
                    {
                        maxId = id;
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
            app =>
            {
                foreach (int id in ReadOperationIds(app))
                {
                    if (id == operationId)
                    {
                        return true;
                    }
                }
                return false;
            },
            "Failed to check operation ID");
    }

    /// <summary>Reads every operation's OperationID; unreadable entries are skipped.</summary>
    private List<int> ReadOperationIds(IAzApplication app)
    {
        var result = new List<int>();
        app.get_Operations(out IAzOperations operations);
        try
        {
            foreach (IAzOperation op in operations.Items())
            {
                try
                {
                    result.Add(op.get_OperationID());
                }
                catch (COMException ex)
                {
                    _logger.LogDebug("[AzManService] Failed to read OperationID: {Message}", ex.Message);
                }
                finally
                {
                    AzRolesCom.Release(op);
                }
            }
        }
        finally
        {
            AzRolesCom.Release(operations);
        }
        return result;
    }

    #endregion
}
