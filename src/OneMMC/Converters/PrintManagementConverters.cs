using System;
using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using OneMMC.Core.Features.PrintManagement.Models.PrintManagement;
using OneMMC.Localization;

namespace OneMMC.Converters;

/// <summary>
/// Converter for formatting print form size display.
/// </summary>
public partial class PrintFormSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is PrintFormInfo form)
        {
            return string.Format(
                LocalizedStrings.Instance.PrintMgmt_FormSizeFormat,
                form.PrintableWidth,
                form.PrintableHeight);
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter for formatting print port printer display.
/// </summary>
public partial class PrintPortPrinterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string printerNames)
        {
            return string.IsNullOrEmpty(printerNames)
                ? LocalizedStrings.Instance.PrintMgmt_NoPrinterAssigned
                : string.Format(LocalizedStrings.Instance.PrintMgmt_PortPrinterFormat, printerNames);
        }
        return LocalizedStrings.Instance.PrintMgmt_NoPrinterAssigned;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter that returns Visible when collection count is 0, Collapsed otherwise.
/// </summary>
public partial class CollectionEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ICollection collection)
        {
            return collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter that returns Collapsed when collection count is 0, Visible otherwise.
/// </summary>
public partial class CollectionNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ICollection collection)
        {
            return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter for GPO printer deployment connection type display.
/// </summary>
public partial class GpoPrinterConnectionTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is GpoPrinterDeploymentScope scope)
        {
            return scope == GpoPrinterDeploymentScope.PerUser
                ? LocalizedStrings.Instance.PrintMgmt_DeployDialogConnectionTypePerUser
                : LocalizedStrings.Instance.PrintMgmt_DeployDialogConnectionTypePerMachine;
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

