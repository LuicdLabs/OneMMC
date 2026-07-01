using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WindowsFirewall;
using Windows.Win32.Security;

namespace OneMMC.Core.Features.SystemManagement.Interop.WF;

internal static class AppContainerNativeMethods
{
    internal static unsafe uint NetworkIsolationEnumAppContainers(
        uint flags,
        out uint numPublicAppContainers,
        out IntPtr appContainers)
    {
        fixed (uint* count = &numPublicAppContainers)
        {
            INET_FIREWALL_APP_CONTAINER* nativeAppContainers = null;
            uint error = PInvoke.NetworkIsolationEnumAppContainers(
                flags,
                count,
                &nativeAppContainers);

            appContainers = (IntPtr)nativeAppContainers;
            return error;
        }
    }

    internal static unsafe void NetworkIsolationFreeAppContainers(IntPtr appContainers)
        => PInvoke.NetworkIsolationFreeAppContainers((INET_FIREWALL_APP_CONTAINER*)appContainers);

    internal static unsafe bool ConvertSidToStringSid(
        IntPtr sid,
        out IntPtr stringSid)
    {
        PWSTR sidString = default;
        bool success = PInvoke.ConvertSidToStringSid(new PSID(sid.ToPointer()), &sidString);
        stringSid = (IntPtr)sidString.Value;
        return success;
    }

    internal static IntPtr LocalFree(IntPtr memory)
        => (IntPtr)PInvoke.LocalFree(new HLOCAL(memory));

    [StructLayout(LayoutKind.Sequential)]
    internal struct InetFirewallAcCapabilities
    {
        internal uint Count;
        internal IntPtr CapabilityArray;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InetFirewallAcBinaries
    {
        internal uint Count;
        internal IntPtr BinaryArray;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct InetFirewallAppContainer
    {
        internal IntPtr AppContainerSid;
        internal IntPtr UserSid;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? AppContainerName;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? DisplayName;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? Description;

        internal InetFirewallAcCapabilities Capabilities;
        internal InetFirewallAcBinaries Binaries;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? WorkingDirectory;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? PackageFullName;
    }
}
