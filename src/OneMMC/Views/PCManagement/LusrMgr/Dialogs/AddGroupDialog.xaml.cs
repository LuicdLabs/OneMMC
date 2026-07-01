using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using OneMMC.Core.Features.PCManagement.Models.LusrMgr;
using System.Collections.ObjectModel;
using System;
using OneMMC.Localization;

namespace OneMMC.Views.LusrMgr;

/// <summary>
/// Dialog for adding a new local group.
/// </summary>
public sealed partial class AddGroupDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public string GroupName => GroupNameBox.Text;
    public string Description => DescriptionBox.Text;
    public ObservableCollection<string> Members { get; } = new ObservableCollection<string>();

    public AddGroupDialog()
    {
        this.InitializeComponent();
        MembersList.ItemsSource = Members;
        this.Closing += AddGroupDialog_Closing;
    }

    private void AddGroupDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(GroupName))
            {
                args.Cancel = true;
            }
        }
    }

    private void OnAddMemberClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            ObjectPickerTypes.UsersAndGroups,
            multiSelect: false);

        if (selections is { Count: > 0 })
        {
            var name = selections[0].Name;
            if (!string.IsNullOrEmpty(name) && !Members.Contains(name))
            {
                Members.Add(name);
            }
        }
    }

    private void OnRemoveMemberClick(object sender, RoutedEventArgs e)
    {
         if (MembersList.SelectedItem is string member)
         {
             Members.Remove(member);
         }
    }
}
