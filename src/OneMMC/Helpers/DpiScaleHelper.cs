using Windows.Win32;
using Windows.Win32.Foundation;

namespace OneMMC.Helpers;

/// <summary>
/// Converts device-independent pixels (DIPs, 96 DPI baseline) to physical pixels using the
/// per-monitor DPI of a given window (the app is PerMonitorV2 DPI aware, see app.manifest).
/// </summary>
internal static class DpiScaleHelper
{
    /// <summary>Baseline DPI at which one DIP equals one physical pixel (100% scale).</summary>
    private const double BaselineDpi = 96.0;

    /// <summary>
    /// Gets the DPI scale factor (1.0 at 100% / 96 DPI) of the monitor hosting
    /// <paramref name="hwnd"/>. Returns 1.0 when the handle is zero or invalid.
    /// </summary>
    /// <param name="hwnd">Win32 window handle whose monitor DPI should be queried.</param>
    /// <returns>The scale factor, e.g. 2.5 at 250% display scaling.</returns>
    internal static double GetScaleForWindow(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return 1.0;
        }

        uint dpi = PInvoke.GetDpiForWindow(new HWND(hwnd));
        return dpi == 0 ? 1.0 : dpi / BaselineDpi;
    }
}
