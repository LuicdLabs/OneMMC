// ============================================================================
// AzMan Service - Application Management
// ============================================================================
// Application management functions: create, delete, update, get applications
// ============================================================================

using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

internal sealed class ApplicationManagement
{
    private readonly AzManService _service;

    public ApplicationManagement(AzManService service)
    {
        _service = service;
    }

    private object _lockObject => _service.LockObject;
    private ILogger<AzManService> _logger => _service.Logger;

    private Task RunComAsync(Action action) => _service.RunComAsync(action);
    private Task<T> RunComAsync<T>(Func<T> func) => _service.RunComAsync(func);
    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<object, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<dynamic> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunStoreWriteAsync(string storePath, Action<dynamic> action, string errorMessage, string? debugMessage = null)
        => _service.RunStoreWriteAsync(storePath, action, errorMessage, debugMessage);
    private void EnsureStoreOpen(string storePath) => _service.EnsureStoreOpen(storePath);
    private string GetComErrorMessage(COMException ex) => AzManService.GetComErrorMessage(ex);
    private AzApplicationInfo? ReadApplicationInfo(object app) => _service.ReadApplicationInfo(app);

    #region Application Management

    /// <summary>
    /// Create a new application
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="name">Application name</param>
    /// <param name="description">Description</param>
    /// <returns>Created application information</returns>
    public async Task<AzApplicationInfo> CreateApplicationAsync(string storePath, string name, string description = "")
    {
        return await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    dynamic authStore = _service.GetAuthStoreOrThrow(storePath);

                    dynamic app = authStore.CreateApplication(name);
                    if (!string.IsNullOrEmpty(description))
                    {
                        app.Description = description;
                    }
                    app.Submit();
                    authStore.Submit();

                    var appInfo = new AzApplicationInfo
                    {
                        Name = name,
                        Description = description
                    };

                    _logger.LogInformation("Successfully created application: {ApplicationName}", name);
                    return appInfo;
                }
                catch (COMException ex)
                {
                    throw new AzManException($"Failed to create application: {GetComErrorMessage(ex)}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Delete an application
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="appName">Application name</param>
    public async Task DeleteApplicationAsync(string storePath, string appName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.DeleteApplication(appName),
            "Failed to delete application",
            $"[AzManService] Successfully deleted application: {appName}");
    }

    /// <summary>
    /// Get complete application information
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="appName">Application name</param>
    /// <returns>Application information</returns>
    public async Task<AzApplicationInfo> GetApplicationAsync(string storePath, string appName)
    {
        return await RunApplicationReadAsync(
            storePath,
            appName,
            app => ReadApplicationInfo(app)!,
            "Failed to get application");
    }

    /// <summary>
    /// Update application properties
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="appName">Application name</param>
    /// <param name="description">New description</param>
    public async Task UpdateApplicationAsync(string storePath, string appName, string description)
    {
        await UpdateApplicationAsync(storePath, appName, description, null);
    }

    /// <summary>
    /// Update application properties
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="appName">Application name</param>
    /// <param name="description">New description</param>
    /// <param name="applicationData">Application data</param>
    public async Task UpdateApplicationAsync(string storePath, string appName, string description, string? applicationData)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app =>
            {
                app.Description = description;
                if (applicationData != null)
                {
                    app.ApplicationData = applicationData;
                }
            },
            "Failed to update application",
            $"[AzManService] Successfully updated application: {appName}");
    }

    /// <summary>
    /// Update application properties with all fields
    /// </summary>
    public async Task UpdateApplicationAsync(
        string storePath, 
        string appName, 
        string description, 
        string? applicationData,
        string? version,
        string? authzInterfaceClsid,
        bool? generateAudits)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app =>
            {
                app.Description = description;

                if (applicationData != null)
                {
                    app.ApplicationData = applicationData;
                }
                if (version != null)
                {
                    app.ApplicationVersion = version;
                }
                if (authzInterfaceClsid != null)
                {
                    app.AuthzInterfaceClsid = authzInterfaceClsid;
                }
                if (generateAudits.HasValue)
                {
                    app.GenerateAudits = generateAudits.Value;
                }
            },
            "Failed to update application",
            $"[AzManService] Successfully updated application: {appName}");
    }

    #endregion

    #region Application Policy Administrators / Readers Management

    /// <summary>
    /// Add a policy administrator to an application
    /// </summary>
    public async Task AddApplicationPolicyAdministratorAsync(string storePath, string appName, string adminName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.AddPolicyAdministratorName(adminName),
            "Failed to add application policy administrator",
            $"[AzManService] Added policy administrator '{adminName}' to application '{appName}'");
    }

    /// <summary>
    /// Remove a policy administrator from an application
    /// </summary>
    public async Task RemoveApplicationPolicyAdministratorAsync(string storePath, string appName, string adminName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeletePolicyAdministratorName(adminName),
            "Failed to remove application policy administrator",
            $"[AzManService] Removed policy administrator '{adminName}' from application '{appName}'");
    }

    /// <summary>
    /// Add a policy reader to an application
    /// </summary>
    public async Task AddApplicationPolicyReaderAsync(string storePath, string appName, string readerName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.AddPolicyReaderName(readerName),
            "Failed to add application policy reader",
            $"[AzManService] Added policy reader '{readerName}' to application '{appName}'");
    }

    /// <summary>
    /// Remove a policy reader from an application
    /// </summary>
    public async Task RemoveApplicationPolicyReaderAsync(string storePath, string appName, string readerName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeletePolicyReaderName(readerName),
            "Failed to remove application policy reader",
            $"[AzManService] Removed policy reader '{readerName}' from application '{appName}'");
    }

    /// <summary>
    /// Add a delegated policy user to an application
    /// </summary>
    public async Task AddApplicationDelegatedPolicyUserAsync(string storePath, string appName, string userName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.AddDelegatedPolicyUserName(userName),
            "Failed to add application delegated policy user",
            $"[AzManService] Added delegated policy user '{userName}' to application '{appName}'");
    }

    /// <summary>
    /// Remove a delegated policy user from an application
    /// </summary>
    public async Task RemoveApplicationDelegatedPolicyUserAsync(string storePath, string appName, string userName)
    {
        await RunApplicationWriteAsync(
            storePath,
            appName,
            app => app.DeleteDelegatedPolicyUserName(userName),
            "Failed to remove application delegated policy user",
            $"[AzManService] Removed delegated policy user '{userName}' from application '{appName}'");
    }

    #endregion
}


