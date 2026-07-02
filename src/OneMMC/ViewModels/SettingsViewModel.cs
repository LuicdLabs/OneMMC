using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Localization;
using OneMMC.Services;

namespace OneMMC.ViewModels;

/// <summary>
/// A selectable theme entry (stable value + localized display name).
/// Declared at namespace level so XAML compiled bindings (x:DataType/x:Bind) can reference it.
/// </summary>
public class ThemeOption
{
    public string Value { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public partial class SettingsViewModel : ObservableObject
{
    private const string ThemeLight = "Light";
    private const string ThemeDark = "Dark";
    private const string ThemeSystem = "Use Windows theme";

    private readonly IThemeService _themeService;

    // Use simple data structure and let the UI layer handle localization
    public class SettingItemData
    {
        public string Glyph { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string SubtitleKey { get; set; } = string.Empty;
        public int NavigationIndex { get; set; }
    }

    public SettingItemData[] SettingsData { get; } = new[]
    {
        new SettingItemData { Glyph = "\uE790", TitleKey = "SettingItem_Theme_Title", SubtitleKey = "SettingItem_Theme_Subtitle", NavigationIndex = 0 }
    };

    public List<ThemeOption> ThemeOptions { get; } =
    [
        new() { Value = ThemeLight, DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.Settings, "Settings_ThemeOption_Light") },
        new() { Value = ThemeDark, DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.Settings, "Settings_ThemeOption_Dark") },
        new() { Value = ThemeSystem, DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.Settings, "Settings_ThemeOption_UseWindowsTheme") }
    ];

    private string _selectedTheme;
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                OnPropertyChanged(nameof(SelectedThemeOption));
                if (!string.IsNullOrEmpty(value))
                {
                    _themeService.SetTheme(ToAppTheme(value));
                }
            }
        }
    }

    /// <summary>
    /// The <see cref="ThemeOption"/> matching <see cref="SelectedTheme"/>, for compiled
    /// SelectedItem binding (replaces the reflection-based SelectedValuePath pattern).
    /// </summary>
    public ThemeOption? SelectedThemeOption
    {
        get => ThemeOptions.Find(option => option.Value == SelectedTheme);
        set
        {
            if (value is not null)
            {
                SelectedTheme = value.Value;
            }
        }
    }

    public SettingsViewModel(IThemeService themeService)
    {
        _themeService = themeService;
        _selectedTheme = ToThemeValue(_themeService.GetCurrentTheme());
    }

    private static AppTheme ToAppTheme(string themeValue)
        => themeValue switch
        {
            ThemeLight => AppTheme.Light,
            ThemeDark => AppTheme.Dark,
            _ => AppTheme.System
        };

    private static string ToThemeValue(AppTheme theme)
        => theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem
        };

    public Action<int>? OnNavigateRequest;
}
