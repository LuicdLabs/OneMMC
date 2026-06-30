using System;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

/// <summary>The bounds chosen in the Custom Range dialog.</summary>
public sealed class CustomRangeSelection
{
    /// <summary>Lower bound (local time); <see langword="null"/> means "First Event" (no lower bound).</summary>
    public DateTime? From { get; init; }

    /// <summary>Upper bound (local time); <see langword="null"/> means "Last Event" (no upper bound).</summary>
    public DateTime? To { get; init; }

    /// <summary>True when neither bound is set — equivalent to "Any time".</summary>
    public bool IsUnbounded => From is null && To is null;
}

/// <summary>
/// "Custom Range" editor reached from the New Event Filter "Logged" picker. Each of the From and To
/// bounds is either open (First Event / Last Event) or a specific "Events On" date and time. The OK
/// button is blocked when a fully specified range has From at or after To.
/// </summary>
public sealed partial class CustomRangeDialog : ContentDialog
{
    private const int ModeOpen = 0;     // "First Event" / "Last Event"
    private const int ModeEventsOn = 1; // a specific date & time

    /// <summary>The committed range; <see langword="null"/> when the dialog was cancelled.</summary>
    public CustomRangeSelection? Result { get; private set; }

    public CustomRangeDialog(CustomRangeSelection? initial = null)
    {
        InitializeComponent();

        var seed = DateTimeOffset.Now;
        var from = initial?.From ?? seed.DateTime;
        var to = initial?.To ?? seed.DateTime;

        FromDate.Date = new DateTimeOffset(from);
        FromTime.Time = from.TimeOfDay;
        ToDate.Date = new DateTimeOffset(to);
        ToTime.Time = to.TimeOfDay;

        FromModeCombo.SelectedIndex = initial?.From is not null ? ModeEventsOn : ModeOpen;
        ToModeCombo.SelectedIndex = initial?.To is not null ? ModeEventsOn : ModeOpen;

        UpdateEnabledState();
        Closing += OnClosing;
    }

    private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEnabledState();

    // The date/time pickers are only meaningful for the "Events On" mode; disable them otherwise.
    private void UpdateEnabledState()
    {
        if (FromDate is null)
        {
            return; // controls not realized yet during initial parse
        }

        var fromOn = FromModeCombo.SelectedIndex == ModeEventsOn;
        FromDate.IsEnabled = fromOn;
        FromTime.IsEnabled = fromOn;

        var toOn = ToModeCombo.SelectedIndex == ModeEventsOn;
        ToDate.IsEnabled = toOn;
        ToTime.IsEnabled = toOn;
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.Primary)
        {
            return;
        }

        var from = FromModeCombo.SelectedIndex == ModeEventsOn ? Combine(FromDate, FromTime) : null;
        var to = ToModeCombo.SelectedIndex == ModeEventsOn ? Combine(ToDate, ToTime) : null;

        // a fully specified range must run forward in time.
        if (from is { } f && to is { } t && f >= t)
        {
            args.Cancel = true;
            ValidationBar.Message = "The From date must be earlier than the To date.";
            ValidationBar.IsOpen = true;
            return;
        }

        Result = new CustomRangeSelection { From = from, To = to };
    }

    private static DateTime? Combine(CalendarDatePicker date, TimePicker time) =>
        date.Date is { } d ? d.Date + time.Time : null;
}
