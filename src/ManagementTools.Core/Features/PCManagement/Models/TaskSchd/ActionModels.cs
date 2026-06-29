using System;
using System.Collections.Generic;

namespace ManagementTools.Core.Features.PCManagement.Models.TaskSchd;

/// <summary>Common properties shared by all actions (mirrors <c>IAction</c>).</summary>
public abstract class ActionModel
{
    /// <summary>The concrete action type.</summary>
    public abstract TaskActionType Type { get; }

    /// <summary>Optional action identifier.</summary>
    public string? Id { get; set; }

    /// <summary>A short, human-readable summary of the action (e.g. the program path). Set by the view-model layer.</summary>
    public string? DisplaySummary { get; set; }
}

/// <summary>Executes a command-line operation (IExecAction).</summary>
public sealed class ExecActionModel : ActionModel
{
    public override TaskActionType Type => TaskActionType.Execute;

    /// <summary>The program or script to run.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The arguments passed to the program.</summary>
    public string? Arguments { get; set; }

    /// <summary>The working directory ("Start in") for the program.</summary>
    public string? WorkingDirectory { get; set; }
}

/// <summary>Fires an in-process COM handler (IComHandlerAction).</summary>
public sealed class ComHandlerActionModel : ActionModel
{
    public override TaskActionType Type => TaskActionType.ComHandler;

    /// <summary>The CLSID of the COM object that implements ITaskHandler.</summary>
    public Guid ClassId { get; set; }

    /// <summary>Additional data passed to the COM handler.</summary>
    public string? Data { get; set; }
}

/// <summary>Sends an email message (IEmailAction). Deprecated since Windows 8 but still authored for parity.</summary>
public sealed class EmailActionModel : ActionModel
{
    public override TaskActionType Type => TaskActionType.SendEmail;

    public string? From { get; set; }
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string? ReplyTo { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }

    /// <summary>The SMTP server used to send the message.</summary>
    public string? Server { get; set; }

    /// <summary>File paths attached to the message.</summary>
    public IList<string> Attachments { get; set; } = new List<string>();

    /// <summary>Additional SMTP headers (name/value pairs).</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Shows a message box (IShowMessageAction). Deprecated since Windows 8 but still authored for parity.</summary>
public sealed class ShowMessageActionModel : ActionModel
{
    public override TaskActionType Type => TaskActionType.ShowMessage;

    /// <summary>The title of the message box.</summary>
    public string? Title { get; set; }

    /// <summary>The body text of the message box.</summary>
    public string? MessageBody { get; set; }
}
