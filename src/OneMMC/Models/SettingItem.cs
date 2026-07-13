using System.Windows.Input;
using OneMMC.Core.Localization;
using OneMMC.Localization;

namespace OneMMC.Models;

public class SettingItem
{
    public string Glyph { get; set; }
    public string Title { get; set; }
    public string Subtitle { get; set; }
    public ICommand? Command { get; set; }

    public string TitleKey
    {
        get => string.Empty; // Not used for getting
        set
        {
            // All SettingItem_* keys live in Settings.resw; resolve the resource directly
            // instead of reflecting over LocalizedStrings properties (trimmed under AOT, IL2075).
            Title = LocalizationService.Instance.GetString(ResourceFileNames.Settings, value);
        }
    }

    public string SubtitleKey
    {
        get => string.Empty; // Not used for getting
        set
        {
            Subtitle = LocalizationService.Instance.GetString(ResourceFileNames.Settings, value);
        }
    }

    public SettingItem()
    {
        Glyph = string.Empty;
        Title = string.Empty;
        Subtitle = string.Empty;
    }

    public SettingItem(string glyph, string titleKey, string subtitleKey)
    {
        Glyph = glyph;
        Title = string.Empty;
        Subtitle = string.Empty;
        TitleKey = titleKey;
        SubtitleKey = subtitleKey;
    }
}