using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ManagementTools.Models;
using ManagementTools.Localization;
using ManagementTools.ViewModels;
using ManagementTools.Views.Settings;
using CommunityToolkit.Mvvm.Input;

namespace ManagementTools.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
        public ObservableCollection<SettingItem> SettingsItems { get; set; } = new();
        private readonly SettingsViewModel _viewModel;
        private bool _isLegalDialogOpen;

        public SettingsPage()
        {
            _viewModel = App.GetRequiredService<SettingsViewModel>();
            InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
            this.RequestedTheme = App.CurrentTheme;
            App.ThemeChanged += OnThemeChanged;
            this.Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.OnNavigateRequest = (index) => { /* Settings items don't navigate */ };
            
            // Handle localization at UI layer, convert data from ViewModel to UI's SettingItem
            foreach (var data in _viewModel.SettingsData)
            {
                SettingsItems.Add(new SettingItem 
                { 
                    Glyph = data.Glyph, 
                    TitleKey = data.TitleKey, 
                    SubtitleKey = data.SubtitleKey,
                    Command = new RelayCommand(() => { /* Theme setting handled separately */ })
                });
            }
            
            this.DataContext = this;
        }

        private void OnThemeChanged(Microsoft.UI.Xaml.ElementTheme theme)
        {
            this.RequestedTheme = theme;
        }

        private void SendFeedback_Click(object sender, RoutedEventArgs e)
        {
            if (IsShiftPressed())
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ManagementTools",
                    "Logs");
                _ = Process.Start("explorer.exe", logFolder);
                return;
            }

            var feedbackUrl = "https://github.com/LuicdLabs/ManagementTools/issues";
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(feedbackUrl));
        }

        private async void LicenseTerms_Click(object sender, RoutedEventArgs e)
        {
            if (_isLegalDialogOpen) return;
            _isLegalDialogOpen = true;
            try
            {
                var dialog = new LegalDocumentDialog(
                    title: LocalizedStrings.Settings_About_LicenseTerms,
                    resourcePath: "ms-appx:///LegalDocs/LicenseTerms.txt",
                    xamlRoot: this.XamlRoot,
                    requestedTheme: App.CurrentTheme);
                await dialog.ShowAsync();
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
            {
                // Another ContentDialog is already open app-wide — silently ignore.
            }
            finally
            {
                _isLegalDialogOpen = false;
            }
        }

        private async void ThirdPartyNotices_Click(object sender, RoutedEventArgs e)
        {
            if (_isLegalDialogOpen) return;
            _isLegalDialogOpen = true;
            try
            {
                var dialog = new LegalDocumentDialog(
                    title: LocalizedStrings.Settings_About_ThirdPartyNotices,
                    resourcePath: "ms-appx:///LegalDocs/ThirdPartyNotices.txt",
                    xamlRoot: this.XamlRoot,
                    requestedTheme: App.CurrentTheme);
                await dialog.ShowAsync();
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
            {
                // Another ContentDialog is already open app-wide — silently ignore.
            }
            finally
            {
                _isLegalDialogOpen = false;
            }
        }

        private async void PrivacyStatement_Click(object sender, RoutedEventArgs e)
        {
            if (_isLegalDialogOpen) return;
            _isLegalDialogOpen = true;
            try
            {
                var dialog = new LegalDocumentDialog(
                    title: LocalizedStrings.Settings_About_PrivacyStatement,
                    resourcePath: "ms-appx:///LegalDocs/PrivacyStatement.txt",
                    xamlRoot: this.XamlRoot,
                    requestedTheme: App.CurrentTheme);
                await dialog.ShowAsync();
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
            {
                // Another ContentDialog is already open app-wide — silently ignore.
            }
            finally
            {
                _isLegalDialogOpen = false;
            }
        }

        public SettingsViewModel ViewModel => _viewModel;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsShiftPressed()
        {
            return (GetAsyncKeyState(0xA0) & 0x8000) != 0 || (GetAsyncKeyState(0xA1) & 0x8000) != 0;
        }
    }
}
