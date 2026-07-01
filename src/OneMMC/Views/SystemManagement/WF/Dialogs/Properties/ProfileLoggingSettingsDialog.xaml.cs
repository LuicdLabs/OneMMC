using System;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.WFProperties;

public sealed partial class ProfileLoggingSettingsDialog : ContentDialog
{
    public FirewallLoggingSettings Settings { get; }
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ProfileLoggingSettingsDialog(string profileName, FirewallLoggingSettings settings)
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += ProfileLoggingSettingsDialog_Unloaded;
        PrimaryButtonClick += ProfileLoggingSettingsDialog_PrimaryButtonClick;

        Settings = settings;
        Title = string.Format(System.Globalization.CultureInfo.CurrentCulture, LocalizedStrings.WF_ProfileLogging_TitleFormat, profileName);
        LogFilePathTextBox.Text = settings.FileName;
        SizeLimitNumberBox.Value = settings.MaxFileSizeKilobytes;
        LogDroppedPacketsComboBox.SelectedIndex = settings.LogDroppedPackets ? 0 : 1;
        LogSuccessfulConnectionsComboBox.SelectedIndex = settings.LogSuccessfulConnections ? 0 : 1;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        string currentPath = LogFilePathTextBox.Text?.Trim() ?? string.Empty;
        string expandedCurrentPath = Environment.ExpandEnvironmentVariables(currentPath);
        string? suggestedDirectory = null;
        string suggestedFileName = "pfirewall.log";

        if (!string.IsNullOrWhiteSpace(expandedCurrentPath))
        {
            try
            {
                suggestedDirectory = System.IO.Directory.Exists(expandedCurrentPath)
                    ? expandedCurrentPath
                    : System.IO.Path.GetDirectoryName(expandedCurrentPath);

                string? currentFileName = System.IO.Path.GetFileName(expandedCurrentPath);
                if (!string.IsNullOrWhiteSpace(currentFileName))
                {
                    suggestedFileName = currentFileName;
                }
            }
            catch
            {
                suggestedDirectory = null;
            }
        }

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? selectedPath = await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().SaveFileAsync(
            hwnd,
            filter: GetLogFilesFilter(),
            initialDirectory: suggestedDirectory,
            defaultExtension: "log",
            suggestedFileName: suggestedFileName);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            LogFilePathTextBox.Text = selectedPath;
        }
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void ProfileLoggingSettingsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }

    private void ProfileLoggingSettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Settings.FileName = LogFilePathTextBox.Text.Trim();
        Settings.MaxFileSizeKilobytes = (int)SizeLimitNumberBox.Value;
        Settings.LogDroppedPackets = LogDroppedPacketsComboBox.SelectedIndex == 0;
        Settings.LogSuccessfulConnections = LogSuccessfulConnectionsComboBox.SelectedIndex == 0;
    }

    private string GetLogFilesFilter()
        => $"{LocalizedStrings.WF_FileDialog_LogFiles}\0*.log\0{LocalizedStrings.WF_FileDialog_AllFiles}\0*.*\0";
}
