using System;

namespace ManagementTools.Core.Features.SystemManagement.Models.ComExp;

public sealed class DtcStatusInfo
{
    public string ServiceStatus { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public DateTime? StartTime { get; init; }
}


