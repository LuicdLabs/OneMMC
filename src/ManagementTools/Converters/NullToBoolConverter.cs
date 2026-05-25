using System;
using Microsoft.UI.Xaml.Data;

namespace ManagementTools.Converters
{
    /// <summary>
    /// Converts any object to a boolean based on whether it is null.
    /// Returns true if the value is not null, false if it is null.
    /// Useful for enabling/disabling controls based on whether data is loaded.
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        /// <summary>
        /// Converts an object to a boolean based on null check.
        /// </summary>
        /// <param name="value">The object to check for null.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="language">The language of the conversion (not used).</param>
        /// <returns>True if value is not null, false if null.</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value != null;
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
