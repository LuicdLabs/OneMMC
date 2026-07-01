using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Models.TaskSchd;
using OneMMC.Core.Features.PCManagement.Services.EventViewer;
using OneMMC.Core.Features.PCManagement.Services.TaskSchd;
using OneMMC.Core.Features.PCManagement.ViewModels.TaskSchd;
using OneMMC.Core.Localization;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace OneMMC.Views.PCManagement;

/// <summary>
/// Main Task Scheduler screen (taskschd.msc replacement), bound to <see cref="TaskSchedulerViewModel"/>.
/// </summary>
public sealed partial class TaskSchedulerPage : Page
{
    public TaskSchedulerViewModel ViewModel { get; } = App.GetRequiredService<TaskSchedulerViewModel>();
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly IFileDialogService _fileDialog = App.GetRequiredService<IFileDialogService>();

    // Guards the programmatic root-node selection (in RebuildTree) so it does not re-enter the
    // SelectionChanged handler and trigger a redundant folder load.
    private bool _suppressTreeSelection;

    public TaskSchedulerPage()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.RootFolders.CollectionChanged += OnRootFoldersChanged;
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
        RebuildTree();
        await RefreshHistoryMenuLabelAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.RootFolders.CollectionChanged -= OnRootFoldersChanged;
    }

    // The view model owns the folder data; whenever it rebuilds the tree (load, folder create/delete,
    // connect), mirror it into the TreeView's explicit node hierarchy.
    private void OnRootFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTree();

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

    /// <summary>Rebuilds the explicit TreeView node hierarchy from the view model's folder tree.</summary>
    private void RebuildTree()
    {
        LibraryTreeView.RootNodes.Clear();
        foreach (var folder in ViewModel.RootFolders)
        {
            LibraryTreeView.RootNodes.Add(BuildTreeNode(folder));
        }

        // Show the root as selected so it matches the task list the view model loads on startup,
        // without re-entering SelectionChanged (which would reload the same folder).
        if (LibraryTreeView.RootNodes.Count > 0)
        {
            _suppressTreeSelection = true;
            LibraryTreeView.SelectedNode = LibraryTreeView.RootNodes[0];
            _suppressTreeSelection = false;
        }
    }

    private static TreeViewNode BuildTreeNode(TaskFolderItem folder)
    {
        var node = new TreeViewNode { Content = folder, IsExpanded = folder.IsRoot };
        foreach (var child in folder.Children)
        {
            node.Children.Add(BuildTreeNode(child));
        }
        return node;
    }

    private async void LibraryTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_suppressTreeSelection || args.AddedItems.Count == 0)
        {
            return;
        }
        if (ResolveFolder(args.AddedItems[0]) is { } folder)
        {
            await SelectFolderAsync(folder);
        }
    }

    private async void LibraryTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (ResolveFolder(args.InvokedItem) is { } folder)
        {
            await SelectFolderAsync(folder);
        }
    }

    // Selection (AddedItems) and invocation (InvokedItem) yield a TreeViewNode for explicit-node
    // trees; tolerate a bare data item as well so the handler is robust to either binding mode.
    private static TaskFolderItem? ResolveFolder(object? item) => item switch
    {
        TreeViewNode { Content: TaskFolderItem folder } => folder,
        TaskFolderItem folder => folder,
        _ => null,
    };

    private async Task SelectFolderAsync(TaskFolderItem folder)
    {
        if (ReferenceEquals(ViewModel.SelectedFolder, folder))
        {
            return;
        }
        await ViewModel.SelectFolderAsync(folder);
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

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFolder is not { IsRoot: false } folder)
        {
            return;
        }
        if (await ConfirmAsync(string.Format(L(TaskSchdKeys.ConfirmDeleteFolderFormat), folder.Name)))
        {
            await ViewModel.DeleteFolderCommand.ExecuteAsync(null);
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
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
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
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }
}
