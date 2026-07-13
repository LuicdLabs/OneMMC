// ============================================================================
// AzMan Service - Application Management
// ============================================================================
// Application management functions: create, delete, update, get applications
// ============================================================================

using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

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
    private Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<IAzApplication, T> func, string errorMessage)
        => _service.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    private Task RunApplicationWriteAsync(string storePath, string appName, Action<IAzApplication> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _service.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    private Task RunStoreWriteAsync(string storePath, Action<IAzAuthorizationStore3> action, string errorMessage, string? debugMessage = null)
        => _service.RunStoreWriteAsync(storePath, action, errorMessage, debugMessage);
    private string GetComErrorMessage(COMException ex) => AzManService.GetComErrorMessage(ex);
    private AzApplicationInfo? ReadApplicationInfo(IAzApplication app) => _service.ReadApplicationInfo(app);

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
                    IAzAuthorizationStore3 authStore = _service.GetAuthStoreOrThrow(storePath);

                    authStore.CreateApplication(name, Variant.Missing, out IAzApplication app);
                    try
                    {
                        if (!string.IsNullOrEmpty(description))
                        {
                            app.put_Description(description);
                        }
                        app.Submit(0, Variant.Missing);
                        authStore.Submit(0, Variant.Missing);
                    }
                    finally
                    {
                        AzRolesCom.Release(app);
                    }

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
            store => store.DeleteApplication(appName, Variant.Missing),
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
                app.put_Description(description);
                if (applicationData != null)
                {
                    app.put_ApplicationData(applicationData);
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
                app.put_Description(description);

                if (applicationData != null)
                {
                    app.put_ApplicationData(applicationData);
                }
                if (version != null)
                {
                    // The AzRoles property is named Version; the previous late-bound
                    // "ApplicationVersion" assignment targeted a name that does not exist.
                    app.put_Version(version);
                }
                if (authzInterfaceClsid != null)
                {
                    app.put_AuthzInterfaceClsid(authzInterfaceClsid);
                }
                if (generateAudits.HasValue)
                {
                    app.put_GenerateAudits(AzRolesCom.FromBool(generateAudits.Value));
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
            app => app.AddPolicyAdministratorName(adminName, Variant.Missing),
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
            app => app.DeletePolicyAdministratorName(adminName, Variant.Missing),
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
            app => app.AddPolicyReaderName(readerName, Variant.Missing),
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
            app => app.DeletePolicyReaderName(readerName, Variant.Missing),
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
            app => app.AddDelegatedPolicyUserName(userName, Variant.Missing),
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
            app => app.DeleteDelegatedPolicyUserName(userName, Variant.Missing),
            "Failed to remove application delegated policy user",
            $"[AzManService] Removed delegated policy user '{userName}' from application '{appName}'");
    }

    #endregion
}
