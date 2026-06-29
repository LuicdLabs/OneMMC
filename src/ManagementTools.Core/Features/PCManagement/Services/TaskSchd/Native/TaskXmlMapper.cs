using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using ManagementTools.Core.Features.PCManagement.Models.TaskSchd;

namespace ManagementTools.Core.Features.PCManagement.Services.TaskSchd.Native;

/// <summary>
/// Maps a <see cref="TaskDefinitionModel"/> to and from the Task Scheduler XML schema
/// (<c>http://schemas.microsoft.com/windows/2004/02/mit/task</c>). The service writes tasks via
/// <c>ITaskFolder.RegisterTask(xml)</c> and reads them via <c>IRegisteredTask.Xml</c>, so this XML
/// mapping — not COM object-model traversal — carries the rich trigger/action/settings detail. The
/// same XML is what the Export/Import commands round-trip.
/// </summary>
internal static class TaskXmlMapper
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // Schema version attribute <-> compatibility level. New tasks author 1.4 (Windows 10) by default;
    // existing task versions are recomputed from the selected "Configure for" value and supported fields.
    private const string DefaultVersion = "1.4";

    private static readonly string[] DayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly string[] WeekNames = ["First", "Second", "Third", "Fourth"];

    #region Serialize (model -> XML)

    /// <summary>Serializes a definition to Task Scheduler XML text suitable for RegisterTask.</summary>
    public static string Serialize(TaskDefinitionModel def)
    {
        var version = ResolveVersion(def);
        var task = new XElement(Ns + "Task", new XAttribute("version", version));

        task.Add(SerializeRegistrationInfo(def.RegistrationInfo));

        if (def.Triggers.Count > 0)
        {
            task.Add(new XElement(Ns + "Triggers", def.Triggers.Select(SerializeTrigger)));
        }

        var principalId = string.IsNullOrEmpty(def.Principal.Id) ? "Author" : def.Principal.Id;
        task.Add(new XElement(Ns + "Principals", SerializePrincipal(def.Principal, principalId, def.Settings.Compatibility)));
        task.Add(SerializeSettings(def.Settings));
        task.Add(SerializeActions(def.Actions, principalId));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-16", null), task);
        return doc.Declaration + Environment.NewLine + task;
    }

    private static XElement SerializeRegistrationInfo(RegistrationInfoModel r)
    {
        var registrationInfo = new XElement(Ns + "RegistrationInfo");
        AddIf(registrationInfo, "Date", r.Date is { } d ? d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : null);
        AddIf(registrationInfo, "Author", r.Author);
        AddIf(registrationInfo, "Version", r.Version);
        AddIf(registrationInfo, "Description", r.Description);
        AddIf(registrationInfo, "Documentation", r.Documentation);
        AddIf(registrationInfo, "URI", r.Uri);
        AddIf(registrationInfo, "Source", r.Source);
        AddIf(registrationInfo, "SecurityDescriptor", r.SecurityDescriptorSddl);
        return registrationInfo;
    }

    private static XElement SerializePrincipal(PrincipalModel p, string id, TaskCompatibility compatibility)
    {
        var principal = new XElement(Ns + "Principal", new XAttribute("id", id));
        if (!string.IsNullOrEmpty(p.UserId))
        {
            principal.Add(new XElement(Ns + "UserId", p.UserId));
        }
        if (!string.IsNullOrEmpty(p.GroupId))
        {
            principal.Add(new XElement(Ns + "GroupId", p.GroupId));
        }
        // <DisplayName> requires schema version 1.2; the 1.1 validator rejects it. Every task this
        // editor emits is already clamped to >= 1.2 (see MinVersionForFeatures), so it is always safe.
        AddIf(principal, "DisplayName", p.DisplayName);
        if (p.LogonType != TaskLogonType.None && string.IsNullOrEmpty(p.GroupId))
        {
            principal.Add(new XElement(Ns + "LogonType", LogonTypeToXml(p.LogonType)));
        }
        // Only emit RunLevel when elevated; LeastPrivilege is the schema default and real tasks omit it
        // (group principals in particular reject an explicit LeastPrivilege RunLevel).
        if (p.RunLevel == TaskRunLevel.HighestAvailable)
        {
            principal.Add(new XElement(Ns + "RunLevel", "HighestAvailable"));
        }
        if (p.RequiredPrivileges.Count > 0 && SupportsWindows7Compatibility(compatibility))
        {
            principal.Add(new XElement(Ns + "RequiredPrivileges", p.RequiredPrivileges.Select(pr => new XElement(Ns + "Privilege", pr))));
        }
        return principal;
    }

    private static XElement SerializeSettings(TaskSettingsModel s)
    {
        var settings = new XElement(Ns + "Settings");
        settings.Add(new XElement(Ns + "MultipleInstancesPolicy", s.MultipleInstances.ToString()));
        settings.Add(new XElement(Ns + "DisallowStartIfOnBatteries", XmlBool(s.DisallowStartIfOnBatteries)));
        settings.Add(new XElement(Ns + "StopIfGoingOnBatteries", XmlBool(s.StopIfGoingOnBatteries)));
        settings.Add(new XElement(Ns + "AllowHardTerminate", XmlBool(s.AllowHardTerminate)));
        settings.Add(new XElement(Ns + "StartWhenAvailable", XmlBool(s.StartWhenAvailable)));
        settings.Add(new XElement(Ns + "RunOnlyIfNetworkAvailable", XmlBool(s.RunOnlyIfNetworkAvailable)));

        if (s.RunOnlyIfIdle || s.IdleSettings.StopOnIdleEnd || s.IdleSettings.RestartOnIdle || s.IdleSettings.IdleDuration is not null)
        {
            var idle = new XElement(Ns + "IdleSettings");
            AddIf(idle, "Duration", DurationOrNull(s.IdleSettings.IdleDuration));
            AddIf(idle, "WaitTimeout", DurationOrNull(s.IdleSettings.WaitTimeout));
            idle.Add(new XElement(Ns + "StopOnIdleEnd", XmlBool(s.IdleSettings.StopOnIdleEnd)));
            idle.Add(new XElement(Ns + "RestartOnIdle", XmlBool(s.IdleSettings.RestartOnIdle)));
            settings.Add(idle);
        }

        settings.Add(new XElement(Ns + "AllowStartOnDemand", XmlBool(s.AllowDemandStart)));
        settings.Add(new XElement(Ns + "Enabled", XmlBool(s.Enabled)));
        settings.Add(new XElement(Ns + "Hidden", XmlBool(s.Hidden)));
        settings.Add(new XElement(Ns + "RunOnlyIfIdle", XmlBool(s.RunOnlyIfIdle)));
        // DisallowStartOnRemoteAppSession and UseUnifiedSchedulingEngine are ITaskSettings2 elements that
        // the schema only accepts at version 1.3 (Windows 8 / Server 2012) and above — the 1.2 (Windows 7)
        // validator rejects them. Emit them only when the user targets Windows 8+, and bump the version
        // accordingly in MinVersionForFeatures.
        if (s.DisallowStartOnRemoteAppSession && SupportsWindows8Compatibility(s.Compatibility))
        {
            settings.Add(new XElement(Ns + "DisallowStartOnRemoteAppSession", XmlBool(true)));
        }
        if (s.UseUnifiedSchedulingEngine && SupportsWindows8Compatibility(s.Compatibility))
        {
            settings.Add(new XElement(Ns + "UseUnifiedSchedulingEngine", XmlBool(true)));
        }
        settings.Add(new XElement(Ns + "WakeToRun", XmlBool(s.WakeToRun)));
        settings.Add(new XElement(Ns + "ExecutionTimeLimit", DurationOrZero(s.ExecutionTimeLimit)));
        if (s.DeleteExpiredTaskAfter is { } del)
        {
            settings.Add(new XElement(Ns + "DeleteExpiredTaskAfter", DurationString(del)));
        }
        settings.Add(new XElement(Ns + "Priority", s.Priority.ToString(CultureInfo.InvariantCulture)));
        if (s.Volatile)
        {
            settings.Add(new XElement(Ns + "Volatile", XmlBool(true)));
        }

        if (s.RestartCount > 0 && s.RestartInterval is { } ri)
        {
            settings.Add(new XElement(Ns + "RestartOnFailure",
                new XElement(Ns + "Interval", DurationString(ri)),
                new XElement(Ns + "Count", s.RestartCount.ToString(CultureInfo.InvariantCulture))));
        }

        if (s.NetworkSettings is { } net && (net.Id is not null || !string.IsNullOrEmpty(net.Name)))
        {
            var networkSettings = new XElement(Ns + "NetworkSettings");
            AddIf(networkSettings, "Name", net.Name);
            if (net.Id is { } id)
            {
                networkSettings.Add(new XElement(Ns + "Id", id.ToString("B").ToUpperInvariant()));
            }
            settings.Add(networkSettings);
        }

        if (s.MaintenanceSettings is { } m && (m.Period is not null || m.Deadline is not null))
        {
            var maintenance = new XElement(Ns + "MaintenanceSettings");
            AddIf(maintenance, "Period", DurationOrNull(m.Period));
            AddIf(maintenance, "Deadline", DurationOrNull(m.Deadline));
            if (m.Exclusive)
            {
                maintenance.Add(new XElement(Ns + "Exclusive", XmlBool(true)));
            }
            settings.Add(maintenance);
        }

        return settings;
    }

    private static XElement SerializeActions(IEnumerable<ActionModel> actions, string context)
    {
        var actionsElement = new XElement(Ns + "Actions", new XAttribute("Context", context));
        foreach (var a in actions)
        {
            actionsElement.Add(a switch
            {
                ExecActionModel x => ExecXml(x),
                ComHandlerActionModel c => ComHandlerXml(c),
                EmailActionModel m => EmailXml(m),
                ShowMessageActionModel s => ShowMessageXml(s),
                _ => throw new NotSupportedException($"Unknown action type {a.Type}."),
            });
        }
        return actionsElement;
    }

    private static XElement ExecXml(ExecActionModel x)
    {
        var exec = new XElement(Ns + "Exec");
        if (!string.IsNullOrEmpty(x.Id))
        {
            exec.Add(new XAttribute("id", x.Id));
        }
        exec.Add(new XElement(Ns + "Command", x.Path));
        AddIf(exec, "Arguments", x.Arguments);
        AddIf(exec, "WorkingDirectory", x.WorkingDirectory);
        return exec;
    }

    private static XElement ComHandlerXml(ComHandlerActionModel c)
    {
        var comHandler = new XElement(Ns + "ComHandler", new XElement(Ns + "ClassId", c.ClassId.ToString("B").ToUpperInvariant()));
        AddIf(comHandler, "Data", c.Data);
        return comHandler;
    }

    private static XElement EmailXml(EmailActionModel m)
    {
        var sendEmail = new XElement(Ns + "SendEmail");
        AddIf(sendEmail, "Server", m.Server);
        AddIf(sendEmail, "Subject", m.Subject);
        AddIf(sendEmail, "To", m.To);
        AddIf(sendEmail, "Cc", m.Cc);
        AddIf(sendEmail, "Bcc", m.Bcc);
        AddIf(sendEmail, "ReplyTo", m.ReplyTo);
        AddIf(sendEmail, "From", m.From);
        AddIf(sendEmail, "Body", m.Body);
        if (m.Attachments.Count > 0)
        {
            sendEmail.Add(new XElement(Ns + "Attachments", m.Attachments.Select(a => new XElement(Ns + "File", a))));
        }
        if (m.Headers.Count > 0)
        {
            sendEmail.Add(new XElement(Ns + "HeaderFields", m.Headers.Select(h =>
                new XElement(Ns + "HeaderField", new XElement(Ns + "Name", h.Key), new XElement(Ns + "Value", h.Value)))));
        }
        return sendEmail;
    }

    private static XElement ShowMessageXml(ShowMessageActionModel s)
    {
        var showMessage = new XElement(Ns + "ShowMessage");
        AddIf(showMessage, "Title", s.Title);
        AddIf(showMessage, "Body", s.MessageBody);
        return showMessage;
    }

    private static XElement SerializeTrigger(TriggerModel t)
    {
        XElement triggerElement = t switch
        {
            BootTriggerModel b => new XElement(Ns + "BootTrigger", DelayElement(b.Delay)),
            LogonTriggerModel l => new XElement(Ns + "LogonTrigger", UserIdElement(l.UserId), DelayElement(l.Delay)),
            RegistrationTriggerModel r => new XElement(Ns + "RegistrationTrigger", DelayElement(r.Delay)),
            IdleTriggerModel => new XElement(Ns + "IdleTrigger"),
            EventTriggerModel ev => EventTriggerXml(ev),
            SessionStateChangeTriggerModel ss => SessionTriggerXml(ss),
            TimeTriggerModel tt => new XElement(Ns + "TimeTrigger", RandomDelayElement(tt.RandomDelay)),
            DailyTriggerModel d => CalendarTriggerXml(d, DailyScheduleXml(d)),
            WeeklyTriggerModel w => CalendarTriggerXml(w, WeeklyScheduleXml(w)),
            MonthlyTriggerModel mo => CalendarTriggerXml(mo, MonthlyScheduleXml(mo)),
            MonthlyDowTriggerModel md => CalendarTriggerXml(md, MonthlyDowScheduleXml(md)),
            _ => throw new NotSupportedException($"Unknown trigger type {t.Type}."),
        };

        // Common ITrigger elements (order per schema: id attr, Enabled, StartBoundary, EndBoundary,
        // ExecutionTimeLimit, Repetition). Prepend those that belong before the type-specific content.
        if (!string.IsNullOrEmpty(t.Id))
        {
            triggerElement.SetAttributeValue("id", t.Id);
        }
        PrependCommon(triggerElement, t);
        return triggerElement;
    }

    private static void PrependCommon(XElement triggerElement, TriggerModel t)
    {
        // Build the common children then insert them in schema order at the front (after any attributes).
        var common = new List<XElement>();
        if (t.Repetition.IsEnabled)
        {
            var repetition = new XElement(Ns + "Repetition",
                new XElement(Ns + "Interval", DurationString(t.Repetition.Interval!.Value)));
            if (t.Repetition.Duration is { } dur)
            {
                repetition.Add(new XElement(Ns + "Duration", DurationString(dur)));
            }
            repetition.Add(new XElement(Ns + "StopAtDurationEnd", XmlBool(t.Repetition.StopAtDurationEnd)));
            common.Add(repetition);
        }
        if (t.ExecutionTimeLimit is { } etl)
        {
            common.Add(new XElement(Ns + "ExecutionTimeLimit", DurationString(etl)));
        }
        if (t.StartBoundary is { } sb)
        {
            common.Add(new XElement(Ns + "StartBoundary", sb.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        }
        if (t.EndBoundary is { } eb)
        {
            common.Add(new XElement(Ns + "EndBoundary", eb.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        }
        common.Add(new XElement(Ns + "Enabled", XmlBool(t.Enabled)));
        // Insert in schema order: Enabled/StartBoundary/EndBoundary/ExecutionTimeLimit/Repetition come
        // BEFORE the type-specific schedule content, so add them at the front in reverse build order.
        for (int i = common.Count - 1; i >= 0; i--)
        {
            triggerElement.AddFirst(common[i]);
        }
    }

    private static XElement CalendarTriggerXml(TriggerModel t, XElement schedule)
    {
        var random = t switch
        {
            DailyTriggerModel d => d.RandomDelay,
            WeeklyTriggerModel w => w.RandomDelay,
            MonthlyTriggerModel m => m.RandomDelay,
            MonthlyDowTriggerModel md => md.RandomDelay,
            _ => null,
        };
        var calendar = new XElement(Ns + "CalendarTrigger");
        calendar.Add(schedule);
        if (random is { } r)
        {
            calendar.Add(new XElement(Ns + "RandomDelay", DurationString(r)));
        }
        return calendar;
    }

    private static XElement DailyScheduleXml(DailyTriggerModel d) =>
        new(Ns + "ScheduleByDay", new XElement(Ns + "DaysInterval", Math.Max((short)1, d.DaysInterval)));

    private static XElement WeeklyScheduleXml(WeeklyTriggerModel w) =>
        new(Ns + "ScheduleByWeek",
            new XElement(Ns + "DaysOfWeek", DaysOfWeekElements(w.DaysOfWeek)),
            new XElement(Ns + "WeeksInterval", Math.Max((short)1, w.WeeksInterval)));

    private static XElement MonthlyScheduleXml(MonthlyTriggerModel m)
    {
        var days = new XElement(Ns + "DaysOfMonth", m.DaysOfMonth.Where(x => x is >= 1 and <= 31).Distinct().OrderBy(x => x)
            .Select(x => new XElement(Ns + "Day", x.ToString(CultureInfo.InvariantCulture))));
        if (m.RunOnLastDayOfMonth)
        {
            days.Add(new XElement(Ns + "Day", "Last"));
        }
        var scheduleByMonth = new XElement(Ns + "ScheduleByMonth", days, new XElement(Ns + "Months", MonthElements(m.MonthsOfYear)));
        return scheduleByMonth;
    }

    private static XElement MonthlyDowScheduleXml(MonthlyDowTriggerModel md)
    {
        var weeks = new XElement(Ns + "Weeks", WeekElements(md.WeeksOfMonth, md.RunOnLastWeekOfMonth));
        return new XElement(Ns + "ScheduleByMonthDayOfWeek",
            weeks,
            new XElement(Ns + "DaysOfWeek", DaysOfWeekElements(md.DaysOfWeek)),
            new XElement(Ns + "Months", MonthElements(md.MonthsOfYear)));
    }

    private static XElement EventTriggerXml(EventTriggerModel ev)
    {
        var eventTrigger = new XElement(Ns + "EventTrigger");
        eventTrigger.Add(new XElement(Ns + "Subscription", ev.Subscription ?? string.Empty));
        if (ev.Delay is { } d)
        {
            eventTrigger.Add(new XElement(Ns + "Delay", DurationString(d)));
        }
        if (ev.ValueQueries.Count > 0)
        {
            eventTrigger.Add(new XElement(Ns + "ValueQueries", ev.ValueQueries.Select(v =>
                new XElement(Ns + "Value", new XAttribute("name", v.Key), v.Value))));
        }
        return eventTrigger;
    }

    private static XElement SessionTriggerXml(SessionStateChangeTriggerModel ss)
    {
        var session = new XElement(Ns + "SessionStateChangeTrigger");
        session.Add(UserIdElement(ss.UserId));
        session.Add(DelayElement(ss.Delay));
        session.Add(new XElement(Ns + "StateChange", ss.StateChange.ToString()));
        return session;
    }

    private static XElement? DelayElement(TimeSpan? delay) =>
        delay is { } d ? new XElement(Ns + "Delay", DurationString(d)) : null;

    private static XElement? RandomDelayElement(TimeSpan? delay) =>
        delay is { } d ? new XElement(Ns + "RandomDelay", DurationString(d)) : null;

    private static XElement? UserIdElement(string? userId) =>
        string.IsNullOrEmpty(userId) ? null : new XElement(Ns + "UserId", userId);

    private static IEnumerable<XElement> DaysOfWeekElements(TaskDaysOfWeek days)
    {
        for (int i = 0; i < DayNames.Length; i++)
        {
            if (((int)days & (1 << i)) != 0)
            {
                yield return new XElement(Ns + DayNames[i]);
            }
        }
    }

    private static IEnumerable<XElement> MonthElements(TaskMonthsOfYear months)
    {
        for (int i = 0; i < MonthNames.Length; i++)
        {
            if (((int)months & (1 << i)) != 0)
            {
                yield return new XElement(Ns + MonthNames[i]);
            }
        }
    }

    private static IEnumerable<XElement> WeekElements(TaskWeeksOfMonth weeks, bool last)
    {
        for (int i = 0; i < WeekNames.Length; i++)
        {
            if (((int)weeks & (1 << i)) != 0)
            {
                yield return new XElement(Ns + "Week", (i + 1).ToString(CultureInfo.InvariantCulture));
            }
        }
        if (last || weeks.HasFlag(TaskWeeksOfMonth.Last))
        {
            yield return new XElement(Ns + "Week", "Last");
        }
    }

    #endregion

    #region Parse (XML -> model)

    /// <summary>Parses Task Scheduler XML into an editable definition.</summary>
    public static TaskDefinitionModel Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var task = doc.Root ?? throw new FormatException("The task XML has no root element.");
        var def = new TaskDefinitionModel { RawXml = xml, SchemaVersion = task.Attribute("version")?.Value };

        var ri = task.Element(Ns + "RegistrationInfo");
        if (ri is not null)
        {
            def.RegistrationInfo = new RegistrationInfoModel
            {
                Author = ri.Element(Ns + "Author")?.Value,
                Description = ri.Element(Ns + "Description")?.Value,
                Uri = ri.Element(Ns + "URI")?.Value,
                Source = ri.Element(Ns + "Source")?.Value,
                Version = ri.Element(Ns + "Version")?.Value,
                Documentation = ri.Element(Ns + "Documentation")?.Value,
                SecurityDescriptorSddl = ri.Element(Ns + "SecurityDescriptor")?.Value,
                Date = ParseDate(ri.Element(Ns + "Date")?.Value),
            };
        }

        var principal = task.Element(Ns + "Principals")?.Elements(Ns + "Principal").FirstOrDefault();
        if (principal is not null)
        {
            def.Principal = ParsePrincipal(principal);
        }

        var settings = task.Element(Ns + "Settings");
        if (settings is not null)
        {
            def.Settings = ParseSettings(settings, task.Attribute("version")?.Value);
        }

        var triggers = task.Element(Ns + "Triggers");
        if (triggers is not null)
        {
            foreach (var te in triggers.Elements())
            {
                var model = ParseTrigger(te);
                if (model is not null)
                {
                    def.Triggers.Add(model);
                }
            }
        }

        var actions = task.Element(Ns + "Actions");
        if (actions is not null)
        {
            foreach (var ae in actions.Elements())
            {
                var model = ParseAction(ae);
                if (model is not null)
                {
                    def.Actions.Add(model);
                }
            }
        }

        return def;
    }

    private static PrincipalModel ParsePrincipal(XElement p) => new()
    {
        Id = p.Attribute("id")?.Value,
        UserId = p.Element(Ns + "UserId")?.Value,
        GroupId = p.Element(Ns + "GroupId")?.Value,
        DisplayName = p.Element(Ns + "DisplayName")?.Value,
        LogonType = ParseLogonType(p.Element(Ns + "LogonType")?.Value),
        RunLevel = string.Equals(p.Element(Ns + "RunLevel")?.Value, "HighestAvailable", StringComparison.OrdinalIgnoreCase)
            ? TaskRunLevel.HighestAvailable : TaskRunLevel.LeastPrivilege,
        RequiredPrivileges = p.Element(Ns + "RequiredPrivileges")?.Elements(Ns + "Privilege").Select(x => x.Value).ToList() ?? [],
    };

    private static TaskSettingsModel ParseSettings(XElement s, string? version)
    {
        var model = new TaskSettingsModel
        {
            Compatibility = CompatibilityForVersion(version),
            MultipleInstances = Enum.TryParse<TaskInstancesPolicy>(s.Element(Ns + "MultipleInstancesPolicy")?.Value, out var mip) ? mip : TaskInstancesPolicy.IgnoreNew,
            DisallowStartIfOnBatteries = ParseBool(s.Element(Ns + "DisallowStartIfOnBatteries")?.Value, true),
            StopIfGoingOnBatteries = ParseBool(s.Element(Ns + "StopIfGoingOnBatteries")?.Value, true),
            AllowHardTerminate = ParseBool(s.Element(Ns + "AllowHardTerminate")?.Value, true),
            StartWhenAvailable = ParseBool(s.Element(Ns + "StartWhenAvailable")?.Value, false),
            RunOnlyIfNetworkAvailable = ParseBool(s.Element(Ns + "RunOnlyIfNetworkAvailable")?.Value, false),
            AllowDemandStart = ParseBool(s.Element(Ns + "AllowStartOnDemand")?.Value, true),
            Enabled = ParseBool(s.Element(Ns + "Enabled")?.Value, true),
            Hidden = ParseBool(s.Element(Ns + "Hidden")?.Value, false),
            RunOnlyIfIdle = ParseBool(s.Element(Ns + "RunOnlyIfIdle")?.Value, false),
            DisallowStartOnRemoteAppSession = ParseBool(s.Element(Ns + "DisallowStartOnRemoteAppSession")?.Value, false),
            UseUnifiedSchedulingEngine = ParseBool(s.Element(Ns + "UseUnifiedSchedulingEngine")?.Value, false),
            WakeToRun = ParseBool(s.Element(Ns + "WakeToRun")?.Value, false),
            Volatile = ParseBool(s.Element(Ns + "Volatile")?.Value, false),
            ExecutionTimeLimit = ParseDuration(s.Element(Ns + "ExecutionTimeLimit")?.Value),
            DeleteExpiredTaskAfter = ParseDuration(s.Element(Ns + "DeleteExpiredTaskAfter")?.Value),
            Priority = int.TryParse(s.Element(Ns + "Priority")?.Value, out var pr) ? pr : 7,
        };

        var idle = s.Element(Ns + "IdleSettings");
        if (idle is not null)
        {
            model.IdleSettings = new IdleSettingsModel
            {
                IdleDuration = ParseDuration(idle.Element(Ns + "Duration")?.Value),
                WaitTimeout = ParseDuration(idle.Element(Ns + "WaitTimeout")?.Value),
                StopOnIdleEnd = ParseBool(idle.Element(Ns + "StopOnIdleEnd")?.Value, true),
                RestartOnIdle = ParseBool(idle.Element(Ns + "RestartOnIdle")?.Value, false),
            };
        }

        var restart = s.Element(Ns + "RestartOnFailure");
        if (restart is not null)
        {
            model.RestartInterval = ParseDuration(restart.Element(Ns + "Interval")?.Value);
            model.RestartCount = int.TryParse(restart.Element(Ns + "Count")?.Value, out var c) ? c : 0;
        }

        var net = s.Element(Ns + "NetworkSettings");
        if (net is not null)
        {
            model.NetworkSettings = new NetworkSettingsModel
            {
                Name = net.Element(Ns + "Name")?.Value,
                Id = Guid.TryParse(net.Element(Ns + "Id")?.Value, out var g) ? g : null,
            };
        }

        var maint = s.Element(Ns + "MaintenanceSettings");
        if (maint is not null)
        {
            model.MaintenanceSettings = new MaintenanceSettingsModel
            {
                Period = ParseDuration(maint.Element(Ns + "Period")?.Value),
                Deadline = ParseDuration(maint.Element(Ns + "Deadline")?.Value),
                Exclusive = ParseBool(maint.Element(Ns + "Exclusive")?.Value, false),
            };
        }

        return model;
    }

    private static TriggerModel? ParseTrigger(XElement element)
    {
        var name = element.Name.LocalName;
        TriggerModel model = name switch
        {
            "BootTrigger" => new BootTriggerModel { Delay = ParseDuration(element.Element(Ns + "Delay")?.Value) },
            "LogonTrigger" => new LogonTriggerModel { UserId = element.Element(Ns + "UserId")?.Value, Delay = ParseDuration(element.Element(Ns + "Delay")?.Value) },
            "RegistrationTrigger" => new RegistrationTriggerModel { Delay = ParseDuration(element.Element(Ns + "Delay")?.Value) },
            "IdleTrigger" => new IdleTriggerModel(),
            "TimeTrigger" => new TimeTriggerModel { RandomDelay = ParseDuration(element.Element(Ns + "RandomDelay")?.Value) },
            "EventTrigger" => ParseEventTrigger(element),
            "SessionStateChangeTrigger" => ParseSessionTrigger(element),
            "CalendarTrigger" => ParseCalendarTrigger(element),
            _ => null!,
        };

        if (model is null)
        {
            return null;
        }

        ApplyCommonTrigger(model, element);
        return model;
    }

    private static void ApplyCommonTrigger(TriggerModel model, XElement element)
    {
        model.Id = element.Attribute("id")?.Value;
        model.Enabled = ParseBool(element.Element(Ns + "Enabled")?.Value, true);
        model.StartBoundary = ParseDate(element.Element(Ns + "StartBoundary")?.Value);
        model.EndBoundary = ParseDate(element.Element(Ns + "EndBoundary")?.Value);
        model.ExecutionTimeLimit = ParseDuration(element.Element(Ns + "ExecutionTimeLimit")?.Value);
        var repetition = element.Element(Ns + "Repetition");
        if (repetition is not null)
        {
            model.Repetition = new RepetitionModel
            {
                Interval = ParseDuration(repetition.Element(Ns + "Interval")?.Value),
                Duration = ParseDuration(repetition.Element(Ns + "Duration")?.Value),
                StopAtDurationEnd = ParseBool(repetition.Element(Ns + "StopAtDurationEnd")?.Value, false),
            };
        }
    }

    private static EventTriggerModel ParseEventTrigger(XElement element)
    {
        var model = new EventTriggerModel
        {
            Subscription = element.Element(Ns + "Subscription")?.Value,
            Delay = ParseDuration(element.Element(Ns + "Delay")?.Value),
        };
        var valueQueries = element.Element(Ns + "ValueQueries");
        if (valueQueries is not null)
        {
            foreach (var v in valueQueries.Elements(Ns + "Value"))
            {
                var key = v.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    model.ValueQueries[key] = v.Value;
                }
            }
        }
        return model;
    }

    private static SessionStateChangeTriggerModel ParseSessionTrigger(XElement element) => new()
    {
        UserId = element.Element(Ns + "UserId")?.Value,
        Delay = ParseDuration(element.Element(Ns + "Delay")?.Value),
        StateChange = Enum.TryParse<SessionStateChangeType>(element.Element(Ns + "StateChange")?.Value, out var sc) ? sc : SessionStateChangeType.ConsoleConnect,
    };

    private static TriggerModel ParseCalendarTrigger(XElement element)
    {
        var random = ParseDuration(element.Element(Ns + "RandomDelay")?.Value);

        if (element.Element(Ns + "ScheduleByDay") is { } byDay)
        {
            return new DailyTriggerModel
            {
                DaysInterval = short.TryParse(byDay.Element(Ns + "DaysInterval")?.Value, out var di) ? di : (short)1,
                RandomDelay = random,
            };
        }
        if (element.Element(Ns + "ScheduleByWeek") is { } byWeek)
        {
            return new WeeklyTriggerModel
            {
                WeeksInterval = short.TryParse(byWeek.Element(Ns + "WeeksInterval")?.Value, out var wi) ? wi : (short)1,
                DaysOfWeek = ParseDaysOfWeek(byWeek.Element(Ns + "DaysOfWeek")),
                RandomDelay = random,
            };
        }
        if (element.Element(Ns + "ScheduleByMonth") is { } byMonth)
        {
            var daysEl = byMonth.Element(Ns + "DaysOfMonth");
            var days = daysEl?.Elements(Ns + "Day").Select(x => x.Value).ToList() ?? [];
            return new MonthlyTriggerModel
            {
                DaysOfMonth = days.Where(d => int.TryParse(d, out _)).Select(int.Parse).ToList(),
                RunOnLastDayOfMonth = days.Any(d => string.Equals(d, "Last", StringComparison.OrdinalIgnoreCase)),
                MonthsOfYear = ParseMonths(byMonth.Element(Ns + "Months")),
                RandomDelay = random,
            };
        }
        if (element.Element(Ns + "ScheduleByMonthDayOfWeek") is { } byMdow)
        {
            var (weeks, last) = ParseWeeks(byMdow.Element(Ns + "Weeks"));
            return new MonthlyDowTriggerModel
            {
                WeeksOfMonth = weeks,
                RunOnLastWeekOfMonth = last,
                DaysOfWeek = ParseDaysOfWeek(byMdow.Element(Ns + "DaysOfWeek")),
                MonthsOfYear = ParseMonths(byMdow.Element(Ns + "Months")),
                RandomDelay = random,
            };
        }

        // Unknown calendar schedule — fall back to a daily trigger so the editor still shows it.
        return new DailyTriggerModel { RandomDelay = random };
    }

    private static ActionModel? ParseAction(XElement element) => element.Name.LocalName switch
    {
        "Exec" => new ExecActionModel
        {
            Id = element.Attribute("id")?.Value,
            Path = element.Element(Ns + "Command")?.Value ?? string.Empty,
            Arguments = element.Element(Ns + "Arguments")?.Value,
            WorkingDirectory = element.Element(Ns + "WorkingDirectory")?.Value,
        },
        "ComHandler" => new ComHandlerActionModel
        {
            Id = element.Attribute("id")?.Value,
            ClassId = Guid.TryParse(element.Element(Ns + "ClassId")?.Value, out var g) ? g : Guid.Empty,
            Data = element.Element(Ns + "Data")?.Value,
        },
        "SendEmail" => ParseEmail(element),
        "ShowMessage" => new ShowMessageActionModel
        {
            Id = element.Attribute("id")?.Value,
            Title = element.Element(Ns + "Title")?.Value,
            MessageBody = element.Element(Ns + "Body")?.Value,
        },
        _ => null,
    };

    private static EmailActionModel ParseEmail(XElement element)
    {
        var model = new EmailActionModel
        {
            Id = element.Attribute("id")?.Value,
            Server = element.Element(Ns + "Server")?.Value,
            Subject = element.Element(Ns + "Subject")?.Value,
            To = element.Element(Ns + "To")?.Value,
            Cc = element.Element(Ns + "Cc")?.Value,
            Bcc = element.Element(Ns + "Bcc")?.Value,
            ReplyTo = element.Element(Ns + "ReplyTo")?.Value,
            From = element.Element(Ns + "From")?.Value,
            Body = element.Element(Ns + "Body")?.Value,
        };
        var attachments = element.Element(Ns + "Attachments");
        if (attachments is not null)
        {
            foreach (var f in attachments.Elements(Ns + "File"))
            {
                model.Attachments.Add(f.Value);
            }
        }
        var headers = element.Element(Ns + "HeaderFields");
        if (headers is not null)
        {
            foreach (var h in headers.Elements(Ns + "HeaderField"))
            {
                var key = h.Element(Ns + "Name")?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    model.Headers[key] = h.Element(Ns + "Value")?.Value ?? string.Empty;
                }
            }
        }
        return model;
    }

    private static TaskDaysOfWeek ParseDaysOfWeek(XElement? element)
    {
        var result = TaskDaysOfWeek.None;
        if (element is null)
        {
            return result;
        }
        for (int i = 0; i < DayNames.Length; i++)
        {
            if (element.Element(Ns + DayNames[i]) is not null)
            {
                result |= (TaskDaysOfWeek)(1 << i);
            }
        }
        return result;
    }

    private static TaskMonthsOfYear ParseMonths(XElement? element)
    {
        var result = TaskMonthsOfYear.None;
        if (element is null)
        {
            return result;
        }
        for (int i = 0; i < MonthNames.Length; i++)
        {
            if (element.Element(Ns + MonthNames[i]) is not null)
            {
                result |= (TaskMonthsOfYear)(1 << i);
            }
        }
        return result;
    }

    private static (TaskWeeksOfMonth weeks, bool last) ParseWeeks(XElement? element)
    {
        var weeks = TaskWeeksOfMonth.None;
        var last = false;
        if (element is null)
        {
            return (weeks, last);
        }
        foreach (var w in element.Elements(Ns + "Week"))
        {
            if (string.Equals(w.Value, "Last", StringComparison.OrdinalIgnoreCase))
            {
                last = true;
            }
            else if (int.TryParse(w.Value, out var n) && n is >= 1 and <= 4)
            {
                weeks |= (TaskWeeksOfMonth)(1 << (n - 1));
            }
        }
        return (weeks, last);
    }

    #endregion

    #region Helpers

    private static void AddIf(XElement parent, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parent.Add(new XElement(Ns + name, value));
        }
    }

    private static string XmlBool(bool value) => value ? "true" : "false";

    private static bool ParseBool(string? value, bool fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value is "true" or "1";

    private static string DurationString(TimeSpan value) => XmlConvert.ToString(value);

    private static string? DurationOrNull(TimeSpan? value) => value is { } v ? XmlConvert.ToString(v) : null;

    // ExecutionTimeLimit uses "PT0S" to mean "no limit"/indefinite in the schema.
    private static string DurationOrZero(TimeSpan? value) => value is { } v ? XmlConvert.ToString(v) : "PT0S";

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            var ts = XmlConvert.ToTimeSpan(value);
            return ts == TimeSpan.Zero ? null : ts;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
    }

    private static string LogonTypeToXml(TaskLogonType type) => type switch
    {
        TaskLogonType.Password => "Password",
        TaskLogonType.S4U => "S4U",
        TaskLogonType.InteractiveToken => "InteractiveToken",
        TaskLogonType.Group => "Group",
        TaskLogonType.ServiceAccount => "ServiceAccount",
        TaskLogonType.InteractiveTokenOrPassword => "InteractiveTokenOrPassword",
        _ => "InteractiveToken",
    };

    private static TaskLogonType ParseLogonType(string? value) => value switch
    {
        "Password" => TaskLogonType.Password,
        "S4U" => TaskLogonType.S4U,
        "InteractiveToken" => TaskLogonType.InteractiveToken,
        "Group" => TaskLogonType.Group,
        "ServiceAccount" => TaskLogonType.ServiceAccount,
        "InteractiveTokenOrPassword" => TaskLogonType.InteractiveTokenOrPassword,
        _ => TaskLogonType.InteractiveToken,
    };

    /// <summary>
    /// Picks the <c>&lt;Task version&gt;</c> to emit. The "Configure for" compatibility chooses the target
    /// version (so lowering it actually takes effect); it is then clamped up only to the minimum the
    /// features in use require — emitting too low a version is rejected as malformed (SCHED_E_MALFORMEDXML).
    /// The original schema version is preserved only when it is newer than the compatibility enum can
    /// express (1.5+), so we never silently downgrade/strip elements from a newer task.
    /// </summary>
    private static string ResolveVersion(TaskDefinitionModel def)
    {
        var target = VersionForCompatibility(def.Settings.Compatibility);
        if (VersionValue(def.SchemaVersion ?? "1.0") > VersionValue("1.4"))
        {
            target = MaxVersion(target, def.SchemaVersion!);
        }
        return MaxVersion(target, MinVersionForFeatures(def));
    }

    private static string MinVersionForFeatures(TaskDefinitionModel def)
    {
        // 1.2 (Windows Vista / Server 2008) is the practical baseline: the Task Scheduler service rejects
        // the standard settings this editor always emits — MultipleInstancesPolicy, AllowStartOnDemand,
        // AllowHardTerminate, StartWhenAvailable, RunOnlyIfNetworkAvailable — at version 1.1, and there are
        // no 1.1 tasks in practice. (Verified empirically against the live service via RegisterTask with
        // TASK_VALIDATE_ONLY.) Choosing "Windows Vista" therefore resolves to 1.2, not 1.1.
        var version = "1.2";
        if (def.Principal.RequiredPrivileges.Count > 0 && SupportsWindows7Compatibility(def.Settings.Compatibility))
        {
            // <RequiredPrivileges> (IPrincipal2) is Windows 7 / Server 2008 R2 = version 1.3.
            version = MaxVersion(version, "1.3");
        }
        if ((def.Settings.DisallowStartOnRemoteAppSession || def.Settings.UseUnifiedSchedulingEngine) &&
            SupportsWindows8Compatibility(def.Settings.Compatibility))
        {
            // ITaskSettings2 fields (DisallowStartOnRemoteAppSession, UseUnifiedSchedulingEngine) are only
            // valid at 1.3 (Windows 8 / Server 2012); the 1.2 validator rejects them. When the user chooses
            // Windows 7 or earlier, SerializeSettings omits them so the lower compatibility stays reachable.
            version = MaxVersion(version, "1.3");
        }
        if (def.Settings.MaintenanceSettings is not null || def.Settings.Volatile)
        {
            // <MaintenanceSettings> and <Volatile> (ITaskSettings3) require 1.4 (Windows 8.1 / Windows 10).
            version = MaxVersion(version, "1.4");
        }
        return version;
    }

    private static string MaxVersion(string a, string b) => VersionValue(a) >= VersionValue(b) ? a : b;

    private static bool SupportsWindows7Compatibility(TaskCompatibility compatibility) =>
        compatibility >= TaskCompatibility.V2_1;

    private static bool SupportsWindows8Compatibility(TaskCompatibility compatibility) =>
        compatibility >= TaskCompatibility.V2_2;

    private static int VersionValue(string version)
    {
        var parts = version.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 1;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        return (major * 100) + minor;
    }

    // The "Configure for" level maps to the <Task version> the service round-trips back to the SAME
    // TASK_COMPATIBILITY (which is what taskschd.msc reads to label the task). Verified against the live
    // service: 1.2->V2 (Vista), 1.3->V2_1 (Win7), 1.4->V2_2 (Win8). The earlier table was off by one, so
    // both Vista and Windows 7 collapsed onto 1.2 and Windows 7 showed up as Windows Vista.
    private static string VersionForCompatibility(TaskCompatibility c) => c switch
    {
        TaskCompatibility.At or TaskCompatibility.V1 => "1.0",
        TaskCompatibility.V2 => "1.2",
        TaskCompatibility.V2_1 => "1.3",
        TaskCompatibility.V2_2 => "1.4",
        TaskCompatibility.V2_3 => "1.4",
        _ => DefaultVersion,
    };

    private static TaskCompatibility CompatibilityForVersion(string? version) => version switch
    {
        "1.0" => TaskCompatibility.V1,
        "1.1" or "1.2" => TaskCompatibility.V2,
        "1.3" => TaskCompatibility.V2_1,
        "1.4" or "1.5" or "1.6" or "1.7" => TaskCompatibility.V2_3,
        _ => TaskCompatibility.V2,
    };

    #endregion
}
