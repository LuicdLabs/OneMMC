using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class FormatVolumeDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public string VolumeLabel => VolumeLabelTextBox.Text;
    public string FileSystem => (FileSystemComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NTFS";
    public bool QuickFormat => QuickFormatCheckBox.IsChecked ?? true;
    public bool EnableCompression => EnableCompressionCheckBox.IsChecked ?? false;

    public FormatVolumeDialog(PartitionInfo partition)
    {
        this.InitializeComponent();
        this.Closing += FormatVolumeDialog_Closing;

        DriveInfoTextBlock.Text = $"{partition.DriveLetter} ({partition.FileSystem})";
        VolumeLabelTextBox.Text = partition.VolumeLabel ?? "";
    }

    private void FormatVolumeDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            // Validation can be added here if needed
        }
    }
}

