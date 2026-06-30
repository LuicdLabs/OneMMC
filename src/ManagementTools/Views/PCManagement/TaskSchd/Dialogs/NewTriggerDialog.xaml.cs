using System;
using System.Globalization;
using ManagementTools.Core.Features.PCManagement.Models.TaskSchd;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using ManagementTools.Core.Localization;
using ManagementTools.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// "New Trigger" / "Edit Trigger" editor. Builds an <see cref="TriggerModel"/> (one of the 11 trigger
/// types) from the "Begin the task" selection plus the shared Advanced settings (delay/random delay,
/// repetition, execution-time-limit, activate/expire boundaries, enabled).
/// </summary>
public sealed partial class NewTriggerDialog : ContentDialog
{
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

    private bool _initialized;
    private string? _pickedUserId;
    private string? _eventSubscription;

    // The Basic "On an event" Log/Source pickers are filled from the live system the first time the
    // event panel is shown, via a controller that also resolves the friendly channel display names
    // (e.g. "Hardware Events") that taskschd.msc shows and maps them back to the raw channel for the query.
    private EventLogPickerController? _eventPicker;
    private bool _eventListsPopulated;

    // When editing a trigger whose subscription parses as Basic, the Log/Source seed is stashed here and
    // applied once EnsureEventListsPopulated() has built the pickers (their population is deferred to the
    // first time the event panel is shown).
    private BasicEventQuery? _pendingBasicEvent;

    /// <summary>The trigger built when the dialog is committed; <see langword="null"/> if cancelled.</summary>
    public TriggerModel? ResultTrigger { get; private set; }

    public IReadOnlyList<string> Months { get; } =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    public IReadOnlyList<string> MonthDays { get; } = BuildMonthDays();

    public NewTriggerDialog(TriggerModel? triggerToEdit = null)
    {
        InitializeComponent();
        Title = L(triggerToEdit is null ? TaskSchdKeys.DialogNewTrigger : TaskSchdKeys.DialogEditTrigger);
        PrimaryButtonText = L(TaskSchdKeys.ButtonOk);
        CloseButtonText = L(TaskSchdKeys.ButtonCancel);
        Closing += OnClosing;

        MonthlyScheduleMode.SelectedIndex = 0;
        ConnectionSourceRadios.SelectedIndex = 0;
        UserModeRadios.SelectedIndex = 0;
        SessionUserModeRadios.SelectedIndex = 0;
        EventModeRadios.SelectedIndex = 0;
        BeginTaskComboBox.SelectedIndex = TriggerOnSchedule;
        ScheduleKindRadios.SelectedIndex = 0;
        UpdateEventFilterButtonLabel();

        var now = DateTimeOffset.Now;
        ScheduleStartDate.Date = now;
        ScheduleStartTime.Time = now.TimeOfDay;
        ActivateDate.Date = now;
        ActivateTime.Time = now.TimeOfDay;
        ExpireDate.Date = now.AddYears(1);
        ExpireTime.Time = now.TimeOfDay;

        if (triggerToEdit is not null)
        {
            PopulateFrom(triggerToEdit);
        }

        UpdateRepeatState();
        UpdateStopIfLongerState();
        UpdateActivateState();
        UpdateExpireState();
        UpdateUserModeState();
        UpdateSessionUserModeState();
        _initialized = true;
    }

    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    private static nint OwnerHwnd => App.MainWindowInstance is null ? 0 : WindowNative.GetWindowHandle(App.MainWindowInstance);

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

    // ====================  BUILD (controls -> model)  ====================

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.Primary)
        {
            return;
        }

        TriggerModel trigger;
        switch (BeginTaskComboBox.SelectedIndex)
        {
            case TriggerOnSchedule:
                trigger = BuildScheduleTrigger();
                break;
            case TriggerAtLogon:
                trigger = new LogonTriggerModel { UserId = SpecificUser(UserModeRadios), Delay = AdvancedDelay() };
                break;
            case TriggerAtStartup:
                trigger = new BootTriggerModel { Delay = AdvancedDelay() };
                break;
            case TriggerOnIdle:
                trigger = new IdleTriggerModel();
                break;
            case TriggerOnEvent:
                trigger = new EventTriggerModel { Subscription = BuildEventSubscription(), Delay = AdvancedDelay() };
                break;
            case TriggerAtCreation:
                trigger = new RegistrationTriggerModel { Delay = AdvancedDelay() };
                break;
            case TriggerOnConnect:
                trigger = new SessionStateChangeTriggerModel
                {
                    StateChange = ConnectionSourceRadios.SelectedIndex == 0 ? SessionStateChangeType.RemoteConnect : SessionStateChangeType.ConsoleConnect,
                    UserId = SpecificUser(SessionUserModeRadios),
                    Delay = AdvancedDelay(),
                };
                break;
            case TriggerOnDisconnect:
                trigger = new SessionStateChangeTriggerModel
                {
                    StateChange = ConnectionSourceRadios.SelectedIndex == 0 ? SessionStateChangeType.RemoteDisconnect : SessionStateChangeType.ConsoleDisconnect,
                    UserId = SpecificUser(SessionUserModeRadios),
                    Delay = AdvancedDelay(),
                };
                break;
            case TriggerOnLock:
                trigger = new SessionStateChangeTriggerModel { StateChange = SessionStateChangeType.SessionLock, UserId = SpecificUser(UserModeRadios), Delay = AdvancedDelay() };
                break;
            case TriggerOnUnlock:
                trigger = new SessionStateChangeTriggerModel { StateChange = SessionStateChangeType.SessionUnlock, UserId = SpecificUser(UserModeRadios), Delay = AdvancedDelay() };
                break;
            default:
                trigger = new TimeTriggerModel();
                break;
        }

        ApplyAdvanced(trigger);

        var error = Validate(trigger);
        if (error is not null)
        {
            args.Cancel = true;
            // Reuse the dialog title area for the error via a simple ContentDialog is not possible here;
            // surface validation by keeping the dialog open. (A lightweight teaching tip could be added.)
            return;
        }

        ResultTrigger = trigger;
    }

    private TriggerModel BuildScheduleTrigger()
    {
        var start = CombineDateTime(ScheduleStartDate, ScheduleStartTime);
        var random = AdvancedDelay();
        switch (ScheduleKindRadios.SelectedIndex)
        {
            case 1: // Daily
                return new DailyTriggerModel { StartBoundary = start, DaysInterval = (short)Math.Max(1, (int)DailyRecurBox.Value), RandomDelay = random };
            case 2: // Weekly
                return new WeeklyTriggerModel
                {
                    StartBoundary = start,
                    WeeksInterval = (short)Math.Max(1, (int)WeeklyRecurBox.Value),
                    DaysOfWeek = SelectedDaysOfWeek(),
                    RandomDelay = random,
                };
            case 3: // Monthly
                return MonthlyScheduleMode.SelectedIndex == 0 ? BuildMonthlyByDay(start, random) : BuildMonthlyByDayOfWeek(start, random);
            default: // One time
                return new TimeTriggerModel { StartBoundary = start, RandomDelay = random };
        }
    }

    private MonthlyTriggerModel BuildMonthlyByDay(DateTime? start, TimeSpan? random)
    {
        var model = new MonthlyTriggerModel { StartBoundary = start, MonthsOfYear = SelectedMonths(), RandomDelay = random };
        foreach (var item in MonthDaysGridView.SelectedItems.OfType<string>())
        {
            if (string.Equals(item, "Last", StringComparison.OrdinalIgnoreCase))
            {
                model.RunOnLastDayOfMonth = true;
            }
            else if (int.TryParse(item, out var day))
            {
                model.DaysOfMonth.Add(day);
            }
        }
        return model;
    }

    private MonthlyDowTriggerModel BuildMonthlyByDayOfWeek(DateTime? start, TimeSpan? random)
    {
        var model = new MonthlyDowTriggerModel { StartBoundary = start, MonthsOfYear = SelectedMonths(), RandomDelay = random };
        switch (MonthlyOccurrenceCombo.SelectedIndex)
        {
            case 0: model.WeeksOfMonth = TaskWeeksOfMonth.First; break;
            case 1: model.WeeksOfMonth = TaskWeeksOfMonth.Second; break;
            case 2: model.WeeksOfMonth = TaskWeeksOfMonth.Third; break;
            case 3: model.WeeksOfMonth = TaskWeeksOfMonth.Fourth; break;
            case 4: model.RunOnLastWeekOfMonth = true; break;
        }
        if (MonthlyWeekdayCombo.SelectedIndex >= 0)
        {
            model.DaysOfWeek = (TaskDaysOfWeek)(1 << MonthlyWeekdayCombo.SelectedIndex);
        }
        return model;
    }

    private void ApplyAdvanced(TriggerModel trigger)
    {
        trigger.Enabled = EnabledCheckBox.IsChecked == true;

        if (RepeatCheckBox.IsChecked == true)
        {
            trigger.Repetition.Interval = ParseDuration(RepeatEveryCombo.Text) ?? TimeSpan.FromHours(1);
            trigger.Repetition.Duration = string.Equals(DurationCombo.Text, "Indefinitely", StringComparison.OrdinalIgnoreCase) ? null : ParseDuration(DurationCombo.Text);
            trigger.Repetition.StopAtDurationEnd = StopAllCheckBox.IsChecked == true;
        }

        if (StopIfLongerCheckBox.IsChecked == true)
        {
            trigger.ExecutionTimeLimit = ParseDuration(StopIfLongerCombo.Text);
        }

        // For non-schedule triggers, Activate provides the StartBoundary.
        if (ActivatePanel.Visibility == Visibility.Visible && ActivateCheckBox.IsChecked == true)
        {
            trigger.StartBoundary = CombineDateTime(ActivateDate, ActivateTime);
        }

        if (ExpireCheckBox.IsChecked == true)
        {
            trigger.EndBoundary = CombineDateTime(ExpireDate, ExpireTime);
        }
    }

    private TimeSpan? AdvancedDelay() => DelayCheckBox.IsChecked == true ? ParseDuration(DelayCombo.Text) : null;

    private string? SpecificUser(RadioButtons radios) => radios.SelectedIndex == 1 ? _pickedUserId : null;

    private TaskDaysOfWeek SelectedDaysOfWeek()
    {
        var days = TaskDaysOfWeek.None;
        if (SundayCheck.IsChecked == true) days |= TaskDaysOfWeek.Sunday;
        if (MondayCheck.IsChecked == true) days |= TaskDaysOfWeek.Monday;
        if (TuesdayCheck.IsChecked == true) days |= TaskDaysOfWeek.Tuesday;
        if (WednesdayCheck.IsChecked == true) days |= TaskDaysOfWeek.Wednesday;
        if (ThursdayCheck.IsChecked == true) days |= TaskDaysOfWeek.Thursday;
        if (FridayCheck.IsChecked == true) days |= TaskDaysOfWeek.Friday;
        if (SaturdayCheck.IsChecked == true) days |= TaskDaysOfWeek.Saturday;
        return days;
    }

    private TaskMonthsOfYear SelectedMonths()
    {
        var months = TaskMonthsOfYear.None;
        foreach (var item in MonthsListView.SelectedItems.OfType<string>())
        {
            var index = Months.ToList().IndexOf(item);
            if (index >= 0)
            {
                months |= (TaskMonthsOfYear)(1 << index);
            }
        }
        return months == TaskMonthsOfYear.None ? TaskMonthsOfYear.AllMonths : months;
    }

    private string BuildEventSubscription()
    {
        if (EventModeRadios.SelectedIndex == 1)
        {
            return _eventSubscription ?? string.Empty;
        }

        var log = _eventPicker?.SelectedRawChannel() ?? "System";
        var source = _eventPicker?.SelectedSource();
        var id = EventIdBox.Text?.Trim();

        var criteria = new EventXPathBuilder.Criteria
        {
            EventIds = !string.IsNullOrEmpty(id) && int.TryParse(id, out _) ? id : null,
        };
        criteria.Selections.Add(new ChannelSelection
        {
            Channel = log,
            Providers = string.IsNullOrEmpty(source) ? [] : [source],
        });
        return EventXPathBuilder.BuildQueryList(criteria);
    }

    private static string? Validate(TriggerModel trigger)
    {
        if (trigger.Repetition.IsEnabled && trigger.Repetition.Duration is { } d && d <= trigger.Repetition.Interval)
        {
            return "duration";
        }
        if (trigger.StartBoundary is { } s && trigger.EndBoundary is { } e && e <= s)
        {
            return "expire";
        }
        return null;
    }

    private static DateTime? CombineDateTime(CalendarDatePicker date, TimePicker time) =>
        date.Date is { } d ? d.Date + time.Time : null;

    private static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text is "Indefinitely" or "Do not wait")
        {
            return null;
        }
        var parts = text.Trim().Split(' ', 2);
        if (parts.Length == 2 && double.TryParse(parts[0], out var n))
        {
            return parts[1].TrimEnd('s') switch
            {
                "minute" => TimeSpan.FromMinutes(n),
                "hour" => TimeSpan.FromHours(n),
                "day" => TimeSpan.FromDays(n),
                _ => null,
            };
        }
        return null;
    }

    // ====================  POPULATE (model -> controls, edit mode)  ====================

    private void PopulateFrom(TriggerModel trigger)
    {
        EnabledCheckBox.IsChecked = trigger.Enabled;
        if (trigger.Repetition.IsEnabled)
        {
            RepeatCheckBox.IsChecked = true;
            RepeatEveryCombo.Text = FormatDuration(trigger.Repetition.Interval) ?? "1 hour";
            DurationCombo.Text = trigger.Repetition.Duration is { } d ? FormatDuration(d)! : "Indefinitely";
            StopAllCheckBox.IsChecked = trigger.Repetition.StopAtDurationEnd;
        }
        if (trigger.ExecutionTimeLimit is { } etl)
        {
            StopIfLongerCheckBox.IsChecked = true;
            StopIfLongerCombo.Text = FormatDuration(etl) ?? "3 days";
        }
        if (trigger.EndBoundary is { } eb)
        {
            ExpireCheckBox.IsChecked = true;
            ExpireDate.Date = eb;
            ExpireTime.Time = eb.TimeOfDay;
        }

        switch (trigger)
        {
            case TimeTriggerModel t:
                BeginTaskComboBox.SelectedIndex = TriggerOnSchedule; ScheduleKindRadios.SelectedIndex = 0;
                SeedScheduleStart(t.StartBoundary); SeedDelay(t.RandomDelay);
                break;
            case DailyTriggerModel d:
                BeginTaskComboBox.SelectedIndex = TriggerOnSchedule; ScheduleKindRadios.SelectedIndex = 1;
                SeedScheduleStart(d.StartBoundary); DailyRecurBox.Value = d.DaysInterval; SeedDelay(d.RandomDelay);
                break;
            case WeeklyTriggerModel w:
                BeginTaskComboBox.SelectedIndex = TriggerOnSchedule; ScheduleKindRadios.SelectedIndex = 2;
                SeedScheduleStart(w.StartBoundary); WeeklyRecurBox.Value = w.WeeksInterval; ApplyDays(w.DaysOfWeek); SeedDelay(w.RandomDelay);
                break;
            case MonthlyTriggerModel or MonthlyDowTriggerModel:
                BeginTaskComboBox.SelectedIndex = TriggerOnSchedule; ScheduleKindRadios.SelectedIndex = 3;
                SeedScheduleStart(trigger.StartBoundary);
                break;
            case LogonTriggerModel l:
                BeginTaskComboBox.SelectedIndex = TriggerAtLogon; SeedUser(l.UserId, UserModeRadios, UserAccountText); SeedDelay(l.Delay);
                break;
            case BootTriggerModel b:
                BeginTaskComboBox.SelectedIndex = TriggerAtStartup; SeedDelay(b.Delay);
                break;
            case IdleTriggerModel:
                BeginTaskComboBox.SelectedIndex = TriggerOnIdle;
                break;
            case EventTriggerModel ev:
                // Mirror taskschd.msc: Basic and Custom are independent authoring paths. A subscription
                // expressible as a single log/source/ID opens in Basic (fields seeded once the pickers
                // populate) and leaves Custom as a genuine "New Event Filter" (blank). Anything richer opens
                // in Custom, pre-loaded as "Edit Event Filter". Set the pending seed/mode BEFORE selecting
                // the panel, because selecting it synchronously runs EnsureEventListsPopulated(), which
                // consumes the pending seed.
                if (EventSubscriptionParser.TryParseBasic(ev.Subscription, out var basic))
                {
                    _eventSubscription = null;
                    EventModeRadios.SelectedIndex = 0;
                    _pendingBasicEvent = basic;
                    EventIdBox.Text = basic.EventId ?? string.Empty;
                }
                else
                {
                    _eventSubscription = ev.Subscription;
                    EventModeRadios.SelectedIndex = 1;
                }
                UpdateEventFilterButtonLabel();
                BeginTaskComboBox.SelectedIndex = TriggerOnEvent;
                SeedDelay(ev.Delay);
                break;
            case RegistrationTriggerModel r:
                BeginTaskComboBox.SelectedIndex = TriggerAtCreation; SeedDelay(r.Delay);
                break;
            case SessionStateChangeTriggerModel s:
                BeginTaskComboBox.SelectedIndex = s.StateChange switch
                {
                    SessionStateChangeType.SessionLock => TriggerOnLock,
                    SessionStateChangeType.SessionUnlock => TriggerOnUnlock,
                    SessionStateChangeType.ConsoleDisconnect or SessionStateChangeType.RemoteDisconnect => TriggerOnDisconnect,
                    _ => TriggerOnConnect,
                };
                SeedUser(s.UserId, s.StateChange is SessionStateChangeType.SessionLock or SessionStateChangeType.SessionUnlock ? UserModeRadios : SessionUserModeRadios,
                    s.StateChange is SessionStateChangeType.SessionLock or SessionStateChangeType.SessionUnlock ? UserAccountText : SessionAccountText);
                SeedDelay(s.Delay);
                break;
        }
    }

    private void SeedScheduleStart(DateTime? start)
    {
        if (start is { } s)
        {
            ScheduleStartDate.Date = s;
            ScheduleStartTime.Time = s.TimeOfDay;
        }
    }

    private void SeedDelay(TimeSpan? delay)
    {
        if (delay is { } d)
        {
            DelayCheckBox.IsChecked = true;
            DelayCombo.Text = FormatDuration(d);
        }
    }

    private void SeedUser(string? userId, RadioButtons radios, TextBlock label)
    {
        if (!string.IsNullOrEmpty(userId))
        {
            _pickedUserId = userId;
            radios.SelectedIndex = 1;
            label.Text = userId;
        }
    }

    private void ApplyDays(TaskDaysOfWeek days)
    {
        SundayCheck.IsChecked = days.HasFlag(TaskDaysOfWeek.Sunday);
        MondayCheck.IsChecked = days.HasFlag(TaskDaysOfWeek.Monday);
        TuesdayCheck.IsChecked = days.HasFlag(TaskDaysOfWeek.Tuesday);
        WednesdayCheck.IsChecked = days.HasFlag(TaskDaysOfWeek.Wednesday);
        ThursdayCheck.IsChecked = days.HasFlag(TaskDaysOfWeek.Thursday);
        FridayCheck.IsChecked = days.HasFlag(TaskDaysOfWeek.Friday);
        SaturdayCheck.IsChecked = days.HasFlag(TaskDaysOfWeek.Saturday);
    }

    private static string? FormatDuration(TimeSpan? span)
    {
        if (span is not { } v || v <= TimeSpan.Zero)
        {
            return null;
        }
        if (v.TotalDays >= 1 && v.TotalDays == Math.Floor(v.TotalDays)) return $"{(int)v.TotalDays} day{(v.TotalDays > 1 ? "s" : "")}";
        if (v.TotalHours >= 1 && v.TotalHours == Math.Floor(v.TotalHours)) return $"{(int)v.TotalHours} hour{(v.TotalHours > 1 ? "s" : "")}";
        return $"{(int)v.TotalMinutes} minute{(v.TotalMinutes > 1 ? "s" : "")}";
    }

    // ====================  PANEL SWITCHING  ====================

    private void BeginTaskComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SchedulePanel is null)
        {
            return;
        }

        SchedulePanel.Visibility = UserPanel.Visibility = NoSettingsPanel.Visibility =
            IdlePanel.Visibility = EventPanel.Visibility = SessionConnPanel.Visibility = Visibility.Collapsed;

        int index = BeginTaskComboBox.SelectedIndex;
        switch (index)
        {
            case TriggerOnSchedule: SchedulePanel.Visibility = Visibility.Visible; break;
            case TriggerAtLogon:
            case TriggerOnLock:
            case TriggerOnUnlock: UserPanel.Visibility = Visibility.Visible; break;
            case TriggerAtStartup:
            case TriggerAtCreation: NoSettingsPanel.Visibility = Visibility.Visible; break;
            case TriggerOnIdle: IdlePanel.Visibility = Visibility.Visible; break;
            case TriggerOnEvent: EventPanel.Visibility = Visibility.Visible; EnsureEventListsPopulated(); break;
            case TriggerOnConnect:
            case TriggerOnDisconnect: SessionConnPanel.Visibility = Visibility.Visible; break;
        }

        bool isSchedule = index == TriggerOnSchedule;
        bool isIdle = index == TriggerOnIdle;
        DelayCheckBox.Content = isSchedule ? L(TaskSchdKeys.TriggerRandomDelay) : L(TaskSchdKeys.TriggerDelay);
        DelayCombo.SelectedIndex = isSchedule ? 2 : 0;
        SetDelayRowEnabled(!isIdle);
        ActivatePanel.Visibility = isSchedule ? Visibility.Collapsed : Visibility.Visible;
    }

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

    private void ScheduleKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScheduleDailyDetail is null)
        {
            return;
        }
        ScheduleDailyDetail.Visibility = ScheduleWeeklyDetail.Visibility = ScheduleMonthlyDetail.Visibility = Visibility.Collapsed;
        switch (ScheduleKindRadios.SelectedIndex)
        {
            case 1: ScheduleDailyDetail.Visibility = Visibility.Visible; break;
            case 2: ScheduleWeeklyDetail.Visibility = Visibility.Visible; break;
            case 3: ScheduleMonthlyDetail.Visibility = Visibility.Visible; break;
        }
    }

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

    // The Custom button reads "Edit Event Filter…" once a query exists (editing reopens it pre-loaded),
    // otherwise "New Event Filter…" — matching taskschd.msc.
    private void UpdateEventFilterButtonLabel()
    {
        if (NewEventFilterButton is null)
        {
            return;
        }
        NewEventFilterButton.Content = L(string.IsNullOrWhiteSpace(_eventSubscription)
            ? TaskSchdKeys.ButtonNewEventFilter
            : TaskSchdKeys.ButtonEditEventFilter);
    }

    // Fill the Log/Source pickers from the live system the first time the "On an event" panel is shown
    // (deferred so opening the dialog for any other trigger type does not pay the enumeration cost).
    private void EnsureEventListsPopulated()
    {
        if (_eventListsPopulated)
        {
            return;
        }
        _eventListsPopulated = true;

        _eventPicker = new EventLogPickerController(EventLogCombo, EventSourceCombo);
        _eventPicker.EnsurePopulated();

        // Apply a Basic seed captured during edit-mode PopulateFrom now that the combos exist.
        if (_pendingBasicEvent is { } basic)
        {
            _eventPicker.Select(basic.Channel, basic.Source);
            _pendingBasicEvent = null;
        }
    }

    // Picking a log narrows the Source list to the sources registered to that channel (matching taskschd.msc);
    // channels that cannot report sources fall back to the full provider list.
    private void EventLogCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _eventPicker?.RefreshSourcesForSelectedLog();

    private async void NewEventFilter_Click(object sender, RoutedEventArgs e)
    {
        // Pass the current subscription so an existing Custom query reopens in edit mode (pre-loaded),
        // rather than starting a blank filter.
        var content = new NewEventFilterContent(_eventSubscription);
        var modal = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = L(TaskSchdKeys.DialogNewEventFilter),
            Content = content,
            OwnerXamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = L(TaskSchdKeys.ButtonOk),
            CloseButtonText = L(TaskSchdKeys.ButtonCancel),
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            // Like taskschd.msc, OK stays disabled until a log/source (or a manual query) makes the filter
            // submittable; CanSubmitChanged keeps it in sync.
            IsPrimaryButtonInitiallyEnabled = content.CanSubmit,
            Width = 660,
            Height = 700,
        });

        content.CanSubmitChanged += (_, _) => modal.SetPrimaryButtonEnabled(content.CanSubmit);

        if (await modal.ShowDialogAsync() == WindowDialogResult.Primary)
        {
            _eventSubscription = content.BuildQuery();
            UpdateEventFilterButtonLabel();
        }
    }

    // ====================  USER / SESSION SCOPE  ====================

    private void UpdateUserModeState() => ChangeUserButton.IsEnabled = UserModeRadios.SelectedIndex == 1;

    private void UserMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChangeUserButton is not null)
        {
            UpdateUserModeState();
        }
    }

    private void UpdateSessionUserModeState() => SessionChangeUserButton.IsEnabled = SessionUserModeRadios.SelectedIndex == 1;

    private void SessionUserMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionChangeUserButton is not null)
        {
            UpdateSessionUserModeState();
        }
    }

    private void ChangeUser_Click(object sender, RoutedEventArgs e)
    {
        var picked = DirectoryObjectPickerService.ShowDialog(OwnerHwnd, ObjectPickerTypes.UsersAndGroups);
        if (picked is { Count: > 0 })
        {
            var obj = picked[0];
            _pickedUserId = string.IsNullOrEmpty(obj.Sid) ? obj.Name : obj.Sid;
            UserAccountText.Text = obj.Name;
            SessionAccountText.Text = obj.Name;
        }
    }

    // ====================  ADVANCED-SETTINGS ENABLEMENT  ====================

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

    private void UpdateStopIfLongerState() => StopIfLongerCombo.IsEnabled = StopIfLongerCheckBox.IsChecked == true;

    private void OnStopIfLongerChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            UpdateStopIfLongerState();
        }
    }

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
}
