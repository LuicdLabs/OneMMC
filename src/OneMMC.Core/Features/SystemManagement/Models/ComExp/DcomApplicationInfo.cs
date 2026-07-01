using System;

namespace OneMMC.Core.Features.SystemManagement.Models.ComExp;

public sealed class DcomApplicationInfo
{
    public string Name { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string? LocalService { get; init; }
    public string? RunAs { get; init; }
    public string? DllSurrogate { get; init; }
    public string? ServiceParameters { get; init; }
}


