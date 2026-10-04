using System;

namespace OneMMC.Core.Features.SystemManagement.Models.ComExp;

public sealed class DcomApplicationInfo
{
    public string Name { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string? LocalService { get; init; }
    public string? RunAs { get; init; }
    public string? DllSurrogate { get; init; }

    /// <summary>
    /// Whether the AppID key defines a <c>DllSurrogate</c> value (present even when empty,
    /// which selects the system-supplied surrogate).
    /// </summary>
    public bool HasDllSurrogate { get; init; }

    public string? ServiceParameters { get; init; }

    /// <summary>
    /// Raw <c>AuthenticationLevel</c> DWORD (<see langword="null"/> means "Default").
    /// </summary>
    public uint? AuthenticationLevel { get; init; }

    /// <summary>Localized authentication-level display text.</summary>
    public string AuthenticationLevelDisplay { get; init; } = string.Empty;

    /// <summary>Localized application-type display text (Local Server / Local Service / Surrogate).</summary>
    public string ApplicationType { get; init; } = string.Empty;

    /// <summary>Whether the application is hosted as a Windows service (<c>LocalService</c> set).</summary>
    public bool IsService { get; init; }

    /// <summary>
    /// Best-effort local executable path resolved from the linked CLSID
    /// (<c>LocalServer32</c>, falling back to <c>InprocServer32</c>).
    /// </summary>
    public string? LocalPath { get; init; }

    /// <summary>Remote computer name from <c>RemoteServerName</c>, if configured.</summary>
    public string? RemoteServerName { get; init; }

    /// <summary>Whether "Run application on this computer" is in effect.</summary>
    public bool RunOnThisComputer { get; init; } = true;

    /// <summary>Whether a per-application launch-permission ACL is stored.</summary>
    public bool HasCustomLaunchPermissions { get; init; }

    /// <summary>Whether a per-application access-permission ACL is stored.</summary>
    public bool HasCustomAccessPermissions { get; init; }

    /// <summary>Localized identity display text (Interactive / Launching / This user / Service).</summary>
    public string IdentityDisplay { get; init; } = string.Empty;
}


