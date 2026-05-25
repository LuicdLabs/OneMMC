using Microsoft.UI.Xaml.Controls;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;
using ManagementTools.Localization;

namespace ManagementTools.Views.DiskMgmt;

public sealed partial class MarkPartitionActiveDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public MarkPartitionActiveDialog()
    {
        InitializeComponent();
    }
}
