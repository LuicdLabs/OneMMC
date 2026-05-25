using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using System;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class ShrinkVolumeDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public ulong ShrinkSizeInMB => (ulong)ShrinkSizeNumberBox.Value;

    /// <summary>
    /// Creates a ShrinkVolumeDialog with partition info and actual shrinkable space from WMI query.
    /// </summary>
    /// <param name="partition">The partition to shrink</param>
    /// <param name="shrinkableSpaceMB">Actual shrinkable space in MB from QueryShrinkableSpace</param>
    public ShrinkVolumeDialog(PartitionInfo partition, ulong shrinkableSpaceMB)
    {
        this.InitializeComponent();
        this.Closing += ShrinkVolumeDialog_Closing;

        VolumeInfoTextBlock.Text = $"{partition.DriveLetter}";
        CurrentSizeTextBlock.Text = FormatSize(partition.TotalSize);
        
        AvailableShrinkTextBlock.Text = $"{shrinkableSpaceMB:N0} MB";
        ShrinkSizeNumberBox.Maximum = shrinkableSpaceMB;
        ShrinkSizeNumberBox.Value = Math.Min(1024, shrinkableSpaceMB);
        
        // Disable primary button if no shrinkable space
        if (shrinkableSpaceMB == 0)
        {
            this.IsPrimaryButtonEnabled = false;
            AvailableShrinkTextBlock.Text = "0 MB (No shrinkable space available)";
        }
    }

    private void ShrinkVolumeDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (ShrinkSizeInMB == 0)
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

