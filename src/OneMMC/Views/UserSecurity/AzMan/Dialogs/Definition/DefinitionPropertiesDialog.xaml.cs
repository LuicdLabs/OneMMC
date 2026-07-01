using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneMMC.Views.UserSecurity.AzMan.Dialogs;

public enum DefinitionItemType
{
    RoleDefinition,
    Task
}

public class DefinitionPropertiesResult
{
    public string Description { get; set; } = string.Empty;
    public string BizRuleLanguage { get; set; } = "VBScript";
    public string ScriptPath { get; set; } = string.Empty;
    public bool ReloadRuleIntoStore { get; set; }
    public bool ClearRuleFromStore { get; set; }
    public List<string> AddedRoles { get; set; } = [];
    public List<string> RemovedRoles { get; set; } = [];
    public List<string> AddedTasks { get; set; } = [];
    public List<string> RemovedTasks { get; set; } = [];
    public List<string> AddedOperations { get; set; } = [];
    public List<string> RemovedOperations { get; set; } = [];
}

public sealed partial class DefinitionPropertiesDialog : ContentDialog
{
    private readonly DefinitionItemType _itemType;
    private readonly List<AssignableItem> _availableRoles;
    private readonly List<AssignableItem> _availableTasks;
    private readonly List<AssignableItem> _availableOperations;

    private readonly HashSet<string> _selectedRoles;
    private readonly HashSet<string> _selectedTasks;
    private readonly HashSet<string> _selectedOperations;
    private readonly HashSet<string> _originalRoles;
    private readonly HashSet<string> _originalTasks;
    private readonly HashSet<string> _originalOperations;

    private string _bizRuleLanguage = "VBScript";
    private string _bizRule = string.Empty;
    private string _scriptPath = string.Empty;
    private bool _reloadRuleIntoStore;
    private bool _clearRuleFromStore;

    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;
    private bool _isTemporarilyHidden;
    private System.Threading.Tasks.TaskCompletionSource<bool>? _subDialogCompletionSource;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DefinitionPropertiesResult? Result { get; private set; }

    /// <summary>
    /// Shows the dialog and handles temporary hide/re-show cycles triggered by sub-dialogs.
    /// Returns Primary if the user saved, None/Secondary if cancelled.
    /// </summary>
    public async Task<ContentDialogResult> ShowDialogAsync()
    {
        ContentDialogResult result;
        do
        {
            _isTemporarilyHidden = false;
            _subDialogCompletionSource = null;
            result = await ShowAsync();

            // If temporarily hidden, wait for sub-dialog to complete
            if (_isTemporarilyHidden && _subDialogCompletionSource != null)
            {
                await _subDialogCompletionSource.Task;
                
                // Yield to the UI thread to ensure the previous dialog is fully cleaned up
                var tcs = new TaskCompletionSource<bool>();
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, 
                    () => tcs.TrySetResult(true));
                await tcs.Task;
            }
        }
        while (_isTemporarilyHidden);

        return result;
    }

    public DefinitionPropertiesDialog(
        DefinitionItemType itemType,
        string name,
        string description,
        string bizRule,
        string bizRuleLanguage,
        string scriptPath,
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

        _availableRoles = availableRoles.Select(item => new AssignableItem
        {
            Name = item.Name,
            Description = item.Description,
            OperationId = item.OperationId,
            Tag = item.Tag
        }).ToList();
        _availableTasks = availableTasks.Select(item => new AssignableItem
        {
            Name = item.Name,
            Description = item.Description,
            OperationId = item.OperationId,
            Tag = item.Tag
        }).ToList();
        _availableOperations = availableOperations.Select(item => new AssignableItem
        {
            Name = item.Name,
            Description = item.Description,
            OperationId = item.OperationId,
            Tag = item.Tag
        }).ToList();

        _selectedRoles = new HashSet<string>(assignedRoles, StringComparer.OrdinalIgnoreCase);
        _selectedTasks = new HashSet<string>(assignedTasks, StringComparer.OrdinalIgnoreCase);
        _selectedOperations = new HashSet<string>(assignedOperations, StringComparer.OrdinalIgnoreCase);
        _originalRoles = new HashSet<string>(_selectedRoles, StringComparer.OrdinalIgnoreCase);
        _originalTasks = new HashSet<string>(_selectedTasks, StringComparer.OrdinalIgnoreCase);
        _originalOperations = new HashSet<string>(_selectedOperations, StringComparer.OrdinalIgnoreCase);

        Title = itemType == DefinitionItemType.RoleDefinition
            ? _localizedStrings.DefinitionPropertiesDialog_Title_RoleDefinition
            : _localizedStrings.DefinitionPropertiesDialog_Title_Task;

        NameTextBox.Text = name;
        DescriptionTextBox.Text = description ?? string.Empty;
        _bizRule = bizRule ?? string.Empty;
        _scriptPath = scriptPath ?? string.Empty;
        _bizRuleLanguage = string.IsNullOrWhiteSpace(bizRuleLanguage) ? "VBScript" : bizRuleLanguage;

        UpdateAssignItemsSummary();
        UpdateAuthorizationRuleSummary();
    }

    private void SelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        GeneralPanel.Visibility = Visibility.Collapsed;
        DefinitionPanel.Visibility = Visibility.Collapsed;

        if (sender.SelectedItem is SelectorBarItem { Tag: string tag })
        {
            switch (tag)
            {
                case "General":
                    GeneralPanel.Visibility = Visibility.Visible;
                    break;
                case "Definition":
                    DefinitionPanel.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = new DefinitionPropertiesResult
        {
            Description = DescriptionTextBox.Text,
            BizRuleLanguage = _bizRuleLanguage,
            ScriptPath = _scriptPath.Trim(),
            ReloadRuleIntoStore = _reloadRuleIntoStore,
            ClearRuleFromStore = _clearRuleFromStore,
            AddedRoles = _selectedRoles.Except(_originalRoles, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedRoles = _originalRoles.Except(_selectedRoles, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedTasks = _selectedTasks.Except(_originalTasks, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedTasks = _originalTasks.Except(_selectedTasks, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedOperations = _selectedOperations.Except(_originalOperations, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedOperations = _originalOperations.Except(_selectedOperations, StringComparer.OrdinalIgnoreCase).ToList()
        };

        ErrorInfoBar.IsOpen = false;
    }

    private async void OnOpenAssignItemsClick(object sender, RoutedEventArgs e)
    {
        _subDialogCompletionSource = new System.Threading.Tasks.TaskCompletionSource<bool>();

        var result = await DefinitionAssignItemsDialog.ShowDialogAsync(
            XamlRoot,
            _itemType,
            NameTextBox.Text,
            _availableRoles,
            _selectedRoles,
            _availableTasks,
            _selectedTasks,
            _availableOperations,
            _selectedOperations);

        if (result is not null)
        {
            foreach (var role in result.AddedRoles) _selectedRoles.Add(role);
            foreach (var role in result.RemovedRoles) _selectedRoles.Remove(role);
            foreach (var task in result.AddedTasks) _selectedTasks.Add(task);
            foreach (var task in result.RemovedTasks) _selectedTasks.Remove(task);
            foreach (var op in result.AddedOperations) _selectedOperations.Add(op);
            foreach (var op in result.RemovedOperations) _selectedOperations.Remove(op);
            UpdateAssignItemsSummary();
        }

        _subDialogCompletionSource.TrySetResult(true);
    }

    private async void OnOpenAuthorizationRuleClick(object sender, RoutedEventArgs e)
    {
        _subDialogCompletionSource = new System.Threading.Tasks.TaskCompletionSource<bool>();

        var result = await AuthorizationRuleDialog.ShowDialogAsync(
            XamlRoot,
            _bizRule,
            _bizRuleLanguage,
            _scriptPath,
            _reloadRuleIntoStore,
            _clearRuleFromStore);

        if (result is not null)
        {
            _bizRule = result.BizRule;
            _bizRuleLanguage = result.BizRuleLanguage;
            _scriptPath = result.ScriptPath;
            _reloadRuleIntoStore = result.ReloadRuleIntoStore;
            _clearRuleFromStore = result.ClearRuleFromStore;
            UpdateAuthorizationRuleSummary();
            ErrorInfoBar.IsOpen = false;
        }

        _subDialogCompletionSource.TrySetResult(true);
    }

    private void UpdateAssignItemsSummary()
    {
        AssignItemsSummaryTextBlock.Text = string.Format(
            _localizedStrings.DefinitionPropertiesDialog_Summary_Format,
            _selectedRoles.Count,
            _selectedTasks.Count,
            _selectedOperations.Count);
    }

    private void UpdateAuthorizationRuleSummary()
    {
        var actionText = _clearRuleFromStore
            ? _localizedStrings.DefinitionPropertiesDialog_Action_ClearFromStore
            : _reloadRuleIntoStore
                ? _localizedStrings.DefinitionPropertiesDialog_Action_ReloadIntoStore
                : _localizedStrings.DefinitionPropertiesDialog_Action_NoAction;

        var pathText = string.IsNullOrWhiteSpace(_scriptPath) 
            ? _localizedStrings.DefinitionPropertiesDialog_Path_None 
            : _scriptPath;
        AuthorizationRuleSummaryTextBlock.Text = $"{actionText} | {_bizRuleLanguage} | {pathText}";
    }
}

