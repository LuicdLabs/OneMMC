using System.Globalization;
using ManagementTools.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// UI prototype for the "Create a New Trigger" editor of a modern Task Scheduler (taskschd.msc)
/// replacement, launched from <see cref="TaskPropertiesPage"/>'s Triggers &gt; "New" command.
/// </summary>
/// <remarks>
/// Everything here is MOCK / SAMPLE behaviour: the code only swaps which Settings panel is visible
/// for the chosen trigger type and keeps the dependent Advanced-settings controls enabled/disabled.
/// No trigger is ever created.
/// TODO(TaskScheduler): build the matching <c>ITrigger</c> from the selection (see the type map in
/// the XAML header) and add it to the task's <c>ITriggerCollection</c> via the Task Scheduler 2.0
/// COM API, and migrate all strings to ResourceKeys / .resw.
/// </remarks>
public sealed partial class NewTriggerDialog : ContentDialog
{
	// "Begin the task" indices. Must match the ComboBoxItem order in NewTriggerDialog.xaml.
	private const int TriggerOnSchedule = 0;
	private const int TriggerAtLogon = 1;
	private const int TriggerAtStartup = 2;
	private const int TriggerOnIdle = 3;
	private const int TriggerOnEvent = 4;
	private const int TriggerAtCreation = 5;
	private const int TriggerOnConnect = 6;
	private const int TriggerOnDisconnect = 7;
	private const int TriggerOnLock = 8;
	private const int TriggerOnUnlock = 9;

	// DelayCombo preset indices (shared "15 minutes / 30 minutes / 1 hour / 12 hours / 1 day" list).
	private const int DelayIndexFixedDefault = 0;  // "15 minutes" — non-schedule Delay default
	private const int DelayIndexRandomDefault = 2; // "1 hour" — schedule RandomDelay default

	/// <summary>
	/// Guards the checkbox enable/disable handlers so they ignore the spurious Checked/Unchecked
	/// events that fire while the XAML is being parsed (before every named control exists).
	/// </summary>
	private bool _initialized;

	/// <summary>Month names for the Monthly "Months" multi-select. TODO(TaskScheduler): localize via DateTimeFormatInfo.</summary>
	public IReadOnlyList<string> Months { get; } =
	[
		"January", "February", "March", "April", "May", "June",
		"July", "August", "September", "October", "November", "December"
	];

	/// <summary>Day-of-month options for the Monthly "Days" multi-select: 1–31 plus "Last".</summary>
	public IReadOnlyList<string> MonthDays { get; } = BuildMonthDays();

	/// <summary>
	/// Creates the dialog in <b>create</b> mode (Title "Create a New Trigger") when
	/// <paramref name="editKind"/> is <c>null</c>, or <b>edit</b> mode (Title "Edit Trigger")
	/// pre-selected to that trigger type when a kind is supplied.
	/// </summary>
	/// <param name="editKind">
	/// The concrete type of the trigger being edited, or <c>null</c> to create a new trigger. In edit
	/// mode only the panels are pre-selected from the type; the per-field values stay at the seeded
	/// sample defaults because this prototype has no real trigger definition to read.
	/// </param>
	public NewTriggerDialog(TriggerEditKind? editKind = null)
	{
		this.InitializeComponent();

		// Defaults for the secondary radio groups, applied in both modes so every panel starts from a
		// valid selection (independent of the chosen trigger type). Doing this after InitializeComponent
		// (rather than via SelectedIndex in XAML) guarantees the target panels already exist when the
		// SelectionChanged handlers run.
		MonthlyScheduleMode.SelectedIndex = 0;    // Days
		ConnectionSourceRadios.SelectedIndex = 0; // Connection from remote computer
		UserModeRadios.SelectedIndex = 0;         // Any user
		SessionUserModeRadios.SelectedIndex = 0;  // Any user
		EventModeRadios.SelectedIndex = 0;        // Basic

		if (editKind is null)
		{
			// Create mode: the defaults shown by the reference "New Trigger" dialog.
			BeginTaskComboBox.SelectedIndex = TriggerOnSchedule; // On a schedule
			ScheduleKindRadios.SelectedIndex = 0;                // One time
		}
		else
		{
			// Edit mode: retitle and pre-select the panels for the trigger being edited.
			this.Title = "Edit Trigger";
			ApplyEditKind(editKind.Value);
		}

		// Seed the date/time pickers with sample defaults that mirror how taskschd seeds a brand-new
		// trigger (Start/Activate = now, Expire = +1 year), matching the reference screenshots. Date and
		// time values cannot be set as XAML attribute strings, so they are assigned here in code-behind.
		// TODO(TaskScheduler): when editing an existing trigger, overwrite these from the trigger's
		// StartBoundary / EndBoundary instead of seeding "now".
		DateTimeOffset now = DateTimeOffset.Now;
		ScheduleStartDate.Date = now;
		ScheduleStartTime.Time = now.TimeOfDay;
		ActivateDate.Date = now;
		ActivateTime.Time = now.TimeOfDay;
		ExpireDate.Date = now.AddYears(1);
		ExpireTime.Time = now.TimeOfDay;

		// Seed the dependent enable/disable rules once for the initial (mock) values, then allow the
		// event handlers to keep them in sync. Every gate checkbox except "Enabled" defaults unchecked,
		// so its value controls start disabled.
		UpdateRepeatState();
		UpdateStopIfLongerState();
		UpdateActivateState();
		UpdateExpireState();
		UpdateUserModeState();
		UpdateSessionUserModeState();
		_initialized = true;
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

	/// <summary>
	/// Pre-selects the "Begin the task" type and (for time-based triggers) the One time / Daily /
	/// Weekly / Monthly schedule sub-kind that match the trigger being edited. The dependent panels
	/// are then revealed by the usual SelectionChanged handlers. This mirrors how taskschd maps a
	/// stored <c>ITrigger</c> back onto the dialog's controls when you edit an existing trigger.
	/// </summary>
	private void ApplyEditKind(TriggerEditKind kind)
	{
		BeginTaskComboBox.SelectedIndex = kind switch
		{
			TriggerEditKind.OneTime or TriggerEditKind.Daily or TriggerEditKind.Weekly or TriggerEditKind.Monthly => TriggerOnSchedule,
			TriggerEditKind.AtLogOn => TriggerAtLogon,
			TriggerEditKind.AtStartup => TriggerAtStartup,
			TriggerEditKind.OnIdle => TriggerOnIdle,
			TriggerEditKind.OnEvent => TriggerOnEvent,
			TriggerEditKind.AtCreation => TriggerAtCreation,
			TriggerEditKind.OnConnect => TriggerOnConnect,
			TriggerEditKind.OnDisconnect => TriggerOnDisconnect,
			TriggerEditKind.OnLock => TriggerOnLock,
			TriggerEditKind.OnUnlock => TriggerOnUnlock,
			_ => TriggerOnSchedule,
		};

		// One time for non-schedule kinds too (their Settings panel is hidden, so it is harmless).
		ScheduleKindRadios.SelectedIndex = kind switch
		{
			TriggerEditKind.Daily => 1,
			TriggerEditKind.Weekly => 2,
			TriggerEditKind.Monthly => 3,
			_ => 0,
		};
	}

	// ====================  TRIGGER TYPE  ====================

	/// <summary>
	/// Shows the Settings panel for the chosen trigger and configures the shared Advanced-settings
	/// rows that vary by type (Delay label/default, the idle-only greying, and the Activate row).
	/// </summary>
	private void BeginTaskComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		// Guard against the event firing before the named panels have been created.
		if (SchedulePanel is null)
		{
			return;
		}

		SchedulePanel.Visibility = UserPanel.Visibility = NoSettingsPanel.Visibility =
			IdlePanel.Visibility = EventPanel.Visibility = SessionConnPanel.Visibility = Visibility.Collapsed;

		int index = BeginTaskComboBox.SelectedIndex;
		switch (index)
		{
			case TriggerOnSchedule:
				SchedulePanel.Visibility = Visibility.Visible;
				break;
			case TriggerAtLogon:
			case TriggerOnLock:
			case TriggerOnUnlock:
				UserPanel.Visibility = Visibility.Visible;
				break;
			case TriggerAtStartup:
			case TriggerAtCreation:
				NoSettingsPanel.Visibility = Visibility.Visible;
				break;
			case TriggerOnIdle:
				IdlePanel.Visibility = Visibility.Visible;
				break;
			case TriggerOnEvent:
				EventPanel.Visibility = Visibility.Visible;
				break;
			case TriggerOnConnect:
			case TriggerOnDisconnect:
				SessionConnPanel.Visibility = Visibility.Visible;
				break;
		}

		bool isSchedule = index == TriggerOnSchedule;
		bool isIdle = index == TriggerOnIdle;

		// Delay row: schedule => RandomDelay ("…up to (random delay)", default 1 hour); every other
		// type => fixed Delay (default 15 minutes); On idle exposes neither, so the row is greyed.
		DelayCheckBox.Content = isSchedule ? "Delay task for up to (random delay):" : "Delay task for:";
		DelayCombo.SelectedIndex = isSchedule ? DelayIndexRandomDefault : DelayIndexFixedDefault;
		SetDelayRowEnabled(!isIdle);

		// Activate = StartBoundary. For "On a schedule" the StartBoundary is the Settings > Start
		// field, so the Activate row is hidden; every other trigger exposes it here.
		ActivatePanel.Visibility = isSchedule ? Visibility.Collapsed : Visibility.Visible;
	}

	/// <summary>Enables or disables the whole "Delay task for" row; the combo additionally follows the checkbox.</summary>
	private void SetDelayRowEnabled(bool enabled)
	{
		DelayCheckBox.IsEnabled = enabled;
		DelayCombo.IsEnabled = enabled && DelayCheckBox.IsChecked == true;
	}

	private void OnDelayChanged(object sender, RoutedEventArgs e)
	{
		if (_initialized)
		{
			DelayCombo.IsEnabled = DelayCheckBox.IsEnabled && DelayCheckBox.IsChecked == true;
		}
	}

	// ====================  SCHEDULE DETAIL  ====================

	/// <summary>Reveals the recurrence detail (none / Daily / Weekly / Monthly) for the chosen schedule kind.</summary>
	private void ScheduleKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (ScheduleDailyDetail is null)
		{
			return;
		}

		ScheduleDailyDetail.Visibility = ScheduleWeeklyDetail.Visibility =
			ScheduleMonthlyDetail.Visibility = Visibility.Collapsed;

		switch (ScheduleKindRadios.SelectedIndex)
		{
			case 1: ScheduleDailyDetail.Visibility = Visibility.Visible; break;   // Daily
			case 2: ScheduleWeeklyDetail.Visibility = Visibility.Visible; break;  // Weekly
			case 3: ScheduleMonthlyDetail.Visibility = Visibility.Visible; break; // Monthly
			// case 0 (One time): just Start, no extra detail.
		}
	}

	/// <summary>Enables either the "Days" picker or the "On" (ordinal weekday) pickers for the Monthly schedule.</summary>
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

	// ====================  EVENT TRIGGER  ====================

	/// <summary>Switches between the Basic (Log/Source/Event ID) and Custom (New Event Filter) event panels.</summary>
	private void EventMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (BasicEventSubPanel is null)
		{
			return;
		}

		bool basic = EventModeRadios.SelectedIndex == 0;
		BasicEventSubPanel.Visibility = basic ? Visibility.Visible : Visibility.Collapsed;
		CustomEventSubPanel.Visibility = basic ? Visibility.Collapsed : Visibility.Visible;
	}

	/// <summary>
	/// Opens the New Event Filter editor in a <see cref="ModalDialogWindow"/>. A ContentDialog cannot
	/// open a second ContentDialog on the same window, but ModalDialogWindow is a real top-level Window,
	/// so it can stack over this open dialog the way the legacy modal "New Event Filter" dialog does.
	/// </summary>
	private async void NewEventFilter_Click(object sender, RoutedEventArgs e)
	{
		var modal = new ModalDialogWindow(new ModalDialogOptions
		{
			Title = "New Event Filter",
			Content = new NewEventFilterContent(),
			OwnerXamlRoot = this.XamlRoot,
			RequestedTheme = App.CurrentTheme,
			PrimaryButtonText = "OK",
			CloseButtonText = "Cancel",
			DefaultButton = WindowDialogResult.Primary,
			IsPrimaryButtonLeading = true,
			Width = 660,
			Height = 700,
		});

		WindowDialogResult result = await modal.ShowDialogAsync();
		if (result == WindowDialogResult.Primary)
		{
			// TODO(TaskScheduler): read the constructed XPath from the filter content and store it as
			// IEventTrigger.Subscription. No-op in this UI prototype.
		}
	}

	// ====================  USER / SESSION SCOPE  ====================

	/// <summary>"Change User…" applies only while "Specific user" is selected (At log on / lock / unlock).</summary>
	private void UpdateUserModeState() => ChangeUserButton.IsEnabled = UserModeRadios.SelectedIndex == 1;

	private void UserMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (ChangeUserButton is not null)
		{
			UpdateUserModeState();
		}
	}

	/// <summary>"Change User…" applies only while "Specific user" is selected (session connect / disconnect).</summary>
	private void UpdateSessionUserModeState() => SessionChangeUserButton.IsEnabled = SessionUserModeRadios.SelectedIndex == 1;

	private void SessionUserMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (SessionChangeUserButton is not null)
		{
			UpdateSessionUserModeState();
		}
	}

	// TODO(TaskScheduler): show the account object picker (DsObjectPicker / IADsOpenObject) and write
	// the chosen DOMAIN\Account (or SID) back into the trigger's UserId. No-op in this UI prototype.
	private void ChangeUser_Click(object sender, RoutedEventArgs e)
	{
	}

	// ====================  ADVANCED-SETTINGS ENABLEMENT  ====================
	// Each gate checkbox enables only its own value controls — the same per-child IsEnabled pattern
	// used by TaskPropertiesPage (layout panels have no IsEnabled, so we never disable a panel).

	/// <summary>Interval, duration and the "stop all" sub-option apply only when "Repeat task every" is checked.</summary>
	private void UpdateRepeatState()
	{
		bool on = RepeatCheckBox.IsChecked == true;
		RepeatEveryCombo.IsEnabled = on;
		DurationCombo.IsEnabled = on;
		StopAllCheckBox.IsEnabled = on;
	}

	private void OnRepeatChanged(object sender, RoutedEventArgs e)
	{
		if (_initialized)
		{
			UpdateRepeatState();
		}
	}

	/// <summary>The execution-time-limit picker applies only when "Stop task if it runs longer than" is checked.</summary>
	private void UpdateStopIfLongerState() => StopIfLongerCombo.IsEnabled = StopIfLongerCheckBox.IsChecked == true;

	private void OnStopIfLongerChanged(object sender, RoutedEventArgs e)
	{
		if (_initialized)
		{
			UpdateStopIfLongerState();
		}
	}

	/// <summary>The Activate (StartBoundary) date/time and its time-zone sync apply only when checked.</summary>
	private void UpdateActivateState()
	{
		bool on = ActivateCheckBox.IsChecked == true;
		ActivateDate.IsEnabled = on;
		ActivateTime.IsEnabled = on;
		ActivateSyncCheckBox.IsEnabled = on;
	}

	private void OnActivateChanged(object sender, RoutedEventArgs e)
	{
		if (_initialized)
		{
			UpdateActivateState();
		}
	}

	/// <summary>The Expire (EndBoundary) date/time and its time-zone sync apply only when checked.</summary>
	private void UpdateExpireState()
	{
		bool on = ExpireCheckBox.IsChecked == true;
		ExpireDate.IsEnabled = on;
		ExpireTime.IsEnabled = on;
		ExpireSyncCheckBox.IsEnabled = on;
	}

	private void OnExpireChanged(object sender, RoutedEventArgs e)
	{
		if (_initialized)
		{
			UpdateExpireState();
		}
	}

	// TODO(TaskScheduler): before committing on OK, validate the selection the way taskschd does, e.g.:
	//   * Repetition.Duration must be strictly greater than Repetition.Interval (unless "Indefinitely").
	//   * "Stop all running tasks…" only applies while Repeat is enabled.
	//   * Expire (EndBoundary) must be after Activate/Start (StartBoundary) when both are set.
	//   * "On a schedule" requires a valid Start; Daily/Weekly/Monthly require their sub-fields
	//     (DaysInterval ≥ 1; ≥1 weekday + WeeksInterval ≥ 1; ≥1 month + day-of-month or week+weekday).
	//   * "On an event" Basic requires a Log/Source/Event ID; Custom requires a non-empty Subscription.
	//   * "Specific user" requires a resolved account/SID.
	// On success, build the ITrigger and return it to the caller. The dialog is sample-only for now.
}

/// <summary>
/// The concrete trigger types a row in the Triggers list can represent, passed to
/// <see cref="NewTriggerDialog"/> so it can pre-select the matching panels in edit mode.
/// The four time-based kinds all map to the "On a schedule" type plus a schedule sub-kind,
/// matching how taskschd resolves a stored ITrigger back onto the "Begin the task" choice.
/// </summary>
/// <remarks>TODO(TaskScheduler): derive this from the real ITrigger.Type when editing a live task.</remarks>
public enum TriggerEditKind
{
	/// <summary>One-time schedule (ITimeTrigger).</summary>
	OneTime,
	/// <summary>Daily schedule (IDailyTrigger).</summary>
	Daily,
	/// <summary>Weekly schedule (IWeeklyTrigger).</summary>
	Weekly,
	/// <summary>Monthly schedule (IMonthlyTrigger / IMonthlyDOWTrigger).</summary>
	Monthly,
	/// <summary>At log on (ILogonTrigger).</summary>
	AtLogOn,
	/// <summary>At startup (IBootTrigger).</summary>
	AtStartup,
	/// <summary>On idle (IIdleTrigger).</summary>
	OnIdle,
	/// <summary>On an event (IEventTrigger).</summary>
	OnEvent,
	/// <summary>At task creation/modification (IRegistrationTrigger).</summary>
	AtCreation,
	/// <summary>On connection to user session (ISessionStateChangeTrigger).</summary>
	OnConnect,
	/// <summary>On disconnect from user session (ISessionStateChangeTrigger).</summary>
	OnDisconnect,
	/// <summary>On workstation lock (ISessionStateChangeTrigger).</summary>
	OnLock,
	/// <summary>On workstation unlock (ISessionStateChangeTrigger).</summary>
	OnUnlock,
}
