using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class DiskPropertiesDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public PhysicalDiskInfo DiskInfo { get; }

    public DiskPropertiesDialog(PhysicalDiskInfo disk)
    {
        DiskInfo = disk;
        InitializeComponent();
    }
}


