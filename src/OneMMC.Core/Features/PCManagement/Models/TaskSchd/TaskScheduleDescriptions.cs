using System;
using System.Globalization;
using System.Linq;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PCManagement.Models.TaskSchd;

/// <summary>
/// Produces localized, human-readable display names and one-line summaries for triggers and actions,
/// matching the wording taskschd.msc shows in the Triggers and Actions columns.
/// </summary>
public static class TaskScheduleDescriptions
{
    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    private static string LF(string key, params object[] args) =>
        LocalizationProvider.Current.GetFormattedString(ResourceFileNames.TaskSchd, key, args);

    /// <summary>The localized trigger type label (e.g. "On a schedule", "At log on").</summary>
    public static string TriggerTypeName(TriggerModel trigger) => trigger switch
    {
        TimeTriggerModel or DailyTriggerModel or WeeklyTriggerModel or MonthlyTriggerModel or MonthlyDowTriggerModel => L(TaskSchdKeys.TriggerOnSchedule),
        LogonTriggerModel => L(TaskSchdKeys.TriggerAtLogon),
        BootTriggerModel => L(TaskSchdKeys.TriggerAtStartup),
        IdleTriggerModel => L(TaskSchdKeys.TriggerOnIdle),
        EventTriggerModel => L(TaskSchdKeys.TriggerOnEvent),
        RegistrationTriggerModel => L(TaskSchdKeys.TriggerAtCreation),
        SessionStateChangeTriggerModel s => SessionTypeName(s.StateChange),
        _ => L(TaskSchdKeys.TriggerOnSchedule),
    };

    private static string SessionTypeName(SessionStateChangeType change) => change switch
    {
        SessionStateChangeType.ConsoleConnect or SessionStateChangeType.RemoteConnect => L(TaskSchdKeys.TriggerOnConnect),
        SessionStateChangeType.ConsoleDisconnect or SessionStateChangeType.RemoteDisconnect => L(TaskSchdKeys.TriggerOnDisconnect),
        SessionStateChangeType.SessionLock => L(TaskSchdKeys.TriggerOnLock),
        SessionStateChangeType.SessionUnlock => L(TaskSchdKeys.TriggerOnUnlock),
        _ => L(TaskSchdKeys.TriggerOnConnect),
    };

    /// <summary>A one-line description of when the trigger fires.</summary>
    public static string TriggerSummary(TriggerModel trigger) => trigger switch
    {
        LogonTriggerModel l => LF(TaskSchdKeys.TriggerSummaryAtLogon, string.IsNullOrEmpty(l.UserId) ? "any user" : l.UserId),
        BootTriggerModel => L(TaskSchdKeys.TriggerSummaryAtStartup),
        DailyTriggerModel d => LF(TaskSchdKeys.TriggerSummaryDailyFormat, FormatTime(d.StartBoundary), d.DaysInterval),
        WeeklyTriggerModel w => LF(TaskSchdKeys.TriggerSummaryWeeklyFormat, FormatTime(w.StartBoundary), w.WeeksInterval, DescribeDays(w.DaysOfWeek)),
        TimeTriggerModel t => LF(TaskSchdKeys.TriggerSummaryOneTimeFormat, FormatDateTime(t.StartBoundary)),
        EventTriggerModel => L(TaskSchdKeys.TriggerOnEvent),
        IdleTriggerModel => L(TaskSchdKeys.TriggerOnIdle),
        RegistrationTriggerModel => L(TaskSchdKeys.TriggerAtCreation),
        MonthlyTriggerModel or MonthlyDowTriggerModel => L(TaskSchdKeys.ScheduleMonthly),
        SessionStateChangeTriggerModel s => SessionTypeName(s.StateChange),
        _ => TriggerTypeName(trigger),
    };

    /// <summary>The localized action type label.</summary>
    public static string ActionTypeName(ActionModel action) => action.Type switch
    {
        TaskActionType.Execute => L(TaskSchdKeys.ActionStartProgram),
        TaskActionType.SendEmail => L(TaskSchdKeys.ActionSendEmail),
        TaskActionType.ShowMessage => L(TaskSchdKeys.ActionDisplayMessage),
        _ => action.Type.ToString(),
    };

    /// <summary>A one-line description of what the action does.</summary>
    public static string ActionSummary(ActionModel action) => action switch
    {
        ExecActionModel e => string.IsNullOrEmpty(e.Arguments) ? e.Path : $"{e.Path} {e.Arguments}",
        ComHandlerActionModel c => c.ClassId.ToString("B"),
        EmailActionModel m => $"{L(TaskSchdKeys.ActionEmailTo)} {m.To}",
        ShowMessageActionModel s => s.Title ?? string.Empty,
        _ => string.Empty,
    };

    private static string FormatTime(DateTime? value) => value?.ToString("t", CultureInfo.CurrentCulture) ?? string.Empty;

    private static string FormatDateTime(DateTime? value) => value?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;

    private static string DescribeDays(TaskDaysOfWeek days)
    {
        if (days == TaskDaysOfWeek.None)
        {
            return string.Empty;
        }
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames; // index 0 = Sunday
        var selected = Enumerable.Range(0, 7)
            .Where(i => ((int)days & (1 << i)) != 0)
            .Select(i => names[i]);
        return string.Join(", ", selected);
    }
}
