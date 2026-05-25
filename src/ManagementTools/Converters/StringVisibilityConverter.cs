using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ManagementTools.Converters;

/// <summary>
/// Converts a string to Visibility based on whether it contains non-whitespace content.
/// Returns Visible if the string is not null, empty, or whitespace-only; Collapsed otherwise.
/// Similar to StringNotEmptyToVisibilityConverter but also checks for whitespace-only strings.
/// </summary>
public class StringVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a string to Visibility based on whether it has meaningful content.
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>Visibility.Visible if string has content, Visibility.Collapsed if null/empty/whitespace.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// ConvertBack is not implemented as this is a one-way conversion.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
