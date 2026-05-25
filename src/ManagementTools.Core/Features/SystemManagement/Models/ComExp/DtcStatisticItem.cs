using System;

namespace ManagementTools.Core.Features.SystemManagement.Models.ComExp;

public sealed class DtcStatisticItem
{
    public string Name { get; init; } = string.Empty;
    public double Value { get; init; }
    public string? Unit { get; init; }
}

public sealed class DtcTransactionItem
{
    public string Status { get; init; } = string.Empty;
    public string UnitOfWorkId { get; init; } = string.Empty;
}

public sealed class DtcTransactionsStatistics
{
    // Current
    public uint Open { get; init; }
    public uint OpenMax { get; init; }
    public uint InDoubt { get; init; }

    // Aggregate
    public uint Committed { get; init; }
    public uint Aborted { get; init; }
    public uint ForcedCommit { get; init; }
    public uint ForcedAbort { get; init; }
    public uint Heuristic { get; init; }
    public uint Total => Committed + Aborted + ForcedCommit + ForcedAbort + Heuristic;

    // Response Times (milliseconds)
    public uint ResponseTimeMin { get; init; }
    public uint ResponseTimeAverage { get; init; }
    public uint ResponseTimeMax { get; init; }
}


