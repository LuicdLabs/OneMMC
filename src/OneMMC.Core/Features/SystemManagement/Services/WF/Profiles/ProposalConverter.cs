using System;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using Microsoft.Management.Infrastructure;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;

internal static class ProposalConverter
{
    private const ushort QuickModeEncapsulationNone = 0;
    private const ushort QuickModeEncapsulationAh = 1;
    private const ushort QuickModeEncapsulationEsp = 2;
    private const ushort QuickModeEncapsulationAhEsp = 3;

    private static readonly Dictionary<string, ushort> MainModeHashByName = new(StringComparer.Ordinal)
    {
        ["MD5"] = 2,
        ["SHA-1"] = 3,
        ["SHA-256"] = 65001,
        ["SHA-384"] = 65002
    };

    private static readonly Dictionary<string, ushort> MainModeCipherByName = new(StringComparer.Ordinal)
    {
        ["DES"] = 2,
        ["3DES"] = 6,
        ["AES-CBC 128"] = 65001,
        ["AES-CBC 192"] = 65002,
        ["AES-CBC 256"] = 65003
    };

    private static readonly Dictionary<string, ushort> MainModeGroupByName = new(StringComparer.Ordinal)
    {
        ["Diffie-Hellman Group 1"] = 1,
        ["Diffie-Hellman Group 2"] = 2,
        ["Diffie-Hellman Group 14"] = 14,
        ["Diffie-Hellman Group 24"] = 24,
        ["Elliptic Curve Diffie-Hellman P-256"] = 19,
        ["Elliptic Curve Diffie-Hellman P-384"] = 20
    };

    private static readonly Dictionary<string, ushort> QuickModeCipherByName = new(StringComparer.Ordinal)
    {
        ["DES"] = 2,
        ["3DES"] = 6,
        ["AES-CBC 128"] = 65001,
        ["AES-CBC 192"] = 65002,
        ["AES-CBC 256"] = 65003,
        ["AES-GCM 128"] = 65004,
        ["AES-GCM 192"] = 65005,
        ["AES-GCM 256"] = 65006
    };

    private static readonly Dictionary<string, ushort> QuickModeHashByName = new(StringComparer.Ordinal)
    {
        ["None"] = 0,
        ["MD5"] = 2,
        ["SHA-1"] = 3,
        ["SHA-256"] = 65001,
        ["SHA-384"] = 65002,
        ["AES-GMAC 128"] = 65003,
        ["AES-GMAC 192"] = 65004,
        ["AES-GMAC 256"] = 65005,
        ["AES-GCM 128"] = 65003,
        ["AES-GCM 192"] = 65004,
        ["AES-GCM 256"] = 65005
    };

    internal static IReadOnlyList<MainModeProposalDefinition> BuildMainModeProposalDefinitions(
        IEnumerable<SecurityMethodEntry>? methods)
    {
        if (methods is null)
        {
            return [];
        }

        List<MainModeProposalDefinition> proposals = [];
        foreach (SecurityMethodEntry method in methods)
        {
            if (!MainModeHashByName.TryGetValue(method.IntegrityAlgorithm, out ushort hashAlgorithm) ||
                !MainModeCipherByName.TryGetValue(method.EncryptionAlgorithm, out ushort cipherAlgorithm) ||
                !MainModeGroupByName.TryGetValue(method.KeyExchangeAlgorithm, out ushort groupId))
            {
                continue;
            }

            proposals.Add(new MainModeProposalDefinition(cipherAlgorithm, hashAlgorithm, groupId));
        }

        return proposals;
    }

    internal static IReadOnlyList<QuickModeProposalDefinition> BuildQuickModeProposalDefinitions(
        IEnumerable<DataIntegrityAlgorithmEntry>? integrityAlgorithms,
        IEnumerable<IntegrityEncryptionAlgorithmEntry>? integrityEncryptionAlgorithms)
    {
        List<QuickModeProposalDefinition> proposals = [];

        if (integrityEncryptionAlgorithms is not null)
        {
            foreach (IntegrityEncryptionAlgorithmEntry algorithm in integrityEncryptionAlgorithms)
            {
                if (!QuickModeCipherByName.TryGetValue(algorithm.EncryptionAlgorithm, out ushort cipherAlgorithm) ||
                    !QuickModeHashByName.TryGetValue(algorithm.IntegrityAlgorithm, out ushort hashAlgorithm))
                {
                    continue;
                }

                bool withAh = string.Equals(algorithm.Protocol, "ESP and AH", StringComparison.OrdinalIgnoreCase);
                proposals.Add(new QuickModeProposalDefinition(
                    withAh ? QuickModeEncapsulationAhEsp : QuickModeEncapsulationEsp,
                    withAh ? hashAlgorithm : null,
                    hashAlgorithm,
                    cipherAlgorithm,
                    (uint)Math.Max(0, algorithm.MinutesLifetime),
                    (ulong)Math.Max(0, algorithm.KilobytesLifetime)));
            }
        }

        if (integrityAlgorithms is not null)
        {
            foreach (DataIntegrityAlgorithmEntry algorithm in integrityAlgorithms)
            {
                if (!QuickModeHashByName.TryGetValue(algorithm.IntegrityAlgorithm, out ushort hashAlgorithm))
                {
                    continue;
                }

                string protocol = algorithm.Protocol ?? string.Empty;
                bool ahOnly = string.Equals(protocol, "AH", StringComparison.OrdinalIgnoreCase);
                bool nullEncapsulation = string.Equals(protocol, "Null encapsulation", StringComparison.OrdinalIgnoreCase);

                proposals.Add(new QuickModeProposalDefinition(
                    ahOnly ? QuickModeEncapsulationAh : nullEncapsulation ? QuickModeEncapsulationNone : QuickModeEncapsulationEsp,
                    ahOnly ? hashAlgorithm : null,
                    ahOnly ? null : hashAlgorithm,
                    0,
                    (uint)Math.Max(0, algorithm.MinutesLifetime),
                    (ulong)Math.Max(0, algorithm.KilobytesLifetime)));
            }
        }

        return proposals;
    }

    internal static IReadOnlyList<SecurityMethodEntry> ReadMainModeSecurityMethods(
        IReadOnlyList<MainModeProposalDefinition> proposals)
    {
        List<SecurityMethodEntry> methods = [];
        foreach (MainModeProposalDefinition proposal in proposals)
        {
            if (!TryResolveMainModeHashName(proposal.HashAlgorithm, out string? hashName) ||
                !TryResolveMainModeCipherName(proposal.CipherAlgorithm, out string? cipherName) ||
                !TryResolveMainModeGroupName(proposal.GroupId, out string? groupName))
            {
                continue;
            }

            methods.Add(new SecurityMethodEntry
            {
                IntegrityAlgorithm = hashName!,
                EncryptionAlgorithm = cipherName!,
                KeyExchangeAlgorithm = groupName!
            });
        }

        return methods;
    }

    internal static IReadOnlyList<DataIntegrityAlgorithmEntry> ReadIntegrityAlgorithms(CimInstance? set)
    {
        if (set?.CimInstanceProperties["Proposals"]?.Value is not CimInstance[] proposals || proposals.Length == 0)
        {
            return [];
        }

        List<DataIntegrityAlgorithmEntry> result = [];
        foreach (CimInstance proposal in proposals)
        {
            ushort encapsulation = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["Encapsulation"]?.Value, 0);
            ushort cipher = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["CipherAlgorithm"]?.Value, 0);

            if (cipher != 0)
            {
                continue;
            }

            ushort hashAh = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["HashAlgorithmAH"]?.Value, 0);
            ushort hashEsp = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["HashAlgorithmESP"]?.Value, 0);
            ushort hash = encapsulation == QuickModeEncapsulationAh ? hashAh : hashEsp != 0 ? hashEsp : hashAh;
            if (!TryResolveQuickModeHashName(hash, preferAead: false, 0, out string? integrityName))
            {
                continue;
            }

            string protocol = encapsulation switch
            {
                QuickModeEncapsulationNone => "Null encapsulation",
                QuickModeEncapsulationAh => "AH",
                _ => "ESP"
            };

            result.Add(new DataIntegrityAlgorithmEntry
            {
                Protocol = protocol,
                IntegrityAlgorithm = integrityName!,
                MinutesLifetime = (int)ValueConverter.ConvertToUInt32(proposal.CimInstanceProperties["MaxLifetimeMinutes"]?.Value, 60),
                KilobytesLifetime = (int)ValueConverter.ConvertToUInt64(proposal.CimInstanceProperties["MaxLifetimeKilobytes"]?.Value, 100000)
            });
        }

        return result;
    }

    internal static IReadOnlyList<IntegrityEncryptionAlgorithmEntry> ReadIntegrityEncryptionAlgorithms(CimInstance? set)
    {
        if (set?.CimInstanceProperties["Proposals"]?.Value is not CimInstance[] proposals || proposals.Length == 0)
        {
            return [];
        }

        List<IntegrityEncryptionAlgorithmEntry> result = [];
        foreach (CimInstance proposal in proposals)
        {
            ushort encapsulation = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["Encapsulation"]?.Value, 0);
            ushort cipher = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["CipherAlgorithm"]?.Value, 0);
            if (!TryResolveQuickModeCipherName(cipher, out string? encryptionName))
            {
                continue;
            }

            ushort hashAh = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["HashAlgorithmAH"]?.Value, 0);
            ushort hashEsp = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["HashAlgorithmESP"]?.Value, 0);
            ushort hash = hashEsp != 0 ? hashEsp : hashAh;
            if (!TryResolveQuickModeHashName(hash, preferAead: true, cipher, out string? integrityName))
            {
                continue;
            }

            result.Add(new IntegrityEncryptionAlgorithmEntry
            {
                Protocol = encapsulation == QuickModeEncapsulationAhEsp ? "ESP and AH" : "ESP",
                IntegrityAlgorithm = integrityName!,
                EncryptionAlgorithm = encryptionName!,
                MinutesLifetime = (int)ValueConverter.ConvertToUInt32(proposal.CimInstanceProperties["MaxLifetimeMinutes"]?.Value, 60),
                KilobytesLifetime = (int)ValueConverter.ConvertToUInt64(proposal.CimInstanceProperties["MaxLifetimeKilobytes"]?.Value, 100000)
            });
        }

        return result;
    }

    internal static IReadOnlyList<MainModeProposalDefinition> ReadMainModeProposals(CimInstance? set)
    {
        if (set?.CimInstanceProperties["Proposals"]?.Value is not CimInstance[] proposals || proposals.Length == 0)
        {
            return [];
        }

        return proposals.Select(proposal => new MainModeProposalDefinition(
                ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["CipherAlgorithm"]?.Value, 0),
                ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["HashAlgorithm"]?.Value, 0),
                ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["GroupId"]?.Value, 0)))
            .ToList();
    }

    internal static ushort[] ReadAuthMethods(CimInstance? set)
    {
        if (set?.CimInstanceProperties["Proposals"]?.Value is not CimInstance[] proposals || proposals.Length == 0)
        {
            return [];
        }

        return proposals
            .Select(proposal => ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["AuthenticationMethod"]?.Value, 0))
            .Where(method => method != 0)
            .ToArray();
    }

    internal static bool AreMainModeProposalsEqual(
        IReadOnlyList<MainModeProposalDefinition> left,
        IReadOnlyList<MainModeProposalDefinition> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveMainModeHashName(ushort value, out string? name)
    {
        name = value switch
        {
            0 => "None",
            2 => "MD5",
            3 => "SHA-1",
            65001 => "SHA-256",
            65002 => "SHA-384",
            _ => null
        };

        return name is not null;
    }

    private static bool TryResolveMainModeCipherName(ushort value, out string? name)
    {
        name = value switch
        {
            2 => "DES",
            6 => "3DES",
            65001 => "AES-CBC 128",
            65002 => "AES-CBC 192",
            65003 => "AES-CBC 256",
            _ => null
        };

        return name is not null;
    }

    private static bool TryResolveMainModeGroupName(ushort value, out string? name)
    {
        name = value switch
        {
            1 => "Diffie-Hellman Group 1",
            2 => "Diffie-Hellman Group 2",
            14 => "Diffie-Hellman Group 14",
            19 => "Elliptic Curve Diffie-Hellman P-256",
            20 => "Elliptic Curve Diffie-Hellman P-384",
            24 => "Diffie-Hellman Group 24",
            _ => null
        };

        return name is not null;
    }

    private static bool TryResolveQuickModeCipherName(ushort value, out string? name)
    {
        name = value switch
        {
            2 => "DES",
            6 => "3DES",
            65001 => "AES-CBC 128",
            65002 => "AES-CBC 192",
            65003 => "AES-CBC 256",
            65004 => "AES-GCM 128",
            65005 => "AES-GCM 192",
            65006 => "AES-GCM 256",
            _ => null
        };

        return name is not null;
    }

    private static bool TryResolveQuickModeHashName(ushort value, bool preferAead, ushort cipherAlgorithm, out string? name)
    {
        if (preferAead)
        {
            if (cipherAlgorithm == 65004 && value == 65003)
            {
                name = "AES-GCM 128";
                return true;
            }

            if (cipherAlgorithm == 65005 && value == 65004)
            {
                name = "AES-GCM 192";
                return true;
            }

            if (cipherAlgorithm == 65006 && value == 65005)
            {
                name = "AES-GCM 256";
                return true;
            }
        }

        name = value switch
        {
            2 => "MD5",
            3 => "SHA-1",
            65001 => "SHA-256",
            65002 => "SHA-384",
            65003 => "AES-GMAC 128",
            65004 => "AES-GMAC 192",
            65005 => "AES-GMAC 256",
            _ => null
        };

        return name is not null;
    }
}
