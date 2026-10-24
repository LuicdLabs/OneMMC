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


