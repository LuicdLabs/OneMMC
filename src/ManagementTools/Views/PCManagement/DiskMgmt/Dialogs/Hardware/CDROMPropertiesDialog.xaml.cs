using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class CDROMPropertiesDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public CDROMInfo CdromInfo { get; }

    public CDROMPropertiesDialog(CDROMInfo cdrom)
    {
        CdromInfo = cdrom;
        InitializeComponent();
    }
}


