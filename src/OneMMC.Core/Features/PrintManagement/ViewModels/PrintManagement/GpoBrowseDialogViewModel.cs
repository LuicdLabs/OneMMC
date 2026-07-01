using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Features.PrintManagement.Models.PrintManagement;

namespace OneMMC.Views;

/// <summary>
/// ViewModel for the GPO browse dialog.
/// </summary>
public sealed partial class GpoBrowseDialogViewModel : ObservableObject
{
    public ObservableCollection<GroupPolicyObjectInfo> Gpos { get; } = new();

    [ObservableProperty]
    public partial GroupPolicyObjectInfo? SelectedGpo { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }
}


