using System.Collections.Generic;

namespace ManagementTools.Core.Features.PCManagement.Models.TaskSchd;

/// <summary>
/// A complete, UI-facing task definition: registration info, principal, settings, triggers and
/// actions. Maps to/from the Task Scheduler XML (mirrors <c>ITaskDefinition</c>). The view layer
/// edits this model; the service serializes it to XML for registration.
/// </summary>
public sealed class TaskDefinitionModel
{
    /// <summary>Registration metadata (author, description, version, date, …).</summary>
    public RegistrationInfoModel RegistrationInfo { get; set; } = new();

    /// <summary>The security context the task runs in.</summary>
    public PrincipalModel Principal { get; set; } = new();

    /// <summary>The settings and conditions that control how the task runs.</summary>
    public TaskSettingsModel Settings { get; set; } = new();

    /// <summary>The triggers that start the task (max 48).</summary>
    public IList<TriggerModel> Triggers { get; set; } = new List<TriggerModel>();

    /// <summary>The actions performed when the task runs (max 32).</summary>
    public IList<ActionModel> Actions { get; set; } = new List<ActionModel>();

    /// <summary>
    /// The original XML the definition was parsed from, when read from an existing task. Preserved so
    /// round-trips and exports retain any elements the editor does not surface.
    /// </summary>
    public string? RawXml { get; set; }

    /// <summary>
    /// The Task Scheduler schema version (the <c>&lt;Task version&gt;</c> attribute), captured when the
    /// definition is parsed. Re-serialization never emits a version below this or below the minimum
    /// required by the features in use, so an edited task keeps its capabilities. <see langword="null"/>
    /// for brand-new tasks, which get a modern default.
    /// </summary>
    public string? SchemaVersion { get; set; }
}
