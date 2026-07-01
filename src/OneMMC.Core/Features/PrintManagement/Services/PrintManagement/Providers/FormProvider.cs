using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.PrintManagement.Models.PrintManagement;
using OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Native;
using OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Providers;

/// <summary>
/// Provides print form information.
/// </summary>
internal class FormProvider
{
    private readonly ILogger _logger;

    public FormProvider(ILogger logger)
    {
        _logger = logger;
    }

    public List<PrintFormInfo> GetPrintForms()
    {
        if (!PrinterNativeMethods.OpenPrinter(null, out var hPrinter, IntPtr.Zero))
        {
            if (!PrinterNativeMethods.OpenPrinter(string.Empty, out hPrinter, IntPtr.Zero))
            {
                _logger.LogWarning("Unable to open local printer server for EnumForms. Error: {ErrorCode}", Marshal.GetLastWin32Error());
                return GetFormsViaRegistry();
            }
        }

        try
        {
            return GetFormsViaPInvoke(hPrinter);
        }
        finally
        {
            PrinterNativeMethods.ClosePrinter(hPrinter);
        }
    }

    private List<PrintFormInfo> GetFormsViaPInvoke(IntPtr hPrinter)
    {
        var forms = new List<PrintFormInfo>();

        PrinterNativeMethods.EnumForms(hPrinter, 1, IntPtr.Zero, 0, out var cbNeeded, out _);

        if (cbNeeded == 0 || cbNeeded > PrinterConstants.MAX_BUFFER_SIZE)
        {
            return GetFormsViaRegistry();
        }

        var pForms = Marshal.AllocHGlobal((int)cbNeeded);
        try
        {
            if (PrinterNativeMethods.EnumForms(hPrinter, 1, pForms, cbNeeded, out _, out var pcReturned))
            {
                var formSize = Marshal.SizeOf<PrinterNativeStructures.FORM_INFO_1>();
                for (uint i = 0; i < pcReturned; i++)
                {
                    var formPtr = IntPtr.Add(pForms, (int)(i * formSize));
                    var formInfo = Marshal.PtrToStructure<PrinterNativeStructures.FORM_INFO_1>(formPtr);

                    var formName = Marshal.PtrToStringUni(formInfo.pName) ?? string.Empty;

                    var printableWidth = (formInfo.ImageableArea.right - formInfo.ImageableArea.left) / 100;
                    var printableHeight = (formInfo.ImageableArea.bottom - formInfo.ImageableArea.top) / 100;

                    forms.Add(new PrintFormInfo
                    {
                        Name = formName,
                        FormType = PrinterStatusMapper.MapFormType(formInfo.Flags),
                        PrintableWidth = printableWidth,
                        PrintableHeight = printableHeight,
                    });
                }
            }
            else
            {
                return GetFormsViaRegistry();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pForms);
        }

        return forms.OrderBy(f => f.Name).ToList();
    }

    private List<PrintFormInfo> GetFormsViaRegistry()
    {
        var forms = new List<PrintFormInfo>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Forms");

            if (key is null)
            {
                return forms;
            }

            foreach (var formName in key.GetValueNames())
            {
                if (key.GetValue(formName) is byte[] data && data.Length >= 32)
                {
                    var flags = BitConverter.ToUInt32(data, 0);
                    var imgLeft = BitConverter.ToInt32(data, 12);
                    var imgTop = BitConverter.ToInt32(data, 16);
                    var imgRight = BitConverter.ToInt32(data, 20);
                    var imgBottom = BitConverter.ToInt32(data, 24);

                    forms.Add(new PrintFormInfo
                    {
                        Name = formName,
                        FormType = PrinterStatusMapper.MapFormType(flags),
                        PrintableWidth = (imgRight - imgLeft) / 100,
                        PrintableHeight = (imgBottom - imgTop) / 100,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate forms from registry.");
        }

        return forms.OrderBy(f => f.Name).ToList();
    }
}


