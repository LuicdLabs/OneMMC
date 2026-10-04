using System;
using System.Collections.Generic;

namespace OneMMC.Core.Features.SystemManagement.Models.ComExp;

/// <summary>
/// Kinds of nodes in the Running Processes tree
/// (Running Processes → process → application → component).
/// </summary>
public enum ComPlusTreeNodeKind
{
    Root,
    Process,
    Application,
    Component
}

/// <summary>
/// A running COM+ server process hosting one application instance
/// (one row of the classic Running Processes list).
/// </summary>
public sealed class ComPlusRunningProcess
{
    public string Name { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string? ExecutableName { get; init; }
    public string? FilePath { get; init; }
    public bool IsPaused { get; init; }
    public bool IsRecycling { get; init; }
    public bool IsNTService { get; init; }
    public ComPlusApplicationInstance Instance { get; init; } = new();

    /// <summary>Tree label in the classic "<c>Name (PID)</c>" form.</summary>
    public string DisplayTitle => $"{Name} ({ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
}

/// <summary>
/// A running instance of a COM+ application (classic instance details:
/// partition ID, application ID, instance ID, activation type).
/// </summary>
public sealed class ComPlusApplicationInstance
{
    public string ApplicationId { get; init; } = string.Empty;
    public string ApplicationName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? PartitionId { get; init; }
    public string? InstanceId { get; init; }

    /// <summary>Localized activation-type display text (Library / Server).</summary>
    public string ActivationType { get; init; } = string.Empty;

    public IReadOnlyList<ComPlusComponentInfo> Components { get; init; } = Array.Empty<ComPlusComponentInfo>();
}

/// <summary>
/// A COM+ component hosted by a running application instance.
/// </summary>
public sealed class ComPlusComponentInfo
{
    public string DisplayName { get; init; } = string.Empty;
    public string? Clsid { get; init; }
    public string? ProgId { get; init; }
    public string? DllPath { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// UI-agnostic tree node content for the Running Processes TreeView.
/// The view maps these to explicit <c>TreeViewNode</c>s (see TaskSchedulerPage);
/// only one of <see cref="Process"/>, <see cref="Instance"/>, <see cref="Component"/> is set.
/// </summary>
public sealed class ComPlusTreeItem
{
    public ComPlusTreeNodeKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public ComPlusRunningProcess? Process { get; init; }
    public ComPlusApplicationInstance? Instance { get; init; }
    public ComPlusComponentInfo? Component { get; init; }
}
