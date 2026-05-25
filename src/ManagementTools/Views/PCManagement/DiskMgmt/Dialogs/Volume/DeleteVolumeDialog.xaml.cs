using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class DeleteVolumeDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public bool IsConfirmed => ConfirmDeleteCheckBox.IsChecked ?? false;

    public DeleteVolumeDialog(PartitionInfo partition)
    {
        this.InitializeComponent();
        this.Closing += DeleteVolumeDialog_Closing;

        VolumeInfoTextBlock.Text = $"{partition.DriveLetter} ({partition.VolumeLabel}) - {FormatSize(partition.TotalSize)}";
    }

    private void DeleteVolumeDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (!IsConfirmed)
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

