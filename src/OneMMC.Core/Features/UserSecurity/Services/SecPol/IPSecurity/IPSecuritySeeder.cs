using System.Runtime.InteropServices;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.Native;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Seeds the objects a newly created legacy IPsec policy needs — a main-mode (ISAKMP) object and a
/// default response rule — entirely through the native <c>polstore.dll</c> create APIs.
/// </summary>
/// <remarks>
/// <para>
/// These two objects used to be written as pre-baked serialized <c>ipsecData</c> blobs directly into
/// <c>HKLM\SOFTWARE\Policies\Microsoft\Windows\IPSec\Policy\Local</c>, because the in-memory layout
/// of the main-mode offer and quick-mode security-method arrays was unknown. The layouts are now
/// recovered and declared in <c>IPSecurityPolicyStructs.cs</c>, so the native APIs do the work.
/// </para>
/// <para>
/// The native path is not merely equivalent — it was validated by creating each object and comparing
/// the <c>ipsecData</c> blob polstore persisted with the blob secpol.msc writes: all three
/// (ISAKMP, negotiation policy, NFA) come out byte-identical. polstore additionally maintains
/// <c>ipsecISAKMPReference</c>, <c>ipsecNegotiationPolicyReference</c>, <c>ipsecOwnersReference</c>
/// and <c>ipsecNFAReference</c> itself, which is why no registry patch-up step is needed any more.
/// </para>
/// </remarks>
internal static unsafe class IPSecuritySeeder
{
    /// <summary>
    /// Creates the default response rule (a negotiation policy plus an NFA) for a newly created
    /// policy, matching what the Windows IP Security Policy snap-in creates.
    /// </summary>
    /// <param name="store">An open policy store handle.</param>
    /// <param name="policyIdentifier">The policy that owns the rule.</param>
    /// <param name="active">
    /// Whether the rule is activated. The reference writer maps <c>activatedefaultrule</c> onto the
    /// rule NFA's activation flag; the default response rule has no other representation.
    /// </param>
    /// <exception cref="InvalidOperationException">A native create call failed.</exception>
    internal static void CreateDefaultResponseRule(IntPtr store, Guid policyIdentifier, bool active)
    {
        uint whenChanged = CurrentUnixSeconds();
        Guid negPolIdentifier = Guid.NewGuid();

        IpsecSecurityMethod* methods = stackalloc IpsecSecurityMethod[2];
        methods[0] = default;
        methods[1] = default;

        // ESP with 3DES confidentiality and SHA-1 integrity, then AH with SHA-1 integrity.
        methods[0].PrimaryAlgorithm = IPSecurityPolicyLayout.EncryptionTripleDes;
        methods[0].SecondaryAlgorithm = IPSecurityPolicyLayout.HashSha1;
        methods[0].Transform = IPSecurityPolicyLayout.TransformEsp;
        methods[1].PrimaryAlgorithm = IPSecurityPolicyLayout.HashSha1;
        methods[1].Transform = IPSecurityPolicyLayout.TransformAh;

        IpsecNegPolData negPol = default;
        negPol.NegPolIdentifier = negPolIdentifier;
        negPol.NegPolAction = IPSecurityPolicyLayout.ActionNegotiate;
        negPol.NegPolType = IPSecurityPolicyLayout.NegPolTypeStandard;
        negPol.SecurityMethodCount = 2;
        negPol.SecurityMethods = (IntPtr)methods;
        negPol.WhenChanged = whenChanged;

        int hr = IPSecurityPolicyNativeMethods.CreateNegPolData(store, (IntPtr)(&negPol));
        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"IPSecCreateNegPolData failed for the default response rule with native error 0x{hr:X8}.");
        }

        IpsecAuthMethod auth = default;
        auth.AuthType = IPSecurityPolicyLayout.AuthKerberos;
        IpsecAuthMethod* authPointer = &auth;

        IpsecNfaData nfa = default;
        nfa.NfaIdentifier = Guid.NewGuid();
        nfa.AuthMethodCount = 1;
        nfa.AuthMethods = (IntPtr)(&authPointer);
        nfa.InterfaceType = IPSecurityPolicyLayout.InterfaceTypeAll;
        nfa.ActiveFlag = active ? 1u : 0u;
        nfa.WhenChanged = whenChanged;
        nfa.NegPolIdentifier = negPolIdentifier;

        // FilterIdentifier stays all-zero: that is what makes this the default response rule.
        hr = IPSecurityPolicyNativeMethods.CreateNFAData(store, policyIdentifier, (IntPtr)(&nfa));
        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"IPSecCreateNFAData failed for the default response rule with native error 0x{hr:X8}.");
        }
    }

    private static uint CurrentUnixSeconds() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
