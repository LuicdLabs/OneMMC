using System;

namespace OneMMC.Core.Features.PCManagement.Models.LusrMgr;

/// <summary>
/// Represents a local user group on the machine.
/// </summary>
public class LocalGroup
{
    /// <summary>
    /// Gets or sets the name of the group.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the group.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}


