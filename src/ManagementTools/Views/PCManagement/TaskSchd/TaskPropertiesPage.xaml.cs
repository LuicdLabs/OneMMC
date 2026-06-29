using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ManagementTools.Core.Features.PCManagement.Models.TaskSchd;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using ManagementTools.Core.Features.PCManagement.Services.TaskSchd;
using ManagementTools.Core.Localization;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// Editor for a single scheduled task. Loads the real definition via <see cref="ITaskSchedulerService"/>,
/// populates the General/Security/TaskTriggers/Actions/Conditions/Settings/History sections, and writes the
/// edits back by re-registering the task.
/// </summary>
public sealed partial class TaskPropertiesPage : Page
{
    private readonly ITaskSchedulerService _service = App.GetRequiredService<ITaskSchedulerService>();
    private readonly TaskHistoryService _history = App.GetRequiredService<TaskHistoryService>();

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ObservableCollection<TriggerRowItem> TaskTriggers { get; } = [];
    public ObservableCollection<ActionRowItem> Actions { get; } = [];
    public ObservableCollection<HistoryRowItem> History { get; } = [];

    private string? _taskPath;
    private TaskDefinitionModel _definition = new();
    private bool _initialized;

    public TaskPropertiesPage()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(ElementTheme theme) => this.RequestedTheme = theme;

    private static nint OwnerHwnd => App.MainWindowInstance is null ? 0 : WindowNative.GetWindowHandle(App.MainWindowInstance);

    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string taskPath && !string.IsNullOrEmpty(taskPath))
        {
            _taskPath = taskPath;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (_taskPath is null)
        {
            return;
        }

        try
        {
            var info = await _service.GetTaskInfoAsync(_taskPath);
            _definition = await _service.GetTaskDefinitionAsync(_taskPath);
            PopulateFromDefinition(_definition, info);
            await LoadHistoryAsync();
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
        catch (Exception)
        {
            // Leave defaults if the task cannot be read.
        }
    }

    private void PopulateFromDefinition(TaskDefinitionModel def, TaskInfo info)
    {
        _initialized = false;

        GeneralNameText.Text = info.Name;
        GeneralDescriptionText.Text = def.RegistrationInfo.Description ?? string.Empty;
        AuthorCard.Description = def.RegistrationInfo.Author ?? string.Empty;
        LocationCard.Description = info.FolderPath;
        ConfigureForCombo.SelectedIndex = CompatibilityToIndex(def.Settings.Compatibility);
        EnabledToggle.IsOn = info.Enabled;
        HideMenuItem.IsChecked = def.Settings.Hidden;

        // Security / principal
        var principal = def.Principal;
        AccountText.Text = principal.DisplayName ?? principal.UserId ?? principal.GroupId ?? string.Empty;
        RunWhetherLoggedOnRadio.IsChecked = principal.RunWhetherLoggedOn;
        RunOnlyLoggedOnRadio.IsChecked = !principal.RunWhetherLoggedOn;
        DoNotStorePasswordCheckBox.IsChecked = principal.LogonType == TaskLogonType.S4U;
        HighestPrivilegesToggle.IsOn = principal.RunLevel == TaskRunLevel.HighestAvailable;

        // TaskTriggers / actions
        TaskTriggers.Clear();
        foreach (var t in def.Triggers)
        {
            TaskTriggers.Add(new TriggerRowItem(t));
        }
        Actions.Clear();
        foreach (var a in def.Actions)
        {
            Actions.Add(new ActionRowItem(a));
        }
        RefreshActionMoveFlags();

        // Conditions
        var s = def.Settings;
        IdleToggle.IsOn = s.RunOnlyIfIdle;
        IdleStartComboBox.Text = FormatDuration(s.IdleSettings.IdleDuration) ?? "10 minutes";
        IdleWaitComboBox.Text = FormatDuration(s.IdleSettings.WaitTimeout) ?? "1 hour";
        IdleStopCeasesCheckBox.IsChecked = s.IdleSettings.StopOnIdleEnd;
        IdleRestartResumesCheckBox.IsChecked = s.IdleSettings.RestartOnIdle;
        PowerToggle.IsOn = s.DisallowStartIfOnBatteries;
        StopOnBatteryCheckBox.IsChecked = s.StopIfGoingOnBatteries;
        WakeToRunCheckBox.IsChecked = s.WakeToRun;
        NetworkToggle.IsOn = s.RunOnlyIfNetworkAvailable;

        // Settings
        AllowDemandStartCheckBox.IsChecked = s.AllowDemandStart;
        StartWhenAvailableCheckBox.IsChecked = s.StartWhenAvailable;
        RestartOnFailureCheckBox.IsChecked = s.RestartCount > 0;
        RestartIntervalComboBox.Text = FormatDuration(s.RestartInterval) ?? "1 minute";
        RestartCountNumberBox.Value = s.RestartCount > 0 ? s.RestartCount : 3;
        StopIfRunsLongerCheckBox.IsChecked = s.ExecutionTimeLimit is not null;
        StopIfRunsLongerComboBox.Text = FormatDuration(s.ExecutionTimeLimit) ?? "3 days";
        ForceStopCheckBox.IsChecked = s.AllowHardTerminate;
        DeleteAfterCheckBox.IsChecked = s.DeleteExpiredTaskAfter is not null;
        DeleteAfterComboBox.Text = FormatDuration(s.DeleteExpiredTaskAfter) ?? "30 days";
        InstancesCombo.SelectedIndex = (int)s.MultipleInstances;

        _initialized = true;
        UpdateAllConditionalStates();
    }

    private async Task LoadHistoryAsync()
    {
        History.Clear();
        var enabled = _history.IsHistoryEnabled();
        HistoryDisabledInfoBar.IsOpen = !enabled;
        if (!enabled || _taskPath is null)
        {
            return;
        }

        try
        {
            var events = await _history.ReadTaskHistoryAsync(_taskPath, 100);
            foreach (var ev in events)
            {
                History.Add(new HistoryRowItem(
                    $"{ev.LevelDisplayName} — {ev.TaskCategory}",
                    $"{ev.TimeCreated:g} | {L(TaskSchdKeys.HistoryColEvent)} {ev.EventId}"));
            }
        }
        catch (Exception)
        {
            // History is best-effort.
        }
    }

    // ----- Command bar -----

    private async void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _taskPath is null)
        {
            return;
        }
        try
        {
            await _service.SetTaskEnabledAsync(_taskPath, EnabledToggle.IsOn);
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_taskPath is null)
        {
            return;
        }
        try
        {
            await _service.RunTaskAsync(_taskPath);
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_taskPath is null)
        {
            return;
        }

        BuildDefinitionFromControls();
        var (folderPath, name) = SplitPath(_taskPath);
        try
        {
            await _service.RegisterTaskAsync(folderPath, name, _definition);
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(string.Format(L(TaskSchdKeys.ErrorOperationFailed), ex.Message));
        }
    }

    private void BuildDefinitionFromControls()
    {
        var def = _definition;
        def.RegistrationInfo.Description = GeneralDescriptionText.Text;
        def.Settings.Compatibility = IndexToCompatibility(ConfigureForCombo.SelectedIndex);
        def.Settings.Hidden = HideMenuItem.IsChecked;

        // Principal
        var runWhether = RunWhetherLoggedOnRadio.IsChecked == true;
        def.Principal.RunLevel = HighestPrivilegesToggle.IsOn ? TaskRunLevel.HighestAvailable : TaskRunLevel.LeastPrivilege;
        if (string.IsNullOrEmpty(def.Principal.GroupId))
        {
            def.Principal.LogonType = runWhether
                ? (DoNotStorePasswordCheckBox.IsChecked == true ? TaskLogonType.S4U : TaskLogonType.Password)
                : TaskLogonType.InteractiveToken;
        }

        // TaskTriggers / actions are kept in sync by the row collections.
        def.Triggers.Clear();
        foreach (var t in TaskTriggers)
        {
            def.Triggers.Add(t.Model);
        }
        def.Actions.Clear();
        foreach (var a in Actions)
        {
            def.Actions.Add(a.Model);
        }

        // Conditions
        var s = def.Settings;
        s.RunOnlyIfIdle = IdleToggle.IsOn;
        s.IdleSettings.IdleDuration = ParseDuration(IdleStartComboBox.Text);
        s.IdleSettings.WaitTimeout = ParseDuration(IdleWaitComboBox.Text);
        s.IdleSettings.StopOnIdleEnd = IdleStopCeasesCheckBox.IsChecked == true;
        s.IdleSettings.RestartOnIdle = IdleRestartResumesCheckBox.IsChecked == true;
        s.DisallowStartIfOnBatteries = PowerToggle.IsOn;
        s.StopIfGoingOnBatteries = StopOnBatteryCheckBox.IsChecked == true;
        s.WakeToRun = WakeToRunCheckBox.IsChecked == true;
        s.RunOnlyIfNetworkAvailable = NetworkToggle.IsOn;

        // Settings
        s.AllowDemandStart = AllowDemandStartCheckBox.IsChecked == true;
        s.StartWhenAvailable = StartWhenAvailableCheckBox.IsChecked == true;
        if (RestartOnFailureCheckBox.IsChecked == true)
        {
            s.RestartInterval = ParseDuration(RestartIntervalComboBox.Text) ?? TimeSpan.FromMinutes(1);
            s.RestartCount = (int)RestartCountNumberBox.Value;
        }
        else
        {
            s.RestartInterval = null;
            s.RestartCount = 0;
        }
        s.ExecutionTimeLimit = StopIfRunsLongerCheckBox.IsChecked == true ? ParseDuration(StopIfRunsLongerComboBox.Text) : null;
        s.AllowHardTerminate = ForceStopCheckBox.IsChecked == true;
        s.DeleteExpiredTaskAfter = DeleteAfterCheckBox.IsChecked == true ? (ParseDuration(DeleteAfterComboBox.Text) ?? TimeSpan.FromDays(30)) : null;
        s.MultipleInstances = (TaskInstancesPolicy)InstancesCombo.SelectedIndex;
    }

    private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_taskPath is null)
        {
            return;
        }
        try
        {
            var xml = await _service.ExportTaskAsync(_taskPath);
            var fileDialog = App.GetRequiredService<IFileDialogService>();
            var path = await fileDialog.SaveFileAsync(OwnerHwnd, "XML Files\0*.xml\0All Files\0*.*\0", title: L(TaskSchdKeys.CommandExportTask), defaultExtension: ".xml", suggestedFileName: GeneralNameText.Text + ".xml");
            if (!string.IsNullOrEmpty(path))
            {
                await File.WriteAllTextAsync(path, xml);
            }
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
    }

    private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_taskPath is null)
        {
            return;
        }
        if (!await ConfirmAsync(string.Format(L(TaskSchdKeys.ConfirmDeleteTaskFormat), GeneralNameText.Text)))
        {
            return;
        }
        try
        {
            await _service.DeleteTaskAsync(_taskPath);
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
    }

    private async void SecurityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_taskPath is null)
        {
            return;
        }
        try
        {
            var sddl = await _service.GetTaskSecurityDescriptorAsync(_taskPath, 0x1 | 0x2 | 0x4) ?? string.Empty;
            var edited = await PromptTextAsync(L(TaskSchdKeys.CommandSecurity), "SDDL", sddl, multiline: true);
            if (edited is not null && !string.Equals(edited, sddl, StringComparison.Ordinal))
            {
                await _service.SetTaskSecurityDescriptorAsync(_taskPath, edited);
            }
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
    }

    // ----- General edit / copy -----

    private async void GeneralEdit_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = L(TaskSchdKeys.GeneralName), Text = GeneralNameText.Text, MinWidth = 360 };
        var descBox = new TextBox { Header = L(TaskSchdKeys.GeneralDescription), Text = GeneralDescriptionText.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(nameBox);
        panel.Children.Add(descBox);
        var dialog = new ContentDialog
        {
            Title = L(TaskSchdKeys.TabGeneral),
            Content = panel,
            PrimaryButtonText = L(TaskSchdKeys.ButtonOk),
            CloseButtonText = L(TaskSchdKeys.ButtonCancel),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            GeneralDescriptionText.Text = descBox.Text;
        }
    }

    private void AuthorCopy_Click(object sender, RoutedEventArgs e) => CopyToClipboard(AuthorCard.Description?.ToString());

    private void LocationCopy_Click(object sender, RoutedEventArgs e) => CopyToClipboard(LocationCard.Description?.ToString());

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void ChangeUser_Click(object sender, RoutedEventArgs e)
    {
        var picked = DirectoryObjectPickerService.ShowDialog(OwnerHwnd, ObjectPickerTypes.UsersAndGroups);
        if (picked is { Count: > 0 })
        {
            var obj = picked[0];
            _definition.Principal.UserId = string.IsNullOrEmpty(obj.Sid) ? obj.Name : obj.Sid;
            _definition.Principal.DisplayName = obj.Name;
            _definition.Principal.GroupId = string.Equals(obj.ObjectClass, "group", StringComparison.OrdinalIgnoreCase) ? obj.Sid : null;
            AccountText.Text = obj.Name;
        }
    }

    // ----- TaskTriggers -----

    private async void NewTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewTriggerDialog { XamlRoot = this.XamlRoot, RequestedTheme = App.CurrentTheme };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.ResultTrigger is { } trigger)
        {
            TaskTriggers.Add(new TriggerRowItem(trigger));
        }
    }

    private async void TriggerEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TriggerRowItem row)
        {
            return;
        }
        var dialog = new NewTriggerDialog(row.Model) { XamlRoot = this.XamlRoot, RequestedTheme = App.CurrentTheme };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.ResultTrigger is { } trigger)
        {
            var index = TaskTriggers.IndexOf(row);
            if (index >= 0)
            {
                TaskTriggers[index] = new TriggerRowItem(trigger);
            }
        }
    }

    private void TriggerDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TriggerRowItem row)
        {
            TaskTriggers.Remove(row);
        }
    }

    // ----- Actions -----

    private async void NewActionButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewActionDialog { XamlRoot = this.XamlRoot, RequestedTheme = App.CurrentTheme };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.ResultAction is { } action)
        {
            Actions.Add(new ActionRowItem(action));
            RefreshActionMoveFlags();
        }
    }

    private async void ActionEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ActionRowItem row)
        {
            return;
        }
        var dialog = new NewActionDialog(row.Model) { XamlRoot = this.XamlRoot, RequestedTheme = App.CurrentTheme };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.ResultAction is { } action)
        {
            var index = Actions.IndexOf(row);
            if (index >= 0)
            {
                Actions[index] = new ActionRowItem(action);
                RefreshActionMoveFlags();
            }
        }
    }

    private void ActionDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ActionRowItem row)
        {
            Actions.Remove(row);
            RefreshActionMoveFlags();
        }
    }

    private void ActionMoveUp_Click(object sender, RoutedEventArgs e) => MoveAction((sender as FrameworkElement)?.Tag as ActionRowItem, -1);

    private void ActionMoveDown_Click(object sender, RoutedEventArgs e) => MoveAction((sender as FrameworkElement)?.Tag as ActionRowItem, +1);

    private void MoveAction(ActionRowItem? row, int delta)
    {
        if (row is null)
        {
            return;
        }
        var index = Actions.IndexOf(row);
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < Actions.Count)
        {
            Actions.Move(index, target);
            RefreshActionMoveFlags();
        }
    }

    private void RefreshActionMoveFlags()
    {
        for (int i = 0; i < Actions.Count; i++)
        {
            Actions[i].CanMoveUp = i > 0;
            Actions[i].CanMoveDown = i < Actions.Count - 1;
        }
    }

    // ----- Conditional enablement -----

    private void OnSecurityRunModeChanged(object sender, RoutedEventArgs e)
    {
        if (DoNotStorePasswordCheckBox is not null)
        {
            DoNotStorePasswordCheckBox.IsEnabled = RunWhetherLoggedOnRadio.IsChecked == true;
        }
    }

    private void OnIdleToggled(object sender, RoutedEventArgs e) => UpdateIdleState();

    private void OnIdleStopCeasesChanged(object sender, RoutedEventArgs e) => UpdateIdleState();

    private void OnPowerToggled(object sender, RoutedEventArgs e) => UpdatePowerState();

    private void OnNetworkToggled(object sender, RoutedEventArgs e) => UpdateNetworkState();

    private void OnRestartOnFailureChanged(object sender, RoutedEventArgs e) => UpdateRestartState();

    private void OnStopIfRunsLongerChanged(object sender, RoutedEventArgs e) => UpdateStopIfLongerState();

    private void OnDeleteAfterChanged(object sender, RoutedEventArgs e) => UpdateDeleteAfterState();

    private void UpdateAllConditionalStates()
    {
        OnSecurityRunModeChanged(this, new RoutedEventArgs());
        UpdateIdleState();
        UpdatePowerState();
        UpdateNetworkState();
        UpdateRestartState();
        UpdateStopIfLongerState();
        UpdateDeleteAfterState();
    }

    private void UpdateIdleState()
    {
        if (IdleToggle is null ||
            IdleStartComboBox is null ||
            IdleWaitComboBox is null ||
            IdleStopCeasesCheckBox is null ||
            IdleRestartResumesCheckBox is null)
        {
            return;
        }
        var on = IdleToggle.IsOn;
        IdleStartComboBox.IsEnabled = on;
        IdleWaitComboBox.IsEnabled = on;
        IdleStopCeasesCheckBox.IsEnabled = on;
        IdleRestartResumesCheckBox.IsEnabled = on && IdleStopCeasesCheckBox.IsChecked == true;
    }

    private void UpdatePowerState()
    {
        if (PowerToggle is null || StopOnBatteryCheckBox is null)
        {
            return;
        }
        StopOnBatteryCheckBox.IsEnabled = PowerToggle.IsOn;
    }

    private void UpdateNetworkState()
    {
        if (NetworkToggle is null || NetworkComboBox is null)
        {
            return;
        }
        NetworkComboBox.IsEnabled = NetworkToggle.IsOn;
    }

    private void UpdateRestartState()
    {
        if (RestartOnFailureCheckBox is null ||
            RestartIntervalComboBox is null ||
            RestartCountNumberBox is null)
        {
            return;
        }
        var on = RestartOnFailureCheckBox.IsChecked == true;
        RestartIntervalComboBox.IsEnabled = on;
        RestartCountNumberBox.IsEnabled = on;
    }

    private void UpdateStopIfLongerState()
    {
        if (StopIfRunsLongerCheckBox is null || StopIfRunsLongerComboBox is null)
        {
            return;
        }
        StopIfRunsLongerComboBox.IsEnabled = StopIfRunsLongerCheckBox.IsChecked == true;
    }

    private void UpdateDeleteAfterState()
    {
        if (DeleteAfterCheckBox is null || DeleteAfterComboBox is null)
        {
            return;
        }
        DeleteAfterComboBox.IsEnabled = DeleteAfterCheckBox.IsChecked == true;
    }

    // ----- Small dialog helpers -----

    private async Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = L(TaskSchdKeys.TabGeneral),
            Content = message,
            CloseButtonText = L(TaskSchdKeys.ButtonOk),
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
        };
        await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = L(TaskSchdKeys.CommandDelete),
            Content = message,
            PrimaryButtonText = L(TaskSchdKeys.ButtonOk),
            CloseButtonText = L(TaskSchdKeys.ButtonCancel),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<string?> PromptTextAsync(string title, string label, string initial, bool multiline = false)
    {
        var box = new TextBox
        {
            Header = label,
            Text = initial,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinWidth = 360,
            Height = multiline ? 160 : double.NaN,
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = L(TaskSchdKeys.ButtonOk),
            CloseButtonText = L(TaskSchdKeys.ButtonCancel),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    // ----- Helpers -----

    private static (string folderPath, string name) SplitPath(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('\\');
        var index = trimmed.LastIndexOf('\\');
        return index <= 0 ? ("\\", trimmed.TrimStart('\\')) : (trimmed[..index], trimmed[(index + 1)..]);
    }

    private static int CompatibilityToIndex(TaskCompatibility c) => c switch
    {
        TaskCompatibility.V2 => 0,
        TaskCompatibility.V2_1 => 1,
        TaskCompatibility.V2_2 => 2,
        TaskCompatibility.V2_3 => 5,
        _ => 5,
    };

    private static TaskCompatibility IndexToCompatibility(int index) => index switch
    {
        0 => TaskCompatibility.V2,
        1 => TaskCompatibility.V2_1,
        2 => TaskCompatibility.V2_2,
        _ => TaskCompatibility.V2_3,
    };

    private static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text is "Do not wait" or "Immediately")
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

    private static string? FormatDuration(TimeSpan? span)
    {
        if (span is not { } v || v <= TimeSpan.Zero)
        {
            return null;
        }
        if (v.TotalDays >= 1 && v.TotalDays == Math.Floor(v.TotalDays))
        {
            return $"{(int)v.TotalDays} day{(v.TotalDays > 1 ? "s" : string.Empty)}";
        }
        if (v.TotalHours >= 1 && v.TotalHours == Math.Floor(v.TotalHours))
        {
            return $"{(int)v.TotalHours} hour{(v.TotalHours > 1 ? "s" : string.Empty)}";
        }
        return $"{(int)v.TotalMinutes} minute{(v.TotalMinutes > 1 ? "s" : string.Empty)}";
    }
}

/// <summary>A bindable trigger row in the TaskTriggers list.</summary>
public sealed partial class TriggerRowItem
{
    public TriggerModel Model { get; }

    public string TypeName { get; }

    public string Summary { get; }

    public TriggerRowItem(TriggerModel model)
    {
        Model = model;
        TypeName = TaskScheduleDescriptions.TriggerTypeName(model);
        Summary = TaskScheduleDescriptions.TriggerSummary(model);
    }
}

/// <summary>A bindable action row in the Actions list.</summary>
public sealed partial class ActionRowItem : ObservableObject
{
    [ObservableProperty]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    public partial bool CanMoveDown { get; set; }

    public ActionModel Model { get; }

    public string TypeName { get; }

    public string Summary { get; }

    public ActionRowItem(ActionModel model)
    {
        Model = model;
        TypeName = TaskScheduleDescriptions.ActionTypeName(model);
        Summary = TaskScheduleDescriptions.ActionSummary(model);
    }
}

/// <summary>A bindable history row.</summary>
public sealed class HistoryRowItem
{
    public string Title { get; }

    public string Detail { get; }

    public HistoryRowItem(string title, string detail)
    {
        Title = title;
        Detail = detail;
    }
}
