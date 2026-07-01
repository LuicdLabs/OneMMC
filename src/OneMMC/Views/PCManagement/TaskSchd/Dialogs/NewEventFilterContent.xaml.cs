using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Features.PCManagement.Services.EventViewer;
using OneMMC.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.PCManagement;

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
/// &gt; Custom &gt; "New Event Filter…"), hosted in a <see cref="OneMMC.Helpers.ModalDialogWindow"/>.
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

    // Guards the "<All …>" sentinel coupling so the programmatic check/uncheck cascade does not re-enter
    // the per-item PropertyChanged handlers.
    private bool _suppressAllSync;

    // Custom-range state for the Logged picker: the committed range, the dynamically inserted combo item
    // that displays it, the last committed (non-action) selection to revert to, and a re-entrancy guard.
    private CustomRangeSelection? _customRange;
    private ComboBoxItem? _customResultItem;
    private object? _lastLoggedSelection;
    private bool _suppressLoggedChange;

    public NewEventFilterContent() : this(null)
    {
    }

    /// <summary>
    /// Creates the editor, optionally pre-loaded with an existing subscription. When
    /// <paramref name="initialQuery"/> is supplied (edit mode), it is shown verbatim on the XML tab with
    /// "Edit query manually" checked — lossless and editable — rather than reverse-parsed into the Filter
    /// fields. When <see langword="null"/> (new filter), the dialog opens on the Filter tab.
    /// </summary>
    public NewEventFilterContent(string? initialQuery)
    {
        InitializeComponent();

        FilterXmlSelector.SelectedItem = FilterTabItem;
        ByLogRadio.IsChecked = true;

        PopulateLogs();
        PopulateSources();
        PopulateKeywords();

        if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            // Edit mode: surface the saved query for manual editing on the XML tab.
            EditQueryManuallyCheckBox.IsChecked = true;
            XmlQueryBox.Text = initialQuery;
            FilterXmlSelector.SelectedItem = XmlTabItem;
            FilterTabPanel.Visibility = Visibility.Collapsed;
            XmlTabPanel.Visibility = Visibility.Visible;
        }

        UpdateEditQueryState();
        UpdateSourcesSummary();
        UpdateKeywordsSummary();
        UpdateScopeAndDownstream();

        _lastLoggedSelection = LoggedCombo.SelectedItem;
        _initialized = true;
    }

    /// <summary>
    /// Raised whenever <see cref="CanSubmit"/> may have changed (scope/source/log selection, the manual
    /// toggle, or the manual query text). The host uses it to enable/disable its OK button.
    /// </summary>
    public event EventHandler? CanSubmitChanged;

    /// <summary>
    /// Whether the filter currently describes a usable query: a manual XPath when editing manually,
    /// otherwise at least one selected log ("By log") or source ("By source"). Drives the host OK button so
    /// an empty filter cannot be submitted (and so no default Application query is produced).
    /// </summary>
    public bool CanSubmit =>
        EditQueryManuallyCheckBox.IsChecked == true
            ? !string.IsNullOrWhiteSpace(XmlQueryBox.Text)
            : HasScope;

    // True once the active scope has a concrete selection: a checked log for "By log", a checked source for
    // "By source".
    private bool HasScope =>
        BySourceRadio.IsChecked == true ? AnySourceChecked() : SelectedLogChannels().Count > 0;

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

        if (ByLogRadio.IsChecked == true)
        {
            foreach (var log in SelectedLogChannels())
            {
                criteria.Selections.Add(new ChannelSelection { Channel = log });
            }
        }
        else
        {
            foreach (var selection in GroupSourcesByChannel(SelectedSources()))
            {
                criteria.Selections.Add(selection);
            }
        }

        // Without at least one channel (By log) or source (By source) there is no scope to query;
        // returning an empty subscription avoids silently defaulting to the Application log and lets
        // the host keep the OK button disabled. See HasScope / the host's primary-button guard.
        if (criteria.Selections.Count == 0)
        {
            XmlQueryBox.Text = string.Empty;
            return string.Empty;
        }

        var query = EventXPathBuilder.BuildQueryList(criteria);
        XmlQueryBox.Text = query;
        return query;
    }

    // "By source" mirrors Event Viewer: resolve each provider to the channel(s) it writes to and emit one
    // Select per channel, so the query Path is correct instead of always assuming the Application log.
    private static List<ChannelSelection> GroupSourcesByChannel(IReadOnlyList<string> sources)
    {
        var providersByChannel = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var channelOrder = new List<string>();

        foreach (var source in sources)
        {
            foreach (var channel in EventSourceChannelResolver.GetChannels(source))
            {
                if (!providersByChannel.TryGetValue(channel, out var providers))
                {
                    providers = [];
                    providersByChannel[channel] = providers;
                    channelOrder.Add(channel);
                }
                if (!providers.Contains(source))
                {
                    providers.Add(source);
                }
            }
        }

        return channelOrder
            .Select(channel => new ChannelSelection { Channel = channel, Providers = providersByChannel[channel] })
            .ToList();
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
        // Probe channel subscribability off the UI thread (≈1000 channels, one channel-config read each),
        // then build the tree on the dispatcher. Non-subscribable Analytic/Debug channels are excluded so
        // checking the "Applications and Services Logs" group cannot cascade-select a channel that makes the
        // Task Scheduler service reject the saved trigger with "The request is not supported".
        _ = Task.Run(() =>
        {
            var all = EventLogSources.GetChannels();
            var subscribable = new HashSet<string>(
                all.Where(EventLogSources.IsSubscribableChannel), StringComparer.OrdinalIgnoreCase);
            DispatcherQueue.TryEnqueue(() => BuildLogTree(all, subscribable));
        });
    }

    // Builds the Windows Logs / Applications and Services Logs tree from the enumerated channels. Classic
    // Windows logs are always included; every other channel is included only when subscribable.
    private void BuildLogTree(List<string> names, HashSet<string> subscribable)
    {
        if (names.Count == 0)
        {
            // Enumeration failed or returned nothing (e.g. no permissions): only the manual XML query works.
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

            // Skip Analytic/Debug (and unreadable) channels: they cannot be part of a subscription query.
            if (!subscribable.Contains(channel))
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
        // In "By source" mode the Log field reflects the channel(s) the selected sources write to,
        // matching taskschd.msc (e.g. source "Kernel-Acpi" → "Microsoft-Windows-Kernel-Acpi/Diagnostic").
        if (BySourceRadio.IsChecked == true)
        {
            var channels = GroupSourcesByChannel(SelectedSources()).Select(s => s.Channel).ToList();
            EventLogsSummary.Text = channels.Count == 0 ? string.Empty : string.Join(", ", channels);
            return;
        }

        var logs = SelectedLogChannels();
        EventLogsSummary.Text = logs.Count == 0 ? SelectLogsLabel : string.Join(", ", logs);
    }

    private void EventLogsFlyout_Closed(object sender, object e) => OnLogsChanged();

    // ----------------------------------------------------------------- Event sources

    private void PopulateSources()
    {
        _sources.Add(new CheckableItem(AllSourcesLabel));
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
        foreach (var item in _sources)
        {
            item.PropertyChanged += OnSourceItemChanged;
        }
        EventSourcesList.ItemsSource = _sources;
    }

    // "All Event Sources" means "no specific source filter", so it contributes no provider names.
    private List<string> SelectedSources()
    {
        if (IsAllChecked(_sources, AllSourcesLabel))
        {
            return [];
        }
        return _sources.Where(i => i.IsChecked && i.Name != AllSourcesLabel).Select(i => i.Name).ToList();
    }

    private bool AnySourceChecked() => _sources.Any(i => i.IsChecked);

    private static bool IsAllChecked(IEnumerable<CheckableItem> items, string allLabel) =>
        items.Any(i => i.Name == allLabel && i.IsChecked);

    private void UpdateSourcesSummary()
    {
        if (IsAllChecked(_sources, AllSourcesLabel))
        {
            EventSourcesSummary.Text = AllSourcesLabel;
            return;
        }
        var picked = _sources.Where(i => i.IsChecked && i.Name != AllSourcesLabel).Select(i => i.Name).ToList();
        EventSourcesSummary.Text = picked.Count == 0 ? string.Empty : string.Join(", ", picked);
    }

    private void EventSourcesFlyout_Closed(object sender, object e) => OnSourcesChanged();

    // ----------------------------------------------------------------- Keywords

    private void PopulateKeywords()
    {
        _keywords.Add(new CheckableItem(AllKeywordsLabel));
        foreach (var (name, _) in KeywordDefinitions)
        {
            _keywords.Add(new CheckableItem(name));
        }
        foreach (var item in _keywords)
        {
            item.PropertyChanged += OnKeywordItemChanged;
        }
        KeywordsList.ItemsSource = _keywords;
    }

    private long SelectedKeywordsMask()
    {
        // "All Keywords" means no keyword restriction.
        if (IsAllChecked(_keywords, AllKeywordsLabel))
        {
            return 0;
        }
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
        if (IsAllChecked(_keywords, AllKeywordsLabel))
        {
            KeywordsSummary.Text = AllKeywordsLabel;
            return;
        }
        var picked = _keywords.Where(i => i.IsChecked && i.Name != AllKeywordsLabel).Select(i => i.Name).ToList();
        KeywordsSummary.Text = picked.Count == 0 ? string.Empty : string.Join(", ", picked);
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

    // Keeps an "<All …>" sentinel in sync with its siblings: toggling it sets them all, and the sentinel
    // stays checked only while every sibling is checked. The cascade is guarded so the re-entrant
    // PropertyChanged notifications it raises do not fight each other.
    private void SyncAllSentinel(ObservableCollection<CheckableItem> items, string allLabel, CheckableItem changed)
    {
        var all = items.FirstOrDefault(i => i.Name == allLabel);
        if (all is null)
        {
            return;
        }

        _suppressAllSync = true;
        try
        {
            if (ReferenceEquals(changed, all))
            {
                foreach (var item in items)
                {
                    if (!ReferenceEquals(item, all))
                    {
                        item.IsChecked = all.IsChecked;
                    }
                }
            }
            else
            {
                all.IsChecked = items.Where(i => !ReferenceEquals(i, all)).All(i => i.IsChecked);
            }
        }
        finally
        {
            _suppressAllSync = false;
        }
    }

    private void OnSourceItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_initialized || _suppressAllSync || e.PropertyName != nameof(CheckableItem.IsChecked) || sender is not CheckableItem changed)
        {
            return;
        }
        SyncAllSentinel(_sources, AllSourcesLabel, changed);
        OnSourcesChanged();
    }

    private void OnKeywordItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_initialized || _suppressAllSync || e.PropertyName != nameof(CheckableItem.IsChecked) || sender is not CheckableItem changed)
        {
            return;
        }
        SyncAllSentinel(_keywords, AllKeywordsLabel, changed);
        UpdateKeywordsSummary();
    }

    // Refreshes the sources summary and downstream state after a source check change. The radio is the
    // master control for scope; choosing a source never flips it (the field is only editable while
    // "By source" is selected anyway).
    private void OnSourcesChanged()
    {
        UpdateSourcesSummary();
        UpdateScopeAndDownstream();
    }

    // Refreshes downstream state after a log selection change. As with sources, the radio stays the master
    // control; choosing a log never flips it.
    private void OnLogsChanged()
    {
        UpdateScopeAndDownstream();
    }

    private void EventScopeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            UpdateScopeAndDownstream();
        }
    }

    // Central enabling/summary update. The scope radio is the master control: "By log" enables the Event
    // logs picker and disables Event sources, "By source" does the reverse — the disabled picker keeps its
    // value but is not editable, mirroring taskschd.msc. Task category is available only when filtering by
    // source (its items come from the selected source); the other downstream filters become available once
    // any log or source is chosen.
    private void UpdateScopeAndDownstream()
    {
        bool bySource = BySourceRadio.IsChecked == true;
        bool hasScope = HasScope;
        bool taskCategoryEnabled = bySource && AnySourceChecked();

        // The radio drives which picker is editable; the other is disabled but retains its value.
        EventLogsDropDown.IsEnabled = !bySource;
        EventSourcesDropDown.IsEnabled = bySource;

        TaskCategoryCombo.IsEnabled = taskCategoryEnabled;
        if (taskCategoryEnabled)
        {
            TaskCategoryCombo.ItemsSource = EventLogSources.GetTaskCategories(SelectedSources());
        }
        else
        {
            TaskCategoryCombo.ItemsSource = null;
            TaskCategoryCombo.SelectedIndex = -1;
            TaskCategoryCombo.Text = string.Empty;
        }

        KeywordsDropDown.IsEnabled = hasScope;
        EventIdsBox.IsEnabled = hasScope;
        FilterUserBox.IsEnabled = hasScope;
        FilterComputerBox.IsEnabled = hasScope;

        UpdateEventLogsSummary();
        CanSubmitChanged?.Invoke(this, EventArgs.Empty);
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
            CanSubmitChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void XmlQueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Only manual edits affect submittability; the programmatic preview refresh is gated by the manual
        // toggle inside CanSubmit, so always notifying here is safe.
        if (_initialized)
        {
            CanSubmitChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        CriticalCheck.IsChecked = WarningCheck.IsChecked = VerboseCheck.IsChecked = ErrorCheck.IsChecked = InfoCheck.IsChecked = false;

        _customRange = null;
        RemoveCustomResultItem();
        SelectLogged(0);

        ByLogRadio.IsChecked = true;
        EventLogsTree.SelectedNodes.Clear();

        // Reset both pickers to the empty state (nothing checked, blank summary) without running the
        // per-item coupling for every reset.
        _suppressAllSync = true;
        foreach (var source in _sources)
        {
            source.IsChecked = false;
        }
        foreach (var keyword in _keywords)
        {
            keyword.IsChecked = false;
        }
        _suppressAllSync = false;

        EventIdsBox.Text = string.Empty;
        TaskCategoryCombo.Text = string.Empty;
        FilterUserBox.Text = string.Empty;
        FilterComputerBox.Text = string.Empty;

        UpdateSourcesSummary();
        UpdateKeywordsSummary();
        UpdateScopeAndDownstream();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
