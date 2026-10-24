using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OneMMC.Core.Infrastructure.PolicyStorage;

/// <summary>
/// Flags for IGroupPolicyObject.OpenLocalMachineGPO and similar methods (gpedit.h <c>GPO_OPEN_*</c>).
/// </summary>
[Flags]
public enum GpoOpenFlags : uint
{
    /// <summary>GPO_OPEN_LOAD_REGISTRY: load the registry files so GetRegistryKey works.</summary>
    LoadRegistry = 0x00000001,
    /// <summary>GPO_OPEN_READ_ONLY: open the GPO without write access.</summary>
    ReadOnly = 0x00000002
}

/// <summary>
/// Section types for GetRegistryKey method.
/// </summary>
public enum GpoSection : uint
{
    /// <summary>Open the root section.</summary>
    Root = 0,
    /// <summary>Open the user configuration section.</summary>
    User = 1,
    /// <summary>Open the machine (computer) configuration section.</summary>
    Machine = 2
}

/// <summary>
/// COM interface for Group Policy Object manipulation.
/// This is the official Windows interface for programmatically editing local and remote GPOs.
/// Using this interface ensures that changes are visible in gpedit.msc and rsop.msc.
/// </summary>
// Source-generated ([GeneratedComInterface]) port of IGroupPolicyObject following the
// repository's Native AOT COM guidance; see doc/NativeAot.md ("COM interop"). This is a pure
// IUnknown-derived (non-dual) interface, so it needs no IDispatch base (unlike the Task
// Scheduler dual interfaces). Member order is the vtable order and is preserved exactly.
// Windows-fill-buffer parameters that were StringBuilder are typed as nint (a caller-pinned
// wide-char buffer) because the interop source generator does not marshal StringBuilder; the
// LPStruct Guids become `in Guid` (a pointer to the Guid), which is the ABI-equivalent
// blittable form. Members OneMMC never calls keep their vtable slots with opaque signatures.
// Lives in Infrastructure because both the Group Policy editor and the Security Settings
// (Software Restriction Policies) features edit the local GPO through it.
[GeneratedComInterface]
[Guid("EA502723-A23D-11d1-A7D3-0000F87571E3")]
public partial interface IGroupPolicyObject
{
    /// <summary>Creates a new GPO in Active Directory. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int New(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDomainName,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        uint dwFlags);

    /// <summary>Opens a domain-based GPO. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int OpenDSGPO(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        uint dwFlags);

    /// <summary>Opens the local machine's GPO for the specified machine.</summary>
    [PreserveSig]
    int OpenLocalMachineGPO(uint dwFlags);

    /// <summary>Opens the local GPO for a remote machine.</summary>
    [PreserveSig]
    int OpenRemoteMachineGPO(
        [MarshalAs(UnmanagedType.LPWStr)] string pszComputerName,
        uint dwFlags);

    /// <summary>Saves the GPO. Changes to the registry portion are saved.</summary>
    [PreserveSig]
    int Save(
        [MarshalAs(UnmanagedType.Bool)] bool bMachine,
        [MarshalAs(UnmanagedType.Bool)] bool bAdd,
        in Guid pGuidExtension,
        in Guid pGuid);

    /// <summary>Deletes the GPO. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int Delete();

    /// <summary>Gets the name (GUID) of the GPO into a caller-allocated buffer. (Unused; placeholder.)</summary>
    [PreserveSig]
    int GetName(nint pszName, int cchMaxLength);

    /// <summary>Gets the display name of the GPO into a caller-allocated wide-char buffer.</summary>
    [PreserveSig]
    int GetDisplayName(nint pszName, int cchMaxLength);

    /// <summary>Sets the display name of the GPO. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    /// <summary>Gets the path to the GPO into a caller-allocated buffer. (Unused; placeholder.)</summary>
    [PreserveSig]
    int GetPath(nint pszPath, int cchMaxPath);

    /// <summary>Gets the Active Directory path of the GPO. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int GetDSPath(uint dwSection, nint pszPath, int cchMaxPath);

    /// <summary>Gets the file system path of the GPO into a caller-allocated wide-char buffer.</summary>
    [PreserveSig]
    int GetFileSysPath(uint dwSection, nint pszPath, int cchMaxPath);

    /// <summary>
    /// Gets a handle to the root of the registry data.
    /// This is the key method for reading/writing policy values.
    /// </summary>
    [PreserveSig]
    int GetRegistryKey(
        uint dwSection,
        out nint hKey);

    /// <summary>Gets options for the GPO. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int GetOptions(out uint dwOptions);

    /// <summary>Sets options for the GPO. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int SetOptions(uint dwOptions, uint dwMask);

    /// <summary>Queries the GPO type. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int GetGpoType(out uint gpoType);

    /// <summary>Gets the machine name associated with this GPO. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int GetMachineName(nint pszName, int cchMaxLength);

    /// <summary>Gets property sheet page extensions. (Unused; vtable placeholder.)</summary>
    [PreserveSig]
    int GetPropertySheetPages(out nint hPages, out uint uPageCount);
}

/// <summary>
/// Client-side extension and snap-in GUIDs passed to <see cref="IGroupPolicyObject.Save"/>. The
/// (extension, snap-in) pair is what ends up in gpt.ini's extension-names line.
/// </summary>
public static class GroupPolicyGuids
{
    /// <summary>
    /// GUID for Registry-based policy processing (Administrative Templates).
    /// {35378EAC-683F-11D2-A89A-00C04FBBCFA2}
    /// </summary>
    public static readonly Guid RegistryExtensionGuid = new Guid("35378EAC-683F-11D2-A89A-00C04FBBCFA2");

    /// <summary>
    /// EFS recovery client-side extension, which also processes the group-policy certificate stores.
    /// {B1BE8D72-6EAC-11D2-A4EA-00C04F79F83A}
    /// </summary>
    public static readonly Guid EfsRecoveryExtensionGuid = new Guid("B1BE8D72-6EAC-11D2-A4EA-00C04F79F83A");

    /// <summary>
    /// Client-side extension GUID for machine policy.
    /// {D02B1F72-3407-48AE-BA88-E8213C6761F1}
    /// </summary>
    public static readonly Guid MachinePreferenceGuid = new Guid("D02B1F72-3407-48AE-BA88-E8213C6761F1");

    /// <summary>
    /// Client-side extension GUID for user policy.
    /// {D02B1F73-3407-48AE-BA88-E8213C6761F1}
    /// </summary>
    public static readonly Guid UserPreferenceGuid = new Guid("D02B1F73-3407-48AE-BA88-E8213C6761F1");

    /// <summary>
    /// MMC snap-in GUID for Group Policy Editor.
    /// {8FC0B734-A0E1-11D1-A7D3-0000F87571E3}
    /// </summary>
    public static readonly Guid MmcSnapInGuid = new Guid("8FC0B734-A0E1-11D1-A7D3-0000F87571E3");

    /// <summary>
    /// Certificates snap-in (certmgr.dll) GUID, which hosts the Public Key Policies and Software
    /// Restriction Policies extensions and is the tool GUID secpol.msc passes when saving them.
    /// {53D6AB1D-2488-11D1-A28C-00C04FB94F17}
    /// </summary>
    public static readonly Guid CertificatesSnapInGuid = new Guid("53D6AB1D-2488-11D1-A28C-00C04FB94F17");

    /// <summary>
    /// Network List Manager Policies snap-in extension (nlmgp.dll) GUID, the tool GUID secpol.msc
    /// passes together with the Registry extension when saving network list policies.
    /// {2D4156A2-897A-11DB-BA21-001185AD2B89}
    /// </summary>
    public static readonly Guid NetworkListManagerSnapInGuid = new Guid("2D4156A2-897A-11DB-BA21-001185AD2B89");
}

/// <summary>CLSID of the <c>GroupPolicyObject</c> coclass, activated via
/// <see cref="OneMMC.Core.Infrastructure.Interop.ComActivator"/> (AOT-safe) rather than a
/// reflection-activated coclass.</summary>
public static class GroupPolicyObjectClsid
{
    /// <summary>{EA502722-A23D-11D1-A7D3-0000F87571E3}</summary>
    public static readonly Guid GroupPolicyObject = new("EA502722-A23D-11D1-A7D3-0000F87571E3");
}
