using OneMMC.Core.Features.PCManagement.Models.TaskSchd;

namespace OneMMC.Core.Features.PCManagement.ViewModels.TaskSchd;

/// <summary>The result produced by the Create Task dialog when committed.</summary>
/// <param name="TaskName">The name to register the task under.</param>
/// <param name="Definition">The fully built task definition.</param>
/// <param name="Password">The account password when the principal uses a stored-password logon; otherwise <see langword="null"/>.</param>
public sealed record CreateTaskResult(string TaskName, TaskDefinitionModel Definition, string? Password);
