using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ManagementTools.Core.Features.PCManagement.Models.EventViewer;

/// <summary>
/// Represents a node in the event log tree hierarchy.
/// Leaf nodes (IsLog=true) have a LogName that maps to a real event log.
/// Branch nodes are organizational containers.
/// </summary>
public partial class EventLogTreeNode : ObservableObject
{
    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The actual log name for EventLogQuery (e.g., "Application", "Microsoft-Windows-PowerShell/Operational").
    /// Null for container/folder nodes.
    /// </summary>
    [ObservableProperty]
    public partial string? LogName { get; set; }

    /// <summary>
    /// Whether this node represents an actual log (leaf) vs. a container (branch).
    /// </summary>
    public bool IsLog => LogName is not null;

    /// <summary>
    /// Child nodes for hierarchical display.
    /// </summary>
    public ObservableCollection<EventLogTreeNode> Children { get; } = [];

    /// <summary>
    /// Used by TreeView default template to display the node text.
    /// </summary>
    public override string ToString() => DisplayName;
}


