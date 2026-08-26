using System;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.PrintManagement.Services.Native;

/// <summary>
/// Native structures for printer management P/Invoke calls.
/// </summary>
internal static class PrinterNativeStructures
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_INFO_2
    {
        public IntPtr pServerName;
        public IntPtr pPrinterName;
        public IntPtr pShareName;
        public IntPtr pPortName;
        public IntPtr pDriverName;
        public IntPtr pComment;
        public IntPtr pLocation;
        public IntPtr pDevMode;
        public IntPtr pSepFile;
        public IntPtr pPrintProcessor;
        public IntPtr pDatatype;
        public IntPtr pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_INFO_8
    {
        public IntPtr pDevMode;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_DEFAULTS
    {
        public IntPtr pDatatype;
        public IntPtr pDevMode;
        public uint DesiredAccess;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_INFO_4
    {
        public IntPtr pPrinterName;
        public IntPtr pServerName;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DRIVER_INFO_6
    {
        public uint cVersion;
        public IntPtr pName;
        public IntPtr pEnvironment;
        public IntPtr pDriverPath;
        public IntPtr pDataFile;
        public IntPtr pConfigFile;
        public IntPtr pHelpFile;
        public IntPtr pDependentFiles;
        public IntPtr pMonitorName;
        public IntPtr pDefaultDataType;
        public IntPtr pszzPreviousNames;
        public FILETIME ftDriverDate;
        public ulong dwlDriverVersion;
        public IntPtr pszMfgName;
        public IntPtr pszOEMUrl;
        public IntPtr pszHardwareID;
        public IntPtr pszProvider;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DRIVER_INFO_8
    {
        public uint cVersion;
        public IntPtr pName;
        public IntPtr pEnvironment;
        public IntPtr pDriverPath;
        public IntPtr pDataFile;
        public IntPtr pConfigFile;
        public IntPtr pHelpFile;
        public IntPtr pDependentFiles;
        public IntPtr pMonitorName;
        public IntPtr pDefaultDataType;
        public IntPtr pszzPreviousNames;
        public FILETIME ftDriverDate;
        public ulong dwlDriverVersion;
        public IntPtr pszMfgName;
        public IntPtr pszOEMUrl;
        public IntPtr pszHardwareID;
        public IntPtr pszProvider;
        public IntPtr pszPrintProcessor;
        public IntPtr pszVendorSetup;
        public IntPtr pszzColorProfiles;
        public IntPtr pszInfPath;
        public uint dwPrinterDriverAttributes;
        public IntPtr pszzCoreDriverDependencies;
        public FILETIME ftMinInboxDriverVerDate;
        public ulong dwlMinInboxDriverVerVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PORT_INFO_2
    {
        public IntPtr pPortName;
        public IntPtr pMonitorName;
        public IntPtr pDescription;
        public uint fPortType;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FORM_INFO_1
    {
        public uint Flags;
        public IntPtr pName;
        public SIZEL Size;
        public RECTL ImageableArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZEL
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECTL
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}


