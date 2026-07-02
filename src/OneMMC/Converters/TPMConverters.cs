using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using OneMMC.Core.Features.SystemManagement.ViewModels.TPM;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Converters;

public partial class HexToSolidColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && hex.Length == 7 && hex.StartsWith("#"))
        {
            byte r = System.Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(5, 2), 16);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public partial class TpmSeverityToInfoBarSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TpmStatusSeverity severity)
        {
            return severity switch
            {
                TpmStatusSeverity.Success => InfoBarSeverity.Success,
                TpmStatusSeverity.Warning => InfoBarSeverity.Warning,
                TpmStatusSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };
        }
        return InfoBarSeverity.Informational;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
