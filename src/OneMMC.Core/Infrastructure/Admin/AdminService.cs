using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using OneMMC.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace OneMMC.Core.Infrastructure.Admin;

/// <summary>
/// Centralized service for administrator privilege detection and elevation.
/// Registered as a singleton since admin status cannot change during process lifetime.
/// </summary>
public class AdminService : IAdminService
{
    private const string ElevationVerb = "runas";
    private const string DefaultPackagedApplicationId = "App";

    private readonly ILogger<AdminService> _logger;
    private readonly Lazy<bool> _isAdmin;

    /// <inheritdoc />
    public event Action? RestartRequested;

    public AdminService(ILogger<AdminService> logger)
    {
        _logger = logger;
        _isAdmin = new Lazy<bool>(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var result = principal.IsInRole(WindowsBuiltInRole.Administrator);
            _logger.LogInformation("Administrator privilege check: {IsAdmin}", result);
            return result;
        });
    }

    /// <inheritdoc />
    public bool IsRunningAsAdmin => _isAdmin.Value;

    /// <inheritdoc />
    public bool IsPermissionError(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
            return true;

        if (ex is Win32Exception win32Ex && win32Ex.NativeErrorCode == 5) // ERROR_ACCESS_DENIED
            return true;

        var message = ex.Message;

        if (string.IsNullOrEmpty(message))
            return false;

        // Check common permission-related message patterns
        if (message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Insufficient priv", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Insufficient perm", StringComparison.OrdinalIgnoreCase))
            return true;

        // Service-specific pattern: "Cannot open <service name> service"
        if (message.Contains("Cannot open", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("service", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check inner exception recursively
        if (ex.InnerException != null)
            return IsPermissionError(ex.InnerException);

        return false;
    }

    /// <inheritdoc />
    public bool RestartAsAdmin()
    {
        try
        {
            var startInfo = CreateElevatedStartInfo();
            if (startInfo is null)
            {
                _logger.LogError("Cannot restart as admin: unable to resolve launch target.");
                return false;
            }

            _logger.LogInformation(
                "Restarting application as administrator using target: {Target}",
                startInfo.FileName);

            Process.Start(startInfo);
            RestartRequested?.Invoke();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED - user declined UAC
        {
            _logger.LogInformation("User cancelled the UAC elevation prompt.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart application as administrator.");
            return false;
        }
    }

    private ProcessStartInfo? CreateElevatedStartInfo()
    {
        var packagedTarget = TryGetPackagedAppsFolderTarget();
        if (!string.IsNullOrEmpty(packagedTarget))
        {
            return new ProcessStartInfo
            {
                FileName = packagedTarget,
                UseShellExecute = true,
                Verb = ElevationVerb
            };
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return null;
        }

        return new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = true,
            Verb = ElevationVerb
        };
    }

    private string? TryGetPackagedAppsFolderTarget()
    {
        var packageFamilyName = TryGetCurrentPackageFamilyName();
        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            _logger.LogDebug("Application is not running with package identity. Falling back to process path restart.");
            return null;
        }

        var appUserModelId = $"{packageFamilyName}!{DefaultPackagedApplicationId}";
        return $"shell:AppsFolder\\{appUserModelId}";
    }

    /// <summary>
    /// Returns the current process's package family name, or <see langword="null"/> when the process
    /// has no package identity. Unlike <c>Windows.ApplicationModel.Package.Current</c>, which throws
    /// for an unpackaged process, this reports that case as a return code.
    /// </summary>
    private static string? TryGetCurrentPackageFamilyName()
    {
        // PACKAGE_FAMILY_NAME_MAX_LENGTH is 64 characters, plus the null terminator.
        Span<char> buffer = stackalloc char[65];
        uint length = (uint)buffer.Length;

        var result = PInvoke.GetCurrentPackageFamilyName(ref length, buffer);
        if (result != WIN32_ERROR.ERROR_SUCCESS || length == 0)
        {
            return null;
        }

        // On success, length includes the null terminator.
        return new string(buffer[..((int)length - 1)]);
    }
}
