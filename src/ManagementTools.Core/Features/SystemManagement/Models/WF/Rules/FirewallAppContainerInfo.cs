namespace ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;

/// <summary>
/// Describes an app container entry that can be targeted by a Windows Firewall rule.
/// </summary>
public sealed class FirewallAppContainerInfo
{
    /// <summary>
    /// Gets the app container moniker. For packaged apps this is typically the package family name.
    /// </summary>
    public string AppContainerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the friendly display name when one is available from Windows.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the package SID / app container SID used by firewall rule storage.
    /// </summary>
    public string AppContainerSid { get; init; } = string.Empty;

    /// <summary>
    /// Gets the raw user SID that owns the app container.
    /// </summary>
    public string UserSid { get; init; } = string.Empty;

    /// <summary>
    /// Gets the resolved user display name for the owner when it can be translated.
    /// </summary>
    public string UserDisplayName { get; init; } = string.Empty;
}
