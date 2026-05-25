using System;
using Microsoft.UI.Xaml.Data;

namespace ManagementTools.Converters
{
    /// <summary>
    /// Converts a string to a boolean: returns true when the input string is not null or empty.
    /// Useful for enabling/disabling controls based on whether text input has content.
    /// </summary>
    public class StringToBoolConverter : IValueConverter
    {
        /// <summary>
        /// Converts a string to a boolean based on whether it has content.
        /// </summary>
        /// <param name="value">The string value to check.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="language">The language of the conversion (not used).</param>
        /// <returns>True if string is not null or empty, false otherwise.</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        /// <summary>
        /// ConvertBack is not implemented as this is a one-way conversion.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
