using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ManagementTools.Core.Features.PCManagement.ViewModels.DevMgmt;
using ManagementTools.Helpers;
using ManagementTools.Core.Features.PCManagement.Services.DevMgmt;

namespace ManagementTools.Views
{
    public sealed partial class DeviceDetailsPage : Page
    {
        public ManagementTools.Localization.LocalizedStrings LocalizedStrings { get; } = ManagementTools.Localization.LocalizedStrings.Instance;
        public DeviceManagerViewModel ViewModel { get; private set; } = null!;
        private DeviceInfo _device = null!;

        public DeviceDetailsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is DeviceInfo device)
            {
                ViewModel = App.GetRequiredService<DeviceManagerViewModel>();
                _device = device;
                ViewModel.SelectedDevice = device;

                UpdateDeviceDetails(device);

                // Enable/disable action buttons
                EnableDeviceButton.IsEnabled = !device.IsEnabled;
                DisableDeviceButton.IsEnabled = device.IsEnabled;
                UninstallDeviceButton.IsEnabled = true;

                // Subscribe to property changes
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedDeviceProperties))
            {
                if (ViewModel.SelectedDeviceProperties != null)
                {
                    UpdateDriverInfo(ViewModel.SelectedDeviceProperties);
                }
                else
                {
                    ClearDriverInfo();
                }
            }
        }

        private void UpdateDeviceDetails(DeviceInfo device)
        {
            // Update general info
            DeviceNameDetail.Text = device.Name ?? LocalizedStrings.DeviceManager_Unknown;
            DeviceClassDetail.Text = device.ClassName ?? LocalizedStrings.DeviceManager_Unknown;
            ManufacturerDetail.Text = device.Manufacturer ?? LocalizedStrings.DeviceManager_Unknown;
            StatusDetail.Text = device.StatusDescription ?? LocalizedStrings.DeviceManager_Unknown;
            DeviceIdDetail.Text = device.DeviceId ?? LocalizedStrings.DeviceManager_Unknown;
            

            // Update driver info when properties are loaded
            if (ViewModel.SelectedDeviceProperties != null)
            {
                UpdateDriverInfo(ViewModel.SelectedDeviceProperties);
            }
            else
            {
                ClearDriverInfo();
            }
        }

        private void UpdateDriverInfo(DeviceProperties properties)
        {
            if (properties.DriverInfo != null)
            {
                DriverVersionDetail.Text = properties.DriverInfo.DriverVersion ?? LocalizedStrings.DeviceManager_Unknown;
                DriverDateDetail.Text = properties.DriverInfo.DriverDate ?? LocalizedStrings.DeviceManager_Unknown;
                DriverProviderDetail.Text = properties.DriverInfo.DriverProviderName ?? LocalizedStrings.DeviceManager_Unknown;
                InfNameDetail.Text = properties.DriverInfo.InfName ?? LocalizedStrings.DeviceManager_Unknown;
                IsSignedDetail.Text = properties.DriverInfo.IsSigned ? LocalizedStrings.DeviceManager_BooleanYes : LocalizedStrings.DeviceManager_BooleanNo;
                SignerDetail.Text = properties.DriverInfo.Signer ?? LocalizedStrings.DeviceManager_Unknown;
            }
            else
            {
                ClearDriverInfo();
            }

            // Update hardware IDs
            HardwareIdsList.ItemsSource = properties.HardwareIds;

            // Update compatible IDs
            CompatibleIdsList.ItemsSource = properties.CompatibleIds;

            
        }

        private void ClearDriverInfo()
        {
            DriverVersionDetail.Text = string.Empty;
            DriverDateDetail.Text = string.Empty;
            DriverProviderDetail.Text = string.Empty;
            InfNameDetail.Text = string.Empty;
            IsSignedDetail.Text = string.Empty;
            SignerDetail.Text = string.Empty;
            
        }

        private async void EnableDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedDevice == null) return;

            // Check admin privileges first
            var adminService = App.GetRequiredService<IAdminService>();
            if (!adminService.IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
                return;
            }

            var success = await ViewModel.EnableDeviceAsync();
            if (success)
            {
                EnableDeviceButton.IsEnabled = false;
                DisableDeviceButton.IsEnabled = true;
            }
            else
            {
                var errorDialog = new ContentDialog
                {
                    Title = LocalizedStrings.Common_ErrorTitle,
                    Content = LocalizedStrings.DeviceManager_EnableDeviceError,
                    CloseButtonText = LocalizedStrings.Common_OKButton,
                    XamlRoot = this.XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    RequestedTheme = App.CurrentTheme
                };
                await errorDialog.ShowAsync();
            }
        }

        private async void DisableDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedDevice == null) return;

            // Check admin privileges first
            var adminService = App.GetRequiredService<IAdminService>();
            if (!adminService.IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.DeviceManager_ConfirmDisableDeviceTitle,
                Content = string.Format(LocalizedStrings.DeviceManager_ConfirmDisableDeviceContent, ViewModel.SelectedDevice.Name),
                PrimaryButtonText = LocalizedStrings.DeviceManager_DisableButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await ViewModel.DisableDeviceAsync();
                if (success)
                {
                    EnableDeviceButton.IsEnabled = true;
                    DisableDeviceButton.IsEnabled = false;
                }
                else
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = LocalizedStrings.Common_ErrorTitle,
                        Content = LocalizedStrings.DeviceManager_DisableDeviceError,
                        CloseButtonText = LocalizedStrings.Common_OKButton,
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = App.CurrentTheme
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }

        private async void UninstallDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedDevice == null) return;

            // Check admin privileges first
            var adminService = App.GetRequiredService<IAdminService>();
            if (!adminService.IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.DeviceManager_ConfirmUninstallDeviceTitle,
                Content = string.Format(LocalizedStrings.DeviceManager_ConfirmUninstallDeviceContent, ViewModel.SelectedDevice.Name),
                PrimaryButtonText = LocalizedStrings.DeviceManager_UninstallButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await ViewModel.UninstallDeviceAsync();
                if (success)
                {
                    // Device uninstalled successfully, go back to device list
                    if (Frame.CanGoBack)
                    {
                        Frame.GoBack();
                    }
                }
                else
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = LocalizedStrings.Common_ErrorTitle,
                        Content = LocalizedStrings.DeviceManager_UninstallDeviceError,
                        CloseButtonText = LocalizedStrings.Common_OKButton,
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = App.CurrentTheme
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }

    }
}
