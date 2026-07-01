using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using System;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class ExtendVolumeDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public ulong ExtendSizeInMB => (ulong)ExtendSizeNumberBox.Value;

    /// <summary>
    /// Creates an ExtendVolumeDialog with partition info and actual extendable space from WMI query.
    /// </summary>
    /// <param name="partition">The partition to extend</param>
    /// <param name="maxExtendSizeMB">Maximum extendable space in MB from QueryExtendableSpace</param>
    public ExtendVolumeDialog(PartitionInfo partition, ulong maxExtendSizeMB)
    {
        this.InitializeComponent();
        this.Closing += ExtendVolumeDialog_Closing;

        VolumeInfoTextBlock.Text = $"{partition.DriveLetter} - {FormatSize(partition.TotalSize)}";
        
        ExtendSizeNumberBox.Maximum = maxExtendSizeMB;
        ExtendSizeNumberBox.Value = Math.Min(1024, maxExtendSizeMB);
        MaxExtendTextBlock.Text = $"{maxExtendSizeMB:N0} MB";
        
        // Disable primary button if no extendable space
        if (maxExtendSizeMB == 0)
        {
            this.IsPrimaryButtonEnabled = false;
            MaxExtendTextBlock.Text = "No unallocated space available for extension";
        }
    }

    private void ExtendVolumeDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (ExtendSizeInMB == 0)
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

