// ============================================================================
// AzMan Service - Export/Import Functions
// ============================================================================
// Export and import authorization store data
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

internal sealed class ExportImportManagement
{
    private readonly AzManService _service;

    public ExportImportManagement(AzManService service)
    {
        _service = service;
    }

    private object _lockObject => _service.LockObject;
    private ILogger<AzManService> _logger => _service.Logger;

    private string AZ_AUTHORIZATION_STORE_PROGID => AzManService.AZ_AUTHORIZATION_STORE_PROGID;
    private int AZ_AZSTORE_FLAG_CREATE => AzManService.AZ_AZSTORE_FLAG_CREATE;
    private int AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY => AzManService.AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY;

    private Task RunComAsync(Action action) => _service.RunComAsync(action);
    private void EnsureStoreOpen(string storePath) => _service.EnsureStoreOpen(storePath);
    private static string GetComErrorMessage(COMException ex) => AzManService.GetComErrorMessage(ex);

    #region Export Functions

    /// <summary>
    /// Export store to XML file
    /// </summary>
    /// <param name="storePath">Source store path</param>
    /// <param name="exportPath">Target XML file path</param>
    /// <param name="includeSecurityInfo">Whether to include security information (administrators, readers)</param>
    public async Task ExportStoreToXmlAsync(string storePath, string exportPath, bool includeSecurityInfo = true)
    {
        await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    EnsureStoreOpen(storePath);

                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Create target store URL
                    string targetUrl = $"msxml://{exportPath}";

                    // Create new XML store
                    var storeType = Type.GetTypeFromProgID(AZ_AUTHORIZATION_STORE_PROGID);
                    if (storeType == null)
                    {
                        throw new InvalidOperationException("Cannot find AzRoles.AzAuthorizationStore COM component.");
                    }

                    dynamic targetStore = Activator.CreateInstance(storeType)!;

                    try
                    {
                        dynamic authStore = _service.GetAuthStoreOrThrow(storePath);

                        // Initialize target store (create mode)
                        targetStore.Initialize(AZ_AZSTORE_FLAG_CREATE, targetUrl);

                        // Copy store properties
                        targetStore.Description = ComPropertyAccessor.GetString(authStore, "Description");
                        targetStore.ApplicationData = ComPropertyAccessor.GetString(authStore, "ApplicationData");
                        targetStore.GenerateAudits = ComPropertyAccessor.GetBool(authStore, "GenerateAudits");

                        // Copy applications
                        CopyApplications(authStore, targetStore);

                        // Copy store-level groups
                        CopyStoreGroups(authStore, targetStore);

                        // Copy security info if requested
                        if (includeSecurityInfo)
                        {
                            CopySecurityInfo(authStore, targetStore);
                        }

                        targetStore.Submit();
                        _logger.LogDebug($"[AzManService] Exported store to: {exportPath}");
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(targetStore);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"Failed to export store: {GetComErrorMessage(ex)}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Export a single application to XML file
    /// </summary>
    public async Task ExportApplicationToXmlAsync(string storePath, string appName, string exportPath)
    {
        await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    EnsureStoreOpen(storePath);

                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    string targetUrl = $"msxml://{exportPath}";

                    var storeType = Type.GetTypeFromProgID(AZ_AUTHORIZATION_STORE_PROGID);
                    if (storeType == null)
                    {
                        throw new InvalidOperationException("Cannot find AzRoles.AzAuthorizationStore COM component.");
                    }

                    dynamic targetStore = Activator.CreateInstance(storeType)!;

                    try
                    {
                        dynamic authStore = _service.GetAuthStoreOrThrow(storePath);

                        targetStore.Initialize(AZ_AZSTORE_FLAG_CREATE, targetUrl);
                        targetStore.Description = $"Exported application: {appName}";

                        // Open source application
                        dynamic sourceApp = authStore.OpenApplication(appName);

                        // Create and copy application
                        CopyApplication(sourceApp, targetStore);

                        targetStore.Submit();
                        _logger.LogDebug($"[AzManService] Exported application '{appName}' to: {exportPath}");
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(targetStore);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"Failed to export application: {GetComErrorMessage(ex)}", ex);
                }
            }
        });
    }

    #endregion

    #region Import Functions

    /// <summary>
    /// Import application from XML file
    /// </summary>
    public async Task ImportApplicationFromXmlAsync(string storePath, string importPath, string? newAppName = null)
    {
        await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    EnsureStoreOpen(storePath);

                    if (!File.Exists(importPath))
                    {
                        throw new FileNotFoundException($"Import file not found: {importPath}");
                    }

                    string sourceUrl = $"msxml://{importPath}";

                    var storeType = Type.GetTypeFromProgID(AZ_AUTHORIZATION_STORE_PROGID);
                    if (storeType == null)
                    {
                        throw new InvalidOperationException("Cannot find AzRoles.AzAuthorizationStore COM component.");
                    }

                    dynamic sourceStore = Activator.CreateInstance(storeType)!;

                    try
                    {
                        sourceStore.Initialize(AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY, sourceUrl);

                        // Get first application from source
                        object sourceStoreObj = sourceStore;
                        var apps = ComPropertyAccessor.GetCollection(sourceStoreObj, "Applications", (object app) => app);

                        if (apps.Count == 0)
                        {
                            throw new InvalidOperationException("No applications found in import file.");
                        }

                        dynamic sourceApp = apps[0];
                        string originalName = ComPropertyAccessor.GetString(sourceApp, "Name");
                        string targetName = newAppName ?? originalName;

                        dynamic authStore = _service.GetAuthStoreOrThrow(storePath);

                        // Check if application already exists
                        try
                        {
                            authStore.OpenApplication(targetName);
                            throw new InvalidOperationException($"Application '{targetName}' already exists in target store.");
                        }
                        catch (COMException)
                        {
                            // Application doesn't exist, which is what we want
                        }

                        // Copy application to target store
                        CopyApplication(sourceApp, authStore, targetName);

                        authStore.Submit();
                        _logger.LogDebug($"[AzManService] Imported application '{targetName}' from: {importPath}");
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sourceStore);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"Failed to import application: {GetComErrorMessage(ex)}", ex);
                }
            }
        });
    }

    #endregion

    #region Copy Helper Methods

    private void CopyApplications(dynamic sourceStore, dynamic targetStore)
    {
        object sourceStoreObj = sourceStore;
        var apps = ComPropertyAccessor.GetCollection(sourceStoreObj, "Applications", (object app) => app);

        foreach (dynamic app in apps)
        {
            CopyApplication(app, targetStore);
        }
    }

    private void CopyApplication(dynamic sourceApp, dynamic targetStore, string? newName = null)
    {
        string appName = newName ?? ComPropertyAccessor.GetString(sourceApp, "Name");
        dynamic targetApp = targetStore.CreateApplication(appName);

        // Copy properties
        targetApp.Description = ComPropertyAccessor.GetString(sourceApp, "Description");
        targetApp.ApplicationData = ComPropertyAccessor.GetString(sourceApp, "ApplicationData");
        targetApp.GenerateAudits = ComPropertyAccessor.GetBool(sourceApp, "GenerateAudits");

        string version = ComPropertyAccessor.GetString(sourceApp, "ApplicationVersion");
        if (!string.IsNullOrEmpty(version))
        {
            targetApp.ApplicationVersion = version;
        }

        // Copy operations first (they are referenced by tasks)
        CopyOperations(sourceApp, targetApp);

        // Copy tasks (including role definitions)
        CopyTasks(sourceApp, targetApp);

        // Copy application groups
        CopyAppGroups(sourceApp, targetApp);

        // Copy roles (role assignments)
        CopyRoles(sourceApp, targetApp);

        // Copy scopes
        CopyScopes(sourceApp, targetApp);

        targetApp.Submit();
    }

    private void CopyOperations(dynamic sourceApp, dynamic targetApp)
    {
        object sourceAppObj = sourceApp;
        var operations = ComPropertyAccessor.GetCollection(sourceAppObj, "Operations", (object op) => op);

        foreach (dynamic op in operations)
        {
            string name = ComPropertyAccessor.GetString(op, "Name");
            dynamic targetOp = targetApp.CreateOperation(name);
            targetOp.Description = ComPropertyAccessor.GetString(op, "Description");
            targetOp.OperationID = ComPropertyAccessor.GetInt(op, "OperationID");
            targetOp.ApplicationData = ComPropertyAccessor.GetString(op, "ApplicationData");
            targetOp.Submit();
        }
    }

    private void CopyTasks(dynamic sourceApp, dynamic targetApp)
    {
        object sourceAppObj = sourceApp;
        var tasks = ComPropertyAccessor.GetCollection(sourceAppObj, "Tasks", (object task) => task);

        foreach (dynamic task in tasks)
        {
            string name = ComPropertyAccessor.GetString(task, "Name");
            dynamic targetTask = targetApp.CreateTask(name);
            targetTask.Description = ComPropertyAccessor.GetString(task, "Description");
            targetTask.IsRoleDefinition = ComPropertyAccessor.GetBool(task, "IsRoleDefinition");
            targetTask.ApplicationData = ComPropertyAccessor.GetString(task, "ApplicationData");

            // Copy business rule
            string bizRule = ComPropertyAccessor.GetString(task, "BizRule");
            if (!string.IsNullOrEmpty(bizRule))
            {
                targetTask.BizRuleLanguage = ComPropertyAccessor.GetString(task, "BizRuleLanguage");
                targetTask.BizRule = bizRule;
            }

            // Copy operations
            var ops = ComPropertyAccessor.GetStringArray(task, "Operations");
            foreach (var opName in ops)
            {
                try { targetTask.AddOperation(opName); } catch { }
            }

            // Copy task links
            var taskLinks = ComPropertyAccessor.GetStringArray(task, "Tasks");
            foreach (var taskName in taskLinks)
            {
                try { targetTask.AddTask(taskName); } catch { }
            }

            targetTask.Submit();
        }
    }

    private void CopyAppGroups(dynamic sourceApp, dynamic targetApp)
    {
        object sourceAppObj = sourceApp;
        var groups = ComPropertyAccessor.GetCollection(sourceAppObj, "ApplicationGroups", (object group) => group);

        foreach (dynamic group in groups)
        {
            string name = ComPropertyAccessor.GetString(group, "Name");
            int groupType = ComPropertyAccessor.GetInt(group, "Type");
            dynamic targetGroup = targetApp.CreateApplicationGroup(name, groupType);
            targetGroup.Description = ComPropertyAccessor.GetString(group, "Description");

            if (groupType == (int)AzGroupType.LdapQuery)
            {
                targetGroup.LdapQuery = ComPropertyAccessor.GetString(group, "LdapQuery");
            }
            else
            {
                // Copy members
                var members = ComPropertyAccessor.GetStringArray(group, "Members");
                foreach (var member in members)
                {
                    try { targetGroup.AddMember(member); } catch { }
                }

                // Copy non-members
                var nonMembers = ComPropertyAccessor.GetStringArray(group, "NonMembers");
                foreach (var nonMember in nonMembers)
                {
                    try { targetGroup.AddNonMember(nonMember); } catch { }
                }
            }

            targetGroup.Submit();
        }
    }

    private void CopyRoles(dynamic sourceApp, dynamic targetApp)
    {
        object sourceAppObj = sourceApp;
        var roles = ComPropertyAccessor.GetCollection(sourceAppObj, "Roles", (object role) => role);

        foreach (dynamic role in roles)
        {
            string name = ComPropertyAccessor.GetString(role, "Name");
            dynamic targetRole = targetApp.CreateRole(name);
            targetRole.Description = ComPropertyAccessor.GetString(role, "Description");

            // Copy tasks
            var tasks = ComPropertyAccessor.GetStringArray(role, "Tasks");
            foreach (var taskName in tasks)
            {
                try { targetRole.AddTask(taskName); } catch { }
            }

            // Copy operations
            var ops = ComPropertyAccessor.GetStringArray(role, "Operations");
            foreach (var opName in ops)
            {
                try { targetRole.AddOperation(opName); } catch { }
            }

            // Note: Members are not copied as they are security principals specific to the source environment

            targetRole.Submit();
        }
    }

    private void CopyScopes(dynamic sourceApp, dynamic targetApp)
    {
        object sourceAppObj = sourceApp;
        var scopes = ComPropertyAccessor.GetCollection(sourceAppObj, "Scopes", (object scope) => scope);

        foreach (dynamic scope in scopes)
        {
            string name = ComPropertyAccessor.GetString(scope, "Name");
            dynamic targetScope = targetApp.CreateScope(name);
            targetScope.Description = ComPropertyAccessor.GetString(scope, "Description");
            targetScope.ApplicationData = ComPropertyAccessor.GetString(scope, "ApplicationData");

            // Copy scope groups, tasks, roles would go here
            // For simplicity, we're copying basic properties only

            targetScope.Submit();
        }
    }

    private void CopyStoreGroups(dynamic sourceStore, dynamic targetStore)
    {
        object sourceStoreObj = sourceStore;
        var groups = ComPropertyAccessor.GetCollection(sourceStoreObj, "ApplicationGroups", (object group) => group);

        foreach (dynamic group in groups)
        {
            string name = ComPropertyAccessor.GetString(group, "Name");
            int groupType = ComPropertyAccessor.GetInt(group, "Type");
            dynamic targetGroup = targetStore.CreateApplicationGroup(name, groupType);
            targetGroup.Description = ComPropertyAccessor.GetString(group, "Description");

            if (groupType == (int)AzGroupType.LdapQuery)
            {
                targetGroup.LdapQuery = ComPropertyAccessor.GetString(group, "LdapQuery");
            }

            targetGroup.Submit();
        }
    }

    private void CopySecurityInfo(dynamic sourceStore, dynamic targetStore)
    {
        // Copy policy administrators
        var admins = ComPropertyAccessor.GetStringArray(sourceStore, "PolicyAdministratorsName");
        foreach (var admin in admins)
        {
            try { targetStore.AddPolicyAdministratorName(admin); } catch { }
        }

        // Copy policy readers
        var readers = ComPropertyAccessor.GetStringArray(sourceStore, "PolicyReadersName");
        foreach (var reader in readers)
        {
            try { targetStore.AddPolicyReaderName(reader); } catch { }
        }
    }

    #endregion
}




