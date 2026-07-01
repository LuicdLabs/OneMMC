namespace OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;

public abstract class SecurityAssociationModel
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public sealed class MainModeSecurityAssociationModel : SecurityAssociationModel
{
    public string LocalEndpoint { get; set; } = string.Empty;

    public string RemoteEndpoint { get; set; } = string.Empty;

    public string MainMode { get; set; } = string.Empty;

    public string FirstAuthMethod { get; set; } = string.Empty;

    public string SecondAuthMethod { get; set; } = string.Empty;

    public string CipherAlgorithm { get; set; } = string.Empty;

    public string HashAlgorithm { get; set; } = string.Empty;

    public string KeyExchange { get; set; } = string.Empty;
}

public sealed class QuickModeSecurityAssociationModel : SecurityAssociationModel
{
    public string LocalAddress { get; set; } = string.Empty;

    public string LocalPort { get; set; } = string.Empty;

    public string RemoteAddress { get; set; } = string.Empty;

    public string RemotePort { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public string AhIntegrity { get; set; } = string.Empty;

    public string EspIntegrity { get; set; } = string.Empty;

    public string EspEncryption { get; set; } = string.Empty;
}


