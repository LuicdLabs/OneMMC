using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Graphics.Printing;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Native;

/// <summary>
/// Native printer interop wrappers backed by CsWin32-generated bindings.
/// </summary>
internal static class PrinterNativeMethods
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate void PrintUiEntryWDelegate(IntPtr hwnd, IntPtr hinst, string lpszCmdLine, int nCmdShow);

    private static readonly Lazy<PrintUiEntryWDelegate> s_printUiEntry = new(LoadPrintUiEntryW);

    internal static unsafe bool OpenPrinter(string? pPrinterName, out IntPtr phPrinter, IntPtr pDefault)
    {
        fixed (char* printerName = pPrinterName)
        {
            PRINTER_HANDLE printerHandle;
            bool result = Win32PInvoke.OpenPrinter(
                printerName,
                &printerHandle,
                pDefault == IntPtr.Zero ? null : (PRINTER_DEFAULTSW*)pDefault);
            phPrinter = (IntPtr)printerHandle;
            return result;
        }
    }

    internal static unsafe bool OpenPrinter(string? pPrinterName, out IntPtr phPrinter, ref PrinterNativeStructures.PRINTER_DEFAULTS pDefault)
    {
        fixed (char* printerName = pPrinterName)
        {
            PRINTER_HANDLE printerHandle;
            ref PRINTER_DEFAULTSW nativeDefaults = ref Unsafe.As<PrinterNativeStructures.PRINTER_DEFAULTS, PRINTER_DEFAULTSW>(ref pDefault);
            fixed (PRINTER_DEFAULTSW* defaultsPtr = &nativeDefaults)
            {
                bool result = Win32PInvoke.OpenPrinter(printerName, &printerHandle, defaultsPtr);
                phPrinter = (IntPtr)printerHandle;
                return result;
            }
        }
    }

    internal static bool ClosePrinter(IntPtr hPrinter)
        => Win32PInvoke.ClosePrinter((PRINTER_HANDLE)hPrinter);

    internal static unsafe bool GetPrinter(IntPtr hPrinter, uint level, IntPtr pPrinter, uint cbBuf, out uint pcbNeeded)
    {
        fixed (uint* pNeeded = &pcbNeeded)
        {
            return Win32PInvoke.GetPrinter((PRINTER_HANDLE)hPrinter, level, (byte*)pPrinter, cbBuf, pNeeded);
        }
    }

    internal static unsafe bool SetPrinter(IntPtr hPrinter, uint level, IntPtr pPrinter, uint command)
        => Win32PInvoke.SetPrinter((PRINTER_HANDLE)hPrinter, level, (byte*)pPrinter, command);

    internal static bool DeletePrinter(IntPtr hPrinter)
        => Win32PInvoke.DeletePrinter((PRINTER_HANDLE)hPrinter);

    internal static bool DeletePrinterDriverEx(
        string? pName,
        string? pEnvironment,
        string pDriverName,
        uint dwDeleteFlag,
        uint dwVersionFlag)
        => Win32PInvoke.DeletePrinterDriverEx(pName, pEnvironment, pDriverName, dwDeleteFlag, dwVersionFlag);

    internal static int DeletePrinterDriverPackage(string? pszServer, string pszInfPath, string? pszEnvironment)
        => (int)Win32PInvoke.DeletePrinterDriverPackage(pszServer, pszInfPath, pszEnvironment);

    internal static unsafe uint SetPrinterDataEx(
        IntPtr hPrinter,
        string? pKeyName,
        string pValueName,
        uint type,
        byte[] pData,
        uint cbData)
    {
        fixed (byte* data = pData)
        fixed (char* keyNamePtr = pKeyName)
        fixed (char* valueNamePtr = pValueName)
        {
            return Win32PInvoke.SetPrinterDataEx((PRINTER_HANDLE)hPrinter, keyNamePtr, valueNamePtr, type, data, cbData);
        }
    }

    internal static bool AddPrinterConnection(string pName)
        => Win32PInvoke.AddPrinterConnection(pName);

    internal static unsafe bool DeletePrinterConnection(string pName)
    {
        fixed (char* name = pName)
        {
            return Win32PInvoke.DeletePrinterConnection(name);
        }
    }

    internal static unsafe bool EnumForms(
        IntPtr hPrinter,
        uint level,
        IntPtr pForm,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned)
    {
        fixed (uint* pNeeded = &pcbNeeded)
        fixed (uint* pReturned = &pcReturned)
        {
            return Win32PInvoke.EnumForms((PRINTER_HANDLE)hPrinter, level, (byte*)pForm, cbBuf, pNeeded, pReturned);
        }
    }

    internal static unsafe bool EnumPorts(
        string? pName,
        uint level,
        IntPtr lpbPorts,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned)
    {
        fixed (char* name = pName)
        fixed (uint* pNeeded = &pcbNeeded)
        fixed (uint* pReturned = &pcReturned)
        {
            return Win32PInvoke.EnumPorts(name, level, (byte*)lpbPorts, cbBuf, pNeeded, pReturned);
        }
    }

    internal static unsafe bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr pPrinterEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned)
    {
        fixed (char* printerName = name)
        fixed (uint* pNeeded = &pcbNeeded)
        fixed (uint* pReturned = &pcReturned)
        {
            return Win32PInvoke.EnumPrinters(flags, printerName, level, (byte*)pPrinterEnum, cbBuf, pNeeded, pReturned);
        }
    }

    internal static unsafe bool EnumPrinterDrivers(
        string? pName,
        string? pEnvironment,
        uint level,
        IntPtr pDriverInfo,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned)
    {
        fixed (char* environment = pEnvironment)
        fixed (char* name = pName)
        fixed (uint* pNeeded = &pcbNeeded)
        fixed (uint* pReturned = &pcReturned)
        {
            return Win32PInvoke.EnumPrinterDrivers(name, environment, level, (byte*)pDriverInfo, cbBuf, pNeeded, pReturned);
        }
    }

    internal static unsafe bool GetPrinterDriver(
        IntPtr hPrinter,
        string? pEnvironment,
        uint level,
        IntPtr pDriverInfo,
        uint cbBuf,
        out uint pcbNeeded)
    {
        fixed (char* environment = pEnvironment)
        fixed (uint* pNeeded = &pcbNeeded)
        {
            return Win32PInvoke.GetPrinterDriver((PRINTER_HANDLE)hPrinter, environment, level, (byte*)pDriverInfo, cbBuf, pNeeded);
        }
    }

    internal static unsafe int DocumentProperties(
        IntPtr hWnd,
        IntPtr hPrinter,
        string pDeviceName,
        IntPtr pDevModeOutput,
        IntPtr pDevModeInput,
        uint fMode)
    {
        fixed (char* deviceName = pDeviceName)
        {
            return Win32PInvoke.DocumentProperties(
                new HWND(hWnd),
                (PRINTER_HANDLE)hPrinter,
                deviceName,
                (DEVMODEW*)pDevModeOutput,
                (DEVMODEW*)pDevModeInput,
                fMode);
        }
    }

    internal static bool PrinterProperties(IntPtr hWnd, IntPtr hPrinter)
        => Win32PInvoke.PrinterProperties(new HWND(hWnd), (PRINTER_HANDLE)hPrinter);

    internal static void PrintUIEntryW(IntPtr hwnd, IntPtr hinst, string lpszCmdLine, int nCmdShow)
        => s_printUiEntry.Value(hwnd, hinst, lpszCmdLine, nCmdShow);

    private static PrintUiEntryWDelegate LoadPrintUiEntryW()
    {
        // CsWin32 does not project PrintUIEntryW from current metadata, so we bind it dynamically.
        IntPtr libraryHandle = NativeLibrary.Load("printui.dll");
        IntPtr export = NativeLibrary.GetExport(libraryHandle, "PrintUIEntryW");
        return Marshal.GetDelegateForFunctionPointer<PrintUiEntryWDelegate>(export);
    }
}
