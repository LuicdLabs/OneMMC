using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class CleanDiskDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public bool IsConfirmed => ConfirmCleanCheckBox.IsChecked ?? false;

    public CleanDiskDialog(PhysicalDiskInfo disk)
    {
        this.InitializeComponent();
        this.Closing += CleanDiskDialog_Closing;

        DiskInfoTextBlock.Text = $"Disk {disk.Index} - {FormatSize(disk.Size)}";
    }

    private void CleanDiskDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
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

