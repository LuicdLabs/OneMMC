using System.Collections.ObjectModel;
using System.Globalization;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;

/// <summary>
/// Edits a legacy static IPsec filter list and an in-memory collection of filters.
/// </summary>
public sealed partial class IPSecurityFilterListEditorControl : UserControl
{
    private const string PendingFilterListName = "__pending_filter_list__";
    private const int FilterDialogWidth = 720;
    private const int FilterDialogHeight = 640;

    private readonly IPSecurityEditorMode _mode;
    private readonly string _originalName;
    private readonly IReadOnlyList<IPSecurityFilterCommandOptions> _originalFilters;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>Gets the editable in-memory filter items.</summary>
    internal ObservableCollection<IPSecurityFilterEditorItem> FilterItems { get; } = [];

    /// <summary>
    /// Shows or hides the list's empty state. Driven from code-behind rather than a binding so the
    /// filter collection stays a plain <see cref="ObservableCollection{T}"/> with no wrapper.
    /// </summary>
    private void UpdateEmptyState()
    {
        EmptyFiltersText.Visibility = FilterItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Initializes a filter-list editor.
    /// </summary>
    /// <param name="mode">The editor mode.</param>
    /// <param name="filterList">The filter list to edit, or <see langword="null"/> when creating one.</param>
    public IPSecurityFilterListEditorControl(
        IPSecurityEditorMode mode,
        IPSecurityFilterListDefinition? filterList = null)
    {
        if (mode == IPSecurityEditorMode.Edit)
        {
            ArgumentNullException.ThrowIfNull(filterList);
        }

        _mode = mode;
        _originalName = filterList?.Name ?? string.Empty;
        _originalFilters = filterList?.Filters
            .Select(IPSecurityEditorValidation.ToFilterOptions)
            .ToList()
            ?? [];

        InitializeComponent();
        NameTextBox.Text = filterList?.Name ?? string.Empty;
        DescriptionTextBox.Text = filterList?.Description ?? string.Empty;
        foreach (IPSecurityFilterCommandOptions filter in _originalFilters)
        {
            FilterItems.Add(CreateFilterItem(filter));
        }

        FilterItems.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    /// <summary>
    /// Builds and validates the filter-list options and all current in-memory filters.
    /// </summary>
    /// <param name="result">The validated result, or <see langword="null"/> when validation fails.</param>
    /// <returns><see langword="true"/> when the result is valid.</returns>
    public bool TryBuildResult(out IPSecurityFilterListEditorResult? result)
    {
        string currentName = NameTextBox.Text;
        var options = new IPSecurityFilterListCommandOptions
        {
            Name = _mode == IPSecurityEditorMode.Create ? currentName : _originalName,
            NewName = IPSecurityEditorValidation.RenamedValue(_mode, _originalName, currentName),
            Description = DescriptionTextBox.Text
        };

        List<IPSecurityFilterCommandOptions> filters = FilterItems
            .Select(item => IPSecurityEditorValidation.WithFilterListName(item.Options, currentName))
            .ToList();

        bool isValid = IPSecurityEditorValidation.TryValidate(
            () =>
            {
                if (_mode == IPSecurityEditorMode.Create)
                {
                    _ = IPSecurityCommandBuilder.BuildAddFilterList(options);
                }
                else
                {
                    _ = IPSecurityCommandBuilder.BuildSetFilterList(options);
                }

                foreach (IPSecurityFilterCommandOptions filter in filters)
                {
                    _ = IPSecurityCommandBuilder.BuildAddFilter(filter);
                }
            },
            ValidationInfoBar,
            LocalizedStrings.IPSec_Editor_ValidationInvalid);

        result = isValid
            ? new IPSecurityFilterListEditorResult
            {
                Options = options,
                Filters = filters
            }
            : null;
        return isValid;
    }

    private async void AddFilterButton_Click(object sender, RoutedEventArgs e)
    {
        string filterListName = string.IsNullOrWhiteSpace(NameTextBox.Text)
            ? PendingFilterListName
            : NameTextBox.Text;
        var editor = new IPSecurityFilterEditorControl(filterListName);

        if (await ShowFilterEditorAsync(editor, LocalizedStrings.IPSec_Editor_AddFilterTitle)
            is IPSecurityFilterCommandOptions filter)
        {
            FilterItems.Add(CreateFilterItem(filter));
        }
    }

    private async void EditFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (FiltersListView.SelectedItem is not IPSecurityFilterEditorItem selected)
        {
            return;
        }

        var editor = new IPSecurityFilterEditorControl(selected.Options);
        if (await ShowFilterEditorAsync(editor, LocalizedStrings.IPSec_Editor_EditFilterTitle)
            is not IPSecurityFilterCommandOptions filter)
        {
            return;
        }

        int index = FilterItems.IndexOf(selected);
        FilterItems[index] = CreateFilterItem(filter);
        FiltersListView.SelectedIndex = index;
    }

    private void DeleteFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (FiltersListView.SelectedItem is IPSecurityFilterEditorItem selected)
        {
            FilterItems.Remove(selected);
        }
    }

    private async Task<IPSecurityFilterCommandOptions?> ShowFilterEditorAsync(
        IPSecurityFilterEditorControl editor,
        string title)
    {
        // A ContentDialog on this editor's own XAML root: the filter editor is a leaf, and this
        // editor is itself hosted in a window precisely so this dialog can be an inline one.
        IPSecurityFilterCommandOptions? result = null;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = editor,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = (double)FilterDialogWidth;
        dialog.Resources["ContentDialogMaxHeight"] = (double)FilterDialogHeight;
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !editor.TryBuildResult(out result);

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? result : null;
    }

    private IPSecurityFilterEditorItem CreateFilterItem(IPSecurityFilterCommandOptions filter)
    {
        string summary = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.IPSec_Editor_FilterSummaryFormat,
            filter.SourceAddress,
            filter.DestinationAddress,
            filter.Protocol ?? "ANY",
            filter.SourcePort ?? 0,
            filter.DestinationPort ?? 0);
        return new IPSecurityFilterEditorItem
        {
            Options = filter,
            Summary = summary
        };
    }
}
