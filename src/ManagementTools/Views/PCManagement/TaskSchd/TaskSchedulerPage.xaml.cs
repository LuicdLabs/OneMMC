using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ManagementTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// UI prototype for a modern Task Scheduler (taskschd.msc) replacement.
/// </summary>
/// <remarks>
/// Everything in this file is MOCK / SAMPLE data used to demonstrate the final UI.
/// TODO(TaskScheduler): replace the sample collections and the inline event handlers
/// with a real ViewModel + service backed by the Task Scheduler 2.0 COM API
/// (Schedule.Service / ITaskService) or Microsoft.Win32.TaskScheduler, registered via
/// DI under Core/Features per the project's MVVM conventions, and move all user-facing
/// strings to ResourceKeys / .resw (en-US, zh-TW).
/// </remarks>
public sealed partial class TaskSchedulerPage : Page, INotifyPropertyChanged
{
	/// <summary>Sample tasks shown in the list. TODO(TaskScheduler): enumerate the selected folder's registered tasks.</summary>
	public ObservableCollection<ScheduledTaskSample> SampleTasks { get; } =
	[
		new("MicrosoftEdgeUpdateTaskMachineCore", "Running | Next run time: 2026/6/28 04:18:03"),
		new("OneDrive Reporting Task", "Ready | Next run time: 2026/6/27 23:01:27"),
	];

	// Mock toggle states for the single Disable/Enable and Run/End buttons.
	private bool _selectedTaskEnabled = true;
	private bool _selectedTaskRunning;

	private bool _isTreeNodeSelected;
	private bool _isNonRootTreeNodeSelected;

	/// <summary>
	/// True when any node in <see cref="LibraryTreeView"/> is selected.
	/// Drives the IsEnabled binding for folder-scoped menu items (New folder / Import Task).
	/// Every node — including the root "Task Scheduler Library" — represents a valid folder
	/// target for both operations.
	/// </summary>
	public bool IsTreeNodeSelected
	{
		get => _isTreeNodeSelected;
		private set => SetField(ref _isTreeNodeSelected, value);
	}

	/// <summary>
	/// True when a non-root node is selected in <see cref="LibraryTreeView"/>.
	/// Drives the IsEnabled binding for Delete folder: the root "Task Scheduler Library"
	/// node is not a user-created folder and must not be deleted.
	/// </summary>
	public bool IsNonRootTreeNodeSelected
	{
		get => _isNonRootTreeNodeSelected;
		private set => SetField(ref _isNonRootTreeNodeSelected, value);
	}

	/// <summary>Raised when a bindable property changes, so the x:Bind OneWay IsEnabled bindings refresh.</summary>
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Assigns <paramref name="field"/> and raises <see cref="PropertyChanged"/> only when the value changes.</summary>
	private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	public TaskSchedulerPage()
	{
		InitializeComponent();
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;

		BuildSampleTree();
	}

	private void OnThemeChanged(ElementTheme theme) => this.RequestedTheme = theme;

	/// <summary>
	/// Opens the <see cref="CreateTaskDialog"/> prototype.
	/// TODO(TaskScheduler): on a Primary result, build an ITaskDefinition from the dialog's
	/// trigger + action selections and register it in the currently selected ITaskFolder. The
	/// dialog only collects sample data today, so no task is created.
	/// </summary>
	private async void CreateTaskButton_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new CreateTaskDialog
		{
			XamlRoot = this.XamlRoot,
			RequestedTheme = App.CurrentTheme,
		};

		ContentDialogResult result = await dialog.ShowAsync().AsTask();
		if (result == ContentDialogResult.Primary)
		{
			// TODO(TaskScheduler): register the task using dialog.TaskName / dialog.TaskDescription
			// and the selected trigger/action. No-op in this UI prototype.
		}
	}

	/// <summary>
	/// Builds the sample folder hierarchy.
	/// TODO(TaskScheduler): populate from the real ITaskFolder tree under "\" (Task Scheduler Library).
	/// </summary>
	private void BuildSampleTree()
	{
		var library = new TreeViewNode { Content = "Task Scheduler Library", IsExpanded = true };
		library.Children.Add(new TreeViewNode { Content = "Microsoft" });
		library.Children.Add(new TreeViewNode { Content = "SoftLanding" });
		LibraryTreeView.RootNodes.Add(library);

		// Select the root by default so the folder-scoped commands reflect the visible
		// selection on first load. Setting SelectedNode raises SelectionChanged, but call the
		// updater explicitly too to guarantee the initial state regardless of event timing.
		LibraryTreeView.SelectedNode = library;
		UpdateFolderSelectionState(library);
	}

	// Selecting a folder node updates the folder-scoped command state and should load that
	// folder's tasks into the list. SelectionChanged (not ItemInvoked) is used so SelectedNode
	// is already current here, and so keyboard/programmatic selection is covered too.
	// TODO(TaskScheduler): enumerate the selected ITaskFolder's registered tasks.
	private void LibraryTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
		=> UpdateFolderSelectionState(sender.SelectedNode);

	/// <summary>
	/// Recomputes the IsEnabled state of the folder-scoped menu items from the selected node
	/// and refreshes the x:Bind one-way bindings.
	/// </summary>
	private void UpdateFolderSelectionState(TreeViewNode? selected)
	{
		// Any node enables New folder / Import Task.
		IsTreeNodeSelected = selected is not null;
		// Only non-root nodes enable Delete folder. Do NOT test selected.Parent here:
		// despite the docs stating a root node's Parent is null, TreeView.RootNodes.Add
		// reparents nodes onto an internal hidden root, so the visible "Task Scheduler
		// Library" node reports a non-null Parent. Test membership in RootNodes instead.
		IsNonRootTreeNodeSelected = selected is not null && !LibraryTreeView.RootNodes.Contains(selected);
	}

	// Double-tapping a task in the list opens its properties page.
	private void TasksListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		ScheduledTaskSample? task = (e.OriginalSource as FrameworkElement)?.DataContext as ScheduledTaskSample
			?? TasksListView.SelectedItem as ScheduledTaskSample;
		if (task is not null)
		{
			OpenTaskProperties(task);
		}
	}

	// The Properties command opens the selected task's properties page.
	private void PropertiesButton_Click(object sender, RoutedEventArgs e)
	{
		if (TasksListView.SelectedItem is ScheduledTaskSample task)
		{
			OpenTaskProperties(task);
		}
	}

	/// <summary>
	/// Navigates to <see cref="TaskPropertiesPage"/> for the given task, using the shell's
	/// breadcrumb so the hosting NavigationView shows a Back button.
	/// </summary>
	private void OpenTaskProperties(ScheduledTaskSample task)
	{
		// TODO(TaskScheduler): pass a real task identifier/model instead of the sample, and use a
		// localized breadcrumb prefix (e.g. ResourceKeys) around the task name.
		BreadcrumbNavigationService.AddBreadcrumb(task.Name, typeof(TaskPropertiesPage), task);
		Frame.Navigate(
			typeof(TaskPropertiesPage),
			task,
			new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	/// <summary>
	/// Disable/Enable is a single toggle button: it swaps glyph (Disable E769 / Enable E768) and label.
	/// TODO(TaskScheduler): set IRegisteredTask.Enabled on the selected task.
	/// </summary>
	private void DisableEnableButton_Click(object sender, RoutedEventArgs e)
	{
		_selectedTaskEnabled = !_selectedTaskEnabled;
		DisableEnableIcon.Glyph = _selectedTaskEnabled ? "" : "";
		DisableEnableButton.Label = _selectedTaskEnabled ? "Disable" : "Enable";
	}

	/// <summary>
	/// Run/End is a single toggle menu item: it swaps glyph (Run E768 / End E71A) and text.
	/// TODO(TaskScheduler): IRegisteredTask.Run(null) to start, or IRunningTask.Stop() to end.
	/// </summary>
	private void RunEndMenuItem_Click(object sender, RoutedEventArgs e)
	{
		_selectedTaskRunning = !_selectedTaskRunning;
		RunEndIcon.Glyph = _selectedTaskRunning ? "" : "";
		RunEndMenuItem.Text = _selectedTaskRunning ? "End" : "Run";
	}

	// TODO(TaskScheduler): launch the legacy taskschd.msc snap-in. No-op in the UI prototype.
	private void OpenLegacyTaskScheduler_Click(object sender, RoutedEventArgs e)
	{
	}
}

/// <summary>Sample row model for the task list. TODO(TaskScheduler): replace with a real task model.</summary>
public sealed class ScheduledTaskSample(string name, string statusLine)
{
	public string Name { get; } = name;

	public string StatusLine { get; } = statusLine;
}
