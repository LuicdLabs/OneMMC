// ============================================================================
// AzMan Service - Export/Import Functions
// ============================================================================
// Export and import authorization store data
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class ExportImportManagement
{
    private readonly AzManService _service;

    public ExportImportManagement(AzManService service)
    {
        _service = service;
    }

    private object _lockObject => _service.LockObject;
    private ILogger<AzManService> _logger => _service.Logger;

    private int AZ_AZSTORE_FLAG_CREATE => AzManService.AZ_AZSTORE_FLAG_CREATE;
    private int AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY => AzManService.AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY;

    private Task RunComAsync(Action action) => _service.RunComAsync(action);
    private void EnsureStoreOpen(string storePath) => _service.EnsureStoreOpen(storePath);
    private static string GetComErrorMessage(COMException ex) => AzManService.GetComErrorMessage(ex);

    #region Safe Read Helpers

    /// <summary>Reads a BSTR property for copying, returning "" on COM failure or null.</summary>
    private static string CopyString(Func<string?> getter)
    {
        try
        {
            return getter() ?? string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    /// <summary>Reads a VARIANT(SAFEARRAY-of-strings) property for copying; empty list when the
    /// property is unsupported (the getters are <c>[PreserveSig]</c> and return the HRESULT rather
    /// than throwing).</summary>
    private static List<string> CopyStringList(AzManVariantGetter getter)
    {
        int hr = getter(out Variant value);
        try
        {
            return hr >= 0 ? value.ToStringList() : [];
        }
        finally
        {
            value.Clear();
        }
    }

    private delegate int AzManVariantGetter(out Variant value);

    #endregion

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
                    IAzAuthorizationStore3 targetStore = AzRolesCom.CreateStore();

                    try
                    {
                        IAzAuthorizationStore3 authStore = _service.GetAuthStoreOrThrow(storePath);

                        // Initialize target store (create mode)
                        targetStore.Initialize(AZ_AZSTORE_FLAG_CREATE, targetUrl, Variant.Missing);

                        // Copy store properties
                        targetStore.put_Description(CopyString(authStore.get_Description));
                        targetStore.put_ApplicationData(CopyString(authStore.get_ApplicationData));
                        try { targetStore.put_GenerateAudits(authStore.get_GenerateAudits()); } catch (COMException) { }

                        // The children of a freshly created store can only be persisted after the
                        // store itself has been submitted once.
                        targetStore.Submit(0, Variant.Missing);

                        // Copy applications
                        CopyApplications(authStore, targetStore);

                        // Copy store-level groups
                        CopyStoreGroups(authStore, targetStore);

                        // Copy security info if requested
                        if (includeSecurityInfo)
                        {
                            CopySecurityInfo(authStore, targetStore);
                        }

                        targetStore.Submit(0, Variant.Missing);
                        _logger.LogDebug($"[AzManService] Exported store to: {exportPath}");
                    }
                    finally
                    {
                        AzRolesCom.Release(targetStore);
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

                    IAzAuthorizationStore3 targetStore = AzRolesCom.CreateStore();

                    try
                    {
                        IAzAuthorizationStore3 authStore = _service.GetAuthStoreOrThrow(storePath);

                        targetStore.Initialize(AZ_AZSTORE_FLAG_CREATE, targetUrl, Variant.Missing);
                        targetStore.put_Description($"Exported application: {appName}");
                        targetStore.Submit(0, Variant.Missing);

                        // Open source application, create and copy it into the target store
                        authStore.OpenApplication(appName, Variant.Missing, out IAzApplication sourceApp);
                        try
                        {
                            CopyApplication(sourceApp, name => { targetStore.CreateApplication(name, Variant.Missing, out IAzApplication created); return created; });
                        }
                        finally
                        {
                            AzRolesCom.Release(sourceApp);
                        }

                        targetStore.Submit(0, Variant.Missing);
                        _logger.LogDebug($"[AzManService] Exported application '{appName}' to: {exportPath}");
                    }
                    finally
                    {
                        AzRolesCom.Release(targetStore);
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

                    IAzAuthorizationStore3 sourceStore = AzRolesCom.CreateStore();

                    try
                    {
                        sourceStore.Initialize(AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY, sourceUrl, Variant.Missing);

                        // Get first application from source
                        sourceStore.get_Applications(out IAzApplications applications);
                        List<IAzApplication> apps;
                        try
                        {
                            apps = applications.Items();
                        }
                        finally
                        {
                            AzRolesCom.Release(applications);
                        }

                        try
                        {
                            if (apps.Count == 0)
                            {
                                throw new InvalidOperationException("No applications found in import file.");
                            }

                            IAzApplication sourceApp = apps[0];
                            string originalName = CopyString(sourceApp.get_Name);
                            string targetName = newAppName ?? originalName;

                            IAzAuthorizationStore3 authStore = _service.GetAuthStoreOrThrow(storePath);

                            // Check if application already exists
                            bool exists = false;
                            try
                            {
                                authStore.OpenApplication(targetName, Variant.Missing, out IAzApplication existing);
                                AzRolesCom.Release(existing);
                                exists = true;
                            }
                            catch (COMException)
                            {
                                // Application doesn't exist, which is what we want
                            }
                            if (exists)
                            {
                                throw new InvalidOperationException($"Application '{targetName}' already exists in target store.");
                            }

                            // Copy application to target store
                            CopyApplication(sourceApp, name => { authStore.CreateApplication(name, Variant.Missing, out IAzApplication created); return created; }, targetName);

                            authStore.Submit(0, Variant.Missing);
                            _logger.LogDebug($"[AzManService] Imported application '{targetName}' from: {importPath}");
                        }
                        finally
                        {
                            foreach (IAzApplication app in apps)
                            {
                                AzRolesCom.Release(app);
                            }
                        }
                    }
                    finally
                    {
                        AzRolesCom.Release(sourceStore);
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

    private void CopyApplications(IAzAuthorizationStore3 sourceStore, IAzAuthorizationStore3 targetStore)
    {
        sourceStore.get_Applications(out IAzApplications applications);
        List<IAzApplication> apps;
        try
        {
            apps = applications.Items();
        }
        finally
        {
            AzRolesCom.Release(applications);
        }

        foreach (IAzApplication app in apps)
        {
            try
            {
                CopyApplication(app, name => { targetStore.CreateApplication(name, Variant.Missing, out IAzApplication created); return created; });
            }
            finally
            {
                AzRolesCom.Release(app);
            }
        }
    }

    /// <summary>
    /// Copies <paramref name="sourceApp"/> (properties, operations, tasks, groups, roles, scopes) into
    /// a new application produced by <paramref name="createTargetApp"/> (bound to the target container).
    /// </summary>
    private void CopyApplication(IAzApplication sourceApp, Func<string, IAzApplication> createTargetApp, string? newName = null)
    {
        string appName = newName ?? CopyString(sourceApp.get_Name);
        IAzApplication targetApp = createTargetApp(appName);
        try
        {
            // Copy properties
            targetApp.put_Description(CopyString(sourceApp.get_Description));
            targetApp.put_ApplicationData(CopyString(sourceApp.get_ApplicationData));
            try { targetApp.put_GenerateAudits(sourceApp.get_GenerateAudits()); } catch (COMException) { }

            string version = CopyString(sourceApp.get_Version);
            if (!string.IsNullOrEmpty(version))
            {
                targetApp.put_Version(version);
            }

            // AzMan requires a parent to be submitted (instantiated in the store) before children can
            // be created on it — CreateOperation/CreateTask/etc. on an unsubmitted app fail with
            // 0x80072089 ("object's parent is either uninstantiated or deleted"). Submit the app's
            // scalar state now, then build its child collections.
            targetApp.Submit(0, Variant.Missing);

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

            targetApp.Submit(0, Variant.Missing);
        }
        finally
        {
            AzRolesCom.Release(targetApp);
        }
    }

    private void CopyOperations(IAzApplication sourceApp, IAzApplication targetApp)
    {
        sourceApp.get_Operations(out IAzOperations operations);
        List<IAzOperation> ops;
        try
        {
            ops = operations.Items();
        }
        finally
        {
            AzRolesCom.Release(operations);
        }

        foreach (IAzOperation op in ops)
        {
            try
            {
                string name = CopyString(op.get_Name);
                targetApp.CreateOperation(name, Variant.Missing, out IAzOperation targetOp);
                try
                {
                    targetOp.put_Description(CopyString(op.get_Description));
                    try { targetOp.put_OperationID(op.get_OperationID()); } catch (COMException) { }
                    targetOp.put_ApplicationData(CopyString(op.get_ApplicationData));
                    targetOp.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(targetOp);
                }
            }
            finally
            {
                AzRolesCom.Release(op);
            }
        }
    }

    private void CopyTasks(IAzApplication sourceApp, IAzApplication targetApp)
    {
        sourceApp.get_Tasks(out IAzTasks tasks);
        List<IAzTask> taskList;
        try
        {
            taskList = tasks.Items();
        }
        finally
        {
            AzRolesCom.Release(tasks);
        }

        foreach (IAzTask task in taskList)
        {
            try
            {
                string name = CopyString(task.get_Name);
                targetApp.CreateTask(name, Variant.Missing, out IAzTask targetTask);
                try
                {
                    targetTask.put_Description(CopyString(task.get_Description));
                    try { targetTask.put_IsRoleDefinition(task.get_IsRoleDefinition()); } catch (COMException) { }
                    targetTask.put_ApplicationData(CopyString(task.get_ApplicationData));

                    // Copy business rule
                    string bizRule = CopyString(task.get_BizRule);
                    if (!string.IsNullOrEmpty(bizRule))
                    {
                        targetTask.put_BizRuleLanguage(CopyString(task.get_BizRuleLanguage));
                        targetTask.put_BizRule(bizRule);
                    }

                    // Instantiate the task before adding operation/task-link members (see CopyApplication).
                    targetTask.Submit(0, Variant.Missing);

                    // Copy operations
                    foreach (var opName in CopyStringList(task.get_Operations))
                    {
                        try { targetTask.AddOperation(opName, Variant.Missing); } catch { }
                    }

                    // Copy task links
                    foreach (var taskName in CopyStringList(task.get_Tasks))
                    {
                        try { targetTask.AddTask(taskName, Variant.Missing); } catch { }
                    }

                    targetTask.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(targetTask);
                }
            }
            finally
            {
                AzRolesCom.Release(task);
            }
        }
    }

    private void CopyAppGroups(IAzApplication sourceApp, IAzApplication targetApp)
    {
        sourceApp.get_ApplicationGroups(out IAzApplicationGroups groups);
        List<IAzApplicationGroup2> groupList;
        try
        {
            groupList = groups.Items();
        }
        finally
        {
            AzRolesCom.Release(groups);
        }

        foreach (IAzApplicationGroup2 group in groupList)
        {
            try
            {
                string name = CopyString(group.get_Name);
                int groupType = group.get_Type();
                targetApp.CreateApplicationGroup(name, Variant.Missing, out IAzApplicationGroup2 targetGroup);
                try
                {
                    // The previous late-bound code passed the group type into CreateApplicationGroup's
                    // reserved VARIANT (where AzRoles ignores it); set the Type property explicitly.
                    targetGroup.put_Type(groupType);
                    targetGroup.put_Description(CopyString(group.get_Description));

                    if (groupType == (int)AzGroupType.LdapQuery)
                    {
                        targetGroup.put_LdapQuery(CopyString(group.get_LdapQuery));
                        targetGroup.Submit(0, Variant.Missing);
                    }
                    else
                    {
                        // Instantiate the group before adding members (see CopyApplication).
                        targetGroup.Submit(0, Variant.Missing);

                        // Copy members
                        foreach (var member in CopyStringList(group.get_Members))
                        {
                            try { targetGroup.AddMember(member, Variant.Missing); } catch { }
                        }

                        // Copy non-members
                        foreach (var nonMember in CopyStringList(group.get_NonMembers))
                        {
                            try { targetGroup.AddNonMember(nonMember, Variant.Missing); } catch { }
                        }

                        targetGroup.Submit(0, Variant.Missing);
                    }
                }
                finally
                {
                    AzRolesCom.Release(targetGroup);
                }
            }
            finally
            {
                AzRolesCom.Release(group);
            }
        }
    }

    private void CopyRoles(IAzApplication sourceApp, IAzApplication targetApp)
    {
        sourceApp.get_Roles(out IAzRoles roles);
        List<IAzRole> roleList;
        try
        {
            roleList = roles.Items();
        }
        finally
        {
            AzRolesCom.Release(roles);
        }

        foreach (IAzRole role in roleList)
        {
            try
            {
                string name = CopyString(role.get_Name);
                targetApp.CreateRole(name, Variant.Missing, out IAzRole targetRole);
                try
                {
                    targetRole.put_Description(CopyString(role.get_Description));

                    // Instantiate the role before adding task/operation members (see CopyApplication).
                    targetRole.Submit(0, Variant.Missing);

                    // Copy tasks
                    foreach (var taskName in CopyStringList(role.get_Tasks))
                    {
                        try { targetRole.AddTask(taskName, Variant.Missing); } catch { }
                    }

                    // Copy operations
                    foreach (var opName in CopyStringList(role.get_Operations))
                    {
                        try { targetRole.AddOperation(opName, Variant.Missing); } catch { }
                    }

                    // Note: Members are not copied as they are security principals specific to the source environment

                    targetRole.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(targetRole);
                }
            }
            finally
            {
                AzRolesCom.Release(role);
            }
        }
    }

    private void CopyScopes(IAzApplication sourceApp, IAzApplication targetApp)
    {
        sourceApp.get_Scopes(out IAzScopes scopes);
        List<IAzScope> scopeList;
        try
        {
            scopeList = scopes.Items();
        }
        finally
        {
            AzRolesCom.Release(scopes);
        }

        foreach (IAzScope scope in scopeList)
        {
            try
            {
                string name = CopyString(scope.get_Name);
                targetApp.CreateScope(name, Variant.Missing, out IAzScope targetScope);
                try
                {
                    targetScope.put_Description(CopyString(scope.get_Description));
                    targetScope.put_ApplicationData(CopyString(scope.get_ApplicationData));

                    // Copy scope groups, tasks, roles would go here
                    // For simplicity, we're copying basic properties only

                    targetScope.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(targetScope);
                }
            }
            finally
            {
                AzRolesCom.Release(scope);
            }
        }
    }

    private void CopyStoreGroups(IAzAuthorizationStore3 sourceStore, IAzAuthorizationStore3 targetStore)
    {
        sourceStore.get_ApplicationGroups(out IAzApplicationGroups groups);
        List<IAzApplicationGroup2> groupList;
        try
        {
            groupList = groups.Items();
        }
        finally
        {
            AzRolesCom.Release(groups);
        }

        foreach (IAzApplicationGroup2 group in groupList)
        {
            try
            {
                string name = CopyString(group.get_Name);
                int groupType = group.get_Type();
                targetStore.CreateApplicationGroup(name, Variant.Missing, out IAzApplicationGroup2 targetGroup);
                try
                {
                    // See CopyAppGroups: Type must be set explicitly, not via the reserved parameter.
                    targetGroup.put_Type(groupType);
                    targetGroup.put_Description(CopyString(group.get_Description));

                    if (groupType == (int)AzGroupType.LdapQuery)
                    {
                        targetGroup.put_LdapQuery(CopyString(group.get_LdapQuery));
                    }

                    targetGroup.Submit(0, Variant.Missing);
                }
                finally
                {
                    AzRolesCom.Release(targetGroup);
                }
            }
            finally
            {
                AzRolesCom.Release(group);
            }
        }
    }

    private void CopySecurityInfo(IAzAuthorizationStore3 sourceStore, IAzAuthorizationStore3 targetStore)
    {
        // Copy policy administrators
        foreach (var admin in CopyStringList(sourceStore.get_PolicyAdministratorsName))
        {
            try { targetStore.AddPolicyAdministratorName(admin, Variant.Missing); } catch { }
        }

        // Copy policy readers
        foreach (var reader in CopyStringList(sourceStore.get_PolicyReadersName))
        {
            try { targetStore.AddPolicyReaderName(reader, Variant.Missing); } catch { }
        }
    }

    #endregion
}
