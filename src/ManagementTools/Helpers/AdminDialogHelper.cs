using System;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Helpers
{
    /// <summary>
    /// Provides unified UI helpers for displaying administrator privilege notifications.
    /// All admin-related ContentDialog and InfoBar interactions should use this helper
    /// to ensure consistent appearance and behavior across the application.
    /// </summary>
    public static class AdminDialogHelper
    {
        private static readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;
        private static bool _isDialogOpen;

        /// <summary>
        /// Shows a unified administrator-required ContentDialog (informational only, with OK button).
        /// Use this when an operation cannot proceed without elevation.
        /// </summary>
        /// <param name="xamlRoot">The XamlRoot for the dialog.</param>
        public static async System.Threading.Tasks.Task ShowAdminRequiredDialogAsync(XamlRoot xamlRoot)
        {
            if (_isDialogOpen) return;

            _isDialogOpen = true;
            try
            {
                var dialog = new ContentDialog
                {
                    Title = _localizedStrings.Common_AdminRequired_Title,
                    Content = _localizedStrings.Common_AdminRequired_Message,
                    CloseButtonText = _localizedStrings.Common_AdminRequired_Close,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = xamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    RequestedTheme = App.CurrentTheme
                };

                await dialog.ShowAsync();
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
            {
                // ContentDialog already open ??silently ignore
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        /// <summary> 
        /// Attempts to restart the application with administrator privileges. 
        /// Returns <c>true</c> if the restart request was successfully initiated; 
        /// returns <c>false</c> if elevation was denied or the operation failed. 
        /// </summary> 
        /// <returns><c>true</c> if the application attempted to restart as administrator; 
        /// otherwise, <c>false</c>.</returns>
        public static bool RunasAdmin()
        {
            try
            {
                var adminService = App.GetRequiredService<IAdminService>();
                adminService.RestartAsAdmin();
                return true;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
            {
                return false;
            }
        }

        /// <summary>
        /// Configures an <see cref="InfoBar"/> with the standard admin-required warning appearance.
        /// </summary>
        /// <param name="infoBar">The InfoBar to configure.</param>
        /// <param name="customMessage">Optional custom message. If null, uses the default admin-required InfoBar message.</param>
        public static void ConfigureAdminInfoBar(InfoBar infoBar, string? customMessage = null)
        {
            infoBar.Severity = InfoBarSeverity.Warning;
            infoBar.Title = _localizedStrings.Common_AdminRequired_Title;
            infoBar.Message = customMessage ?? _localizedStrings.Common_AdminRequired_InfoBarMessage;
            infoBar.IsClosable = false;
            infoBar.IsOpen = true;
        }
    }
}
