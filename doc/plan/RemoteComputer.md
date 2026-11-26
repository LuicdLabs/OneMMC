# Remote Computers and Policy Stores

## Table of Contents

- [Overview](#overview)
- [Implementation Principle: Native Interfaces Only](#implementation-principle-native-interfaces-only)
- [Snap-in Coverage](#snap-in-coverage)
  - [Not planned](#not-planned)
- [Shared Infrastructure](#shared-infrastructure)
  - [1. The target model](#1-the-target-model)
  - [2. The Select Computer dialog](#2-the-select-computer-dialog)
  - [3. Saved targets](#3-saved-targets)
  - [4. Remote prerequisites and errors](#4-remote-prerequisites-and-errors)
- [Policy Stores: Firewall and Group Policy](#policy-stores-firewall-and-group-policy)
  - [Motivating case: rule merging](#motivating-case-rule-merging)
  - [Group Policy objects: IGroupPolicyObject and registry.pol](#group-policy-objects-igrouppolicyobject-and-registrypol)
  - [Firewall policy stores: FirewallAPI.dll and MS-FASP](#firewall-policy-stores-firewallapidll-and-ms-fasp)
  - [Firewall and GPO targets](#firewall-and-gpo-targets)
- [Technical Obstacles](#technical-obstacles)
- [Open Questions](#open-questions)
- [Roadmap](#roadmap)
- [Guidance for New Code](#guidance-for-new-code)
- [References](#references)

## Overview

Most MMC snap-ins that OneMMC replaces can manage **another computer**, not only the one they run
on: Component Services has *Add Computer* (with *Browse* opening the directory object picker),
services.msc, eventvwr.msc, taskschd.msc, lusrmgr.msc, fsmgmt.msc and diskmgmt.msc have *Connect to
another computer*, perfmon accepts counters from other machines, the Certificates snap-in can target
another computer's store, and printmanagement.msc lists any number of print servers. Some snap-ins
can also target a **policy store** instead of live settings: wf.msc is the same snap-in whether it is
opened directly (local store), inside gpedit.msc (local GPO), or inside the GPMC editor (domain GPO).

OneMMC has neither notion yet. Every feature talks to the local computer, and a setting that only a
GPO can configure is shown read-only with its effective value. This document records:

- which snap-ins support a remote target, and through which native interface (the coverage table);
- the shared pieces every remote-capable snap-in needs (target model, Select Computer dialog, saved
  targets, error handling);
- the policy-store case (firewall and Group Policy), which goes further than "another computer".

## Implementation Principle: Native Interfaces Only

Remote and policy-store targets are implemented on the system's own interfaces, the same layers the
MMC snap-ins use, not on PowerShell:

- **No PowerShell at runtime.** No hosted runspace, no `powershell.exe`/`pwsh.exe` child process, no
  cmdlets (`Set-NetFirewallProfile`, `Open-NetGPO`, `Get-Service -ComputerName`, `Invoke-Command`, ...).
  They are AOT-hostile, slow to start, depend on the PowerShell install and WinRM configuration, and
  are themselves wrappers over the layers below.
- **Each remote-capable Win32 API takes the target directly**: a server-name parameter
  (`OpenSCManager`, `NetUserEnum`, `NetShareEnum`, `EvtOpenSession`, `OpenPrinter`), a connect call
  (`ITaskService::Connect`, `ICOMAdminCatalog::Connect`, `IGroupPolicyObject::OpenRemoteMachineGPO`),
  or a machine-qualified name (PDH counter paths, `\\computer\store` for certificates). Use that
  parameter; do not tunnel the local implementation through a remote shell.
- **WMI is native where it is the system's management interface** (Storage Management `MSFT_Disk`,
  RSoP `root\rsop`), reached remotely with `IWbemLocator::ConnectServer` over DCOM. It is not the
  target layer where it only wraps a lower API, as `root\StandardCimv2` `MSFT_NetFirewall*` wraps the
  firewall service.
- **Thin managed wrappers are acceptable only where they add nothing**: `ServiceController(name,
  machineName)` over `OpenSCManager`, `EventLogSession(computer)` over `EvtOpenSession`. New code prefers
  the API itself through CsWin32 or the existing marshal-free COM wrappers, per
  `.github/copilot-instructions.md` (Native AOT Compatibility).

PowerShell documentation is still useful as a description of semantics, and PowerShell remains a
convenient way to set up test conditions by hand. It is never part of the implementation.

## Snap-in Coverage

"Today" is what OneMMC passes now; every row except Task Scheduler is local-only today.

| Snap-in (Windows) | OneMMC today | Native remote interface | Notes |
|---|---|---|---|
| Component Services (comexp.msc) | `ICOMAdminCatalog`, local registry for DCOM Config | `ICOMAdminCatalog::Connect(machine)`; DCOM Config via `RegConnectRegistry` | The *Add Computer* dialog; COM+ Network Access and Remote Registry on the target |
| Services (services.msc) | `ServiceController`, `OpenSCManager(NULL)` | `OpenSCManager(machine)` | "Remote Service Management" firewall group |
| Event Viewer (eventvwr.msc) | `EventLogSession` (local) | `EvtOpenSession(EvtRpcLogin, computer)` | "Remote Event Log Management" firewall group |
| Task Scheduler (taskschd.msc) | `ITaskService::Connect` with one mutable connection | Same, per target | Already remote-capable; needs the target model ([MultiInstance.md](MultiInstance.md) obstacle 1) |
| Local Users and Groups (lusrmgr.msc) | `NetUserEnum`/`NetLocalGroupEnum` with `servername: default` | Same APIs with `servername` | Remote SAM is restricted by the "Restrict clients allowed to make remote calls to SAM" policy |
| Shared Folders (fsmgmt.msc) | `NetShareEnum`/`NetSessionEnum`/`NetFileEnum` with `servername: default` | Same APIs with `servername` | "File and Printer Sharing" on the target |
| Performance Monitor (perfmon.msc) | PDH, `machineName = "."` | `\\computer\object\counter` paths, `PdhConnectMachine` | Remote Registry service; caller in Performance Monitor Users |
| Disk Management (diskmgmt.msc) | Storage Management WMI (`MSFT_Disk`, `MSFT_Volume`) local | `IWbemLocator::ConnectServer(\\computer\root\Microsoft\Windows\Storage)` | "Remote Volume Management" firewall group |
| Certificates (certlm.msc) | `CertOpenStore`, local machine | `CertOpenStore(CERT_STORE_PROV_SYSTEM, ..., CERT_SYSTEM_STORE_LOCAL_MACHINE, "\\computer\MY")` | Remote open is supported for local-machine and service stores only, not current-user |
| Print Management (printmanagement.msc) | `EnumPrinters`/`OpenPrinter`, local server | `EnumPrinters(PRINTER_ENUM_NAME, "\\server")`, `OpenPrinter("\\server\printer")` | A list of servers rather than one target |
| Local Group Policy Editor (gpedit.msc) | `IGroupPolicyObject::OpenLocalMachineGPO` | `OpenRemoteMachineGPO(computer)` (gpedit.msc `/gpcomputer`) | Wrapper already has the call |
| Resultant Set of Policy (rsop.msc) | RSoP WMI, local | `IWbemLocator::ConnectServer(\\computer\root\rsop)` | Logging mode on the target |
| Windows Defender Firewall (wf.msc) | `INetFwPolicy2`, local WMI, FirewallAPI local store | `FWOpenPolicyStore(machine, LOCAL)` over MS-FASP; GPOs via `IGroupPolicyObject` | See [Policy Stores](#policy-stores-firewall-and-group-policy) |
| Authorization Manager (azman.msc) | Store URLs | Already target-independent | Stores are addressed by URL (`msxml://\\server\share\...`, `msldap://...`), not by computer |

### Not planned

These follow Windows, which does not offer remote management for them either:

- **Device Manager.** Remote access through `CM_Connect_Machine` was removed in Windows 8 / Server
  2012, and remote Device Manager is at most read-only.
- **TPM Management.** tpm.msc is local-only; TBS has no remote form.
- **Local Security Policy.** secpol.msc is local-only. Its settings reach other computers through
  Group Policy, which the GPO targets below cover.

## Shared Infrastructure

### 1. The target model

A single value type describes what a snap-in instance is connected to:

- `Local` (the default, and the only target for the "Not planned" snap-ins);
- `Computer(name)` for the remote rows of the coverage table;
- for policy-store snap-ins, additionally `LocalGpo`, `RemoteLocalGpo(computer)`, `DomainGpo(path)`.

Each snap-in declares which kinds it supports, so the navigation pane only offers *Connect to another
computer* where the coverage table says it works. The target is shown in the breadcrumb and window
title (as MMC shows "Services (COMPUTER)"), so a remote view is never mistaken for the local one.

### 2. The Select Computer dialog

Windows uses one pattern across snap-ins: a dialog with *Local computer* / *Another computer* and a
name box, where *Browse* opens the directory object picker filtered to computers. OneMMC builds that
once:

- `IDsObjectPicker` through the existing `DirectoryObjectPickerService` with
  `ObjectPickerTypes.Computers` (domain, workgroup and user-entered scopes), returning the DNS or
  NetBIOS name;
- name validation before connecting (resolve the name, then let the snap-in's own connect call report
  the real error);
- shared by every remote-capable snap-in, so Component Services, Services and Event Viewer do not each
  grow their own dialog.

### 3. Saved targets

Recently used computers (and GPOs) are saved per user, like Component Services keeps added
computers. A target that fails to connect is kept and shown as unreachable; it is removed only when
the user removes it. This is the same rule the AzMan store list follows: distinguish "gone" from "not
reachable right now".

### 4. Remote prerequisites and errors

Remote failures are expected, not exceptional, and the same few causes recur across snap-ins:

| Cause | Typical error | What the user needs |
|---|---|---|
| Target firewall blocks the interface | RPC server unavailable (`0x800706BA`) | The snap-in's firewall rule group on the target (see the coverage table) |
| Not an administrator on the target | Access denied (`0x80070005`) | Rights on the target; local elevation does not grant them |
| Required service stopped on the target | Varies (for example Remote Registry for PDH and certificates) | Start the service |
| Name does not resolve | Network path not found (`0x80070035`) | Correct the name |

Each snap-in maps its native error to one of these messages through a shared helper, instead of
showing raw HRESULTs, and logs the native code.

## Policy Stores: Firewall and Group Policy

### Motivating case: rule merging

`AllowLocalPolicyMerge` / `AllowLocalIPsecPolicyMerge` decide whether rules created by local
administrators are merged with rules delivered by Group Policy. They are only meaningful in a GPO:

- In the local store the value has no effect, which is why wf.msc disables the controls there.
  OneMMC's `ProfileFirewallBehaviorDialog` does the same: disabled combo boxes showing the value read
  from `HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\{Domain|Private|Public}Profile` (where the
  registry extension copies GPO firewall settings, [MS-GPFAS] 1.3.2), a "managed by Group Policy"
  note when a policy sets it, and an info tooltip pointing to where it is edited.
- In a GPO the setting has three states: not configured (value absent from `registry.pol`), allowed
  (`1`), or not allowed (`0`) ([MS-GPFAS] 2.2.3.13; the value must not appear under the legacy
  `StandardProfile` key).
- Microsoft notes that rules from the **local** GPO are applied even when merging is disabled through
  MDM or a domain GPO, so the useful place to set `0` is a domain GPO.

Making these controls editable means making the page edit a GPO, not adding a write path to the local
store.

### Group Policy objects: IGroupPolicyObject and registry.pol

Firewall settings in a GPO are registry-based policy, stored in the GPO's `registry.pol` under
`Software\Policies\Microsoft\WindowsFirewall\` (profile settings under `DomainProfile` /
`PrivateProfile` / `PublicProfile`, rules as encoded strings under `FirewallRules`) ([MS-GPFAS]). The
native way to edit them is the one gpedit.msc and GPMC use, and `Infrastructure/PolicyStorage`
already wraps most of it (`GroupPolicyObjectWrapper`, `PolFile`):

1. Open the GPO: `IGroupPolicyObject::OpenLocalMachineGPO`, `OpenRemoteMachineGPO`, or `OpenDSGPO`
   (domain GPO by LDAP path; declared today only as a vtable placeholder), with
   `GPO_OPEN_LOAD_REGISTRY`.
2. `GetRegistryKey(GPO_SECTION_MACHINE)` returns a key backed by the GPO's machine `registry.pol`;
   read and write the `WindowsFirewall` values in the [MS-GPFAS] format.
3. `Save(TRUE, TRUE, <extension GUID>, <snap-in GUID>)` writes `registry.pol`, bumps the GPO version,
   and registers the client-side extension.

`IGroupPolicyObject` loads `registry.pol` into a private hive and nothing reaches the GPO until
`Save`, so a GPO target is naturally a session: open when the page targets it, `Save` on OK/Apply,
release on Cancel (the existing `IUnsavedChangesGuard` flow). Changes reach computers only after
Group Policy refreshes, and the UI says so. A failed domain `Save` leaves the page dirty.

### Firewall policy stores: FirewallAPI.dll and MS-FASP

FirewallAPI.dll's `FW*` exports are what wf.msc uses to open a firewall *store*.
`FWOpenPolicyStore(binaryVersion, machineOrGpo, storeType, accessRight, flags, &handle)` takes a
machine name for remote access; remote calls travel over the [MS-FASP] RPC protocol, which documents
the structures (`FW_RULE`, `FW_PROFILE_CONFIG`, ...) and the store types:

| `FW_STORE_TYPE` | Value | Meaning |
|---|---|---|
| `FW_STORE_TYPE_GP_RSOP` | 1 | Policy aggregated from all GPOs; read-only |
| `FW_STORE_TYPE_LOCAL` | 2 | Local store, editable by local administrators |
| `FW_STORE_TYPE_DYNAMIC` | 5 | Effective policy merged from all sources |
| `FW_STORE_TYPE_GPO` | 6 | A GPO; not used on the wire, so local only |
| `FW_STORE_TYPE_DEFAULTS` | 7 | Default policy (value to confirm against MS-FASP 2.2.1) |

Over MS-FASP the server only accepts GP_RSOP, LOCAL, DYNAMIC and DEFAULTS, so a remote computer's GPO
is never opened through this path; GPOs go through `IGroupPolicyObject`.

`FirewallBinaryNativeMethods` already loads these exports dynamically and opens the local store. The
exports have no SDK header, import library or CsWin32 metadata; their structures are specified by
MS-FASP but the export signatures and accepted binary versions are not. New uses follow the existing
pattern (dynamic loading, fallback when an entry point is missing), document each signature's source,
and degrade to "not available on this Windows version" rather than fail the page.

`INetFwPolicy2` (documented, but local store of the local computer only) and WMI `MSFT_NetFirewall*`
(a wrapper over the firewall service, with store selection as an operation option) keep their
current uses; new store-aware code is not built on them.

### Firewall and GPO targets

| Target | Native interface | Editable here |
|---|---|---|
| Local computer, local store | `INetFwPolicy2` today; `FWOpenPolicyStore(NULL, LOCAL)` | Everything except GPO-only settings |
| Local computer, policy from GPOs | Registry `HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall`, or `FWOpenPolicyStore(NULL, GP_RSOP)` | Nothing; display only |
| Local GPO | `OpenLocalMachineGPO` + `registry.pol` | Everything, including GPO-only settings |
| Domain GPO | `OpenDSGPO` + `registry.pol` | Everything, including GPO-only settings |
| Another computer, local store | `FWOpenPolicyStore(L"computer", LOCAL)` over MS-FASP | As the local store |
| Another computer, local GPO | `OpenRemoteMachineGPO` | As the local GPO |

## Technical Obstacles

1. **Singleton services hold one target.** Feature services are DI singletons; Task Scheduler keeps
   one mutable connection. A target has to become an explicit input (per call, or a scoped service
   per page), or connecting one page to another computer switches every page. This is the same work
   as [MultiInstance.md](MultiInstance.md) obstacle 1 and should be done once for both.
2. **Code that assumes "local" without an API parameter.** Some features read the local registry or
   file system directly (for example DCOM Config reads `HKLM` through `Registry.LocalMachine`). Remote
   targets need the remote form (`RegConnectRegistry`, UNC paths), so each feature's local-only
   shortcuts must be listed before its remote phase.
3. **Local-store-only interfaces.** `INetFwPolicy2` cannot pick a computer; firewall profiles move to
   `FWOpenPolicyStore` + `FWGetConfig`/`FWSetConfig` before the firewall can go remote.
4. **GPO-only settings are tri-state.** The model needs "not configured", offered only when the target
   is a GPO; the local-store UI keeps today's read-only display.
5. **Credentials and elevation.** Remote targets use the caller's identity, and local elevation does
   not grant rights on another computer. Domain GPO edits need GPO edit rights, not local
   administrator rights. Alternate credentials (MMC's "connect as") are out of scope for the first
   phases.
6. **Latency.** Remote calls are slower and can hang on unreachable hosts; every remote call runs off
   the UI thread with a cancellable timeout, and pages show a connecting state.

## Open Questions

- The `guidExtension` / `guidSnapin` pair wf.msc passes to `IGroupPolicyObject::Save` for firewall
  settings. The registry extension GUID (`35378EAC-683F-11D2-A89A-00C04FBBCFA2`) is known; whether the
  firewall snap-in also registers its own GUID has to be confirmed (for example by diffing
  `gPCMachineExtensionNames` after an edit in the GPMC editor).
- The exact `FWGetConfig`/`FWSetConfig` export signatures and which `FW_PROFILE_CONFIG` values
  correspond to the two merge settings.
- Whether `FW_STORE_TYPE_GP_RSOP` returns the same values as the registry read used today.
- For each remote row of the coverage table: the minimum rights and services on the target, verified
  against a workgroup and a domain machine, before that snap-in's phase ships.

## Roadmap

1. **Phase 0 — done.** Local only. GPO-only firewall settings shown read-only with the value Group
   Policy applies, a "managed by Group Policy" note, and an info tooltip.
2. **Phase 1 — shared infrastructure.** Target model, Select Computer dialog (with the object
   picker), saved targets, error mapping, and moving target state off singletons. Task Scheduler
   adopts it first, since it already connects remotely. Exit: Task Scheduler connected to another
   computer in one page does not affect any other page.
3. **Phase 2 — server-name APIs.** Services, Event Viewer, Shared Folders, Local Users and Groups, and
   Component Services (COM+ catalog first, then DCOM Config over the remote registry). Exit: each
   matches its MMC snap-in against the same remote computer.
4. **Phase 3 — path- and WMI-based snap-ins.** Performance Monitor, Certificates, Print Management
   (multiple servers), Disk Management, RSoP.
5. **Phase 4 — Group Policy targets.** Local GPO, remote local GPO and domain GPO for gpedit and the
   firewall's GPO-only settings, through `IGroupPolicyObject` (implementing `OpenDSGPO`). Exit:
   changing rule merging in OneMMC matches gpedit.msc / the GPMC editor, and clients apply it after a
   policy refresh.
6. **Phase 5 — remote firewall store.** Firewall profiles and rules on another computer through
   `FWOpenPolicyStore(machine, ...)`, after profiles have moved off `INetFwPolicy2`.

## Guidance for New Code

- Do not call PowerShell or cmdlets, in any form, to implement a feature or a remote target.
- Take the target as a parameter and pass it to the API's own server-name, connect or path argument;
  do not add per-target state to DI singletons.
- Do not read `HKLM` or local paths directly in code that a remote target will need; go through a
  seam that can take a computer.
- Do not enable a control for a GPO-only setting on a local-store page; show the value Group Policy
  applies, read-only, as wf.msc does.
- Do not drop a saved target or store when it fails to open; distinguish "gone" from "not reachable
  right now".

## References

- [ICOMAdminCatalog::Connect](https://learn.microsoft.com/en-us/windows/win32/api/comadmin/nf-comadmin-icomadmincatalog-connect)
- [OpenSCManagerW](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-openscmanagerw)
- [EvtOpenSession](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtopensession)
- [ITaskService::Connect](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nf-taskschd-itaskservice-connect)
- [NetUserEnum](https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netuserenum),
  [NetShareEnum](https://learn.microsoft.com/en-us/windows/win32/api/lmshare/nf-lmshare-netshareenum)
- [PdhConnectMachine](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhconnectmachinew)
- [System Store Locations](https://learn.microsoft.com/en-us/windows/win32/seccrypto/system-store-locations)
  — remote certificate stores.
- [EnumPrinters](https://learn.microsoft.com/en-us/windows/win32/printdocs/enumprinters)
- [IGroupPolicyObject](https://learn.microsoft.com/en-us/windows/win32/api/gpedit/nn-gpedit-igrouppolicyobject)
- [IDsObjectPicker](https://learn.microsoft.com/en-us/windows/win32/api/objsel/nn-objsel-idsobjectpicker)
- [CM_Connect_MachineW](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_connect_machinew)
  and [KB 2781106](https://support.microsoft.com/en-us/kb/2781106) — remote Device Manager removed.
- [MS-GPFAS]: [Group Policy: Firewall and Advanced Security Data Structure](https://learn.microsoft.com/en-us/openspecs/windows_protocols/MS-GPFAS/)
  — [1.3.2](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpfas/11efd46c-2b93-4cb5-9a5d-7da2f35da86d),
  [2.2.3.13](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpfas/2c979624-900a-4b6e-b4ef-09b387cd62ab).
- [MS-GPREG]: [Registry Policy Message Syntax](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpreg/5c092c22-bf6b-4e7f-b180-b20743d368f5).
- [MS-FASP]: [2.2.1 FW_STORE_TYPE](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fasp/37ebed95-8abf-472c-8b4b-7a510a2a6baa),
  [3.1.1 Abstract Data Model](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fasp/43507d53-8955-416d-b913-dfb27dc76b17),
  [3.1.4.1 RRPC_FWOpenPolicyStore](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fasp/2157e39a-1bf0-4e0c-abae-0811fd918b11).
- [Windows Firewall rules](https://learn.microsoft.com/windows/security/operating-system-security/network-security/windows-firewall/rules)
  — rule merging and local Group Policy rules.
- [MultiInstance.md](MultiInstance.md) — singleton connection targets.
