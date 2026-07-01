using System;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.PCManagement.Services.TaskSchd.Native;

/// <summary>
/// Activation, flag constants and marshalling helpers for the Task Scheduler 2.0 COM surface
/// declared in <see cref="ITaskService"/> and friends. Centralizes the few low-level idioms
/// (coclass creation, VARIANT sentinels, OLE-automation date conversion, safe release) so the
/// service implementation stays readable. See <c>TaskSchedulerNative.cs</c> for the rationale
/// behind the handwritten COM interfaces.
/// </summary>
internal static class TaskSchedulerCom
{
    /// <summary>CLSID of the <c>TaskScheduler</c> coclass (registered as ProgID <c>Schedule.Service</c>).</summary>
    private static readonly Guid ClsidTaskScheduler = new("0f87369f-a4e5-4cfc-bd3e-73e6154572dd");

    // ITaskFolder.RegisterTask / RegisterTaskDefinition creation flags (TASK_CREATION).
    internal const int TaskValidateOnly = 0x1;
    internal const int TaskCreate = 0x2;
    internal const int TaskUpdate = 0x4;
    internal const int TaskCreateOrUpdate = TaskCreate | TaskUpdate; // 0x6
    internal const int TaskDisable = 0x8;
    internal const int TaskDontAddPrincipalAce = 0x10;
    internal const int TaskIgnoreRegistrationTriggers = 0x20;

    // GetTasks / GetFolders / GetRunningTasks enumeration flags (TASK_ENUM_FLAGS).
    internal const int TaskEnumHidden = 0x1;

    // IRegisteredTask.Stop / DeleteTask / DeleteFolder reserved flags must be 0.
    internal const int NoFlags = 0x0;

    // SECURITY_INFORMATION bits used with Get/SetSecurityDescriptor.
    internal const int OwnerSecurityInformation = 0x1;
    internal const int GroupSecurityInformation = 0x2;
    internal const int DaclSecurityInformation = 0x4;
    internal const int SaclSecurityInformation = 0x8;
    internal const int LabelSecurityInformation = 0x10;

    /// <summary>
    /// VARIANT sentinel for a <b>truly omitted</b> optional parameter (VT_ERROR / DISP_E_PARAMNOTFOUND).
    /// Use for <see cref="ITaskService.Connect"/> arguments that should fall back to the local session.
    /// </summary>
    internal static readonly object MissingVariant = Type.Missing;

    /// <summary>
    /// VARIANT sentinel for an <b>explicit empty</b> value (VT_EMPTY). Use for value parameters such as
    /// <see cref="IRegisteredTask.Run"/> <c>params</c> where the API expects a present-but-empty VARIANT.
    /// </summary>
    internal static readonly object? EmptyVariant = null;

    /// <summary>Creates the Task Scheduler coclass and returns its <see cref="ITaskService"/> interface.</summary>
    /// <remarks>
    /// CsWin32 does not emit coclass activation; the RCW from <see cref="Activator.CreateInstance(Type)"/>
    /// casts cleanly to the handwritten dual interface (validated). Caller is responsible for calling
    /// <see cref="ITaskService.Connect"/> before any other method, and for releasing the returned object.
    /// </remarks>
    internal static ITaskService CreateTaskService()
    {
        Type type = Type.GetTypeFromCLSID(ClsidTaskScheduler)
            ?? throw new InvalidOperationException("The Task Scheduler coclass (Schedule.Service) is not registered on this system.");
        object instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Failed to create the Task Scheduler service COM object.");
        return (ITaskService)instance;
    }

    /// <summary>
    /// Converts an OLE automation date (as returned by <see cref="IRegisteredTask.LastRunTime"/> and
    /// <see cref="IRegisteredTask.NextRunTime"/>) to a local <see cref="DateTime"/>. The scheduler returns
    /// a zero/sentinel date when there is no value; those are surfaced as <see langword="null"/>.
    /// </summary>
    internal static DateTime? FromOleDate(double oleDate)
    {
        // The service reports "never" as 1899-12-30 (OLE 0) and occasionally 1899-12-31. Treat anything
        // before 1900 as "no value" rather than a real timestamp.
        if (double.IsNaN(oleDate) || Math.Abs(oleDate) < 2.0)
        {
            return null;
        }

        try
        {
            DateTime value = DateTime.FromOADate(oleDate);
            return value.Year < 1900 ? null : value;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Releases a COM object reference if it is a runtime-callable wrapper; ignores managed objects and nulls.</summary>
    internal static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
