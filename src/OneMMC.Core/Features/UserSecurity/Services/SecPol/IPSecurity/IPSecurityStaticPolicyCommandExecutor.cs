using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes validated mutation commands against the legacy static local IPsec policy store
/// using the native <c>polstore.dll</c> struct-based create, set, and delete APIs.
/// </summary>
public sealed class IPSecurityStaticPolicyCommandExecutor
{
    private static readonly HashSet<string> AllowedVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "add", "set", "delete" };

    private static readonly HashSet<string> AllowedObjectKinds =
        new(StringComparer.OrdinalIgnoreCase) { "policy", "filterlist", "filter", "filteraction", "rule" };

    private readonly ILogger<IPSecurityStaticPolicyCommandExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyCommandExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public IPSecurityStaticPolicyCommandExecutor(
        ILogger<IPSecurityStaticPolicyCommandExecutor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes a validated legacy static IPsec mutation command via the native struct-based APIs.
    /// </summary>
    internal Task ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCommand(arguments);
        IPSecurityPolicyLayout.Validate();

        if (!IPSecurityPolicyLayout.IsSupportedArchitecture)
        {
            throw new PlatformNotSupportedException(
                "The legacy IPsec policy store is only supported on 64-bit processes.");
        }

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr store, out int errorCode))
        {
            ThrowOpenStoreFailure(errorCode);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCore(store, arguments);
        }
        catch (UnauthorizedAccessException)
        {
            LogFailure(arguments);
            throw;
        }
        catch (InvalidOperationException)
        {
            LogFailure(arguments);
            throw;
        }
        finally
        {
            IPSecurityPolicyNativeMethods.CloseStore(store);
        }

        return Task.CompletedTask;
    }

    private static void ExecuteCore(IntPtr store, IReadOnlyList<string> arguments)
    {
        var parameters = ParseParameters(arguments, startIndex: 4);

        switch ((arguments[2].ToLowerInvariant(), arguments[3].ToLowerInvariant()))
        {
            case ("add", "policy"):          AddPolicy(store, parameters); break;
            case ("set", "policy"):          SetPolicy(store, parameters); break;
            case ("delete", "policy"):       DeletePolicy(store, parameters); break;
            case ("add", "filterlist"):      AddFilterList(store, parameters); break;
            case ("set", "filterlist"):      SetFilterList(store, parameters); break;
            case ("delete", "filterlist"):   DeleteByName(store, parameters, IPSecurityPolicyNativeMethods.EnumFilterData, IPSecurityPolicyNativeMethods.DeleteFilterData, 40, "filterlist"); break;
            case ("add", "filter"):          AddFilter(store, parameters); break;
            case ("delete", "filter"):       DeleteFilter(store, parameters); break;
            case ("add", "filteraction"):    AddFilterAction(store, parameters); break;
            case ("set", "filteraction"):    SetFilterAction(store, parameters); break;
            case ("delete", "filteraction"): DeleteByName(store, parameters, IPSecurityPolicyNativeMethods.EnumNegPolData, IPSecurityPolicyNativeMethods.DeleteNegPolData, 72, "filteraction"); break;
            case ("add", "rule"):            AddRule(store, parameters, arguments); break;
            case ("set", "rule"):            SetRule(store, parameters, arguments); break;
            case ("delete", "rule"):         DeleteRule(store, parameters); break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported IPsec operation: {arguments[2]} {arguments[3]}.");
        }
    }

    // ===== Policy Operations =====

    /// <remarks>
    /// See <see cref="IpsecPolicyData"/> for the layout. A policy is only enumerable once it owns at
    /// least one rule, which is why the default response rule is created straight afterwards.
    /// Main-mode settings (mmsecmethods, mmpfs, qmpermm, mmlifetime) are written to a dedicated
    /// per-policy ISAKMP object, matching the reference writer: Windows' own tools never share one
    /// ISAKMP between policies.
    /// </remarks>
    private static void AddPolicy(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string description = GetOptional(parameters, "description") ?? string.Empty;
        int pollingMinutes = GetOptionalInt(parameters, "pollinginterval") ?? 180;
        bool assign = GetOptionalBool(parameters, "assign") ?? false;

        MainModeSettings mainMode = ReadMainModeSettings(parameters);
        Guid policyGuid = Guid.NewGuid();
        Guid isakmpGuid = CreateMainModeObject(store, mainMode);

        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        IntPtr pIsakmp = FindIsakmpPointer(store, isakmpGuid);
        try
        {
            IpsecPolicyData data = default;
            data.PolicyIdentifier = policyGuid;
            data.PollingIntervalSeconds = (uint)(pollingMinutes * 60);
            data.IsakmpData = pIsakmp;
            data.WhenChanged = (uint)CurrentUnixSeconds();
            data.Name = pName;
            data.Description = pDesc;
            data.IsakmpIdentifier = isakmpGuid;

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.CreatePolicyData(store, (IntPtr)(&data)); }
            if (hr != 0)
            {
                throw new InvalidOperationException(
                    $"IPSecCreatePolicyData failed with native error 0x{hr:X8}. " +
                    $"Policy GUID: {policyGuid}, ISAKMP GUID: {isakmpGuid}.");
            }
        }
        finally
        {
            FreeIfNotZero(pDesc);
            FreeIfNotZero(pName);
        }

        // Native tools (the secpol.msc wizard, netsh) always create this rule, and polstore only
        // reports a policy through IPSecEnumPolicyData once it has one. Creating it also makes
        // polstore write the policy's ipsecNFAReference value, so no registry patch-up is needed.
        IPSecurityStaticPolicySeeder.CreateDefaultResponseRule(
            store, policyGuid, mainMode.IsDefaultResponseRuleActive);

        // Repair step for stores left inconsistent by earlier builds of this app, which created
        // ipsecPolicy keys through the registry and could leave ones polstore cannot parse.
        CleanOrphanedPolicyRegistryKeys(store);

        if (assign)
        {
            IPSecurityPolicyNativeMethods.AssignPolicy(store, policyGuid);
        }
    }

    private static void SetPolicy(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");
        int? pollingMinutes = GetOptionalInt(parameters, "pollinginterval");
        bool? assign = GetOptionalBool(parameters, "assign");

        (Guid policyGuid, IntPtr original) = FindPolicyByName(store, name);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Policy '{name}' not found.");
        }

        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        try
        {
            IpsecPolicyData data;
            unsafe { data = *(IpsecPolicyData*)original; }

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                data.Name = pNewName;
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                data.Description = pNewDesc;
            }

            if (pollingMinutes is not null)
            {
                data.PollingIntervalSeconds = (uint)(pollingMinutes.Value * 60);
            }

            data.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetPolicyData(store, (IntPtr)(&data)); }
            ThrowOnError(hr, "set policy");
        }
        finally
        {
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
        }

        // Main-mode settings mutate the policy's referenced ISAKMP object (matching the reference
        // writer, which never leaves these on the policy itself), then the default response rule's
        // activation when requested.
        if (TryReadMainModeSettings(parameters) is { } mainMode)
        {
            UpdatePolicyMainMode(store, policyGuid, mainMode);
        }

        bool? defaultRuleActive = GetOptionalBool(parameters, "activatedefaultrule");
        if (defaultRuleActive is not null)
        {
            SetDefaultResponseRuleActivation(store, policyGuid, defaultRuleActive.Value);
        }

        if (assign is true)
        {
            IPSecurityPolicyNativeMethods.AssignPolicy(store, policyGuid);
        }
        else if (assign is false)
        {
            IgnoreUnassignFailure(store, policyGuid);
        }
    }

    // ===== Main-mode (ISAKMP) plumbing =====

    /// <summary>Main-mode policy settings carried by an add/set policy command.</summary>
    private readonly struct MainModeSettings
    {
        internal uint MasterPfsEnabled { get; init; }

        internal uint QuickModeSessionsPerMainMode { get; init; }

        internal uint MainModeLifetimeSeconds { get; init; }

        internal bool HasOfferOverrides { get; init; }

        internal IReadOnlyList<IpsecMmOffer> Offers { get; init; }

        internal bool IsDefaultResponseRuleActive { get; init; }
    }

    private static MainModeSettings ReadMainModeSettings(Dictionary<string, string> parameters)
    {
        bool masterPfs = GetOptionalBool(parameters, "mmpfs") ?? false;
        bool defaultRule = GetOptionalBool(parameters, "activatedefaultrule") ?? false;
        bool hasMethods = GetOptional(parameters, "mmsecmethods") is not null;

        // The reference writer forces qmpermm to 1 when mmpfs is enabled; mirror that so the store
        // never holds a combination Windows' own tools would not produce.
        uint qmPerMm = (uint)(GetOptionalInt(parameters, "qmpermm") ?? 0);
        if (masterPfs)
        {
            qmPerMm = 1;
        }

        uint lifetimeSeconds = (uint)((GetOptionalInt(parameters, "mmlifetime")
            ?? (int)(IPSecurityPolicyLayout.DefaultMainModeLifetimeSeconds / 60)) * 60);

        return new MainModeSettings
        {
            MasterPfsEnabled = masterPfs ? 1u : 0u,
            QuickModeSessionsPerMainMode = qmPerMm,
            MainModeLifetimeSeconds = lifetimeSeconds,
            HasOfferOverrides = hasMethods,
            Offers = hasMethods
                ? ParseMainModeOffers(GetRequired(parameters, "mmsecmethods"), lifetimeSeconds)
                : [],
            IsDefaultResponseRuleActive = defaultRule
        };
    }

    private static MainModeSettings? TryReadMainModeSettings(Dictionary<string, string> parameters)
    {
        bool any =
            GetOptionalBool(parameters, "mmpfs") is not null
            || GetOptionalInt(parameters, "qmpermm") is not null
            || GetOptionalInt(parameters, "mmlifetime") is not null
            || GetOptional(parameters, "mmsecmethods") is not null;
        return any ? ReadMainModeSettings(parameters) : null;
    }

    /// <summary>
    /// Creates the per-policy ISAKMP object the command describes, matching the reference writer:
    /// one dedicated main-mode object per policy rather than a shared default.
    /// </summary>
    private static Guid CreateMainModeObject(IntPtr store, MainModeSettings settings)
    {
        Guid identifier = Guid.NewGuid();

        IpsecMmOffer[] offers = settings.HasOfferOverrides && settings.Offers.Count > 0
            ? [.. settings.Offers]
            :
            [
                new IpsecMmOffer
                {
                    EncryptionAlgorithm = IPSecurityPolicyLayout.EncryptionTripleDes,
                    HashAlgorithm = IPSecurityPolicyLayout.HashSha1,
                    DiffieHellmanGroup = IPSecurityPolicyLayout.DiffieHellmanMedium,
                    LifetimeSeconds = settings.MainModeLifetimeSeconds
                }
            ];

        int offerSize = Unsafe.SizeOf<IpsecMmOffer>();
        IntPtr pOffers = Marshal.AllocHGlobal(offerSize * offers.Length);
        try
        {
            unsafe
            {
                for (int index = 0; index < offers.Length; index++)
                {
                    *(IpsecMmOffer*)(pOffers + (offerSize * index)) = offers[index];
                }
            }

            IpsecIsakmpData isakmp = default;
            isakmp.IsakmpIdentifier = identifier;
            isakmp.PayloadIdentifier = identifier;
            isakmp.MasterPfsEnabled = settings.MasterPfsEnabled;
            isakmp.QuickModeSessionsPerMainMode = settings.QuickModeSessionsPerMainMode;
            isakmp.MainModeLifetimeSeconds = settings.MainModeLifetimeSeconds;
            isakmp.OfferCount = (uint)offers.Length;
            isakmp.Offers = pOffers;
            isakmp.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.CreateISAKMPData(store, (IntPtr)(&isakmp)); }
            ThrowOnError(hr, "create main mode policy");
            return identifier;
        }
        finally
        {
            Marshal.FreeHGlobal(pOffers);
        }
    }

    /// <summary>
    /// Mutates the ISAKMP object a policy references, copying the store-owned object first so the
    /// update works on a caller-owned buffer like every other set operation.
    /// </summary>
    private static void UpdatePolicyMainMode(IntPtr store, Guid policyGuid, MainModeSettings settings)
    {
        Guid isakmpId = FindPolicyIsakmpReference(store, policyGuid);
        if (isakmpId == Guid.Empty)
        {
            isakmpId = CreateMainModeObject(store, settings);
            RetargetPolicyIsakmp(store, policyGuid, isakmpId);
            return;
        }

        IpsecMmOffer[]? offers = settings.HasOfferOverrides && settings.Offers.Count > 0
            ? [.. settings.Offers]
            : null;

        int offerSize = Unsafe.SizeOf<IpsecMmOffer>();
        int offerCount = offers?.Length ?? 0;
        IntPtr pOffers = offers is not null && offerCount > 0
            ? Marshal.AllocHGlobal(offerSize * offerCount)
            : IntPtr.Zero;
        IntPtr offersBufferToFree = IntPtr.Zero;
        try
        {
            if (offers is not null)
            {
                unsafe
                {
                    for (int index = 0; index < offerCount; index++)
                    {
                        *(IpsecMmOffer*)(pOffers + (offerSize * index)) = offers[index];
                    }
                }
            }

            IpsecIsakmpData data;
            IntPtr pExisting = FindIsakmpPointer(store, isakmpId);
            if (pExisting == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "The policy's main mode policy object could not be found in the store.");
            }

            unsafe { data = *(IpsecIsakmpData*)pExisting; }

            data.MasterPfsEnabled = settings.MasterPfsEnabled;
            data.QuickModeSessionsPerMainMode = settings.QuickModeSessionsPerMainMode;
            uint targetLifetime = settings.MainModeLifetimeSeconds;
            bool lifetimeChanged = targetLifetime != data.MainModeLifetimeSeconds;
            data.MainModeLifetimeSeconds = targetLifetime;
            if (pOffers != IntPtr.Zero)
            {
                data.OfferCount = (uint)offerCount;
                data.Offers = pOffers;
            }
            else if (lifetimeChanged)
            {
                // Keep the existing offers but rewrite their lifetimes so the pair stays
                // consistent, as the reference writer does.
                RewriteOfferLifetimes(ref data, targetLifetime, out IntPtr pRewritten);
                offersBufferToFree = pRewritten;
            }

            data.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetISAKMPData(store, (IntPtr)(&data)); }
            ThrowOnError(hr, "set main mode policy");
        }
        finally
        {
            FreeIfNotZero(offersBufferToFree);
            FreeIfNotZero(pOffers);
        }
    }

    /// <summary>
    /// Clones the store-owned offer array with a new lifetime so the caller can keep using store
    /// offers while updating only <c>mmlifetime</c>.
    /// </summary>
    /// <param name="data">The ISAKMP struct copy whose offers should be replaced.</param>
    /// <param name="lifetimeSeconds">The new lifetime written to every offer.</param>
    /// <param name="pRewritten">The cloned offer array; the caller frees it after the set call.</param>
    private static void RewriteOfferLifetimes(
        ref IpsecIsakmpData data, uint lifetimeSeconds, out IntPtr pRewritten)
    {
        int count = (int)data.OfferCount;
        if (count <= 0 || data.Offers == IntPtr.Zero)
        {
            pRewritten = IntPtr.Zero;
            return;
        }

        int offerSize = Unsafe.SizeOf<IpsecMmOffer>();
        pRewritten = Marshal.AllocHGlobal(offerSize * count);
        unsafe
        {
            for (int index = 0; index < count; index++)
            {
                IpsecMmOffer offer = *(IpsecMmOffer*)(data.Offers + (offerSize * index));
                offer.LifetimeSeconds = lifetimeSeconds;
                *(IpsecMmOffer*)(pRewritten + (offerSize * index)) = offer;
            }
        }

        data.Offers = pRewritten;
    }

    private static Guid FindPolicyIsakmpReference(IntPtr store, Guid policyGuid)
    {
        Guid found = Guid.Empty;
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteIntPtr(ppp, IntPtr.Zero);
            Marshal.WriteInt32(pCount, 0);
            if (IPSecurityPolicyNativeMethods.EnumPolicyData(store, ppp, pCount) != 0)
            {
                return Guid.Empty;
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr array = Marshal.ReadIntPtr(ppp);
            for (int index = 0; index < count && array != IntPtr.Zero; index++)
            {
                IntPtr p = Marshal.ReadIntPtr(array, IntPtr.Size * index);
                if (p == IntPtr.Zero)
                {
                    continue;
                }

                if (ReadGuid(p, 0) == policyGuid)
                {
                    found = ReadGuid(p, 64);
                    break;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }

        return found;
    }

    private static void RetargetPolicyIsakmp(IntPtr store, Guid policyGuid, Guid isakmpGuid)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteIntPtr(ppp, IntPtr.Zero);
            Marshal.WriteInt32(pCount, 0);
            if (IPSecurityPolicyNativeMethods.EnumPolicyData(store, ppp, pCount) != 0)
            {
                throw new InvalidOperationException("The policy could not be enumerated for update.");
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr array = Marshal.ReadIntPtr(ppp);
            for (int index = 0; index < count && array != IntPtr.Zero; index++)
            {
                IntPtr p = Marshal.ReadIntPtr(array, IntPtr.Size * index);
                if (p == IntPtr.Zero || ReadGuid(p, 0) != policyGuid)
                {
                    continue;
                }

                IpsecPolicyData data;
                unsafe { data = *(IpsecPolicyData*)p; }
                data.IsakmpIdentifier = isakmpGuid;
                data.IsakmpData = FindIsakmpPointer(store, isakmpGuid);
                data.WhenChanged = (uint)CurrentUnixSeconds();

                int hr;
                unsafe { hr = IPSecurityPolicyNativeMethods.SetPolicyData(store, (IntPtr)(&data)); }
                ThrowOnError(hr, "set policy");
                return;
            }

            throw new InvalidOperationException($"Policy '{policyGuid}' not found.");
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    /// <summary>Returns the store-owned pointer to the ISAKMP object with the given identifier.</summary>
    private static IntPtr FindIsakmpPointer(IntPtr store, Guid isakmpGuid)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteIntPtr(ppp, IntPtr.Zero);
            Marshal.WriteInt32(pCount, 0);
            if (IPSecurityPolicyNativeMethods.EnumISAKMPData(store, ppp, pCount) != 0)
            {
                return IntPtr.Zero;
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr array = Marshal.ReadIntPtr(ppp);
            for (int index = 0; index < count && array != IntPtr.Zero; index++)
            {
                IntPtr p = Marshal.ReadIntPtr(array, IntPtr.Size * index);
                if (p == IntPtr.Zero)
                {
                    continue;
                }

                if (ReadGuid(p, 0) == isakmpGuid)
                {
                    return p;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    /// <summary>Activates or deactivates a policy's default response rule.</summary>
    private static void SetDefaultResponseRuleActivation(IntPtr store, Guid policyGuid, bool active)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteIntPtr(ppp, IntPtr.Zero);
            Marshal.WriteInt32(pCount, 0);
            if (IPSecurityPolicyNativeMethods.EnumNFAData(store, policyGuid, ppp, pCount) != 0)
            {
                return;
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr array = Marshal.ReadIntPtr(ppp);
            for (int index = 0; index < count && array != IntPtr.Zero; index++)
            {
                IntPtr p = Marshal.ReadIntPtr(array, IntPtr.Size * index);
                if (p == IntPtr.Zero)
                {
                    continue;
                }

                IpsecNfaData nfa;
                unsafe { nfa = *(IpsecNfaData*)p; }

                // The default response rule is the NFA with an all-zero filter reference.
                if (nfa.FilterIdentifier == Guid.Empty)
                {
                    nfa.ActiveFlag = active ? 1u : 0u;
                    nfa.WhenChanged = (uint)CurrentUnixSeconds();

                    int hr;
                    unsafe { hr = IPSecurityPolicyNativeMethods.SetNFAData(store, policyGuid, (IntPtr)(&nfa)); }
                    ThrowOnError(hr, "set default response rule");
                    return;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    /// <summary>
    /// Parses netsh-style main-mode security methods (<c>Conf-Hash-Group</c>, space separated).
    /// Each offer inherits the command's main-mode lifetime, matching the reference writer, which
    /// keeps the ISAKMP lifetime and its offers' lifetimes consistent.
    /// </summary>
    private static IReadOnlyList<IpsecMmOffer> ParseMainModeOffers(string spec, uint lifetimeSeconds)
    {
        List<IpsecMmOffer> offers = [];
        foreach (string term in spec.Split([' ', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = term.Split('-');
            if (parts.Length != 3)
            {
                continue;
            }

            uint encryption = ParseMainModeAlgorithm(parts[0], isConfidentiality: true);
            uint hash = ParseMainModeAlgorithm(parts[1], isConfidentiality: false);
            if (encryption == 0 || hash == 0 || !uint.TryParse(parts[2], out uint group))
            {
                continue;
            }

            offers.Add(new IpsecMmOffer
            {
                EncryptionAlgorithm = encryption,
                HashAlgorithm = hash,
                DiffieHellmanGroup = group,
                LifetimeSeconds = lifetimeSeconds
            });
        }

        return offers;
    }

    private static uint ParseMainModeAlgorithm(string name, bool isConfidentiality)
    {
        return (isConfidentiality ? name : name) switch
        {
            "DES" => IPSecurityPolicyLayout.EncryptionDes,
            "3DES" => IPSecurityPolicyLayout.EncryptionTripleDes,
            "MD5" => IPSecurityPolicyLayout.HashMd5,
            "SHA1" => IPSecurityPolicyLayout.HashSha1,
            _ => 0
        };
    }

    private static void DeletePolicy(IntPtr store, Dictionary<string, string> parameters)
    {
        string policyName = GetRequired(parameters, "name");
        (Guid policyId, IntPtr policyPtr) = FindPolicyByName(store, policyName);
        if (policyId == Guid.Empty)
        {
            return;
        }

        IgnoreUnassignFailure(store, policyId);
        DeleteAllRules(store, policyId);
        ThrowOnError(IPSecurityPolicyNativeMethods.DeletePolicyData(store, policyPtr), "delete policy");
    }

    private static void DeleteAllRules(IntPtr store, Guid policyId)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumNFAData(store, policyId, ppp, pCount);
            if (hr != 0)
            {
                // ERROR_NO_DATA (0xE8) means the policy has no NFA references.
                if (hr == 0xE8) return;
                ThrowOnError(hr, "enumerate policy rules");
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                DeleteRuleAndOwnedAction(store, policyId, p);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static void IgnoreUnassignFailure(IntPtr store, Guid policyId)
    {
        int hr = IPSecurityPolicyNativeMethods.UnassignPolicy(store, policyId);
        if (hr != 0 && (hr == 5 || hr == unchecked((int)0x80070005)))
        {
            ThrowOnError(hr, "unassign policy");
        }
    }

    // ===== FilterList Operations =====

    /// <remarks>See <see cref="IpsecFilterData"/> for the layout.</remarks>
    private static void AddFilterList(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? description = GetOptional(parameters, "description");

        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        try
        {
            IpsecFilterData data = default;
            data.FilterIdentifier = Guid.NewGuid();
            data.WhenChanged = (uint)CurrentUnixSeconds();
            data.Name = pName;
            data.Description = pDesc;

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.CreateFilterData(store, (IntPtr)(&data)); }
            ThrowOnError(hr, "add filterlist");
        }
        finally
        {
            FreeIfNotZero(pDesc);
            FreeIfNotZero(pName);
        }
    }

    private static void SetFilterList(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");

        (_, IntPtr original) = FindByName(store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, name);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter list '{name}' not found.");
        }

        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        try
        {
            IpsecFilterData data;
            unsafe { data = *(IpsecFilterData*)original; }

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                data.Name = pNewName;
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                data.Description = pNewDesc;
            }

            data.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetFilterData(store, (IntPtr)(&data)); }
            ThrowOnError(hr, "set filterlist");
        }
        finally
        {
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
        }
    }

    // ===== Filter Operations =====

    /// <remarks>
    /// See <see cref="IpsecFilterSpec"/>. The filter list's spec array is an array of pointers, so
    /// adding a filter means building a new pointer array that keeps the store-owned specs and
    /// appends ours, then handing the whole list back through <c>IPSecSetFilterData</c>.
    /// </remarks>
    private static void AddFilter(IntPtr store, Dictionary<string, string> parameters)
    {
        string filterListName = GetRequired(parameters, "filterlist");
        string srcAddr = GetRequired(parameters, "srcaddr");
        string dstAddr = GetRequired(parameters, "dstaddr");
        string? description = GetOptional(parameters, "description");
        string? protocol = GetOptional(parameters, "protocol");
        bool mirrored = GetOptionalBool(parameters, "mirrored") ?? true;
        string? srcMask = GetOptional(parameters, "srcmask");
        string? dstMask = GetOptional(parameters, "dstmask");
        int srcPort = GetOptionalInt(parameters, "srcport") ?? 0;
        int dstPort = GetOptionalInt(parameters, "dstport") ?? 0;

        (_, IntPtr filterListPtr) = FindByName(
            store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
        if (filterListPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
        }

        IpsecFilterData list;
        unsafe { list = *(IpsecFilterData*)filterListPtr; }
        int oldCount = (int)list.FilterSpecCount;

        (IpsecAddress source, string? srcDnsName) = ParseAddress(srcAddr, srcMask);
        (IpsecAddress destination, string? dstDnsName) = ParseAddress(dstAddr, dstMask);

        IntPtr pSpec = Marshal.AllocHGlobal(Unsafe.SizeOf<IpsecFilterSpec>());
        IntPtr pNewArray = Marshal.AllocHGlobal(IntPtr.Size * (oldCount + 1));
        IntPtr pSrcDns = AllocString(srcDnsName);
        IntPtr pDstDns = AllocString(dstDnsName);
        IntPtr pDescription = AllocString(description);
        try
        {
            IpsecFilterSpec spec = default;
            spec.FilterSpecGuid = Guid.NewGuid();
            spec.MirrorFlag = mirrored ? 1u : 0u;
            spec.SourceDnsName = pSrcDns;
            spec.DestinationDnsName = pDstDns;
            spec.Description = pDescription;
            spec.SourceAddress = source;
            spec.DestinationAddress = destination;
            spec.SourcePort = MakePort(srcPort);
            spec.DestinationPort = MakePort(dstPort);
            spec.Protocol = (uint)ParseProtocolNumber(protocol);
            unsafe { *(IpsecFilterSpec*)pSpec = spec; }

            for (int index = 0; index < oldCount; index++)
            {
                Marshal.WriteIntPtr(
                    pNewArray,
                    IntPtr.Size * index,
                    Marshal.ReadIntPtr(list.FilterSpecs, IntPtr.Size * index));
            }

            Marshal.WriteIntPtr(pNewArray, IntPtr.Size * oldCount, pSpec);

            list.FilterSpecCount = (uint)(oldCount + 1);
            list.FilterSpecs = pNewArray;
            list.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetFilterData(store, (IntPtr)(&list)); }
            ThrowOnError(hr, "add filter");
        }
        finally
        {
            FreeIfNotZero(pDescription);
            FreeIfNotZero(pDstDns);
            FreeIfNotZero(pSrcDns);
            Marshal.FreeHGlobal(pNewArray);
            Marshal.FreeHGlobal(pSpec);
        }
    }


    private static void DeleteFilter(IntPtr store, Dictionary<string, string> parameters)
    {
        string filterListName = GetRequired(parameters, "filterlist");
        string srcAddr = GetRequired(parameters, "srcaddr");
        string dstAddr = GetRequired(parameters, "dstaddr");
        string? protocol = GetOptional(parameters, "protocol");
        bool mirrored = GetOptionalBool(parameters, "mirrored") ?? true;
        string? srcMask = GetOptional(parameters, "srcmask");
        string? dstMask = GetOptional(parameters, "dstmask");
        int srcPort = GetOptionalInt(parameters, "srcport") ?? 0;
        int dstPort = GetOptionalInt(parameters, "dstport") ?? 0;

        (_, IntPtr filterListPtr) = FindByName(
            store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
        if (filterListPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
        }

        IpsecFilterData list;
        unsafe { list = *(IpsecFilterData*)filterListPtr; }
        int oldCount = (int)list.FilterSpecCount;
        if (oldCount == 0 || list.FilterSpecs == IntPtr.Zero) return;

        (IpsecAddress source, _) = ParseAddress(srcAddr, srcMask);
        (IpsecAddress destination, _) = ParseAddress(dstAddr, dstMask);
        uint targetProtocol = (uint)ParseProtocolNumber(protocol);
        IpsecPort targetSrcPort = MakePort(srcPort);
        IpsecPort targetDstPort = MakePort(dstPort);

        int matchIndex = -1;
        for (int index = 0; index < oldCount; index++)
        {
            IntPtr entry = Marshal.ReadIntPtr(list.FilterSpecs, IntPtr.Size * index);
            if (entry == IntPtr.Zero) continue;

            IpsecFilterSpec candidate;
            unsafe { candidate = *(IpsecFilterSpec*)entry; }
            if (SameAddress(candidate.SourceAddress, source)
                && SameAddress(candidate.DestinationAddress, destination)
                && candidate.Protocol == targetProtocol
                && candidate.SourcePort.Port == targetSrcPort.Port
                && candidate.DestinationPort.Port == targetDstPort.Port
                && (candidate.MirrorFlag != 0) == mirrored)
            {
                matchIndex = index;
                break;
            }
        }

        if (matchIndex < 0) return;

        int newCount = oldCount - 1;
        IntPtr pNewArray = newCount > 0 ? Marshal.AllocHGlobal(IntPtr.Size * newCount) : IntPtr.Zero;
        try
        {
            int writeIndex = 0;
            for (int index = 0; index < oldCount; index++)
            {
                if (index == matchIndex) continue;
                Marshal.WriteIntPtr(
                    pNewArray,
                    IntPtr.Size * writeIndex,
                    Marshal.ReadIntPtr(list.FilterSpecs, IntPtr.Size * index));
                writeIndex++;
            }

            list.FilterSpecCount = (uint)newCount;
            list.FilterSpecs = pNewArray;
            list.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetFilterData(store, (IntPtr)(&list)); }
            ThrowOnError(hr, "delete filter");
        }
        finally
        {
            FreeIfNotZero(pNewArray);
        }
    }

    private static IpsecPort MakePort(int port)
    {
        return new IpsecPort
        {
            PortType = port > 0
                ? IPSecurityPolicyLayout.PortTypeSpecific
                : IPSecurityPolicyLayout.PortTypeAny,
            Port = (uint)Math.Max(0, port)
        };
    }

    private static bool SameAddress(IpsecAddress left, IpsecAddress right)
    {
        return left.AddressType == right.AddressType
            && left.IpAddress == right.IpAddress
            && left.SubnetMask == right.SubnetMask;
    }


    // ===== FilterAction Operations =====

    /// <summary>
    /// The encoding a filter action's flags use in the store, all recovered by measurement:
    /// inpass lives in the action GUID, qmpfs in the first method's flag DWORD, and soft in an
    /// extra all-zero terminator method after the real ones.
    /// </summary>
    private readonly record struct FilterActionEncoding(
        Guid ActionGuid,
        bool QuickModePfs,
        bool AllowUnsecuredFallback);

    /// <remarks>
    /// See <see cref="IpsecNegPolData"/> for the layout. Negotiate actions encode inpass through
    /// <see cref="IPSecurityPolicyLayout.ActionNegotiateAcceptUnsecuredInbound"/> and soft through
    /// a trailing all-zero method, matching the reference writer.
    /// </remarks>
    private static void AddFilterAction(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? description = GetOptional(parameters, "description");
        string actionStr = GetOptional(parameters, "action") ?? "negotiate";

        FilterActionEncoding encoding = ReadFilterActionEncoding(parameters, actionStr, null);
        SecurityMethodBuffer security = BuildSecurityMethods(parameters, in encoding);

        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        try
        {
            IpsecNegPolData data = default;
            data.NegPolIdentifier = Guid.NewGuid();
            data.NegPolAction = encoding.ActionGuid;
            data.NegPolType = encoding.ActionGuid == IPSecurityPolicyLayout.ActionBlock
                || encoding.ActionGuid == IPSecurityPolicyLayout.ActionPermit
                    ? IPSecurityPolicyLayout.NegPolTypeStandard
                    : IPSecurityPolicyLayout.NegPolTypeNegotiate;
            data.SecurityMethodCount = (uint)security.Count;
            data.SecurityMethods = security.Methods;
            data.WhenChanged = (uint)CurrentUnixSeconds();
            data.Name = pName;
            data.Description = pDesc;

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.CreateNegPolData(store, (IntPtr)(&data)); }
            ThrowOnError(hr, "add filteraction");
        }
        finally
        {
            FreeIfNotZero(pDesc);
            FreeIfNotZero(pName);
            security.Dispose();
        }
    }

    private static void SetFilterAction(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");
        string? actionStr = GetOptional(parameters, "action");

        (_, IntPtr original) = FindByName(store, IPSecurityPolicyNativeMethods.EnumNegPolData, 72, name);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter action '{name}' not found.");
        }

        IpsecNegPolData existing;
        unsafe { existing = *(IpsecNegPolData*)original; }

        FilterActionEncoding encoding = ReadFilterActionEncoding(parameters, actionStr, existing);
        bool rebuildMethods =
            actionStr is not null
            || GetOptional(parameters, "qmsecmethods") is not null
            || GetOptionalBool(parameters, "qmpfs") is not null
            || GetOptionalBool(parameters, "soft") is not null;

        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        SecurityMethodBuffer? security = null;
        IntPtr terminatorBuffer = IntPtr.Zero;
        try
        {
            IpsecNegPolData data;
            unsafe { data = *(IpsecNegPolData*)original; }

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                data.Name = pNewName;
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                data.Description = pNewDesc;
            }

            data.NegPolAction = encoding.ActionGuid;
            if (encoding.ActionGuid == IPSecurityPolicyLayout.ActionBlock
                || encoding.ActionGuid == IPSecurityPolicyLayout.ActionPermit)
            {
                data.NegPolType = IPSecurityPolicyLayout.NegPolTypeStandard;
            }
            else if (actionStr is not null)
            {
                data.NegPolType = IPSecurityPolicyLayout.NegPolTypeNegotiate;
            }

            if (rebuildMethods)
            {
                // Rebuild when methods, qmpfs, or soft changed — the terminator participates in
                // the count so both flags need the array rebuilt; a pure action-GUID switch keeps
                // the store's methods.
                if (GetOptional(parameters, "qmsecmethods") is not null
                    || GetOptionalBool(parameters, "qmpfs") is not null
                    || GetOptionalBool(parameters, "soft") is not null)
                {
                    security = BuildSecurityMethods(parameters, in encoding, existing);
                    data.SecurityMethodCount = (uint)security.Value.Count;
                    data.SecurityMethods = security.Value.Methods;
                }
                else
                {
                    ApplySoftTerminator(ref data, in encoding, existing, out terminatorBuffer);
                }
            }

            data.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetNegPolData(store, (IntPtr)(&data)); }
            ThrowOnError(hr, "set filteraction");
        }
        finally
        {
            FreeIfNotZero(terminatorBuffer);
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
            security?.Dispose();
        }
    }

    /// <summary>
    /// Resolves the effective filter-action encoding for a command, falling back to the existing
    /// object's values for parameters the command omits.
    /// </summary>
    private static FilterActionEncoding ReadFilterActionEncoding(
        Dictionary<string, string> parameters, string? actionStr, IpsecNegPolData? existing)
    {
        Guid actionGuid = actionStr is not null
            ? ParseAction(actionStr)
            : existing?.NegPolAction ?? IPSecurityPolicyLayout.ActionNegotiate;

        bool? qmpfs = GetOptionalBool(parameters, "qmpfs");
        bool qmpfsValue = qmpfs ?? ReadQuickModePfs(existing);

        bool? soft = GetOptionalBool(parameters, "soft");
        bool softValue = soft ?? HasSoftTerminator(existing);

        // inpass only applies to negotiate actions; when the command switches an action to
        // block or permit the inpass encoding is dropped with it, matching the reference writer.
        bool? inpass = GetOptionalBool(parameters, "inpass");
        bool inpassValue = inpass
            ?? (existing is { } current
                && current.NegPolAction == IPSecurityPolicyLayout.ActionNegotiateAcceptUnsecuredInbound
                && current.NegPolType == IPSecurityPolicyLayout.NegPolTypeNegotiate);

        Guid effectiveAction = actionGuid;
        if (actionGuid == IPSecurityPolicyLayout.ActionNegotiate && inpassValue)
        {
            effectiveAction = IPSecurityPolicyLayout.ActionNegotiateAcceptUnsecuredInbound;
        }

        return new FilterActionEncoding(effectiveAction, qmpfsValue, softValue);
    }

    /// <summary>
    /// Native buffer holding the quick-mode security methods for one filter action. The count
    /// includes the all-zero terminator method used to encode <c>soft</c>.
    /// </summary>
    private readonly struct SecurityMethodBuffer(int count, IntPtr methods)
    {
        /// <summary>Number of entries in the buffer, including the soft terminator when present.</summary>
        internal int Count { get; } = count;

        /// <summary>Contiguous array to pass as <see cref="IpsecNegPolData.SecurityMethods"/>.</summary>
        internal IntPtr Methods { get; } = methods;

        internal void Dispose() => FreeIfNotZero(Methods);
    }

    /// <summary>
    /// Builds the quick-mode security methods for a filter action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Accepts the netsh-style <c>qmsecmethods</c> spelling, a space- or colon-separated list of
    /// <c>ESP[confidentiality,integrity]</c>, <c>AH[integrity]</c> and <c>AH[h]+ESP[c,i]</c> terms,
    /// each optionally suffixed <c>:kBytesk/secondss</c> to carry per-method lifetimes. Unparsable
    /// terms are skipped.
    /// </para>
    /// <para>
    /// A negotiate action with no security methods is rejected by polstore with
    /// <c>ERROR_INVALID_PARAMETER</c>, so when the caller supplies none the snap-in's own default
    /// pair — ESP 3DES/SHA-1 followed by AH SHA-1 — is used. Block and permit actions carry no
    /// methods at all.
    /// </para>
    /// <para>
    /// The measured flag encoding is applied here: quick-mode PFS is written to the first method's
    /// flag DWORD, and <c>soft</c> appends one all-zero terminator method that the declared count
    /// includes.
    /// </para>
    /// </remarks>
    private static SecurityMethodBuffer BuildSecurityMethods(
        Dictionary<string, string> parameters, in FilterActionEncoding encoding, IpsecNegPolData? existing = null)
    {
        if (encoding.ActionGuid != IPSecurityPolicyLayout.ActionNegotiate
            && encoding.ActionGuid != IPSecurityPolicyLayout.ActionNegotiateAcceptUnsecuredInbound)
        {
            return new SecurityMethodBuffer(0, IntPtr.Zero);
        }

        List<IpsecSecurityMethod> methods = [];
        string? spec = GetOptional(parameters, "qmsecmethods");
        if (spec is not null)
        {
            foreach (string term in SplitMethodTerms(spec))
            {
                if (TryParseSecurityMethod(term, out IpsecSecurityMethod method))
                {
                    methods.Add(method);
                }
            }
        }
        else if (existing is { } value)
        {
            // Preserve the store's methods when the command only changes flags.
            methods.AddRange(ReadExistingMethods(value, includeTerminator: false));
        }

        if (methods.Count == 0)
        {
            methods.Add(MakeSecurityMethod(
                IPSecurityPolicyLayout.TransformEsp,
                IPSecurityPolicyLayout.EncryptionTripleDes,
                IPSecurityPolicyLayout.HashSha1,
                lifetimeSeconds: 0,
                lifetimeKilobytes: 0));
            methods.Add(MakeSecurityMethod(
                IPSecurityPolicyLayout.TransformAh,
                IPSecurityPolicyLayout.HashSha1,
                0,
                lifetimeSeconds: 0,
                lifetimeKilobytes: 0));
        }

        if (encoding.QuickModePfs && methods.Count > 0)
        {
            IpsecSecurityMethod first = methods[0];
            first.QuickModePfsEnabled = 1;
            methods[0] = first;
        }

        // soft is encoded as one extra all-zero method that the declared count includes.
        if (encoding.AllowUnsecuredFallback)
        {
            methods.Add(default);
        }

        int size = Unsafe.SizeOf<IpsecSecurityMethod>();
        IntPtr buffer = Marshal.AllocHGlobal(size * methods.Count);
        unsafe { NativeMemory.Clear((void*)buffer, (nuint)(size * methods.Count)); }
        for (int index = 0; index < methods.Count; index++)
        {
            IpsecSecurityMethod method = methods[index];
            unsafe { *(IpsecSecurityMethod*)(buffer + (size * index)) = method; }
        }

        return new SecurityMethodBuffer(methods.Count, buffer);
    }

    /// <summary>
    /// Reads the real (non-terminator) methods out of an existing negotiation policy, preserving
    /// their lifetimes and quick-mode PFS flag.
    /// </summary>
    private static List<IpsecSecurityMethod> ReadExistingMethods(
        IpsecNegPolData existing, bool includeTerminator)
    {
        List<IpsecSecurityMethod> methods = [];
        int count = (int)existing.SecurityMethodCount;
        int size = Unsafe.SizeOf<IpsecSecurityMethod>();
        for (int index = 0; index < count && existing.SecurityMethods != IntPtr.Zero; index++)
        {
            IpsecSecurityMethod method;
            unsafe { method = *(IpsecSecurityMethod*)(existing.SecurityMethods + (size * index)); }

            bool isTerminator = method.Transform == 0 && method.PrimaryAlgorithm == 0;
            if (isTerminator && !includeTerminator)
            {
                continue;
            }

            methods.Add(method);
        }

        return methods;
    }

    /// <summary>
    /// Adjusts the soft terminator in place on an existing struct copy without rebuilding methods.
    /// The buffer handed out through <paramref name="pBuffer"/> must be freed by the caller after
    /// the native set call.
    /// </summary>
    private static void ApplySoftTerminator(
        ref IpsecNegPolData data,
        in FilterActionEncoding encoding,
        in IpsecNegPolData existing,
        out IntPtr pBuffer)
    {
        pBuffer = IntPtr.Zero;
        if (encoding.AllowUnsecuredFallback)
        {
            if (HasSoftTerminator(existing))
            {
                return;
            }

            // Append an all-zero terminator method to a cloned array.
            List<IpsecSecurityMethod> methods = [.. ReadExistingMethods(existing, includeTerminator: false)];
            methods.Add(default);
            int size = Unsafe.SizeOf<IpsecSecurityMethod>();
            IntPtr buffer = Marshal.AllocHGlobal(size * methods.Count);
            unsafe
            {
                NativeMemory.Clear((void*)buffer, (nuint)(size * methods.Count));
                for (int index = 0; index < methods.Count; index++)
                {
                    *(IpsecSecurityMethod*)(buffer + (size * index)) = methods[index];
                }
            }

            data.SecurityMethodCount = (uint)methods.Count;
            data.SecurityMethods = buffer;
            pBuffer = buffer;
            return;
        }

        if (!HasSoftTerminator(existing))
        {
            return;
        }

        // Drop the terminator: rebuild without the trailing zero entry.
        List<IpsecSecurityMethod> kept = [.. ReadExistingMethods(existing, includeTerminator: false)];
        int entrySize = Unsafe.SizeOf<IpsecSecurityMethod>();
        IntPtr keptBuffer = Marshal.AllocHGlobal(entrySize * kept.Count);
        unsafe
        {
            NativeMemory.Clear((void*)keptBuffer, (nuint)(entrySize * kept.Count));
            for (int index = 0; index < kept.Count; index++)
            {
                *(IpsecSecurityMethod*)(keptBuffer + (entrySize * index)) = kept[index];
            }
        }

        data.SecurityMethodCount = (uint)kept.Count;
        data.SecurityMethods = keptBuffer;
        pBuffer = keptBuffer;
    }

    /// <summary>Reads the quick-mode PFS flag from an existing negotiation policy.</summary>
    private static bool ReadQuickModePfs(IpsecNegPolData? existing)
    {
        if (existing is not { } value || value.SecurityMethods == IntPtr.Zero)
        {
            return false;
        }

        IpsecSecurityMethod first;
        unsafe { first = *(IpsecSecurityMethod*)value.SecurityMethods; }
        return first.QuickModePfsEnabled != 0;
    }

    /// <summary>
    /// Detects the soft encoding: the last method is all-zero (a terminator) and the count covers it.
    /// </summary>
    private static bool HasSoftTerminator(IpsecNegPolData? existing)
    {
        if (existing is not { } value || value.SecurityMethods == IntPtr.Zero || value.SecurityMethodCount == 0)
        {
            return false;
        }

        int size = Unsafe.SizeOf<IpsecSecurityMethod>();
        IpsecSecurityMethod last;
        unsafe
        {
            last = *(IpsecSecurityMethod*)(value.SecurityMethods + (size * ((int)value.SecurityMethodCount - 1)));
        }

        return last.Transform == 0 && last.PrimaryAlgorithm == 0;
    }

    private static IpsecSecurityMethod MakeSecurityMethod(
        uint transform, uint primary, uint secondary, uint lifetimeSeconds, uint lifetimeKilobytes)
    {
        return new IpsecSecurityMethod
        {
            LifetimeSeconds = lifetimeSeconds,
            LifetimeKilobytes = lifetimeKilobytes,
            AlgorithmCount = 1,
            PrimaryAlgorithm = primary,
            SecondaryAlgorithm = secondary,
            Transform = transform
        };
    }

    /// <summary>
    /// Splits a method list on spaces only. A <c>:</c> can also separate terms, but it additionally
    /// introduces per-method lifetime suffixes (<c>ESP[3DES,SHA1]:50000k/1200s</c>), so colons are
    /// not treated as separators once the term's brackets have closed and a lifetime suffix may
    /// follow; the separator colon form therefore only applies to a term with no brackets.
    /// </summary>
    private static IEnumerable<string> SplitMethodTerms(string spec)
    {
        List<string> terms = [];
        StringBuilder current = new();
        bool inBrackets = false;
        bool sawBrackets = false;
        foreach (char character in spec)
        {
            switch (character)
            {
                case '[':
                    inBrackets = true;
                    sawBrackets = true;
                    current.Append(character);
                    break;
                case ']':
                    inBrackets = false;
                    current.Append(character);
                    break;
                case ' ' when !inBrackets:
                case ':' when !inBrackets && !sawBrackets:
                    if (current.Length > 0)
                    {
                        terms.Add(current.ToString());
                        current.Clear();
                    }

                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        if (current.Length > 0)
        {
            terms.Add(current.ToString());
        }

        return terms;
    }

    private static bool TryParseSecurityMethod(string term, out IpsecSecurityMethod method)
    {
        method = default;

        // Split a lifetime suffix first: ESP[3DES,SHA1]:50000k/1200s.
        string algorithmsPart = term;
        string? lifetimePart = null;
        int lifetimeSeparator = term.LastIndexOf(':');
        if (lifetimeSeparator > 0 && term.IndexOf('[') is var open && open > 0 && lifetimeSeparator > term.LastIndexOf(']'))
        {
            algorithmsPart = term[..lifetimeSeparator];
            lifetimePart = term[(lifetimeSeparator + 1)..];
        }

        int openIndex = algorithmsPart.IndexOf('[');
        int closeIndex = algorithmsPart.LastIndexOf(']');
        if (openIndex <= 0 || closeIndex <= openIndex)
        {
            return false;
        }

        string kind = algorithmsPart[..openIndex].Trim();
        string[] algorithms = algorithmsPart[(openIndex + 1)..closeIndex]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (algorithms.Length == 0)
        {
            return false;
        }

        uint lifetimeSeconds = 0;
        uint lifetimeKilobytes = 0;
        if (lifetimePart is not null)
        {
            ParseLifetime(lifetimePart, out lifetimeSeconds, out lifetimeKilobytes);
        }

        if (kind.Equals("AH", StringComparison.OrdinalIgnoreCase))
        {
            method = MakeSecurityMethod(
                IPSecurityPolicyLayout.TransformAh,
                ParseAlgorithm(algorithms[0]),
                0,
                lifetimeSeconds,
                lifetimeKilobytes);
            return true;
        }

        if (kind.Equals("ESP", StringComparison.OrdinalIgnoreCase))
        {
            uint confidentiality = ParseAlgorithm(algorithms[0]);
            uint integrity = algorithms.Length > 1 ? ParseAlgorithm(algorithms[1]) : 0;
            method = MakeSecurityMethod(
                IPSecurityPolicyLayout.TransformEsp,
                confidentiality,
                integrity,
                lifetimeSeconds,
                lifetimeKilobytes);
            return true;
        }

        // Combined "AH[hash]+ESP[conf,integrity]" terms collapse into ESP-only offers the way
        // the reference writer stores them, because a combined term shares one lifetime.
        if (kind.Contains("AH", StringComparison.OrdinalIgnoreCase)
            && kind.Contains("ESP", StringComparison.OrdinalIgnoreCase)
            && algorithmsPart.Contains('+'))
        {
            string[] halves = algorithmsPart.Split('+', 2, StringSplitOptions.TrimEntries);
            uint ahHash = TryParseAlgorithms(halves[0], out uint[] ahAlgorithms) && ahAlgorithms.Length > 0
                ? ahAlgorithms[0]
                : 0;
            if (TryParseAlgorithms(halves.Length > 1 ? halves[1] : string.Empty, out uint[] espAlgorithms)
                && espAlgorithms.Length >= 1)
            {
                method = MakeSecurityMethod(
                    IPSecurityPolicyLayout.TransformEsp,
                    espAlgorithms[0],
                    espAlgorithms.Length > 1 ? espAlgorithms[1] : ahHash,
                    lifetimeSeconds,
                    lifetimeKilobytes);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseAlgorithms(string term, out uint[] algorithms)
    {
        algorithms = [];
        int open = term.IndexOf('[');
        int close = term.LastIndexOf(']');
        if (open < 0 || close <= open)
        {
            return false;
        }

        algorithms = term[(open + 1)..close]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Trim().ToUpperInvariant() switch
            {
                "NONE" => 0u,
                "DES" => IPSecurityPolicyLayout.EncryptionDes,
                "3DES" => IPSecurityPolicyLayout.EncryptionTripleDes,
                "MD5" => IPSecurityPolicyLayout.HashMd5,
                "SHA1" => IPSecurityPolicyLayout.HashSha1,
                _ => 0u
            })
            .ToArray();
        return algorithms.Length > 0;
    }

    /// <summary>Parses a <c>kBytesk/secondss</c> lifetime suffix, for example <c>50000k/1200s</c>.</summary>
    private static void ParseLifetime(string spec, out uint seconds, out uint kilobytes)
    {
        seconds = 0;
        kilobytes = 0;

        foreach (string part in spec.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(part[..^1], out uint s))
            {
                seconds = s;
            }
            else if (part.EndsWith("k", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(part[..^1], out uint k))
            {
                kilobytes = k;
            }
        }
    }

    private static uint ParseAlgorithm(string name)
    {
        return name.Trim().ToUpperInvariant() switch
        {
            "NONE" => 0,
            "DES" => IPSecurityPolicyLayout.EncryptionDes,
            "3DES" => IPSecurityPolicyLayout.EncryptionTripleDes,
            "MD5" => IPSecurityPolicyLayout.HashMd5,
            "SHA1" => IPSecurityPolicyLayout.HashSha1,
            _ => 0
        };
    }

    /// <summary>Maps a netsh-style filter action verb onto its well-known action GUID.</summary>
    private static Guid ParseAction(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "block" => IPSecurityPolicyLayout.ActionBlock,
            "permit" => IPSecurityPolicyLayout.ActionPermit,
            _ => IPSecurityPolicyLayout.ActionNegotiate
        };
    }

    // ===== Rule Operations =====

    /// <remarks>
    /// See <see cref="IpsecNfaData"/>. Note that the authentication methods are an array of
    /// <em>pointers</em> to <see cref="IpsecAuthMethod"/>, not a contiguous struct array.
    /// </remarks>
    private static void AddRule(IntPtr store, Dictionary<string, string> parameters, IReadOnlyList<string> arguments)
    {
        string name = GetRequired(parameters, "name");
        string policyName = GetRequired(parameters, "policy");
        string filterListName = GetRequired(parameters, "filterlist");
        string filterActionName = GetRequired(parameters, "filteraction");
        string? description = GetOptional(parameters, "description");
        string? tunnel = GetOptional(parameters, "tunnel");
        string? connType = GetOptional(parameters, "conntype");
        bool active = GetOptionalBool(parameters, "activate") ?? true;

        (Guid policyGuid, IntPtr policyPtr) = FindPolicyByName(store, policyName);
        if (policyPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Policy '{policyName}' not found.");
        }

        (Guid filterGuid, _) = FindByName(store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
        if (filterGuid == Guid.Empty)
        {
            throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
        }

        (Guid negPolGuid, _) = FindByName(store, IPSecurityPolicyNativeMethods.EnumNegPolData, 72, filterActionName);
        if (negPolGuid == Guid.Empty)
        {
            throw new InvalidOperationException($"Filter action '{filterActionName}' not found.");
        }

        if (tunnel is not null)
        {
            throw new InvalidOperationException(
                "Tunnel rules are not supported by this build: the tunnel endpoint field of " +
                "IPSEC_NFA_DATA has not been recovered, and writing it at a guessed offset would " +
                "corrupt the rule. Configure the tunnel endpoint with the Windows IP Security " +
                "Policy snap-in instead.");
        }

        AuthMethodBuffer auth = BuildAuthMethods(parameters, arguments);
        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        try
        {
            IpsecNfaData data = default;
            data.Name = pName;
            data.NfaIdentifier = Guid.NewGuid();
            data.AuthMethodCount = (uint)auth.Count;
            data.AuthMethods = auth.PointerArray;
            data.InterfaceType = ParseInterfaceType(connType);
            data.ActiveFlag = active ? 1u : 0u;
            data.WhenChanged = (uint)CurrentUnixSeconds();
            data.NegPolIdentifier = negPolGuid;
            data.FilterIdentifier = filterGuid;
            data.Description = pDesc;

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.CreateNFAData(store, policyGuid, (IntPtr)(&data)); }
            ThrowOnError(hr, "add rule");
        }
        finally
        {
            FreeIfNotZero(pDesc);
            FreeIfNotZero(pName);
            auth.Dispose();
        }
    }

    private static void SetRule(IntPtr store, Dictionary<string, string> parameters, IReadOnlyList<string> arguments)
    {
        string? name = GetOptional(parameters, "name");
        Guid? ruleId = GetOptionalGuid(parameters, "id");
        string policyName = GetRequired(parameters, "policy");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");
        string? filterListName = GetOptional(parameters, "filterlist");
        string? filterActionName = GetOptional(parameters, "filteraction");
        string? connType = GetOptional(parameters, "conntype");
        bool? active = GetOptionalBool(parameters, "activate");

        (Guid policyGuid, _) = FindPolicyByName(store, policyName);
        if (policyGuid == Guid.Empty)
        {
            throw new InvalidOperationException($"Policy '{policyName}' not found.");
        }

        IntPtr original = ruleId is { } identifier
            ? FindNfaByIdentifier(store, policyGuid, identifier)
            : FindNfaByName(store, policyGuid, name!);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Rule '{name ?? ruleId?.ToString()}' in policy '{policyName}' not found.");
        }

        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        AuthMethodBuffer? auth = HasAuthenticationParameters(parameters, arguments)
            ? BuildAuthMethods(parameters, arguments)
            : null;
        try
        {
            IpsecNfaData data;
            unsafe { data = *(IpsecNfaData*)original; }
            Guid negPolIdentifier = data.NegPolIdentifier;
            bool isDefaultResponseRule = data.FilterIdentifier == Guid.Empty;

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                data.Name = pNewName;
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                data.Description = pNewDesc;
            }

            if (filterListName is not null)
            {
                (Guid filterGuid, _) = FindByName(
                    store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
                if (filterGuid == Guid.Empty)
                {
                    throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
                }

                data.FilterIdentifier = filterGuid;
            }

            if (filterActionName is not null)
            {
                (Guid negPolGuid, _) = FindByName(
                    store, IPSecurityPolicyNativeMethods.EnumNegPolData, 72, filterActionName);
                if (negPolGuid == Guid.Empty)
                {
                    throw new InvalidOperationException($"Filter action '{filterActionName}' not found.");
                }

                data.NegPolIdentifier = negPolGuid;
            }

            if (connType is not null)
            {
                data.InterfaceType = ParseInterfaceType(connType);
            }

            if (active is not null)
            {
                data.ActiveFlag = active.Value ? 1u : 0u;
            }

            if (auth is { } authentication)
            {
                data.AuthMethodCount = (uint)authentication.Count;
                data.AuthMethods = authentication.PointerArray;
            }

            data.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetNFAData(store, policyGuid, (IntPtr)(&data)); }
            ThrowOnError(hr, "set rule");

            if (isDefaultResponseRule
                && (GetOptionalBool(parameters, "qmpfs") is not null
                    || GetOptional(parameters, "qmsecmethods") is not null))
            {
                SetRuleOwnedSecurityMethods(store, negPolIdentifier, parameters);
            }
        }
        finally
        {
            auth?.Dispose();
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
        }
    }

    private static bool HasAuthenticationParameters(
        Dictionary<string, string> parameters,
        IReadOnlyList<string> arguments)
    {
        return parameters.ContainsKey("kerberos")
            || parameters.ContainsKey("psk")
            || arguments.Skip(4).Any(static argument =>
                argument.StartsWith("rootca=", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Updates the unnamed negotiation policy owned by a default response rule.</summary>
    private static void SetRuleOwnedSecurityMethods(
        IntPtr store,
        Guid negPolIdentifier,
        Dictionary<string, string> parameters)
    {
        IntPtr original = FindUnnamedNegPol(store, negPolIdentifier);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException("The default response rule's security policy was not found.");
        }

        IpsecNegPolData data;
        unsafe { data = *(IpsecNegPolData*)original; }

        FilterActionEncoding encoding = ReadFilterActionEncoding(parameters, actionStr: null, data);
        SecurityMethodBuffer security = BuildSecurityMethods(parameters, in encoding, data);
        try
        {
            data.SecurityMethodCount = (uint)security.Count;
            data.SecurityMethods = security.Methods;
            data.WhenChanged = (uint)CurrentUnixSeconds();

            int hr;
            unsafe { hr = IPSecurityPolicyNativeMethods.SetNegPolData(store, (IntPtr)(&data)); }
            ThrowOnError(hr, "set default response security methods");
        }
        finally
        {
            security.Dispose();
        }
    }

    private static void DeleteRule(IntPtr store, Dictionary<string, string> parameters)
    {
        string policyName = GetRequired(parameters, "policy");
        string ruleName = GetRequired(parameters, "name");

        (Guid policyGuid, _) = FindPolicyByName(store, policyName);
        if (policyGuid == Guid.Empty) return;

        IntPtr nfaPtr = FindNfaByName(store, policyGuid, ruleName);
        if (nfaPtr == IntPtr.Zero) return;

        DeleteRuleAndOwnedAction(store, policyGuid, nfaPtr);
    }

    /// <summary>
    /// Deletes one rule, plus the negotiation policy it owns.
    /// </summary>
    /// <remarks>
    /// A negotiation policy with no name is private to its rule — that is how the snap-in
    /// distinguishes a rule's own filter action from a shared one — so deleting the rule without it
    /// would leave an unreachable object in the store. Named (shared) filter actions are left alone.
    /// </remarks>
    private static void DeleteRuleAndOwnedAction(IntPtr store, Guid policyGuid, IntPtr nfaPtr)
    {
        IpsecNfaData nfa;
        unsafe { nfa = *(IpsecNfaData*)nfaPtr; }

        IntPtr ownedAction = nfa.NegPolIdentifier == Guid.Empty
            ? IntPtr.Zero
            : FindUnnamedNegPol(store, nfa.NegPolIdentifier);

        ThrowOnError(
            IPSecurityPolicyNativeMethods.DeleteNFAData(store, policyGuid, nfaPtr), "delete rule");

        if (ownedAction != IntPtr.Zero)
        {
            // Best effort: a shared action that only looked private must never fail the rule delete.
            IPSecurityPolicyNativeMethods.DeleteNegPolData(store, ownedAction);
        }
    }

    /// <summary>Returns the store-owned pointer to an unnamed negotiation policy, or zero.</summary>
    private static IntPtr FindUnnamedNegPol(IntPtr store, Guid identifier)
    {
        IntPtr arrayHolder = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr countHolder = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteIntPtr(arrayHolder, IntPtr.Zero);
            Marshal.WriteInt32(countHolder, 0);
            if (IPSecurityPolicyNativeMethods.EnumNegPolData(store, arrayHolder, countHolder) != 0)
            {
                return IntPtr.Zero;
            }

            int count = Marshal.ReadInt32(countHolder);
            IntPtr array = Marshal.ReadIntPtr(arrayHolder);
            for (int index = 0; index < count && array != IntPtr.Zero; index++)
            {
                IntPtr entry = Marshal.ReadIntPtr(array, IntPtr.Size * index);
                if (entry == IntPtr.Zero) continue;

                IpsecNegPolData candidate;
                unsafe { candidate = *(IpsecNegPolData*)entry; }
                if (candidate.NegPolIdentifier == identifier && candidate.Name == IntPtr.Zero)
                {
                    return entry;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(arrayHolder);
            Marshal.FreeHGlobal(countHolder);
        }
    }

    /// <summary>Maps a netsh-style connection type onto its native interface-type value.</summary>
    private static uint ParseInterfaceType(string? connType)
    {
        return connType?.ToLowerInvariant() switch
        {
            "lan" => IPSecurityPolicyLayout.InterfaceTypeLan,
            "dialup" => IPSecurityPolicyLayout.InterfaceTypeDialup,
            _ => IPSecurityPolicyLayout.InterfaceTypeAll
        };
    }


    // ===== Auth Method Builder =====

    /// <summary>
    /// Native buffer holding the authentication methods for one rule: an array of
    /// <see cref="IpsecAuthMethod"/> structs plus the array of pointers into it that
    /// <see cref="IpsecNfaData.AuthMethods"/> expects.
    /// </summary>
    private readonly struct AuthMethodBuffer(int count, IntPtr structs, IntPtr pointerArray, IntPtr[] strings)
    {
        /// <summary>Number of methods in the buffer.</summary>
        internal int Count { get; } = count;

        /// <summary>Array of pointers to pass as <see cref="IpsecNfaData.AuthMethods"/>.</summary>
        internal IntPtr PointerArray { get; } = pointerArray;

        private IntPtr Structs { get; } = structs;

        private IntPtr[] Strings { get; } = strings;

        internal void Dispose()
        {
            foreach (IntPtr value in Strings)
            {
                FreeIfNotZero(value);
            }

            FreeIfNotZero(PointerArray);
            FreeIfNotZero(Structs);
        }
    }

    /// <remarks>
    /// See <see cref="IpsecAuthMethod"/>. Kerberos is authentication type 5. The length field next
    /// to the value pointer is left zero: polstore derives the serialized length from the string
    /// itself, exactly as it does for the rule name and description.
    /// </remarks>
    private static AuthMethodBuffer BuildAuthMethods(
        Dictionary<string, string> parameters, IReadOnlyList<string> arguments)
    {
        List<(uint Type, string? Value)> methods = [];

        if (GetOptionalBool(parameters, "kerberos") is true)
        {
            methods.Add((IPSecurityPolicyLayout.AuthKerberos, null));
        }

        string? psk = GetOptional(parameters, "psk");
        if (psk is not null)
        {
            methods.Add((IPSecurityPolicyLayout.AuthPreSharedKey, psk));
        }

        const string rootcaPrefix = "rootca=";
        for (int index = 4; index < arguments.Count; index++)
        {
            if (arguments[index].StartsWith(rootcaPrefix, StringComparison.OrdinalIgnoreCase))
            {
                methods.Add((IPSecurityPolicyLayout.AuthCertificate, arguments[index][rootcaPrefix.Length..]));
            }
        }

        if (methods.Count == 0)
        {
            methods.Add((IPSecurityPolicyLayout.AuthKerberos, null));
        }

        int structSize = Unsafe.SizeOf<IpsecAuthMethod>();
        IntPtr structs = Marshal.AllocHGlobal(structSize * methods.Count);
        IntPtr pointerArray = Marshal.AllocHGlobal(IntPtr.Size * methods.Count);
        IntPtr[] strings = new IntPtr[methods.Count];

        unsafe { NativeMemory.Clear((void*)structs, (nuint)(structSize * methods.Count)); }

        for (int index = 0; index < methods.Count; index++)
        {
            IntPtr entry = structs + (structSize * index);
            strings[index] = AllocString(methods[index].Value);

            IpsecAuthMethod method = default;
            method.AuthType = methods[index].Type;
            method.AuthMethodValue = strings[index];
            unsafe { *(IpsecAuthMethod*)entry = method; }

            Marshal.WriteIntPtr(pointerArray, IntPtr.Size * index, entry);
        }

        return new AuthMethodBuffer(methods.Count, structs, pointerArray, strings);
    }


    // ===== Generic Find / Delete Helpers =====

    private delegate int EnumFunc(IntPtr store, IntPtr pppData, IntPtr pdwCount);

    private delegate int DeleteFunc(IntPtr store, IntPtr pData);

    private static (Guid Id, IntPtr Ptr) FindByName(
        IntPtr store, EnumFunc enumFunc, int nameOffset, string name)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = enumFunc(store, ppp, pCount);
            if (hr != 0) return (Guid.Empty, IntPtr.Zero);

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                IntPtr pName = Marshal.ReadIntPtr(p, nameOffset);
                string? itemName = pName != IntPtr.Zero ? Marshal.PtrToStringUni(pName) : null;
                if (name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                {
                    return (ReadGuid(p, 0), p);
                }
            }

            return (Guid.Empty, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static (Guid Id, IntPtr Ptr) FindPolicyByName(IntPtr store, string name)
    {
        return FindByName(store, IPSecurityPolicyNativeMethods.EnumPolicyData, 48, name);
    }

    private static IntPtr FindNfaByName(IntPtr store, Guid policyGuid, string name)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumNFAData(store, policyGuid, ppp, pCount);
            if (hr != 0) return IntPtr.Zero;

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                IntPtr pName = Marshal.ReadIntPtr(p, 0);
                string? nfaName = pName != IntPtr.Zero ? Marshal.PtrToStringUni(pName) : null;
                if (name.Equals(nfaName, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static IntPtr FindNfaByIdentifier(IntPtr store, Guid policyGuid, Guid identifier)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteIntPtr(ppp, IntPtr.Zero);
            Marshal.WriteInt32(pCount, 0);
            if (IPSecurityPolicyNativeMethods.EnumNFAData(store, policyGuid, ppp, pCount) != 0)
            {
                return IntPtr.Zero;
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr array = Marshal.ReadIntPtr(ppp);
            for (int index = 0; index < count && array != IntPtr.Zero; index++)
            {
                IntPtr entry = Marshal.ReadIntPtr(array, IntPtr.Size * index);
                if (entry != IntPtr.Zero && ReadGuid(entry, 8) == identifier)
                {
                    return entry;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static void DeleteByName(
        IntPtr store, Dictionary<string, string> parameters,
        EnumFunc enumFunc, DeleteFunc deleteFunc,
        int nameOffset, string objectKind)
    {
        string name = GetRequired(parameters, "name");
        (_, IntPtr ptr) = FindByName(store, enumFunc, nameOffset, name);
        if (ptr == IntPtr.Zero) return;

        ThrowOnError(deleteFunc(store, ptr), $"delete {objectKind}");
    }

    // ===== Registry Repair (the only remaining direct registry access) =====

    /// <summary>
    /// Root of the legacy local policy store. Used only by
    /// <see cref="CleanOrphanedPolicyRegistryKeys"/>; every mutation goes through polstore.dll.
    /// </summary>
    private const string PolicyStoreRegistryPath =
        @"SOFTWARE\Policies\Microsoft\Windows\IPSec\Policy\Local";


    /// <summary>
    /// Removes orphaned <c>ipsecPolicy{...}</c> keys that <c>IPSecEnumPolicyData</c> cannot parse.
    /// </summary>
    /// <remarks>
    /// This is the one operation with no native equivalent: <c>IPSecDeletePolicyData</c> needs a
    /// parsed struct, so a key polstore refuses to parse can only be removed through the registry.
    /// Such keys are a legacy of earlier builds that created policies by writing serialized blobs;
    /// the native creation path no longer produces them. The step deletes only keys whose GUID is
    /// absent from the enumeration, never a policy the store can see.
    /// </remarks>
    internal static void CleanOrphanedPolicyRegistryKeys(IntPtr store)
    {
        using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(PolicyStoreRegistryPath, writable: true);
        if (baseKey is null) return;

        HashSet<string> knownGuids = [];
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumPolicyData(store, ppp, pCount);
            if (hr == 0)
            {
                int count = Marshal.ReadInt32(pCount);
                IntPtr pp = Marshal.ReadIntPtr(ppp);
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                    if (p == IntPtr.Zero) continue;
                    Guid id = ReadGuid(p, 0);
                    knownGuids.Add(id.ToString("B").ToLowerInvariant());
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }

        foreach (string keyName in baseKey.GetSubKeyNames())
        {
            if (!keyName.StartsWith("ipsecPolicy{", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string guidPart = keyName["ipsecPolicy".Length..].ToLowerInvariant();
            if (!knownGuids.Contains(guidPart))
            {
                baseKey.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            }
        }
    }

    // ===== Struct Helpers =====

    private static Guid ReadGuid(IntPtr ptr, int offset)
    {
        byte[] bytes = new byte[16];
        Marshal.Copy(ptr + offset, bytes, 0, 16);
        return new Guid(bytes);
    }

    private static IntPtr AllocString(string? value)
    {
        return value is not null ? Marshal.StringToHGlobalUni(value) : IntPtr.Zero;
    }

    private static void FreeIfNotZero(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    private static int CurrentUnixSeconds()
    {
        return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Translates a netsh-style address token into an <see cref="IpsecAddress"/>.
    /// </summary>
    /// <remarks>
    /// Addresses are stored in network byte order, which is exactly the byte order
    /// <see cref="IPAddress.GetAddressBytes"/> produces, so the bytes are copied through unchanged.
    /// A subnet mask is only written when one was supplied: for a single host Windows leaves the
    /// mask zero and lets the address type imply /32.
    /// </remarks>
    private static (IpsecAddress Address, string? DnsName) ParseAddress(string address, string? mask)
    {
        if (address.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return (
                new IpsecAddress
                {
                    AddressType = IPSecurityPolicyLayout.AddressTypeAny,
                    AddressCount = IPSecurityPolicyLayout.AddressCountOne
                },
                null);
        }

        if (address.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            return (
                new IpsecAddress
                {
                    AddressType = IPSecurityPolicyLayout.AddressTypeMe,
                    AddressCount = IPSecurityPolicyLayout.AddressCountOne
                },
                null);
        }

        if (address.Equals("dns", StringComparison.OrdinalIgnoreCase))
        {
            return (
                new IpsecAddress
                {
                    AddressType = IPSecurityPolicyLayout.AddressTypeDnsServer,
                    AddressCount = IPSecurityPolicyLayout.AddressCountOne
                },
                null);
        }

        if (IPAddress.TryParse(address, out IPAddress? ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            uint maskValue = 0;
            if (mask is not null
                && IPAddress.TryParse(mask, out IPAddress? maskIp)
                && maskIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                maskValue = BitConverter.ToUInt32(maskIp.GetAddressBytes(), 0);
            }

            return (
                new IpsecAddress
                {
                    AddressType = IPSecurityPolicyLayout.AddressTypeSpecific,
                    AddressCount = IPSecurityPolicyLayout.AddressCountOne,
                    IpAddress = BitConverter.ToUInt32(ip.GetAddressBytes(), 0),
                    SubnetMask = maskValue
                },
                null);
        }

        // Anything else is treated as a DNS name, which polstore resolves when the policy is applied.
        return (
            new IpsecAddress
            {
                AddressType = IPSecurityPolicyLayout.AddressTypeSpecific,
                AddressCount = IPSecurityPolicyLayout.AddressCountOne
            },
            address);
    }

    private static int ParseProtocolNumber(string? protocol)
    {
        if (protocol is null || protocol.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return protocol.ToUpperInvariant() switch
        {
            "ICMP" => 1,
            "TCP" => 6,
            "UDP" => 17,
            "RAW" => 255,
            _ => int.TryParse(protocol, out int num) ? num : 0
        };
    }

    // ===== Command Parsing =====

    private static void ValidateCommand(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4
            || !arguments[0].Equals("ipsec", StringComparison.OrdinalIgnoreCase)
            || !arguments[1].Equals("static", StringComparison.OrdinalIgnoreCase)
            || !AllowedVerbs.Contains(arguments[2])
            || !AllowedObjectKinds.Contains(arguments[3]))
        {
            throw new ArgumentException(
                "Only legacy static IPsec add, set, and delete commands are allowed.",
                nameof(arguments));
        }

        if (arguments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("IPsec command tokens cannot be empty.", nameof(arguments));
        }
    }

    private static Dictionary<string, string> ParseParameters(IReadOnlyList<string> arguments, int startIndex)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        for (int index = startIndex; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            int equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0)
            {
                result[argument[..equalsIndex]] = argument[(equalsIndex + 1)..];
            }
        }

        return result;
    }

    private static string GetRequired(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out string? value) || string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        }

        return value;
    }

    private static string? GetOptional(Dictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out string? value) ? value : null;
    }

    private static bool? GetOptionalBool(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out string? value)) return null;
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int? GetOptionalInt(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out string? value)) return null;
        return int.TryParse(value, out int result) ? result : null;
    }

    private static Guid? GetOptionalGuid(Dictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out string? value) && Guid.TryParse(value, out Guid result)
            ? result
            : null;
    }

    // ===== Error Handling =====

    private static void ThrowOpenStoreFailure(int errorCode)
    {
        if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(errorCode) || errorCode == 5)
        {
            throw new UnauthorizedAccessException(
                "The local IPsec policy store cannot be modified because the operation requires elevation.");
        }

        throw new InvalidOperationException(
            $"Failed to open the legacy IPsec policy store (native error 0x{errorCode:X8}).");
    }

    private static void ThrowOnError(int hr, string operation)
    {
        if (hr == 0) return;

        if (hr == 5 || hr == unchecked((int)0x80070005))
        {
            throw new UnauthorizedAccessException(
                "The local IPsec policy store cannot be modified because the operation requires elevation.");
        }

        throw new InvalidOperationException(
            $"The legacy IPsec policy {operation} command failed with native error 0x{hr:X8}.");
    }

    private void LogFailure(IReadOnlyList<string> arguments)
    {
        _logger.LogWarning(
            "The legacy IPsec static {Verb} {ObjectKind} command failed. Arguments and output were omitted because the command may contain policy secrets.",
            arguments[2],
            arguments[3]);
    }
}
