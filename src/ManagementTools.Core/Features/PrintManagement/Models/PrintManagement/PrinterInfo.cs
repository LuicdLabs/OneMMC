namespace ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;

/// <summary>
/// Represents information about a printer installed on the system.
/// </summary>
public class PrinterInfo
{
    /// <summary>Name of the printer</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Current printer status description (e.g., "Ready", "Offline")</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Whether the printer is currently paused.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Number of jobs currently in the print queue</summary>
    public int JobCount { get; set; }

    /// <summary>Version of the printer driver</summary>
    public string DriverVersion { get; set; } = string.Empty;

    /// <summary>Name of the printer driver</summary>
    public string DriverName { get; set; } = string.Empty;

    /// <summary>Whether this is the default printer</summary>
    public bool IsDefault { get; set; }

    /// <summary>Port name the printer is connected to</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>Share name if the printer is shared</summary>
    public string ShareName { get; set; } = string.Empty;

    /// <summary>Comment/description associated with the printer</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Whether the printer is shared on the network</summary>
    public bool IsShared { get; set; }

    /// <summary>Whether the printer is a network printer</summary>
    public bool IsNetwork { get; set; }

    /// <summary>Server name for network printers</summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Processor used by this printer</summary>
    public string PrintProcessor { get; set; } = string.Empty;

    /// <summary>Location of the printer</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Driver isolation mode</summary>
    public string IsolationMode { get; set; } = "None";

    /// <summary>Whether the printer was deployed via GPO</summary>
    public bool IsDeployedViaGPO { get; set; }

    /// <summary>Whether the printer is deployed per-user (PRINTER_ATTRIBUTE_PER_USER flag)</summary>
    public bool IsPerUser { get; set; }

    /// <summary>Whether the printer was pushed via Per-User GPO (PRINTER_ATTRIBUTE_PUSHED_USER flag)</summary>
    public bool IsPushedUser { get; set; }

    /// <summary>Whether the printer was pushed via Per-Machine GPO (PRINTER_ATTRIBUTE_PUSHED_MACHINE flag)</summary>
    public bool IsPushedMachine { get; set; }

    /// <summary>The name of the GPO that deployed this printer for the user</summary>
    public string PerUserGPO { get; set; } = string.Empty;

    /// <summary>The name of the GPO that deployed this printer for the computer</summary>
    public string PerComputerGPO { get; set; } = string.Empty;
}


