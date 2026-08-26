namespace OneMMC.Core.Features.PrintManagement.Services.Native;

/// <summary>
/// Constants for printer management from winspool.h
/// </summary>
internal static class PrinterConstants
{
    // Printer Enumeration Flags
    internal const uint PRINTER_ENUM_DEFAULT = 0x00000001;
    internal const uint PRINTER_ENUM_LOCAL = 0x00000002;
    internal const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;
    internal const uint PRINTER_ENUM_NAME = 0x00000008;
    internal const uint PRINTER_ENUM_REMOTE = 0x00000010;
    internal const uint PRINTER_ENUM_SHARED = 0x00000020;
    internal const uint PRINTER_ENUM_NETWORK = 0x00000040;
    internal const uint PRINTER_ENUM_EXPAND = 0x00004000;
    internal const uint PRINTER_ENUM_CONTAINER = 0x00008000;
    internal const uint PRINTER_ENUM_CATEGORY_3D = 0x00400000;

    // Printer Attributes
    internal const uint PRINTER_ATTRIBUTE_QUEUED = 0x00000001;
    internal const uint PRINTER_ATTRIBUTE_DIRECT = 0x00000002;
    internal const uint PRINTER_ATTRIBUTE_DEFAULT = 0x00000004;
    internal const uint PRINTER_ATTRIBUTE_SHARED = 0x00000008;
    internal const uint PRINTER_ATTRIBUTE_NETWORK = 0x00000010;
    internal const uint PRINTER_ATTRIBUTE_HIDDEN = 0x00000020;
    internal const uint PRINTER_ATTRIBUTE_LOCAL = 0x00000040;
    internal const uint PRINTER_ATTRIBUTE_ENABLE_BIDI = 0x00000200;
    internal const uint PRINTER_ATTRIBUTE_RAW_ONLY = 0x00001000;
    internal const uint PRINTER_ATTRIBUTE_PUBLISHED = 0x00002000;
    internal const uint PRINTER_ATTRIBUTE_FAX = 0x00004000;
    internal const uint PRINTER_ATTRIBUTE_TS = 0x00008000;
    internal const uint PRINTER_ATTRIBUTE_PUSHED_USER = 0x00020000;
    internal const uint PRINTER_ATTRIBUTE_PUSHED_MACHINE = 0x00040000;
    internal const uint PRINTER_ATTRIBUTE_PER_USER = 0x00400000;

    // Driver Isolation Flags
    internal const uint DRIVER_ISOLATION_NONE = 0;
    internal const uint DRIVER_ISOLATION_SHARED = 1;
    internal const uint DRIVER_ISOLATION_ISOLATED = 2;

    // Printer / Server Access Rights
    internal const uint SERVER_ACCESS_ADMINISTER = 0x00000001;
    internal const uint PRINTER_ACCESS_ADMINISTER = 0x00000004;
    internal const uint PRINTER_ACCESS_USE = 0x00000008;
    internal const uint PRINTER_ALL_ACCESS = 0x000F000C;

    // Printer Control Commands
    internal const uint PRINTER_CONTROL_PAUSE = 1;
    internal const uint PRINTER_CONTROL_RESUME = 2;

    // Delete printer driver flags
    internal const uint DPD_DELETE_UNUSED_FILES = 0x00000001;
    internal const uint DPD_DELETE_SPECIFIC_VERSION = 0x00000002;
    internal const uint DPD_DELETE_ALL_FILES = 0x00000004;

    // DocumentProperties flags / return values
    internal const uint DM_OUT_BUFFER = 0x00000002;
    internal const uint DM_PROMPT = 0x00000004;
    internal const uint DM_IN_BUFFER = 0x00000008;
    internal const int IDOK = 1;

    // Print driver isolation registry values
    internal const string PrintDriverIsolationGroupsValueName = "PrintDriverIsolationGroups";
    internal const char PrintDriverIsolationSeparator = '\\';

    // Driver Attributes
    internal const uint PRINTER_DRIVER_SANDBOX_ENABLED = 0x00000004;

    // Form Flags
    internal const uint FORM_USER = 0x00000000;
    internal const uint FORM_BUILTIN = 0x00000001;
    internal const uint FORM_PRINTER = 0x00000002;

    // Printer Status Flags
    internal const uint PRINTER_STATUS_PAUSED = 0x00000001;
    internal const uint PRINTER_STATUS_ERROR = 0x00000002;
    internal const uint PRINTER_STATUS_PENDING_DELETION = 0x00000004;
    internal const uint PRINTER_STATUS_PAPER_JAM = 0x00000008;
    internal const uint PRINTER_STATUS_PAPER_OUT = 0x00000010;
    internal const uint PRINTER_STATUS_MANUAL_FEED = 0x00000020;
    internal const uint PRINTER_STATUS_PAPER_PROBLEM = 0x00000040;
    internal const uint PRINTER_STATUS_OFFLINE = 0x00000080;
    internal const uint PRINTER_STATUS_IO_ACTIVE = 0x00000100;
    internal const uint PRINTER_STATUS_BUSY = 0x00000200;
    internal const uint PRINTER_STATUS_PRINTING = 0x00000400;
    internal const uint PRINTER_STATUS_OUTPUT_BIN_FULL = 0x00000800;
    internal const uint PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;
    internal const uint PRINTER_STATUS_WAITING = 0x00002000;
    internal const uint PRINTER_STATUS_PROCESSING = 0x00004000;
    internal const uint PRINTER_STATUS_INITIALIZING = 0x00008000;
    internal const uint PRINTER_STATUS_WARMING_UP = 0x00010000;
    internal const uint PRINTER_STATUS_TONER_LOW = 0x00020000;
    internal const uint PRINTER_STATUS_NO_TONER = 0x00040000;
    internal const uint PRINTER_STATUS_USER_INTERVENTION = 0x00100000;
    internal const uint PRINTER_STATUS_OUT_OF_MEMORY = 0x00200000;
    internal const uint PRINTER_STATUS_DOOR_OPEN = 0x00400000;
    internal const uint PRINTER_STATUS_POWER_SAVE = 0x01000000;

    // Maximum buffer size to prevent memory exhaustion
    internal const uint MAX_BUFFER_SIZE = 10 * 1024 * 1024; // 10 MB
}


