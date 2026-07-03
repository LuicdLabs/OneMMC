// ============================================================================
// AzMan Service - Reader Methods
// ============================================================================
// Reader methods: read store, application, group, role, task, operation information
// through the typed AzRoles interfaces (Native/AzRolesNative.cs). Property reads are
// wrapped so a COM failure on one property degrades to a default value instead of
// aborting the whole read, matching the previous reflection-based reader behavior.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class AzManReaders
{
    private readonly AzManService _service;

    public AzManReaders(AzManService service)
    {
        _service = service;
    }

    private ILogger<AzManService> _logger => _service.Logger;
    private void TryReadVersionFromXml(string storeUrl, ref AzAuthorizationStoreInfo info) => _service.TryReadVersionFromXml(storeUrl, ref info);
    private static string ExtractStoreName(string path) => AzManService.ExtractStoreName(path);

    #region Safe Read Helpers

    private delegate void VariantGetter(out Variant value);

    /// <summary>Reads a BSTR property, returning <paramref name="defaultValue"/> on COM failure or null.</summary>
    private string SafeString(Func<string?> getter, string defaultValue = "")
    {
        try
        {
            return getter() ?? defaultValue;
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] COM property read failed: {Message}", ex.Message);
            return defaultValue;
        }
    }

    /// <summary>Reads a LONG-typed boolean property, returning <paramref name="defaultValue"/> on COM failure.</summary>
    private bool SafeBool(Func<int> getter, bool defaultValue = false)
    {
        try
        {
            return AzRolesCom.ToBool(getter());
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] COM property read failed: {Message}", ex.Message);
            return defaultValue;
        }
    }

    /// <summary>Reads a LONG property, returning <paramref name="defaultValue"/> on COM failure.</summary>
    private int SafeInt(Func<int> getter, int defaultValue = 0)
    {
        try
        {
            return getter();
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] COM property read failed: {Message}", ex.Message);
            return defaultValue;
        }
    }

    /// <summary>
    /// Reads a VARIANT(SAFEARRAY-of-strings) property, returning an empty list on COM failure
    /// (e.g. name resolution needs permissions the caller lacks).
    /// </summary>
    private List<string> SafeStringList(VariantGetter getter)
    {
        try
        {
            getter(out Variant value);
            try
            {
                return value.ToStringList();
            }
            finally
            {
                value.Clear();
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] COM string-array read failed: {Message}", ex.Message);
            return [];
        }
    }

    #endregion

    #region Reader Methods

    /// <summary>
    /// Read store information
    /// </summary>
    internal AzAuthorizationStoreInfo ReadStoreInfo(IAzAuthorizationStore3 store, string storeUrl, AzStoreType storeType)
    {
        var info = new AzAuthorizationStoreInfo
        {
            StorePath = storeUrl,
            Name = ExtractStoreName(storeUrl),
            StoreType = storeType,
            Description = SafeString(store.get_Description),
            IsWritable = SafeBool(store.get_Writable),
            GenerateAudits = SafeBool(store.get_GenerateAudits),
            TargetMachine = SafeString(store.get_TargetMachine),
            // Default to 1.0; overridden below based on store type.
            MajorVersion = 1,
            MinorVersion = 0
        };

        if (storeType == AzStoreType.Xml)
        {
            // For XML stores: parse version attributes directly from the file because the COM
            // object always reports the in-memory defaults, not what is persisted on disk.
            TryReadVersionFromXml(storeUrl, ref info);
        }
        else
        {
            // For Active Directory stores: read the persisted schema version from AD via ADSI.
            //
            // Background: AzRoles COM does NOT populate MajorVersion/MinorVersion when the store
            // is opened with BATCH_UPDATE (4) – the properties remain at defaults (1 / 0)
            // regardless of what is stored in Active Directory.  A MANAGE_STORE_ONLY (0) handle
            // reads them correctly, which is also the mode used by azman.msc.
            (info.MajorVersion, info.MinorVersion) = _service.ReadAdStoreSchemaVersion(storeUrl);
        }

        // Read applications
        try
        {
            store.get_Applications(out IAzApplications applications);
            try
            {
                foreach (IAzApplication app in applications.Items())
                {
                    try
                    {
                        if (ReadApplicationInfo(app) is { } appInfo)
                        {
                            info.Applications.Add(appInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[AzManService] Error reading application: {ex.Message}");
                    }
                    finally
                    {
                        AzRolesCom.Release(app);
                    }
                }
            }
            finally
            {
                AzRolesCom.Release(applications);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate applications: {Message}", ex.Message);
        }
        _logger.LogDebug($"[AzManService] Successfully read {info.Applications.Count} applications");

        // Read store-level groups
        try
        {
            store.get_ApplicationGroups(out IAzApplicationGroups groups);
            try
            {
                foreach (IAzApplicationGroup2 group in groups.Items())
                {
                    try
                    {
                        if (ReadGroupInfo(group) is { } groupInfo)
                        {
                            info.Groups.Add(groupInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[AzManService] Error reading group: {ex.Message}");
                    }
                    finally
                    {
                        AzRolesCom.Release(group);
                    }
                }
            }
            finally
            {
                AzRolesCom.Release(groups);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate store groups: {Message}", ex.Message);
        }
        _logger.LogDebug($"[AzManService] Successfully read {info.Groups.Count} groups");

        // Read policy administrators / readers / delegated users
        info.PolicyAdministrators = SafeStringList(store.get_PolicyAdministratorsName);
        info.PolicyReaders = SafeStringList(store.get_PolicyReadersName);
        info.DelegatedPolicyUsers = SafeStringList(store.get_DelegatedPolicyUsersName);

        return info;
    }

    /// <summary>
    /// Read application information
    /// </summary>
    internal AzApplicationInfo? ReadApplicationInfo(IAzApplication app)
    {
        if (app == null) return null;

        var info = new AzApplicationInfo
        {
            Name = SafeString(app.get_Name),
            Description = SafeString(app.get_Description),
            GenerateAudits = SafeBool(app.get_GenerateAudits),
            ApplicationData = SafeString(app.get_ApplicationData),
            AuthzInterfaceClsid = SafeString(app.get_AuthzInterfaceClsid),
            // The AzRoles property is named Version (the previous "ApplicationVersion" name never
            // existed in the typelib and always fell back to Version under late binding).
            Version = SafeString(app.get_Version)
        };

        // Read application groups
        info.Groups = ReadGroups(app);

        // Read roles (role assignments)
        info.RoleAssignments = ReadRoleAssignments(GetRoles(app));

        // Read tasks and role definitions
        ReadTasksAndRoleDefinitions(GetTasks(app), info.RoleDefinitions, info.Tasks);

        // Read operations
        info.Operations = ReadOperations(app);

        // Read scopes
        info.Scopes = ReadScopes(app);

        // Read policy administrators, readers, and delegated users
        info.PolicyAdministrators = SafeStringList(app.get_PolicyAdministratorsName);
        info.PolicyReaders = SafeStringList(app.get_PolicyReadersName);
        info.DelegatedPolicyUsers = SafeStringList(app.get_DelegatedPolicyUsersName);

        return info;
    }

    private List<AzApplicationGroupInfo> ReadGroups(IAzApplication app)
    {
        var result = new List<AzApplicationGroupInfo>();
        try
        {
            app.get_ApplicationGroups(out IAzApplicationGroups groups);
            try
            {
                foreach (IAzApplicationGroup2 group in groups.Items())
                {
                    try
                    {
                        if (ReadGroupInfo(group) is { } groupInfo)
                        {
                            result.Add(groupInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[AzManService] Error reading group: {ex.Message}");
                    }
                    finally
                    {
                        AzRolesCom.Release(group);
                    }
                }
            }
            finally
            {
                AzRolesCom.Release(groups);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate groups: {Message}", ex.Message);
        }
        return result;
    }

    private List<IAzRole> GetRoles(IAzApplication app)
    {
        try
        {
            app.get_Roles(out IAzRoles roles);
            try
            {
                return roles.Items();
            }
            finally
            {
                AzRolesCom.Release(roles);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate roles: {Message}", ex.Message);
            return [];
        }
    }

    private List<IAzTask> GetTasks(IAzApplication app)
    {
        try
        {
            app.get_Tasks(out IAzTasks tasks);
            try
            {
                return tasks.Items();
            }
            finally
            {
                AzRolesCom.Release(tasks);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate tasks: {Message}", ex.Message);
            return [];
        }
    }

    private List<AzRoleAssignmentInfo> ReadRoleAssignments(List<IAzRole> roles)
    {
        var result = new List<AzRoleAssignmentInfo>();
        foreach (IAzRole role in roles)
        {
            try
            {
                if (ReadRoleAssignmentInfo(role) is { } roleInfo)
                {
                    result.Add(roleInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[AzManService] Error reading role assignment: {ex.Message}");
            }
            finally
            {
                AzRolesCom.Release(role);
            }
        }
        return result;
    }

    private List<AzOperationInfo> ReadOperations(IAzApplication app)
    {
        var result = new List<AzOperationInfo>();
        try
        {
            app.get_Operations(out IAzOperations operations);
            try
            {
                foreach (IAzOperation op in operations.Items())
                {
                    try
                    {
                        if (ReadOperationInfo(op) is { } opInfo)
                        {
                            result.Add(opInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[AzManService] Error reading operation: {ex.Message}");
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
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate operations: {Message}", ex.Message);
        }
        return result;
    }

    private List<AzScopeInfo> ReadScopes(IAzApplication app)
    {
        var result = new List<AzScopeInfo>();
        try
        {
            app.get_Scopes(out IAzScopes scopes);
            try
            {
                foreach (IAzScope scope in scopes.Items())
                {
                    try
                    {
                        if (ReadScopeInfo(scope) is { } scopeInfo)
                        {
                            result.Add(scopeInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[AzManService] Error reading scope: {ex.Message}");
                    }
                    finally
                    {
                        AzRolesCom.Release(scope);
                    }
                }
            }
            finally
            {
                AzRolesCom.Release(scopes);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate scopes: {Message}", ex.Message);
        }
        return result;
    }

    /// <summary>
    /// Read tasks from an already-materialized list, separating role definitions from plain tasks.
    /// Releases every task in the list.
    /// </summary>
    private void ReadTasksAndRoleDefinitions(List<IAzTask> tasks, List<AzRoleDefinitionInfo> roleDefinitions, List<AzTaskInfo> plainTasks)
    {
        foreach (IAzTask task in tasks)
        {
            try
            {
                if (SafeBool(task.get_IsRoleDefinition))
                {
                    if (ReadRoleDefinitionFromTask(task) is { } roleDef)
                    {
                        roleDefinitions.Add(roleDef);
                    }
                }
                else
                {
                    if (ReadTaskInfo(task) is { } taskInfo)
                    {
                        plainTasks.Add(taskInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[AzManService] Error processing task/role definition: {ex.Message}");
            }
            finally
            {
                AzRolesCom.Release(task);
            }
        }
    }

    /// <summary>
    /// Read scope information
    /// </summary>
    internal AzScopeInfo? ReadScopeInfo(IAzScope scope)
    {
        if (scope == null) return null;

        var info = new AzScopeInfo
        {
            Name = SafeString(scope.get_Name),
            Description = SafeString(scope.get_Description),
            ApplicationData = SafeString(scope.get_ApplicationData),
            IsWritable = SafeBool(scope.get_Writable)
        };

        // Read groups in scope
        try
        {
            scope.get_ApplicationGroups(out IAzApplicationGroups groups);
            try
            {
                foreach (IAzApplicationGroup2 group in groups.Items())
                {
                    try
                    {
                        if (ReadGroupInfo(group) is { } groupInfo)
                        {
                            info.Groups.Add(groupInfo);
                        }
                    }
                    finally
                    {
                        AzRolesCom.Release(group);
                    }
                }
            }
            finally
            {
                AzRolesCom.Release(groups);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate scope groups: {Message}", ex.Message);
        }

        // Read tasks and role definitions in scope
        try
        {
            scope.get_Tasks(out IAzTasks tasks);
            try
            {
                ReadTasksAndRoleDefinitions(tasks.Items(), info.Roles, info.Tasks);
            }
            finally
            {
                AzRolesCom.Release(tasks);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate scope tasks: {Message}", ex.Message);
        }

        // Read role assignments in scope
        try
        {
            scope.get_Roles(out IAzRoles roles);
            try
            {
                info.RoleAssignments = ReadRoleAssignments(roles.Items());
            }
            finally
            {
                AzRolesCom.Release(roles);
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug("[AzManService] Failed to enumerate scope roles: {Message}", ex.Message);
        }

        return info;
    }

    /// <summary>
    /// Read group information
    /// </summary>
    internal AzApplicationGroupInfo? ReadGroupInfo(IAzApplicationGroup2 group)
    {
        if (group == null) return null;

        var info = new AzApplicationGroupInfo
        {
            Name = SafeString(group.get_Name),
            Description = SafeString(group.get_Description)
        };

        // Read Type property - values match COM AZ_GROUPTYPE_* constants directly:
        // AZ_GROUPTYPE_LDAP_QUERY = 1, AZ_GROUPTYPE_BASIC = 2, AZ_GROUPTYPE_BIZRULE = 3
        int groupType = SafeInt(group.get_Type, AzManService.AZ_GROUPTYPE_BASIC);

        if (Enum.IsDefined(typeof(AzGroupType), groupType))
        {
            info.GroupType = (AzGroupType)groupType;
        }
        else
        {
            _logger.LogWarning("[AzManService] Unknown group type {GroupType} for group '{GroupName}', defaulting to Basic", groupType, info.Name);
            info.GroupType = AzGroupType.Basic;
        }

        // Read LDAP query (only for LdapQuery groups)
        if (info.GroupType == AzGroupType.LdapQuery)
        {
            info.LdapQuery = SafeString(group.get_LdapQuery);
        }

        _logger.LogDebug("[AzManService] Group '{GroupName}': Type={GroupType}", info.Name, info.GroupType);

        // Read members (both Basic and LDAP Query groups can have members)
        info.Members = SafeStringList(group.get_Members);
        info.MemberNames = SafeStringList(group.get_MembersName);
        info.NonMembers = SafeStringList(group.get_NonMembers);
        info.NonMemberNames = SafeStringList(group.get_NonMembersName);

        // Application group member links
        info.AppMemberLinks = SafeStringList(group.get_AppMembers);
        info.AppNonMemberLinks = SafeStringList(group.get_AppNonMembers);

        // Read business rule properties
        info.BizRule = SafeString(group.get_BizRule);
        info.BizRuleLanguage = SafeString(group.get_BizRuleLanguage);
        info.BizRuleImportedPath = SafeString(group.get_BizRuleImportedPath);

        return info;
    }

    /// <summary>
    /// Read role assignment information
    /// </summary>
    internal AzRoleAssignmentInfo? ReadRoleAssignmentInfo(IAzRole role)
    {
        if (role == null) return null;

        var info = new AzRoleAssignmentInfo
        {
            Name = SafeString(role.get_Name),
            Description = SafeString(role.get_Description)
        };

        // Read members (both SID and Name)
        info.Members = SafeStringList(role.get_Members);
        info.MemberNames = SafeStringList(role.get_MembersName);

        // Read application group member links
        info.AppMemberLinks = SafeStringList(role.get_AppMembers);

        // Read task list
        info.Tasks = SafeStringList(role.get_Tasks);

        // Read operation list
        info.Operations = SafeStringList(role.get_Operations);

        return info;
    }

    /// <summary>
    /// Read role definition information from task
    /// </summary>
    internal AzRoleDefinitionInfo? ReadRoleDefinitionFromTask(IAzTask task)
    {
        if (task == null) return null;

        var info = new AzRoleDefinitionInfo
        {
            Name = SafeString(task.get_Name),
            Description = SafeString(task.get_Description)
        };

        // Read Operations
        info.Operations = SafeStringList(task.get_Operations);

        // Read Tasks (nested task references)
        info.Tasks = SafeStringList(task.get_Tasks);

        // Read business rule properties
        info.BizRule = SafeString(task.get_BizRule);
        info.BizRuleLanguage = SafeString(task.get_BizRuleLanguage);
        info.BizRuleImportedPath = SafeString(task.get_BizRuleImportedPath);

        return info;
    }

    /// <summary>
    /// Read task information
    /// </summary>
    internal AzTaskInfo? ReadTaskInfo(IAzTask task)
    {
        if (task == null) return null;

        var info = new AzTaskInfo
        {
            Name = SafeString(task.get_Name),
            Description = SafeString(task.get_Description),
            ApplicationData = SafeString(task.get_ApplicationData),
            IsRoleDefinition = SafeBool(task.get_IsRoleDefinition)
        };

        // Read Operations
        info.Operations = SafeStringList(task.get_Operations);

        // Read task links (nested task references)
        info.TaskLinks = SafeStringList(task.get_Tasks);

        // Read business rule properties
        info.BizRule = SafeString(task.get_BizRule);
        info.BizRuleLanguage = SafeString(task.get_BizRuleLanguage);
        info.BizRuleImportedPath = SafeString(task.get_BizRuleImportedPath);

        return info;
    }

    /// <summary>
    /// Read operation information
    /// </summary>
    internal AzOperationInfo? ReadOperationInfo(IAzOperation op)
    {
        if (op == null) return null;

        return new AzOperationInfo
        {
            Name = SafeString(op.get_Name),
            Description = SafeString(op.get_Description),
            OperationId = SafeInt(op.get_OperationID),
            ApplicationData = SafeString(op.get_ApplicationData)
        };
    }

    #endregion
}
