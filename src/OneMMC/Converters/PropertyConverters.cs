using Microsoft.UI.Xaml.Data;
using System;
using OneMMC.Localization;

namespace OneMMC.Converters;

/// <summary>
/// Converts null or empty values to a dash ("-") string for display purposes.
/// Useful for showing a placeholder when property values are unavailable.
/// </summary>
public partial class NullToDashStringConverter : IValueConverter
{
    /// <summary>
    /// Converts a value to its string representation, or "-" if null/empty.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>String representation of the value, or "-" if null/empty.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null) return "-";
        if (value is string s && string.IsNullOrEmpty(s)) return "-";
        return value.ToString() ?? "-";
    }

    /// <summary>
    /// ConvertBack is not implemented as this is a one-way conversion.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean value to localized "Yes" or "No" strings.
/// Uses the application's localization system for internationalization support.
/// </summary>
public partial class BoolToYesNoConverter : IValueConverter
{
    private static readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    /// <summary>
    /// Converts a boolean to a localized Yes/No string.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>Localized "Yes" if true, "No" if false or not a boolean.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return b ? _localizedStrings.ComExp_Yes : _localizedStrings.ComExp_No;
        }
        return _localizedStrings.ComExp_No;
    }

    /// <summary>
    /// ConvertBack is not implemented as this is a one-way conversion.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts byte count values (ulong or uint) to formatted strings with thousand separators.
/// Displays values in the format "1,234,567 bytes".
/// </summary>
public partial class BytesToStringConverter : IValueConverter
{
    /// <summary>
    /// Converts a byte count to a formatted string.
    /// </summary>
    /// <param name="value">The byte count (ulong or uint).</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>Formatted byte string with thousand separators, or "-" if invalid.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ulong bytes)
        {
            return $"{bytes:N0} bytes";
        }
        if (value is uint bytesUint)
        {
            return $"{bytesUint:N0} bytes";
        }
        return "-";
    }

    /// <summary>
    /// ConvertBack is not implemented as this is a one-way conversion.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a double value to a formatted percentage string with one decimal place.
/// Displays values in the format "75.5%".
/// </summary>
public partial class PercentageConverter : IValueConverter
{
    /// <summary>
    /// Converts a double to a formatted percentage string.
    /// </summary>
    /// <param name="value">The percentage value as a double.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>Formatted percentage string with one decimal place, or "-" if invalid.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double percentage)
        {
            return $"{percentage:F1}%";
        }
        return "-";
    }

    /// <summary>
    /// ConvertBack is not implemented as this is a one-way conversion.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts numeric values to Visibility based on whether they are greater than zero.
/// Returns Visible if value > 0, Collapsed otherwise.
/// Useful for hiding UI elements when counts or sizes are zero.
/// </summary>
public partial class GreaterThanZeroToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a numeric value to Visibility based on whether it's greater than zero.
    /// </summary>
    /// <param name="value">The numeric value (ulong or uint).</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>Visibility.Visible if value > 0, Visibility.Collapsed otherwise.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ulong u && u > 0) return Microsoft.UI.Xaml.Visibility.Visible;
        if (value is uint ui && ui > 0) return Microsoft.UI.Xaml.Visibility.Visible;
        return Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    /// <summary>
    /// ConvertBack is not implemented as this is a one-way conversion.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a string to Visibility based on whether it is not null or empty.
/// Returns Visible if the string has content, Collapsed if null or empty.
/// Useful for conditionally showing labels or text blocks based on data availability.
/// </summary>
public partial class StringNotEmptyToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a string to Visibility based on whether it has content.
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language of the conversion (not used).</param>
    /// <returns>Visibility.Visible if string is not null or empty, Visibility.Collapsed otherwise.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string s && !string.IsNullOrEmpty(s)) return Microsoft.UI.Xaml.Visibility.Visible;
        return Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    /// <summary>
    /// ConvertBack is not implemented as this is a one-way conversion.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}


