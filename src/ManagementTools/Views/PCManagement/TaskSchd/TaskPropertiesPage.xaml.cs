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
	public TaskPropertiesPage()
	{
		InitializeComponent();
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;
	}

	private void OnThemeChanged(ElementTheme theme) => this.RequestedTheme = theme;

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
			// only the General identity line is updated so the navigation reflects the selection.
			GeneralCard.Description = $"{task.Name} — Keeps your Microsoft software up to date. " +
				"If disabled or stopped, your Microsoft software will not be kept up to date.";
		}
	}
}
