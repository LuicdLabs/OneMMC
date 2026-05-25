using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class RemoveDriveLetterDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public RemoveDriveLetterDialog(PartitionInfo partition)
    {
        InitializeComponent();
        MessageTextBlock.Text = string.Format(LocalizedStrings.DiskMgmt_RemoveDriveLetterConfirm, partition.DriveLetter);
    }
}

