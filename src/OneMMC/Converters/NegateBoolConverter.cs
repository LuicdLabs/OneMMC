using Microsoft.UI.Xaml.Data;
using System;

namespace OneMMC.Converters
{
    /// <summary>
    /// Inverts a boolean value (true becomes false, false becomes true).
    /// Useful for binding scenarios where you need the opposite of a boolean property,
    /// such as disabling a button when something is enabled.
    /// </summary>
    public partial class NegateBoolConverter : IValueConverter
    {
        /// <summary>
        /// Converts a boolean value to its negated form.
        /// </summary>
        /// <param name="value">The boolean value to negate.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="language">The language of the conversion (not used).</param>
        /// <returns>The negated boolean value, or false if input is not a boolean.</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        /// <summary>
        /// ConvertBack is not implemented as negation is symmetric.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
