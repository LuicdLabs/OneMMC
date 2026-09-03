using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.Native;

/// <summary>
/// Well-known values and layout invariants for the legacy <c>polstore.dll</c> structures declared
/// in <c>IPSecurityPolicyStructs.cs</c>.
/// </summary>
/// <remarks>
/// Sizes are asserted in Debug builds by <see cref="Validate"/>: a mismatch means the managed
/// declaration drifted from the native layout, which would silently corrupt the policy store.
/// </remarks>
internal static class IPSecurityPolicyLayout
{
    // ===== Negotiation policy actions and types =====

    /// <summary>Filter action: block traffic.</summary>
    internal static readonly Guid ActionBlock = new("3f91a819-7647-11d1-864d-d46a00000000");

    /// <summary>Filter action: negotiate security.</summary>
    internal static readonly Guid ActionNegotiate = new("8a171dd3-77e3-11d1-8659-a04f00000000");

    /// <summary>Filter action: permit traffic.</summary>
    internal static readonly Guid ActionPermit = new("3f91a81a-7647-11d1-864d-d46a00000000");

    /// <summary>
    /// Filter action GUID the reference writer stores for a <em>negotiate</em> action with
    /// "accept unsecured communication, but always respond using IPsec" (<c>inpass</c>) enabled.
    /// The GUID sits in the permit family, yet Windows' own tooling renders the action as
    /// negotiate-with-inpass; the flag is encoded nowhere else in the store.
    /// </summary>
    internal static readonly Guid ActionNegotiateAcceptUnsecuredInbound =
        new("3f91a81a-7647-11d1-864d-d46a00000000");

    /// <summary>
    /// Negotiation policy type GUID the reference writer stores when the action negotiates. A
    /// store created purely by the snap-in carries the <see cref="NegPolTypeStandard"/> GUID
    /// everywhere, but the reference writer emits this sibling when a negotiate action exists,
    /// so it is accepted on read and written for negotiate actions to match.
    /// </summary>
    internal static readonly Guid NegPolTypeNegotiate = new("62f49e10-6c37-11d1-864c-14a300000000");

    /// <summary>The only negotiation policy type the snap-in uses.</summary>
    internal static readonly Guid NegPolTypeStandard = new("62f49e13-6c37-11d1-864c-14a300000000");

    // ===== Main-mode algorithm identifiers =====

    /// <summary>DES encryption.</summary>
    internal const uint EncryptionDes = 1;

    /// <summary>Triple DES encryption — the default main-mode offer.</summary>
    internal const uint EncryptionTripleDes = 3;

    /// <summary>Diffie-Hellman group 1 (768-bit), rendered as <c>Low(1)</c>.</summary>
    internal const uint DiffieHellmanLow = 1;

    /// <summary>Diffie-Hellman group 2 (1024-bit), rendered as <c>Medium(2)</c>.</summary>
    internal const uint DiffieHellmanMedium = 2;

    /// <summary>Diffie-Hellman group 2048-bit (3), rendered as <c>2048</c>.</summary>
    internal const uint DiffieHellmanHigh = 3;

    /// <summary>MD5 integrity.</summary>
    internal const uint HashMd5 = 1;

    /// <summary>SHA-1 integrity — the default main-mode offer.</summary>
    internal const uint HashSha1 = 2;

    /// <summary>Default main-mode key lifetime: 480 minutes, matching secpol.msc.</summary>
    internal const uint DefaultMainModeLifetimeSeconds = 28800;

    // ===== Quick-mode security methods =====

    /// <summary>AH transform.</summary>
    internal const uint TransformAh = 1;

    /// <summary>ESP transform.</summary>
    internal const uint TransformEsp = 2;

    // ===== Authentication methods =====

    /// <summary>Pre-shared key authentication.</summary>
    internal const uint AuthPreSharedKey = 1;

    /// <summary>Certificate (root CA) authentication.</summary>
    internal const uint AuthCertificate = 2;

    /// <summary>Kerberos authentication. Five, not three.</summary>
    internal const uint AuthKerberos = 5;

    // ===== Rule connection types =====

    /// <summary>All network connections. Rendered as <c>ALL</c>; zero renders as <c>NONE</c>.</summary>
    internal const uint InterfaceTypeAll = 0xFFFFFFFD;

    /// <summary>Local area network connections only.</summary>
    internal const uint InterfaceTypeLan = 1;

    /// <summary>Remote access connections only.</summary>
    internal const uint InterfaceTypeDialup = 2;

    // ===== Filter address and port types =====

    /// <summary>Any IP address.</summary>
    internal const uint AddressTypeAny = 0;

    /// <summary>A specific IP address or subnet.</summary>
    internal const uint AddressTypeSpecific = 1;

    /// <summary>This machine's own addresses.</summary>
    internal const uint AddressTypeMe = 8;

    /// <summary>The configured DNS servers.</summary>
    internal const uint AddressTypeDnsServer = 0x10;

    /// <summary>Number of addresses Windows writes per endpoint.</summary>
    internal const uint AddressCountOne = 1;

    /// <summary>Any port.</summary>
    internal const uint PortTypeAny = 0;

    /// <summary>A specific port.</summary>
    internal const uint PortTypeSpecific = 1;

    /// <summary>
    /// Whether the process architecture matches the layouts declared here. Both x64 and ARM64 are
    /// LP64 with identical natural alignment, so one set of declarations serves both; x86 would
    /// need 4-byte pointers and different offsets throughout and is not supported.
    /// </summary>
    internal static bool IsSupportedArchitecture => IntPtr.Size == 8;

    /// <summary>Asserts, in Debug builds, that every managed declaration still matches the native size.</summary>
    [Conditional("DEBUG")]
    internal static void Validate()
    {
        Check<IpsecPolicyData>(96);
        Check<IpsecIsakmpData>(96);
        Check<IpsecMmOffer>(64);
        Check<IpsecNegPolData>(96);
        Check<IpsecSecurityMethod>(80);
        Check<IpsecNfaData>(168);
        Check<IpsecAuthMethod>(40);
        Check<IpsecFilterData>(64);
        Check<IpsecFilterSpec>(152);
        Check<IpsecAddress>(40);
        Check<IpsecPort>(8);
    }

    private static void Check<T>(int expected)
        where T : unmanaged
    {
        int actual = Unsafe.SizeOf<T>();
        Debug.Assert(
            actual == expected,
            $"{typeof(T).Name} must be {expected} bytes to match polstore.dll but is {actual}.");
    }
}
