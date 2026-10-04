using System.Collections.Generic;
using OneMMC.Core.Features.PrintManagement.Services.Native;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PrintManagement.Services.Helpers;

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

        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAUSED) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusPaused));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_ERROR) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusError));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PENDING_DELETION) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusPendingDeletion));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAPER_JAM) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusPaperJam));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAPER_OUT) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusPaperOut));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_MANUAL_FEED) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusManualFeed));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PAPER_PROBLEM) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusPaperProblem));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_OFFLINE) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusOffline));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_IO_ACTIVE) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusIoActive));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_BUSY) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusBusy));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PRINTING) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusPrinting));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_OUTPUT_BIN_FULL) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusOutputBinFull));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_NOT_AVAILABLE) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusNotAvailable));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_WAITING) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusWaiting));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_PROCESSING) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusProcessing));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_INITIALIZING) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusInitializing));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_WARMING_UP) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusWarmingUp));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_TONER_LOW) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusTonerLow));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_NO_TONER) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusNoToner));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_USER_INTERVENTION) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusUserIntervention));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_OUT_OF_MEMORY) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusOutOfMemory));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_DOOR_OPEN) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusDoorOpen));
        if ((statusCode & PrinterConstants.PRINTER_STATUS_POWER_SAVE) != 0) statusFlags.Add(L(PrintMgmtKeys.PrinterStatusPowerSave));

        return statusFlags.Count > 0 ? string.Join(", ", statusFlags) : L(PrintMgmtKeys.PrinterStatusReady);
    }

    /// <summary>
    /// Maps form flags to a user-readable form type string.
    /// </summary>
    internal static string MapFormType(uint flags) => flags switch
    {
        PrinterConstants.FORM_BUILTIN => L(PrintMgmtKeys.FormTypeBuiltIn),
        PrinterConstants.FORM_PRINTER => L(PrintMgmtKeys.FormTypePrinter),
        PrinterConstants.FORM_USER => L(PrintMgmtKeys.FormTypeUserDefined),
        _ => L(PrintMgmtKeys.FormTypeUnknown),
    };

    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.PrintManagement, key);
}


