namespace ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;

/// <summary>
/// Represents information about a print port on the system.
/// </summary>
public class PrintPortInfo
{
    /// <summary>Name of the port (e.g., "PORTPROMPT:", "USB001")</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>Description of the port</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Type of the port (e.g., "Local Port", "TCP/IP Port")</summary>
    public string PortType { get; set; } = string.Empty;

    /// <summary>Names of printers using this port (comma-separated)</summary>
    public string PrinterNames { get; set; } = string.Empty;

    /// <summary>Combined description and type for display</summary>
    public string DescriptionAndType => $"{Description}, {PortType}";
}


