using System.Linq;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Native;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Utilities
{
    public static class SystemInfo
    {
        public static bool HasGroupPolicyInfrastructure()
        {
            int windowsEdition;
            // The first four arguments to GetProductInfo are OS version info, but 
            // the parameters are dwOSMajorVersion, dwOSMinorVersion, dwSpMajorVersion, dwSpMinorVersion.
            PInvoke.GetProductInfo(6, 0, 0, 0, out windowsEdition);
            
            // Exclude Home editions and other versions without gpedit.msc
            // Explain: https://docs.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getproductinfo
            var homeEditions = new int[] { 2, 3, 5, 11, 26, 42, 64, 98, 99, 100, 101 };
            return !homeEditions.Contains(windowsEdition);
        }
    }
}


