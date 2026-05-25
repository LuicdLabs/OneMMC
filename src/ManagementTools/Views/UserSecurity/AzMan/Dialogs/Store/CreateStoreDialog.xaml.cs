// ============================================================================
// CreateStoreDialog.xaml.cs
// 
// Create New Authorization Store Dialog - Code-behind
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using System;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Create New Authorization Store Dialog
/// </summary>
public sealed partial class CreateStoreDialog : ContentDialog
{
    /// <summary>
    /// Create result - Create parameters
    /// </summary>
    public CreateStoreParameters? Result { get; private set; }

    /// <summary>
    /// Localized strings
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Currently selected store type
    /// </summary>
    private AzStoreType _currentStoreType = AzStoreType.Xml;

    public CreateStoreDialog()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
    }

    /// <summary>
    /// Update UI when store type changes
    /// </summary>
    private void OnStoreTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        // Check if panels are initialized (avoid NullReferenceException during InitializeComponent)
        if (XmlPathPanel == null || AdPathPanel == null || SqlPathPanel == null)
            return;

        if (StoreTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentStoreType = tag switch
            {
                "Xml" => AzStoreType.Xml,
                "ActiveDirectory" => AzStoreType.ActiveDirectory,
                "SqlServer" => AzStoreType.SqlServer,
                _ => AzStoreType.Xml
            };

            // Update visibility
            XmlPathPanel.Visibility = _currentStoreType == AzStoreType.Xml ? Visibility.Visible : Visibility.Collapsed;
            AdPathPanel.Visibility = _currentStoreType == AzStoreType.ActiveDirectory ? Visibility.Visible : Visibility.Collapsed;
            SqlPathPanel.Visibility = _currentStoreType == AzStoreType.SqlServer ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Browse XML file path
    /// </summary>
    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var result = await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().SaveFileAsync(
            hwnd,
            "XML Files\0*.xml\0All Files\0*.*\0",
            LocalizedStrings.CreateStoreDialog_FileDialog_Title,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "xml");

        if (!string.IsNullOrEmpty(result))
        {
            var rawResult = result;

            // Ensure .xml extension
            if (!result.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                result += ".xml";
            }

            App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().CleanupPlaceholderFile(rawResult);
            App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().CleanupPlaceholderFile(result);
            XmlPathTextBox.Text = result;
        }
    }

    /// <summary>
    /// Primary button click - Validate and create
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Validate input
        string path = GetStorePath();
        
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowError(LocalizedStrings.CreateStoreDialog_Error_EmptyPath);
            args.Cancel = true;
            return;
        }

        // Validate XML path
        if (_currentStoreType == AzStoreType.Xml)
        {
            if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                path += ".xml";
            }

            // Check if directory exists
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                ShowError(string.Format(LocalizedStrings.CreateStoreDialog_Error_DirectoryMissing, directory));
                args.Cancel = true;
                return;
            }

            // Check if file already exists
            if (System.IO.File.Exists(path))
            {
                ShowError(string.Format(LocalizedStrings.CreateStoreDialog_Error_FileExists, path));
                args.Cancel = true;
                return;
            }
        }

        // Create result
        Result = new CreateStoreParameters
        {
            StoreType = _currentStoreType,
            Path = path,
            Description = DescriptionTextBox.Text.Trim(),
            GenerateAudits = false
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

