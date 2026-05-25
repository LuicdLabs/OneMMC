// ============================================================================
// OpenStoreDialog.xaml.cs
// 
// Open Existing Authorization Store Dialog - Code-behind
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using System;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Open Existing Authorization Store Dialog
/// </summary>
public sealed partial class OpenStoreDialog : ContentDialog
{
    /// <summary>
    /// Localized strings
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Open result - Open parameters
    /// </summary>
    public OpenStoreParameters? Result { get; private set; }

    /// <summary>
    /// Currently selected store type
    /// </summary>
    private AzStoreType _currentStoreType = AzStoreType.Xml;

    public OpenStoreDialog()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
    }

    /// <summary>
    /// Update UI when store type changes
    /// </summary>
    private void OnStoreTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StoreTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentStoreType = tag switch
            {
                "Xml" => AzStoreType.Xml,
                "ActiveDirectory" => AzStoreType.ActiveDirectory,
                "SqlServer" => AzStoreType.SqlServer,
                _ => AzStoreType.Xml
            };

            // Only update visibility after controls are initialized
            if (XmlPathPanel != null && AdPathPanel != null && SqlPathPanel != null)
            {
                XmlPathPanel.Visibility = _currentStoreType == AzStoreType.Xml ? Visibility.Visible : Visibility.Collapsed;
                AdPathPanel.Visibility = _currentStoreType == AzStoreType.ActiveDirectory ? Visibility.Visible : Visibility.Collapsed;
                SqlPathPanel.Visibility = _currentStoreType == AzStoreType.SqlServer ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Browse XML file
    /// </summary>
    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var result = await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            "XML Files\0*.xml\0All Files\0*.*\0",
            LocalizedStrings.OpenStoreDialog_FileDialog_Title,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        if (!string.IsNullOrEmpty(result))
        {
            XmlPathTextBox.Text = result;
        }
    }

    /// <summary>
    /// Primary button click - Validate and open
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string path = GetStorePath();

        if (string.IsNullOrWhiteSpace(path))
        {
            ShowError(LocalizedStrings.OpenStoreDialog_Error_InvalidPath);
            args.Cancel = true;
            return;
        }

        // Validate XML file exists
        if (_currentStoreType == AzStoreType.Xml && !System.IO.File.Exists(path))
        {
            ShowError(string.Format(LocalizedStrings.OpenStoreDialog_Error_FileNotFound, path));
            args.Cancel = true;
            return;
        }

        // Get open mode
        bool readOnly = false;
        if (OpenModeRadio.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            readOnly = tag == "ReadOnly";
        }

        Result = new OpenStoreParameters
        {
            StoreType = _currentStoreType,
            Path = path,
            ReadOnly = readOnly
        };

        ErrorInfoBar.IsOpen = false;
    }

    /// <summary>
    /// Get store path
    /// </summary>
    private string GetStorePath()
    {
        return _currentStoreType switch
        {
            AzStoreType.Xml => XmlPathTextBox.Text.Trim(),
            AzStoreType.ActiveDirectory => AdPathTextBox.Text.Trim(),
            AzStoreType.SqlServer => BuildSqlPath(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Build SQL Server path
    /// </summary>
    private string BuildSqlPath()
    {
        string server = SqlServerTextBox.Text.Trim();
        string database = SqlDatabaseTextBox.Text.Trim();
        string storeName = SqlStoreNameTextBox.Text.Trim();

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(database) || string.IsNullOrEmpty(storeName))
        {
            return string.Empty;
        }

        return $"{server}/{database}/{storeName}";
    }

    /// <summary>
    /// Show error message
    /// </summary>
    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }
}

