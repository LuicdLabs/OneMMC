using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// Body of the "New Event Filter" editor (reached from <see cref="NewTriggerDialog"/>'s
/// "On an event" &gt; Custom &gt; "New Event Filter…"), hosted in a
/// <see cref="ManagementTools.Helpers.ModalDialogWindow"/>.
/// </summary>
/// <remarks>
/// MOCK / SAMPLE data only. The code just switches the Filter / XML tabs, keeps the By log /
/// By source pickers mutually exclusive, and toggles the manual-XPath edit mode. Nothing is parsed.
/// TODO(TaskScheduler): build/return the IEventTrigger.Subscription XPath from these fields.
/// </remarks>
public sealed partial class NewEventFilterContent : UserControl
{
	/// <summary>Guards the enable/disable handlers against events that fire during XAML parsing.</summary>
	private bool _initialized;

	public NewEventFilterContent()
	{
		this.InitializeComponent();

		// Apply the defaults after InitializeComponent so the target controls exist when the handlers run.
		FilterXmlSelector.SelectedItem = FilterTabItem; // Filter tab
		EventScopeRadios.SelectedIndex = 0;             // By log

		UpdateEventScopeState();
		UpdateEditQueryState();
		_initialized = true;
	}

	/// <summary>Shows the Filter or XML tab content for the selected <see cref="SelectorBarItem"/>.</summary>
	private void FilterXmlSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
	{
		if (FilterTabPanel is null)
		{
			return;
		}

		bool filter = FilterXmlSelector.SelectedItem == FilterTabItem;
		FilterTabPanel.Visibility = filter ? Visibility.Visible : Visibility.Collapsed;
		XmlTabPanel.Visibility = filter ? Visibility.Collapsed : Visibility.Visible;
	}

	/// <summary>"By log" enables the Event logs picker; "By source" enables the Event sources picker.</summary>
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

	/// <summary>
	/// While "Edit query manually" is unchecked the XML box mirrors the query built on the Filter tab
	/// and is read-only. Checking it makes the box editable and locks the Filter tab, because a manual
	/// XPath overrides the structured filter (mirrors taskschd / Event Viewer behavior).
	/// </summary>
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

	// TODO(TaskScheduler): reset every Filter-tab field to its <All …> sentinel default. No-op in this UI prototype.
	private void ClearFilter_Click(object sender, RoutedEventArgs e)
	{
	}
}
