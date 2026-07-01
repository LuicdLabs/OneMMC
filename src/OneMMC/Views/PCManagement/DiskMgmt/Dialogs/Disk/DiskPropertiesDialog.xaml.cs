using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

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


