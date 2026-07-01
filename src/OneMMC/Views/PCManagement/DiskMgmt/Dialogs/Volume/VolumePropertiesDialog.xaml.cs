using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

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

