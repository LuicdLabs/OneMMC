using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OneMMC.Core.Features.PCManagement.Services.DevMgmt;
using OneMMC.Core.Features.PCManagement.ViewModels.DevMgmt;
using OneMMC.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace OneMMC.Views
{
    /// <summary>
    /// Device Manager Page
    /// </summary>
    public sealed partial class DeviceManagerPage : Page
    {
        public OneMMC.Localization.LocalizedStrings LocalizedStrings { get; } = OneMMC.Localization.LocalizedStrings.Instance;
        public DeviceManagerViewModel ViewModel { get; }

        public DeviceManagerPage()
        {
            ViewModel = App.GetRequiredService<DeviceManagerViewModel>();
            this.InitializeComponent();
            this.Loaded += DeviceManagerPage_Loaded;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            this.Unloaded += (_, _) =>
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.ClearCachedData();
                DataContext = null;
                this.Loaded -= DeviceManagerPage_Loaded;
            };
        }

        private async void DeviceManagerPage_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadDevicesAsync();
            UpdateDeviceCount();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Merged: Refresh and Scan Hardware Changes
            await ViewModel.ScanForHardwareChangesAsync();
        }

        private async void PullToRefresh_RefreshRequested(Microsoft.UI.Xaml.Controls.RefreshContainer sender, Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs args)
        {
            var def = args.GetDeferral();
            try
            {
                await ViewModel.ScanForHardwareChangesAsync();
            }
            finally
            {
                def.Complete();
            }
        }

        private void OpenDevMgmtButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenDeviceManager();
        }

        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CommunityToolkit.WinUI.Controls.SettingsCard card && card.Tag is DeviceInfo device)
            {
                // Add breadcrumb and navigate to device details page
                BreadcrumbNavigationService.AddBreadcrumb(
                    device.Name ?? LocalizedStrings.DeviceManager_Unknown,
                    typeof(DeviceDetailsPage),
                    device);
                Frame.Navigate(typeof(DeviceDetailsPage), device, new SlideNavigationTransitionInfo()
                {
			        Effect = SlideNavigationTransitionEffect.FromRight
		        });
            }
        }

        private void UpdateDeviceCount()
        {
            if (ViewModel.DeviceCategories != null)
            {
                int count = ViewModel.DeviceCategories.Sum(c => c.DeviceCount);
                DeviceCountText.Text = string.Format(LocalizedStrings.DeviceManager_DeviceCountFormat, count);
            }
            else
            {
                DeviceCountText.Text = LocalizedStrings.DeviceManager_DeviceCountZero;
            }
        }


        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.DeviceCategories))
            {
                UpdateDeviceCount();
            }
        }
    }

    // Value Converters
    public partial class BoolToGlyphConverter : IValueConverter
    {
        public string TrueValue { get; set; } = string.Empty;
        public string FalseValue { get; set; } = string.Empty;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }
            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public partial class BoolToColorConverter : IValueConverter
    {
        public string TrueValue { get; set; } = string.Empty;
        public string FalseValue { get; set; } = string.Empty;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                var colorString = boolValue ? TrueValue : FalseValue;
                return new SolidColorBrush(
                    Windows.UI.Color.FromArgb(
                        byte.Parse(colorString.Substring(1, 2), System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(colorString.Substring(3, 2), System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(colorString.Substring(5, 2), System.Globalization.NumberStyles.HexNumber),
                        byte.Parse(colorString.Substring(7, 2), System.Globalization.NumberStyles.HexNumber)
                    )
                );
            }
            return new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public partial class DeviceCountFormatter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int count)
            {
                var localizedStrings = OneMMC.Localization.LocalizedStrings.Instance;
                return string.Format(localizedStrings.DeviceManager_DeviceCountFormat, count);
            }
            return value?.ToString() ?? "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public partial class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public partial class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
