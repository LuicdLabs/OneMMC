using System;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Utilities
{
    public class Privilege
    {
        public static void EnablePrivilege(string name)
        {
            using var processHandle = Win32PInvoke.GetCurrentProcess_SafeHandle();
            if (!Win32PInvoke.OpenProcessToken(
                processHandle,
                TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY,
                out var tokenHandle))
            {
                return;
            }

            using (tokenHandle)
            {
                if (!Win32PInvoke.LookupPrivilegeValue(string.Empty, name, out LUID luid))
                {
                    return;
                }

                unsafe
                {
                    TOKEN_PRIVILEGES privileges = default;
                    privileges.PrivilegeCount = 1;
                    privileges.Privileges[0] = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED
                    };

                    Win32PInvoke.AdjustTokenPrivileges(tokenHandle, false, &privileges, Span<byte>.Empty);
                }
            }
        }
    }
}
