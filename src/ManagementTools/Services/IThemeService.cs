using System;

namespace ManagementTools.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public interface IThemeService
{
    AppTheme GetCurrentTheme();
    void SetTheme(AppTheme theme);
    event EventHandler<AppTheme>? ThemeChanged;
}