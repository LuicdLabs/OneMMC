// ============================================================================
// RoleAssignmentTasksDialog.xaml.cs
// 
// Role Assignment Tasks/Operations Dialog - For managing tasks and operations in a role assignment
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Core.Features.UserSecurity.Services.AzMan;
using ManagementTools.Localization;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Role Assignment Tasks/Operations Dialog
/// </summary>
public sealed partial class RoleAssignmentTasksDialog : ContentDialog
{
    private readonly AzManService _service;
    private readonly string _storePath;
    private readonly string _appName;
    private readonly string _roleName;
    private readonly string? _scopeName; // null for application-level, non-null for scope-level
    private readonly ObservableCollection<string> _tasks = [];
    private readonly ObservableCollection<string> _operations = [];
    private AzApplicationInfo? _appInfo;
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Whether changes were made
    /// </summary>
    public bool HasChanges { get; private set; }

    /// <summary>
    /// Create dialog for application-level role assignment
    /// </summary>
    public RoleAssignmentTasksDialog(
        AzManService service, 
        string storePath, 
        string appName, 
        AzRoleAssignmentInfo roleInfo)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        this.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        this.XamlRoot = this.XamlRoot;
        _service = service;
        _storePath = storePath;
        _appName = appName;
        _roleName = roleInfo.Name;
        _scopeName = null;

        Title = string.Format(LocalizedStrings.RoleAssignmentTasksDialog_TitleWithName, roleInfo.Name);
        
        TasksListView.ItemsSource = _tasks;
        OperationsListView.ItemsSource = _operations;
        
        TasksListView.SelectionChanged += (s, e) => RemoveTaskButton.IsEnabled = TasksListView.SelectedItem != null;
        OperationsListView.SelectionChanged += (s, e) => RemoveOperationButton.IsEnabled = OperationsListView.SelectedItem != null;

        LoadData(roleInfo);
    }

    /// <summary>
    /// Create dialog for scope-level role assignment
    /// </summary>
    public RoleAssignmentTasksDialog(
        AzManService service, 
        string storePath, 
        string appName,
        string scopeName,
        AzRoleAssignmentInfo roleInfo)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        this.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        this.XamlRoot = this.XamlRoot;
        _service = service;
        _storePath = storePath;
        _appName = appName;
        _roleName = roleInfo.Name;
        _scopeName = scopeName;

        Title = string.Format(LocalizedStrings.RoleAssignmentTasksDialog_TitleWithScope, roleInfo.Name, scopeName);
        
        TasksListView.ItemsSource = _tasks;
        OperationsListView.ItemsSource = _operations;
        
        TasksListView.SelectionChanged += (s, e) => RemoveTaskButton.IsEnabled = TasksListView.SelectedItem != null;
        OperationsListView.SelectionChanged += (s, e) => RemoveOperationButton.IsEnabled = OperationsListView.SelectedItem != null;

        LoadData(roleInfo);
    }

    /// <summary>
    /// Load initial data
    /// </summary>
    private void LoadData(AzRoleAssignmentInfo roleInfo)
    {
        foreach (var task in roleInfo.Tasks)
        {
            _tasks.Add(task);
        }

        foreach (var op in roleInfo.Operations)
        {
            _operations.Add(op);
        }

        UpdateEmptyStates();
    }

    /// <summary>
    /// Update empty state visibility
    /// </summary>
    private void UpdateEmptyStates()
    {
        NoTasksText.Visibility = _tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoOperationsText.Visibility = _operations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Add task button click
    /// </summary>
    private async void OnAddTaskClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // Get available tasks from application
            if (_appInfo == null)
            {
                _appInfo = await _service.GetApplicationAsync(_storePath, _appName);
            }

            // Get tasks that are not already assigned (including role definitions)
            var availableTasks = _appInfo.Tasks
                .Select(t => t.Name)
                .Concat(_appInfo.RoleDefinitions.Select(r => r.Name))
                .Where(t => !_tasks.Contains(t))
                .OrderBy(t => t)
                .ToList();

            if (availableTasks.Count == 0)
            {
                ShowMessage(LocalizedStrings.RoleAssignmentTasksDialog_Message_NoTasksAvailable, LocalizedStrings.Common_InformationTitle, InfoBarSeverity.Informational);
                return;
            }

            // Show selection dialog
            var dialog = new SelectItemDialog(
                LocalizedStrings.SelectItemDialog_Title_AddTask,
                LocalizedStrings.SelectItemDialog_Description_AddTask,
                availableTasks,
                LocalizedStrings.Common_AddButton,
                LocalizedStrings.Common_CancelButton)
            {
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme,
                XamlRoot = this.XamlRoot
            };


            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(dialog.SelectedItem))
            {
                await AddTaskAsync(dialog.SelectedItem);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_LoadTasksFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Add a task to the role assignment
    /// </summary>
    private async Task AddTaskAsync(string taskName)
    {
        try
        {
            if (_scopeName == null)
            {
                await _service.AddTaskToRoleAssignmentAsync(_storePath, _appName, _roleName, taskName);
            }
            else
            {
                await _service.AddTaskToScopeRoleAssignmentAsync(_storePath, _appName, _scopeName, _roleName, taskName);
            }

            _tasks.Add(taskName);
            HasChanges = true;
            UpdateEmptyStates();
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_TaskAdded, taskName), LocalizedStrings.RoleAssignmentTasksDialog_Title_TaskAdded, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_AddTaskFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Remove task button click
    /// </summary>
    private async void OnRemoveTaskClick(object sender, RoutedEventArgs e)
    {
        var selectedTask = TasksListView.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedTask)) return;

        try
        {
            if (_scopeName == null)
            {
                await _service.RemoveTaskFromRoleAssignmentAsync(_storePath, _appName, _roleName, selectedTask);
            }
            else
            {
                await _service.RemoveTaskFromScopeRoleAssignmentAsync(_storePath, _appName, _scopeName, _roleName, selectedTask);
            }

            _tasks.Remove(selectedTask);
            HasChanges = true;
            UpdateEmptyStates();
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_TaskRemoved, selectedTask), LocalizedStrings.RoleAssignmentTasksDialog_Title_TaskRemoved, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_RemoveTaskFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Add operation button click
    /// </summary>
    private async void OnAddOperationClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // Get available operations from application
            if (_appInfo == null)
            {
                _appInfo = await _service.GetApplicationAsync(_storePath, _appName);
            }

            // Get operations that are not already assigned
            var availableOps = _appInfo.Operations
                .Select(o => o.Name)
                .Where(o => !_operations.Contains(o))
                .OrderBy(o => o)
                .ToList();

            if (availableOps.Count == 0)
            {
                ShowMessage(LocalizedStrings.RoleAssignmentTasksDialog_Message_NoOperationsAvailable, LocalizedStrings.Common_InformationTitle, InfoBarSeverity.Informational);
                return;
            }

            // Show selection dialog
            var dialog = new SelectItemDialog(
                LocalizedStrings.SelectItemDialog_Title_AddOperation,
                LocalizedStrings.SelectItemDialog_Description_AddOperation,
                availableOps,
                LocalizedStrings.Common_AddButton,
                LocalizedStrings.Common_CancelButton)
            {
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(dialog.SelectedItem))
            {
                await AddOperationAsync(dialog.SelectedItem);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_LoadOperationsFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Add an operation to the role assignment
    /// </summary>
    private async Task AddOperationAsync(string operationName)
    {
        try
        {
            if (_scopeName == null)
            {
                await _service.AddOperationToRoleAssignmentAsync(_storePath, _appName, _roleName, operationName);
            }
            else
            {
                await _service.AddOperationToScopeRoleAssignmentAsync(_storePath, _appName, _scopeName, _roleName, operationName);
            }

            _operations.Add(operationName);
            HasChanges = true;
            UpdateEmptyStates();
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_OperationAdded, operationName), LocalizedStrings.RoleAssignmentTasksDialog_Title_OperationAdded, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_AddOperationFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Remove operation button click
    /// </summary>
    private async void OnRemoveOperationClick(object sender, RoutedEventArgs e)
    {
        var selectedOp = OperationsListView.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedOp)) return;

        try
        {
            if (_scopeName == null)
            {
                await _service.RemoveOperationFromRoleAssignmentAsync(_storePath, _appName, _roleName, selectedOp);
            }
            else
            {
                await _service.RemoveOperationFromScopeRoleAssignmentAsync(_storePath, _appName, _scopeName, _roleName, selectedOp);
            }

            _operations.Remove(selectedOp);
            HasChanges = true;
            UpdateEmptyStates();
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_OperationRemoved, selectedOp), LocalizedStrings.RoleAssignmentTasksDialog_Title_OperationRemoved, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(LocalizedStrings.RoleAssignmentTasksDialog_Message_RemoveOperationFailed, ex.Message), LocalizedStrings.Common_ErrorTitle, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Show message in InfoBar
    /// </summary>
    private void ShowMessage(string message, string title, InfoBarSeverity severity)
    {
        MessageInfoBar.Title = title;
        MessageInfoBar.Message = message;
        MessageInfoBar.Severity = severity;
        MessageInfoBar.IsOpen = true;
    }
}

/// <summary>
/// Simple item selection dialog
/// </summary>
public sealed class SelectItemDialog : ContentDialog
{
    private readonly ListView _listView;

    /// <summary>
    /// Selected item
    /// </summary>
    public string? SelectedItem { get; private set; }

    /// <summary>
    /// Create dialog
    /// </summary>
    public SelectItemDialog(string title, string description, IEnumerable<string> items, string primaryButtonText, string closeButtonText)
    {
        Title = title;
        PrimaryButtonText = primaryButtonText;
        CloseButtonText = closeButtonText;
        DefaultButton = ContentDialogButton.Primary;
        RequestedTheme = App.CurrentTheme;
        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        XamlRoot = this.XamlRoot;

        var panel = new StackPanel { Spacing = 12, MinWidth = 300 };
        panel.Children.Add(new TextBlock { Text = description });

        _listView = new ListView
        {
            ItemsSource = items,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 300
        };
        _listView.SelectionChanged += (s, e) => IsPrimaryButtonEnabled = _listView.SelectedItem != null;

        panel.Children.Add(_listView);
        Content = panel;

        IsPrimaryButtonEnabled = false;
        PrimaryButtonClick += (s, e) => SelectedItem = _listView.SelectedItem as string;
    }
}
