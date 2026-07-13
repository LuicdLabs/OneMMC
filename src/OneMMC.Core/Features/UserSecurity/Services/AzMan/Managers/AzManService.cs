// ============================================================================
// AzMan Service - Main Class
// ============================================================================
// Provides services for interacting with Windows Authorization Manager (AzMan) COM API.
// Uses source-generated COM interop (Native/AzRolesNative.cs) over the
// AzRoles.AzAuthorizationStore coclass to manage authorization policies.
//
// Supported store types:
// - XML files (msxml://)
// - Active Directory (msldap://)
// - SQL Server (mssql://)
//
// Main features:
// - Create, open, delete authorization stores
// - Manage applications, groups, roles, tasks, operations
// - Export and import authorization policies
//
// File structure:
// - Managers/AzManService.cs          - Main class (constants, fields, properties, Dispose)
// - Managers/StoreManagement.cs       - Store management
// - Managers/ApplicationManagement.cs - Application management
// - Managers/GroupManagement.cs       - Group management
// - Managers/RoleManagement.cs        - Role management (definitions and assignments)
// - Managers/TaskManagement.cs        - Task management
// - Managers/OperationManagement.cs   - Operation management
// - Managers/ScopeManagement.cs       - Scope management
// - Managers/ExportImportManagement.cs - Export and import policy operations
// - Readers/AzManReaders.cs           - Reader helper methods
// - Infrastructure/AzManInfrastructure.cs - Common helper methods
// - Native/AzRolesNative.cs           - Source-generated COM interfaces (vtable order from typelib)
// - Native/AzRolesCom.cs              - Activation + marshalling helpers
// - AzManException.cs                 - Exception class
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

/// <summary>
/// Authorization Manager Service - Manages AzMan authorization stores
/// </summary>
public partial class AzManService : IDisposable
{
    #region COM Constants

    // AzMan initialization flags
    internal const int AZ_AZSTORE_FLAG_CREATE = 1;
    internal const int AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY = 0;
    internal const int AZ_AZSTORE_FLAG_BATCH_UPDATE = 4;

    // AzMan group types (COM API values)
    internal const int AZ_GROUPTYPE_LDAP_QUERY = 1;
    internal const int AZ_GROUPTYPE_BASIC = 2;
    internal const int AZ_GROUPTYPE_BIZRULE = 3;

    #endregion

    #region Fields

    private readonly Dictionary<string, IAzAuthorizationStore3> _authStores = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private readonly object _lockObject = new();
    private readonly List<AzAuthorizationStoreInfo> _openedStores = [];
    private readonly StaTaskScheduler _comScheduler;
    private readonly TaskFactory _comTaskFactory;
    private readonly ILogger<AzManService> _logger;
    private readonly AzManInfrastructure _infrastructure;
    private readonly AzManReaders _readers;
    private readonly StoreManagement _storeManagement;
    private readonly ApplicationManagement _applicationManagement;
    private readonly GroupManagement _groupManagement;
    private readonly RoleManagement _roleManagement;
    private readonly TaskManagement _taskManagement;
    private readonly OperationManagement _operationManagement;
    private readonly ScopeManagement _scopeManagement;
    private readonly ExportImportManagement _exportImportManagement;

    public AzManService(ILogger<AzManService> logger)
    {
        _logger = logger;
        _comScheduler = new StaTaskScheduler("AzMan COM");
        _comTaskFactory = new TaskFactory(_comScheduler);
        _infrastructure = new AzManInfrastructure(this);
        _readers = new AzManReaders(this);
        _storeManagement = new StoreManagement(this);
        _applicationManagement = new ApplicationManagement(this);
        _groupManagement = new GroupManagement(this);
        _roleManagement = new RoleManagement(this);
        _taskManagement = new TaskManagement(this);
        _operationManagement = new OperationManagement(this);
        _scopeManagement = new ScopeManagement(this);
        _exportImportManagement = new ExportImportManagement(this);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Get the list of currently opened stores
    /// </summary>
    public IReadOnlyList<AzAuthorizationStoreInfo> OpenedStores => _openedStores.AsReadOnly();

    /// <summary>
    /// Get whether a store is currently open
    /// </summary>
    public bool HasOpenStore => _authStores.Count > 0;

    #endregion

    #region Internal Service Bridges

    internal IAzAuthorizationStore3? GetAuthStore(string storePath)
    {
        return _authStores.TryGetValue(storePath, out var store) ? store : null;
    }

    internal void SetAuthStore(string storePath, IAzAuthorizationStore3 store)
    {
        _authStores[storePath] = store;
    }

    internal void RemoveAuthStoreInstance(string storePath)
    {
        if (_authStores.TryGetValue(storePath, out var store))
        {
            try { AzRolesCom.Release(store); } catch { }
            _authStores.Remove(storePath);
        }
    }

    internal object LockObject => _lockObject;
    internal List<AzAuthorizationStoreInfo> OpenedStoresInternal => _openedStores;
    internal TaskFactory ComTaskFactory => _comTaskFactory;
    internal ILogger<AzManService> Logger => _logger;

    internal void EnsureStoreOpen(string storePath) => _infrastructure.EnsureStoreOpen(storePath);
    internal void CloseStoreInternal(string storePath) => _infrastructure.CloseStoreInternal(storePath);
    internal static string GetXmlFilePathFromStoreUrl(string storeUrl) => AzManInfrastructure.GetXmlFilePathFromStoreUrl(storeUrl);
    internal void EnsureXmlStoreSchemaV2(string storeUrl) => _infrastructure.EnsureXmlStoreSchemaV2(storeUrl);
    internal void EnsureAdStoreSchemaV2(string storeUrl) => _infrastructure.EnsureAdStoreSchemaV2(storeUrl);
    internal (int MajorVersion, int MinorVersion) ReadAdStoreSchemaVersion(string storeUrl) => _infrastructure.ReadAdStoreSchemaVersion(storeUrl);
    internal IAzAuthorizationStore3 GetAuthStoreOrThrow(string storePath) => _infrastructure.GetAuthStoreOrThrow(storePath);
    internal Task RunComAsync(Action action) => _infrastructure.RunComAsync(action);
    internal Task<T> RunComAsync<T>(Func<T> func) => _infrastructure.RunComAsync(func);
    internal Task RunStoreWriteAsync(string storePath, Action<IAzAuthorizationStore3> action, string errorMessage, string? debugMessage = null)
        => _infrastructure.RunStoreWriteAsync(storePath, action, errorMessage, debugMessage);
    internal Task<T> RunStoreReadAsync<T>(string storePath, Func<IAzAuthorizationStore3, T> func, string errorMessage)
        => _infrastructure.RunStoreReadAsync(storePath, func, errorMessage);
    internal Task RunApplicationWriteAsync(string storePath, string appName, Action<IAzApplication> action, string errorMessage, string? debugMessage = null, bool submitStore = false)
        => _infrastructure.RunApplicationWriteAsync(storePath, appName, action, errorMessage, debugMessage, submitStore);
    internal Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<IAzApplication, T> func, string errorMessage)
        => _infrastructure.RunApplicationReadAsync(storePath, appName, func, errorMessage);
    internal Task RunStoreGroupWriteAsync(string storePath, string groupName, Action<IAzApplicationGroup2> action, string errorMessage, string? debugMessage = null, bool submitStore = true)
        => _infrastructure.RunStoreGroupWriteAsync(storePath, groupName, action, errorMessage, debugMessage, submitStore);
    internal Task RunAppGroupWriteAsync(string storePath, string appName, string groupName, Action<IAzApplicationGroup2> action, string errorMessage, string? debugMessage = null, bool submitApp = true)
        => _infrastructure.RunAppGroupWriteAsync(storePath, appName, groupName, action, errorMessage, debugMessage, submitApp);
    internal Task RunRoleWriteAsync(string storePath, string appName, string roleName, Action<IAzRole> action, string errorMessage, string? debugMessage = null)
        => _infrastructure.RunRoleWriteAsync(storePath, appName, roleName, action, errorMessage, debugMessage);
    internal Task RunTaskWriteAsync(string storePath, string appName, string taskName, Action<IAzTask> action, string errorMessage, string? debugMessage = null)
        => _infrastructure.RunTaskWriteAsync(storePath, appName, taskName, action, errorMessage, debugMessage);
    internal Task RunOperationWriteAsync(string storePath, string appName, string operationName, Action<IAzOperation> action, string errorMessage, string? debugMessage = null)
        => _infrastructure.RunOperationWriteAsync(storePath, appName, operationName, action, errorMessage, debugMessage);
    internal Task RunScopeWriteAsync(string storePath, string appName, string scopeName, Action<IAzScope> action, string errorMessage, string? debugMessage = null, bool submitScope = true, bool submitApp = false)
        => _infrastructure.RunScopeWriteAsync(storePath, appName, scopeName, action, errorMessage, debugMessage, submitScope, submitApp);
    internal Task<T> RunScopeReadAsync<T>(string storePath, string appName, string scopeName, Func<IAzScope, T> func, string errorMessage)
        => _infrastructure.RunScopeReadAsync(storePath, appName, scopeName, func, errorMessage);
    internal static string ExtractStoreName(string path) => AzManInfrastructure.ExtractStoreName(path);
    internal static string GetComErrorMessage(COMException ex) => AzManInfrastructure.GetComErrorMessage(ex);
    internal void TryReadVersionFromXml(string storeUrl, ref AzAuthorizationStoreInfo info) => _infrastructure.TryReadVersionFromXml(storeUrl, ref info);

    internal AzAuthorizationStoreInfo ReadStoreInfo(IAzAuthorizationStore3 store, string storeUrl, AzStoreType storeType) => _readers.ReadStoreInfo(store, storeUrl, storeType);
    internal AzApplicationInfo? ReadApplicationInfo(IAzApplication app) => _readers.ReadApplicationInfo(app);
    internal AzScopeInfo? ReadScopeInfo(IAzScope scope) => _readers.ReadScopeInfo(scope);
    internal AzApplicationGroupInfo? ReadGroupInfo(IAzApplicationGroup2 group) => _readers.ReadGroupInfo(group);
    internal AzRoleAssignmentInfo? ReadRoleAssignmentInfo(IAzRole role) => _readers.ReadRoleAssignmentInfo(role);
    internal AzRoleDefinitionInfo? ReadRoleDefinitionFromTask(IAzTask task) => _readers.ReadRoleDefinitionFromTask(task);
    internal AzTaskInfo? ReadTaskInfo(IAzTask task) => _readers.ReadTaskInfo(task);
    internal AzOperationInfo? ReadOperationInfo(IAzOperation op) => _readers.ReadOperationInfo(op);

    #endregion

    #region Public API Delegation

    public Task<AzAuthorizationStoreInfo> CreateStoreAsync(CreateStoreParameters parameters) => _storeManagement.CreateStoreAsync(parameters);
    public Task<AzAuthorizationStoreInfo> OpenStoreAsync(OpenStoreParameters parameters) => _storeManagement.OpenStoreAsync(parameters);
    public void CloseStore(string storePath) => _storeManagement.CloseStore(storePath);
    public Task DeleteStoreAsync(string storePath) => _storeManagement.DeleteStoreAsync(storePath);
    public Task<AzAuthorizationStoreInfo?> RefreshStoreAsync(string storePath) => _storeManagement.RefreshStoreAsync(storePath);
    public Task UpdateStorePropertiesAsync(string storePath, string description, string applicationData, bool generateAudits) => _storeManagement.UpdateStorePropertiesAsync(storePath, description, applicationData, generateAudits);
    public Task AddPolicyAdministratorAsync(string storePath, string adminName) => _storeManagement.AddPolicyAdministratorAsync(storePath, adminName);
    public Task RemovePolicyAdministratorAsync(string storePath, string adminName) => _storeManagement.RemovePolicyAdministratorAsync(storePath, adminName);
    public Task AddPolicyReaderAsync(string storePath, string readerName) => _storeManagement.AddPolicyReaderAsync(storePath, readerName);
    public Task RemovePolicyReaderAsync(string storePath, string readerName) => _storeManagement.RemovePolicyReaderAsync(storePath, readerName);
    public Task AddDelegatedPolicyUserAsync(string storePath, string userName) => _storeManagement.AddDelegatedPolicyUserAsync(storePath, userName);
    public Task RemoveDelegatedPolicyUserAsync(string storePath, string userName) => _storeManagement.RemoveDelegatedPolicyUserAsync(storePath, userName);
    public Task<StoreAdvancedProperties> GetStoreAdvancedPropertiesAsync(string storePath) => _storeManagement.GetStoreAdvancedPropertiesAsync(storePath);
    public Task UpdateStoreAdvancedPropertiesAsync(string storePath, StoreAdvancedProperties properties) => _storeManagement.UpdateStoreAdvancedPropertiesAsync(storePath, properties);
    public Task UpgradeStoreSchemaToV2Async(string storePath) => _storeManagement.UpgradeStoreSchemaToV2Async(storePath);

    public Task<AzApplicationInfo> CreateApplicationAsync(string storePath, string name, string description = "") => _applicationManagement.CreateApplicationAsync(storePath, name, description);
    public Task DeleteApplicationAsync(string storePath, string appName) => _applicationManagement.DeleteApplicationAsync(storePath, appName);
    public Task<AzApplicationInfo> GetApplicationAsync(string storePath, string appName) => _applicationManagement.GetApplicationAsync(storePath, appName);
    public Task UpdateApplicationAsync(string storePath, string appName, string description) => _applicationManagement.UpdateApplicationAsync(storePath, appName, description);
    public Task UpdateApplicationAsync(string storePath, string appName, string description, string? applicationData) => _applicationManagement.UpdateApplicationAsync(storePath, appName, description, applicationData);
    public Task UpdateApplicationAsync(string storePath, string appName, string description, string? applicationData, string? version, string? authzInterfaceClsid, bool? generateAudits)
        => _applicationManagement.UpdateApplicationAsync(storePath, appName, description, applicationData, version, authzInterfaceClsid, generateAudits);
    public Task AddApplicationPolicyAdministratorAsync(string storePath, string appName, string adminName) => _applicationManagement.AddApplicationPolicyAdministratorAsync(storePath, appName, adminName);
    public Task RemoveApplicationPolicyAdministratorAsync(string storePath, string appName, string adminName) => _applicationManagement.RemoveApplicationPolicyAdministratorAsync(storePath, appName, adminName);
    public Task AddApplicationPolicyReaderAsync(string storePath, string appName, string readerName) => _applicationManagement.AddApplicationPolicyReaderAsync(storePath, appName, readerName);
    public Task RemoveApplicationPolicyReaderAsync(string storePath, string appName, string readerName) => _applicationManagement.RemoveApplicationPolicyReaderAsync(storePath, appName, readerName);
    public Task AddApplicationDelegatedPolicyUserAsync(string storePath, string appName, string userName) => _applicationManagement.AddApplicationDelegatedPolicyUserAsync(storePath, appName, userName);
    public Task RemoveApplicationDelegatedPolicyUserAsync(string storePath, string appName, string userName) => _applicationManagement.RemoveApplicationDelegatedPolicyUserAsync(storePath, appName, userName);

    public Task<AzApplicationGroupInfo> CreateStoreGroupAsync(string storePath, string name, AzGroupType groupType, string description = "", string ldapQuery = "")
        => _groupManagement.CreateStoreGroupAsync(storePath, name, groupType, description, ldapQuery);
    public Task<AzApplicationGroupInfo> CreateAppGroupAsync(string storePath, string appName, string name, AzGroupType groupType, string description = "", string ldapQuery = "")
        => _groupManagement.CreateAppGroupAsync(storePath, appName, name, groupType, description, ldapQuery);
    public Task DeleteStoreGroupAsync(string storePath, string groupName) => _groupManagement.DeleteStoreGroupAsync(storePath, groupName);
    public Task DeleteAppGroupAsync(string storePath, string appName, string groupName) => _groupManagement.DeleteAppGroupAsync(storePath, appName, groupName);
    public Task UpdateStoreGroupAsync(string storePath, string groupName, string description, string ldapQuery = "") => _groupManagement.UpdateStoreGroupAsync(storePath, groupName, description, ldapQuery);
    public Task SetStoreGroupBizRuleAsync(string storePath, string groupName, string bizRule, string bizRuleLanguage) => _groupManagement.SetStoreGroupBizRuleAsync(storePath, groupName, bizRule, bizRuleLanguage);
    public Task AddGroupMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false) => _groupManagement.AddGroupMemberAsync(storePath, groupName, memberSid, isAppGroup);
    public Task RemoveGroupMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false) => _groupManagement.RemoveGroupMemberAsync(storePath, groupName, memberSid, isAppGroup);
    public Task AddGroupMemberAsync(string storePath, string appName, string groupName, string memberSid) => _groupManagement.AddGroupMemberAsync(storePath, appName, groupName, memberSid);
    public Task RemoveGroupMemberAsync(string storePath, string appName, string groupName, string memberSid) => _groupManagement.RemoveGroupMemberAsync(storePath, appName, groupName, memberSid);
    public Task AddAppMemberToGroupAsync(string storePath, string appName, string groupName, string appGroupName) => _groupManagement.AddAppMemberToGroupAsync(storePath, appName, groupName, appGroupName);
    public Task RemoveAppMemberFromGroupAsync(string storePath, string appName, string groupName, string appGroupName) => _groupManagement.RemoveAppMemberFromGroupAsync(storePath, appName, groupName, appGroupName);
    public Task AddGroupNonMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false) => _groupManagement.AddGroupNonMemberAsync(storePath, groupName, memberSid, isAppGroup);
    public Task RemoveGroupNonMemberAsync(string storePath, string groupName, string memberSid, bool isAppGroup = false) => _groupManagement.RemoveGroupNonMemberAsync(storePath, groupName, memberSid, isAppGroup);
    public Task AddGroupNonMemberAsync(string storePath, string appName, string groupName, string memberSid) => _groupManagement.AddGroupNonMemberAsync(storePath, appName, groupName, memberSid);
    public Task RemoveGroupNonMemberAsync(string storePath, string appName, string groupName, string memberSid) => _groupManagement.RemoveGroupNonMemberAsync(storePath, appName, groupName, memberSid);
    public Task AddAppNonMemberToGroupAsync(string storePath, string appName, string groupName, string appGroupName) => _groupManagement.AddAppNonMemberToGroupAsync(storePath, appName, groupName, appGroupName);
    public Task RemoveAppNonMemberFromGroupAsync(string storePath, string appName, string groupName, string appGroupName) => _groupManagement.RemoveAppNonMemberFromGroupAsync(storePath, appName, groupName, appGroupName);
    public Task UpdateAppGroupAsync(string storePath, string appName, string groupName, string description, string ldapQuery = "") => _groupManagement.UpdateAppGroupAsync(storePath, appName, groupName, description, ldapQuery);
    public Task SetAppGroupBizRuleAsync(string storePath, string appName, string groupName, string bizRule, string bizRuleLanguage) => _groupManagement.SetAppGroupBizRuleAsync(storePath, appName, groupName, bizRule, bizRuleLanguage);

    public Task<AzRoleDefinitionInfo> CreateRoleDefinitionAsync(string storePath, string appName, string name, string description = "") => _roleManagement.CreateRoleDefinitionAsync(storePath, appName, name, description);
    public Task DeleteRoleDefinitionAsync(string storePath, string appName, string roleName) => _roleManagement.DeleteRoleDefinitionAsync(storePath, appName, roleName);
    public Task AddTaskToRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string taskName) => _roleManagement.AddTaskToRoleDefinitionAsync(storePath, appName, roleDefinitionName, taskName);
    public Task RemoveTaskFromRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string taskName) => _roleManagement.RemoveTaskFromRoleDefinitionAsync(storePath, appName, roleDefinitionName, taskName);
    public Task<AzRoleAssignmentInfo> CreateRoleAssignmentAsync(string storePath, string appName, string name, string description = "") => _roleManagement.CreateRoleAssignmentAsync(storePath, appName, name, description);
    public Task DeleteRoleAssignmentAsync(string storePath, string appName, string roleName) => _roleManagement.DeleteRoleAssignmentAsync(storePath, appName, roleName);
    public Task AddRoleMemberAsync(string storePath, string appName, string roleName, string memberSid) => _roleManagement.AddRoleMemberAsync(storePath, appName, roleName, memberSid);
    public Task AddTaskToRoleAssignmentAsync(string storePath, string appName, string roleName, string taskName) => _roleManagement.AddTaskToRoleAssignmentAsync(storePath, appName, roleName, taskName);
    public Task RemoveTaskFromRoleAssignmentAsync(string storePath, string appName, string roleName, string taskName) => _roleManagement.RemoveTaskFromRoleAssignmentAsync(storePath, appName, roleName, taskName);
    public Task AddOperationToRoleAssignmentAsync(string storePath, string appName, string roleName, string operationName) => _roleManagement.AddOperationToRoleAssignmentAsync(storePath, appName, roleName, operationName);
    public Task RemoveOperationFromRoleAssignmentAsync(string storePath, string appName, string roleName, string operationName) => _roleManagement.RemoveOperationFromRoleAssignmentAsync(storePath, appName, roleName, operationName);
    public Task RemoveRoleMemberAsync(string storePath, string appName, string roleName, string memberSid) => _roleManagement.RemoveRoleMemberAsync(storePath, appName, roleName, memberSid);
    public Task AddAppMemberToRoleAssignmentAsync(string storePath, string appName, string roleName, string appGroupName) => _roleManagement.AddAppMemberToRoleAssignmentAsync(storePath, appName, roleName, appGroupName);
    public Task RemoveAppMemberFromRoleAssignmentAsync(string storePath, string appName, string roleName, string appGroupName) => _roleManagement.RemoveAppMemberFromRoleAssignmentAsync(storePath, appName, roleName, appGroupName);
    public Task AddOperationToRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string operationName) => _roleManagement.AddOperationToRoleDefinitionAsync(storePath, appName, roleDefinitionName, operationName);
    public Task RemoveOperationFromRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string operationName) => _roleManagement.RemoveOperationFromRoleDefinitionAsync(storePath, appName, roleDefinitionName, operationName);
    public Task UpdateRoleDefinitionAsync(string storePath, string appName, string roleDefinitionName, string description) => _roleManagement.UpdateRoleDefinitionAsync(storePath, appName, roleDefinitionName, description);
    public Task UpdateRoleAssignmentAsync(string storePath, string appName, string roleName, string description) => _roleManagement.UpdateRoleAssignmentAsync(storePath, appName, roleName, description);
    [Obsolete("Please use AddTaskToRoleDefinitionAsync or AddTaskToRoleAssignmentAsync")]
    public Task AddTaskToRoleAsync(string storePath, string appName, string roleName, string taskName) => _roleManagement.AddTaskToRoleAsync(storePath, appName, roleName, taskName);
    [Obsolete("Please use RemoveTaskFromRoleDefinitionAsync or RemoveTaskFromRoleAssignmentAsync")]
    public Task RemoveTaskFromRoleAsync(string storePath, string appName, string roleName, string taskName) => _roleManagement.RemoveTaskFromRoleAsync(storePath, appName, roleName, taskName);

    public Task<AzTaskInfo> CreateTaskAsync(string storePath, string appName, string name, string description = "") => _taskManagement.CreateTaskAsync(storePath, appName, name, description);
    public Task DeleteTaskAsync(string storePath, string appName, string taskName) => _taskManagement.DeleteTaskAsync(storePath, appName, taskName);
    public Task AddOperationToTaskAsync(string storePath, string appName, string taskName, string operationName) => _taskManagement.AddOperationToTaskAsync(storePath, appName, taskName, operationName);
    public Task RemoveOperationFromTaskAsync(string storePath, string appName, string taskName, string operationName) => _taskManagement.RemoveOperationFromTaskAsync(storePath, appName, taskName, operationName);
    public Task UpdateTaskAsync(string storePath, string appName, string taskName, string description, string applicationData = "") => _taskManagement.UpdateTaskAsync(storePath, appName, taskName, description, applicationData);
    public Task AddTaskLinkAsync(string storePath, string appName, string taskName, string linkedTaskName) => _taskManagement.AddTaskLinkAsync(storePath, appName, taskName, linkedTaskName);
    public Task RemoveTaskLinkAsync(string storePath, string appName, string taskName, string linkedTaskName) => _taskManagement.RemoveTaskLinkAsync(storePath, appName, taskName, linkedTaskName);
    public Task SetTaskBizRuleAsync(string storePath, string appName, string taskName, string bizRule, string bizRuleLanguage) => _taskManagement.SetTaskBizRuleAsync(storePath, appName, taskName, bizRule, bizRuleLanguage);
    public Task ClearTaskBizRuleAsync(string storePath, string appName, string taskName) => _taskManagement.ClearTaskBizRuleAsync(storePath, appName, taskName);
    public Task ImportTaskBizRuleAsync(string storePath, string appName, string taskName, string filePath, string bizRuleLanguage) => _taskManagement.ImportTaskBizRuleAsync(storePath, appName, taskName, filePath, bizRuleLanguage);
    public Task SetRoleDefinitionBizRuleAsync(string storePath, string appName, string roleDefName, string bizRule, string bizRuleLanguage) => _taskManagement.SetRoleDefinitionBizRuleAsync(storePath, appName, roleDefName, bizRule, bizRuleLanguage);
    public Task ClearRoleDefinitionBizRuleAsync(string storePath, string appName, string roleDefName) => _taskManagement.ClearRoleDefinitionBizRuleAsync(storePath, appName, roleDefName);
    public Task ImportRoleDefinitionBizRuleAsync(string storePath, string appName, string roleDefName, string filePath, string bizRuleLanguage) => _taskManagement.ImportRoleDefinitionBizRuleAsync(storePath, appName, roleDefName, filePath, bizRuleLanguage);

    public Task<AzOperationInfo> CreateOperationAsync(string storePath, string appName, string name, int operationId, string description = "") => _operationManagement.CreateOperationAsync(storePath, appName, name, operationId, description);
    public Task DeleteOperationAsync(string storePath, string appName, string operationName) => _operationManagement.DeleteOperationAsync(storePath, appName, operationName);
    public Task UpdateOperationAsync(string storePath, string appName, string operationName, string description, string? applicationData = null, int? operationId = null)
        => _operationManagement.UpdateOperationAsync(storePath, appName, operationName, description, applicationData, operationId);
    public Task<AzOperationInfo> GetOperationAsync(string storePath, string appName, string operationName) => _operationManagement.GetOperationAsync(storePath, appName, operationName);
    public Task<int> GetNextOperationIdAsync(string storePath, string appName) => _operationManagement.GetNextOperationIdAsync(storePath, appName);
    public Task<bool> IsOperationIdInUseAsync(string storePath, string appName, int operationId) => _operationManagement.IsOperationIdInUseAsync(storePath, appName, operationId);

    public Task<AzScopeInfo> CreateScopeAsync(string storePath, string appName, string name, string description = "") => _scopeManagement.CreateScopeAsync(storePath, appName, name, description);
    public Task UpdateScopeAsync(string storePath, string appName, string name, string description) => _scopeManagement.UpdateScopeAsync(storePath, appName, name, description);
    public Task DeleteScopeAsync(string storePath, string appName, string scopeName) => _scopeManagement.DeleteScopeAsync(storePath, appName, scopeName);
    public Task<AzScopeInfo> GetScopeAsync(string storePath, string appName, string scopeName) => _scopeManagement.GetScopeAsync(storePath, appName, scopeName);
    public Task<AzApplicationGroupInfo> CreateScopeGroupAsync(string storePath, string appName, string scopeName, string name, AzGroupType groupType, string description = "", string ldapQuery = "")
        => _scopeManagement.CreateScopeGroupAsync(storePath, appName, scopeName, name, groupType, description, ldapQuery);
    public Task AddScopeGroupMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid) => _scopeManagement.AddScopeGroupMemberAsync(storePath, appName, scopeName, groupName, memberSid);
    public Task RemoveScopeGroupMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid) => _scopeManagement.RemoveScopeGroupMemberAsync(storePath, appName, scopeName, groupName, memberSid);
    public Task AddScopeGroupNonMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid) => _scopeManagement.AddScopeGroupNonMemberAsync(storePath, appName, scopeName, groupName, memberSid);
    public Task RemoveScopeGroupNonMemberAsync(string storePath, string appName, string scopeName, string groupName, string memberSid) => _scopeManagement.RemoveScopeGroupNonMemberAsync(storePath, appName, scopeName, groupName, memberSid);
    public Task DeleteScopeGroupAsync(string storePath, string appName, string scopeName, string groupName) => _scopeManagement.DeleteScopeGroupAsync(storePath, appName, scopeName, groupName);
    public Task SetScopeGroupBizRuleAsync(string storePath, string appName, string scopeName, string groupName, string bizRule, string bizRuleLanguage) => _scopeManagement.SetScopeGroupBizRuleAsync(storePath, appName, scopeName, groupName, bizRule, bizRuleLanguage);
    public Task<AzRoleAssignmentInfo> CreateScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string name, string description = "") => _scopeManagement.CreateScopeRoleAssignmentAsync(storePath, appName, scopeName, name, description);
    public Task AddScopeRoleAssignmentMemberAsync(string storePath, string appName, string scopeName, string roleName, string memberSid) => _scopeManagement.AddScopeRoleAssignmentMemberAsync(storePath, appName, scopeName, roleName, memberSid);
    public Task DeleteScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName) => _scopeManagement.DeleteScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName);
    public Task<AzRoleDefinitionInfo> CreateScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string name, string description = "") => _scopeManagement.CreateScopeRoleDefinitionAsync(storePath, appName, scopeName, name, description);
    public Task UpdateScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string name, string description) => _scopeManagement.UpdateScopeRoleDefinitionAsync(storePath, appName, scopeName, name, description);
    public Task DeleteScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string name) => _scopeManagement.DeleteScopeRoleDefinitionAsync(storePath, appName, scopeName, name);
    public Task<AzTaskInfo> CreateScopeTaskAsync(string storePath, string appName, string scopeName, string name, string description = "") => _scopeManagement.CreateScopeTaskAsync(storePath, appName, scopeName, name, description);
    public Task UpdateScopeTaskAsync(string storePath, string appName, string scopeName, string name, string description) => _scopeManagement.UpdateScopeTaskAsync(storePath, appName, scopeName, name, description);
    public Task DeleteScopeTaskAsync(string storePath, string appName, string scopeName, string name) => _scopeManagement.DeleteScopeTaskAsync(storePath, appName, scopeName, name);
    public Task AddOperationToScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string operationName) => _scopeManagement.AddOperationToScopeTaskAsync(storePath, appName, scopeName, taskName, operationName);
    public Task RemoveOperationFromScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string operationName) => _scopeManagement.RemoveOperationFromScopeTaskAsync(storePath, appName, scopeName, taskName, operationName);
    public Task AddTaskLinkToScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string linkedTaskName) => _scopeManagement.AddTaskLinkToScopeTaskAsync(storePath, appName, scopeName, taskName, linkedTaskName);
    public Task RemoveTaskLinkFromScopeTaskAsync(string storePath, string appName, string scopeName, string taskName, string linkedTaskName) => _scopeManagement.RemoveTaskLinkFromScopeTaskAsync(storePath, appName, scopeName, taskName, linkedTaskName);
    public Task RemoveScopeRoleAssignmentMemberAsync(string storePath, string appName, string scopeName, string roleName, string memberSid) => _scopeManagement.RemoveScopeRoleAssignmentMemberAsync(storePath, appName, scopeName, roleName, memberSid);
    public Task AddTaskToScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string taskName) => _scopeManagement.AddTaskToScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName, taskName);
    public Task RemoveTaskFromScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string taskName) => _scopeManagement.RemoveTaskFromScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName, taskName);
    public Task AddOperationToScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string operationName) => _scopeManagement.AddOperationToScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName, operationName);
    public Task RemoveOperationFromScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string operationName) => _scopeManagement.RemoveOperationFromScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName, operationName);
    public Task AddAppMemberToScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string appGroupName) => _scopeManagement.AddAppMemberToScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName, appGroupName);
    public Task RemoveAppMemberFromScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string appGroupName) => _scopeManagement.RemoveAppMemberFromScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName, appGroupName);
    public Task UpdateScopeRoleAssignmentAsync(string storePath, string appName, string scopeName, string roleName, string description) => _scopeManagement.UpdateScopeRoleAssignmentAsync(storePath, appName, scopeName, roleName, description);
    public Task AddTaskToScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string taskName) => _scopeManagement.AddTaskToScopeRoleDefinitionAsync(storePath, appName, scopeName, roleDefName, taskName);
    public Task RemoveTaskFromScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string taskName) => _scopeManagement.RemoveTaskFromScopeRoleDefinitionAsync(storePath, appName, scopeName, roleDefName, taskName);
    public Task AddOperationToScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string operationName) => _scopeManagement.AddOperationToScopeRoleDefinitionAsync(storePath, appName, scopeName, roleDefName, operationName);
    public Task RemoveOperationFromScopeRoleDefinitionAsync(string storePath, string appName, string scopeName, string roleDefName, string operationName) => _scopeManagement.RemoveOperationFromScopeRoleDefinitionAsync(storePath, appName, scopeName, roleDefName, operationName);
    public Task SetScopeTaskBizRuleAsync(string storePath, string appName, string scopeName, string taskName, string bizRule, string bizRuleLanguage) => _scopeManagement.SetScopeTaskBizRuleAsync(storePath, appName, scopeName, taskName, bizRule, bizRuleLanguage);
    public Task ClearScopeTaskBizRuleAsync(string storePath, string appName, string scopeName, string taskName) => _scopeManagement.ClearScopeTaskBizRuleAsync(storePath, appName, scopeName, taskName);
    public Task SetScopeRoleDefinitionBizRuleAsync(string storePath, string appName, string scopeName, string roleDefName, string bizRule, string bizRuleLanguage) => _scopeManagement.SetScopeRoleDefinitionBizRuleAsync(storePath, appName, scopeName, roleDefName, bizRule, bizRuleLanguage);
    public Task ClearScopeRoleDefinitionBizRuleAsync(string storePath, string appName, string scopeName, string roleDefName) => _scopeManagement.ClearScopeRoleDefinitionBizRuleAsync(storePath, appName, scopeName, roleDefName);
    public Task ImportScopeTaskBizRuleAsync(string storePath, string appName, string scopeName, string taskName, string filePath, string bizRuleLanguage) => _scopeManagement.ImportScopeTaskBizRuleAsync(storePath, appName, scopeName, taskName, filePath, bizRuleLanguage);
    public Task ImportScopeRoleDefinitionBizRuleAsync(string storePath, string appName, string scopeName, string roleDefName, string filePath, string bizRuleLanguage) => _scopeManagement.ImportScopeRoleDefinitionBizRuleAsync(storePath, appName, scopeName, roleDefName, filePath, bizRuleLanguage);

    public Task ExportStoreToXmlAsync(string storePath, string exportPath, bool includeSecurityInfo = true) => _exportImportManagement.ExportStoreToXmlAsync(storePath, exportPath, includeSecurityInfo);
    public Task ExportApplicationToXmlAsync(string storePath, string appName, string exportPath) => _exportImportManagement.ExportApplicationToXmlAsync(storePath, appName, exportPath);
    public Task ImportApplicationFromXmlAsync(string storePath, string importPath, string? newAppName = null) => _exportImportManagement.ImportApplicationFromXmlAsync(storePath, importPath, newAppName);

    #endregion

    #region IDisposable

    /// <summary>
    /// Release resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Release resources
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _openedStores.Clear();
            }

            // Release COM wrappers on their owning STA thread.
            foreach (var store in _authStores.Values)
            {
                try
                {
                    RunComAsync(() =>
                    {
                        try
                        {
                            AzRolesCom.Release(store);
                        }
                        catch
                        {
                        }
                    }).GetAwaiter().GetResult();
                }
                catch
                {
                }
            }
            _authStores.Clear();

            if (disposing)
            {
                _comScheduler.Dispose();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Destructor
    /// </summary>
    ~AzManService()
    {
        Dispose(false);
    }

    #endregion
}
