// ============================================================================
// AzMan Models
// ============================================================================
// Data models representing Authorization Manager (AzMan) entities.
// These models map to the COM objects defined in azroles.dll.
// 
// AzMan Hierarchy:
// AuthorizationStore -> Applications -> Roles, Tasks, Operations, Groups
//                    -> Groups (Store-level)
// ============================================================================

using System;
using System.Collections.Generic;

namespace ManagementTools.Core.Features.UserSecurity.Models.AzMan;

/// <summary>
/// AzMan authorization store type
/// </summary>
public enum AzStoreType
{
    /// <summary>XML file store</summary>
    Xml,
    /// <summary>Active Directory store</summary>
    ActiveDirectory,
    /// <summary>SQL Server store</summary>
    SqlServer
}

/// <summary>
/// Application group type (values match COM AZ_GROUPTYPE_* constants)
/// </summary>
public enum AzGroupType
{
    /// <summary>LDAP query group - defined by LDAP query (COM AZ_GROUPTYPE_LDAP_QUERY = 1)</summary>
    LdapQuery = 1,
    /// <summary>Basic group - defined by membership (COM AZ_GROUPTYPE_BASIC = 2)</summary>
    Basic = 2,
    /// <summary>Business rule group - membership determined by script (COM AZ_GROUPTYPE_BIZRULE = 3)</summary>
    Bizrule = 3
}

/// <summary>
/// AzMan initialization flags
/// </summary>
[Flags]
public enum AzStoreFlags
{
    /// <summary>Open in read-only mode</summary>
    ManageStoreOnly = 0,
    /// <summary>Create new store</summary>
    Create = 1,
    /// <summary>Open in read-write mode</summary>
    ReadWrite = 2,
    /// <summary>Open in batch mode</summary>
    BatchUpdate = 4,
    /// <summary>Create without opening</summary>
    CreateAndOpen = 3
}

/// <summary>
/// Authorization store model
/// </summary>
public class AzAuthorizationStoreInfo
{
    /// <summary>Store path (URL format)</summary>
    public string StorePath { get; set; } = string.Empty;

    /// <summary>Store name (display name extracted from path)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Store type</summary>
    public AzStoreType StoreType { get; set; }

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Application data (custom field)</summary>
    public string ApplicationData { get; set; } = string.Empty;

    /// <summary>Whether the store is writable</summary>
    public bool IsWritable { get; set; }

    /// <summary>Whether to generate audits</summary>
    public bool GenerateAudits { get; set; }

    /// <summary>Target machine name</summary>
    public string TargetMachine { get; set; } = string.Empty;

    /// <summary>Schema version (1.0 or 2.0)</summary>
    public int MajorVersion { get; set; }

    /// <summary>Minor version</summary>
    public int MinorVersion { get; set; }

    /// <summary>List of applications in this store</summary>
    public List<AzApplicationInfo> Applications { get; set; } = [];

    /// <summary>List of store-level application groups</summary>
    public List<AzApplicationGroupInfo> Groups { get; set; } = [];

    /// <summary>List of policy administrators</summary>
    public List<string> PolicyAdministrators { get; set; } = [];

    /// <summary>List of policy readers</summary>
    public List<string> PolicyReaders { get; set; } = [];

    /// <summary>List of delegated policy users</summary>
    public List<string> DelegatedPolicyUsers { get; set; } = [];

    /// <summary>Get formatted version string</summary>
    public string VersionString => $"{MajorVersion}.{MinorVersion}";
}

/// <summary>
/// Application model
/// </summary>
public class AzApplicationInfo
{
    /// <summary>Application name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Application data (custom field)</summary>
    public string ApplicationData { get; set; } = string.Empty;

    /// <summary>Authorization script interface CLSID</summary>
    public string AuthzInterfaceClsid { get; set; } = string.Empty;

    /// <summary>Version</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Whether to generate audits</summary>
    public bool GenerateAudits { get; set; }

    /// <summary>List of application groups</summary>
    public List<AzApplicationGroupInfo> Groups { get; set; } = [];

    /// <summary>List of role definitions</summary>
    public List<AzRoleDefinitionInfo> RoleDefinitions { get; set; } = [];

    /// <summary>List of role assignments</summary>
    public List<AzRoleAssignmentInfo> RoleAssignments { get; set; } = [];

    /// <summary>List of task definitions</summary>
    public List<AzTaskInfo> Tasks { get; set; } = [];

    /// <summary>List of operation definitions</summary>
    public List<AzOperationInfo> Operations { get; set; } = [];

    /// <summary>List of scopes</summary>
    public List<AzScopeInfo> Scopes { get; set; } = [];

    /// <summary>List of policy administrators</summary>
    public List<string> PolicyAdministrators { get; set; } = [];

    /// <summary>List of policy readers</summary>
    public List<string> PolicyReaders { get; set; } = [];

    /// <summary>List of delegated policy users</summary>
    public List<string> DelegatedPolicyUsers { get; set; } = [];
}

/// <summary>
/// Application group model
/// </summary>
public class AzApplicationGroupInfo
{
    /// <summary>Group name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Group type</summary>
    public AzGroupType GroupType { get; set; }

    /// <summary>LDAP query (for LDAP query groups only)</summary>
    public string LdapQuery { get; set; } = string.Empty;

    /// <summary>List of members (SID text format)</summary>
    public List<string> Members { get; set; } = [];

    /// <summary>List of member names</summary>
    public List<string> MemberNames { get; set; } = [];

    /// <summary>List of non-members (SID text format)</summary>
    public List<string> NonMembers { get; set; } = [];

    /// <summary>List of non-member names</summary>
    public List<string> NonMemberNames { get; set; } = [];

    /// <summary>Included application group links</summary>
    public List<string> AppMemberLinks { get; set; } = [];

    /// <summary>Excluded application group links</summary>
    public List<string> AppNonMemberLinks { get; set; } = [];

    /// <summary>Business rule script</summary>
    public string BizRule { get; set; } = string.Empty;

    /// <summary>Business rule script language</summary>
    public string BizRuleLanguage { get; set; } = string.Empty;

    /// <summary>Business rule imported path</summary>
    public string BizRuleImportedPath { get; set; } = string.Empty;

    /// <summary>Get display text for group type</summary>
    public string GroupTypeDisplayText => GroupType switch
    {
        AzGroupType.Basic when !string.IsNullOrEmpty(BizRule) => "Business Rule Application Group",
        AzGroupType.Basic => "Application Basic Group",
        AzGroupType.LdapQuery => "LDAP Query Group",
        AzGroupType.Bizrule => "Business Rule Application Group",
        _ => "Unknown"
    };

    /// <summary>Whether this group uses business rule scripts for membership determination</summary>
    public bool IsBizruleGroup => GroupType == AzGroupType.Bizrule || (GroupType == AzGroupType.Basic && !string.IsNullOrEmpty(BizRule));
}

/// <summary>
/// Role definition model
/// </summary>
public class AzRoleDefinitionInfo
{
    /// <summary>Role name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>List of included operations</summary>
    public List<string> Operations { get; set; } = [];

    /// <summary>List of included tasks</summary>
    public List<string> Tasks { get; set; } = [];

    /// <summary>List of included role links</summary>
    public List<string> RoleLinks { get; set; } = [];

    /// <summary>Business rule script</summary>
    public string BizRule { get; set; } = string.Empty;

    /// <summary>Business rule script language</summary>
    public string BizRuleLanguage { get; set; } = string.Empty;

    /// <summary>Business rule imported path</summary>
    public string BizRuleImportedPath { get; set; } = string.Empty;
}

/// <summary>
/// Role assignment model (AzMan 2.0)
/// </summary>
public class AzRoleAssignmentInfo
{
    /// <summary>Role assignment name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Associated role definition</summary>
    public string RoleDefinition { get; set; } = string.Empty;

    /// <summary>List of members (SID text format)</summary>
    public List<string> Members { get; set; } = [];

    /// <summary>List of member names</summary>
    public List<string> MemberNames { get; set; } = [];

    /// <summary>List of included application groups</summary>
    public List<string> AppMemberLinks { get; set; } = [];

    /// <summary>List of included tasks</summary>
    public List<string> Tasks { get; set; } = [];

    /// <summary>List of included operations</summary>
    public List<string> Operations { get; set; } = [];
}

/// <summary>
/// Task model
/// </summary>
public class AzTaskInfo
{
    /// <summary>Task name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Application data (custom field)</summary>
    public string ApplicationData { get; set; } = string.Empty;

    /// <summary>Whether this is a role definition</summary>
    public bool IsRoleDefinition { get; set; }

    /// <summary>List of included operations</summary>
    public List<string> Operations { get; set; } = [];

    /// <summary>List of included task links</summary>
    public List<string> TaskLinks { get; set; } = [];

    /// <summary>Business rule script</summary>
    public string BizRule { get; set; } = string.Empty;

    /// <summary>Business rule script language</summary>
    public string BizRuleLanguage { get; set; } = string.Empty;

    /// <summary>Business rule imported path</summary>
    public string BizRuleImportedPath { get; set; } = string.Empty;
}

/// <summary>
/// Operation model
/// </summary>
public class AzOperationInfo
{
    /// <summary>Operation name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Application data (custom field)</summary>
    public string ApplicationData { get; set; } = string.Empty;

    /// <summary>Operation ID (unique numeric identifier)</summary>
    public int OperationId { get; set; }
}

/// <summary>
/// Scope model
/// </summary>
public class AzScopeInfo
{
    /// <summary>Scope name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Application data (custom field)</summary>
    public string ApplicationData { get; set; } = string.Empty;

    /// <summary>Whether the scope is writable</summary>
    public bool IsWritable { get; set; }

    /// <summary>List of groups in this scope</summary>
    public List<AzApplicationGroupInfo> Groups { get; set; } = [];

    /// <summary>List of role definitions in this scope</summary>
    public List<AzRoleDefinitionInfo> Roles { get; set; } = [];

    /// <summary>List of role assignments in this scope</summary>
    public List<AzRoleAssignmentInfo> RoleAssignments { get; set; } = [];

    /// <summary>List of tasks in this scope</summary>
    public List<AzTaskInfo> Tasks { get; set; } = [];
}

/// <summary>
/// Parameters for opening a store
/// </summary>
public class OpenStoreParameters
{
    /// <summary>Store type</summary>
    public AzStoreType StoreType { get; set; }

    /// <summary>Store path or connection string</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether to open in read-only mode</summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>Get the full store URL</summary>
    public string GetStoreUrl()
    {
        return StoreType switch
        {
            AzStoreType.Xml => $"msxml://{Path}",
            AzStoreType.ActiveDirectory => $"msldap://{Path}",
            AzStoreType.SqlServer => $"mssql://{Path}",
            _ => Path
        };
    }
}

/// <summary>
/// Parameters for creating a store
/// </summary>
public class CreateStoreParameters
{
    /// <summary>Store type</summary>
    public AzStoreType StoreType { get; set; }

    /// <summary>Store path or connection string</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether to generate audits</summary>
    public bool GenerateAudits { get; set; }

    /// <summary>Get the full store URL</summary>
    public string GetStoreUrl()
    {
        return StoreType switch
        {
            AzStoreType.Xml => $"msxml://{Path}",
            AzStoreType.ActiveDirectory => $"msldap://{Path}",
            AzStoreType.SqlServer => $"mssql://{Path}",
            _ => Path
        };
    }
}


