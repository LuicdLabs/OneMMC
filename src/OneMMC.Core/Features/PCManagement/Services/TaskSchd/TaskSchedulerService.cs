using System;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Infrastructure.Interop;
using System.Threading;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Models.TaskSchd;
using OneMMC.Core.Features.PCManagement.Services.TaskSchd.Native;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.PCManagement.Services.TaskSchd;

/// <summary>
/// Native Task Scheduler 2.0 implementation of <see cref="ITaskSchedulerService"/>. All COM work runs
/// on a single STA thread (<see cref="StaComExecutor"/>); the connected <see cref="ITaskService"/> is
/// cached per connection target. Tasks are written via <c>RegisterTask(xml)</c> and read via
/// <c>IRegisteredTask.Xml</c>, with <see cref="TaskXmlMapper"/> doing the model&lt;-&gt;XML mapping.
/// </summary>
public sealed partial class TaskSchedulerService : ITaskSchedulerService, IDisposable
{
    private readonly ILogger<TaskSchedulerService> _logger;
    private readonly StaComExecutor _executor = new("TaskScheduler COM");

    // The following fields are only ever touched on the STA thread.
    private ITaskService? _service;
    private TaskSchedulerConnection _connection = TaskSchedulerConnection.Local;

    public TaskSchedulerService(ILogger<TaskSchedulerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public TaskSchedulerConnection CurrentConnection => _connection;

    /// <inheritdoc />
    public Task ConnectAsync(TaskSchedulerConnection connection, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() =>
        {
            ReleaseService();
            _connection = connection ?? TaskSchedulerConnection.Local;
            _service = Connect(_connection);
        });

    /// <inheritdoc />
    public Task<TaskFolderNode> GetFolderTreeAsync(CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() =>
        {
            var service = GetService();
            service.GetFolder("\\", out var root);
            try
            {
                return BuildFolderNode(root);
            }
            finally
            {
                TaskSchedulerCom.Release(root);
            }
        });

    /// <inheritdoc />
    public Task<IReadOnlyList<TaskInfo>> GetTasksAsync(string folderPath, bool includeHidden = true, CancellationToken cancellationToken = default) =>
        _executor.RunAsync<IReadOnlyList<TaskInfo>>(() =>
        {
            var service = GetService();
            service.GetFolder(NormalizeFolder(folderPath), out var folder);
            IRegisteredTaskCollection? tasks = null;
            try
            {
                folder.GetTasks(includeHidden ? TaskSchedulerCom.TaskEnumHidden : TaskSchedulerCom.NoFlags, out tasks);
                int count = tasks.get_Count();
                var result = new List<TaskInfo>(count);
                for (int i = 1; i <= count; i++)
                {
                    using var index = Variant.FromInt32(i);
                    tasks.get_Item(index, out var task);
                    try
                    {
                        result.Add(ToTaskInfo(task, folderPath));
                    }
                    finally
                    {
                        TaskSchedulerCom.Release(task);
                    }
                }
                return result;
            }
            finally
            {
                TaskSchedulerCom.Release(tasks);
                TaskSchedulerCom.Release(folder);
            }
        });

    /// <inheritdoc />
    public Task<TaskInfo> GetTaskInfoAsync(string taskPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, folderPath) => ToTaskInfo(task, folderPath, includeAuthor: true)));

    /// <inheritdoc />
    public Task<TaskDefinitionModel> GetTaskDefinitionAsync(string taskPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) => TaskXmlMapper.Parse(task.get_Xml())));

    /// <inheritdoc />
    public Task RegisterTaskAsync(string folderPath, string taskName, TaskDefinitionModel definition, string? password = null, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() =>
        {
            var xml = TaskXmlMapper.Serialize(definition);
            RegisterXml(folderPath, taskName, xml, definition.Principal, password);
        });

    /// <inheritdoc />
    public Task<TaskDefinitionModel> ImportTaskAsync(string folderPath, string taskName, string xml, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() =>
        {
            var definition = TaskXmlMapper.Parse(xml); // validates the XML and surfaces the principal
            RegisterXml(folderPath, taskName, xml, definition.Principal, password: null);
            return definition;
        });

    /// <inheritdoc />
    public Task<string> ExportTaskAsync(string taskPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) => task.get_Xml()));

    /// <inheritdoc />
    public Task DeleteTaskAsync(string taskPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() =>
        {
            var (folderPath, name) = SplitPath(taskPath);
            var service = GetService();
            service.GetFolder(NormalizeFolder(folderPath), out var folder);
            try
            {
                folder.DeleteTask(name, TaskSchedulerCom.NoFlags);
            }
            finally
            {
                TaskSchedulerCom.Release(folder);
            }
        });

    /// <inheritdoc />
    public Task SetTaskEnabledAsync(string taskPath, bool enabled, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) =>
        {
            task.put_Enabled(TaskSchedulerCom.ToVariantBool(enabled));
            return (object?)null;
        }));

    /// <inheritdoc />
    public Task RunTaskAsync(string taskPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) =>
        {
            task.Run(TaskSchedulerCom.EmptyVariant, out var running);
            TaskSchedulerCom.Release(running);
            return (object?)null;
        }));

    /// <inheritdoc />
    public Task StopTaskAsync(string taskPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) =>
        {
            task.Stop(TaskSchedulerCom.NoFlags);
            return (object?)null;
        }));

    /// <inheritdoc />
    public Task<int> GetRunningInstanceCountAsync(string taskPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) =>
        {
            task.GetInstances(TaskSchedulerCom.NoFlags, out var running);
            try
            {
                return running.get_Count();
            }
            finally
            {
                TaskSchedulerCom.Release(running);
            }
        }));

    /// <inheritdoc />
    public Task CreateFolderAsync(string parentFolderPath, string folderName, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() =>
        {
            var service = GetService();
            service.GetFolder(NormalizeFolder(parentFolderPath), out var parent);
            ITaskFolder? created = null;
            try
            {
                parent.CreateFolder(folderName, TaskSchedulerCom.MissingVariant, out created);
            }
            finally
            {
                TaskSchedulerCom.Release(created);
                TaskSchedulerCom.Release(parent);
            }
        });

    /// <inheritdoc />
    public Task DeleteFolderAsync(string folderPath, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() =>
        {
            var (parentPath, name) = SplitPath(folderPath);
            var service = GetService();
            service.GetFolder(NormalizeFolder(parentPath), out var parent);
            try
            {
                // ITaskFolder::DeleteFolder returns ERROR_DIR_NOT_EMPTY (0x80070091) for a folder that
                // still contains tasks or subfolders, so recursively empty it first (matching MMC).
                parent.GetFolder(name, out var target);
                try
                {
                    EmptyFolder(target);
                }
                finally
                {
                    TaskSchedulerCom.Release(target);
                }
                parent.DeleteFolder(name, TaskSchedulerCom.NoFlags);
            }
            finally
            {
                TaskSchedulerCom.Release(parent);
            }
        });

    /// <inheritdoc />
    public Task<string?> GetTaskSecurityDescriptorAsync(string taskPath, int securityInformation, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) =>
        {
            task.GetSecurityDescriptor(securityInformation, out var sddl);
            return (string?)sddl;
        }));

    /// <inheritdoc />
    public Task SetTaskSecurityDescriptorAsync(string taskPath, string sddl, CancellationToken cancellationToken = default) =>
        _executor.RunAsync(() => WithTask(taskPath, (task, _) =>
        {
            task.SetSecurityDescriptor(sddl, TaskSchedulerCom.NoFlags);
            return (object?)null;
        }));

    /// <inheritdoc />
    public string SerializeToXml(TaskDefinitionModel definition) => TaskXmlMapper.Serialize(definition);

    /// <inheritdoc />
    public TaskDefinitionModel ParseXml(string xml) => TaskXmlMapper.Parse(xml);

    // ----- STA-thread helpers (private; always invoked inside _executor) -----

    private ITaskService GetService() => _service ??= Connect(_connection);

    private ITaskService Connect(TaskSchedulerConnection connection)
    {
        var service = TaskSchedulerCom.CreateTaskService();
        try
        {
            // Each VT_BSTR variant owns a BSTR we must free after the call; the Missing sentinel is a
            // non-allocating shared value, so disposing its copy here is a harmless no-op.
            using var server = connection.IsRemote
                ? Variant.FromString(connection.Server!)
                : TaskSchedulerCom.MissingVariant;
            using var user = TaskSchedulerCom.OptionalBstr(connection.User);
            using var domain = TaskSchedulerCom.OptionalBstr(connection.Domain);
            using var password = TaskSchedulerCom.OptionalBstr(connection.Password);
            service.Connect(server, user, domain, password);
            if (!TaskSchedulerCom.ToBool(service.get_Connected()))
            {
                throw new InvalidOperationException("The Task Scheduler service did not report a connected state.");
            }

            return service;
        }
        catch
        {
            TaskSchedulerCom.Release(service);
            throw;
        }
    }

    private void ReleaseService()
    {
        TaskSchedulerCom.Release(_service);
        _service = null;
    }

    /// <summary>Resolves a task by full path, runs <paramref name="body"/>, and releases COM objects.</summary>
    private T WithTask<T>(string taskPath, Func<IRegisteredTask, string, T> body)
    {
        var (folderPath, name) = SplitPath(taskPath);
        var service = GetService();
        service.GetFolder(NormalizeFolder(folderPath), out var folder);
        IRegisteredTask? task = null;
        try
        {
            folder.GetTask(name, out task);
            return body(task, folderPath);
        }
        finally
        {
            TaskSchedulerCom.Release(task);
            TaskSchedulerCom.Release(folder);
        }
    }

    private void RegisterXml(string folderPath, string taskName, string xml, PrincipalModel principal, string? password)
    {
        var service = GetService();
        service.GetFolder(NormalizeFolder(folderPath), out var folder);
        IRegisteredTask? registered = null;
        // VT_BSTR variants own BSTRs freed in the finally; Missing-sentinel copies dispose as no-ops.
        Variant userId = TaskSchedulerCom.MissingVariant;
        Variant pwd = TaskSchedulerCom.MissingVariant;
        try
        {
            switch (principal.LogonType)
            {
                case TaskLogonType.Password:
                case TaskLogonType.InteractiveTokenOrPassword:
                    if (!string.IsNullOrEmpty(principal.UserId))
                    {
                        userId = Variant.FromString(principal.UserId);
                    }
                    if (!string.IsNullOrEmpty(password))
                    {
                        pwd = Variant.FromString(password);
                    }
                    break;
                case TaskLogonType.S4U:
                    if (!string.IsNullOrEmpty(principal.UserId))
                    {
                        userId = Variant.FromString(principal.UserId);
                    }
                    break;
            }

            folder.RegisterTask(
                taskName,
                xml,
                TaskSchedulerCom.TaskCreateOrUpdate,
                userId,
                pwd,
                (int)principal.LogonType,
                TaskSchedulerCom.MissingVariant,
                out registered);
        }
        finally
        {
            userId.Dispose();
            pwd.Dispose();
            TaskSchedulerCom.Release(registered);
            TaskSchedulerCom.Release(folder);
        }
    }

    /// <summary>Recursively deletes every task and subfolder inside <paramref name="folder"/>, leaving it empty.</summary>
    /// <remarks>Always invoked on the STA thread. The COM collections returned by GetFolders/GetTasks are
    /// snapshots, so deleting by name through the live <paramref name="folder"/> while iterating is safe.</remarks>
    private static void EmptyFolder(ITaskFolder folder)
    {
        // Subfolders first (depth-first): each must be emptied before it can be deleted.
        folder.GetFolders(TaskSchedulerCom.NoFlags, out var subFolders);
        try
        {
            int subCount = subFolders.get_Count();
            for (int i = 1; i <= subCount; i++)
            {
                using var index = Variant.FromInt32(i);
                subFolders.get_Item(index, out var child);
                try
                {
                    var childName = child.get_Name();
                    EmptyFolder(child);
                    folder.DeleteFolder(childName, TaskSchedulerCom.NoFlags);
                }
                finally
                {
                    TaskSchedulerCom.Release(child);
                }
            }
        }
        finally
        {
            TaskSchedulerCom.Release(subFolders);
        }

        // Then the tasks (include hidden, otherwise the folder would still report as non-empty).
        folder.GetTasks(TaskSchedulerCom.TaskEnumHidden, out var tasks);
        try
        {
            int taskCount = tasks.get_Count();
            for (int i = 1; i <= taskCount; i++)
            {
                using var index = Variant.FromInt32(i);
                tasks.get_Item(index, out var task);
                try
                {
                    folder.DeleteTask(task.get_Name(), TaskSchedulerCom.NoFlags);
                }
                finally
                {
                    TaskSchedulerCom.Release(task);
                }
            }
        }
        finally
        {
            TaskSchedulerCom.Release(tasks);
        }
    }

    private TaskFolderNode BuildFolderNode(ITaskFolder folder)
    {
        var node = new TaskFolderNode { Name = folder.get_Name(), Path = folder.get_Path() };
        folder.GetFolders(TaskSchedulerCom.NoFlags, out var children);
        try
        {
            int childCount = children.get_Count();
            for (int i = 1; i <= childCount; i++)
            {
                using var index = Variant.FromInt32(i);
                children.get_Item(index, out var child);
                try
                {
                    node.Children.Add(BuildFolderNode(child));
                }
                finally
                {
                    TaskSchedulerCom.Release(child);
                }
            }
        }
        finally
        {
            TaskSchedulerCom.Release(children);
        }
        return node;
    }

    private static TaskInfo ToTaskInfo(IRegisteredTask task, string folderPath, bool includeAuthor = false)
    {
        string? author = null;
        if (includeAuthor)
        {
            try
            {
                author = TaskXmlMapper.Parse(task.get_Xml()).RegistrationInfo.Author;
            }
            catch (Exception)
            {
                author = null;
            }
        }

        return new TaskInfo
        {
            Name = task.get_Name(),
            Path = task.get_Path(),
            FolderPath = folderPath,
            State = (TaskState)task.get_State(),
            Enabled = TaskSchedulerCom.ToBool(task.get_Enabled()),
            LastRunTime = TaskSchedulerCom.FromOleDate(task.get_LastRunTime()),
            LastTaskResult = task.get_LastTaskResult(),
            NextRunTime = TaskSchedulerCom.FromOleDate(task.get_NextRunTime()),
            NumberOfMissedRuns = task.get_NumberOfMissedRuns(),
            Author = author,
        };
    }

    /// <summary>Splits a full task or folder path into its parent folder path and leaf name.</summary>
    private static (string folderPath, string name) SplitPath(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('\\');
        var index = trimmed.LastIndexOf('\\');
        if (index <= 0)
        {
            return ("\\", trimmed.TrimStart('\\'));
        }
        return (trimmed[..index], trimmed[(index + 1)..]);
    }

    /// <summary>Normalizes an empty/relative folder path to the root folder marker.</summary>
    private static string NormalizeFolder(string? folderPath) =>
        string.IsNullOrEmpty(folderPath) ? "\\" : folderPath;

    public void Dispose()
    {
        try
        {
            _executor.Shutdown(ReleaseService);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[TaskSchedulerService] Failed to release the COM service during dispose.");
        }
    }
}
