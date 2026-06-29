using System;
using System.Collections.Generic;

namespace ManagementTools.Core.Features.PCManagement.Models.TaskSchd;

/// <summary>
/// A lightweight summary of a registered task for the task list (mirrors the status columns of
/// <c>IRegisteredTask</c>). The richer <see cref="TaskDefinitionModel"/> is loaded only when a task
/// is opened in the properties editor.
/// </summary>
public sealed class TaskInfo
{
    /// <summary>The task name (the leaf of <see cref="Path"/>).</summary>
    public required string Name { get; init; }

    /// <summary>The full task path, e.g. <c>\Microsoft\Windows\Defrag\ScheduledDefrag</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The path of the folder that contains the task.</summary>
    public required string FolderPath { get; init; }

    /// <summary>The operational state of the task.</summary>
    public TaskState State { get; init; }

    /// <summary>Whether the task is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>The last time the task ran, or <see langword="null"/> if it has never run.</summary>
    public DateTime? LastRunTime { get; init; }

    /// <summary>The HRESULT returned by the last run (0 = success).</summary>
    public int LastTaskResult { get; init; }

    /// <summary>The next scheduled run time, or <see langword="null"/> if the task is not scheduled.</summary>
    public DateTime? NextRunTime { get; init; }

    /// <summary>The number of times the task missed a scheduled run.</summary>
    public int NumberOfMissedRuns { get; init; }

    /// <summary>The task author, when available from the registration information.</summary>
    public string? Author { get; init; }

    /// <summary>A short, localized summary of the task's triggers, set by the view-model layer.</summary>
    public string? TriggersSummary { get; set; }
}

/// <summary>A node in the task folder tree (mirrors <c>ITaskFolder</c> hierarchy).</summary>
public sealed class TaskFolderNode
{
    /// <summary>The folder name (the leaf of <see cref="Path"/>); the root folder's name is <c>\</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The full folder path; the root is <c>\</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The immediate subfolders of this folder.</summary>
    public IList<TaskFolderNode> Children { get; } = new List<TaskFolderNode>();

    /// <summary>True for the root "Task Scheduler Library" folder (path <c>\</c>), which cannot be deleted.</summary>
    public bool IsRoot => Path == "\\";
}

/// <summary>
/// Connection target for the Task Scheduler service. The default is the local machine; a non-empty
/// <see cref="Server"/> connects to a remote computer ("Connect to Another Computer").
/// </summary>
public sealed class TaskSchedulerConnection
{
    /// <summary>The local-machine connection (current user, current session).</summary>
    public static TaskSchedulerConnection Local { get; } = new();

    /// <summary>The remote computer name, or <see langword="null"/>/empty for the local machine.</summary>
    public string? Server { get; init; }

    /// <summary>The user name used for a remote connection (optional; current token when omitted).</summary>
    public string? User { get; init; }

    /// <summary>The domain of <see cref="User"/> (optional).</summary>
    public string? Domain { get; init; }

    /// <summary>The password used for a remote connection (optional; current token when omitted).</summary>
    public string? Password { get; init; }

    /// <summary>True when this targets a remote computer.</summary>
    public bool IsRemote => !string.IsNullOrWhiteSpace(Server);
}
