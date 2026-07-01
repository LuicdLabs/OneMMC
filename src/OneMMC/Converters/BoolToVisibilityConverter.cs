using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace OneMMC.Converters;

/// <summary>
/// Converts a boolean value to a Visibility value.
/// True converts to Visible, False converts to Collapsed.
/// Supports two-way binding for scenarios where visibility changes need to update boolean properties.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean value to Visibility.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>Visibility.Visible if true, Visibility.Collapsed if false or invalid input.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    /// <summary>
    /// Converts a Visibility value back to boolean.
    /// </summary>
    /// <param name="value">The Visibility value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>True if Visible, false otherwise.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}
