using System;

namespace OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;

public sealed class NetworkConnectionItem
{
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

public sealed class SecurityMethodEntry
{
    public string IntegrityAlgorithm { get; set; } = "SHA-256";
    public string EncryptionAlgorithm { get; set; } = "AES-CBC 256";
    public string KeyExchangeAlgorithm { get; set; } = "Elliptic Curve Diffie-Hellman P-256";
}

public sealed class DataIntegrityAlgorithmEntry
{
    public string Protocol { get; set; } = "ESP";
    public string IntegrityAlgorithm { get; set; } = "SHA-256";
    public int MinutesLifetime { get; set; } = 60;
    public int KilobytesLifetime { get; set; } = 100000;

    public string KeyLifetimeDisplay => $"{MinutesLifetime}/{KilobytesLifetime:N0}";
}

public sealed class IntegrityEncryptionAlgorithmEntry
{
    public string Protocol { get; set; } = "ESP";
    public string IntegrityAlgorithm { get; set; } = "AES-GCM 256";
    public string EncryptionAlgorithm { get; set; } = "AES-GCM 256";
    public int MinutesLifetime { get; set; } = 60;
    public int KilobytesLifetime { get; set; } = 100000;

    public string KeyLifetimeDisplay => $"{MinutesLifetime}/{KilobytesLifetime:N0}";
}

public sealed class TunnelAuthorizationItem
{
    public string Name { get; set; } = string.Empty;

    public string Sid { get; set; } = string.Empty;

    public override string ToString() => Name;
}


