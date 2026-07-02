using System;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OneMMC.Converters;

/// <summary>
/// Converts an event log level byte value to a color brush for display.
/// Level values: 1=Critical (Red), 2=Error (Tomato), 3=Warning (Orange), 5=Verbose (Gray).
/// Information (4) and others use Transparent (used on indicator bar Background, not text).
/// </summary>
public partial class LevelToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush CriticalBrush = new(Colors.Red);
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromArgb(255, 255, 99, 71)); // Tomato
    private static readonly SolidColorBrush WarningBrush = new(Color.FromArgb(255, 255, 165, 0)); // Orange
    private static readonly SolidColorBrush VerboseBrush = new(Colors.Gray);
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is byte level)
        {
            return level switch
            {
                1 => CriticalBrush,
                2 => ErrorBrush,
                3 => WarningBrush,
                5 => VerboseBrush,
                _ => TransparentBrush
            };
        }
        return TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
