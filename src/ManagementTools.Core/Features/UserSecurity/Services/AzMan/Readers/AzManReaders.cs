// ============================================================================
// AzMan Service - Reader Methods
// ============================================================================
// Reader methods: read store, application, group, role, task, operation information
// Uses ComPropertyAccessor for safe COM property access to avoid RuntimeBinderException
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

internal sealed class AzManReaders
{
    private readonly AzManService _service;

    public AzManReaders(AzManService service)
    {
        _service = service;
    }

    private ILogger<AzManService> _logger => _service.Logger;
    private int AZ_GROUPTYPE_BASIC => AzManService.AZ_GROUPTYPE_BASIC;
    private void TryReadVersionFromXml(string storeUrl, ref AzAuthorizationStoreInfo info) => _service.TryReadVersionFromXml(storeUrl, ref info);
    private static string ExtractStoreName(string path) => AzManService.ExtractStoreName(path);

    #region Reader Methods

    /// <summary>
    /// Read store information
    /// </summary>
    internal AzAuthorizationStoreInfo ReadStoreInfo(dynamic store, string storeUrl, AzStoreType storeType)
    {
        var info = new AzAuthorizationStoreInfo
        {
            StorePath = storeUrl,
            Name = ExtractStoreName(storeUrl),
            StoreType = storeType,
            Description = ComPropertyAccessor.GetString(store, "Description"),
            IsWritable = ComPropertyAccessor.GetBool(store, "Writable"),
            GenerateAudits = ComPropertyAccessor.GetBool(store, "GenerateAudits"),
            TargetMachine = ComPropertyAccessor.GetString(store, "TargetMachine"),
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
            // For Active Directory stores: open a transient MANAGE_STORE_ONLY handle to read
            // the persisted schema version from AD.
            //
            // Background: AzRoles COM does NOT populate MajorVersion/MinorVersion when the store
            // is opened with BATCH_UPDATE (4) – the properties remain at defaults (1 / 0)
            // regardless of what is stored in Active Directory.  A MANAGE_STORE_ONLY (0) handle
            // reads them correctly, which is also the mode used by azman.msc.
            //
            // For SQL Server stores the BATCH_UPDATE handle does report version correctly, so
            // that path falls back to a direct COM property read if the dedicated-handle read
            // fails (which it shouldn't for AD stores with the same ProgID).
            (info.MajorVersion, info.MinorVersion) = _service.ReadAdStoreSchemaVersion(storeUrl);
        }

        // Read applications
        object storeObj = store;
        info.Applications = ComPropertyAccessor.GetCollection(storeObj, "Applications", obj => ReadApplicationInfo(obj));
        _logger.LogDebug($"[AzManService] Successfully read {info.Applications.Count} applications");

        // Read store-level groups
        info.Groups = ComPropertyAccessor.GetCollection(storeObj, "ApplicationGroups", obj => ReadGroupInfo(obj));
        _logger.LogDebug($"[AzManService] Successfully read {info.Groups.Count} groups");

        // Read policy administrators
        info.PolicyAdministrators = ComPropertyAccessor.GetStringArray(storeObj, "PolicyAdministratorsName");

        // Read policy readers
        info.PolicyReaders = ComPropertyAccessor.GetStringArray(storeObj, "PolicyReadersName");

        // Read delegated policy users
        info.DelegatedPolicyUsers = ComPropertyAccessor.GetStringArray(storeObj, "DelegatedPolicyUsersName");

        return info;
    }

    /// <summary>
    /// Read application information
    /// </summary>
    internal AzApplicationInfo? ReadApplicationInfo(object app)
    {
        if (app == null) return null;

        var info = new AzApplicationInfo
        {
            Name = ComPropertyAccessor.GetString(app, "Name"),
            Description = ComPropertyAccessor.GetString(app, "Description"),
            GenerateAudits = ComPropertyAccessor.GetBool(app, "GenerateAudits"),
            ApplicationData = ComPropertyAccessor.GetString(app, "ApplicationData"),
            AuthzInterfaceClsid = ComPropertyAccessor.GetString(app, "AuthzInterfaceClsid")
        };

        // Read ApplicationVersion (try ApplicationVersion first, then Version)
        info.Version = ComPropertyAccessor.GetString(app, "ApplicationVersion");
        if (string.IsNullOrEmpty(info.Version))
        {
            info.Version = ComPropertyAccessor.GetString(app, "Version");
        }

        // Read application groups
        info.Groups = ComPropertyAccessor.GetCollection(app, "ApplicationGroups", ReadGroupInfo);

        // Read roles (role assignments)
        info.RoleAssignments = ComPropertyAccessor.GetCollection(app, "Roles", ReadRoleAssignmentInfo);

        // Read tasks and role definitions
        ReadTasksAndRoleDefinitions(app, info);

        // Read operations
        info.Operations = ComPropertyAccessor.GetCollection(app, "Operations", ReadOperationInfo);

        // Read scopes
        info.Scopes = ComPropertyAccessor.GetCollection(app, "Scopes", ReadScopeInfo);

        // Read policy administrators, readers, and delegated users
        info.PolicyAdministrators = ComPropertyAccessor.GetStringArray(app, "PolicyAdministratorsName");
        info.PolicyReaders = ComPropertyAccessor.GetStringArray(app, "PolicyReadersName");
        info.DelegatedPolicyUsers = ComPropertyAccessor.GetStringArray(app, "DelegatedPolicyUsersName");

        return info;
    }

    /// <summary>
    /// Read tasks and separate them into tasks and role definitions
    /// </summary>
    internal void ReadTasksAndRoleDefinitions(object app, AzApplicationInfo info)
    {
        var allTasks = ComPropertyAccessor.GetCollection(app, "Tasks", (object task) =>
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
                        info.RoleDefinitions.Add(roleDef);
                    }
                }
                else
                {
                    var taskInfo = ReadTaskInfo(item.Task);
                    if (taskInfo != null)
                    {
                        info.Tasks.Add(taskInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[AzManService] Error processing task/role definition: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Read scope information
    /// </summary>
    internal AzScopeInfo? ReadScopeInfo(object scope)
    {
        if (scope == null) return null;

        var info = new AzScopeInfo
        {
            Name = ComPropertyAccessor.GetString(scope, "Name"),
            Description = ComPropertyAccessor.GetString(scope, "Description"),
            ApplicationData = ComPropertyAccessor.GetString(scope, "ApplicationData"),
            IsWritable = ComPropertyAccessor.GetBool(scope, "Writable")
        };

        // Read groups in scope
        info.Groups = ComPropertyAccessor.GetCollection(scope, "ApplicationGroups", ReadGroupInfo);

        // Read tasks and role definitions in scope
        ReadScopeTasksAndRoles(scope, info);

        // Read role assignments in scope
        info.RoleAssignments = ComPropertyAccessor.GetCollection(scope, "Roles", ReadRoleAssignmentInfo);

        return info;
    }

    /// <summary>
    /// Read tasks and role definitions within a scope
    /// </summary>
    internal void ReadScopeTasksAndRoles(object scope, AzScopeInfo info)
    {
        var allTasks = ComPropertyAccessor.GetCollection(scope, "Tasks", (object task) =>
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
                        info.Roles.Add(roleDef);
                    }
                }
                else
                {
                    var taskInfo = ReadTaskInfo(item.Task);
                    if (taskInfo != null)
                    {
                        info.Tasks.Add(taskInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[AzManService] Error processing scope task/role: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Read group information
    /// </summary>
    internal AzApplicationGroupInfo? ReadGroupInfo(object group)
    {
        if (group == null) return null;

        var info = new AzApplicationGroupInfo
        {
            Name = ComPropertyAccessor.GetString(group, "Name"),
            Description = ComPropertyAccessor.GetString(group, "Description")
        };

        // Read Type property - values match COM AZ_GROUPTYPE_* constants directly:
        // AZ_GROUPTYPE_LDAP_QUERY = 1, AZ_GROUPTYPE_BASIC = 2, AZ_GROUPTYPE_BIZRULE = 3
        int groupType = ComPropertyAccessor.GetInt(group, "Type", AzManService.AZ_GROUPTYPE_BASIC);

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
            info.LdapQuery = ComPropertyAccessor.GetString(group, "LdapQuery");
        }

        _logger.LogDebug("[AzManService] Group '{GroupName}': Type={GroupType}", info.Name, info.GroupType);

        // Read members (both Basic and LDAP Query groups can have members)
        info.Members = ComPropertyAccessor.GetStringArray(group, "Members");
        info.MemberNames = ComPropertyAccessor.GetStringArray(group, "MembersName");
        info.NonMembers = ComPropertyAccessor.GetStringArray(group, "NonMembers");
        info.NonMemberNames = ComPropertyAccessor.GetStringArray(group, "NonMembersName");

        // Application group member links
        info.AppMemberLinks = ComPropertyAccessor.GetStringArray(group, "AppMembers");
        info.AppNonMemberLinks = ComPropertyAccessor.GetStringArray(group, "AppNonMembers");

        // Read business rule properties
        info.BizRule = ComPropertyAccessor.GetString(group, "BizRule");
        info.BizRuleLanguage = ComPropertyAccessor.GetString(group, "BizRuleLanguage");
        info.BizRuleImportedPath = ComPropertyAccessor.GetString(group, "BizRuleImportedPath");

        return info;
    }

    /// <summary>
    /// Read role assignment information
    /// </summary>
    internal AzRoleAssignmentInfo? ReadRoleAssignmentInfo(object role)
    {
        if (role == null) return null;

        var info = new AzRoleAssignmentInfo
        {
            Name = ComPropertyAccessor.GetString(role, "Name"),
            Description = ComPropertyAccessor.GetString(role, "Description")
        };

        // Read members (both SID and Name)
        info.Members = ComPropertyAccessor.GetStringArray(role, "Members");
        info.MemberNames = ComPropertyAccessor.GetStringArray(role, "MembersName");

        // Read application group member links
        info.AppMemberLinks = ComPropertyAccessor.GetStringArray(role, "AppMembers");

        // Read task list
        info.Tasks = ComPropertyAccessor.GetStringArray(role, "Tasks");

        // Read operation list
        info.Operations = ComPropertyAccessor.GetStringArray(role, "Operations");

        return info;
    }

    /// <summary>
    /// Read role definition information from task
    /// </summary>
    internal AzRoleDefinitionInfo? ReadRoleDefinitionFromTask(object task)
    {
        if (task == null) return null;

        var info = new AzRoleDefinitionInfo
        {
            Name = ComPropertyAccessor.GetString(task, "Name"),
            Description = ComPropertyAccessor.GetString(task, "Description")
        };

        // Read Operations
        info.Operations = ComPropertyAccessor.GetStringArray(task, "Operations");

        // Read Tasks (nested task references)
        info.Tasks = ComPropertyAccessor.GetStringArray(task, "Tasks");

        // Read business rule properties
        info.BizRule = ComPropertyAccessor.GetString(task, "BizRule");
        info.BizRuleLanguage = ComPropertyAccessor.GetString(task, "BizRuleLanguage");
        info.BizRuleImportedPath = ComPropertyAccessor.GetString(task, "BizRuleImportedPath");

        return info;
    }

    /// <summary>
    /// Read task information
    /// </summary>
    internal AzTaskInfo? ReadTaskInfo(object task)
    {
        if (task == null) return null;

        var info = new AzTaskInfo
        {
            Name = ComPropertyAccessor.GetString(task, "Name"),
            Description = ComPropertyAccessor.GetString(task, "Description"),
            ApplicationData = ComPropertyAccessor.GetString(task, "ApplicationData"),
            IsRoleDefinition = ComPropertyAccessor.GetBool(task, "IsRoleDefinition")
        };

        // Read Operations
        info.Operations = ComPropertyAccessor.GetStringArray(task, "Operations");

        // Read task links (nested task references)
        info.TaskLinks = ComPropertyAccessor.GetStringArray(task, "Tasks");

        // Read business rule properties
        info.BizRule = ComPropertyAccessor.GetString(task, "BizRule");
        info.BizRuleLanguage = ComPropertyAccessor.GetString(task, "BizRuleLanguage");
        info.BizRuleImportedPath = ComPropertyAccessor.GetString(task, "BizRuleImportedPath");

        return info;
    }

    /// <summary>
    /// Read operation information
    /// </summary>
    internal AzOperationInfo? ReadOperationInfo(object op)
    {
        if (op == null) return null;

        return new AzOperationInfo
        {
            Name = ComPropertyAccessor.GetString(op, "Name"),
            Description = ComPropertyAccessor.GetString(op, "Description"),
            OperationId = ComPropertyAccessor.GetInt(op, "OperationID"),
            ApplicationData = ComPropertyAccessor.GetString(op, "ApplicationData")
        };
    }

    #endregion
}




