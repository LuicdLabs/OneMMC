using System.Windows.Input;
using OneMMC.Models;

namespace OneMMC.Models;

public class SettingItem
{
    private OneMMC.Localization.LocalizedStrings _localizedStrings = OneMMC.Localization.LocalizedStrings.Instance;

    public string Glyph { get; set; }
    public string Title { get; set; }
    public string Subtitle { get; set; }
    public ICommand? Command { get; set; }

    public string TitleKey
    {
        get => string.Empty; // Not used for getting
        set
        {
            Title = _localizedStrings.GetType().GetProperty(value)?.GetValue(_localizedStrings)?.ToString() ?? string.Empty;
        }
    }

    public string SubtitleKey
    {
        get => string.Empty; // Not used for getting
        set
        {
            Subtitle = _localizedStrings.GetType().GetProperty(value)?.GetValue(_localizedStrings)?.ToString() ?? string.Empty;
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