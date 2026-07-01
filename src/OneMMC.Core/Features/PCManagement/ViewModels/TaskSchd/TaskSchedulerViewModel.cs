using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.PCManagement.Models.TaskSchd;
using OneMMC.Core.Features.PCManagement.Services.TaskSchd;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.PCManagement.ViewModels.TaskSchd;

/// <summary>
/// View model for the main Task Scheduler screen: the folder tree, the task list for the selected
/// folder, and the folder/task commands. Backed by <see cref="ITaskSchedulerService"/>.
/// </summary>
public sealed partial class TaskSchedulerViewModel : ObservableObject
{
    private readonly ITaskSchedulerService _service;
    private readonly IAdminService _adminService;
    private readonly ILogger<TaskSchedulerViewModel> _logger;

    /// <summary>The root folder(s) of the tree (a single Task Scheduler Library node).</summary>
    public ObservableCollection<TaskFolderItem> RootFolders { get; } = [];

    /// <summary>The tasks registered in the currently selected folder (filtered by <see cref="_filterText"/>).</summary>
    public ObservableCollection<TaskListItem> Tasks { get; } = [];

    private readonly List<TaskInfo> _allTasks = [];
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderSelected))]
    [NotifyPropertyChangedFor(nameof(IsNonRootFolderSelected))]
    private TaskFolderItem? _selectedFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTask))]
    [NotifyPropertyChangedFor(nameof(IsSelectedTaskEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSelectedTaskRunning))]
    private TaskListItem? _selectedTask;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string _connectionDisplayName = string.Empty;

    /// <summary>Raised when an operation fails because the user lacks administrator privileges.</summary>
    public event Action? AdminPermissionRequired;

    public TaskSchedulerViewModel(ITaskSchedulerService service, IAdminService adminService, ILogger<TaskSchedulerViewModel> logger)
    {
        _service = service;
        _adminService = adminService;
        _logger = logger;
        ConnectionDisplayName = Localize(TaskSchdKeys.ConnectLocalComputer);
    }

    public bool IsFolderSelected => SelectedFolder is not null;

    public bool IsNonRootFolderSelected => SelectedFolder is { IsRoot: false };

    public bool HasSelectedTask => SelectedTask is not null;

    public bool IsSelectedTaskEnabled => SelectedTask?.Enabled ?? false;

    public bool IsSelectedTaskRunning => SelectedTask?.State == TaskState.Running;

    /// <summary>Loads the folder tree and the tasks for the root folder.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunGuardedAsync(async () =>
        {
            var root = await _service.GetFolderTreeAsync();
            RootFolders.Clear();
            var rootItem = TaskFolderItem.FromModel(root, isRootLabel: true);
            RootFolders.Add(rootItem);
            SelectedFolder = rootItem;
            await LoadTasksForSelectedFolderAsync();
        });
    }

    /// <summary>Selects a folder and loads its tasks.</summary>
    public async Task SelectFolderAsync(TaskFolderItem? folder)
    {
        SelectedFolder = folder;
        await LoadTasksForSelectedFolderAsync();
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadTasksForSelectedFolderAsync();

    private async Task LoadTasksForSelectedFolderAsync()
    {
        if (SelectedFolder is null)
        {
            return;
        }

        await RunGuardedAsync(async () =>
        {
            var tasks = await _service.GetTasksAsync(SelectedFolder.Path);
            _allTasks.Clear();
            _allTasks.AddRange(tasks.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase));
            RebuildTaskList();
        });
    }

    /// <summary>Filters the task list by a name substring (the search box).</summary>
    public void ApplyFilter(string? filterText)
    {
        _filterText = filterText?.Trim() ?? string.Empty;
        RebuildTaskList();
    }

    private void RebuildTaskList()
    {
        Tasks.Clear();
        foreach (var task in _allTasks)
        {
            if (_filterText.Length == 0 || task.Name.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase))
            {
                Tasks.Add(new TaskListItem(task));
            }
        }
        SelectedTask = null;
    }

    [RelayCommand]
    public async Task RunTaskAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _service.RunTaskAsync(SelectedTask.Path);
            await RefreshSelectedTaskAsync();
        });
    }

    [RelayCommand]
    public async Task StopTaskAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _service.StopTaskAsync(SelectedTask.Path);
            await RefreshSelectedTaskAsync();
        });
    }

    [RelayCommand]
    public async Task EnableTaskAsync()
    {
        await SetSelectedTaskEnabledAsync(true);
    }

    [RelayCommand]
    public async Task DisableTaskAsync()
    {
        await SetSelectedTaskEnabledAsync(false);
    }

    private async Task SetSelectedTaskEnabledAsync(bool enabled)
    {
        if (SelectedTask is null)
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _service.SetTaskEnabledAsync(SelectedTask.Path, enabled);
            await RefreshSelectedTaskAsync();
        });
    }

    [RelayCommand]
    public async Task DeleteTaskAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _service.DeleteTaskAsync(SelectedTask.Path);
            Tasks.Remove(SelectedTask);
            SelectedTask = null;
        });
    }

    /// <summary>Returns the XML of the selected task for the Export command (the View saves the file).</summary>
    public async Task<string?> ExportSelectedTaskXmlAsync()
    {
        if (SelectedTask is null)
        {
            return null;
        }

        string? xml = null;
        await RunGuardedAsync(async () => xml = await _service.ExportTaskAsync(SelectedTask.Path));
        return xml;
    }

    /// <summary>Imports a task from XML into the selected folder (the View supplies file + name).</summary>
    public async Task ImportTaskAsync(string taskName, string xml)
    {
        var folder = SelectedFolder?.Path ?? "\\";
        await RunGuardedAsync(async () =>
        {
            await _service.ImportTaskAsync(folder, taskName, xml);
            await LoadTasksForSelectedFolderAsync();
        });
    }

    /// <summary>Registers (creates or updates) a task built by the Create Task dialog.</summary>
    public async Task CreateTaskAsync(string taskName, TaskDefinitionModel definition, string? password = null)
    {
        var folder = SelectedFolder?.Path ?? "\\";
        await RunGuardedAsync(async () =>
        {
            await _service.RegisterTaskAsync(folder, taskName, definition, password);
            await LoadTasksForSelectedFolderAsync();
        });
    }

    public async Task CreateFolderAsync(string folderName)
    {
        if (SelectedFolder is null || string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _service.CreateFolderAsync(SelectedFolder.Path, folderName);
            await LoadAsync();
        });
    }

    [RelayCommand]
    public async Task DeleteFolderAsync()
    {
        if (SelectedFolder is null || SelectedFolder.IsRoot)
        {
            return;
        }
        await RunGuardedAsync(async () =>
        {
            await _service.DeleteFolderAsync(SelectedFolder.Path);
            await LoadAsync();
        });
    }

    /// <summary>Connects to the local machine or a remote computer and reloads.</summary>
    public async Task ConnectAsync(TaskSchedulerConnection connection)
    {
        await RunGuardedAsync(async () =>
        {
            await _service.ConnectAsync(connection);
            ConnectionDisplayName = connection.IsRemote
                ? connection.Server!
                : Localize(TaskSchdKeys.ConnectLocalComputer);
            await LoadAsync();
        });
    }

    private async Task RefreshSelectedTaskAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var info = await _service.GetTaskInfoAsync(SelectedTask.Path);
        SelectedTask.Update(info);
        OnPropertyChanged(nameof(IsSelectedTaskEnabled));
        OnPropertyChanged(nameof(IsSelectedTaskRunning));
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            LastError = null;
            await action();
        }
        catch (Exception ex) when (_adminService.IsPermissionError(ex))
        {
            _logger.LogWarning(ex, "[TaskSchedulerViewModel] Operation denied (requires elevation).");
            LastError = Localize(TaskSchdKeys.ErrorAccessDenied);
            AdminPermissionRequired?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TaskSchedulerViewModel] Operation failed.");
            LastError = TaskSchedulerErrorFormatter.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Localize(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    private static string LocalizeFormat(string key, params object[] args) =>
        LocalizationProvider.Current.GetFormattedString(ResourceFileNames.TaskSchd, key, args);
}

/// <summary>A bindable folder node in the Task Scheduler tree.</summary>
public sealed partial class TaskFolderItem : ObservableObject
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public bool IsRoot { get; init; }

    public ObservableCollection<TaskFolderItem> Children { get; } = [];

    public static TaskFolderItem FromModel(TaskFolderNode node, bool isRootLabel = false)
    {
        var item = new TaskFolderItem
        {
            Name = isRootLabel || node.IsRoot
                ? LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, TaskSchdKeys.LibraryRoot)
                : node.Name,
            Path = node.Path,
            IsRoot = node.IsRoot,
        };
        foreach (var child in node.Children.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            item.Children.Add(FromModel(child));
        }
        return item;
    }
}

/// <summary>A bindable row in the task list.</summary>
public sealed partial class TaskListItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _statusLine = string.Empty;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private TaskState _state;

    public string Path { get; private set; } = string.Empty;

    public TaskListItem(TaskInfo info)
    {
        Update(info);
    }

    public void Update(TaskInfo info)
    {
        Name = info.Name;
        Path = info.Path;
        Enabled = info.Enabled;
        State = info.State;
        StatusLine = BuildStatusLine(info);
    }

    private static string BuildStatusLine(TaskInfo info)
    {
        var stateKey = info.State switch
        {
            TaskState.Ready => TaskSchdKeys.StateReady,
            TaskState.Running => TaskSchdKeys.StateRunning,
            TaskState.Disabled => TaskSchdKeys.StateDisabled,
            TaskState.Queued => TaskSchdKeys.StateQueued,
            _ => TaskSchdKeys.StateUnknown,
        };
        var stateText = LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, stateKey);
        var nextRun = info.NextRunTime is { } n
            ? n.ToString("g")
            : LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, TaskSchdKeys.LastRunNever);
        return LocalizationProvider.Current.GetFormattedString(ResourceFileNames.TaskSchd, TaskSchdKeys.StatusLineFormat, stateText, nextRun);
    }
}
