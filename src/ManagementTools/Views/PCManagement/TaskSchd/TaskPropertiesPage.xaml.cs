using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// Properties of a single scheduled task. Navigated to from <see cref="TaskSchedulerPage"/>
/// when a task is double-tapped (or via the Properties command).
/// </summary>
/// <remarks>
/// MOCK / SAMPLE data only. TODO(TaskScheduler): load the selected task's definition from
/// the Task Scheduler 2.0 COM API (Schedule.Service / ITaskService) or
/// Microsoft.Win32.TaskScheduler, expose it through a ViewModel, and move all user-facing
/// strings to ResourceKeys / .resw (en-US, zh-TW).
/// </remarks>
public sealed partial class TaskPropertiesPage : Page
{
	/// <summary>
	/// Guards the conditional-enablement handlers so they ignore the spurious
	/// Toggled/Checked/Unchecked events that fire while the XAML is being parsed
	/// (before every named control exists). Set once the initial state is applied.
	/// </summary>
	private bool _conditionalStatesInitialized;

	/// <summary>
	/// The ordered list of actions shown in the Actions expander. Order is priority:
	/// the topmost action runs first. Bound to the expander's <c>ItemsSource</c>.
	/// </summary>
	/// <remarks>MOCK data. TODO(TaskScheduler): populate from the task's IActionCollection.</remarks>
	public ObservableCollection<TaskActionItem> Actions { get; }

	public TaskPropertiesPage()
	{
		InitializeComponent();
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;

		// Build the (mock) action rows, then seed their move-button enabled states.
		Actions = new ObservableCollection<TaskActionItem>
		{
			new(Actions_MoveUp, Actions_MoveDown, Actions_Edit, "Start a program", @"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe /c"),
			new(Actions_MoveUp, Actions_MoveDown, Actions_Edit, "Display a message (deprecated)", "test title message"),
			new(Actions_MoveUp, Actions_MoveDown, Actions_Edit, "Send an e-mail (deprecated)", "noreply@google.com test subject"),
		};
		UpdateActionMoveStates();

		// Apply the dependent enable/disable rules once for the initial (mock) values so the
		// declared states are consistent, then allow the event handlers to keep them in sync.
		UpdateSecurityRunModeState();
		UpdateIdleState();
		UpdatePowerState();
		UpdateNetworkState();
		UpdateRestartOnFailureState();
		UpdateStopIfRunsLongerState();
		UpdateDeleteAfterState();
		_conditionalStatesInitialized = true;
	}

	private void OnThemeChanged(ElementTheme theme) => this.RequestedTheme = theme;

	// ====================  CONDITIONAL ENABLEMENT  ====================
	// These rules mirror the dependencies enforced by the Windows Task Scheduler property sheet:
	// child controls are only interactive while their governing toggle / checkbox / radio is set.

	/// <summary>"Do not store password" applies only when the task runs whether or not the user is logged on.</summary>
	private void UpdateSecurityRunModeState() =>
		DoNotStorePasswordCheckBox.IsEnabled = RunWhetherLoggedOnRadio.IsChecked == true;

	/// <summary>
	/// While the Idle condition is off, every idle sub-option is disabled. While it is on, the
	/// "Restart if the idle state resumes" option additionally requires "Stop if the computer ceases to be idle".
	/// </summary>
	private void UpdateIdleState()
	{
		bool idleOn = IdleToggle.IsOn;
		IdleStartComboBox.IsEnabled = idleOn;
		IdleWaitComboBox.IsEnabled = idleOn;
		IdleStopCeasesCheckBox.IsEnabled = idleOn;
		IdleRestartResumesCheckBox.IsEnabled = idleOn && IdleStopCeasesCheckBox.IsChecked == true;
	}

	/// <summary>"Stop if the computer switches to battery power" applies only while the AC-power condition is on.</summary>
	private void UpdatePowerState() =>
		StopOnBatteryCheckBox.IsEnabled = PowerToggle.IsOn;

	/// <summary>The network-connection picker applies only while the Network condition is on.</summary>
	private void UpdateNetworkState() =>
		NetworkComboBox.IsEnabled = NetworkToggle.IsOn;

	/// <summary>The restart interval and retry count apply only when restart-on-failure is enabled.</summary>
	private void UpdateRestartOnFailureState()
	{
		bool enabled = RestartOnFailureCheckBox.IsChecked == true;
		RestartIntervalComboBox.IsEnabled = enabled;
		RestartCountNumberBox.IsEnabled = enabled;
	}

	/// <summary>The execution-time-limit picker applies only when "Stop the task if it runs longer than" is checked.</summary>
	private void UpdateStopIfRunsLongerState() =>
		StopIfRunsLongerComboBox.IsEnabled = StopIfRunsLongerCheckBox.IsChecked == true;

	/// <summary>The deletion-delay picker applies only when "delete it after" is checked.</summary>
	private void UpdateDeleteAfterState() =>
		DeleteAfterComboBox.IsEnabled = DeleteAfterCheckBox.IsChecked == true;

	private void OnSecurityRunModeChanged(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdateSecurityRunModeState();
		}
	}

	private void OnIdleToggled(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdateIdleState();
		}
	}

	private void OnIdleStopCeasesChanged(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdateIdleState();
		}
	}

	private void OnPowerToggled(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdatePowerState();
		}
	}

	private void OnNetworkToggled(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdateNetworkState();
		}
	}

	private void OnRestartOnFailureChanged(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdateRestartOnFailureState();
		}
	}

	private void OnStopIfRunsLongerChanged(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdateStopIfRunsLongerState();
		}
	}

	private void OnDeleteAfterChanged(object sender, RoutedEventArgs e)
	{
		if (_conditionalStatesInitialized)
		{
			UpdateDeleteAfterState();
		}
	}

	// ====================  ACTIONS REORDERING  ====================
	// Order is priority: index 0 runs first. Move up/down shift an action one slot;
	// the buttons at the list ends are disabled via CanMoveUp / CanMoveDown.

	/// <summary>Moves the given action one position earlier (higher priority).</summary>
	private void Actions_MoveUp(TaskActionItem item)
	{
		int index = Actions.IndexOf(item);
		if (index > 0)
		{
			Actions.Move(index, index - 1);
			UpdateActionMoveStates();
		}
	}

	/// <summary>Moves the given action one position later (lower priority).</summary>
	private void Actions_MoveDown(TaskActionItem item)
	{
		int index = Actions.IndexOf(item);
		if (index >= 0 && index < Actions.Count - 1)
		{
			Actions.Move(index, index + 1);
			UpdateActionMoveStates();
		}
	}

	/// <summary>Refreshes each action's move-button enabled state after the order changes.</summary>
	private void UpdateActionMoveStates()
	{
		for (int i = 0; i < Actions.Count; i++)
		{
			Actions[i].CanMoveUp = i > 0;
			Actions[i].CanMoveDown = i < Actions.Count - 1;
		}
	}

	/// <summary>
	/// Receives the task that was opened from the list and reflects its identity in the UI.
	/// </summary>
	protected override void OnNavigatedTo(NavigationEventArgs e)
	{
		base.OnNavigatedTo(e);

		// e.Parameter is null when returning here via a breadcrumb (parameters are not retained
		// in history); keep the default sample content in that case.
		if (e.Parameter is ScheduledTaskSample task)
		{
			// TODO(TaskScheduler): populate every field from the real task definition. For now
			// only the General Name line is updated so the navigation reflects the selection;
			// the Description keeps its sample text.
			GeneralNameText.Text = task.Name;
		}
	}

	/// <summary>
	/// Triggers &gt; "New": opens the <see cref="NewTriggerDialog"/> in create mode
	/// (Title "Create a New Trigger").
	/// </summary>
	private async void NewTriggerButton_Click(object sender, RoutedEventArgs e) =>
		await ShowTriggerDialogAsync(null);

	/// <summary>
	/// A trigger row's Edit button: opens the <see cref="NewTriggerDialog"/> in edit mode
	/// (Title "Edit Trigger") for the row's trigger type, carried in the button's <c>Tag</c>.
	/// </summary>
	/// <remarks>
	/// The sample rows are static XAML, so the type travels in <c>Tag</c>; a real, data-bound
	/// Triggers list would pass the row's model instead (as the Actions list does via x:Bind).
	/// </remarks>
	private async void TriggerEdit_Click(object sender, RoutedEventArgs e)
	{
		var kind = Enum.Parse<TriggerEditKind>((string)((FrameworkElement)sender).Tag);
		await ShowTriggerDialogAsync(kind);
	}

	/// <summary>
	/// Shows the trigger editor. A <c>null</c> <paramref name="editKind"/> means create a new trigger;
	/// otherwise the dialog opens in edit mode pre-selected to that trigger type.
	/// </summary>
	/// <remarks>
	/// TODO(TaskScheduler): on a Primary result, build the matching ITrigger from the dialog and add it
	/// to (create) or update it in (edit) the task's ITriggerCollection. The dialog collects sample data only.
	/// </remarks>
	private async Task ShowTriggerDialogAsync(TriggerEditKind? editKind)
	{
		var dialog = new NewTriggerDialog(editKind)
		{
			XamlRoot = this.XamlRoot,
			RequestedTheme = App.CurrentTheme,
		};

		ContentDialogResult result = await dialog.ShowAsync().AsTask();
		if (result == ContentDialogResult.Primary)
		{
			// TODO(TaskScheduler): persist the created/edited trigger. No-op prototype.
		}
	}

	/// <summary>
	/// Actions &gt; "New": opens the <see cref="NewActionDialog"/> in create mode
	/// (Title "Create a New Action").
	/// </summary>
	private async void NewActionButton_Click(object sender, RoutedEventArgs e) =>
		await ShowActionDialogAsync(null);

	/// <summary>
	/// An action row's Edit button: opens the <see cref="NewActionDialog"/> in edit mode
	/// (Title "Edit Action") for <paramref name="item"/>. Delegated here from
	/// <see cref="TaskActionItem.Edit"/> the same way Move up/down are routed back to the page.
	/// </summary>
	private async void Actions_Edit(TaskActionItem item) =>
		await ShowActionDialogAsync(item);

	/// <summary>
	/// Shows the action editor. A <c>null</c> <paramref name="actionToEdit"/> means create a new
	/// action; otherwise the dialog opens in edit mode pre-selected to that action's type.
	/// </summary>
	/// <remarks>
	/// TODO(TaskScheduler): on a Primary result, build the matching IAction from the dialog and add it
	/// to (create) or update it in (edit) the task's IActionCollection. The dialog collects sample data only.
	/// </remarks>
	private async Task ShowActionDialogAsync(TaskActionItem? actionToEdit)
	{
		var dialog = new NewActionDialog(actionToEdit)
		{
			XamlRoot = this.XamlRoot,
			RequestedTheme = App.CurrentTheme,
		};

		ContentDialogResult result = await dialog.ShowAsync().AsTask();
		if (result == ContentDialogResult.Primary)
		{
			// TODO(TaskScheduler): persist the created/edited action. No-op prototype.
		}
	}
}

/// <summary>
/// A single row in the Actions list. Its position in the owning collection is its
/// priority (top = runs first). Move requests are delegated back to the page.
/// </summary>
/// <remarks>MOCK model. TODO(TaskScheduler): replace with a model derived from IAction.</remarks>
public sealed partial class TaskActionItem : ObservableObject
{
	private readonly Action<TaskActionItem> _moveUp;
	private readonly Action<TaskActionItem> _moveDown;
	private readonly Action<TaskActionItem> _edit;

	public TaskActionItem(Action<TaskActionItem> moveUp, Action<TaskActionItem> moveDown, Action<TaskActionItem> edit, string header, string description)
	{
		_moveUp = moveUp;
		_moveDown = moveDown;
		_edit = edit;
		Header = header;
		Description = description;
	}

	/// <summary>Action title shown as the card header (e.g. "Start a program").</summary>
	public string Header { get; }

	/// <summary>Action detail shown as the card description (command line, message, …).</summary>
	public string Description { get; }

	/// <summary>True while this action is not the first one, so "Move up" is enabled.</summary>
	[ObservableProperty]
	private bool _canMoveUp;

	/// <summary>True while this action is not the last one, so "Move down" is enabled.</summary>
	[ObservableProperty]
	private bool _canMoveDown;

	/// <summary>Invoked by the row's "Move up" button (x:Bind event binding).</summary>
	public void MoveUp() => _moveUp(this);

	/// <summary>Invoked by the row's "Move down" button (x:Bind event binding).</summary>
	public void MoveDown() => _moveDown(this);

	/// <summary>Invoked by the row's "Edit" button (x:Bind event binding); opens the action editor for this row.</summary>
	public void Edit() => _edit(this);
}
