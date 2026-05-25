using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class ManageDriveLetterDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public string? SelectedDriveLetter => (DriveLetterComboBox.SelectedItem as string)?.Replace(":", "");

    private readonly PartitionInfo? _partition;
    private readonly string? _currentDriveLetter;

    // Constructor for Partition
    public ManageDriveLetterDialog(PartitionInfo partition, List<string> availableLetters)
    {
        this.InitializeComponent();
        this.Closing += ManageDriveLetterDialog_Closing;

        _partition = partition;
        
        SetupUI(availableLetters);
    }

    // Constructor for CD-ROM (simple string-based)
    public ManageDriveLetterDialog(string currentDriveLetter)
    {
        this.InitializeComponent();
        this.Closing += ManageDriveLetterDialog_Closing;

        _currentDriveLetter = currentDriveLetter?.TrimEnd(':') ?? string.Empty;
        
        // Get available drive letters
        var usedLetters = System.IO.DriveInfo.GetDrives()
            .Select(d => d.Name.TrimEnd('\\').TrimEnd(':').ToUpper())
            .ToHashSet();

        var availableLetters = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            if (!usedLetters.Contains(c.ToString()) || c.ToString() == _currentDriveLetter)
            {
                availableLetters.Add($"{c}:");
            }
        }

        SetupUIForString(availableLetters);
    }

    private void SetupUI(List<string> availableLetters)
    {
        if (_partition == null) return;

        bool hasCurrentLetter = !string.IsNullOrEmpty(_partition.DriveLetter);
        
        if (hasCurrentLetter)
        {
            CurrentDriveLetterTextBlock.Text = $"{_partition.DriveLetter}";
            CurrentLetterPanel.Visibility = Visibility.Visible;
        }
        else
        {
            CurrentLetterPanel.Visibility = Visibility.Collapsed;
        }

        DriveLetterComboBox.ItemsSource = availableLetters;
        
        if (hasCurrentLetter && availableLetters.Contains($"{_partition.DriveLetter}:"))
        {
            DriveLetterComboBox.SelectedItem = $"{_partition.DriveLetter}:";
        }
    }

    private void SetupUIForString(List<string> availableLetters)
    {
        if (!string.IsNullOrEmpty(_currentDriveLetter))
        {
            CurrentDriveLetterTextBlock.Text = $"{_currentDriveLetter}:";
            CurrentLetterPanel.Visibility = Visibility.Visible;
        }
        else
        {
            CurrentLetterPanel.Visibility = Visibility.Collapsed;
        }

        DriveLetterComboBox.ItemsSource = availableLetters;
        
        if (!string.IsNullOrEmpty(_currentDriveLetter) && availableLetters.Contains($"{_currentDriveLetter}:"))
        {
            DriveLetterComboBox.SelectedItem = $"{_currentDriveLetter}:";
        }
        else if (availableLetters.Count > 0)
        {
            DriveLetterComboBox.SelectedIndex = 0;
        }
    }

    private void ManageDriveLetterDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (DriveLetterComboBox.SelectedItem == null)
            {
                args.Cancel = true;
            }
        }
    }
}
