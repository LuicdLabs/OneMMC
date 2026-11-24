using System;

namespace OneMMC.Core.Abstractions.Services;

/// <summary>
/// Provides centralized administrator privilege detection and elevation support.
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Gets whether the current process is running with administrator privileges.
    /// Result is cached since elevation cannot change during process lifetime.
    /// </summary>
    bool IsRunningAsAdmin { get; }

    /// <summary>
    /// Determines whether the given exception represents a permission/access-denied error.
    /// Checks for <see cref="UnauthorizedAccessException"/>, Win32 error code 5 (ERROR_ACCESS_DENIED),
    /// and common permission-related error message patterns.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><c>true</c> if the exception indicates insufficient privileges; otherwise <c>false</c>.</returns>
    bool IsPermissionError(Exception ex);

    /// <summary>
    /// Launches a new instance of the application with elevated (administrator) privileges.
    /// After the elevated process is started, <see cref="RestartRequested"/> is raised so the
    /// UI layer can shut down the current process.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the elevated process was started; <c>false</c> when the user declined the UAC
    /// prompt or the launch failed, in which case the current process must keep running.
    /// </returns>
    bool RestartAsAdmin();

    /// <summary>
    /// Raised after <see cref="RestartAsAdmin"/> successfully starts an elevated process.
    /// The UI layer should subscribe to this event and close its window, which ends the process.
    /// </summary>
    event Action? RestartRequested;
}
