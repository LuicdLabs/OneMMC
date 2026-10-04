# Security Editor: Native ACL Editor and Object Picker, and the WinUI Plan

## Table of Contents

- [Overview](#overview)
- [Current State: Native Dialogs Behind Managed Models](#current-state-native-dialogs-behind-managed-models)
  - [AclEditorService](#acleditorservice)
  - [DirectoryObjectPickerService](#directoryobjectpickerservice)
  - [Callers](#callers)
- [Why Native Dialogs for Now](#why-native-dialogs-for-now)
- [Technical Obstacles](#technical-obstacles)
  - [ACL editor](#acl-editor)
    - [1. The feature surface is a whole product](#1-the-feature-surface-is-a-whole-product)
    - [2. Security-descriptor correctness is unforgiving](#2-security-descriptor-correctness-is-unforgiving)
    - [3. Inheritance and tree propagation](#3-inheritance-and-tree-propagation)
    - [4. Effective access needs Authz](#4-effective-access-needs-authz)
    - [5. SID resolution and principal display](#5-sid-resolution-and-principal-display)
  - [Object picker](#object-picker)
    - [6. Scopes are a directory topology](#6-scopes-are-a-directory-topology)
    - [7. Two providers with different semantics](#7-two-providers-with-different-semantics)
    - [8. Check Names and Advanced search](#8-check-names-and-advanced-search)
    - [9. Well-known principals and filter compatibility](#9-well-known-principals-and-filter-compatibility)
    - [10. Credentials, latency, and failure](#10-credentials-latency-and-failure)
  - [Shared](#shared)
    - [11. The two dialogs are coupled](#11-the-two-dialogs-are-coupled)
    - [12. Nested modal UI in WinUI 3](#12-nested-modal-ui-in-winui-3)
    - [13. Localization](#13-localization)
    - [14. Native AOT](#14-native-aot)
    - [15. Elevation](#15-elevation)
    - [16. Verification](#16-verification)
- [Known Limitations of the Native Approach](#known-limitations-of-the-native-approach)
- [Roadmap: A Standalone WinUI Security Editor](#roadmap-a-standalone-winui-security-editor)
  - [Goals](#goals)
  - [Shape of the app](#shape-of-the-app)
  - [Invocation contract](#invocation-contract)
  - [Stages](#stages)
- [Guidance for New Code](#guidance-for-new-code)
- [References](#references)

## Overview

OneMMC is a WinUI 3 application, yet two of its security dialogs are classic Win32 UI:

| Service | Native component | Dialog |
|---|---|---|
| `AclEditorService` | `aclui.dll` (`EditSecurity`, `EditSecurityAdvanced`) | "Permissions for &lt;object&gt;", "Advanced Security Settings" |
| `DirectoryObjectPickerService` | `objsel.dll` (`IDsObjectPicker`) | "Select Users or Groups" |

Both live in `src/OneMMC.Core/Infrastructure/WindowsCapabilities/`.

This is a deliberate, **temporary** decision. These look like two small dialogs, but behind them are
an ACL editing engine and a directory client that Windows has refined for over twenty years. Rebuilding
them in WinUI is a project of its own, and a defective ACL editor or principal picker is a **security
bug**, not a cosmetic one: it can lock administrators out or grant access to the wrong account.

The plan is to build a **standalone WinUI app that combines the ACL editor and the object picker**,
usable by OneMMC and by other programs. Until it exists, OneMMC calls the native dialogs through
managed request/result models, so callers will not change when the implementation is replaced.

For how to call the object picker today, see [`ObjectPickerService.md`](../ObjectPickerService.md).

## Current State: Native Dialogs Behind Managed Models

### AclEditorService

```csharp
public AclEditorResult EditSecurity(nint ownerWindowHandle, AclEditorRequest request);
```

- **Input**: `AclEditorRequest` — object name/title, full resource path, initial descriptor as SDDL,
  `SI_OBJECT_INFO` flags, access-right rows (`SI_ACCESS`), inherit types (`SI_INHERIT_TYPE`),
  generic mapping, and an optional secondary descriptor (used for the Share tab).
- **Output**: `AclEditorResult` — `WasModified`, the merged `RawSecurityDescriptor`, and the
  `SecurityInformation` bits naming which components changed. The service never touches the
  securable object; the caller persists only the changed sections.
- **Entry point**: the basic page must use `EditSecurity` (`EditSecurityAdvanced` rejects it with
  `E_INVALIDARG`); every other page uses `EditSecurityAdvanced` with the tab to activate packed into
  the high word of `uSIPage` (`COMBINE_PAGE_ACTIVATION`). The tab *set* is decided by the object flags.
- **Callback**: `EditableSecurityInformation` implements `ISecurityInformation`,
  `ISecurityInformation3` (`GetFullResourceName`; `OpenElevatedEditor` → `E_NOTIMPL`),
  `ISecurityInformation4` (secondary/share descriptor), `ISecurityObjectTypeInfo` ("Inherited from"
  via `GetInheritanceSourceW`), and `IEffectivePermission` (→ `E_NOTIMPL`).
  `ISecurityInformation2` is not implemented, so aclui resolves SIDs itself.
- **Merge**: `SetSecurity` merges only the owner/group/DACL/SACL components (and their control bits)
  named by `SecurityInformation`. Replacing the whole descriptor previously dropped the DACL on an
  owner-only change.
- **Memory**: data aclui frees is allocated with `LocalAlloc`; data that must outlive callbacks is
  owned by the callback object and freed in `Dispose`.

### DirectoryObjectPickerService

```csharp
public static List<DirectoryObject>? ShowDialog(IntPtr ownerHwnd, DirectoryObjectPickerOptions options);
```

- **Scopes**: the local computer (WinNT provider) plus one combined scope for joined/enterprise/
  external domains, global catalog, workgroup, and user-entered locations (WinNT + LDAP), all with
  SID paths.
- **Filters**: uplevel (AD group-type bits, users, computers, well-known principals) and downlevel
  (SAM users/groups/computers, well-known SIDs) built from `ObjectPickerTypes`.
  `BuiltInPrincipalsOnly` reproduces the Windows Firewall IPsec computer-authorization dialog exactly.
- **Result**: `DirectoryObject` (`Name`, `AdsPath`, `ObjectClass`, `Upn`, `Sid`) read from the
  `CFSTR_DSOP_DS_SELECTION_LIST` blob. The SID comes from the fetched `objectSid` attribute, then a
  SID embedded in the ADsPath, then `NTAccount.Translate` (LDAP DNs are never guessed into account
  names).
- `null` means canceled **or** failed.

Both services are **modal and synchronous** on the caller's UI thread, and both are Native AOT-safe:
COM interfaces are `[GeneratedComInterface]` ports activated through `ComActivator`/`ComWrappers`
(see [`NativeAot.md`](../NativeAot.md) §COM Interop Model).

### Callers

| Service | Callers |
|---|---|
| `AclEditorService` | `SharePermissionsDialog` (folder security + share ACL as secondary descriptor); `SystemAuditAclEditorService` (Global Object Access Auditing SACL) |
| `DirectoryObjectPickerService` | About 15 dialogs: Local Users and Groups, Services, Task Scheduler, Windows Firewall, Authorization Manager, Local Security Policy, share permissions |

## Why Native Dialogs for Now

1. **Correctness.** aclui and the object picker are the reference implementations used by Explorer
   and every MMC snap-in. Their ACE ordering, inheritance handling, name resolution, and directory
   discovery have been exercised for decades. A new implementation must match them before it can be
   trusted with live security data.
2. **Environment coverage.** The same picker call works on a workgroup machine, a domain member, a
   domain controller, and multi-domain forests with trusts. A rewrite has to rediscover all of that.
3. **Familiarity.** OneMMC replaces MMC snap-ins. Administrators know these dialogs; a dialog that
   behaves differently is a regression in exactly the place where mistakes are expensive.
4. **Cost.** Today the two wrappers total about 2,400 lines. The WinUI equivalent is
   application-sized (see below) and would block every snap-in that needs permissions or principals.
5. **A replaceable seam.** Callers depend on `AclEditorRequest` / `AclEditorResult`,
   `DirectoryObjectPickerOptions` / `DirectoryObject`, SDDL, and SIDs — not on aclui or objsel. Using
   the native dialogs now creates no lock-in.

## Technical Obstacles

### ACL editor

#### 1. The feature surface is a whole product

"The permissions dialog" is a set of windows: the basic page (principal list and an Allow/Deny grid
computed from `SI_ACCESS` flags and implied inherit flags, with a "Special permissions" row);
Advanced Security Settings with Permissions, Auditing, Owner, Effective Access, and Share tabs;
"Disable inheritance" with *Convert* vs *Remove*; "Replace all child object permission entries"; the
Permission Entry and Auditing Entry dialogs ("Applies to", basic/advanced rights, "Only apply these
permissions to objects and/or containers within this container", conditions for conditional ACEs);
and owner change with "Replace owner on subcontainers and objects".

All of it is **data-driven** by caller flags (`SI_ACCESS_GENERAL`, `SI_ACCESS_SPECIFIC`,
`SI_ACCESS_CONTAINER`, `SI_NO_ACL_PROTECT`, `SI_NO_TREE_APPLY`, …). A replacement must keep that
model, not hard-code a file-system layout, or it cannot serve registry keys, services, shares, WMI,
COM+, and the other object types OneMMC edits.

#### 2. Security-descriptor correctness is unforgiving

- **Canonical ACE order** (explicit deny → explicit allow → inherited by generation) on write, and
  correct handling of non-canonical ACLs on read.
- **Lossless round-trip** of ACE types the UI does not edit: object ACEs, callback and conditional
  ACEs, resource-attribute ACEs, scoped-policy-ID ACEs, mandatory-label ACEs. Losing any of them
  during an unrelated edit changes the object's security.
- **Partial-update merging** — the defect already fixed in `MergeSecurityDescriptor` is exactly what
  a new editor would reintroduce.
- **Control bits and edge cases**: `SE_DACL_PROTECTED`, `SE_DACL_AUTO_INHERITED`, NULL DACL (everyone
  has full access) vs empty DACL (nobody has access), generic-right mapping.
- **Privileges**: SACL editing needs `SeSecurityPrivilege`; taking ownership needs
  `SeTakeOwnershipPrivilege` / `SeRestorePrivilege`.

`CommonAcl` canonicalizes and rejects ACE types it does not understand, so the editor must work at
the `RawAcl` level and implement the ordering rules itself.

#### 3. Inheritance and tree propagation

"Inherited from" needs `GetInheritanceSource` with the right object type and generic mapping.
"Replace all child object permission entries" and "Replace owner on subcontainers" are recursive
operations over potentially millions of objects; aclui runs them with progress, cancellation, and a
per-object "Continue / Cancel" error prompt. A WinUI version needs background execution with the same
behavior, without blocking the UI thread.

#### 4. Effective access needs Authz

The Effective Access tab depends on the principal's full token: nested and domain groups, user and
device claims, privileges, callback ACEs. The correct path is the Authz API
(`AuthzInitializeResourceManager`, `AuthzInitializeContextFromSid`, compound device context,
`AuthzAccessCheck`). `AuthzInitializeContextFromSid` must read the user's
`tokenGroupsGlobalAndUniversal` attribute and fails with `ACCESS_DENIED` without that right. Remote
resources need `AuthzInitializeRemoteResourceManager`, and cross-domain evaluation can still miss
domain-local groups. The UI must present such partial results as partial. A SID-only DACL scan gives
wrong answers, which is why `GetEffectivePermission` currently returns `E_NOTIMPL`.

#### 5. SID resolution and principal display

The editor shows names, not SIDs: `LsaLookupSids` locally and against remote targets, unresolvable
SIDs (deleted accounts, unreachable domains), asynchronous resolution so a slow domain controller does
not freeze the dialog, caching, and icons by principal type. aclui does all of this internally today.

### Object picker

#### 6. Scopes are a directory topology

The Locations tree is **discovered at runtime**: target computer, joined domain (`DsGetDcName`,
`DsRoleGetPrimaryDomainInformation`), the rest of the forest, global catalog, external trusts
(`DsEnumerateDomainTrusts`), workgroup, and user-typed locations. Discovery must tolerate offline
domain controllers, partly reachable trusts, and non-domain machines.

#### 7. Two providers with different semantics

- **Downlevel (WinNT/SAM)**: NetAPI32 (`NetUserEnum`, `NetLocalGroupEnum`, `NetGroupEnum`) or ADSI
  `WinNT://`; enumeration only, no LDAP filters.
- **Uplevel (LDAP/AD)**: LDAP queries with group-type bitmasks (universal / global / domain-local ×
  security / distribution), mixed vs native mode, `objectSid` / `userPrincipalName` /
  `sAMAccountName`.

One "Users and Groups" request maps to different queries per provider — the uplevel/downlevel split in
`BuildUplevelFilter` / `BuildDownlevelFilter`. Results must be merged and must keep the ADsPath formats
callers receive today.

#### 8. Check Names and Advanced search

"Check Names" accepts `john`, `CORP\john`, `john@corp.example`, `S-1-5-32-544`, or partial names, and
resolves them through SID parsing, UPN lookup, `LsaLookupNames2`, and AD ambiguous name resolution,
with "Multiple Names Found" and "Name Not Found" follow-up dialogs. These rules exist to stop users
granting rights to the wrong account.

"Advanced…" offers common queries (name/description starts with, disabled accounts, non-expiring
passwords, days since last logon) over result sets that can be very large: paged LDAP search,
server size limits, cancellation, and virtualized lists.

#### 9. Well-known principals and filter compatibility

Everyone, Authenticated Users, SYSTEM, LOCAL SERVICE, INTERACTIVE, BATCH, CREATOR OWNER, and similar
principals are SIDs whose **names Windows localizes**. They must come from `CreateWellKnownSid` +
`LookupAccountSid`, never hard-coded English, and must be filtered per scope like the native
`DSOP_DOWNLEVEL_FILTER_*` flags. Existing call sites rely on precise `DSOP_*` combinations (for
example `BuiltInPrincipalsOnly`), and a replacement that differs would silently offer different
principals.

#### 10. Credentials, latency, and failure

Other domains or remote computers may need alternate credentials (`IDsObjectPickerCredentials`).
Domain controllers can be slow or unreachable, so every query must be asynchronous and cancelable,
with per-location errors instead of a failed dialog. Remote SAM enumeration is also restricted on
modern Windows.

### Shared

#### 11. The two dialogs are coupled

Every "Add", "Select a principal", and "Change owner" button in the ACL editor opens the object
picker, and the ACL editor's principal list needs the same SID ↔ name resolution the picker does. A
WinUI ACL editor that still pops up the Win32 picker is only half a rewrite. **This is why the plan is
one combined app, not two separate rewrites.**

#### 12. Nested modal UI in WinUI 3

Both dialogs stack modals: basic page → Advanced Security Settings → Permission Entry → object picker →
Locations / Object Types / Advanced / Multiple Names Found. WinUI 3 allows one `ContentDialog` per
`XamlRoot`, so the chain needs real owned top-level windows (OneMMC's `ModalDialogWindow` is the
starting point), with owner disabling, focus return, and centering at several levels at once. OneMMC
is also limited to one main window today ([`MultiInstance.md`](MultiInstance.md)), another reason to
host the editor in its own process.

WinUI 3 has no built-in data grid; principal, ACE, and search-result lists (sortable columns,
multi-select, keyboard navigation, accessibility) must be built from `ListView` templates or a
third-party control proven trim- and AOT-safe.

#### 13. Localization

aclui and objsel are localized by Windows into every OS language, with terminology that matches
Explorer and Microsoft documentation ("Full control", "Traverse folder / execute file", "This folder,
subfolders and files"). A WinUI version needs its own `.resw` strings for every right, inherit type,
query, and message ([`Localization.md`](../Localization.md)). Callers already pass localized
`AclEditorAccessEntry.Name` values; the dialog chrome is the new work.

#### 14. Native AOT

`System.DirectoryServices` and `System.DirectoryServices.AccountManagement` are not allowed
([`NativeAot.md`](../NativeAot.md) §Directory and Account Access Model). Directory access must use
NetAPI32 via CsWin32 and the repository ADSI layer (`Core/Infrastructure/Interop/Adsi/`), whose
`AdsiSearcher` would need paged search, attribute selection, and cancellation. LSA, Authz, and DsGetDc
APIs all need AOT-safe projections. Request/response payloads need source-generated JSON.

#### 15. Elevation

Explorer's editor shows shield buttons and re-opens itself elevated through
`ISecurityInformation3::OpenElevatedEditor`. In-process that cannot work, since re-entering aclui does
not elevate the process. The current code relies on OneMMC running elevated (the app manifest requests
`requireAdministrator`), which is also why `SharePermissionsDialog` omits
`SI_OWNER_ELEVATION_REQUIRED`. A general-purpose editor used by **non-elevated** programs needs an
out-of-process elevated broker. A standalone app can provide one; an in-process wrapper cannot.

#### 16. Verification

The repository has no test projects. Replacing either dialog requires:

- a **regression corpus** of SDDL inputs (every ACE type, inheritance shape, and control-bit
  combination), with the new editor's output compared byte-for-byte to aclui's output for the same
  user actions;
- **lab topologies** for the picker: workgroup, single domain, multi-domain forest, external and forest
  trusts, and a non-English OS.

## Known Limitations of the Native Approach

These are accepted today and are what motivates the rewrite:

| Limitation | Detail |
|---|---|
| Visual mismatch | Win32 dialogs: no Fluent styling, Mica, or OneMMC dark theme |
| Language follows Windows | Dialog chrome uses the Windows display language, even when OneMMC's language is overridden with `PrimaryLanguageOverride` |
| Blocking calls | Both are modal and synchronous on the UI thread; the owner window is disabled |
| Fixed ACL tab set | The advanced sheet always has a Permissions tab, so the audit-only Global SACL editor shows a tab native secpol does not (documented on `SystemAuditAclEditorService.EditGlobalObjectAccessPolicy`). No aclui flag removes it |
| Effective Access / elevated editor | Both `E_NOTIMPL` |
| Picker errors | Cancel and failure both return `null` |
| Fixed principal types | Users, groups, computers, and well-known principals only; no first-class service accounts, capability SIDs, or app container SIDs |
| No customization | No OneMMC-specific hints, validation, presets, or recent selections |

## Roadmap: A Standalone WinUI Security Editor

### Goals

- One **standalone WinUI 3 app** providing both the **ACL editor** and the **principal (object)
  picker**, sharing principal resolution, search, and UI components.
- Usable by OneMMC **and by other programs**, so most callers that invoke aclui or `IDsObjectPicker`
  today can use the new UI instead.
- Native AOT, Fluent design, OneMMC localization, light and dark themes.
- Output equivalent to the native dialogs for every supported object type, with fallback to the
  native dialogs for anything not yet covered.

### Shape of the app

```
┌──────────────────────── Security Editor app (WinUI 3, Native AOT) ────────────────────────┐
│                                                                                            │
│   ACL editor UI  ───uses───▶  Principal picker UI                                          │
│   (basic, advanced, entry,     (locations, object types, check names, advanced search)     │
│    owner, auditing, share,                    │                                            │
│    effective access)                          │                                            │
│          │                                    ▼                                            │
│          │                         Principal services                                      │
│          │                         (LSA lookup, SAM/NetAPI, LDAP/ADSI, trusts,             │
│          │                          well-known SIDs, SID ↔ name cache)                     │
│          ▼                                                                                 │
│   Security-descriptor engine (RawAcl level: canonical order, lossless round-trip, merge)   │
│   Authz effective-access engine  ·  Tree-apply worker (progress, cancel, per-item errors)  │
└────────────────────────────────────────────────────────────────────────────────────────────┘
        ▲ request: object description + SDDL (or object path)   │ result: SDDL + changed sections
        │                                                       ▼   or selected principals
   OneMMC (AclEditorService / DirectoryObjectPickerService)  ·  third-party programs
```

The descriptor engine and the principal services belong in a UI-free library, so they can be tested
in isolation and reused by OneMMC directly.

### Invocation contract

The app runs out of process, so requests and results must be serializable. The current models are
close: SDDL, flag tables, access entries, inherit types, and picker options are plain data. The
delegate members of `AclEditorRequest` (`MapGenericAccess`, `EmptySecurityDescriptorFactory`) become
data (`AclEditorGenericMapping`, an initial SDDL).

| Mode | Caller supplies | App does | Use case |
|---|---|---|---|
| **Picker** | `DirectoryObjectPickerOptions` equivalent | Returns `DirectoryObject` list (same `AdsPath` formats, resolved `Sid`) | Any "select users or groups" need |
| **Descriptor** | SDDL + rights/inherit tables | Edits; returns SDDL + changed sections; caller persists | Today's `AclEditorService` behavior; works for virtual objects (global SACL, share ACL) |
| **Object** | Object path + type (file, registry, service, …) | Reads, edits, writes, including tree apply | Callers that do not want to handle descriptors; enables an elevated broker for non-elevated callers |

Transports to evaluate: command-line or protocol activation with a JSON payload, plus a named pipe or
out-of-process COM channel for results and owner-window parenting. The contract is versioned from the
first release.

### Stages

1. **Now — native dialogs behind stable models.** `AclEditorService` and `DirectoryObjectPickerService`
   remain the only entry points. Fix problems in the wrappers; do not add new aclui or
   `IDsObjectPicker` call sites, and do not build custom ACL grids or principal pickers in snap-ins.
2. **Shared engines.** Build the UI-free security-descriptor engine and principal services, plus the
   SDDL regression corpus compared against aclui.
3. **Principal picker.** Ship the WinUI picker first: smaller surface, and the ACL editor depends on
   it. It accepts `DirectoryObjectPickerOptions` (including `BuiltInPrincipalsOnly`) and returns
   `DirectoryObject`, so existing callers switch without code changes. Verify against
   `IDsObjectPicker` in each lab topology.
4. **ACL editor: DACL and owner.** Basic and advanced Permissions pages, Permission Entry, Owner, and
   inheritance display, for file system and registry, in descriptor mode.
5. **Auditing, Share, and audit-only objects.** SACL editing, secondary (share) descriptors, and an
   audit-only layout that removes the Global SACL deviation.
6. **Effective Access and tree apply.** Authz-based evaluation; background propagation with progress.
7. **Object mode and elevation broker.** Support non-elevated callers; publish the invocation contract
   for third-party programs.
8. **Switch OneMMC over.** The two services route to the new app per feature or object type, keeping
   the native dialogs as a fallback until each reaches parity.

## Guidance for New Code

- Use `AclEditorService` for permissions, auditing, and owner editing, and
  `DirectoryObjectPickerService` for choosing principals. Do not build custom equivalents.
- Express requirements through the request/options models (flags, access entries, inherit types,
  `DirectoryObjectPickerOptions`), not by post-processing results, so the intent carries over to the
  new app. Keep new request members serializable. No new delegate-typed members.
- For files and registry keys, supply `FullResourceName`, `ResourceObjectType`, and `GenericMapping`
  so "Inherited from" works.
- Persist only the sections named in `AclEditorResult.SecurityInformation`.
- Do not set `AclEditorObjectFlags.OwnerElevationRequired` while `OpenElevatedEditor` is
  unimplemented. It makes the owner unchangeable.
- Consume `DirectoryObject.Sid`; do not parse `AdsPath`.
- Call both services from Views with a valid owner HWND, never from Core ViewModels.

## References

- Microsoft Learn — [Access Control Editor](https://learn.microsoft.com/windows/win32/secauthz/access-control-editor),
  [`ISecurityInformation`](https://learn.microsoft.com/windows/win32/api/aclui/nn-aclui-isecurityinformation),
  [`EditSecurityAdvanced`](https://learn.microsoft.com/windows/win32/api/aclui/nf-aclui-editsecurityadvanced),
  [`SI_OBJECT_INFO`](https://learn.microsoft.com/windows/win32/api/aclui/ns-aclui-si_object_info)
- Microsoft Learn — [Order of ACEs in a DACL](https://learn.microsoft.com/windows/win32/secauthz/order-of-aces-in-a-dacl),
  [Using Authz API](https://learn.microsoft.com/windows/win32/secauthz/using-authz-api),
  [How to evaluate effective permissions for resources on remote computers](https://learn.microsoft.com/troubleshoot/windows-server/windows-security/access-checks-windows-apis-return-incorrect-results)
- Microsoft Learn — [`IDsObjectPicker`](https://learn.microsoft.com/windows/win32/api/objsel/nn-objsel-idsobjectpicker),
  [`IDsObjectPickerCredentials`](https://learn.microsoft.com/windows/win32/api/objsel/nn-objsel-idsobjectpickercredentials),
  [`DSOP_SCOPE_INIT_INFO`](https://learn.microsoft.com/windows/win32/api/objsel/ns-objsel-dsop_scope_init_info)
- Repository — [`ObjectPickerService.md`](../ObjectPickerService.md), [`NativeAot.md`](../NativeAot.md),
  [`Localization.md`](../Localization.md), [`MultiInstance.md`](MultiInstance.md)
