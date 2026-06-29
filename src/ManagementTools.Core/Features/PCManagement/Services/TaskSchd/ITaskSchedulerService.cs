using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ManagementTools.Core.Features.PCManagement.Models.TaskSchd;

namespace ManagementTools.Core.Features.PCManagement.Services.TaskSchd;

/// <summary>
/// Abstraction over the native Task Scheduler 2.0 service. Provides folder/task enumeration and the
/// full task lifecycle (create, update, delete, enable, run, stop, import/export, security) against
/// the local machine or a remote computer.
/// </summary>
/// <remarks>
/// All operations are marshalled onto a dedicated STA thread by the implementation; callers may
/// invoke them from any thread. Machine-level or other-users' tasks require elevation — permission
/// failures surface as <see cref="System.Runtime.InteropServices.COMException"/>/
/// <see cref="System.UnauthorizedAccessException"/> for the caller to detect via
/// <c>IAdminService.IsPermissionError</c>.
/// </remarks>
public interface ITaskSchedulerService
{
    /// <summary>The connection the service currently targets (local by default).</summary>
    TaskSchedulerConnection CurrentConnection { get; }

    /// <summary>
    /// Connects the service to the given target (local or remote) and verifies the connection.
    /// Subsequent calls operate against this target until <see cref="ConnectAsync"/> is called again.
    /// </summary>
    Task ConnectAsync(TaskSchedulerConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Builds the full task-folder tree rooted at the Task Scheduler Library (<c>\</c>).</summary>
    Task<TaskFolderNode> GetFolderTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>Enumerates the tasks registered directly in the given folder.</summary>
    Task<IReadOnlyList<TaskInfo>> GetTasksAsync(string folderPath, bool includeHidden = true, CancellationToken cancellationToken = default);

    /// <summary>Loads the lightweight status summary for a single task.</summary>
    Task<TaskInfo> GetTaskInfoAsync(string taskPath, CancellationToken cancellationToken = default);

    /// <summary>Loads and parses the full editable definition of a task.</summary>
    Task<TaskDefinitionModel> GetTaskDefinitionAsync(string taskPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers (creates or updates) a task from the given definition in the specified folder.
    /// Uses the definition's <see cref="PrincipalModel"/> for the logon type and credentials.
    /// </summary>
    /// <param name="password">The account password when the principal uses a stored-password logon; otherwise <see langword="null"/>.</param>
    Task RegisterTaskAsync(string folderPath, string taskName, TaskDefinitionModel definition, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>Imports a task from raw Task Scheduler XML and registers it, returning the parsed definition.</summary>
    Task<TaskDefinitionModel> ImportTaskAsync(string folderPath, string taskName, string xml, CancellationToken cancellationToken = default);

    /// <summary>Returns the Task Scheduler XML for a registered task (for the Export command).</summary>
    Task<string> ExportTaskAsync(string taskPath, CancellationToken cancellationToken = default);

    /// <summary>Deletes a registered task.</summary>
    Task DeleteTaskAsync(string taskPath, CancellationToken cancellationToken = default);

    /// <summary>Enables or disables a registered task.</summary>
    Task SetTaskEnabledAsync(string taskPath, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Runs a task immediately (on demand).</summary>
    Task RunTaskAsync(string taskPath, CancellationToken cancellationToken = default);

    /// <summary>Stops all running instances of a task.</summary>
    Task StopTaskAsync(string taskPath, CancellationToken cancellationToken = default);

    /// <summary>Returns the number of currently running instances of a task.</summary>
    Task<int> GetRunningInstanceCountAsync(string taskPath, CancellationToken cancellationToken = default);

    /// <summary>Creates a new subfolder under the given parent folder.</summary>
    Task CreateFolderAsync(string parentFolderPath, string folderName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a (empty) task folder.</summary>
    Task DeleteFolderAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>Reads a task's security descriptor as an SDDL string for the given security-information bits.</summary>
    Task<string?> GetTaskSecurityDescriptorAsync(string taskPath, int securityInformation, CancellationToken cancellationToken = default);

    /// <summary>Applies an SDDL security descriptor to a task.</summary>
    Task SetTaskSecurityDescriptorAsync(string taskPath, string sddl, CancellationToken cancellationToken = default);

    /// <summary>Serializes a definition to Task Scheduler XML (used by dialogs for preview/validation and by export).</summary>
    string SerializeToXml(TaskDefinitionModel definition);

    /// <summary>Parses Task Scheduler XML into an editable definition (used by import and the editor).</summary>
    TaskDefinitionModel ParseXml(string xml);
}
