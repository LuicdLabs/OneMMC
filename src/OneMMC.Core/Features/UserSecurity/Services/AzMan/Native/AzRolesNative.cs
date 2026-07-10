using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;

// Source-generated ([GeneratedComInterface]) dual interfaces for the Authorization Manager (AzRoles)
// COM surface. Ported from IDispatch late binding + ProgID-reflection activation for Native AOT
// (doc/NativeAot.md, M3): late binding and reflection COM activation are unsupported there.
// The interfaces derive from the source-generated IDispatch base (Infrastructure/Interop/IDispatch.cs)
// to reproduce the dual vtable layout (IUnknown[3] + IDispatch[4] + members) and are called by vtable.
//
// Member ORDER is the authoritative vtable order and must not change. It was transcribed from the type
// library embedded in %SystemRoot%\System32\azroles.dll on the target OS via the typelib vtable-dump
// tool (retired after the migration; in git history — interface members begin at vtable slot 7).
// Conventions, all verified against that dump:
//   * AzRoles booleans (GenerateAudits, Writable, ApplyStoreSacl, IsRoleDefinition, ...) are LONG
//     (I4) properties, NOT VARIANT_BOOL — late binding used to coerce .NET bools silently; typed
//     callers convert via AzRolesCom.ToBool/FromBool.
//   * Nearly every mutator carries a trailing [in, optional] VARIANT varReserved, and Submit takes
//     ([in, optional] LONG lFlags, [in, optional] VARIANT varReserved) — late binding filled these
//     automatically; typed callers pass Variant.Missing (and 0 for lFlags) explicitly.
//   * "Array of strings" getters (Members, MembersName, PolicyAdministratorsName, Operations, ...)
//     return a VARIANT holding a SAFEARRAY — read via Variant.ToStringList().
//   * Collections (IAzApplications, ...) expose get_Item(index) -> VARIANT(IDispatch) with ONE-BASED
//     indices (verified empirically: Item(0) fails with E_INVALIDARG, Item(1) returns the first
//     element) plus get_Count; enumeration helpers live in AzRolesCom.
//   * The store is declared as IAzAuthorizationStore3 and groups as IAzApplicationGroup2 (the widest
//     interfaces the OS coclass implements) so UpgradeStoresFunctionalLevel and the group BizRule
//     members are reachable; objects returned by narrower-typed methods are QI'd up at the wrapper
//     cast, which the coclass supports.
//   * Members OneMMC never calls keep their vtable slots with the dump's exact ABI (opaque nint for
//     unused interface out-params such as IAzClientContext / IAzApplication2 / IAzRoleAssignments).

/// <summary>
/// Authorization store (AzRoles.AzAuthorizationStore coclass), widest OS interface (v3).
/// Activated via <see cref="AzRolesCom.CreateStore"/>.
/// </summary>
[GeneratedComInterface, Guid("ABC08425-0C86-4FA0-9BE3-7189956C926E")]
internal partial interface IAzAuthorizationStore3 : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_ApplicationData();
    void put_ApplicationData([MarshalAs(UnmanagedType.BStr)] string value);
    int get_DomainTimeout();
    void put_DomainTimeout(int value);
    int get_ScriptEngineTimeout();
    void put_ScriptEngineTimeout(int value);
    int get_MaxScriptEngines();
    void put_MaxScriptEngines(int value);
    int get_GenerateAudits(); // LONG-typed boolean
    void put_GenerateAudits(int value);
    int get_Writable(); // LONG-typed boolean
    void GetProperty(int lPropId, Variant varReserved, out Variant pvarProp); // unused (placeholder)
    void SetProperty(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void AddPropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void DeletePropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void get_PolicyAdministrators(out Variant pvarAdmins); // unused (placeholder; SID form)
    void get_PolicyReaders(out Variant pvarReaders); // unused (placeholder; SID form)
    void AddPolicyAdministrator([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void DeletePolicyAdministrator([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void AddPolicyReader([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
    void DeletePolicyReader([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
    void Initialize(int lFlags, [MarshalAs(UnmanagedType.BStr)] string bstrPolicyUrl, Variant varReserved);
    void UpdateCache(Variant varReserved);
    void Delete(Variant varReserved);
    void get_Applications(out IAzApplications ppApplications);
    void OpenApplication([MarshalAs(UnmanagedType.BStr)] string bstrApplicationName, Variant varReserved, out IAzApplication ppApplication);
    void CreateApplication([MarshalAs(UnmanagedType.BStr)] string bstrApplicationName, Variant varReserved, out IAzApplication ppApplication);
    void DeleteApplication([MarshalAs(UnmanagedType.BStr)] string bstrApplicationName, Variant varReserved);
    void get_ApplicationGroups(out IAzApplicationGroups ppGroups);
    void CreateApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved, out IAzApplicationGroup2 ppGroup);
    void OpenApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved, out IAzApplicationGroup2 ppGroup);
    void DeleteApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved);
    void Submit(int lFlags, Variant varReserved);
    void get_DelegatedPolicyUsers(out Variant pvarUsers); // unused (placeholder; SID form)
    void AddDelegatedPolicyUser([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved); // unused (placeholder)
    void DeleteDelegatedPolicyUser([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string? get_TargetMachine();
    int get_ApplyStoreSacl(); // LONG-typed boolean
    void put_ApplyStoreSacl(int value);
    [PreserveSig] int get_PolicyAdministratorsName(out Variant pvarAdmins);
    [PreserveSig] int get_PolicyReadersName(out Variant pvarReaders);
    void AddPolicyAdministratorName([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved);
    void DeletePolicyAdministratorName([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved);
    void AddPolicyReaderName([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved);
    void DeletePolicyReaderName([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved);
    [PreserveSig] int get_DelegatedPolicyUsersName(out Variant pvarUsers);
    void AddDelegatedPolicyUserName([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved);
    void DeleteDelegatedPolicyUserName([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved);
    void CloseApplication([MarshalAs(UnmanagedType.BStr)] string bstrApplicationName, int lFlags); // unused (placeholder)
    void OpenApplication2([MarshalAs(UnmanagedType.BStr)] string bstrApplicationName, Variant varReserved, out nint ppApplication); // unused (placeholder, IAzApplication2)
    void CreateApplication2([MarshalAs(UnmanagedType.BStr)] string bstrApplicationName, Variant varReserved, out nint ppApplication); // unused (placeholder, IAzApplication2)
    short IsUpdateNeeded(); // unused (placeholder, VARIANT_BOOL)
    short BizruleGroupSupported(); // unused (placeholder, VARIANT_BOOL)
    void UpgradeStoresFunctionalLevel(int lFunctionalLevel);
    short IsFunctionalLevelUpgradeSupported(int lFunctionalLevel); // unused (placeholder, VARIANT_BOOL)
    void GetSchemaVersion(out int plMajorVersion, out int plMinorVersion); // unused (placeholder)
}

/// <summary>An AzMan application.</summary>
[GeneratedComInterface, Guid("987BC7C7-B813-4D27-BEDE-6BA5AE867E95")]
internal partial interface IAzApplication : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Name();
    void put_Name([MarshalAs(UnmanagedType.BStr)] string value); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_ApplicationData();
    void put_ApplicationData([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_AuthzInterfaceClsid();
    void put_AuthzInterfaceClsid([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Version();
    void put_Version([MarshalAs(UnmanagedType.BStr)] string value);
    int get_GenerateAudits(); // LONG-typed boolean
    void put_GenerateAudits(int value);
    int get_ApplyStoreSacl(); // unused (placeholder, LONG-typed boolean)
    void put_ApplyStoreSacl(int value); // unused (placeholder)
    int get_Writable(); // LONG-typed boolean
    void GetProperty(int lPropId, Variant varReserved, out Variant pvarProp); // unused (placeholder)
    void SetProperty(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void get_PolicyAdministrators(out Variant pvarAdmins); // unused (placeholder; SID form)
    void get_PolicyReaders(out Variant pvarReaders); // unused (placeholder; SID form)
    void AddPolicyAdministrator([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void DeletePolicyAdministrator([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void AddPolicyReader([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
    void DeletePolicyReader([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
    void get_Scopes(out IAzScopes ppScopes);
    void OpenScope([MarshalAs(UnmanagedType.BStr)] string bstrScopeName, Variant varReserved, out IAzScope ppScope);
    void CreateScope([MarshalAs(UnmanagedType.BStr)] string bstrScopeName, Variant varReserved, out IAzScope ppScope);
    void DeleteScope([MarshalAs(UnmanagedType.BStr)] string bstrScopeName, Variant varReserved);
    void get_Operations(out IAzOperations ppOperations);
    void OpenOperation([MarshalAs(UnmanagedType.BStr)] string bstrOperationName, Variant varReserved, out IAzOperation ppOperation);
    void CreateOperation([MarshalAs(UnmanagedType.BStr)] string bstrOperationName, Variant varReserved, out IAzOperation ppOperation);
    void DeleteOperation([MarshalAs(UnmanagedType.BStr)] string bstrOperationName, Variant varReserved);
    void get_Tasks(out IAzTasks ppTasks);
    void OpenTask([MarshalAs(UnmanagedType.BStr)] string bstrTaskName, Variant varReserved, out IAzTask ppTask);
    void CreateTask([MarshalAs(UnmanagedType.BStr)] string bstrTaskName, Variant varReserved, out IAzTask ppTask);
    void DeleteTask([MarshalAs(UnmanagedType.BStr)] string bstrTaskName, Variant varReserved);
    void get_ApplicationGroups(out IAzApplicationGroups ppGroups);
    void OpenApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved, out IAzApplicationGroup2 ppGroup);
    void CreateApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved, out IAzApplicationGroup2 ppGroup);
    void DeleteApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved);
    void get_Roles(out IAzRoles ppRoles);
    void OpenRole([MarshalAs(UnmanagedType.BStr)] string bstrRoleName, Variant varReserved, out IAzRole ppRole);
    void CreateRole([MarshalAs(UnmanagedType.BStr)] string bstrRoleName, Variant varReserved, out IAzRole ppRole);
    void DeleteRole([MarshalAs(UnmanagedType.BStr)] string bstrRoleName, Variant varReserved);
    void InitializeClientContextFromToken(ulong ullTokenHandle, Variant varReserved, out nint ppClientContext); // unused (placeholder, IAzClientContext)
    void AddPropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void DeletePropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void Submit(int lFlags, Variant varReserved);
    void InitializeClientContextFromName([MarshalAs(UnmanagedType.BStr)] string clientName, [MarshalAs(UnmanagedType.BStr)] string domainName, Variant varReserved, out nint ppClientContext); // unused (placeholder, IAzClientContext)
    void get_DelegatedPolicyUsers(out Variant pvarUsers); // unused (placeholder; SID form)
    void AddDelegatedPolicyUser([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved); // unused (placeholder)
    void DeleteDelegatedPolicyUser([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved); // unused (placeholder)
    void InitializeClientContextFromStringSid([MarshalAs(UnmanagedType.BStr)] string sidString, int lOptions, Variant varReserved, out nint ppClientContext); // unused (placeholder, IAzClientContext)
    [PreserveSig] int get_PolicyAdministratorsName(out Variant pvarAdmins);
    [PreserveSig] int get_PolicyReadersName(out Variant pvarReaders);
    void AddPolicyAdministratorName([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved);
    void DeletePolicyAdministratorName([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved);
    void AddPolicyReaderName([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved);
    void DeletePolicyReaderName([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved);
    [PreserveSig] int get_DelegatedPolicyUsersName(out Variant pvarUsers);
    void AddDelegatedPolicyUserName([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved);
    void DeleteDelegatedPolicyUserName([MarshalAs(UnmanagedType.BStr)] string bstrUser, Variant varReserved);
}

/// <summary>An AzMan scope within an application.</summary>
[GeneratedComInterface, Guid("00E52487-E08D-4514-B62E-877D5645F5AB")]
internal partial interface IAzScope : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Name();
    void put_Name([MarshalAs(UnmanagedType.BStr)] string value); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_ApplicationData();
    void put_ApplicationData([MarshalAs(UnmanagedType.BStr)] string value);
    int get_Writable(); // LONG-typed boolean
    void GetProperty(int lPropId, Variant varReserved, out Variant pvarProp); // unused (placeholder)
    void SetProperty(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void AddPropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void DeletePropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void get_PolicyAdministrators(out Variant pvarAdmins); // unused (placeholder; SID form)
    void get_PolicyReaders(out Variant pvarReaders); // unused (placeholder; SID form)
    void AddPolicyAdministrator([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void DeletePolicyAdministrator([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void AddPolicyReader([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
    void DeletePolicyReader([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
    void get_ApplicationGroups(out IAzApplicationGroups ppGroups);
    void OpenApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved, out IAzApplicationGroup2 ppGroup);
    void CreateApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved, out IAzApplicationGroup2 ppGroup);
    void DeleteApplicationGroup([MarshalAs(UnmanagedType.BStr)] string bstrGroupName, Variant varReserved);
    void get_Roles(out IAzRoles ppRoles);
    void OpenRole([MarshalAs(UnmanagedType.BStr)] string bstrRoleName, Variant varReserved, out IAzRole ppRole);
    void CreateRole([MarshalAs(UnmanagedType.BStr)] string bstrRoleName, Variant varReserved, out IAzRole ppRole);
    void DeleteRole([MarshalAs(UnmanagedType.BStr)] string bstrRoleName, Variant varReserved);
    void get_Tasks(out IAzTasks ppTasks);
    void OpenTask([MarshalAs(UnmanagedType.BStr)] string bstrTaskName, Variant varReserved, out IAzTask ppTask);
    void CreateTask([MarshalAs(UnmanagedType.BStr)] string bstrTaskName, Variant varReserved, out IAzTask ppTask);
    void DeleteTask([MarshalAs(UnmanagedType.BStr)] string bstrTaskName, Variant varReserved);
    void Submit(int lFlags, Variant varReserved);
    int get_CanBeDelegated(); // unused (placeholder, LONG-typed boolean)
    int get_BizrulesWritable(); // unused (placeholder, LONG-typed boolean)
    [PreserveSig] int get_PolicyAdministratorsName(out Variant pvarAdmins); // unused (placeholder)
    [PreserveSig] int get_PolicyReadersName(out Variant pvarReaders); // unused (placeholder)
    void AddPolicyAdministratorName([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void DeletePolicyAdministratorName([MarshalAs(UnmanagedType.BStr)] string bstrAdmin, Variant varReserved); // unused (placeholder)
    void AddPolicyReaderName([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
    void DeletePolicyReaderName([MarshalAs(UnmanagedType.BStr)] string bstrReader, Variant varReserved); // unused (placeholder)
}

/// <summary>An AzMan application group (v2 interface: includes the BizRule members).</summary>
[GeneratedComInterface, Guid("3F0613FC-B71A-464E-A11D-5B881A56CEFA")]
internal partial interface IAzApplicationGroup2 : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Name();
    void put_Name([MarshalAs(UnmanagedType.BStr)] string value); // unused (placeholder)
    int get_Type();
    void put_Type(int value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_LdapQuery();
    void put_LdapQuery([MarshalAs(UnmanagedType.BStr)] string value);
    [PreserveSig] int get_AppMembers(out Variant pvarMembers);
    [PreserveSig] int get_AppNonMembers(out Variant pvarNonMembers);
    [PreserveSig] int get_Members(out Variant pvarMembers);
    [PreserveSig] int get_NonMembers(out Variant pvarNonMembers);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string value);
    void AddAppMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteAppMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void AddAppNonMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteAppNonMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void AddMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void AddNonMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteNonMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    int get_Writable(); // unused (placeholder, LONG-typed boolean)
    void GetProperty(int lPropId, Variant varReserved, out Variant pvarProp); // unused (placeholder)
    void SetProperty(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void AddPropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void DeletePropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void Submit(int lFlags, Variant varReserved);
    void AddMemberName([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved); // unused (placeholder)
    void DeleteMemberName([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved); // unused (placeholder)
    void AddNonMemberName([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved); // unused (placeholder)
    void DeleteNonMemberName([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved); // unused (placeholder)
    [PreserveSig] int get_MembersName(out Variant pvarMembersName);
    [PreserveSig] int get_NonMembersName(out Variant pvarNonMembersName);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_BizRule();
    void put_BizRule([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_BizRuleLanguage();
    void put_BizRuleLanguage([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_BizRuleImportedPath();
    void put_BizRuleImportedPath([MarshalAs(UnmanagedType.BStr)] string value);
    void RoleAssignments([MarshalAs(UnmanagedType.BStr)] string bstrScopeName, short bRecursive, out nint ppRoleAssignments); // unused (placeholder, IAzRoleAssignments)
}

/// <summary>An AzMan role assignment.</summary>
[GeneratedComInterface, Guid("859E0D8D-62D7-41D8-A034-C0CD5D43FDFA")]
internal partial interface IAzRole : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Name();
    void put_Name([MarshalAs(UnmanagedType.BStr)] string value); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_ApplicationData();
    void put_ApplicationData([MarshalAs(UnmanagedType.BStr)] string value); // unused (placeholder)
    void AddAppMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteAppMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void AddTask([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteTask([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void AddOperation([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteOperation([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void AddMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteMember([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    int get_Writable(); // unused (placeholder, LONG-typed boolean)
    void GetProperty(int lPropId, Variant varReserved, out Variant pvarProp); // unused (placeholder)
    void SetProperty(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    [PreserveSig] int get_AppMembers(out Variant pvarMembers);
    [PreserveSig] int get_Members(out Variant pvarMembers);
    [PreserveSig] int get_Operations(out Variant pvarOperations);
    [PreserveSig] int get_Tasks(out Variant pvarTasks);
    void AddPropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void DeletePropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void Submit(int lFlags, Variant varReserved);
    void AddMemberName([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved); // unused (placeholder)
    void DeleteMemberName([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved); // unused (placeholder)
    [PreserveSig] int get_MembersName(out Variant pvarMembersName);
}

/// <summary>An AzMan task (a role definition when IsRoleDefinition is set).</summary>
[GeneratedComInterface, Guid("CB94E592-2E0E-4A6C-A336-B89A6DC1E388")]
internal partial interface IAzTask : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Name();
    void put_Name([MarshalAs(UnmanagedType.BStr)] string value); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_ApplicationData();
    void put_ApplicationData([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_BizRule();
    void put_BizRule([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_BizRuleLanguage();
    void put_BizRuleLanguage([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_BizRuleImportedPath();
    void put_BizRuleImportedPath([MarshalAs(UnmanagedType.BStr)] string value);
    int get_IsRoleDefinition(); // LONG-typed boolean
    void put_IsRoleDefinition(int value);
    [PreserveSig] int get_Operations(out Variant pvarOperations);
    [PreserveSig] int get_Tasks(out Variant pvarTasks);
    void AddOperation([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteOperation([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void AddTask([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    void DeleteTask([MarshalAs(UnmanagedType.BStr)] string bstrProp, Variant varReserved);
    int get_Writable(); // unused (placeholder, LONG-typed boolean)
    void GetProperty(int lPropId, Variant varReserved, out Variant pvarProp); // unused (placeholder)
    void SetProperty(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void AddPropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void DeletePropertyItem(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void Submit(int lFlags, Variant varReserved);
}

/// <summary>An AzMan operation.</summary>
[GeneratedComInterface, Guid("5E56B24F-EA01-4D61-BE44-C49B5E4EAF74")]
internal partial interface IAzOperation : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Name();
    void put_Name([MarshalAs(UnmanagedType.BStr)] string value); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string? get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string? get_ApplicationData();
    void put_ApplicationData([MarshalAs(UnmanagedType.BStr)] string value);
    int get_OperationID();
    void put_OperationID(int value);
    int get_Writable(); // unused (placeholder, LONG-typed boolean)
    void GetProperty(int lPropId, Variant varReserved, out Variant pvarProp); // unused (placeholder)
    void SetProperty(int lPropId, Variant varProp, Variant varReserved); // unused (placeholder)
    void Submit(int lFlags, Variant varReserved);
}

// The six AzMan collections share one shape — get_Item(1-based index) -> VARIANT(IDispatch),
// get_Count, get__NewEnum — but each has its own IID, so each needs its own declaration.

/// <summary>Collection of <see cref="IAzApplication"/>.</summary>
[GeneratedComInterface, Guid("929B11A9-95C5-4A84-A29A-20AD42C2F16C")]
internal partial interface IAzApplications : IDispatch
{
    void get_Item(int index, out Variant pvarObtPtr);
    int get_Count();
    nint get__NewEnum(); // unused (placeholder, IEnumVARIANT)
}

/// <summary>Collection of <see cref="IAzApplicationGroup2"/>.</summary>
[GeneratedComInterface, Guid("4CE66AD5-9F3C-469D-A911-B99887A7E685")]
internal partial interface IAzApplicationGroups : IDispatch
{
    void get_Item(int index, out Variant pvarObtPtr);
    int get_Count();
    nint get__NewEnum(); // unused (placeholder, IEnumVARIANT)
}

/// <summary>Collection of <see cref="IAzRole"/>.</summary>
[GeneratedComInterface, Guid("95E0F119-13B4-4DAE-B65F-2F7D60D822E4")]
internal partial interface IAzRoles : IDispatch
{
    void get_Item(int index, out Variant pvarObtPtr);
    int get_Count();
    nint get__NewEnum(); // unused (placeholder, IEnumVARIANT)
}

/// <summary>Collection of <see cref="IAzTask"/>.</summary>
[GeneratedComInterface, Guid("B338CCAB-4C85-4388-8C0A-C58592BAD398")]
internal partial interface IAzTasks : IDispatch
{
    void get_Item(int index, out Variant pvarObtPtr);
    int get_Count();
    nint get__NewEnum(); // unused (placeholder, IEnumVARIANT)
}

/// <summary>Collection of <see cref="IAzOperation"/>.</summary>
[GeneratedComInterface, Guid("90EF9C07-9706-49D9-AF80-0438A5F3EC35")]
internal partial interface IAzOperations : IDispatch
{
    void get_Item(int index, out Variant pvarObtPtr);
    int get_Count();
    nint get__NewEnum(); // unused (placeholder, IEnumVARIANT)
}

/// <summary>Collection of <see cref="IAzScope"/>.</summary>
[GeneratedComInterface, Guid("78E14853-9F5E-406D-9B91-6BDBA6973510")]
internal partial interface IAzScopes : IDispatch
{
    void get_Item(int index, out Variant pvarObtPtr);
    int get_Count();
    nint get__NewEnum(); // unused (placeholder, IEnumVARIANT)
}
