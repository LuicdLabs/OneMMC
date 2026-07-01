using System;

namespace OneMMC.Core.Features.PCManagement.Models.TaskSchd;

/// <summary>Operational state of a registered task (mirrors <c>TASK_STATE</c>).</summary>
public enum TaskState
{
    /// <summary>The state could not be determined.</summary>
    Unknown = 0,
    /// <summary>The task is registered but disabled and has no scheduled runs.</summary>
    Disabled = 1,
    /// <summary>The task is queued and waiting to run.</summary>
    Queued = 2,
    /// <summary>The task is ready to run at its next scheduled time.</summary>
    Ready = 3,
    /// <summary>One or more instances of the task are currently running.</summary>
    Running = 4,
}

/// <summary>The kind of trigger that starts a task (mirrors <c>TASK_TRIGGER_TYPE2</c>).</summary>
public enum TaskTriggerType
{
    /// <summary>Starts the task when a specific event is logged (IEventTrigger).</summary>
    Event = 0,
    /// <summary>Starts the task once at a specific date and time (ITimeTrigger).</summary>
    Time = 1,
    /// <summary>Starts the task on a daily schedule (IDailyTrigger).</summary>
    Daily = 2,
    /// <summary>Starts the task on a weekly schedule (IWeeklyTrigger).</summary>
    Weekly = 3,
    /// <summary>Starts the task on specific days of specific months (IMonthlyTrigger).</summary>
    Monthly = 4,
    /// <summary>Starts the task on a monthly day-of-week schedule (IMonthlyDOWTrigger).</summary>
    MonthlyDayOfWeek = 5,
    /// <summary>Starts the task when the computer becomes idle (IIdleTrigger).</summary>
    Idle = 6,
    /// <summary>Starts the task when it is registered or updated (IRegistrationTrigger).</summary>
    Registration = 7,
    /// <summary>Starts the task when the computer boots (IBootTrigger).</summary>
    Boot = 8,
    /// <summary>Starts the task when a specific user logs on (ILogonTrigger).</summary>
    Logon = 9,
    /// <summary>Starts the task on a Terminal Server session state change (ISessionStateChangeTrigger).</summary>
    SessionStateChange = 11,
}

/// <summary>The kind of action a task performs (mirrors <c>TASK_ACTION_TYPE</c>).</summary>
public enum TaskActionType
{
    /// <summary>Executes a command-line operation (IExecAction).</summary>
    Execute = 0,
    /// <summary>Fires an in-process COM handler (IComHandlerAction).</summary>
    ComHandler = 5,
    /// <summary>Sends an email message (IEmailAction). Deprecated since Windows 8.</summary>
    SendEmail = 6,
    /// <summary>Shows a message box (IShowMessageAction). Deprecated since Windows 8.</summary>
    ShowMessage = 7,
}

/// <summary>The security logon method used to run a task (mirrors <c>TASK_LOGON_TYPE</c>).</summary>
public enum TaskLogonType
{
    /// <summary>No logon method specified; defaults are used.</summary>
    None = 0,
    /// <summary>Run with the stored password of the specified account.</summary>
    Password = 1,
    /// <summary>Run as the specified account using service-for-user (no stored password).</summary>
    S4U = 2,
    /// <summary>Run only when the specified user is logged on interactively.</summary>
    InteractiveToken = 3,
    /// <summary>Run as the specified group.</summary>
    Group = 4,
    /// <summary>Run as a well-known service account (LocalService, NetworkService, LocalSystem).</summary>
    ServiceAccount = 5,
    /// <summary>Use an interactive token if available, otherwise the stored password.</summary>
    InteractiveTokenOrPassword = 6,
}

/// <summary>The privilege level a task runs with (mirrors <c>TASK_RUNLEVEL_TYPE</c>).</summary>
public enum TaskRunLevel
{
    /// <summary>Run with the least privileges of the account (LUA-limited token).</summary>
    LeastPrivilege = 0,
    /// <summary>Run with the highest privileges of the account (elevated token).</summary>
    HighestAvailable = 1,
}

/// <summary>Task Scheduler version a task is compatible with (mirrors <c>TASK_COMPATIBILITY</c>).</summary>
public enum TaskCompatibility
{
    /// <summary>AT command compatibility.</summary>
    At = 0,
    /// <summary>Task Scheduler 1.0 (Windows XP / Server 2003).</summary>
    V1 = 1,
    /// <summary>Task Scheduler 2.0 (Windows Vista / Server 2008).</summary>
    V2 = 2,
    /// <summary>Windows 7 / Server 2008 R2.</summary>
    V2_1 = 3,
    /// <summary>Windows 8 / Server 2012.</summary>
    V2_2 = 4,
    /// <summary>Windows 10.</summary>
    V2_3 = 5,
}

/// <summary>Policy used when multiple instances of a task are triggered (mirrors <c>TASK_INSTANCES_POLICY</c>).</summary>
public enum TaskInstancesPolicy
{
    /// <summary>Start a new instance in parallel with existing instances.</summary>
    Parallel = 0,
    /// <summary>Queue a new instance after the existing ones.</summary>
    Queue = 1,
    /// <summary>Do not start a new instance if one is already running.</summary>
    IgnoreNew = 2,
    /// <summary>Stop the existing instance before starting a new one.</summary>
    StopExisting = 3,
}

/// <summary>Process priority class of a task (Settings &gt; Priority; 0 = highest .. 10 = lowest).</summary>
public enum TaskProcessPriority
{
    /// <summary>Realtime (0).</summary>
    Realtime = 0,
    /// <summary>High (1).</summary>
    High = 1,
    /// <summary>Above normal (2-3).</summary>
    AboveNormal = 2,
    /// <summary>Normal (4-6) — the default.</summary>
    Normal = 6,
    /// <summary>Below normal (7-8).</summary>
    BelowNormal = 7,
    /// <summary>Idle / lowest (9-10).</summary>
    Idle = 10,
}

/// <summary>The Terminal Server session change that fires a session-state-change trigger
/// (mirrors <c>TASK_SESSION_STATE_CHANGE_TYPE</c>).</summary>
public enum SessionStateChangeType
{
    /// <summary>Local console connect (e.g. fast user switch back to the console).</summary>
    ConsoleConnect = 1,
    /// <summary>Local console disconnect.</summary>
    ConsoleDisconnect = 2,
    /// <summary>Remote (RDP) connect.</summary>
    RemoteConnect = 3,
    /// <summary>Remote (RDP) disconnect.</summary>
    RemoteDisconnect = 4,
    /// <summary>Workstation lock.</summary>
    SessionLock = 7,
    /// <summary>Workstation unlock.</summary>
    SessionUnlock = 8,
}

/// <summary>Days of the week, as a bitmask matching the Task Scheduler weekly trigger encoding.</summary>
[Flags]
public enum TaskDaysOfWeek
{
    None = 0,
    Sunday = 0x01,
    Monday = 0x02,
    Tuesday = 0x04,
    Wednesday = 0x08,
    Thursday = 0x10,
    Friday = 0x20,
    Saturday = 0x40,
    AllDays = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
}

/// <summary>Months of the year, as a bitmask matching the Task Scheduler monthly trigger encoding.</summary>
[Flags]
public enum TaskMonthsOfYear
{
    None = 0,
    January = 0x0001,
    February = 0x0002,
    March = 0x0004,
    April = 0x0008,
    May = 0x0010,
    June = 0x0020,
    July = 0x0040,
    August = 0x0080,
    September = 0x0100,
    October = 0x0200,
    November = 0x0400,
    December = 0x0800,
    AllMonths = January | February | March | April | May | June | July | August | September | October | November | December,
}

/// <summary>Weeks of the month, as a bitmask matching the Task Scheduler monthly day-of-week trigger encoding.</summary>
[Flags]
public enum TaskWeeksOfMonth
{
    None = 0,
    First = 0x01,
    Second = 0x02,
    Third = 0x04,
    Fourth = 0x08,
    /// <summary>The last week of the month (encoded separately via <c>RunOnLastWeekOfMonth</c> in XML).</summary>
    Last = 0x10,
}
