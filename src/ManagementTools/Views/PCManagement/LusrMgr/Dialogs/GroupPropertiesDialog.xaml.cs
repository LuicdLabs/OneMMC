using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PCManagement.Models.LusrMgr;
using ManagementTools.Core.Features.PCManagement.Services.LusrMgr;

namespace ManagementTools.Views.LusrMgr;

/// <summary>
/// Dialog for editing properties of a local group.
/// </summary>
public sealed partial class GroupPropertiesDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly LocalGroup _group;
    private readonly LocalUserGroupManager _manager;
    private ObservableCollection<string> _currentMembers = new();
    private HashSet<string> _originalMembers = new();

    public string GroupName => GroupNameBox.Text;
    public string Description => DescriptionBox.Text;

    public GroupPropertiesDialog(LocalGroup group)
    {
        this.InitializeComponent();
        _group = group;
        _manager = new LocalUserGroupManager();
        this.Closing += GroupPropertiesDialog_Closing;
        _ = LoadDataAsync(); // Fire and forget with discard
    }

    private async Task LoadDataAsync()
    {
        GroupNameBox.Text = _group.Name;
        DescriptionBox.Text = _group.Description;

        var members = await _manager.GetGroupMembersAsync(_group.Name);
        
        _originalMembers = new HashSet<string>(members);
        _currentMembers = new ObservableCollection<string>(_originalMembers);
        MembersList.ItemsSource = _currentMembers;
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
            if (!string.IsNullOrEmpty(name) && !_currentMembers.Contains(name))
            {
                _currentMembers.Add(name);
            }
        }
    }

    private void OnRemoveMemberClick(object sender, RoutedEventArgs e)
    {
        if (MembersList.SelectedItem is string member)
        {
            _currentMembers.Remove(member);
        }
    }

    private async void GroupPropertiesDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.Primary) return;

        var deferral = args.GetDeferral();
        try
        {
            await SaveChangesAsync();
        }
        catch (Exception ex)
        {
            ManagementTools.Services.Logging.UiLogger.LogDebug($"Error saving group: {ex.Message}");
            // Consider showing error message to user.
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task SaveChangesAsync()
    {
        // Update group description.
        await _manager.UpdateGroupAsync(_group.Name, DescriptionBox.Text);

        // Find added and removed members.
        var membersToAdd = _currentMembers.Except(_originalMembers);
        var membersToRemove = _originalMembers.Except(_currentMembers);

        // Process member changes in batch.
        foreach (var member in membersToAdd)
        {
            _manager.AddUserToGroup(member, _group.Name);
        }

        foreach (var member in membersToRemove)
        {
            _manager.RemoveUserFromGroup(member, _group.Name);
        }
    }
}

