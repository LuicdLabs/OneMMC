using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;
using ManagementTools.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Data transfer object returned by the Role Assignment Properties Dialog
/// containing the updated description and lists of added/removed member SIDs.
/// </summary>
public class RoleAssignmentPropertiesResult
{
    /// <summary>
    /// Gets or sets the updated description for the role assignment.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of member SIDs that were added to the role assignment.
    /// </summary>
    public List<string> AddedMembers { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of member SIDs that were removed from the role assignment.
    /// </summary>
    public List<string> RemovedMembers { get; set; } = [];
}

/// <summary>
/// Display item for ListView data binding in the Assigned Users tab.
/// Combines the display name with the SID for tracking purposes.
/// </summary>
public class MemberDisplayItem
{
    /// <summary>
    /// Gets or sets the display name of the user or group.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Security Identifier (SID) for tracking the member.
    /// </summary>
    public string Sid { get; set; } = string.Empty;
}

/// <summary>
/// Modal dialog for viewing and editing role assignment properties.
/// Provides a tabbed interface with General (name/description) and Assigned Users (member management) tabs.
/// </summary>
public sealed partial class RoleAssignmentPropertiesDialog : ContentDialog
{
    /// <summary>
    /// Gets the localized strings for the dialog.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    /// <summary>
    /// Gets the static localized strings instance for use in DataTemplates.
    /// </summary>
    public static LocalizedStrings StaticLocalizedStrings => LocalizedStrings.Instance;

    /// <summary>
    /// Gets the result of the dialog operation, or null if canceled.
    /// </summary>
    public RoleAssignmentPropertiesResult? Result { get; private set; }

    // Private fields
    private readonly AzRoleAssignmentInfo _roleAssignment;
    private readonly string _storePath;
    private readonly string _appName;
    private readonly string? _scopeName;
    private readonly ObservableCollection<MemberDisplayItem> _members;
    private readonly HashSet<string> _originalMemberSids;
    private readonly HashSet<string> _addedMemberSids;
    private readonly HashSet<string> _removedMemberSids;
    private readonly ILogger<RoleAssignmentPropertiesDialog> _logger;

    /// <summary>
    /// Initializes a new instance of the RoleAssignmentPropertiesDialog class.
    /// </summary>
    /// <param name="roleAssignment">The role assignment to display and edit.</param>
    /// <param name="storePath">The path to the authorization store.</param>
    /// <param name="appName">The name of the application.</param>
    /// <param name="scopeName">The name of the scope (optional, null for application-level roles).</param>
    public RoleAssignmentPropertiesDialog(
        AzRoleAssignmentInfo roleAssignment,
        string storePath,
        string appName,
        string? scopeName = null)
    {
        // Store constructor parameters
        _roleAssignment = roleAssignment;
        _storePath = storePath;
        _appName = appName;
        _scopeName = scopeName;

        // Initialize collections
        _members = new ObservableCollection<MemberDisplayItem>();
        _originalMemberSids = new HashSet<string>();
        _addedMemberSids = new HashSet<string>();
        _removedMemberSids = new HashSet<string>();

        // Get logger from dependency injection
        _logger = App.GetRequiredService<ILogger<RoleAssignmentPropertiesDialog>>();

        // Initialize component
        InitializeComponent();

        // Load data into the dialog
        LoadData();
    }

    /// <summary>
    /// Loads the role assignment data into the dialog controls.
    /// </summary>
    private void LoadData()
    {
        // Set dialog title to include role assignment name using localized format string
        var titleFormat = LocalizedStrings.RoleAssignmentPropertiesDialog_Title;
        Title = string.Format(titleFormat, _roleAssignment.Name);

        // Set name and description text boxes
        NameTextBox.Text = _roleAssignment.Name;
        DescriptionTextBox.Text = _roleAssignment.Description;

        // Populate original member SIDs from role assignment
        foreach (var memberSid in _roleAssignment.Members)
        {
            _originalMemberSids.Add(memberSid);
        }

        // Refresh the members list to populate ListView
        RefreshMembersList();

        // Log information message about dialog opening
        _logger.LogInformation("Role assignment properties dialog opened for '{RoleName}'", _roleAssignment.Name);
    }

    /// <summary>
    /// Refreshes the members list in the Assigned Users tab.
    /// </summary>
    private void RefreshMembersList()
    {
        // Clear the members collection
        _members.Clear();

        // Iterate through Members (SIDs) and MemberNames in parallel
        for (int i = 0; i < _roleAssignment.Members.Count; i++)
        {
            var sid = _roleAssignment.Members[i];
            var name = i < _roleAssignment.MemberNames.Count 
                ? _roleAssignment.MemberNames[i] 
                : sid; // Fallback to SID if name not available

            // Create MemberDisplayItem for each member
            var memberItem = new MemberDisplayItem
            {
                Name = name,
                Sid = sid
            };

            // Add to members collection
            _members.Add(memberItem);
        }

        // Bind MembersListView.ItemsSource to _members
        MembersListView.ItemsSource = _members;

        // Update empty state message visibility based on member count
        NoMembersMessage.Visibility = _members.Count == 0 
            ? Microsoft.UI.Xaml.Visibility.Visible 
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    /// <summary>
    /// Handles the SelectionChanged event of the SelectorBar to switch between tabs.
    /// </summary>
    private void OnSelectorBarSelectionChanged(object sender, SelectorBarSelectionChangedEventArgs e)
    {
        // Get the selected SelectorBarItem from the sender
        if (sender is not SelectorBar selectorBar)
        {
            return;
        }

        var selectedItem = selectorBar.SelectedItem;
        if (selectedItem == null)
        {
            return;
        }

        // Check Tag property to determine which tab is selected
        var tag = selectedItem.Tag as string;
        
        if (tag == "General")
        {
            // Show General panel, hide Assigned Users panel
            GeneralPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            AssignedUsersPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        else if (tag == "AssignedUsers")
        {
            // Show Assigned Users panel, hide General panel
            AssignedUsersPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            GeneralPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Handles the Click event of the Add button in the Assigned Users tab.
    /// Opens the Directory Object Picker to allow the user to select users and groups to add.
    /// </summary>
    private void OnAddUsersClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            // Get window handle for the Directory Object Picker dialog
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);

            // Show the Directory Object Picker with multi-select enabled
            var selections = DirectoryObjectPickerService.ShowDialog(
                hwnd,
                ObjectPickerTypes.UsersAndGroups,
                multiSelect: true);

            // Check if selections is not null and has items
            if (selections is { Count: > 0 })
            {
                int addedCount = 0;

                // Iterate through each DirectoryObject in selections
                foreach (var obj in selections)
                {
                    // Check if Sid is not null/empty and not a duplicate
                    if (!string.IsNullOrEmpty(obj.Sid) && !IsDuplicateMember(obj.Sid))
                    {
                        // Add Sid to _addedMemberSids
                        _addedMemberSids.Add(obj.Sid);

                        // Create MemberDisplayItem
                        var memberItem = new MemberDisplayItem
                        {
                            Name = obj.Name,
                            Sid = obj.Sid
                        };

                        // Add MemberDisplayItem to _members collection
                        _members.Add(memberItem);
                        addedCount++;
                    }
                }

                // Update empty state message visibility
                NoMembersMessage.Visibility = _members.Count == 0
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;

                // Log information about users added
                _logger.LogInformation(
                    "Added {AddedCount} user(s) to role assignment '{RoleName}'",
                    addedCount,
                    _roleAssignment.Name);
            }
        }
        catch (Exception ex)
        {
            // Log error
            _logger.LogError(ex, "Failed to add users to role assignment '{RoleName}'", _roleAssignment.Name);

            // Display error in ErrorInfoBar
            var errorFormat = LocalizedStrings.RoleAssignmentPropertiesDialog_Error_AddUsersFailed;
            ErrorInfoBar.Message = !string.IsNullOrEmpty(errorFormat)
                ? string.Format(errorFormat, ex.Message)
                : string.Format(_localizedStrings.Common_FailedToAddUsers_Format, ex.Message);
            ErrorInfoBar.IsOpen = true;
        }
    }

    /// <summary>
    /// Checks if a member SID already exists in the role assignment.
    /// </summary>
    /// <param name="sid">The SID to check.</param>
    /// <returns>True if the SID is a duplicate, false otherwise.</returns>
    private bool IsDuplicateMember(string sid)
    {
        // Check if sid exists in _originalMemberSids
        if (_originalMemberSids.Contains(sid))
        {
            return true;
        }

        // Check if sid exists in _addedMemberSids
        if (_addedMemberSids.Contains(sid))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles the SelectionChanged event of the MembersListView to enable/disable the Remove button.
    /// </summary>
    private void OnMembersSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        // Enable Remove button only if there are selected items
        RemoveButton.IsEnabled = MembersListView.SelectedItems.Count > 0;
    }

    /// <summary>
    /// Handles the Click event of the Remove button to remove selected members.
    /// </summary>
    private void OnRemoveMemberClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            // Get selected items from MembersListView
            var selectedItems = MembersListView.SelectedItems.Cast<MemberDisplayItem>().ToList();
            
            if (selectedItems.Count == 0)
            {
                return;
            }

            // Process each selected member
            foreach (var memberToRemove in selectedItems)
            {
                var sid = memberToRemove.Sid;

                // If member was in original list, add Sid to _removedMemberSids
                if (_originalMemberSids.Contains(sid))
                {
                    _removedMemberSids.Add(sid);
                }

                // If member was in added list, remove Sid from _addedMemberSids
                if (_addedMemberSids.Contains(sid))
                {
                    _addedMemberSids.Remove(sid);
                }

                // Remove MemberDisplayItem from _members collection
                _members.Remove(memberToRemove);

                // Log information about user removed
                _logger.LogInformation(
                    "Removed user '{UserName}' (SID: {Sid}) from role assignment '{RoleName}'",
                    memberToRemove.Name,
                    sid,
                    _roleAssignment.Name);
            }

            // Update empty state message visibility
            NoMembersMessage.Visibility = _members.Count == 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            // Log error
            _logger.LogError(ex, "Failed to remove user from role assignment '{RoleName}'", _roleAssignment.Name);

            // Display error in ErrorInfoBar
            var errorFormat = LocalizedStrings.RoleAssignmentPropertiesDialog_Error_RemoveUserFailed;
            ErrorInfoBar.Message = !string.IsNullOrEmpty(errorFormat)
                ? string.Format(errorFormat, ex.Message)
                : string.Format(_localizedStrings.Common_FailedToRemoveUser_Format, ex.Message);
            ErrorInfoBar.IsOpen = true;
        }
    }

    /// <summary>
    /// Handles the PrimaryButtonClick event (Save button).
    /// Creates a result object with all changes and assigns it to the Result property.
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Create new RoleAssignmentPropertiesResult instance
        Result = new RoleAssignmentPropertiesResult
        {
            // Set Description from DescriptionTextBox.Text
            Description = DescriptionTextBox.Text,
            
            // Set AddedMembers from _addedMemberSids (convert to List)
            AddedMembers = _addedMemberSids.ToList(),
            
            // Set RemovedMembers from _removedMemberSids (convert to List)
            RemovedMembers = _removedMemberSids.ToList()
        };

        // Log information about save operation
        _logger.LogInformation(
            "Saving role assignment '{RoleName}' properties: Description updated, {AddedCount} member(s) added, {RemovedCount} member(s) removed",
            _roleAssignment.Name,
            Result.AddedMembers.Count,
            Result.RemovedMembers.Count);

        // Dialog will close with ContentDialogResult.Primary automatically
    }
}
