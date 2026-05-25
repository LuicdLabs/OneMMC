namespace ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;

/// <summary>
/// Represents information about a print driver installed on the system.
/// </summary>
public class PrintDriverInfo
{
    /// <summary>Name of the print driver</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>INF file name for the driver</summary>
    public string InfName { get; set; } = string.Empty;

    /// <summary>Target environment for the driver (for example, Windows x64).</summary>
    public string EnvironmentName { get; set; } = string.Empty;

    /// <summary>Version of the driver</summary>
    public string DriverVersion { get; set; } = string.Empty;

    /// <summary>Driver isolation mode (None, Shared, Isolated)</summary>
    public string IsolationMode { get; set; } = string.Empty;

    /// <summary>Configuration file for the driver</summary>
    public string ConfigFile { get; set; } = string.Empty;

    /// <summary>Data file for the driver</summary>
    public string DataFile { get; set; } = string.Empty;

    /// <summary>Path to the driver file</summary>
    public string DriverPath { get; set; } = string.Empty;

    /// <summary>Monitor name associated with the driver</summary>
    public string MonitorName { get; set; } = string.Empty;

    /// <summary>Whether the driver advertises isolation support.</summary>
    public bool SupportsIsolation { get; set; }
}


