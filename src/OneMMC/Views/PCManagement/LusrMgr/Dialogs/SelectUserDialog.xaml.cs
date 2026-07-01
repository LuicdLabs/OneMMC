using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PCManagement.Services.LusrMgr;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Localization;
using OneMMC.Core.Features.PCManagement.Models.LusrMgr;

namespace OneMMC.Views.LusrMgr;

/// <summary>
/// Dialog for selecting a local user.
/// </summary>
public sealed partial class SelectUserDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private List<LocalUser> _allUsers = new();
    public LocalUser? SelectedUser => UsersList.SelectedItem as LocalUser;

    public SelectUserDialog()
    {
        this.InitializeComponent();
        LoadUsers();
    }

    private void LoadUsers()
    {
        var manager = new LocalUserGroupManager();
        _allUsers = manager.GetUsers();
        UsersList.ItemsSource = _allUsers;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            // show all users
            UsersList.ItemsSource = _allUsers;
        }
        else
        {
            // filter users by name or full name
            UsersList.ItemsSource = _allUsers.Where(u => u.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase) || u.FullName.Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}

