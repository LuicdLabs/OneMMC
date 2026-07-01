using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class CreateSimpleVolumeDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    private const ulong MinimumVolumeSizeMB = 1; // Minimum 1MB partition size
    
    public ulong VolumeSizeInMB => (ulong)VolumeSizeNumberBox.Value;
    public string? SelectedDriveLetter => (DriveLetterComboBox.SelectedItem as string)?.Replace(":", "");
    public string VolumeLabel => VolumeLabelTextBox.Text;
    public string FileSystem => (FileSystemComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NTFS";
    public bool QuickFormat => QuickFormatCheckBox.IsChecked ?? true;

    public CreateSimpleVolumeDialog(PhysicalDiskInfo disk, List<string> availableLetters, ulong? maxSpaceOverrideBytes = null)
    {
        this.InitializeComponent();
        this.Closing += CreateSimpleVolumeDialog_Closing;

        DiskInfoTextBlock.Text = $"Disk {disk.Index} - {DiskFormatHelper.FormatSize(disk.Size)}";
        
        ulong unallocatedSpace;
        if (maxSpaceOverrideBytes.HasValue)
        {
            unallocatedSpace = maxSpaceOverrideBytes.Value;
        }
        else
        {
            // Calculate unallocated space from partitions (ignoring unallocated placeholders)
            ulong usedSpace = disk.PartitionInfos
                .Where(p => !p.IsUnallocated)
                .Aggregate(0UL, (sum, p) => sum + (p.TotalSize > 0 ? p.TotalSize : p.Size));
            unallocatedSpace = disk.Size > usedSpace ? disk.Size - usedSpace : 0;
        }

        ulong maxSizeMB = unallocatedSpace / (1024 * 1024);
        VolumeSizeNumberBox.Minimum = MinimumVolumeSizeMB;
        VolumeSizeNumberBox.Maximum = maxSizeMB;
        VolumeSizeNumberBox.Value = maxSizeMB;
        MaxSizeTextBlock.Text = $"Maximum size: {maxSizeMB} MB (Minimum: {MinimumVolumeSizeMB} MB)";

        foreach (var letter in availableLetters)
        {
            DriveLetterComboBox.Items.Add(letter);
        }

        if (DriveLetterComboBox.Items.Count > 0)
        {
            DriveLetterComboBox.SelectedIndex = 0;
        }
    }

    private void CreateSimpleVolumeDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            // Validate minimum partition size (1MB)
            if (VolumeSizeInMB < MinimumVolumeSizeMB)
            {
                args.Cancel = true;
                return;
            }

            // Validate drive letter selection
            if (DriveLetterComboBox.SelectedItem == null)
            {
                args.Cancel = true;
                return;
            }
        }
    }

    private static string FormatSize(ulong bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

