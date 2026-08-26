using System;
using System.Globalization;
using OneMMC.Core.Features.PCManagement.Models.TaskSchd;
using OneMMC.Core.Features.PCManagement.Services.EventViewer;
using OneMMC.Core.Features.PCManagement.ViewModels.TaskSchd;
using OneMMC.Core.Localization;
using OneMMC.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace OneMMC.Views.PCManagement;

/// <summary>
/// "Create a Task" dialog. Collects a name/description, one trigger and one action, and builds a
/// <see cref="CreateTaskResult"/> the page registers. This is the streamlined create flow; the full
/// per-tab editor (all triggers/actions/conditions/settings) is the task Properties page.
/// </summary>
public sealed partial class CreateTaskDialog : ContentDialog
{
    private const int TriggerDaily = 0;
    private const int TriggerWeekly = 1;
    private const int TriggerMonthly = 2;
    private const int TriggerOneTime = 3;
    private const int TriggerStartup = 4;
    private const int TriggerLogon = 5;
    private const int TriggerEvent = 6;

    private const int ActionExec = 0;
    private const int ActionEmail = 1;
    private const int ActionMessage = 2;

    // The Basic "On an event" Log/Source pickers are filled from the live system the first time the
    // event trigger is selected, via a controller that also resolves the friendly channel display names
    // (e.g. "Hardware Events") that taskschd.msc shows and maps them back to the raw channel for the query.
    private EventLogPickerController? _eventPicker;
    private bool _eventListsPopulated;

    /// <summary>Month names for the Monthly schedule's multi-select "Months" flyout, January first.</summary>
    /// <remarks>
    /// Deliberately built by <see cref="BuildMonths"/> so the runtime type is a plain <see cref="string"/>
    /// array. A non-empty collection expression assigned to <see cref="IReadOnlyList{T}"/> lowers to the
    /// compiler-synthesized <c>&lt;&gt;z__ReadOnlyArray&lt;T&gt;</c>, which CsWinRT cannot project as a WinRT
    /// collection; binding one to <c>ItemsControl.ItemsSource</c> fails with E_INVALIDARG
    /// ("Value does not fall within the expected range").
    /// </remarks>
    public IReadOnlyList<string> Months { get; } = BuildMonths();

    public IReadOnlyList<string> MonthDays { get; } = BuildMonthDays();

    /// <summary>The task name entered by the user.</summary>
    public string TaskName => NameBox.Text;

    /// <summary>The optional description entered by the user.</summary>
    public string TaskDescription => DescriptionBox.Text;

    /// <summary>The built task, set when the dialog is committed; <see langword="null"/> if cancelled.</summary>
    public CreateTaskResult? Result { get; private set; }

    public CreateTaskDialog()
    {
        InitializeComponent();
        Title = L(TaskSchdKeys.DialogCreateTask);
        PrimaryButtonText = L(TaskSchdKeys.CommandCreateTask);
        CloseButtonText = L(TaskSchdKeys.ButtonCancel);
        Closing += OnClosing;

        TriggerComboBox.SelectedIndex = TriggerDaily;
        ActionComboBox.SelectedIndex = ActionExec;
        MonthlyScheduleMode.SelectedIndex = 0;
    }

    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    private static nint OwnerHwnd => App.MainWindowInstance is null ? 0 : WindowNative.GetWindowHandle(App.MainWindowInstance);

    private static string[] BuildMonths() =>
        [
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        ];

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

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            args.Cancel = true;
            ShowValidation(L(TaskSchdKeys.ValidationNameRequired));
            return;
        }

        var action = BuildAction();
        if (action is null)
        {
            args.Cancel = true;
            ShowValidation(L(TaskSchdKeys.ValidationProgramRequired));
            return;
        }

        var def = new TaskDefinitionModel();
        def.RegistrationInfo.Description = NullIfEmpty(DescriptionBox.Text);
        def.RegistrationInfo.Author = Environment.UserName;
        def.Principal.LogonType = TaskLogonType.InteractiveToken;
        def.Principal.RunLevel = TaskRunLevel.LeastPrivilege;
        def.Triggers.Add(BuildTrigger());
        def.Actions.Add(action);

        Result = new CreateTaskResult(NameBox.Text.Trim(), def, Password: null);
    }

    private TriggerModel BuildTrigger()
    {
        var now = DateTime.Now;
        return TriggerComboBox.SelectedIndex switch
        {
            TriggerWeekly => new WeeklyTriggerModel { StartBoundary = now, WeeksInterval = 1, DaysOfWeek = TaskDaysOfWeek.AllDays },
            TriggerMonthly => new MonthlyTriggerModel { StartBoundary = now, MonthsOfYear = TaskMonthsOfYear.AllMonths, DaysOfMonth = { 1 } },
            TriggerOneTime => new TimeTriggerModel { StartBoundary = now },
            TriggerStartup => new BootTriggerModel(),
            TriggerLogon => new LogonTriggerModel(),
            TriggerEvent => new EventTriggerModel { Subscription = BuildEventSubscription() },
            _ => new DailyTriggerModel { StartBoundary = now, DaysInterval = 1 },
        };
    }

    private string BuildEventSubscription()
    {
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

    // Fill the Log/Source pickers from the live system the first time the event trigger is shown.
    private void EnsureEventListsPopulated()
    {
        if (_eventListsPopulated)
        {
            return;
        }
        _eventListsPopulated = true;

        _eventPicker = new EventLogPickerController(EventLogCombo, EventSourceCombo);
        _eventPicker.EnsurePopulated();
    }

    // Picking a log narrows the Source list to the sources registered to that channel (matching taskschd.msc).
    private void EventLogCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _eventPicker?.RefreshSourcesForSelectedLog();

    private ActionModel? BuildAction()
    {
        switch (ActionComboBox.SelectedIndex)
        {
            case ActionEmail:
                return new EmailActionModel
                {
                    From = NullIfEmpty(EmailFromBox.Text),
                    To = NullIfEmpty(EmailToBox.Text),
                    Subject = NullIfEmpty(EmailSubjectBox.Text),
                    Body = NullIfEmpty(EmailTextBox.Text),
                    Server = NullIfEmpty(EmailSmtpBox.Text),
                };
            case ActionMessage:
                return new ShowMessageActionModel
                {
                    Title = NullIfEmpty(MessageTitleBox.Text),
                    MessageBody = NullIfEmpty(MessageBodyBox.Text),
                };
            default:
                if (string.IsNullOrWhiteSpace(ProgramScriptBox.Text))
                {
                    return null;
                }
                return new ExecActionModel
                {
                    Path = ProgramScriptBox.Text.Trim(),
                    Arguments = NullIfEmpty(ArgumentsBox.Text),
                    WorkingDirectory = NullIfEmpty(StartInBox.Text),
                };
        }
    }

    private void ShowValidation(string message)
    {
        ValidationInfoBar.Message = message;
        ValidationInfoBar.IsOpen = true;
    }

    private void TriggerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DailyPanel is null)
        {
            return;
        }

        DailyPanel.Visibility = WeeklyPanel.Visibility = MonthlyPanel.Visibility =
            OneTimePanel.Visibility = TriggerNoSettingsPanel.Visibility = EventPanel.Visibility = Visibility.Collapsed;

        switch (TriggerComboBox.SelectedIndex)
        {
            case TriggerDaily: DailyPanel.Visibility = Visibility.Visible; break;
            case TriggerWeekly: WeeklyPanel.Visibility = Visibility.Visible; break;
            case TriggerMonthly: MonthlyPanel.Visibility = Visibility.Visible; break;
            case TriggerOneTime: OneTimePanel.Visibility = Visibility.Visible; break;
            case TriggerStartup:
            case TriggerLogon: TriggerNoSettingsPanel.Visibility = Visibility.Visible; break;
            case TriggerEvent: EventPanel.Visibility = Visibility.Visible; EnsureEventListsPopulated(); break;
        }
    }

    private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StartProgramPanel is null)
        {
            return;
        }

        StartProgramPanel.Visibility = SendEmailPanel.Visibility = DisplayMessagePanel.Visibility = Visibility.Collapsed;
        switch (ActionComboBox.SelectedIndex)
        {
            case ActionExec: StartProgramPanel.Visibility = Visibility.Visible; break;
            case ActionEmail: SendEmailPanel.Visibility = Visibility.Visible; break;
            case ActionMessage: DisplayMessagePanel.Visibility = Visibility.Visible; break;
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

    private async void BrowseProgram_Click(object sender, RoutedEventArgs e)
    {
        var path = await App.GetRequiredService<IFileDialogService>()
            .OpenFileAsync(OwnerHwnd, "Programs\0*.exe;*.bat;*.cmd;*.ps1\0All Files\0*.*\0", title: L(TaskSchdKeys.ActionProgramScript));
        if (!string.IsNullOrEmpty(path))
        {
            ProgramScriptBox.Text = path;
        }
    }

    private async void BrowseAttachment_Click(object sender, RoutedEventArgs e)
    {
        var path = await App.GetRequiredService<IFileDialogService>()
            .OpenFileAsync(OwnerHwnd, "All Files\0*.*\0", title: L(TaskSchdKeys.ActionEmailAttachment));
        if (!string.IsNullOrEmpty(path))
        {
            EmailAttachmentBox.Text = path;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
