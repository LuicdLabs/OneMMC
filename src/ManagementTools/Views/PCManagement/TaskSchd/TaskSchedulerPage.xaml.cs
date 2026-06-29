using System;
using System.IO;
using System.Threading.Tasks;
using ManagementTools.Core.Features.PCManagement.Models.TaskSchd;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using ManagementTools.Core.Features.PCManagement.Services.TaskSchd;
using ManagementTools.Core.Features.PCManagement.ViewModels.TaskSchd;
using ManagementTools.Core.Localization;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// Main Task Scheduler screen (taskschd.msc replacement), bound to <see cref="TaskSchedulerViewModel"/>.
/// </summary>
public sealed partial class TaskSchedulerPage : Page
{
    public TaskSchedulerViewModel ViewModel { get; } = App.GetRequiredService<TaskSchedulerViewModel>();
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly IFileDialogService _fileDialog = App.GetRequiredService<IFileDialogService>();

    public TaskSchedulerPage()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static nint OwnerHwnd => App.MainWindowInstance is null ? 0 : WindowNative.GetWindowHandle(App.MainWindowInstance);

    private string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.RootFolders.Count == 0)
        {
            await ViewModel.LoadAsync();
        }
        await RefreshHistoryMenuLabelAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void OnThemeChanged(ElementTheme theme) => this.RequestedTheme = theme;

    private async void OnAdminPermissionRequired() =>
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.SelectedTask) or nameof(ViewModel.IsSelectedTaskEnabled) or nameof(ViewModel.IsSelectedTaskRunning))
        {
            UpdateToggleButtons();
        }
    }

    private void UpdateToggleButtons()
    {
        var enabled = ViewModel.IsSelectedTaskEnabled;
        DisableEnableIcon.Glyph = enabled ? "" : "";
        DisableEnableButton.Label = enabled ? L(TaskSchdKeys.CommandDisable) : L(TaskSchdKeys.CommandEnable);

        var running = ViewModel.IsSelectedTaskRunning;
        RunEndIcon.Glyph = running ? "" : "";
        RunEndMenuItem.Text = running ? L(TaskSchdKeys.CommandEnd) : L(TaskSchdKeys.CommandRun);
    }

    private async void LibraryTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is TaskFolderItem folder)
        {
            await ViewModel.SelectFolderAsync(folder);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.ApplyFilter(sender.Text);
        }
    }

    private void TasksListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TaskListItem task)
        {
            OpenTaskProperties(task);
        }
    }

    private void PropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is { } task)
        {
            OpenTaskProperties(task);
        }
    }

    private void OpenTaskProperties(TaskListItem task)
    {
        BreadcrumbNavigationService.AddBreadcrumb(task.Name, typeof(TaskPropertiesPage), task.Path);
        Frame.Navigate(typeof(TaskPropertiesPage), task.Path, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private async void CreateTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateTaskDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is { } result)
        {
            await ViewModel.CreateTaskAsync(result.TaskName, result.Definition, result.Password);
        }
    }

    private async void DisableEnableButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsSelectedTaskEnabled)
        {
            await ViewModel.DisableTaskCommand.ExecuteAsync(null);
        }
        else
        {
            await ViewModel.EnableTaskCommand.ExecuteAsync(null);
        }
        UpdateToggleButtons();
    }

    private async void RunEndMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsSelectedTaskRunning)
        {
            await ViewModel.StopTaskCommand.ExecuteAsync(null);
        }
        else
        {
            await ViewModel.RunTaskCommand.ExecuteAsync(null);
        }
        UpdateToggleButtons();
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is not { } task)
        {
            return;
        }
        if (await ConfirmAsync(string.Format(L(TaskSchdKeys.ConfirmDeleteTaskFormat), task.Name)))
        {
            await ViewModel.DeleteTaskCommand.ExecuteAsync(null);
        }
    }

    private async void ExportTask_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is not { } task)
        {
            return;
        }

        var xml = await ViewModel.ExportSelectedTaskXmlAsync();
        if (string.IsNullOrEmpty(xml))
        {
            return;
        }

        var path = await _fileDialog.SaveFileAsync(
            OwnerHwnd,
            "XML Files\0*.xml\0All Files\0*.*\0",
            title: L(TaskSchdKeys.CommandExportTask),
            defaultExtension: ".xml",
            suggestedFileName: task.Name + ".xml");

        if (!string.IsNullOrEmpty(path))
        {
            await File.WriteAllTextAsync(path, xml);
        }
    }

    private async void ImportTask_Click(object sender, RoutedEventArgs e)
    {
        var path = await _fileDialog.OpenFileAsync(OwnerHwnd, "XML Files\0*.xml\0All Files\0*.*\0", title: L(TaskSchdKeys.CommandImportTask));
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var xml = await File.ReadAllTextAsync(path);
        var defaultName = Path.GetFileNameWithoutExtension(path);
        var name = await PromptTextAsync(L(TaskSchdKeys.CommandImportTask), L(TaskSchdKeys.GeneralName), defaultName);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await ViewModel.ImportTaskAsync(name, xml);
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = await PromptTextAsync(L(TaskSchdKeys.CommandNewFolder), L(TaskSchdKeys.NewFolderName), string.Empty);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await ViewModel.CreateFolderAsync(name);
        }
    }

    private async void ConnectComputer_Click(object sender, RoutedEventArgs e)
    {
        var server = await PromptTextAsync(L(TaskSchdKeys.DialogConnectComputer), L(TaskSchdKeys.ConnectComputerLabel), string.Empty);
        var connection = string.IsNullOrWhiteSpace(server)
            ? TaskSchedulerConnection.Local
            : new TaskSchedulerConnection { Server = server.Trim() };
        await ViewModel.ConnectAsync(connection);
    }

    private async void SecurityTask_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is not { } task)
        {
            return;
        }

        var service = App.GetRequiredService<ITaskSchedulerService>();
        try
        {
            // DACL + OWNER + GROUP
            var sddl = await service.GetTaskSecurityDescriptorAsync(task.Path, 0x1 | 0x2 | 0x4) ?? string.Empty;
            var edited = await PromptTextAsync(L(TaskSchdKeys.CommandSecurity), "SDDL", sddl, multiline: true);
            if (edited is not null && !string.Equals(edited, sddl, StringComparison.Ordinal))
            {
                await service.SetTaskSecurityDescriptorAsync(task.Path, edited);
            }
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
    }

    private async void ToggleAllHistory_Click(object sender, RoutedEventArgs e)
    {
        var history = App.GetRequiredService<TaskHistoryService>();
        try
        {
            var enabled = history.IsHistoryEnabled();
            if (!enabled && !await ConfirmAsync(L(TaskSchdKeys.HistoryEnablePrompt)))
            {
                return;
            }
            await history.SetHistoryEnabledAsync(!enabled);
            await RefreshHistoryMenuLabelAsync();
        }
        catch (Exception ex) when (App.GetRequiredService<IAdminService>().IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
    }

    private async Task RefreshHistoryMenuLabelAsync()
    {
        try
        {
            var history = App.GetRequiredService<TaskHistoryService>();
            var enabled = await Task.Run(history.IsHistoryEnabled);
            HistoryToggleMenuItem.Text = enabled ? L(TaskSchdKeys.CommandDisableAllHistory) : L(TaskSchdKeys.CommandEnableAllHistory);
        }
        catch
        {
            // Leave the default label if the channel cannot be queried.
        }
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = L(TaskSchdKeys.DialogCreateTask),
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
}
