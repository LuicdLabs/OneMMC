// ============================================================================
// AuthApplicationsPage.xaml.cs
//
// Application Details Page - Manage groups, roles, tasks, and operations
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OneMMC.Core.Features.UserSecurity.Services.AzMan;
using OneMMC.Localization;
using OneMMC.Services;
using OneMMC.Views.UserSecurity.AzMan.AuthStore;
using OneMMC.Views.UserSecurity.AzMan.AuthStore.Scopes;
using OneMMC.Views.UserSecurity.AzMan.Dialogs;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using OneMMC.Core.Features.UserSecurity.ViewModels.AzMan;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Views.UserSecurity.AzMan.AuthStore.AuthApplications;

/// <summary>
/// Application Details Page - Manage groups, roles, tasks, and operations
/// </summary>
public sealed partial class AuthApplicationsPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    private int _currentPageIndex = 0;
    private AzManService? _service;
    internal AuthApplicationViewModel? _viewModel;
    private string _storePath = string.Empty;
    private string _applicationName = string.Empty;
    private bool _isDefinitionPropertiesDialogOpen;

    public AuthApplicationsPage()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        this.Unloaded += (_, _) =>
        {
            App.ThemeChanged -= OnThemeChanged;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel = null;
            }
            _service = null;
        };
    }

    #region Navigation

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ApplicationNavigationParameter param)
        {
            // AzManService is a DI singleton; the navigation parameter deliberately no longer carries it.
            _service = App.GetRequiredService<AzManService>();
            _storePath = param.StorePath;
            _applicationName = param.ApplicationName;

            _viewModel = new AuthApplicationViewModel(_service);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Load data
            await _viewModel.LoadAsync(_storePath, _applicationName);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    #endregion

    #region Event Handlers

    private void OnThemeChanged(ElementTheme theme)
    {
        this.RequestedTheme = theme;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AuthApplicationViewModel.IsLoading):
                    LoadingRing.IsActive = _viewModel?.IsLoading ?? false;
                    break;

                case nameof(AuthApplicationViewModel.HasError):
                case nameof(AuthApplicationViewModel.StatusMessage):
                    UpdateStatusBar();
                    break;
            }
        });
    }

    private void PageSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem == GroupsRolesItem)
        {
            int newIndex = 0;
            bool isForward = newIndex > _currentPageIndex;
            AnimatePageTransition(DefinitionsPanel, GroupsRolesPanel, isForward);
            _currentPageIndex = newIndex;
        }
        else if (sender.SelectedItem == DefinitionsItem)
        {
            int newIndex = 1;
            bool isForward = newIndex > _currentPageIndex;
            AnimatePageTransition(GroupsRolesPanel, DefinitionsPanel, isForward);
            _currentPageIndex = newIndex;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                await _viewModel.LoadAsync(_storePath, _applicationName);
            }
        }

    private async void OnPropertiesClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.Application == null) return;

            var editData = new EditItemData
            {
                Name = _viewModel.Application.Name,
                Description = _viewModel.Application.Description
            };

            var dialog = new EditItemDialog(EditItemType.Application, editData) 
            { 
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme 
            };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.Result != null)
            {
                try
                {
                    await _viewModel.UpdateApplicationAsync(dialog.Result.Description, _viewModel.Application.ApplicationData);
                    ShowStatus(LocalizedStrings.AuthApplicationsPage_Status_ApplicationUpdated, false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_ApplicationUpdateFailed, ex.Message), true);
                }
            }
        }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = LocalizedStrings.AuthApplicationsPage_DeleteApplication_Title,
            Content = string.Format(LocalizedStrings.AuthApplicationsPage_DeleteApplication_Content, _applicationName),
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && _service is not null)
        {
            try
            {
                await _service.DeleteApplicationAsync(_storePath, _applicationName);
            }
            catch (Exception ex)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_ApplicationUpdateFailed, ex.Message), true);
                return;
            }

            // Return to previous page after successful delete
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }
    }

    private async void OnHelpClick(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://learn.microsoft.com/en-us/windows/win32/secauthz/authorization-manager"));
    }

    // Groups
    private void OnGroupSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.GroupSearchText = sender.Text;
        }
    }

    private async void OnAddGroupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.Group) 
        { 
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme 
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var group = await _viewModel.CreateGroupAsync(
                dialog.Result.Name, dialog.Result.GroupType, dialog.Result.Description, dialog.Result.LdapQuery);
            if (group != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_GroupCreated, group.Name), false);
            }
        }
    }

    private async void OnEditGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzApplicationGroupInfo group)
        {
            // Show group members management dialog
            var availableAppGroups = _viewModel?.Groups
                .Where(g => !string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Name)
                .ToList();

            var dialog = new GroupMembersDialog(group, availableAppGroups) 
            { 
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme 
            };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
            {
                try
                {
                    // Process member changes
                    foreach (var member in dialog.Result.AddedMembers)
                    {
                        await _viewModel.AddGroupMemberAsync(group.Name, member);
                    }
                    foreach (var member in dialog.Result.RemovedMembers)
                    {
                        await _viewModel.RemoveGroupMemberAsync(group.Name, member);
                    }

                    // Process non-member changes
                    foreach (var nonMember in dialog.Result.AddedNonMembers)
                    {
                        await _viewModel.AddGroupNonMemberAsync(group.Name, nonMember);
                    }
                    foreach (var nonMember in dialog.Result.RemovedNonMembers)
                    {
                        await _viewModel.RemoveGroupNonMemberAsync(group.Name, nonMember);
                    }

                    // Process included application-group links
                    foreach (var appMember in dialog.Result.AddedAppMemberLinks)
                    {
                        await _viewModel.AddAppMemberToGroupAsync(group.Name, appMember);
                    }
                    foreach (var appMember in dialog.Result.RemovedAppMemberLinks)
                    {
                        await _viewModel.RemoveAppMemberFromGroupAsync(group.Name, appMember);
                    }

                    // Process excluded application-group links
                    foreach (var appNonMember in dialog.Result.AddedAppNonMemberLinks)
                    {
                        await _viewModel.AddAppNonMemberToGroupAsync(group.Name, appNonMember);
                    }
                    foreach (var appNonMember in dialog.Result.RemovedAppNonMemberLinks)
                    {
                        await _viewModel.RemoveAppNonMemberFromGroupAsync(group.Name, appNonMember);
                    }

                    if (dialog.Result.BizRuleChanged)
                    {
                        await _viewModel.SetGroupBizRuleAsync(group.Name, dialog.Result.BizRule, dialog.Result.BizRuleLanguage);
                    }

                    // Reload
                    await _viewModel.LoadAsync(_storePath, _applicationName);
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_GroupUpdated, group.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_GroupUpdateFailed, ex.Message), true);
                }
            }
        }
    }

    private async void OnDeleteGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzApplicationGroupInfo group && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthApplicationsPage_DeleteGroup_Title,
                Content = string.Format(LocalizedStrings.AuthApplicationsPage_DeleteGroup_Content, group.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteGroupAsync(group.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_GroupDeleted, group.Name), false);
                }
            }
        }
    }

    // Role Assignments
    private void OnRoleAssignmentSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.RoleAssignmentSearchText = sender.Text;
        }
    }

    private async void OnAddRoleAssignmentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.RoleAssignment) 
        { 
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme 
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var role = await _viewModel.CreateRoleAssignmentAsync(dialog.Result.Name, dialog.Result.Description);
            if (role != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleAssignmentCreated, role.Name), false);
            }
        }
    }

    private async void OnEditRoleAssignmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzRoleAssignmentInfo role && _viewModel != null)
        {
            try
            {
                // Create and show the Role Assignment Properties Dialog
                var dialog = new RoleAssignmentPropertiesDialog(
                    role,
                    _storePath,
                    _applicationName,
                    scopeName: null) // null for application-level roles
                {
                    XamlRoot = this.XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    RequestedTheme = App.CurrentTheme
                };

                var result = await dialog.ShowAsync();

                // Handle dialog result
                if (result == ContentDialogResult.Primary && dialog.Result != null)
                {
                    try
                    {
                        // Update description if changed
                        if (dialog.Result.Description != role.Description)
                        {
                            await _service!.UpdateRoleAssignmentAsync(_storePath, _applicationName, role.Name, dialog.Result.Description);
                        }

                        // Add new members
                        foreach (var memberSid in dialog.Result.AddedMembers)
                        {
                            await _service!.AddRoleMemberAsync(_storePath, _applicationName, role.Name, memberSid);
                        }

                        // Remove members
                        foreach (var memberSid in dialog.Result.RemovedMembers)
                        {
                            await _service!.RemoveRoleMemberAsync(_storePath, _applicationName, role.Name, memberSid);
                        }

                        // Refresh the view
                        await _viewModel.LoadAsync(_storePath, _applicationName);
                        ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleAssignmentUpdated, role.Name), false);
                    }
                    catch (Exception ex)
                    {
                        ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleAssignmentUpdateFailed, ex.Message), true);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus(string.Format(_localizedStrings.Common_FailedToOpenDialog_Format, ex.Message), true);
            }
        }
    }

    private async void OnDeleteRoleAssignmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzRoleAssignmentInfo role && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthApplicationsPage_DeleteRoleAssignment_Title,
                Content = string.Format(LocalizedStrings.AuthApplicationsPage_DeleteRoleAssignment_Content, role.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteRoleAssignmentAsync(role.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleAssignmentDeleted, role.Name), false);
                }
            }
        }
    }

    // Role Definitions
    private void OnRoleSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.RoleDefinitionSearchText = sender.Text;
        }
    }

    private async void OnAddRoleClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.RoleDefinition) 
        { 
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme 
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var role = await _viewModel.CreateRoleDefinitionAsync(dialog.Result.Name, dialog.Result.Description);
            if (role != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleCreated, role.Name), false);
            }
        }
    }

    private async void OnEditRoleDefinitionClick(object sender, RoutedEventArgs e)
    {
        if (_isDefinitionPropertiesDialogOpen)
        {
            return;
        }

        if (sender is Button btn && btn.Tag is AzRoleDefinitionInfo role && _viewModel != null)
        {
            _isDefinitionPropertiesDialogOpen = true;
            try
            {
            var roleDefinitionNames = _viewModel.RoleDefinitions.Select(roleDef => roleDef.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var taskNames = _viewModel.Tasks.Select(task => task.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var assignedRoles = role.Tasks.Where(name => roleDefinitionNames.Contains(name) && !name.Equals(role.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var assignedTasks = role.Tasks.Where(name => taskNames.Contains(name)).ToList();

            var availableRoles = _viewModel.RoleDefinitions
                .Where(roleDef => !roleDef.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase))
                .Select(roleDef => new AssignableItem
                {
                    Name = roleDef.Name,
                    Description = roleDef.Description
                }).ToList();

            var availableTasks = _viewModel.Tasks.Select(task => new AssignableItem
            {
                Name = task.Name,
                Description = task.Description
            }).ToList();

            var availableOperations = _viewModel.Operations.Select(op => new AssignableItem
            {
                Name = op.Name,
                Description = op.Description,
                OperationId = op.OperationId
            }).ToList();

            var dialog = new DefinitionPropertiesDialog(
                DefinitionItemType.RoleDefinition,
                role.Name,
                role.Description,
                role.BizRule,
                role.BizRuleLanguage,
                role.BizRuleImportedPath,
                availableRoles,
                assignedRoles,
                availableTasks,
                assignedTasks,
                availableOperations,
                role.Operations)
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowDialogAsync();
            if (result == ContentDialogResult.Primary && dialog.Result != null)
            {
                try
                {
                    await _viewModel.UpdateRoleDefinitionAsync(role.Name, dialog.Result.Description);

                    foreach (var added in dialog.Result.AddedRoles)
                    {
                        await _viewModel.AddTaskToRoleDefinitionAsync(role.Name, added);
                    }
                    foreach (var removed in dialog.Result.RemovedRoles)
                    {
                        await _viewModel.RemoveTaskFromRoleDefinitionAsync(role.Name, removed);
                    }

                    foreach (var added in dialog.Result.AddedTasks)
                    {
                        await _viewModel.AddTaskToRoleDefinitionAsync(role.Name, added);
                    }
                    foreach (var removed in dialog.Result.RemovedTasks)
                    {
                        await _viewModel.RemoveTaskFromRoleDefinitionAsync(role.Name, removed);
                    }

                    foreach (var added in dialog.Result.AddedOperations)
                    {
                        await _viewModel.AddOperationToRoleDefinitionAsync(role.Name, added);
                    }
                    foreach (var removed in dialog.Result.RemovedOperations)
                    {
                        await _viewModel.RemoveOperationFromRoleDefinitionAsync(role.Name, removed);
                    }

                    if (dialog.Result.ClearRuleFromStore)
                    {
                        await _viewModel.SetRoleDefinitionBizRuleAsync(role.Name, string.Empty, string.Empty);
                    }
                    else if (dialog.Result.ReloadRuleIntoStore)
                    {
                        await _viewModel.ImportRoleDefinitionBizRuleAsync(role.Name, dialog.Result.ScriptPath, dialog.Result.BizRuleLanguage);
                    }

                    await _viewModel.LoadAsync(_storePath, _applicationName);
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleUpdated, role.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleUpdateFailed, ex.Message), true);
                }
            }
            }
            finally
            {
                _isDefinitionPropertiesDialogOpen = false;
            }
        }
    }

    private async void OnDeleteRoleDefinitionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzRoleDefinitionInfo role && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthApplicationsPage_DeleteRole_Title,
                Content = string.Format(LocalizedStrings.AuthApplicationsPage_DeleteRole_Content, role.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteRoleDefinitionAsync(role.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_RoleDeleted, role.Name), false);
                }
            }
        }
    }

    // Tasks
    private void OnTaskSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.TaskSearchText = sender.Text;
        }
    }

    private async void OnAddTaskClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.Task) 
        { 
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme 
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var task = await _viewModel.CreateTaskAsync(dialog.Result.Name, dialog.Result.Description);
            if (task != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_TaskCreated, task.Name), false);
            }
        }
    }

    private async void OnEditTaskClick(object sender, RoutedEventArgs e)
    {
        if (_isDefinitionPropertiesDialogOpen)
        {
            return;
        }

        if (sender is Button btn && btn.Tag is AzTaskInfo task && _viewModel != null)
        {
            _isDefinitionPropertiesDialogOpen = true;
            try
            {
            var roleDefinitionNames = _viewModel.RoleDefinitions.Select(roleDef => roleDef.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var taskNames = _viewModel.Tasks.Select(taskItem => taskItem.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var assignedRoles = task.TaskLinks.Where(name => roleDefinitionNames.Contains(name)).ToList();
            var assignedTasks = task.TaskLinks.Where(name => taskNames.Contains(name) && !name.Equals(task.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            var availableRoles = _viewModel.RoleDefinitions.Select(roleDef => new AssignableItem
            {
                Name = roleDef.Name,
                Description = roleDef.Description
            }).ToList();

            var availableTasks = _viewModel.Tasks
                .Where(taskItem => !taskItem.Name.Equals(task.Name, StringComparison.OrdinalIgnoreCase))
                .Select(taskItem => new AssignableItem
                {
                    Name = taskItem.Name,
                    Description = taskItem.Description
                }).ToList();

            var availableOperations = _viewModel.Operations.Select(op => new AssignableItem
            {
                Name = op.Name,
                Description = op.Description,
                OperationId = op.OperationId
            }).ToList();

            var dialog = new DefinitionPropertiesDialog(
                DefinitionItemType.Task,
                task.Name,
                task.Description,
                task.BizRule,
                task.BizRuleLanguage,
                task.BizRuleImportedPath,
                availableRoles,
                assignedRoles,
                availableTasks,
                assignedTasks,
                availableOperations,
                task.Operations)
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowDialogAsync();
            if (result == ContentDialogResult.Primary && dialog.Result != null)
            {
                try
                {
                    await _viewModel.UpdateTaskAsync(task.Name, dialog.Result.Description);

                    foreach (var added in dialog.Result.AddedRoles)
                    {
                        await _viewModel.AddTaskLinkAsync(task.Name, added);
                    }
                    foreach (var removed in dialog.Result.RemovedRoles)
                    {
                        await _viewModel.RemoveTaskLinkAsync(task.Name, removed);
                    }

                    foreach (var added in dialog.Result.AddedTasks)
                    {
                        await _viewModel.AddTaskLinkAsync(task.Name, added);
                    }
                    foreach (var removed in dialog.Result.RemovedTasks)
                    {
                        await _viewModel.RemoveTaskLinkAsync(task.Name, removed);
                    }

                    foreach (var added in dialog.Result.AddedOperations)
                    {
                        await _viewModel.AddOperationToTaskAsync(task.Name, added);
                    }
                    foreach (var removed in dialog.Result.RemovedOperations)
                    {
                        await _viewModel.RemoveOperationFromTaskAsync(task.Name, removed);
                    }

                    if (dialog.Result.ClearRuleFromStore)
                    {
                        await _viewModel.SetTaskBizRuleAsync(task.Name, string.Empty, string.Empty);
                    }
                    else if (dialog.Result.ReloadRuleIntoStore)
                    {
                        await _viewModel.ImportTaskBizRuleAsync(task.Name, dialog.Result.ScriptPath, dialog.Result.BizRuleLanguage);
                    }

                    await _viewModel.LoadAsync(_storePath, _applicationName);
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_TaskUpdated, task.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_TaskUpdateFailed, ex.Message), true);
                }
            }
            }
            finally
            {
                _isDefinitionPropertiesDialogOpen = false;
            }
        }
    }

    private async void OnDeleteTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzTaskInfo task && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthApplicationsPage_DeleteTask_Title,
                Content = string.Format(LocalizedStrings.AuthApplicationsPage_DeleteTask_Content, task.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteTaskAsync(task.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_TaskDeleted, task.Name), false);
                }
            }
        }
    }

    // Operations
    private void OnOperationSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.OperationSearchText = sender.Text;
        }
    }

    private async void OnAddOperationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.Operation) 
        { 
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme 
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var operation = await _viewModel.CreateOperationAsync(
                dialog.Result.Name, dialog.Result.Description, dialog.Result.OperationId);
            if (operation != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_OperationCreated, operation.Name), false);
            }
        }
    }

    private async void OnEditOperationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzOperationInfo operation)
        {
            var editData = new EditItemData
            {
                Name = operation.Name,
                Description = operation.Description,
                OperationId = operation.OperationId
            };

            var dialog = new EditItemDialog(EditItemType.Operation, editData) 
            { 
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme 
            };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
            {
                try
                {
                    await _viewModel.UpdateOperationAsync(operation.Name, dialog.Result.Description, operation.ApplicationData, dialog.Result.OperationId);
                    await _viewModel.LoadAsync(_storePath, _applicationName);
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_OperationUpdated, operation.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_OperationUpdateFailed, ex.Message), true);
                }
            }
        }
    }

    private async void OnDeleteOperationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzOperationInfo operation && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthApplicationsPage_DeleteOperation_Title,
                Content = string.Format(LocalizedStrings.AuthApplicationsPage_DeleteOperation_Content, operation.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteOperationAsync(operation.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_OperationDeleted, operation.Name), false);
                }
            }
        }
    }

    // Scopes
    private void OnScopeSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.ScopeSearchText = sender.Text;
        }
    }

    private void OnScopeCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.SettingsCard card && card.Tag is AzScopeInfo scope && _service != null && this.Frame != null)
        {
            var param = new ScopeNavigationParameter(_storePath, _applicationName, scope.Name);
            BreadcrumbNavigationService.AddBreadcrumb(scope.Name, typeof(Scopes.ScopesPage), param);
            this.Frame.Navigate(typeof(Scopes.ScopesPage), param, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
        }
    }

    private async void OnAddScopeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.Application) 
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme 
        };
        dialog.Title = LocalizedStrings.AuthApplicationsPage_NewScope_Title;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var scope = await _viewModel.CreateScopeAsync(dialog.Result.Name, dialog.Result.Description);
            if (scope != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_ScopeCreated, scope.Name), false);
            }
        }
    }

    private async void OnEditScopeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzScopeInfo scope)
        {
            var editData = new EditItemData
            {
                Name = scope.Name,
                Description = scope.Description
            };

            var dialog = new EditItemDialog(EditItemType.Application, editData) 
            { 
                XamlRoot = this.XamlRoot, 
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme 
            };
            dialog.Title = LocalizedStrings.AuthApplicationsPage_ScopeProperties_Title;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
            {
                try
                {
                    await _viewModel.UpdateScopeAsync(scope.Name, dialog.Result.Description);
                    await _viewModel.LoadAsync(_storePath, _applicationName);
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_ScopeUpdated, scope.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_ScopeUpdateFailed, ex.Message), true);
                }
            }
        }
    }

    private async void OnDeleteScopeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzScopeInfo scope && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthApplicationsPage_DeleteScope_Title,
                Content = string.Format(LocalizedStrings.AuthApplicationsPage_DeleteScope_Content, scope.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteScopeAsync(scope.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthApplicationsPage_Status_ScopeDeleted, scope.Name), false);
                }
            }
        }
    }

    #endregion

    #region Private Methods

    private void AnimatePageTransition(UIElement oldPage, UIElement newPage, bool isForward)
    {
        if (newPage.RenderTransform is not TranslateTransform)
            newPage.RenderTransform = new TranslateTransform();
        if (oldPage.RenderTransform is not TranslateTransform)
            oldPage.RenderTransform = new TranslateTransform();

        var newTransform = (TranslateTransform)newPage.RenderTransform;
        var oldTransform = (TranslateTransform)oldPage.RenderTransform;

        var duration = TimeSpan.FromMilliseconds(300);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var oldAnimation = new DoubleAnimation { To = isForward ? -100 : 100, Duration = duration, EasingFunction = easing };
        var newAnimation = new DoubleAnimation { From = isForward ? 100 : -100, To = 0, Duration = duration, EasingFunction = easing };
        var oldFadeOut = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = easing };
        var newFadeIn = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = easing };

        var storyboard = new Storyboard();

        Storyboard.SetTarget(oldAnimation, oldTransform);
        Storyboard.SetTargetProperty(oldAnimation, "X");
        storyboard.Children.Add(oldAnimation);

        Storyboard.SetTarget(oldFadeOut, oldPage);
        Storyboard.SetTargetProperty(oldFadeOut, "Opacity");
        storyboard.Children.Add(oldFadeOut);

        Storyboard.SetTarget(newAnimation, newTransform);
        Storyboard.SetTargetProperty(newAnimation, "X");
        storyboard.Children.Add(newAnimation);

        Storyboard.SetTarget(newFadeIn, newPage);
        Storyboard.SetTargetProperty(newFadeIn, "Opacity");
        storyboard.Children.Add(newFadeIn);

        storyboard.Completed += (s, e) =>
        {
            oldPage.Visibility = Visibility.Collapsed;
            oldTransform.X = 0;
        };

        newPage.Visibility = Visibility.Visible;
        storyboard.Begin();
    }

    private void UpdateStatusBar()
    {
        if (_viewModel == null) return;

        if (_viewModel.HasError)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = LocalizedStrings.Common_ErrorTitle;
            StatusInfoBar.Message = _viewModel.StatusMessage;
            StatusInfoBar.IsOpen = true;
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusInfoBar.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        StatusInfoBar.Title = isError ? LocalizedStrings.Common_ErrorTitle : LocalizedStrings.Common_SuccessTitle;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    #endregion
}
