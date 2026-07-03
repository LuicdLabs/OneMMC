using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Native
{
    #region Group Policy Interop (COM)

    /// <summary>
    /// Flags for IGroupPolicyObject.OpenLocalMachineGPO and similar methods.
    /// </summary>
    [Flags]
    public enum GpoOpenFlags : uint
    {
        /// <summary>Load the GPO in read-only mode.</summary>
        LoadRegistry = 0x00000001,
        /// <summary>Load the GPO for editing (read-write).</summary>
        Editing = 0x00000002
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
    /// GPO link information.
    /// </summary>
    public enum GpoLink : uint
    {
        /// <summary>No link information available.</summary>
        Unknown = 0,
        /// <summary>Linked to a machine.</summary>
        Machine = 1,
        /// <summary>Linked to a site.</summary>
        Site = 2,
        /// <summary>Linked to a domain.</summary>
        Domain = 3,
        /// <summary>Linked to an organizational unit.</summary>
        OrganizationalUnit = 4
    }

    /// <summary>
    /// Options for Save method.
    /// </summary>
    [Flags]
    public enum GpoSaveFlags : uint
    {
        /// <summary>Save all sections.</summary>
        None = 0,
        /// <summary>Save the machine section only.</summary>
        Machine = 0x00000001,
        /// <summary>Save the user section only.</summary>
        User = 0x00000002
    }

    /// <summary>
    /// COM interface for Group Policy Object manipulation.
    /// This is the official Windows interface for programmatically editing local and remote GPOs.
    /// Using this interface ensures that changes are visible in gpedit.msc and rsop.msc.
    /// </summary>
    // Source-generated ([GeneratedComInterface]) port of IGroupPolicyObject for Native AOT
    // (doc/NativeAotMigration.md, M3). This is a pure IUnknown-derived (non-dual) interface, so it
    // needs no IDispatch base (unlike the Task Scheduler dual interfaces). Member order is the vtable
    // order and is preserved exactly. Windows-fill-buffer parameters that were StringBuilder are
    // typed as nint (a caller-pinned wide-char buffer) because the interop source generator does not
    // marshal StringBuilder; the LPStruct Guids become `in Guid` (a pointer to the Guid), which is the
    // ABI-equivalent blittable form. Members OneMMC never calls keep their vtable slots with opaque
    // signatures.
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
    /// GUID for the Registry-based Client Side Extension.
    /// This extension processes the Registry.pol file.
    /// </summary>
    public static class GroupPolicyGuids
    {
        /// <summary>
        /// GUID for Registry-based policy processing (Administrative Templates).
        /// {35378EAC-683F-11D2-A89A-00C04FBBCFA2}
        /// </summary>
        public static readonly Guid RegistryExtensionGuid = new Guid("35378EAC-683F-11D2-A89A-00C04FBBCFA2");

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
    }

    /// <summary>CLSID of the <c>GroupPolicyObject</c> coclass, activated via
    /// <see cref="OneMMC.Core.Infrastructure.Interop.ComActivator"/> (AOT-safe) rather than a
    /// reflection-activated coclass.</summary>
    public static class GroupPolicyObjectClsid
    {
        /// <summary>{EA502722-A23D-11D1-A7D3-0000F87571E3}</summary>
        public static readonly Guid GroupPolicyObject = new("EA502722-A23D-11D1-A7D3-0000F87571E3");
    }

    #endregion

    #region PInvoke

    public class PInvoke
    {
        // Window Messages
        public const int WM_SETTINGCHANGE = 0x1A;
        public const int HWND_BROADCAST = 0xFFFF;
        
        // Registry Policy Refresh Options
        public const uint RP_FORCE = 1;  // Force refresh even if no changes detected
        
        public static bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow)
            => Win32PInvoke.ShowScrollBar(new HWND(hWnd), (SCROLLBAR_CONSTANTS)wBar, bShow);

        public static bool RefreshPolicyEx(bool bMachine, uint dwOptions)
            => Win32PInvoke.RefreshPolicyEx(bMachine, dwOptions);

        public static unsafe int RegLoadKeyW(IntPtr hKey, string lpSubKey, string lpFile)
        {
            fixed (char* subKeyPtr = lpSubKey)
            fixed (char* filePtr = lpFile)
            {
                return (int)Win32PInvoke.RegLoadKey((HKEY)hKey, subKeyPtr, filePtr);
            }
        }

        public static unsafe int RegUnLoadKeyW(IntPtr hKey, string lpSubKey)
        {
            fixed (char* subKeyPtr = lpSubKey)
            {
                return (int)Win32PInvoke.RegUnLoadKey((HKEY)hKey, subKeyPtr);
            }
        }

        public static int RegFlushKey(IntPtr hKey)
            => (int)Win32PInvoke.RegFlushKey((HKEY)hKey);

        public static bool GetProductInfo(int dwOSMajorVersion, int dwOSMinorVersion, int dwSpMajorVersion, int dwSpMinorVersion, out int pdwReturnedProductType)
        {
            bool result = Win32PInvoke.GetProductInfo(
                (uint)dwOSMajorVersion,
                (uint)dwOSMinorVersion,
                (uint)dwSpMajorVersion,
                (uint)dwSpMinorVersion,
                out var productType);
            pdwReturnedProductType = (int)productType;
            return result;
        }

        public static unsafe void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2)
            => Win32PInvoke.SHChangeNotify((SHCNE_ID)wEventId, (SHCNF_FLAGS)uFlags, dwItem1.ToPointer(), dwItem2.ToPointer());
        
        // SHChangeNotify
        public const int SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNF_IDLIST = 0x0000;
        
        /// <summary>
        /// Broadcast a WM_SETTINGCHANGE message to all top-level windows.
        /// </summary>
        public static void BroadcastSettingChange()
        {
            Win32PInvoke.SendNotifyMessage(
                new HWND(new IntPtr(HWND_BROADCAST)),
                WM_SETTINGCHANGE,
                default,
                default);
        }
        
        /// <summary>
        /// Force a Group Policy refresh for both machine and user policy.
        /// </summary>
        public static void ForceRefreshGroupPolicy()
        {
            // Refresh machine policy
            RefreshPolicyEx(true, RP_FORCE);
            // Refresh user policy
            RefreshPolicyEx(false, RP_FORCE);
        }
    }

    #endregion
}


