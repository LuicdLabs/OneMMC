using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class VolumePropertiesDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public PartitionInfo PartitionInfo { get; }

    public VolumePropertiesDialog(PartitionInfo partition)
    {
        PartitionInfo = partition;
        InitializeComponent();
    }
}

