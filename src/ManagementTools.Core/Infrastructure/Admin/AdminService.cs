using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using ManagementTools.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;

namespace ManagementTools.Core.Infrastructure.Admin;

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
    public void RestartAsAdmin()
    {
        try
        {
            var startInfo = CreateElevatedStartInfo();
            if (startInfo is null)
            {
                _logger.LogError("Cannot restart as admin: unable to resolve launch target.");
                return;
            }

            _logger.LogInformation(
                "Restarting application as administrator using target: {Target}",
                startInfo.FileName);

            Process.Start(startInfo);
            RestartRequested?.Invoke();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED - user declined UAC
        {
            _logger.LogInformation("User cancelled the UAC elevation prompt.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart application as administrator.");
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
        try
        {
            var packageFamilyName = Package.Current.Id.FamilyName;
            if (string.IsNullOrWhiteSpace(packageFamilyName))
            {
                return null;
            }

            var appUserModelId = $"{packageFamilyName}!{DefaultPackagedApplicationId}";
            return $"shell:AppsFolder\\{appUserModelId}";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Application is not running with package identity. Falling back to process path restart.");
            return null;
        }
    }
}
