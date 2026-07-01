using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class RemoveDriveLetterDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public RemoveDriveLetterDialog(PartitionInfo partition)
    {
        InitializeComponent();
        MessageTextBlock.Text = string.Format(LocalizedStrings.DiskMgmt_RemoveDriveLetterConfirm, partition.DriveLetter);
    }
}

