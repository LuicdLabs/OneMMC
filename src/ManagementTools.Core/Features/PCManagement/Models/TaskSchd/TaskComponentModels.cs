using System;
using System.Collections.Generic;

namespace ManagementTools.Core.Features.PCManagement.Models.TaskSchd;

/// <summary>Registration information that describes a task (mirrors <c>IRegistrationInfo</c>).</summary>
public sealed class RegistrationInfoModel
{
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Uri { get; set; }
    public string? Source { get; set; }
    public string? Version { get; set; }
    public DateTime? Date { get; set; }
    public string? Documentation { get; set; }

    /// <summary>The security descriptor (SDDL) embedded in the registration information, if any.</summary>
    public string? SecurityDescriptorSddl { get; set; }
}

/// <summary>The security context a task runs in (mirrors <c>IPrincipal</c>/<c>IPrincipal2</c>).</summary>
public sealed class PrincipalModel
{
    /// <summary>The principal identifier referenced by the Actions <c>Context</c> attribute.</summary>
    public string? Id { get; set; }

    /// <summary>The account (SID or DOMAIN\User / UPN) the task runs as.</summary>
    public string? UserId { get; set; }

    /// <summary>The group the task runs as (mutually exclusive with <see cref="UserId"/>).</summary>
    public string? GroupId { get; set; }

    /// <summary>The logon method used to run the task.</summary>
    public TaskLogonType LogonType { get; set; } = TaskLogonType.InteractiveToken;

    /// <summary>The privilege level the task runs with.</summary>
    public TaskRunLevel RunLevel { get; set; } = TaskRunLevel.LeastPrivilege;

    /// <summary>The friendly display name of the principal.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Privileges required by the task (IPrincipal2 RequiredPrivileges).</summary>
    public IList<string> RequiredPrivileges { get; set; } = new List<string>();

    /// <summary>True when the task is configured to run whether or not the user is logged on (password/S4U).</summary>
    public bool RunWhetherLoggedOn =>
        LogonType is TaskLogonType.Password or TaskLogonType.S4U or TaskLogonType.InteractiveTokenOrPassword or TaskLogonType.ServiceAccount;
}

/// <summary>Idle-condition settings (mirrors <c>IIdleSettings</c>).</summary>
public sealed class IdleSettingsModel
{
    /// <summary>Start the task only if the computer has been idle for this long.</summary>
    public TimeSpan? IdleDuration { get; set; }

    /// <summary>Wait this long for the computer to become idle.</summary>
    public TimeSpan? WaitTimeout { get; set; }

    /// <summary>Stop the task if the computer ceases to be idle.</summary>
    public bool StopOnIdleEnd { get; set; } = true;

    /// <summary>Restart the task when the computer becomes idle again.</summary>
    public bool RestartOnIdle { get; set; }
}

/// <summary>Network-condition settings (mirrors <c>INetworkSettings</c>).</summary>
public sealed class NetworkSettingsModel
{
    /// <summary>The identifier of the required network profile, if a specific one is selected.</summary>
    public Guid? Id { get; set; }

    /// <summary>The friendly name of the required network profile ("Any connection" when unset).</summary>
    public string? Name { get; set; }
}

/// <summary>Automatic-maintenance settings (mirrors <c>IMaintenanceSettings</c>, ITaskSettings3).</summary>
public sealed class MaintenanceSettingsModel
{
    /// <summary>The amount of time the task needs to run during automatic maintenance.</summary>
    public TimeSpan? Period { get; set; }

    /// <summary>The deadline after which maintenance is run regardless of the maintenance schedule.</summary>
    public TimeSpan? Deadline { get; set; }

    /// <summary>Whether the task runs exclusively during automatic maintenance.</summary>
    public bool Exclusive { get; set; }
}

/// <summary>
/// The settings and conditions that control how the Task Scheduler runs a task. Covers
/// <c>ITaskSettings</c>, <c>ITaskSettings2</c> and <c>ITaskSettings3</c> plus the Conditions tab
/// (idle/power/network) which the Task XML stores under the <c>&lt;Settings&gt;</c> element.
/// </summary>
public sealed class TaskSettingsModel
{
    // --- General run behaviour ---
    public bool Enabled { get; set; } = true;
    public bool AllowDemandStart { get; set; } = true;
    public bool StartWhenAvailable { get; set; }
    public bool Hidden { get; set; }
    public bool AllowHardTerminate { get; set; } = true;
    public TaskInstancesPolicy MultipleInstances { get; set; } = TaskInstancesPolicy.IgnoreNew;
    public TaskCompatibility Compatibility { get; set; } = TaskCompatibility.V2_3;

    /// <summary>Process priority (0 = highest .. 10 = lowest); Task Scheduler default is 7.</summary>
    public int Priority { get; set; } = 7;

    // --- Restart on failure (Settings tab) ---
    public TimeSpan? RestartInterval { get; set; }
    public int RestartCount { get; set; }

    // --- Time limits (Settings tab) ---
    /// <summary>Stop the task if it runs longer than this. Task Scheduler default is 3 days.</summary>
    public TimeSpan? ExecutionTimeLimit { get; set; } = TimeSpan.FromDays(3);

    /// <summary>Delete the task this long after it expires. <see langword="null"/> keeps the task.</summary>
    public TimeSpan? DeleteExpiredTaskAfter { get; set; }

    // --- Power conditions (Conditions tab) ---
    public bool DisallowStartIfOnBatteries { get; set; } = true;
    public bool StopIfGoingOnBatteries { get; set; } = true;
    public bool WakeToRun { get; set; }

    // --- Idle condition (Conditions tab) ---
    public bool RunOnlyIfIdle { get; set; }
    public IdleSettingsModel IdleSettings { get; set; } = new();

    // --- Network condition (Conditions tab) ---
    public bool RunOnlyIfNetworkAvailable { get; set; }
    public NetworkSettingsModel? NetworkSettings { get; set; }

    // --- ITaskSettings2 / ITaskSettings3 extensions ---
    public bool DisallowStartOnRemoteAppSession { get; set; }
    public bool UseUnifiedSchedulingEngine { get; set; }
    public bool Volatile { get; set; }
    public bool RestartOnIdle { get; set; }
    public MaintenanceSettingsModel? MaintenanceSettings { get; set; }
}
