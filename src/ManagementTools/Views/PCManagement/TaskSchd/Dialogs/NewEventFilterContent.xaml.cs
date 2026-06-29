using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// Body of the "New Event Filter" editor (reached from <see cref="NewTriggerDialog"/>'s "On an event"
/// &gt; Custom &gt; "New Event Filter…"), hosted in a <see cref="ManagementTools.Helpers.ModalDialogWindow"/>.
/// Builds the IEventTrigger.Subscription query from the Filter tab fields, or returns a hand-written
/// query from the XML tab.
/// </summary>
public sealed partial class NewEventFilterContent : UserControl
{
    private bool _initialized;

    public NewEventFilterContent()
    {
        InitializeComponent();

        FilterXmlSelector.SelectedItem = FilterTabItem;
        EventScopeRadios.SelectedIndex = 0;
        EventLogsCombo.IsEditable = true;
        EventSourcesCombo.IsEditable = true;

        UpdateEventScopeState();
        UpdateEditQueryState();
        PopulateLogs();
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
            EventIds = string.IsNullOrWhiteSpace(EventIdsBox.Text) ? null : EventIdsBox.Text.Trim(),
            UserSid = string.IsNullOrWhiteSpace(FilterUserBox.Text) ? null : FilterUserBox.Text.Trim(),
            Computer = string.IsNullOrWhiteSpace(FilterComputerBox.Text) ? null : FilterComputerBox.Text.Trim(),
            WithinLast = LoggedWindow(),
        };

        if (CriticalCheck.IsChecked == true) criteria.Levels.Add(1);
        if (ErrorCheck.IsChecked == true) criteria.Levels.Add(2);
        if (WarningCheck.IsChecked == true) criteria.Levels.Add(3);
        if (InfoCheck.IsChecked == true) criteria.Levels.Add(4);
        if (VerboseCheck.IsChecked == true) criteria.Levels.Add(5);

        if (EventScopeRadios.SelectedIndex == 0)
        {
            var log = (EventLogsCombo.SelectedItem as string) ?? EventLogsCombo.Text;
            if (!string.IsNullOrWhiteSpace(log))
            {
                criteria.Logs.Add(log.Trim());
            }
        }
        else
        {
            var source = (EventSourcesCombo.SelectedItem as string) ?? EventSourcesCombo.Text;
            if (!string.IsNullOrWhiteSpace(source))
            {
                criteria.Sources.Add(source.Trim());
            }
        }

        var query = EventXPathBuilder.BuildQueryList(criteria);
        XmlQueryBox.Text = query;
        return query;
    }

    private TimeSpan? LoggedWindow() => LoggedCombo.SelectedIndex switch
    {
        1 => TimeSpan.FromHours(1),
        2 => TimeSpan.FromHours(12),
        3 => TimeSpan.FromHours(24),
        4 => TimeSpan.FromDays(7),
        5 => TimeSpan.FromDays(30),
        _ => null,
    };

    private void PopulateLogs()
    {
        try
        {
            var names = EventLogSession.GlobalSession.GetLogNames()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            EventLogsCombo.ItemsSource = names;
            // Seed the most common logs as quick source suggestions.
            EventSourcesCombo.ItemsSource = new List<string>();
        }
        catch (Exception)
        {
            // If channel enumeration fails (e.g. no permissions), the combos stay editable for manual entry.
        }
    }

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
        EventLogsCombo.IsEnabled = byLog;
        EventSourcesCombo.IsEnabled = !byLog;
    }

    private void EventScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventLogsCombo is not null)
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
        LoggedCombo.SelectedIndex = 0;
        EventScopeRadios.SelectedIndex = 0;
        EventLogsCombo.SelectedItem = null;
        EventLogsCombo.Text = string.Empty;
        EventSourcesCombo.SelectedItem = null;
        EventSourcesCombo.Text = string.Empty;
        EventIdsBox.Text = string.Empty;
        TaskCategoryCombo.SelectedItem = null;
        KeywordsCombo.SelectedItem = null;
        FilterUserBox.Text = string.Empty;
        FilterComputerBox.Text = string.Empty;
    }
}
