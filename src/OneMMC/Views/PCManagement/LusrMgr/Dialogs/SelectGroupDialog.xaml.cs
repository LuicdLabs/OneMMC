using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PCManagement.Services.LusrMgr;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.LusrMgr;

namespace OneMMC.Views.LusrMgr;

/// <summary>
/// Dialog for selecting a local group.
/// </summary>
public sealed partial class SelectGroupDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private List<LocalGroup> _allGroups = new();
    public LocalGroup? SelectedGroup => GroupsList.SelectedItem as LocalGroup;

    public SelectGroupDialog()
    {
        this.InitializeComponent();
        LoadGroups();
    }

    private void LoadGroups()
    {
        var manager = new LocalUserGroupManager();
        _allGroups = manager.GetGroups();
        GroupsList.ItemsSource = _allGroups;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            // show all groups
            GroupsList.ItemsSource = _allGroups;
        }
        else
        {
            // filter groups by name
            GroupsList.ItemsSource = _allGroups.Where(g => g.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}

