using System;

namespace ManagementTools.Core.Features.SystemManagement.Models.ComExp;

public sealed class ProcessInfo
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? FilePath { get; init; }
    public DateTime? StartTime { get; init; }
    public string? ExecutableName { get; init; }
    public bool IsPaused { get; init; }
    public bool IsRecycling { get; init; }
    public bool IsNTService { get; init; }
}


