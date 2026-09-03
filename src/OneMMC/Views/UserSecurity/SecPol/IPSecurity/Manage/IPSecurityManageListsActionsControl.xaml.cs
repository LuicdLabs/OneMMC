using System.Collections.ObjectModel;
using System.Globalization;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Manage;

/// <summary>
/// MMC-style "Manage IP filter lists and filter actions" dialog content. Presents the shared
/// filter lists and filter actions of the legacy static IPsec store on two tabs, each supporting
/// add, edit, and remove against the owning view model's mutation callbacks.
/// </summary>
public sealed partial class IPSecurityManageListsActionsControl : UserControl
{
    private const int ManagerDialogWidth = 900;
    private const int ManagerDialogHeight = 640;
    private const int EditorDialogWidth = 820;
    private const int EditorDialogHeight = 720;
    private const int ConfirmationDialogWidth = 520;
    private const int ConfirmationDialogHeight = 300;

    private readonly Func<IPSecurityFilterListCommandOptions, IReadOnlyList<IPSecurityFilterCommandOptions>, Task<bool>> _addFilterListAsync;
    private readonly Func<IPSecurityFilterListDefinition, IPSecurityFilterListCommandOptions, IReadOnlyList<IPSecurityFilterCommandOptions>, Task<bool>> _setFilterListAsync;
    private readonly Func<string, Task<bool>> _deleteFilterListAsync;
    private readonly Func<IPSecurityFilterActionCommandOptions, Task<bool>> _addFilterActionAsync;
    private readonly Func<IPSecurityFilterActionCommandOptions, Task<bool>> _setFilterActionAsync;
    private readonly Func<string, Task<bool>> _deleteFilterActionAsync;
    private bool _isBusy;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private ObservableCollection<IPSecurityManageListItem> FilterListItems { get; } = [];

    private ObservableCollection<IPSecurityManageListItem> FilterActionItems { get; } = [];

    /// <summary>
    /// Initializes the manage dialog with the current shared definitions and the mutation callbacks
    /// that persist changes through the owning view model.
    /// </summary>
    /// <param name="filterLists">The current shared filter-list definitions.</param>
    /// <param name="filterActions">The current shared filter-action definitions.</param>
    /// <param name="addFilterListAsync">Callback that creates a filter list with its filters.</param>
    /// <param name="setFilterListAsync">Callback that updates a filter list with its filters.</param>
    /// <param name="deleteFilterListAsync">Callback that deletes a filter list by name.</param>
    /// <param name="addFilterActionAsync">Callback that creates a filter action.</param>
    /// <param name="setFilterActionAsync">Callback that updates a filter action.</param>
    /// <param name="deleteFilterActionAsync">Callback that deletes a filter action by name.</param>
    public IPSecurityManageListsActionsControl(
        IReadOnlyList<IPSecurityFilterListDefinition> filterLists,
        IReadOnlyList<IPSecurityFilterActionDefinition> filterActions,
        Func<IPSecurityFilterListCommandOptions, IReadOnlyList<IPSecurityFilterCommandOptions>, Task<bool>> addFilterListAsync,
        Func<IPSecurityFilterListDefinition, IPSecurityFilterListCommandOptions, IReadOnlyList<IPSecurityFilterCommandOptions>, Task<bool>> setFilterListAsync,
        Func<string, Task<bool>> deleteFilterListAsync,
        Func<IPSecurityFilterActionCommandOptions, Task<bool>> addFilterActionAsync,
        Func<IPSecurityFilterActionCommandOptions, Task<bool>> setFilterActionAsync,
        Func<string, Task<bool>> deleteFilterActionAsync)
    {
        ArgumentNullException.ThrowIfNull(filterLists);
        ArgumentNullException.ThrowIfNull(filterActions);
        ArgumentNullException.ThrowIfNull(addFilterListAsync);
        ArgumentNullException.ThrowIfNull(setFilterListAsync);
        ArgumentNullException.ThrowIfNull(deleteFilterListAsync);
        ArgumentNullException.ThrowIfNull(addFilterActionAsync);
        ArgumentNullException.ThrowIfNull(setFilterActionAsync);
        ArgumentNullException.ThrowIfNull(deleteFilterActionAsync);

        _addFilterListAsync = addFilterListAsync;
        _setFilterListAsync = setFilterListAsync;
        _deleteFilterListAsync = deleteFilterListAsync;
        _addFilterActionAsync = addFilterActionAsync;
        _setFilterActionAsync = setFilterActionAsync;
        _deleteFilterActionAsync = deleteFilterActionAsync;

        InitializeComponent();

        foreach (IPSecurityFilterListDefinition filterList in filterLists)
        {
            FilterListItems.Add(CreateFilterListItem(filterList));
        }

        foreach (IPSecurityFilterActionDefinition filterAction in filterActions)
        {
            FilterActionItems.Add(CreateFilterActionItem(filterAction));
        }

        SectionSelectorBar.SelectedItem = FilterListsTab;
        ItemsListView.ItemsSource = FilterListItems;
        UpdateCommandState();
        FilterListItems.CollectionChanged += (_, _) => UpdateEmptyState();
        FilterActionItems.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    /// <summary>
    /// Shows or hides the empty state for whichever collection the SelectorBar has in view, and
    /// keeps its message in step with the selected tab.
    /// </summary>
    private void UpdateEmptyState()
    {
        bool isLists = IsFilterListsTab;
        int count = isLists ? FilterListItems.Count : FilterActionItems.Count;
        EmptyItemsText.Text = isLists
            ? LocalizedStrings.IPSec_Empty_FilterLists
            : LocalizedStrings.IPSec_Empty_FilterActions;
        EmptyItemsText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows the manage dialog in a modal window owned by the supplied XAML root.
    /// </summary>
    /// <param name="ownerXamlRoot">The owning XAML root.</param>
    /// <returns>The result produced when the manage window closes.</returns>
    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        ArgumentNullException.ThrowIfNull(ownerXamlRoot);

        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.IPSec_Dialog_ManageFiltersActions_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            DefaultButton = WindowDialogResult.None,
            Width = ManagerDialogWidth,
            Height = ManagerDialogHeight
        });

        return modalWindow.ShowDialogAsync();
    }

    private bool IsFilterListsTab => SectionSelectorBar.SelectedItem == FilterListsTab;

    private void SectionSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ItemsListView.ItemsSource = IsFilterListsTab ? FilterListItems : FilterActionItems;
        UpdateCommandState();
        UpdateEmptyState();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsFilterListsTab)
        {
            await AddFilterListAsync();
        }
        else
        {
            await AddFilterActionAsync();
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedAsync();
    }

    private async void ItemsListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await EditSelectedAsync();
    }

    private void ItemsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandState();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsListView.SelectedItem is not IPSecurityManageListItem selected)
        {
            return;
        }

        string message = string.Format(
            CultureInfo.CurrentCulture,
            IsFilterListsTab
                ? LocalizedStrings.IPSec_DeleteFilterList_MessageFormat
                : LocalizedStrings.IPSec_DeleteFilterAction_MessageFormat,
            selected.Name);
        if (!await ConfirmDeleteAsync(message))
        {
            return;
        }

        bool isListsTab = IsFilterListsTab;
        Func<Task<bool>> delete = isListsTab
            ? () => _deleteFilterListAsync(selected.Name)
            : () => _deleteFilterActionAsync(selected.Name);
        if (!await RunMutationAsync(delete))
        {
            return;
        }

        (isListsTab ? FilterListItems : FilterActionItems).Remove(selected);
    }

    private async Task EditSelectedAsync()
    {
        if (ItemsListView.SelectedItem is not IPSecurityManageListItem selected)
        {
            return;
        }

        if (IsFilterListsTab)
        {
            await EditFilterListAsync(selected);
        }
        else
        {
            await EditFilterActionAsync(selected);
        }
    }

    private async Task AddFilterListAsync()
    {
        var editor = new IPSecurityFilterListEditorControl(IPSecurityEditorMode.Create);
        IPSecurityFilterListEditorResult? result = await ShowFilterListEditorAsync(
            editor,
            LocalizedStrings.IPSec_Dialog_CreateFilterList_Title,
            LocalizedStrings.Common_CreateButton);
        if (result is null || !await RunMutationAsync(() => _addFilterListAsync(result.Options, result.Filters)))
        {
            return;
        }

        IPSecurityManageListItem item = CreateFilterListItem(BuildFilterListDefinition(result));
        FilterListItems.Add(item);
        ItemsListView.SelectedItem = item;
    }

    private async Task EditFilterListAsync(IPSecurityManageListItem selected)
    {
        IPSecurityFilterListDefinition? original = selected.FilterList;
        if (original is null)
        {
            return;
        }

        var editor = new IPSecurityFilterListEditorControl(IPSecurityEditorMode.Edit, original);
        IPSecurityFilterListEditorResult? result = await ShowFilterListEditorAsync(
            editor,
            string.Format(CultureInfo.CurrentCulture, LocalizedStrings.IPSec_Dialog_EditFilterList_TitleFormat, original.Name),
            LocalizedStrings.Common_SaveButton);
        if (result is null || !await RunMutationAsync(() => _setFilterListAsync(original, result.Options, result.Filters)))
        {
            return;
        }

        int index = FilterListItems.IndexOf(selected);
        FilterListItems[index] = CreateFilterListItem(BuildFilterListDefinition(result));
        ItemsListView.SelectedIndex = index;
    }

    private async Task AddFilterActionAsync()
    {
        var editor = new IPSecurityFilterActionEditorControl(IPSecurityEditorMode.Create);
        IPSecurityFilterActionCommandOptions? options = await ShowFilterActionEditorAsync(
            editor,
            LocalizedStrings.IPSec_Dialog_CreateFilterAction_Title,
            LocalizedStrings.Common_CreateButton);
        if (options is null || !await RunMutationAsync(() => _addFilterActionAsync(options)))
        {
            return;
        }

        IPSecurityManageListItem item = CreateFilterActionItem(BuildFilterActionDefinition(options));
        FilterActionItems.Add(item);
        ItemsListView.SelectedItem = item;
    }

    private async Task EditFilterActionAsync(IPSecurityManageListItem selected)
    {
        IPSecurityFilterActionDefinition? original = selected.FilterAction;
        if (original is null)
        {
            return;
        }

        var editor = new IPSecurityFilterActionEditorControl(IPSecurityEditorMode.Edit, original);
        IPSecurityFilterActionCommandOptions? options = await ShowFilterActionEditorAsync(
            editor,
            string.Format(CultureInfo.CurrentCulture, LocalizedStrings.IPSec_Dialog_EditFilterAction_TitleFormat, original.Name),
            LocalizedStrings.Common_SaveButton);
        if (options is null || !await RunMutationAsync(() => _setFilterActionAsync(options)))
        {
            return;
        }

        int index = FilterActionItems.IndexOf(selected);
        FilterActionItems[index] = CreateFilterActionItem(BuildFilterActionDefinition(options, original));
        ItemsListView.SelectedIndex = index;
    }

    private async Task<IPSecurityFilterListEditorResult?> ShowFilterListEditorAsync(
        IPSecurityFilterListEditorControl editor,
        string title,
        string primaryButtonText)
    {
        IPSecurityFilterListEditorResult? result = null;
        return await ShowWindowEditorAsync(title, editor, primaryButtonText, () => editor.TryBuildResult(out result)) == WindowDialogResult.Primary
            ? result
            : null;
    }

    private async Task<IPSecurityFilterActionCommandOptions?> ShowFilterActionEditorAsync(
        IPSecurityFilterActionEditorControl editor,
        string title,
        string primaryButtonText)
    {
        IPSecurityFilterActionCommandOptions? options = null;
        return await ShowInlineEditorAsync(title, editor, primaryButtonText, () => editor.TryBuildResult(out options)) == WindowDialogResult.Primary
            ? options
            : null;
    }

    /// <summary>
    /// Hosts an editor that opens a further dialog of its own in a real window.
    /// </summary>
    /// <remarks>
    /// The filter-list editor adds and edits individual filters, and WinUI allows one
    /// <c>ContentDialog</c> per XAML root. Giving the editor its own window gives it its own root,
    /// which is what lets the filter dialog be an ordinary ContentDialog.
    /// </remarks>
    private Task<WindowDialogResult> ShowWindowEditorAsync(
        string title,
        UserControl editor,
        string primaryButtonText,
        Func<bool> validate)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = EditorDialogWidth,
            Height = EditorDialogHeight,
            OnPrimaryButtonClick = validate
        });

        return modalWindow.ShowDialogAsync();
    }

    /// <summary>Hosts a leaf editor, which needs nothing more than a ContentDialog.</summary>
    private Task<WindowDialogResult> ShowInlineEditorAsync(
        string title,
        UserControl editor,
        string primaryButtonText,
        Func<bool> validate)
    {
        return InlineDialogHost.ShowAsync(new InlineDialogOptions
        {
            Title = title,
            Content = editor,
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            MaxWidth = EditorDialogWidth,
            MaxHeight = EditorDialogHeight,
            OnPrimaryButtonClick = validate
        });
    }

    private async Task<bool> ConfirmDeleteAsync(string message)
    {
        return await InlineDialogHost.ShowAsync(new InlineDialogOptions
        {
            Title = LocalizedStrings.IPSec_DeleteConfirm_Title,
            Content = message,
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.None,
            MaxWidth = ConfirmationDialogWidth,
            MaxHeight = ConfirmationDialogHeight
        }) == WindowDialogResult.Primary;
    }

    private async Task<bool> RunMutationAsync(Func<Task<bool>> mutation)
    {
        SetBusyState(true);
        ErrorInfoBar.IsOpen = false;
        try
        {
            bool succeeded = await mutation();
            ErrorInfoBar.IsOpen = !succeeded;
            return succeeded;
        }
        catch (Exception)
        {
            ErrorInfoBar.IsOpen = true;
            return false;
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool isBusy)
    {
        _isBusy = isBusy;
        BusyProgressRing.IsActive = isBusy;
        BusyProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        ItemsListView.IsEnabled = !isBusy;
        SectionSelectorBar.IsEnabled = !isBusy;
        UpdateCommandState();
    }

    private void UpdateCommandState()
    {
        if (AddButton is null)
        {
            return;
        }

        bool hasSelection = ItemsListView?.SelectedItem is IPSecurityManageListItem;
        AddButton.IsEnabled = !_isBusy;
        EditButton.IsEnabled = !_isBusy && hasSelection;
        DeleteButton.IsEnabled = !_isBusy && hasSelection;
    }

    private static IPSecurityManageListItem CreateFilterListItem(IPSecurityFilterListDefinition filterList)
    {
        return new IPSecurityManageListItem
        {
            Name = filterList.Name,
            Description = filterList.Description,
            FilterList = filterList
        };
    }

    private static IPSecurityManageListItem CreateFilterActionItem(IPSecurityFilterActionDefinition filterAction)
    {
        return new IPSecurityManageListItem
        {
            Name = filterAction.Name,
            Description = filterAction.Description,
            FilterAction = filterAction
        };
    }

    private static IPSecurityFilterListDefinition BuildFilterListDefinition(IPSecurityFilterListEditorResult result)
    {
        string name = result.Options.NewName ?? result.Options.Name;
        return new IPSecurityFilterListDefinition
        {
            Name = name,
            Description = result.Options.Description ?? string.Empty,
            Filters = result.Filters.Select(filter => ToFilterDefinition(filter, name)).ToList()
        };
    }

    private static IPSecurityFilterDefinition ToFilterDefinition(IPSecurityFilterCommandOptions filter, string filterListName)
    {
        return new IPSecurityFilterDefinition
        {
            FilterListName = filterListName,
            Description = filter.Description ?? string.Empty,
            SourceAddress = filter.SourceAddress,
            SourceMask = filter.SourceMask ?? string.Empty,
            DestinationAddress = filter.DestinationAddress,
            DestinationMask = filter.DestinationMask ?? string.Empty,
            Protocol = filter.Protocol ?? string.Empty,
            SourcePort = filter.SourcePort ?? 0,
            DestinationPort = filter.DestinationPort ?? 0,
            IsMirrored = filter.IsMirrored ?? false
        };
    }

    private static IPSecurityFilterActionDefinition BuildFilterActionDefinition(
        IPSecurityFilterActionCommandOptions options,
        IPSecurityFilterActionDefinition? existing = null)
    {
        return new IPSecurityFilterActionDefinition
        {
            Name = options.NewName ?? options.Name,
            Description = options.Description ?? existing?.Description ?? string.Empty,
            Action = options.Action ?? existing?.Action ?? IPSecurityFilterActionKind.Permit,
            UseQuickModePerfectForwardSecrecy = options.UseQuickModePerfectForwardSecrecy ?? existing?.UseQuickModePerfectForwardSecrecy ?? false,
            AcceptUnsecuredInbound = options.AcceptUnsecuredInbound ?? existing?.AcceptUnsecuredInbound ?? false,
            AllowUnsecuredFallback = options.AllowUnsecuredFallback ?? existing?.AllowUnsecuredFallback ?? false,
            QuickModeSecurityMethods = options.QuickModeSecurityMethods ?? existing?.QuickModeSecurityMethods ?? []
        };
    }
}

/// <summary>
/// A row shown in the manage filter lists and filter actions dialog. Holds the underlying
/// definition so the editor can be re-opened against the current values.
/// </summary>
internal sealed class IPSecurityManageListItem
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    internal IPSecurityFilterListDefinition? FilterList { get; init; }

    internal IPSecurityFilterActionDefinition? FilterAction { get; init; }
}
