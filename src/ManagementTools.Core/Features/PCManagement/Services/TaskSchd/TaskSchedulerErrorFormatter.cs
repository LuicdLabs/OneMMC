using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.PCManagement.Services.TaskSchd;

/// <summary>
/// Turns the raw COM/HRESULT failures the native Task Scheduler service raises into the friendly,
/// localized sentence that taskschd.msc shows.
/// </summary>
/// <remarks>
/// When <c>ITaskFolder.RegisterTask</c> rejects a definition it surfaces a
/// <see cref="COMException"/> whose <see cref="Exception.Message"/> is the terse internal form the
/// service composes via <c>IErrorInfo</c> — for example <c>"(50,8):ShowMessage:"</c>. That text is
/// meaningless to a user. The HRESULT carried on the exception (e.g.
/// <c>SCHED_E_DEPRECATED_FEATURE_USED</c> = 0x80041330), however, resolves through the OS message
/// table to a human sentence ("The task definition uses a deprecated feature.") that Windows already
/// localizes to the system language. This helper extracts that HRESULT, looks up the OS message, and
/// wraps it in the standard "An error has occurred for task …" sentence.
/// </remarks>
public static class TaskSchedulerErrorFormatter
{
    /// <summary>
    /// Builds the user-facing message for a failed Task Scheduler operation.
    /// </summary>
    /// <param name="exception">The exception raised by the service.</param>
    /// <param name="taskName">The task the operation targeted, when known; included in the message.</param>
    /// <returns>A localized, friendly description of the failure.</returns>
    public static string Describe(Exception exception, string? taskName = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var detail = ResolveSystemMessage(ExtractHResult(exception)) ?? CleanMessage(exception);

        return string.IsNullOrWhiteSpace(taskName)
            ? string.Format(CultureInfo.CurrentCulture, L(TaskSchdKeys.ErrorOperationFailed), detail)
            : string.Format(CultureInfo.CurrentCulture, L(TaskSchdKeys.ErrorTaskFailedFormat), taskName, detail);
    }

    // The failure may be wrapped (e.g. by the STA executor), so unwrap to the first COMException whose
    // HResult carries the real Task Scheduler error code before falling back to the outer HResult.
    private static int ExtractHResult(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is COMException com)
            {
                return com.HResult;
            }
        }
        return exception.HResult;
    }

    private static string? ResolveSystemMessage(int hr)
    {
        // Only failure HRESULTs carry a meaningful message-table entry.
        if (hr >= 0)
        {
            return null;
        }

        string message;
        try
        {
            // Win32Exception.Message resolves the code through FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM);
            // the Task Scheduler SCHED_E_* codes are registered in the system message table.
            message = new Win32Exception(hr).Message;
        }
        catch (Exception)
        {
            return null;
        }

        // Win32Exception yields "Unknown error (0x........)" when the OS has no entry for the code; in
        // that case there is nothing friendlier than the original exception text to show.
        if (string.IsNullOrWhiteSpace(message) ||
            message.StartsWith("Unknown error", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return message.Trim();
    }

    private static string CleanMessage(Exception exception)
    {
        var message = exception.Message?.Trim();
        return string.IsNullOrEmpty(message) ? exception.GetType().Name : message;
    }

    private static string L(string key) =>
        LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);
}
