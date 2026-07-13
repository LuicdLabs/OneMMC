using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace OneMMC.Converters
{
    /// <summary>
    /// Converts any object to Visibility based on whether it is null.
    /// Returns Visible if the value is not null, Collapsed if it is null.
    /// Commonly used to show/hide UI elements based on data availability.
    /// </summary>
    public partial class NullToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Converts an object to Visibility based on null check.
        /// </summary>
        /// <param name="value">The object to check for null.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">Optional parameter (not used).</param>
        /// <param name="language">The language of the conversion (not used).</param>
        /// <returns>Visibility.Visible if value is not null, Visibility.Collapsed if null.</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
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
