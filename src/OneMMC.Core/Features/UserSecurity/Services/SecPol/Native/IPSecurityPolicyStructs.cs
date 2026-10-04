using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.Native;

/// <summary>
/// Blittable projections of the legacy IPsec policy structures exchanged with
/// <c>polstore.dll</c>. Replaces the hand-computed byte offsets that the executor and store
/// service previously used.
/// </summary>
/// <remarks>
/// <para>
/// None of these types exist in CsWin32 metadata, the Windows SDK headers, or any public
/// Microsoft documentation. The layouts below were recovered on Windows Server 2025 x64 by
/// enumerating the live store through <c>IPSecEnum*Data</c>, measuring every returned allocation
/// with <c>HeapSize</c> (polstore allocates with <c>HeapAlloc(GetProcessHeap(), …)</c>, so
/// <c>HeapSize</c> reports the exact requested size), and confirming each field by creating
/// objects through <c>IPSecCreate*Data</c> and comparing the serialized <c>ipsecData</c> blob
/// polstore persisted against the blobs Windows' own tooling writes.
/// </para>
/// <para>
/// Every struct is blittable (no <c>[MarshalAs]</c>, no string or bool fields), so it works under
/// Native AOT with zero marshalling. Reserved fields are named <c>_reservedNN</c> after the byte
/// offset they occupy and must stay zero: Windows writes zero there, and reproducing that is what
/// makes the persisted blobs byte-identical.
/// </para>
/// <para>
/// The layouts are identical on x64 and ARM64 — both are LP64, so pointers are 8 bytes and every
/// natural alignment matches. They are <em>not</em> valid for x86; see
/// <see cref="IPSecurityPolicyLayout.IsSupportedArchitecture"/>.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecPolicyData
{
    /// <summary>Identifier of the policy; becomes the <c>ipsecPolicy{…}</c> key name.</summary>
    public Guid PolicyIdentifier;

    /// <summary>Polling interval in seconds (the UI presents minutes).</summary>
    public uint PollingIntervalSeconds;

    private uint _reserved20;

    /// <summary>Store-owned <see cref="IpsecIsakmpData"/> the policy references.</summary>
    public IntPtr IsakmpData;

    /// <summary>Store-owned array of <see cref="IpsecNfaData"/> pointers; not filled by enumeration.</summary>
    public IntPtr NfaDataArray;

    /// <summary>Number of entries in <see cref="NfaDataArray"/>.</summary>
    public uint NfaCount;

    /// <summary>Last modification time as Unix seconds.</summary>
    public uint WhenChanged;

    /// <summary>UTF-16 display name; persisted as the <c>ipsecName</c> registry value.</summary>
    public IntPtr Name;

    /// <summary>UTF-16 description; persisted as the <c>description</c> registry value.</summary>
    public IntPtr Description;

    /// <summary>Identifier of the referenced main-mode object.</summary>
    public Guid IsakmpIdentifier;

    private long _reserved80;
    private long _reserved88;
}

/// <summary>
/// Main-mode (ISAKMP) policy object. 96 bytes; verified by allocation measurement.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecIsakmpData
{
    /// <summary>Identifier of the main-mode object; becomes the <c>ipsecISAKMPPolicy{…}</c> key name.</summary>
    public Guid IsakmpIdentifier;

    /// <summary>
    /// Leading GUID of the serialized payload. Windows writes the identifier again here; the value
    /// is not surfaced anywhere in the snap-in, so it is mirrored to match byte-identical blobs.
    /// </summary>
    public Guid PayloadIdentifier;

    private uint _reserved32;

    /// <summary>Master-key perfect forward secrecy; 1 = enabled.</summary>
    public uint MasterPfsEnabled;

    private uint _reserved40;
    private uint _reserved44;

    /// <summary>
    /// Quick-mode sessions per main-mode session. Zero means unlimited; netsh forces 1 when master
    /// PFS is enabled. Rendered by netsh as <c>N Quick Mode sessions</c>.
    /// </summary>
    public uint QuickModeSessionsPerMainMode;

    /// <summary>Main-mode key lifetime in seconds (secpol.msc shows this in minutes).</summary>
    public uint MainModeLifetimeSeconds;

    private uint _reserved56;
    private uint _reserved60;
    private uint _reserved64;
    private uint _reserved68;
    private uint _reserved72;

    /// <summary>Number of <see cref="IpsecMmOffer"/> entries in <see cref="Offers"/>.</summary>
    public uint OfferCount;

    /// <summary>Contiguous array of <see cref="IpsecMmOffer"/> (not an array of pointers).</summary>
    public IntPtr Offers;

    /// <summary>Last modification time as Unix seconds.</summary>
    public uint WhenChanged;

    private uint _reserved92;
}

/// <summary>
/// One main-mode security-method offer. 64 bytes, proven by the offer-array allocation size
/// growing 64 → 128 between a one-offer and a two-offer object.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecMmOffer
{
    private uint _reserved0;

    /// <summary>Encryption algorithm; 3 renders as <c>3DES</c> in <c>netsh ipsec static show</c>.</summary>
    public uint EncryptionAlgorithm;

    private uint _reserved8;
    private uint _reserved12;

    /// <summary>Integrity algorithm; 2 renders as <c>SHA1</c>.</summary>
    public uint HashAlgorithm;

    private uint _reserved20;
    private uint _reserved24;
    private uint _reserved28;
    private uint _reserved32;
    private uint _reserved36;
    private uint _reserved40;

    /// <summary>Diffie-Hellman group; 2 renders as <c>Medium(2)</c>.</summary>
    public uint DiffieHellmanGroup;

    private uint _reserved48;
    private uint _reserved52;

    /// <summary>Offer key lifetime in seconds.</summary>
    public uint LifetimeSeconds;

    private uint _reserved60;
}

/// <summary>
/// Negotiation policy — what the snap-in calls a filter action. 96 bytes, not the 88 bytes the
/// field extent suggests; the trailing slot must exist or polstore reads past the allocation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecNegPolData
{
    /// <summary>Identifier; becomes the <c>ipsecNegotiationPolicy{…}</c> key name.</summary>
    public Guid NegPolIdentifier;

    /// <summary>Block, permit or negotiate; see <see cref="IPSecurityPolicyLayout"/>.</summary>
    public Guid NegPolAction;

    /// <summary>Negotiation policy type; always the standard type for the snap-in.</summary>
    public Guid NegPolType;

    /// <summary>Number of <see cref="IpsecSecurityMethod"/> entries in <see cref="SecurityMethods"/>.</summary>
    public uint SecurityMethodCount;

    private uint _reserved52;

    /// <summary>Contiguous array of <see cref="IpsecSecurityMethod"/> (not an array of pointers).</summary>
    public IntPtr SecurityMethods;

    /// <summary>Last modification time as Unix seconds.</summary>
    public uint WhenChanged;

    private uint _reserved68;

    /// <summary>UTF-16 display name; persisted as <c>ipsecName</c>. Null for rule-owned actions.</summary>
    public IntPtr Name;

    /// <summary>UTF-16 description; persisted as <c>description</c>.</summary>
    public IntPtr Description;

    private long _reserved88;
}

/// <summary>
/// One quick-mode security method. 80 bytes, proven by the array allocation being exactly
/// <c>80 × SecurityMethodCount</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecSecurityMethod
{
    /// <summary>Quick-mode lifetime in seconds; zero when no explicit lifetime was authored.</summary>
    public uint LifetimeSeconds;

    /// <summary>Quick-mode lifetime in kilobytes; zero when no explicit lifetime was authored.</summary>
    public uint LifetimeKilobytes;

    private uint _reserved8;

    /// <summary>
    /// Quick-mode perfect forward secrecy flag. Only the <em>first</em> method's value is read by
    /// Windows' tooling; the reference writer stores the flag there and leaves it zero elsewhere.
    /// </summary>
    public uint QuickModePfsEnabled;

    /// <summary>Number of algorithm entries carried by this method; Windows writes 1.</summary>
    public uint AlgorithmCount;

    /// <summary>
    /// Primary algorithm. For an ESP transform this is the confidentiality algorithm (3 = 3DES);
    /// for an AH transform it is the integrity algorithm (2 = SHA1).
    /// </summary>
    public uint PrimaryAlgorithm;

    /// <summary>Secondary algorithm: ESP integrity (2 = SHA1), or 0 when unused.</summary>
    public uint SecondaryAlgorithm;

    /// <summary>Transform kind: 1 = AH, 2 = ESP.</summary>
    public uint Transform;

    private uint _reserved32;
    private uint _reserved36;
    private uint _reserved40;
    private uint _reserved44;
    private uint _reserved48;
    private uint _reserved52;
    private uint _reserved56;
    private uint _reserved60;
    private uint _reserved64;
    private uint _reserved68;
    private uint _reserved72;
    private uint _reserved76;
}

/// <summary>
/// Negotiation filter association — what the snap-in calls a rule. 168 bytes, and its field order
/// is nothing like the layout previously assumed: the name pointer leads the struct and the
/// identifier follows it, the authentication methods are an array of <em>pointers</em>, and the
/// negotiation-policy and filter identifiers live near the end.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecNfaData
{
    /// <summary>UTF-16 rule name; persisted as <c>ipsecName</c>. Null for an unnamed rule.</summary>
    public IntPtr Name;

    /// <summary>Identifier; becomes the <c>ipsecNFA{…}</c> key name.</summary>
    public Guid NfaIdentifier;

    /// <summary>Number of entries in <see cref="AuthMethods"/>.</summary>
    public uint AuthMethodCount;

    private uint _reserved28;

    /// <summary>Array of <see cref="IpsecAuthMethod"/> <em>pointers</em>, one per method.</summary>
    public IntPtr AuthMethods;

    /// <summary>
    /// Connection type. <see cref="IPSecurityPolicyLayout.InterfaceTypeAll"/> renders as
    /// <c>ALL</c>; 0 renders as <c>NONE</c>.
    /// </summary>
    public uint InterfaceType;

    private uint _reserved44;
    private IntPtr _reserved48;
    private IntPtr _reserved56;
    private IntPtr _reserved64;
    private uint _reserved72;
    private uint _reserved76;

    /// <summary>Non-zero when the rule is activated.</summary>
    public uint ActiveFlag;

    private uint _reserved84;
    private IntPtr _reserved88;
    private IntPtr _reserved96;
    private IntPtr _reserved104;

    /// <summary>Last modification time as Unix seconds.</summary>
    public uint WhenChanged;

    /// <summary>Filter action this rule applies.</summary>
    public Guid NegPolIdentifier;

    /// <summary>Filter list this rule matches; all-zero for a default response rule.</summary>
    public Guid FilterIdentifier;

    private uint _reserved148;

    /// <summary>UTF-16 description; persisted as <c>description</c>.</summary>
    public IntPtr Description;

    private long _reserved160;
}

/// <summary>
/// One authentication method. 40 bytes, measured directly on a store-owned instance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecAuthMethod
{
    /// <summary>
    /// Authentication kind. Kerberos is <see cref="IPSecurityPolicyLayout.AuthKerberos"/> (5) —
    /// not 3, which the previous hand-written layout assumed.
    /// </summary>
    public uint AuthType;

    private uint _reserved4;

    /// <summary>UTF-16 pre-shared key or root-CA distinguished name; empty string when unused.</summary>
    public IntPtr AuthMethodValue;

    private IntPtr _reserved16;
    private IntPtr _reserved24;
    private IntPtr _reserved32;
}

/// <summary>Filter list. 64 bytes, measured on a store-owned instance.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecFilterData
{
    /// <summary>Identifier; becomes the <c>ipsecFilter{…}</c> key name.</summary>
    public Guid FilterIdentifier;

    /// <summary>Number of entries in <see cref="FilterSpecs"/>.</summary>
    public uint FilterSpecCount;

    private uint _reserved20;

    /// <summary>Array of <see cref="IpsecFilterSpec"/> <em>pointers</em>, one per filter.</summary>
    public IntPtr FilterSpecs;

    /// <summary>Last modification time as Unix seconds.</summary>
    public uint WhenChanged;

    private uint _reserved36;

    /// <summary>UTF-16 display name; persisted as <c>ipsecName</c>.</summary>
    public IntPtr Name;

    /// <summary>UTF-16 description; persisted as <c>description</c>.</summary>
    public IntPtr Description;

    private long _reserved56;
}

/// <summary>
/// One address/port endpoint inside a filter. 40 bytes; the stride was proven by the source and
/// destination blocks sitting exactly 40 bytes apart inside a filter authored by Windows itself.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecAddress
{
    /// <summary>
    /// One of <see cref="IPSecurityPolicyLayout.AddressTypeAny"/>,
    /// <see cref="IPSecurityPolicyLayout.AddressTypeSpecific"/>,
    /// <see cref="IPSecurityPolicyLayout.AddressTypeMe"/> or
    /// <see cref="IPSecurityPolicyLayout.AddressTypeDnsServer"/>.
    /// </summary>
    public uint AddressType;

    /// <summary>Number of addresses this endpoint covers; Windows always writes 1.</summary>
    public uint AddressCount;

    /// <summary>IPv4 address in network byte order (1.2.3.4 is stored as bytes 01 02 03 04).</summary>
    public uint IpAddress;

    /// <summary>IPv4 subnet mask in network byte order; zero for a single host.</summary>
    public uint SubnetMask;

    // Reserved as six DWORDs rather than three QWORDs on purpose: the native struct is 4-byte
    // aligned and sits at offset 44 inside IPSEC_FILTER_SPEC, which 8-byte members would break.
    private uint _reserved16;
    private uint _reserved20;
    private uint _reserved24;
    private uint _reserved28;
    private uint _reserved32;
    private uint _reserved36;
}

/// <summary>One port endpoint inside a filter. 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecPort
{
    /// <summary>0 for any port, 1 when <see cref="Port"/> is meaningful.</summary>
    public uint PortType;

    /// <summary>Port number in host order.</summary>
    public uint Port;
}

/// <summary>One filter inside a filter list. 152 bytes, measured on a store-owned instance.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IpsecFilterSpec
{
    /// <summary>UTF-16 source DNS name, or null.</summary>
    public IntPtr SourceDnsName;

    /// <summary>UTF-16 destination DNS name, or null.</summary>
    public IntPtr DestinationDnsName;

    /// <summary>UTF-16 description, or null.</summary>
    public IntPtr Description;

    /// <summary>Identifier of this filter.</summary>
    public Guid FilterSpecGuid;

    /// <summary>Non-zero when the filter is mirrored.</summary>
    public uint MirrorFlag;

    /// <summary>Source endpoint.</summary>
    public IpsecAddress SourceAddress;

    /// <summary>Destination endpoint.</summary>
    public IpsecAddress DestinationAddress;

    /// <summary>Source port.</summary>
    public IpsecPort SourcePort;

    /// <summary>Destination port.</summary>
    public IpsecPort DestinationPort;

    /// <summary>IP protocol number; 0 means any.</summary>
    public uint Protocol;

    private uint _reserved144;
    private uint _reserved148;
}
