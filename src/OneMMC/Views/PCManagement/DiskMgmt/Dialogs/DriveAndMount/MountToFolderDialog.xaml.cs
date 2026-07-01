using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class MountToFolderDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public string MountPath => MountPathTextBox.Text;

    public MountToFolderDialog(PartitionInfo partition)
    {
        this.InitializeComponent();
        this.Closing += MountToFolderDialog_Closing;

        VolumeInfoTextBlock.Text = $"{partition.DriveLetter}";
    }

    private void MountToFolderDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(MountPath))
            {
                args.Cancel = true;
                return;
            }
        }
    }
}

