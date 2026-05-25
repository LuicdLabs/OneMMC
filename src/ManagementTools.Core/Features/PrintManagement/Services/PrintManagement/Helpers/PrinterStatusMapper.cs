using System.Collections.Generic;
using ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Native;

namespace ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Helpers;

/// <summary>
/// Maps printer status codes to human-readable strings.
/// </summary>
internal static class PrinterStatusMapper
{
    /// <summary>
    /// Maps the Win32_Printer PrinterStatus code to a human-readable status string.
    /// Handles bit flags for composite status.
    /// </summary>
    internal static string MapPrinterStatus(uint statusCode)
    {
        var statusFlags = new List<string>();

        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAUSED) != 0) statusFlags.Add("Paused");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_ERROR) != 0) statusFlags.Add("Error");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PENDING_DELETION) != 0) statusFlags.Add("Pending Deletion");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAPER_JAM) != 0) statusFlags.Add("Paper Jam");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAPER_OUT) != 0) statusFlags.Add("Paper Out");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_MANUAL_FEED) != 0) statusFlags.Add("Manual Feed");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAPER_PROBLEM) != 0) statusFlags.Add("Paper Problem");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_OFFLINE) != 0) statusFlags.Add("Offline");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_IO_ACTIVE) != 0) statusFlags.Add("IO Active");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_BUSY) != 0) statusFlags.Add("Busy");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PRINTING) != 0) statusFlags.Add("Printing");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_OUTPUT_BIN_FULL) != 0) statusFlags.Add("Output Bin Full");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_NOT_AVAILABLE) != 0) statusFlags.Add("Not Available");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_WAITING) != 0) statusFlags.Add("Waiting");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PROCESSING) != 0) statusFlags.Add("Processing");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_INITIALIZING) != 0) statusFlags.Add("Initializing");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_WARMING_UP) != 0) statusFlags.Add("Warming Up");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_TONER_LOW) != 0) statusFlags.Add("Toner Low");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_NO_TONER) != 0) statusFlags.Add("No Toner");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_USER_INTERVENTION) != 0) statusFlags.Add("User Intervention Required");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_OUT_OF_MEMORY) != 0) statusFlags.Add("Out of Memory");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_DOOR_OPEN) != 0) statusFlags.Add("Door Open");
        if ((statusCode & PrinterConstants.PRINTER_STATUS_POWER_SAVE) != 0) statusFlags.Add("Power Save");

        return statusFlags.Count > 0 ? string.Join(", ", statusFlags) : "Ready";
    }

    /// <summary>
    /// Maps form flags to a user-readable form type string.
    /// </summary>
    internal static string MapFormType(uint flags) => flags switch
    {
        PrinterConstants.FORM_BUILTIN => "Built-in",
        PrinterConstants.FORM_PRINTER => "Printer",
        PrinterConstants.FORM_USER => "User-defined",
        _ => "Unknown",
    };
}


