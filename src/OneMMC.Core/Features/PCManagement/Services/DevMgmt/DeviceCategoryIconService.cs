using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Extensions.Logging;
using Windows.Win32.UI.WindowsAndMessaging;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.DevMgmt;

/// <summary>
/// Loads device setup class icons the same way Device Manager does.
/// </summary>
public sealed class DeviceCategoryIconService
{
    // GUID_DEVCLASS_UNKNOWN ("Other devices"): WMI reports no ClassGuid for devices without a
    // matched driver, and Device Manager shows them under this class with its question-mark icon.
    private static readonly Guid UnknownClassGuid = new("4d36e97e-e325-11ce-bfc1-08002be10318");

    private readonly ILogger<DeviceCategoryIconService> _logger;
    private readonly Dictionary<Guid, byte[]?> _icons = [];

    /// <summary>
    /// Initializes the category icon loader.
    /// </summary>
    /// <param name="logger">The diagnostic logger.</param>
    public DeviceCategoryIconService(ILogger<DeviceCategoryIconService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns PNG bytes for a device setup class, or null when the class icon cannot be loaded.
    /// </summary>
    /// <param name="classGuid">The setup class GUID string; empty or invalid values select the unknown class.</param>
    /// <returns>The optional PNG payload.</returns>
    public byte[]? GetIconPng(string? classGuid)
    {
        Guid guid = Guid.TryParse(classGuid, out Guid parsed) && parsed != Guid.Empty ? parsed : UnknownClassGuid;

        // The cache is bounded by the installed setup classes and lives with this transient service.
        // Keep only managed PNG bytes; native icon handles are released during extraction.
        lock (_icons)
        {
            if (!_icons.TryGetValue(guid, out byte[]? png))
            {
                png = ExtractIconPng(guid);
                _icons.Add(guid, png);
            }

            return png;
        }
    }

    private unsafe byte[]? ExtractIconPng(Guid classGuid)
    {
        HICON iconHandle = default;
        try
        {
            // SetupAPI resolves the class's registry Icon/IconPath values and class installer
            // resources, so third-party and newer classes get the same icon as Device Manager.
            if (!Win32PInvoke.SetupDiLoadClassIcon(&classGuid, &iconHandle, null) || iconHandle.IsNull)
            {
                return null;
            }

            using Icon icon = Icon.FromHandle((nint)iconHandle.Value);
            using Bitmap bitmap = icon.ToBitmap();
            using MemoryStream stream = new();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load device class icon {ClassGuid}.", classGuid);
            return null;
        }
        finally
        {
            if (!iconHandle.IsNull)
            {
                _ = Win32PInvoke.DestroyIcon(iconHandle);
            }
        }
    }
}
