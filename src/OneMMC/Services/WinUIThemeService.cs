using System;
using Microsoft.UI.Xaml;
using OneMMC.Models;

namespace OneMMC.Services;

/// <summary>
/// Service that manages the application theme using the unified settings.json store.
/// </summary>
public class WinUIThemeService : IThemeService
{
    /// <summary>
    /// Raised when the application theme changes.
    /// </summary>
    public event EventHandler<AppTheme>? ThemeChanged;

    /// <summary>
    /// Gets the current application theme from persistent settings.
    /// </summary>
    public AppTheme GetCurrentTheme()
    {
        var settings = AppSettings.Load();
        return settings.Theme switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.System
        };
    }

    /// <summary>
    /// Sets the application theme and persists it to settings.json.
    /// </summary>
    public void SetTheme(AppTheme theme)
    {
        try
        {
            var settings = AppSettings.Load();
            settings.Theme = theme switch
            {
                AppTheme.Light => "Light",
                AppTheme.Dark => "Dark",
                _ => "Default"
            };
            settings.Save();

            if (Application.Current is App app)
            {
                app.SetAppTheme(ToElementTheme(theme));
            }
            ThemeChanged?.Invoke(this, theme);
        }
        catch { }
    }

    /// <summary>
    /// Converts an <see cref="AppTheme"/> value to a WinUI <see cref="ElementTheme"/>.
    /// </summary>
    public static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
