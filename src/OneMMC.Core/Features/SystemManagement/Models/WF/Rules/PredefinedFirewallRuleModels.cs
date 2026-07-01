using System.Collections.Generic;

namespace OneMMC.Core.Features.SystemManagement.Models.WF.Rules;

/// <summary>
/// Represents a predefined firewall rule group that can be selected from the New Rule dialog.
/// </summary>
public sealed class PredefinedFirewallRuleGroup
{
    /// <summary>
    /// Gets or sets the internal grouping key from Windows Firewall (for example, "@FirewallAPI.dll,-28502").
    /// </summary>
    public string GroupKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for the group.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the predefined rules in this group.
    /// </summary>
    public List<PredefinedFirewallRuleItem> Rules { get; } = [];
}

/// <summary>
/// Represents one predefined firewall rule entry shown in the New Rule dialog list.
/// </summary>
public sealed class PredefinedFirewallRuleItem
{
    /// <summary>
    /// Gets or sets the actual firewall rule name.
    /// </summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown to users.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description for this predefined rule.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service short name when the rule targets a specific service.
    /// </summary>
    public string Service { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this predefined rule is selected in the UI.
    /// </summary>
    public bool IsSelected { get; set; }
}

