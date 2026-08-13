using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.SystemManagement.Infrastructure.WF;

/// <summary>
/// Activation and marshalling helpers for the source-generated HNetCfg firewall interfaces
/// (<see cref="INetFwPolicy2"/> and the <see cref="INetFwRule"/> hierarchy). Centralizes coclass
/// creation via <see cref="ComActivator"/> (AOT-safe, replacing <c>Type.GetTypeFromProgID</c> +
/// <c>Activator.CreateInstance</c>), VARIANT_BOOL conversion, rule-collection enumeration through
/// <c>IEnumVARIANT</c>, and the VARIANT <c>Interfaces</c> property. See <c>ComInterfaces.cs</c> for
/// the interface definitions.
/// </summary>
internal static class FirewallCom
{
    // Registry-verified CLSIDs of the HNetCfg coclasses (both ThreadingModel=Both, so the callers'
    // MTA threads are fine): HNetCfg.FwPolicy2 and HNetCfg.FWRule.
    private static readonly Guid ClsidNetFwPolicy2 = new("E2B3C97F-6AE1-41AC-817A-F6F92166D7DD");
    private static readonly Guid ClsidNetFwRule = new("2C5BC43E-3369-4C33-AB0C-BE9469677AF4");

    /// <summary>Creates the Windows Firewall policy coclass (<c>HNetCfg.FwPolicy2</c>).</summary>
    internal static INetFwPolicy2 CreatePolicy2() => ComActivator.CreateInstance<INetFwPolicy2>(ClsidNetFwPolicy2);

    /// <summary>Creates a new firewall rule coclass (<c>HNetCfg.FWRule</c>).</summary>
    internal static INetFwRule3 CreateRule() => ComActivator.CreateInstance<INetFwRule3>(ClsidNetFwRule);

    /// <summary>Releases a source-generated firewall COM wrapper; ignores managed objects and nulls.</summary>
    internal static void Release(object? comObject) => ComActivator.Release(comObject);

    /// <summary>Converts a raw <c>VARIANT_BOOL</c> to a <see cref="bool"/>.</summary>
    internal static bool ToBool(short variantBool) => variantBool != 0;

    /// <summary>Converts a <see cref="bool"/> to a raw <c>VARIANT_BOOL</c> (-1 = true, 0 = false).</summary>
    internal static short ToVariantBool(bool value) => value ? (short)-1 : (short)0;

    /// <summary>
    /// Enumerates a firewall rule collection through its <c>IEnumVARIANT</c> (the collection exposes
    /// no index accessor), yielding each rule as an <see cref="INetFwRule3"/>. Each yielded wrapper is
    /// owned by the caller and must be released via <see cref="Release"/>.
    /// </summary>
    internal static IEnumerable<INetFwRule3> EnumerateRules(INetFwRules rules)
    {
        nint enumPtr = rules.get__NewEnum();
        if (enumPtr == 0)
        {
            yield break;
        }

        // _NewEnum returns an AddRef'd IUnknown; wrap it (which takes its own ref) then drop ours.
        object wrapper;
        try
        {
            wrapper = ComActivator.ComWrappers.GetOrCreateObjectForComInstance(enumPtr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(enumPtr);
        }

        IEnumVariant enumerator;
        try
        {
            enumerator = (IEnumVariant)wrapper;
        }
        catch
        {
            ComActivator.Release(wrapper);
            throw;
        }

        try
        {
            while (true)
            {
                Variant variant = default;
                INetFwRule3? rule;
                try
                {
                    int hr = enumerator.Next(1, out variant, out uint fetched);
                    if (hr < 0 || fetched == 0)
                    {
                        yield break;
                    }

                    // Each element is a VT_DISPATCH holding the rule. The wrapper takes its own
                    // reference before the element variant's reference is released.
                    rule = variant.ToComInterface<INetFwRule3>();
                }
                finally
                {
                    variant.Clear();
                }

                if (rule is not null)
                {
                    yield return rule;
                }
            }
        }
        finally
        {
            ComActivator.Release(enumerator);
        }
    }

    /// <summary>
    /// Reads the rule's <c>Interfaces</c> VARIANT (a SAFEARRAY of interface aliases, or empty for
    /// "all interfaces") into a normalized comma-separated alias string.
    /// </summary>
    internal static string ReadInterfaces(INetFwRule3 rule)
    {
        rule.get_Interfaces(out Variant interfaces);
        try
        {
            List<string> list = interfaces.ToStringList();
            if (list.Count > 0)
            {
                return WindowsFirewallSupport.JoinCsv(list);
            }

            // Fallback for a single (non-array) BSTR value.
            string? single = interfaces.ToInvariantString();
            return string.IsNullOrEmpty(single)
                ? string.Empty
                : WindowsFirewallSupport.NormalizeInterfaceAliases(single);
        }
        finally
        {
            interfaces.Clear();
        }
    }

    /// <summary>
    /// Writes the rule's <c>Interfaces</c> VARIANT from a comma-separated alias string. An empty/blank
    /// value becomes <c>VT_EMPTY</c> ("all interfaces") because the COM property rejects an empty
    /// SAFEARRAY.
    /// </summary>
    internal static void WriteInterfaces(INetFwRule3 rule, string? aliases)
    {
        string[] parsed = WindowsFirewallSupport.ParseCsv(aliases);
        using Variant variant = Variant.FromStringArray(parsed);
        rule.put_Interfaces(variant);
    }
}
