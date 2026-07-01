using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;
using OneMMC.Localization;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class MarkPartitionActiveDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public MarkPartitionActiveDialog()
    {
        InitializeComponent();
    }
}
