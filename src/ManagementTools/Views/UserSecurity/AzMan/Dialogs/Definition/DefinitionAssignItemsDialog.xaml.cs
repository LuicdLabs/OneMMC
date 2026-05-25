using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

public class DefinitionAssignItemsResult
{
    public List<string> AddedRoles { get; set; } = [];
    public List<string> RemovedRoles { get; set; } = [];
    public List<string> AddedTasks { get; set; } = [];
    public List<string> RemovedTasks { get; set; } = [];
    public List<string> AddedOperations { get; set; } = [];
    public List<string> RemovedOperations { get; set; } = [];
}

public sealed partial class DefinitionAssignItemsDialog : UserControl
{
    private readonly DefinitionItemType _itemType;
    private readonly ObservableCollection<AssignableItem> _roles = [];
    private readonly ObservableCollection<AssignableItem> _tasks = [];
    private readonly ObservableCollection<AssignableItem> _operations = [];

    private readonly HashSet<string> _originalRoles;
    private readonly HashSet<string> _originalTasks;
    private readonly HashSet<string> _originalOperations;

    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    public DefinitionAssignItemsResult? Result { get; private set; }

    private DefinitionAssignItemsDialog(
        DefinitionItemType itemType,
        string targetName,
        IEnumerable<AssignableItem> availableRoles,
        IEnumerable<string> assignedRoles,
        IEnumerable<AssignableItem> availableTasks,
        IEnumerable<string> assignedTasks,
        IEnumerable<AssignableItem> availableOperations,
        IEnumerable<string> assignedOperations)
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;

        _itemType = itemType;

        HeaderTextBlock.Text = itemType == DefinitionItemType.Task
            ? _localizedStrings.DefinitionAssignItemsDialog_Header_Task
            : _localizedStrings.DefinitionAssignItemsDialog_Header_Role;

        _originalRoles = new HashSet<string>(assignedRoles, System.StringComparer.OrdinalIgnoreCase);
        _originalTasks = new HashSet<string>(assignedTasks, System.StringComparer.OrdinalIgnoreCase);
        _originalOperations = new HashSet<string>(assignedOperations, System.StringComparer.OrdinalIgnoreCase);

        foreach (var role in availableRoles)
            _roles.Add(new AssignableItem { Name = role.Name, Description = role.Description, OperationId = role.OperationId, Tag = role.Tag, IsSelected = _originalRoles.Contains(role.Name) });

        foreach (var task in availableTasks)
            _tasks.Add(new AssignableItem { Name = task.Name, Description = task.Description, OperationId = task.OperationId, Tag = task.Tag, IsSelected = _originalTasks.Contains(task.Name) });

        foreach (var operation in availableOperations)
            _operations.Add(new AssignableItem { Name = operation.Name, Description = operation.Description, OperationId = operation.OperationId, Tag = operation.Tag, IsSelected = _originalOperations.Contains(operation.Name) });

        RolesListView.ItemsSource = _roles;
        TasksListView.ItemsSource = _tasks;
        OperationsListView.ItemsSource = _operations;
        ConfigureTabVisibility();
    }

    /// <summary>
    /// Shows the dialog as a modal window and returns the result.
    /// </summary>
    public static async Task<DefinitionAssignItemsResult?> ShowDialogAsync(
        XamlRoot ownerXamlRoot,
        DefinitionItemType itemType,
        string targetName,
        IEnumerable<AssignableItem> availableRoles,
        IEnumerable<string> assignedRoles,
        IEnumerable<AssignableItem> availableTasks,
        IEnumerable<string> assignedTasks,
        IEnumerable<AssignableItem> availableOperations,
        IEnumerable<string> assignedOperations)
    {
        var dialog = new DefinitionAssignItemsDialog(
            itemType,
            targetName,
            availableRoles,
            assignedRoles,
            availableTasks,
            assignedTasks,
            availableOperations,
            assignedOperations);

        var tcs = new TaskCompletionSource<DefinitionAssignItemsResult?>();

        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = string.Format(LocalizedStrings.Instance.DefinitionAssignItemsDialog_Title_Format, targetName),
            Content = dialog,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Instance.Common_OKButton,
            CloseButtonText = LocalizedStrings.Instance.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 640,
            Height = 520,
            OnPrimaryButtonClick = () =>
            {
                var currentRoles = new HashSet<string>(dialog._roles.Where(i => i.IsSelected).Select(i => i.Name), System.StringComparer.OrdinalIgnoreCase);
                var currentTasks = new HashSet<string>(dialog._tasks.Where(i => i.IsSelected).Select(i => i.Name), System.StringComparer.OrdinalIgnoreCase);
                var currentOperations = new HashSet<string>(dialog._operations.Where(i => i.IsSelected).Select(i => i.Name), System.StringComparer.OrdinalIgnoreCase);

                dialog.Result = new DefinitionAssignItemsResult
                {
                    AddedRoles = currentRoles.Except(dialog._originalRoles, System.StringComparer.OrdinalIgnoreCase).ToList(),
                    RemovedRoles = dialog._originalRoles.Except(currentRoles, System.StringComparer.OrdinalIgnoreCase).ToList(),
                    AddedTasks = currentTasks.Except(dialog._originalTasks, System.StringComparer.OrdinalIgnoreCase).ToList(),
                    RemovedTasks = dialog._originalTasks.Except(currentTasks, System.StringComparer.OrdinalIgnoreCase).ToList(),
                    AddedOperations = currentOperations.Except(dialog._originalOperations, System.StringComparer.OrdinalIgnoreCase).ToList(),
                    RemovedOperations = dialog._originalOperations.Except(currentOperations, System.StringComparer.OrdinalIgnoreCase).ToList()
                };

                return true;
            }
        });

        var result = await modalWindow.ShowDialogAsync();
        return result == WindowDialogResult.Primary ? dialog.Result : null;
    }

    private void ConfigureTabVisibility()
    {
        if (_itemType == DefinitionItemType.Task)
        {
            AssignSelectorBar.Items.Remove(RolesTabItem);
            RolesListView.Visibility = Visibility.Collapsed;
            TasksListView.Visibility = Visibility.Visible;
            OperationsListView.Visibility = Visibility.Collapsed;
            AssignSelectorBar.SelectedItem = TasksTabItem;
            return;
        }

        AssignSelectorBar.SelectedItem = RolesTabItem;
        RolesListView.Visibility = Visibility.Visible;
        TasksListView.Visibility = Visibility.Collapsed;
        OperationsListView.Visibility = Visibility.Collapsed;
    }

    private void AssignSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        RolesListView.Visibility = Visibility.Collapsed;
        TasksListView.Visibility = Visibility.Collapsed;
        OperationsListView.Visibility = Visibility.Collapsed;

        if (sender.SelectedItem is SelectorBarItem { Tag: string tag })
        {
            switch (tag)
            {
                case "Roles": RolesListView.Visibility = Visibility.Visible; break;
                case "Tasks": TasksListView.Visibility = Visibility.Visible; break;
                case "Operations": OperationsListView.Visibility = Visibility.Visible; break;
            }
        }
    }
}