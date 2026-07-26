// ============================================================================
// ScopesPage.xaml.cs
// 
// Scope Details Page - Manage groups and role assignments within the scope
// Mimics the scope functionality design of azman.msc
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OneMMC.Core.Features.UserSecurity.Services.AzMan;
using OneMMC.Localization;
using OneMMC.Views.UserSecurity.AzMan.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Views.UserSecurity.AzMan.AuthStore.Scopes;

/// <summary>
/// Scope Details Page - Manage groups and role assignments within the scope
/// </summary>
public sealed partial class ScopesPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private AzManService? _service;
    internal ScopeDetailViewModel? _viewModel;
    private string _storePath = string.Empty;
    private string _applicationName = string.Empty;
    private string _scopeName = string.Empty;
    private bool _isDefinitionPropertiesDialogOpen;

    public ScopesPage()
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

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ScopeNavigationParameter param)
        {
            // AzManService is a DI singleton; the navigation parameter deliberately no longer carries it.
            _service = App.GetRequiredService<AzManService>();
            _storePath = param.StorePath;
            _applicationName = param.ApplicationName;
            _scopeName = param.ScopeName;

            _viewModel = new ScopeDetailViewModel(_service);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            await _viewModel.LoadAsync(_storePath, _applicationName, _scopeName);
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
                case nameof(ScopeDetailViewModel.IsLoading):
                    LoadingRing.IsActive = _viewModel?.IsLoading ?? false;
                    break;

                case nameof(ScopeDetailViewModel.HasError):
                case nameof(ScopeDetailViewModel.StatusMessage):
                    UpdateStatusBar();
                    break;
            }
        });
    }

    #region Toolbar Events

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.LoadAsync(_storePath, _applicationName, _scopeName);
        }
    }

    private async void OnPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var editData = new EditItemData
        {
            Name = _scopeName,
            Description = _viewModel.ScopeDescription
        };

        var dialog = new EditItemDialog(EditItemType.Application, editData) 
        { 
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme 
        };
        dialog.Title = LocalizedStrings.ScopesPage_ScopeProperties_Title;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
        {
            try
            {
                await _viewModel.UpdateScopePropertiesAsync(dialog.Result.Description);
                ShowStatus(LocalizedStrings.ScopesPage_Status_ScopePropertiesUpdated, false);
            }
            catch (Exception ex)
            {
                ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_ScopePropertiesUpdateFailed, ex.Message), true);
            }
        }
    }

    private async void OnDeleteScopeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = LocalizedStrings.ScopesPage_DeleteScope_Title,
            Content = string.Format(LocalizedStrings.ScopesPage_DeleteScope_Content, _scopeName),
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && _viewModel != null)
        {
            var success = await _viewModel.DeleteScopeAsync();
            if (success && this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }
    }

    #endregion

    #region Groups Events

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
                ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_GroupCreated, group.Name), false);
            }
        }
    }

    private async void OnEditGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzApplicationGroupInfo group)
        {
            var dialog = new GroupMembersDialog(group) 
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

                    if (dialog.Result.BizRuleChanged)
                    {
                        await _viewModel.SetGroupBizRuleAsync(group.Name, dialog.Result.BizRule, dialog.Result.BizRuleLanguage);
                    }

                    await _viewModel.LoadAsync(_storePath, _applicationName, _scopeName);
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_GroupUpdated, group.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_GroupUpdateFailed, ex.Message), true);
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
                Title = LocalizedStrings.ScopesPage_DeleteGroup_Title,
                Content = string.Format(LocalizedStrings.ScopesPage_DeleteGroup_Content, group.Name),
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
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_GroupDeleted, group.Name), false);
                }
            }
        }
    }

    #endregion

    #region Role Assignments Events

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
                ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleAssignmentCreated, role.Name), false);
            }
        }
    }

    private async void OnEditRoleAssignmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzRoleAssignmentInfo role)
        {
            // Create and show the Role Assignment Properties Dialog
            var dialog = new RoleAssignmentPropertiesDialog(
                role,
                _storePath,
                _applicationName,
                scopeName: _scopeName) // pass scope name for scope-level roles
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();

            // Handle dialog result
            if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
            {
                try
                {
                    // Update description if changed
                    if (dialog.Result.Description != role.Description)
                    {
                        await _service!.UpdateScopeRoleAssignmentAsync(_storePath, _applicationName, _scopeName, role.Name, dialog.Result.Description);
                    }

                    // Add new members
                    foreach (var memberSid in dialog.Result.AddedMembers)
                    {
                        await _service!.AddScopeRoleAssignmentMemberAsync(_storePath, _applicationName, _scopeName, role.Name, memberSid);
                    }

                    // Remove members
                    foreach (var memberSid in dialog.Result.RemovedMembers)
                    {
                        await _service!.RemoveScopeRoleAssignmentMemberAsync(_storePath, _applicationName, _scopeName, role.Name, memberSid);
                    }

                    // Refresh the view
                    await _viewModel.LoadAsync(_storePath, _applicationName, _scopeName);
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleAssignmentUpdated, role.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleAssignmentUpdateFailed, ex.Message), true);
                }
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
                Title = LocalizedStrings.ScopesPage_DeleteRoleAssignment_Title,
                Content = string.Format(LocalizedStrings.ScopesPage_DeleteRoleAssignment_Content, role.Name),
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
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleAssignmentDeleted, role.Name), false);
                }
            }
        }
    }

    #endregion

    #region Role Definitions Events

    private void OnRoleDefinitionSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.RoleDefinitionSearchText = sender.Text;
        }
    }

    private async void OnAddRoleDefinitionClick(object sender, RoutedEventArgs e)
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
                ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleCreated, role.Name), false);
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
            var taskNames = _viewModel.Tasks.Select(taskItem => taskItem.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var assignedRoles = role.Tasks.Where(name => roleDefinitionNames.Contains(name) && !name.Equals(role.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var assignedTasks = role.Tasks.Where(name => taskNames.Contains(name)).ToList();

            var availableRoles = _viewModel.RoleDefinitions
                .Where(roleDef => !roleDef.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase))
                .Select(roleDef => new AssignableItem
                {
                    Name = roleDef.Name,
                    Description = roleDef.Description
                }).ToList();

            var availableTasks = _viewModel.Tasks.Select(taskItem => new AssignableItem
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
                role.Operations) {
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

                    await _viewModel.LoadAsync(_storePath, _applicationName, _scopeName);
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleUpdated, role.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleUpdateFailed, ex.Message), true);
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
                Title = LocalizedStrings.ScopesPage_DeleteRole_Title,
                Content = string.Format(LocalizedStrings.ScopesPage_DeleteRole_Content, role.Name),
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
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_RoleDeleted, role.Name), false);
                }
            }
        }
    }

    #endregion

    #region Tasks Events

    private void OnTaskSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.TaskSearchText = sender.Text;
        }
    }

    private async void OnAddTaskClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.Task) { 
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
                ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_TaskCreated, task.Name), false);
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
                task.Operations) {
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

                    await _viewModel.LoadAsync(_storePath, _applicationName, _scopeName);
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_TaskUpdated, task.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_TaskUpdateFailed, ex.Message), true);
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
                Title = LocalizedStrings.ScopesPage_DeleteTask_Title,
                Content = string.Format(LocalizedStrings.ScopesPage_DeleteTask_Content, task.Name),
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
                    ShowStatus(string.Format(LocalizedStrings.ScopesPage_Status_TaskDeleted, task.Name), false);
                }
            }
        }
    }

    #endregion

    #region Private Methods

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


/// <summary>
/// Scope navigation parameter. Identifiers only — see <c>StoreNavigationParameter</c> for why navigation
/// parameters must not carry live services or view models.
/// </summary>
public class ScopeNavigationParameter
{
    public string StorePath { get; }
    public string ApplicationName { get; }
    public string ScopeName { get; }

    public ScopeNavigationParameter(string storePath, string appName, string scopeName = "")
    {
        StorePath = storePath;
        ApplicationName = appName;
        ScopeName = scopeName;
    }
}

/// <summary>
/// Scope Detail ViewModel - Manage groups, role assignments, etc. within the scope
/// </summary>
public partial class ScopeDetailViewModel : INotifyPropertyChanged
{
    private readonly AzManService _azManService;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;
    private string _storePath = string.Empty;
    private string _applicationName = string.Empty;
    private string _scopeName = string.Empty;
    private string _scopeDescription = string.Empty;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _hasError;
    private string _groupSearchText = string.Empty;
    private string _roleAssignmentSearchText = string.Empty;
    private string _roleDefinitionSearchText = string.Empty;
    private string _taskSearchText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Collections
    public ObservableCollection<AzApplicationGroupInfo> Groups { get; } = [];
    public ObservableCollection<AzRoleAssignmentInfo> RoleAssignments { get; } = [];
    public ObservableCollection<AzRoleDefinitionInfo> RoleDefinitions { get; } = [];
    public ObservableCollection<AzTaskInfo> Tasks { get; } = [];
    public ObservableCollection<AzOperationInfo> Operations { get; } = [];

    // Filtered Collections
    public ObservableCollection<AzApplicationGroupInfo> FilteredGroups { get; } = [];
    public ObservableCollection<AzRoleAssignmentInfo> FilteredRoleAssignments { get; } = [];
    public ObservableCollection<AzRoleDefinitionInfo> FilteredRoleDefinitions { get; } = [];
    public ObservableCollection<AzTaskInfo> FilteredTasks { get; } = [];

    public string ScopeDescription
    {
        get => _scopeDescription;
        set { if (_scopeDescription != value) { _scopeDescription = value; OnPropertyChanged(); } }
    }

    public string GroupSearchText
    {
        get => _groupSearchText;
        set { if (_groupSearchText != value) { _groupSearchText = value; OnPropertyChanged(); ApplyGroupFilter(); } }
    }

    public string RoleAssignmentSearchText
    {
        get => _roleAssignmentSearchText;
        set { if (_roleAssignmentSearchText != value) { _roleAssignmentSearchText = value; OnPropertyChanged(); ApplyRoleAssignmentFilter(); } }
    }

    public string RoleDefinitionSearchText
    {
        get => _roleDefinitionSearchText;
        set { if (_roleDefinitionSearchText != value) { _roleDefinitionSearchText = value; OnPropertyChanged(); ApplyRoleDefinitionFilter(); } }
    }

    public string TaskSearchText
    {
        get => _taskSearchText;
        set { if (_taskSearchText != value) { _taskSearchText = value; OnPropertyChanged(); ApplyTaskFilter(); } }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); } }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    public bool HasError
    {
        get => _hasError;
        set { if (_hasError != value) { _hasError = value; OnPropertyChanged(); } }
    }

    // Count Text Properties
    public string GroupCountText => FilteredGroups.Count == 1
        ? string.Format(_localizedStrings.Common_CountItem_Singular, FilteredGroups.Count)
        : string.Format(_localizedStrings.Common_CountItem_Plural, FilteredGroups.Count);
    public string RoleAssignmentCountText => FilteredRoleAssignments.Count == 1
        ? string.Format(_localizedStrings.Common_CountItem_Singular, FilteredRoleAssignments.Count)
        : string.Format(_localizedStrings.Common_CountItem_Plural, FilteredRoleAssignments.Count);
    public string RoleDefinitionCountText => FilteredRoleDefinitions.Count == 1
        ? string.Format(_localizedStrings.Common_CountRole_Singular, FilteredRoleDefinitions.Count)
        : string.Format(_localizedStrings.Common_CountRole_Plural, FilteredRoleDefinitions.Count);
    public string TaskCountText => FilteredTasks.Count == 1
        ? string.Format(_localizedStrings.Common_CountTask_Singular, FilteredTasks.Count)
        : string.Format(_localizedStrings.Common_CountTask_Plural, FilteredTasks.Count);

    public ScopeDetailViewModel(AzManService service)
    {
        _azManService = service;
    }

    public async Task LoadAsync(string storePath, string applicationName, string scopeName)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            _storePath = storePath;
            _applicationName = applicationName;
            _scopeName = scopeName;

            var scope = await _azManService.GetScopeAsync(storePath, applicationName, scopeName);
            var application = await _azManService.GetApplicationAsync(storePath, applicationName);

            ScopeDescription = scope.Description;

            Groups.Clear();
            foreach (var g in scope.Groups) Groups.Add(g);

            // Load role definitions and tasks from scope
            RoleDefinitions.Clear();
            foreach (var r in scope.Roles) RoleDefinitions.Add(r);

            Tasks.Clear();
            foreach (var t in scope.Tasks) Tasks.Add(t);

            Operations.Clear();
            foreach (var op in application.Operations) Operations.Add(op);

            // Load role assignments from scope
            RoleAssignments.Clear();
            foreach (var r in scope.RoleAssignments) RoleAssignments.Add(r);

            ApplyAllFilters();
            StatusMessage = _localizedStrings.Common_LoadedSuccessfully;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    #region Scope Operations

    public async Task UpdateScopePropertiesAsync(string description)
    {
        await _azManService.UpdateScopeAsync(_storePath, _applicationName, _scopeName, description);
        ScopeDescription = description;
    }

    public async Task<bool> DeleteScopeAsync()
    {
        try
        {
            IsLoading = true;
            HasError = false;
            await _azManService.DeleteScopeAsync(_storePath, _applicationName, _scopeName);
            return true;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Group Operations

    public async Task<AzApplicationGroupInfo?> CreateGroupAsync(string name, AzGroupType groupType, string description, string ldapQuery)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            var group = await _azManService.CreateScopeGroupAsync(_storePath, _applicationName, _scopeName, name, groupType, description, ldapQuery);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return group;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AddGroupMemberAsync(string groupName, string memberSid)
    {
        await _azManService.AddScopeGroupMemberAsync(_storePath, _applicationName, _scopeName, groupName, memberSid);
    }

    public async Task RemoveGroupMemberAsync(string groupName, string memberSid)
    {
        await _azManService.RemoveScopeGroupMemberAsync(_storePath, _applicationName, _scopeName, groupName, memberSid);
    }

    public async Task AddGroupNonMemberAsync(string groupName, string memberSid)
    {
        await _azManService.AddScopeGroupNonMemberAsync(_storePath, _applicationName, _scopeName, groupName, memberSid);
    }

    public async Task RemoveGroupNonMemberAsync(string groupName, string memberSid)
    {
        await _azManService.RemoveScopeGroupNonMemberAsync(_storePath, _applicationName, _scopeName, groupName, memberSid);
    }

    public async Task SetGroupBizRuleAsync(string groupName, string bizRule, string bizRuleLanguage)
    {
        await _azManService.SetScopeGroupBizRuleAsync(_storePath, _applicationName, _scopeName, groupName, bizRule, bizRuleLanguage);
    }

    public async Task<bool> DeleteGroupAsync(string name)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            await _azManService.DeleteScopeGroupAsync(_storePath, _applicationName, _scopeName, name);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return true;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Role Assignment Operations

    public async Task<AzRoleAssignmentInfo?> CreateRoleAssignmentAsync(string name, string description)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            var role = await _azManService.CreateScopeRoleAssignmentAsync(_storePath, _applicationName, _scopeName, name, description);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return role;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AddRoleAssignmentMemberAsync(string roleName, string memberSid)
    {
        await _azManService.AddScopeRoleAssignmentMemberAsync(_storePath, _applicationName, _scopeName, roleName, memberSid);
    }

    public async Task<bool> DeleteRoleAssignmentAsync(string name)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            await _azManService.DeleteScopeRoleAssignmentAsync(_storePath, _applicationName, _scopeName, name);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return true;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Role Definition Operations

    public async Task<AzRoleDefinitionInfo?> CreateRoleDefinitionAsync(string name, string description)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            var role = await _azManService.CreateScopeRoleDefinitionAsync(_storePath, _applicationName, _scopeName, name, description);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return role;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task UpdateRoleDefinitionAsync(string name, string description)
    {
        await _azManService.UpdateScopeRoleDefinitionAsync(_storePath, _applicationName, _scopeName, name, description);
        await LoadAsync(_storePath, _applicationName, _scopeName);
    }

    public async Task AddTaskToRoleDefinitionAsync(string roleDefinitionName, string taskName)
    {
        await _azManService.AddTaskToScopeRoleDefinitionAsync(_storePath, _applicationName, _scopeName, roleDefinitionName, taskName);
    }

    public async Task RemoveTaskFromRoleDefinitionAsync(string roleDefinitionName, string taskName)
    {
        await _azManService.RemoveTaskFromScopeRoleDefinitionAsync(_storePath, _applicationName, _scopeName, roleDefinitionName, taskName);
    }

    public async Task AddOperationToRoleDefinitionAsync(string roleDefinitionName, string operationName)
    {
        await _azManService.AddOperationToScopeRoleDefinitionAsync(_storePath, _applicationName, _scopeName, roleDefinitionName, operationName);
    }

    public async Task RemoveOperationFromRoleDefinitionAsync(string roleDefinitionName, string operationName)
    {
        await _azManService.RemoveOperationFromScopeRoleDefinitionAsync(_storePath, _applicationName, _scopeName, roleDefinitionName, operationName);
    }

    public async Task SetRoleDefinitionBizRuleAsync(string roleDefinitionName, string bizRule, string bizRuleLanguage)
    {
        if (string.IsNullOrWhiteSpace(bizRule))
        {
            await _azManService.ClearScopeRoleDefinitionBizRuleAsync(_storePath, _applicationName, _scopeName, roleDefinitionName);
            return;
        }

        await _azManService.SetScopeRoleDefinitionBizRuleAsync(_storePath, _applicationName, _scopeName, roleDefinitionName, bizRule, bizRuleLanguage);
    }

    public async Task ImportRoleDefinitionBizRuleAsync(string roleDefinitionName, string scriptPath, string bizRuleLanguage)
    {
        await _azManService.ImportScopeRoleDefinitionBizRuleAsync(_storePath, _applicationName, _scopeName, roleDefinitionName, scriptPath, bizRuleLanguage);
    }

    public async Task<bool> DeleteRoleDefinitionAsync(string name)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            await _azManService.DeleteScopeRoleDefinitionAsync(_storePath, _applicationName, _scopeName, name);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return true;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Task Operations

    public async Task<AzTaskInfo?> CreateTaskAsync(string name, string description)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            var task = await _azManService.CreateScopeTaskAsync(_storePath, _applicationName, _scopeName, name, description);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return task;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task UpdateTaskAsync(string name, string description)
    {
        await _azManService.UpdateScopeTaskAsync(_storePath, _applicationName, _scopeName, name, description);
        await LoadAsync(_storePath, _applicationName, _scopeName);
    }

    public async Task AddOperationToTaskAsync(string taskName, string operationName)
    {
        await _azManService.AddOperationToScopeTaskAsync(_storePath, _applicationName, _scopeName, taskName, operationName);
    }

    public async Task RemoveOperationFromTaskAsync(string taskName, string operationName)
    {
        await _azManService.RemoveOperationFromScopeTaskAsync(_storePath, _applicationName, _scopeName, taskName, operationName);
    }

    public async Task AddTaskLinkAsync(string taskName, string linkedTaskName)
    {
        await _azManService.AddTaskLinkToScopeTaskAsync(_storePath, _applicationName, _scopeName, taskName, linkedTaskName);
    }

    public async Task RemoveTaskLinkAsync(string taskName, string linkedTaskName)
    {
        await _azManService.RemoveTaskLinkFromScopeTaskAsync(_storePath, _applicationName, _scopeName, taskName, linkedTaskName);
    }

    public async Task SetTaskBizRuleAsync(string taskName, string bizRule, string bizRuleLanguage)
    {
        if (string.IsNullOrWhiteSpace(bizRule))
        {
            await _azManService.ClearScopeTaskBizRuleAsync(_storePath, _applicationName, _scopeName, taskName);
            return;
        }

        await _azManService.SetScopeTaskBizRuleAsync(_storePath, _applicationName, _scopeName, taskName, bizRule, bizRuleLanguage);
    }

    public async Task ImportTaskBizRuleAsync(string taskName, string scriptPath, string bizRuleLanguage)
    {
        await _azManService.ImportScopeTaskBizRuleAsync(_storePath, _applicationName, _scopeName, taskName, scriptPath, bizRuleLanguage);
    }

    public async Task<bool> DeleteTaskAsync(string name)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            await _azManService.DeleteScopeTaskAsync(_storePath, _applicationName, _scopeName, name);
            await LoadAsync(_storePath, _applicationName, _scopeName);
            return true;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Filter Methods

    private void ApplyAllFilters()
    {
        ApplyGroupFilter();
        ApplyRoleAssignmentFilter();
        ApplyRoleDefinitionFilter();
        ApplyTaskFilter();
    }

    private void ApplyGroupFilter()
    {
        FilteredGroups.Clear();
        var filtered = string.IsNullOrWhiteSpace(GroupSearchText)
            ? Groups
            : Groups.Where(g => g.Name.Contains(GroupSearchText, StringComparison.OrdinalIgnoreCase) ||
                               g.Description.Contains(GroupSearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var g in filtered) FilteredGroups.Add(g);
        OnPropertyChanged(nameof(GroupCountText));
    }

    private void ApplyRoleAssignmentFilter()
    {
        FilteredRoleAssignments.Clear();
        var filtered = string.IsNullOrWhiteSpace(RoleAssignmentSearchText)
            ? RoleAssignments
            : RoleAssignments.Where(r => r.Name.Contains(RoleAssignmentSearchText, StringComparison.OrdinalIgnoreCase) ||
                                        r.Description.Contains(RoleAssignmentSearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var r in filtered) FilteredRoleAssignments.Add(r);
        OnPropertyChanged(nameof(RoleAssignmentCountText));
    }

    private void ApplyRoleDefinitionFilter()
    {
        FilteredRoleDefinitions.Clear();
        var filtered = string.IsNullOrWhiteSpace(RoleDefinitionSearchText)
            ? RoleDefinitions
            : RoleDefinitions.Where(r => r.Name.Contains(RoleDefinitionSearchText, StringComparison.OrdinalIgnoreCase) ||
                                        r.Description.Contains(RoleDefinitionSearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var r in filtered) FilteredRoleDefinitions.Add(r);
        OnPropertyChanged(nameof(RoleDefinitionCountText));
    }

    private void ApplyTaskFilter()
    {
        FilteredTasks.Clear();
        var filtered = string.IsNullOrWhiteSpace(TaskSearchText)
            ? Tasks
            : Tasks.Where(t => t.Name.Contains(TaskSearchText, StringComparison.OrdinalIgnoreCase) ||
                              t.Description.Contains(TaskSearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var t in filtered) FilteredTasks.Add(t);
        OnPropertyChanged(nameof(TaskCountText));
    }

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
