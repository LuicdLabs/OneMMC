using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// UI prototype for the "Create a Task" dialog of a modern Task Scheduler (taskschd.msc)
/// replacement, launched from <see cref="TaskSchedulerPage"/>.
/// </summary>
/// <remarks>
/// Everything here is MOCK / SAMPLE behaviour: the code only toggles which settings panel is
/// visible based on the Trigger / Action drop-downs and performs a trivial name validation.
/// No task is ever registered.
/// TODO(TaskScheduler): build an ITaskDefinition from the selected trigger + action and register
/// it via the Task Scheduler 2.0 COM API (ITaskService.NewTask / ITaskFolder.RegisterTaskDefinition)
/// or Microsoft.Win32.TaskScheduler, and migrate all strings to ResourceKeys / .resw.
/// </remarks>
public sealed partial class CreateTaskDialog : ContentDialog
{
	/// <summary>Month names for the Monthly "Months" multi-select. TODO(TaskScheduler): localize via DateTimeFormatInfo.</summary>
	public IReadOnlyList<string> Months { get; } =
	[
		"January", "February", "March", "April", "May", "June",
		"July", "August", "September", "October", "November", "December"
	];

	/// <summary>Day-of-month options for the Monthly "Days" multi-select: 1–31 plus "Last".</summary>
	public IReadOnlyList<string> MonthDays { get; } = BuildMonthDays();

	/// <summary>Task name entered by the user. TODO(TaskScheduler): surface the full trigger/action selection too.</summary>
	public string TaskName => NameBox.Text;

	/// <summary>Optional task description entered by the user.</summary>
	public string TaskDescription => DescriptionBox.Text;

	public CreateTaskDialog()
	{
		this.InitializeComponent();
		this.Closing += CreateTaskDialog_Closing;

		// Select the defaults shown by the reference dialog. Doing this after InitializeComponent
		// (rather than via SelectedIndex in XAML) guarantees the target panels already exist when
		// the SelectionChanged handlers run.
		TriggerComboBox.SelectedIndex = 0;        // Daily
		ActionComboBox.SelectedIndex = 0;         // Start a program
		MonthlyScheduleMode.SelectedIndex = 0;    // Days
	}

	private static string[] BuildMonthDays()
	{
		var days = new string[32];
		for (int day = 1; day <= 31; day++)
		{
			days[day - 1] = day.ToString(CultureInfo.InvariantCulture);
		}

		days[31] = "Last";
		return days;
	}

	/// <summary>Shows the settings panel that matches the chosen trigger; the rest stay collapsed.</summary>
	private void TriggerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		// Guard against the event firing before the named panels have been created.
		if (DailyPanel is null)
		{
			return;
		}

		DailyPanel.Visibility = WeeklyPanel.Visibility = MonthlyPanel.Visibility =
			OneTimePanel.Visibility = TriggerNoSettingsPanel.Visibility =
			EventPanel.Visibility = Visibility.Collapsed;

		// Indices must match the ComboBoxItem order in CreateTaskDialog.xaml.
		switch (TriggerComboBox.SelectedIndex)
		{
			case 0: DailyPanel.Visibility = Visibility.Visible; break;
			case 1: WeeklyPanel.Visibility = Visibility.Visible; break;
			case 2: MonthlyPanel.Visibility = Visibility.Visible; break;
			case 3: OneTimePanel.Visibility = Visibility.Visible; break;
			case 4: // When the computer starts
			case 5: TriggerNoSettingsPanel.Visibility = Visibility.Visible; break; // When I log on
			case 6: EventPanel.Visibility = Visibility.Visible; break; // When a specific event is logged
		}
	}

	/// <summary>Shows the settings panel that matches the chosen action; the rest stay collapsed.</summary>
	private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (StartProgramPanel is null)
		{
			return;
		}

		StartProgramPanel.Visibility = SendEmailPanel.Visibility =
			DisplayMessagePanel.Visibility = Visibility.Collapsed;

		// Indices must match the ComboBoxItem order in CreateTaskDialog.xaml.
		switch (ActionComboBox.SelectedIndex)
		{
			case 0: StartProgramPanel.Visibility = Visibility.Visible; break;
			case 1: SendEmailPanel.Visibility = Visibility.Visible; break;
			case 2: DisplayMessagePanel.Visibility = Visibility.Visible; break;
		}
	}

	/// <summary>Enables either the "Days" picker or the "On" (ordinal weekday) pickers for the Monthly trigger.</summary>
	private void MonthlyScheduleMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (MonthlyDaysDropDown is null)
		{
			return;
		}

		bool daysMode = MonthlyScheduleMode.SelectedIndex == 0;
		MonthlyDaysDropDown.IsEnabled = daysMode;
		MonthlyOccurrenceCombo.IsEnabled = !daysMode;
		MonthlyWeekdayCombo.IsEnabled = !daysMode;
	}

	/// <summary>Validates the minimal required input (a non-empty name) before the dialog closes with "Add".</summary>
	private void CreateTaskDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
	{
		if (args.Result != ContentDialogResult.Primary)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(NameBox.Text))
		{
			args.Cancel = true;
			ValidationInfoBar.Message = "Enter a name for the task.";
			ValidationInfoBar.IsOpen = true;
		}
	}

	// TODO(TaskScheduler): replace with a real file picker (FileOpenPicker initialized with the
	// owning window's HWND) that sets ProgramScriptBox.Text. No-op in this UI prototype.
	private void BrowseProgram_Click(object sender, RoutedEventArgs e)
	{
	}

	// TODO(TaskScheduler): replace with a real file picker that sets EmailAttachmentBox.Text.
	// No-op in this UI prototype.
	private void BrowseAttachment_Click(object sender, RoutedEventArgs e)
	{
	}
}
