using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views.DiskMgmt;

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


