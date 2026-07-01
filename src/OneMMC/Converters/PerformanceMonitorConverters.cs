// ============================================================================
// Performance Monitor Value Converters
// ============================================================================
// File Description:
//   This file contains all XAML value converters required for performance monitor UI, used for:
//   - Converting data model values to UI displayable formats
//   - Handling boolean to visual element conversions
//   - Formatting numeric displays
//
// Architecture Position: View Layer (View layer helper classes in MVVM architecture)
// Usage: Referenced in XAML via StaticResource
// ============================================================================

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;

namespace OneMMC.Converters
{
    // ========================================================================
    // Bool to Brush Converter
    // ========================================================================
    /// <summary>
    /// Converts boolean values to SolidColorBrush.
    /// true: Green (#0F7B0F) - Indicates running/enabled
    /// false: Gray (#888888) - Indicates stopped/disabled
    /// </summary>
    /// <remarks>
    /// Usage scenarios: Monitoring status indicators, enabled/disabled state display
    /// </remarks>
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool b && b
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 123, 15))   // Green
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136)); // Gray

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // ========================================================================
    // Hex Color to Brush Converter
    // ========================================================================
    /// <summary>
    /// Converts hexadecimal color string to SolidColorBrush.
    /// Supported formats: #RRGGBB or RRGGBB
    /// </summary>
    /// <remarks>
    /// Usage scenarios: Counter line colors, legend color display
    /// Error handling: Invalid color strings return yellow as default value
    /// </remarks>
    public class HexColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string hex && hex.Length >= 6)
            {
                try
                {
                    // Remove # prefix (if present)
                    hex = hex.TrimStart('#');
                    // Parse RGB values
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255,
                        byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber)));
                }
                catch { /* Parse failed, return default color */ }
            }
            return new SolidColorBrush(Colors.Yellow); // Default yellow
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // ========================================================================
    // Zero to Visibility Converter
    // ========================================================================
    /// <summary>
    /// Converts integer count to visibility.
    /// Shows (Visible) when count is 0, otherwise hides (Collapsed).
    /// </summary>
    /// <remarks>
    /// Usage scenarios: Display "no data" prompt messages
    /// Example: Show "No counters added yet" when counter list is empty
    /// </remarks>
    public class ZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is int count && count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // ========================================================================
    // Double to Formatted String Converter
    // ========================================================================
    /// <summary>
    /// Formats double precision values to string, preserving three decimal places.
    /// Format: N3 (e.g., 123.456)
    /// </summary>
    /// <remarks>
    /// Usage scenarios: Statistical information display (latest value, average, minimum, maximum)
    /// </remarks>
    public class DoubleToFormattedStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is double d ? d.ToString("N3") : "0.000";

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // ========================================================================
    // Bool to Monitoring State Converter
    // ========================================================================
    /// <summary>
    /// Converts monitoring state (boolean) to icon or text.
    /// Output type determined by parameter:
    /// - "glyph": Returns icon character (pause/play)
    /// - "text": Returns localized text (pause/resume)
    /// </summary>
    /// <remarks>
    /// Usage scenarios: Pause/resume button icons and tooltip text
    /// Icon descriptions:
    /// - \uE769: Pause icon (displayed when monitoring)
    /// - \uE768: Play icon (displayed when paused)
    /// </remarks>
    public class BoolToMonitoringStateConverter : IValueConverter
    {
        /// <summary>Localized string resources</summary>
        public OneMMC.Localization.LocalizedStrings LocalizedStrings { get; set; } = OneMMC.Localization.LocalizedStrings.Instance;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var isMonitoring = value is bool b && b;
            return parameter?.ToString()?.ToLower() switch
            {
                "glyph" => isMonitoring ? "\uE769" : "\uE768",  // Pause/play icons
                "text" => isMonitoring ? LocalizedStrings.PerfMon_Pause : LocalizedStrings.PerfMon_Resume,
                _ => isMonitoring ? "\uE769" : "\uE768"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // ========================================================================
    // Double to Height Converter
    // ========================================================================
    /// <summary>
    /// Converts numeric values to bar chart height.
    /// Calculates ratio based on MaxValue and MaxHeight properties.
    /// </summary>
    /// <remarks>
    /// Usage scenarios: Counter value visualization in bar chart view mode
    /// Calculation formula: height = (value / MaxValue) * MaxHeight
    /// </remarks>
    public class DoubleToHeightConverter : IValueConverter
    {
        /// <summary>Maximum bar chart height (pixels)</summary>
        public double MaxHeight { get; set; } = 180;
        
        /// <summary>Maximum numeric value (for normalization)</summary>
        public double MaxValue { get; set; } = 100;

        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is double d ? Math.Clamp(d / MaxValue, 0, 1) * MaxHeight : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // ========================================================================
    // Legacy Converters - Backward Compatibility
    // ========================================================================
    // Description:
    //   The following converters are legacy implementations, retained for backward compatibility.
    //   New code should use BoolToMonitoringStateConverter.
    // ========================================================================

    /// <summary>
    /// [Legacy] Converts boolean values to icon characters.
    /// Recommend using BoolToMonitoringStateConverter instead.
    /// </summary>
    public class BoolToGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool b && b ? "\uE769" : "\uE768"; // Pause/play icons
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>
    /// [Legacy] Converts boolean values to text.
    /// Recommend using BoolToMonitoringStateConverter instead.
    /// </summary>
    public class BoolToTextConverter : IValueConverter
    {
        /// <summary>Localized string resources</summary>
        public OneMMC.Localization.LocalizedStrings LocalizedStrings { get; set; } = OneMMC.Localization.LocalizedStrings.Instance;

        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool b && b ? LocalizedStrings.PerfMon_Pause : LocalizedStrings.PerfMon_Resume;

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
