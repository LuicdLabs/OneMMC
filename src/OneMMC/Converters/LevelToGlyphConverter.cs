using System;
using Microsoft.UI.Xaml.Data;

namespace OneMMC.Converters;

/// <summary>
/// Converts an event log level byte value to a Segoe Fluent Icons glyph for the severity icon.
/// Level values: 1=Critical, 2=Error, 3=Warning, 4=Information, 5=Verbose.
/// Glyph code points are from the Segoe Fluent Icons reference.
/// </summary>
public partial class LevelToGlyphConverter : IValueConverter
{
    private const string CriticalGlyph = "\uEA39";   // StatusErrorFull
    private const string ErrorGlyph = "\uE783";       // StatusError
    private const string WarningGlyph = "\uE7BA";     // StatusWarning
    private const string InformationGlyph = "\uE946"; // StatusInfo
    private const string VerboseGlyph = "\uEA1F";     // Info

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is byte level)
        {
            return level switch
            {
                1 => CriticalGlyph,
                2 => ErrorGlyph,
                3 => WarningGlyph,
                5 => VerboseGlyph,
                _ => InformationGlyph
            };
        }
        return InformationGlyph;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
