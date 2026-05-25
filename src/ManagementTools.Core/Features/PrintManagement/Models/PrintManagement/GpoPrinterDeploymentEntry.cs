using System;

namespace ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;

/// <summary>
/// Represents a printer connection deployment entry in a Group Policy Object (GPO).
/// </summary>
public sealed class GpoPrinterDeploymentEntry
{
    /// <summary>
    /// Display name for the printer connection (typically the UNC path).
    /// </summary>
    public string PrinterName { get; set; } = string.Empty;

    /// <summary>
    /// The UNC connection path for the printer (for example, \\server\share).
    /// </summary>
    public string ConnectionPath { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the target Group Policy Object.
    /// </summary>
    public string GpoName { get; set; } = string.Empty;

    /// <summary>
    /// The GUID of the target Group Policy Object, including braces.
    /// </summary>
    public string GpoGuid { get; set; } = string.Empty;

    /// <summary>
    /// The deployment scope for this printer connection.
    /// </summary>
    public GpoPrinterDeploymentScope ConnectionType { get; set; }

    /// <summary>
    /// The distinguished name of the deployment entry in Active Directory.
    /// </summary>
    public string DistinguishedName { get; set; } = string.Empty;
}

/// <summary>
/// Represents the deployment scope for a printer connection in a GPO.
/// </summary>
public enum GpoPrinterDeploymentScope
{
    /// <summary>
    /// Deploys the connection to users (per-user).
    /// </summary>
    PerUser,

    /// <summary>
    /// Deploys the connection to computers (per-machine).
    /// </summary>
    PerMachine
}

/// <summary>
/// Represents a Group Policy Object (GPO) available for deployment.
/// </summary>
public sealed class GroupPolicyObjectInfo
{
    /// <summary>
    /// The GUID of the GPO, including braces.
    /// </summary>
    public string Guid { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the GPO.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The distinguished name of the GPO container in Active Directory.
    /// </summary>
    public string DistinguishedName { get; set; } = string.Empty;
}


