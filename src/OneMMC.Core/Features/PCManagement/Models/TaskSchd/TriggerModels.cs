using System;
using System.Collections.Generic;

namespace OneMMC.Core.Features.PCManagement.Models.TaskSchd;

/// <summary>Defines how often a started task repeats and for how long (mirrors <c>IRepetitionPattern</c>).</summary>
public sealed class RepetitionModel
{
    /// <summary>How often the task repeats after it starts. <see langword="null"/> disables repetition.</summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>How long the repetition pattern runs. <see langword="null"/> means repeat indefinitely.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Whether a running instance is stopped at the end of the repetition duration.</summary>
    public bool StopAtDurationEnd { get; set; }

    /// <summary>True when a repetition interval has been configured.</summary>
    public bool IsEnabled => Interval.HasValue && Interval.Value > TimeSpan.Zero;
}

/// <summary>Common properties shared by all triggers (mirrors <c>ITrigger</c>).</summary>
public abstract class TriggerModel
{
    /// <summary>The concrete trigger type.</summary>
    public abstract TaskTriggerType Type { get; }

    /// <summary>Optional trigger identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Whether the trigger is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The date/time the trigger is activated (StartBoundary). Not used by Registration/Boot/Idle/Logon.</summary>
    public DateTime? StartBoundary { get; set; }

    /// <summary>The date/time the trigger expires (EndBoundary).</summary>
    public DateTime? EndBoundary { get; set; }

    /// <summary>Maximum amount of time the task is allowed to run when started by this trigger.</summary>
    public TimeSpan? ExecutionTimeLimit { get; set; }

    /// <summary>Repetition pattern applied after the trigger fires.</summary>
    public RepetitionModel Repetition { get; set; } = new();

    /// <summary>A short, human-readable summary of the trigger (e.g. "At log on of any user"). Set by the view-model layer.</summary>
    public string? DisplaySummary { get; set; }
}

/// <summary>Starts the task once at a specific date and time (ITimeTrigger).</summary>
public sealed class TimeTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Time;

    /// <summary>An optional random delay added to the start time.</summary>
    public TimeSpan? RandomDelay { get; set; }
}

/// <summary>Starts the task on a daily schedule (IDailyTrigger).</summary>
public sealed class DailyTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Daily;

    /// <summary>Recur every N days (≥ 1).</summary>
    public short DaysInterval { get; set; } = 1;

    /// <summary>An optional random delay added to the start time.</summary>
    public TimeSpan? RandomDelay { get; set; }
}

/// <summary>Starts the task on a weekly schedule (IWeeklyTrigger).</summary>
public sealed class WeeklyTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Weekly;

    /// <summary>Recur every N weeks (≥ 1).</summary>
    public short WeeksInterval { get; set; } = 1;

    /// <summary>Days of the week the task runs.</summary>
    public TaskDaysOfWeek DaysOfWeek { get; set; }

    /// <summary>An optional random delay added to the start time.</summary>
    public TimeSpan? RandomDelay { get; set; }
}

/// <summary>Starts the task on specific days of specific months (IMonthlyTrigger).</summary>
public sealed class MonthlyTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Monthly;

    /// <summary>Days of the month (1–31) the task runs.</summary>
    public IList<int> DaysOfMonth { get; set; } = new List<int>();

    /// <summary>Run on the last day of the month regardless of the day count.</summary>
    public bool RunOnLastDayOfMonth { get; set; }

    /// <summary>Months in which the task runs.</summary>
    public TaskMonthsOfYear MonthsOfYear { get; set; }

    /// <summary>An optional random delay added to the start time.</summary>
    public TimeSpan? RandomDelay { get; set; }
}

/// <summary>Starts the task on a monthly day-of-week schedule (IMonthlyDOWTrigger).</summary>
public sealed class MonthlyDowTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.MonthlyDayOfWeek;

    /// <summary>Weeks of the month (First..Fourth) the task runs on.</summary>
    public TaskWeeksOfMonth WeeksOfMonth { get; set; }

    /// <summary>Run during the last week of the month.</summary>
    public bool RunOnLastWeekOfMonth { get; set; }

    /// <summary>Days of the week the task runs.</summary>
    public TaskDaysOfWeek DaysOfWeek { get; set; }

    /// <summary>Months in which the task runs.</summary>
    public TaskMonthsOfYear MonthsOfYear { get; set; }

    /// <summary>An optional random delay added to the start time.</summary>
    public TimeSpan? RandomDelay { get; set; }
}

/// <summary>Starts the task when the computer boots (IBootTrigger).</summary>
public sealed class BootTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Boot;

    /// <summary>An optional delay after boot before the task starts.</summary>
    public TimeSpan? Delay { get; set; }
}

/// <summary>Starts the task when the computer becomes idle (IIdleTrigger).</summary>
public sealed class IdleTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Idle;
}

/// <summary>Starts the task when it is registered or updated (IRegistrationTrigger).</summary>
public sealed class RegistrationTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Registration;

    /// <summary>An optional delay after registration before the task starts.</summary>
    public TimeSpan? Delay { get; set; }
}

/// <summary>Starts the task when a specific user logs on (ILogonTrigger).</summary>
public sealed class LogonTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Logon;

    /// <summary>The user whose logon starts the task. <see langword="null"/>/empty means any user.</summary>
    public string? UserId { get; set; }

    /// <summary>An optional delay after logon before the task starts.</summary>
    public TimeSpan? Delay { get; set; }
}

/// <summary>Starts the task when a specific event is logged (IEventTrigger).</summary>
public sealed class EventTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.Event;

    /// <summary>The event query as an XPath subscription string (the New Event Filter result).</summary>
    public string? Subscription { get; set; }

    /// <summary>An optional delay after the event before the task starts.</summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>Named value queries that capture event data into task variables.</summary>
    public IDictionary<string, string> ValueQueries { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Starts the task on a Terminal Server session state change (ISessionStateChangeTrigger).</summary>
public sealed class SessionStateChangeTriggerModel : TriggerModel
{
    public override TaskTriggerType Type => TaskTriggerType.SessionStateChange;

    /// <summary>The session change that fires the trigger.</summary>
    public SessionStateChangeType StateChange { get; set; } = SessionStateChangeType.ConsoleConnect;

    /// <summary>The user the change applies to. <see langword="null"/>/empty means any user.</summary>
    public string? UserId { get; set; }

    /// <summary>An optional delay after the state change before the task starts.</summary>
    public TimeSpan? Delay { get; set; }
}
