using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

/// <summary>A name with a check state, used for the source/keyword multi-select flyout lists.</summary>
public partial class CheckableItem : ObservableObject
{
    /// <summary>The display name (a provider/source name, a keyword, or the "&lt;All …&gt;" sentinel).</summary>
    public string Name { get; }

    /// <summary>Whether the item is currently checked.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public CheckableItem(string name, bool isChecked = false)
    {
        Name = name;
        IsChecked = isChecked;
    }
}

/// <summary>
/// Body of the "New Event Filter" editor (reached from <see cref="NewTriggerDialog"/>'s "On an event"
/// &gt; Custom &gt; "New Event Filter…"), hosted in a <see cref="ManagementTools.Helpers.ModalDialogWindow"/>.
/// Builds the IEventTrigger.Subscription query from the Filter tab fields, or returns a hand-written
/// query from the XML tab.
/// </summary>
public sealed partial class NewEventFilterContent : UserControl
{
    private const string AllSourcesLabel = "<All Event Sources>";
    private const string AllKeywordsLabel = "<All Keywords>";
    private const string SelectLogsLabel = "<Select event logs>";

    // The classic Windows Logs shown under the "Windows Logs" group, in taskschd.msc order.
    private static readonly string[] ClassicWindowsLogs =
        ["Application", "Security", "Setup", "System", "ForwardedEvents"];

    // The reserved Microsoft keyword bits (from winmeta.xml) the Keywords picker exposes.
    private static readonly (string Name, long Mask)[] KeywordDefinitions =
    [
        ("Audit Failure", 0x10000000000000),
        ("Audit Success", 0x20000000000000),
        ("Classic", 0x80000000000000),
        ("Correlation Hint", 0x40000000000000),
        ("Response Time", 0x01000000000000),
        ("SQM", 0x08000000000000),
        ("WDI Diag", 0x04000000000000),
    ];

    private readonly Dictionary<TreeViewNode, string> _logChannelByNode = [];
    private readonly ObservableCollection<CheckableItem> _sources = [];
    private readonly ObservableCollection<CheckableItem> _keywords = [];

    private bool _initialized;

    // Custom-range state for the Logged picker: the committed range, the dynamically inserted combo item
    // that displays it, the last committed (non-action) selection to revert to, and a re-entrancy guard.
    private CustomRangeSelection? _customRange;
    private ComboBoxItem? _customResultItem;
    private object? _lastLoggedSelection;
    private bool _suppressLoggedChange;

    public NewEventFilterContent()
    {
        InitializeComponent();

        FilterXmlSelector.SelectedItem = FilterTabItem;
        EventScopeRadios.SelectedIndex = 0;

        PopulateLogs();
        PopulateSources();
        PopulateKeywords();

        UpdateEventScopeState();
        UpdateEditQueryState();
        UpdateEventLogsSummary();

        _lastLoggedSelection = LoggedCombo.SelectedItem;
        _initialized = true;
    }

    /// <summary>
    /// Returns the event-query subscription: the manual XPath when "Edit query manually" is checked,
    /// otherwise a <c>QueryList</c> built from the Filter-tab fields.
    /// </summary>
    public string BuildQuery()
    {
        if (EditQueryManuallyCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(XmlQueryBox.Text))
        {
            return XmlQueryBox.Text.Trim();
        }

        var criteria = new EventXPathBuilder.Criteria
        {
            EventIds = NullIfBlank(EventIdsBox.Text),
            UserSid = NullIfBlank(FilterUserBox.Text),
            Computer = NullIfBlank(FilterComputerBox.Text),
            KeywordsMask = SelectedKeywordsMask(),
        };

        if (CriticalCheck.IsChecked == true) criteria.Levels.Add(1);
        if (ErrorCheck.IsChecked == true) criteria.Levels.Add(2);
        if (WarningCheck.IsChecked == true) criteria.Levels.Add(3);
        if (InfoCheck.IsChecked == true) criteria.Levels.Add(4);
        if (VerboseCheck.IsChecked == true) criteria.Levels.Add(5);

        ApplyLoggedRange(criteria);

        if (EventScopeRadios.SelectedIndex == 0)
        {
            foreach (var log in SelectedLogChannels())
            {
                criteria.Logs.Add(log);
            }
        }
        else
        {
            foreach (var source in SelectedSources())
            {
                criteria.Sources.Add(source);
            }
        }

        var query = EventXPathBuilder.BuildQueryList(criteria);
        XmlQueryBox.Text = query;
        return query;
    }

    // ----------------------------------------------------------------- Logged / custom range

    private async void LoggedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _suppressLoggedChange)
        {
            return;
        }

        if (ReferenceEquals(LoggedCombo.SelectedItem, CustomRangeActionItem))
        {
            await OpenCustomRangeAsync();
            return;
        }

        _lastLoggedSelection = LoggedCombo.SelectedItem;
    }

    private async Task OpenCustomRangeAsync()
    {
        var dialog = new CustomRangeDialog(_customRange)
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result is { } range)
        {
            if (range.IsUnbounded)
            {
                // No bounds is the same as "Any time": drop any custom entry and select it.
                _customRange = null;
                RemoveCustomResultItem();
                SelectLogged(0);
            }
            else
            {
                ApplyCustomRange(range);
            }
        }
        else
        {
            // Cancelled or dismissed: leave the Logged selection where it was before opening the dialog.
            RevertLoggedSelection();
        }
    }

    private void ApplyCustomRange(CustomRangeSelection range)
    {
        _customRange = range;

        _suppressLoggedChange = true;
        if (_customResultItem is null)
        {
            _customResultItem = new ComboBoxItem();
            var actionIndex = LoggedCombo.Items.IndexOf(CustomRangeActionItem);
            LoggedCombo.Items.Insert(actionIndex, _customResultItem);
        }
        _customResultItem.Content = DescribeCustomRange(range);
        LoggedCombo.SelectedItem = _customResultItem;
        _lastLoggedSelection = _customResultItem;
        _suppressLoggedChange = false;
    }

    private void RemoveCustomResultItem()
    {
        if (_customResultItem is not null)
        {
            LoggedCombo.Items.Remove(_customResultItem);
            _customResultItem = null;
        }
    }

    private void SelectLogged(int index)
    {
        _suppressLoggedChange = true;
        LoggedCombo.SelectedIndex = index;
        _lastLoggedSelection = LoggedCombo.SelectedItem;
        _suppressLoggedChange = false;
    }

    private void RevertLoggedSelection()
    {
        _suppressLoggedChange = true;
        LoggedCombo.SelectedItem = _lastLoggedSelection ?? LoggedCombo.Items.FirstOrDefault();
        _suppressLoggedChange = false;
    }

    private void ApplyLoggedRange(EventXPathBuilder.Criteria criteria)
    {
        if (ReferenceEquals(LoggedCombo.SelectedItem, _customResultItem) && _customRange is { } range)
        {
            criteria.FromUtc = range.From;
            criteria.ToUtc = range.To;
            return;
        }

        criteria.WithinLast = LoggedCombo.SelectedIndex switch
        {
            1 => TimeSpan.FromHours(1),
            2 => TimeSpan.FromHours(12),
            3 => TimeSpan.FromHours(24),
            4 => TimeSpan.FromDays(7),
            5 => TimeSpan.FromDays(30),
            _ => null,
        };
    }

    private static string DescribeCustomRange(CustomRangeSelection range)
    {
        if (range.From is null && range.To is { } upto)
        {
            return $"All Events upto {FormatRangeTime(upto)}";
        }
        if (range.From is { } from && range.To is null)
        {
            return $"From {FormatRangeTime(from)}";
        }
        return $"From {FormatRangeTime(range.From!.Value)} to {FormatRangeTime(range.To!.Value)}";
    }

    private static string FormatRangeTime(DateTime value) =>
        value.ToString("yyyy/M/d HH:mm:ss", CultureInfo.CurrentCulture);

    // ----------------------------------------------------------------- Event logs (checkbox tree)

    private void PopulateLogs()
    {
        List<string> names;
        try
        {
            names = EventLogSession.GlobalSession.GetLogNames()
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            // If channel enumeration fails (e.g. no permissions), only the manual XML query is available.
            return;
        }

        var windowsNode = new TreeViewNode { Content = "Windows Logs", IsExpanded = true };
        foreach (var classic in ClassicWindowsLogs)
        {
            var channel = names.FirstOrDefault(n => string.Equals(n, classic, StringComparison.OrdinalIgnoreCase));
            if (channel is not null)
            {
                AddLogLeaf(windowsNode, channel);
            }
        }

        var appsNode = new TreeViewNode { Content = "Applications and Services Logs" };
        var microsoftNode = new TreeViewNode { Content = "Microsoft" };

        foreach (var channel in names)
        {
            if (ClassicWindowsLogs.Contains(channel, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // Microsoft channels (the long Microsoft-Windows-* set) nest under a single "Microsoft" group
            // so the top level stays readable; everything else sits directly under the group.
            var parent = channel.StartsWith("Microsoft-", StringComparison.OrdinalIgnoreCase) ? microsoftNode : appsNode;
            AddLogLeaf(parent, channel);
        }

        if (microsoftNode.Children.Count > 0)
        {
            appsNode.Children.Insert(0, microsoftNode);
        }

        EventLogsTree.RootNodes.Add(windowsNode);
        EventLogsTree.RootNodes.Add(appsNode);
    }

    private void AddLogLeaf(TreeViewNode parent, string channel)
    {
        var leaf = new TreeViewNode { Content = channel };
        _logChannelByNode[leaf] = channel;
        parent.Children.Add(leaf);
    }

    private List<string> SelectedLogChannels()
    {
        var result = new List<string>();
        foreach (var node in EventLogsTree.SelectedNodes)
        {
            if (_logChannelByNode.TryGetValue(node, out var channel))
            {
                result.Add(channel);
            }
        }
        return result;
    }

    private void UpdateEventLogsSummary()
    {
        var logs = SelectedLogChannels();
        EventLogsSummary.Text = logs.Count == 0 ? SelectLogsLabel : string.Join(", ", logs);
    }

    private void EventLogsFlyout_Closed(object sender, object e) => UpdateEventLogsSummary();

    // ----------------------------------------------------------------- Event sources

    private void PopulateSources()
    {
        _sources.Add(new CheckableItem(AllSourcesLabel, isChecked: true));
        try
        {
            foreach (var provider in EventLogSession.GlobalSession.GetProviderNames()
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                _sources.Add(new CheckableItem(provider));
            }
        }
        catch (Exception)
        {
            // Provider enumeration can fail without elevation; the list keeps just "<All Event Sources>".
        }
        EventSourcesList.ItemsSource = _sources;
    }

    private List<string> SelectedSources() =>
        _sources.Where(i => i.IsChecked && i.Name != AllSourcesLabel).Select(i => i.Name).ToList();

    private void UpdateSourcesSummary()
    {
        var picked = SelectedSources();
        EventSourcesSummary.Text = picked.Count == 0 ? AllSourcesLabel : string.Join(", ", picked);
    }

    private void EventSourcesFlyout_Closed(object sender, object e) => UpdateSourcesSummary();

    // ----------------------------------------------------------------- Keywords

    private void PopulateKeywords()
    {
        _keywords.Add(new CheckableItem(AllKeywordsLabel, isChecked: true));
        foreach (var (name, _) in KeywordDefinitions)
        {
            _keywords.Add(new CheckableItem(name));
        }
        KeywordsList.ItemsSource = _keywords;
    }

    private long SelectedKeywordsMask()
    {
        long mask = 0;
        foreach (var item in _keywords)
        {
            if (item.IsChecked && item.Name != AllKeywordsLabel)
            {
                mask |= KeywordMaskFor(item.Name);
            }
        }
        return mask;
    }

    private static long KeywordMaskFor(string name)
    {
        foreach (var (n, m) in KeywordDefinitions)
        {
            if (n == name)
            {
                return m;
            }
        }
        return 0;
    }

    private void UpdateKeywordsSummary()
    {
        var picked = _keywords.Where(i => i.IsChecked && i.Name != AllKeywordsLabel).Select(i => i.Name).ToList();
        KeywordsSummary.Text = picked.Count == 0 ? AllKeywordsLabel : string.Join(", ", picked);
    }

    private void KeywordsFlyout_Closed(object sender, object e) => UpdateKeywordsSummary();

    // ----------------------------------------------------------------- Scope / tabs / clear

    private void FilterXmlSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (FilterTabPanel is null)
        {
            return;
        }
        bool filter = FilterXmlSelector.SelectedItem == FilterTabItem;
        FilterTabPanel.Visibility = filter ? Visibility.Visible : Visibility.Collapsed;
        XmlTabPanel.Visibility = filter ? Visibility.Collapsed : Visibility.Visible;

        // When switching to the XML tab (and not editing manually), refresh the preview from the form.
        if (!filter && EditQueryManuallyCheckBox.IsChecked != true)
        {
            XmlQueryBox.Text = BuildQuery();
        }
    }

    private void UpdateEventScopeState()
    {
        bool byLog = EventScopeRadios.SelectedIndex == 0;
        EventLogsDropDown.IsEnabled = byLog;
        EventSourcesDropDown.IsEnabled = !byLog;
    }

    private void EventScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventLogsDropDown is not null)
        {
            UpdateEventScopeState();
        }
    }

    private void UpdateEditQueryState()
    {
        bool manual = EditQueryManuallyCheckBox.IsChecked == true;
        XmlQueryBox.IsReadOnly = !manual;
        FilterTabItem.IsEnabled = !manual;
    }

    private void OnEditQueryManuallyChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            UpdateEditQueryState();
        }
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        CriticalCheck.IsChecked = WarningCheck.IsChecked = VerboseCheck.IsChecked = ErrorCheck.IsChecked = InfoCheck.IsChecked = false;

        _customRange = null;
        RemoveCustomResultItem();
        SelectLogged(0);

        EventScopeRadios.SelectedIndex = 0;
        EventLogsTree.SelectedNodes.Clear();

        foreach (var source in _sources)
        {
            source.IsChecked = source.Name == AllSourcesLabel;
        }
        foreach (var keyword in _keywords)
        {
            keyword.IsChecked = keyword.Name == AllKeywordsLabel;
        }

        EventIdsBox.Text = string.Empty;
        TaskCategoryCombo.Text = string.Empty;
        FilterUserBox.Text = string.Empty;
        FilterComputerBox.Text = string.Empty;

        UpdateEventLogsSummary();
        UpdateSourcesSummary();
        UpdateKeywordsSummary();
        UpdateEventScopeState();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
