using Microsoft.UI.Xaml.Data;
using System;

namespace OneMMC.Converters
{
    /// <summary>
    /// Converts a nullable boolean (bool?) to a non-nullable boolean (bool).
    /// Null values are converted to false.
    /// Supports two-way binding for checkbox scenarios with nullable backing properties.
    /// </summary>
    public class NullableBoolToBoolConverter : IValueConverter
    {
        /// <summary>
        /// Converts a nullable boolean to a non-nullable boolean.
        /// </summary>
        /// <param name="value">The nullable boolean value.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="language">The language of the conversion (not used).</param>
        /// <returns>The boolean value, or false if null or not a boolean.</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b;
            }
            return false;
        }

        /// <summary>
        /// Converts a boolean back to its original form (passes through).
        /// </summary>
        /// <param name="value">The boolean value to convert back.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="language">The language of the conversion (not used).</param>
        /// <returns>The original value.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
}
