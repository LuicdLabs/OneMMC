using System;

namespace OneMMC.Core.Features.PCManagement.Models.LusrMgr;

/// <summary>
/// Represents a local user account on the machine.
/// </summary>
public class LocalUser
{
    /// <summary>
    /// Gets or sets the login name of the user.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full display name of the user.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the user account.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the account is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a password is required for this account.
    /// </summary>
    public bool PasswordRequired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user can change their password.
    /// </summary>
    public bool PasswordChangeable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the password expires.
    /// </summary>
    public bool PasswordExpires { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the password has expired.
    /// </summary>
    public bool PasswordExpired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is prevented from changing their password.
    /// </summary>
    public bool UserCannotChangePassword { get; set; }
    
    /// <summary>
    /// Gets the display name string usually formatted as "Name (FullName)".
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Name : $"{Name} ({FullName})";

    /// <summary>
    /// Gets the user's initial (first letter of the name).
    /// </summary>
    public string Initials => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";
}


