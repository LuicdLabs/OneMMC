// ============================================================================
// Directory Object Picker Service
// ============================================================================
// Provides a unified wrapper around the Windows native Directory Object Picker
// (IDsObjectPicker COM interface) for selecting users, groups, and other
// directory objects across the application.
//
// This replaces the various custom picker implementations (UserPickerDialog,
// SelectUserDialog, SelectGroupDialog, etc.) with the standard Windows dialog
// used by secpol.msc, services.msc, and other MMC snap-ins.
//
// Key Features:
// - Invokes the native Windows object picker dialog
// - Supports local machine, domain, and workgroup scopes
// - Configurable object type filtering (users, groups, computers)
// - Single and multi-select modes
// - Proper COM resource management
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Principal;
using OneMMC.Core.Infrastructure.Interop;
using Windows.Win32.Foundation;
using Win32Com = Windows.Win32.System.Com;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.WindowsCapabilities;

/// <summary>
/// Represents a directory object selected from the Windows Object Picker dialog.
/// </summary>
public sealed class DirectoryObject
{
    /// <summary>Display name of the selected object.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>ADsPath of the selected object (e.g., "WinNT://MACHINE/User").</summary>
    public string AdsPath { get; init; } = string.Empty;

    /// <summary>Object class (e.g., "user", "group", "computer").</summary>
    public string ObjectClass { get; init; } = string.Empty;

    /// <summary>User Principal Name, if available.</summary>
    public string Upn { get; init; } = string.Empty;

    /// <summary>
    /// Security Identifier (SID) string of the selected object (e.g., "S-1-5-21-...").
    /// Resolved from the account name after selection. Empty if resolution failed.
    /// </summary>
    public string Sid { get; init; } = string.Empty;
}

/// <summary>
/// Specifies the types of directory objects to display in the picker.
/// </summary>
[Flags]
public enum ObjectPickerTypes
{
    /// <summary>No specific object types. Use with well-known principal flags to show only built-in security principals.</summary>
    None = 0x00,

    /// <summary>Show user objects.</summary>
    Users = 0x01,

    /// <summary>Show group objects.</summary>
    Groups = 0x02,

    /// <summary>Show computer objects.</summary>
    Computers = 0x04,

    /// <summary>Show both users and groups.</summary>
    UsersAndGroups = Users | Groups,

    /// <summary>Show all supported object types.</summary>
    All = Users | Groups | Computers,
}

/// <summary>
/// Configures scopes and filters for the native Directory Object Picker.
/// </summary>
public sealed class DirectoryObjectPickerOptions
{
    /// <summary>
    /// Gets or sets the object types displayed by the picker.
    /// </summary>
    public ObjectPickerTypes Types { get; set; } = ObjectPickerTypes.UsersAndGroups;

    /// <summary>
    /// Gets or sets whether multiple selections are allowed.
    /// </summary>
    public bool MultiSelect { get; set; }

    /// <summary>
    /// Gets or sets whether the local computer scope is available.
    /// </summary>
    public bool IncludeLocalComputerScope { get; set; } = true;

    /// <summary>
    /// Gets or sets whether joined, enterprise, global-catalog, and external domain scopes are available.
    /// </summary>
    public bool IncludeDomainScopes { get; set; } = true;

    /// <summary>
    /// Gets or sets whether WORKGROUP locations are available.
    /// </summary>
    public bool IncludeWorkgroupScope { get; set; } = true;

    /// <summary>
    /// Gets or sets whether users can type a custom location.
    /// </summary>
    public bool IncludeUserEnteredScopes { get; set; } = true;

    /// <summary>
    /// Gets or sets whether well-known principals are offered by uplevel scopes.
    /// </summary>
    public bool IncludeWellKnownPrincipals { get; set; } = true;

    /// <summary>
    /// Gets or sets whether downlevel well-known principals such as Everyone or SYSTEM are offered.
    /// </summary>
    public bool IncludeDownlevelWellKnownPrincipals { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to restrict the picker to only show built-in security principals
    /// (e.g., Authenticated Users, BATCH, INTERACTIVE) without local/global groups.
    /// When <see langword="true"/>, the downlevel filter uses specific well-known SID flags
    /// instead of the broad <c>ALL_WELLKNOWN_SIDS</c> flag, and local/global group filters
    /// are excluded even if <see cref="ObjectPickerTypes.Groups"/> is set in <see cref="Types"/>.
    /// This matches the native <c>WF.msc</c> behavior for IPsec computer authorization.
    /// </summary>
    public bool BuiltInPrincipalsOnly { get; set; }
}

/// <summary>
/// Provides access to the Windows native Directory Object Picker dialog
/// (<c>IDsObjectPicker</c> COM interface) for selecting users, groups, and
/// other security principals. This is the same dialog shown by
/// <c>secpol.msc</c>, <c>services.msc</c>, and other OneMMC.
/// </summary>
public static partial class DirectoryObjectPickerService
{
    private static readonly Guid CLSID_DsObjectPicker = new("17D6CCD8-3B7B-11D2-B9E0-00C04FD8DBF7");

    // Source-generated ([GeneratedComInterface]) ports of IDsObjectPicker and the minimal IDataObject
    // surface OneMMC uses, following the Native AOT COM guidance in doc/NativeAot.md ("COM interop").
    // Both are pure IUnknown-derived (non-dual), so no IDispatch base. Activated via ComActivator
    // (CoCreateInstance + ComWrappers) rather than Type.GetTypeFromCLSID + Activator.CreateInstance
    // (unsupported under AOT).
    // Nested types used in [GeneratedComInterface] signatures must be at least `internal` so the
    // interop source generator's emitted code can reference them (SYSLIB1090).
    [GeneratedComInterface, Guid("0C87E64E-3B7A-11D2-B9E0-00C04FD8DBF7")]
    internal partial interface IDsObjectPicker
    {
        [PreserveSig]
        int Initialize(ref DSOP_INIT_INFO pInitInfo);

        [PreserveSig]
        int InvokeDialog(nint hwndParent, out IDsSelectionDataObject ppdoSelections);
    }

    /// <summary>Minimal <c>IDataObject</c>: only <c>GetData</c> (the first vtable slot after IUnknown)
    /// is called, to retrieve the <c>CFSTR_DSOP_DS_SELECTION_LIST</c> selection blob.</summary>
    [GeneratedComInterface, Guid("0000010E-0000-0000-C000-000000000046")]
    internal partial interface IDsSelectionDataObject
    {
        [PreserveSig]
        int GetData(in FORMATETC_Native pformatetcIn, out NativeStorageMediumRaw pmedium);
    }

    /// <summary>Blittable FORMATETC. The BCL <see cref="FORMATETC"/> carries <c>[MarshalAs]</c> on its
    /// enum fields, so it cannot be used directly in a source-generated COM signature.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FORMATETC_Native
    {
        public short cfFormat;
        public IntPtr ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DSOP_UPLEVEL_FILTER_FLAGS
    {
        public uint flBothModes;
        public uint flMixedModeOnly;
        public uint flNativeModeOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DSOP_FILTER_FLAGS
    {
        public DSOP_UPLEVEL_FILTER_FLAGS Uplevel;
        public uint flDownlevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DSOP_SCOPE_INIT_INFO
    {
        public uint cbSize;
        public uint flType;
        public uint flScope;
        public DSOP_FILTER_FLAGS FilterFlags;
        public IntPtr pwzDcName;
        public IntPtr pwzADsPath;
        public int hr;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DSOP_INIT_INFO
    {
        public uint cbSize;
        public IntPtr pwzTargetComputer;
        public uint cDsScopeInfos;
        public IntPtr aDsScopeInfos;
        public uint flOptions;
        public uint cAttributesToFetch;
        public IntPtr apwzAttributesToFetch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DS_SELECTION
    {
        public IntPtr pwzName;
        public IntPtr pwzADsPath;
        public IntPtr pwzClass;
        public IntPtr pwzUPN;
        public IntPtr pvarFetchedAttributes;
        public uint flScopeType;
    }

    private const uint DSOP_SCOPE_TYPE_TARGET_COMPUTER = 0x00000001;
    private const uint DSOP_SCOPE_TYPE_UPLEVEL_JOINED_DOMAIN = 0x00000002;
    private const uint DSOP_SCOPE_TYPE_DOWNLEVEL_JOINED_DOMAIN = 0x00000004;
    private const uint DSOP_SCOPE_TYPE_ENTERPRISE_DOMAIN = 0x00000008;
    private const uint DSOP_SCOPE_TYPE_GLOBAL_CATALOG = 0x00000010;
    private const uint DSOP_SCOPE_TYPE_EXTERNAL_UPLEVEL_DOMAIN = 0x00000020;
    private const uint DSOP_SCOPE_TYPE_EXTERNAL_DOWNLEVEL_DOMAIN = 0x00000040;
    private const uint DSOP_SCOPE_TYPE_WORKGROUP = 0x00000080;
    private const uint DSOP_SCOPE_TYPE_USER_ENTERED_UPLEVEL_SCOPE = 0x00000100;
    private const uint DSOP_SCOPE_TYPE_USER_ENTERED_DOWNLEVEL_SCOPE = 0x00000200;

    private const uint DSOP_FILTER_USERS = 0x00000002;
    private const uint DSOP_FILTER_BUILTIN_GROUPS = 0x00000004;
    private const uint DSOP_FILTER_WELL_KNOWN_PRINCIPALS = 0x00000008;
    private const uint DSOP_FILTER_UNIVERSAL_GROUPS_DL = 0x00000010;
    private const uint DSOP_FILTER_UNIVERSAL_GROUPS_SE = 0x00000020;
    private const uint DSOP_FILTER_GLOBAL_GROUPS_DL = 0x00000040;
    private const uint DSOP_FILTER_GLOBAL_GROUPS_SE = 0x00000080;
    private const uint DSOP_FILTER_DOMAIN_LOCAL_GROUPS_DL = 0x00000100;
    private const uint DSOP_FILTER_DOMAIN_LOCAL_GROUPS_SE = 0x00000200;
    private const uint DSOP_FILTER_COMPUTERS = 0x00000800;

    private const uint DSOP_DOWNLEVEL_FILTER_USERS = 0x80000001;
    private const uint DSOP_DOWNLEVEL_FILTER_LOCAL_GROUPS = 0x80000002;
    private const uint DSOP_DOWNLEVEL_FILTER_GLOBAL_GROUPS = 0x80000004;
    private const uint DSOP_DOWNLEVEL_FILTER_COMPUTERS = 0x80000008;
    private const uint DSOP_DOWNLEVEL_FILTER_WORLD = 0x80000010;
    private const uint DSOP_DOWNLEVEL_FILTER_AUTHENTICATED_USER = 0x80000020;
    private const uint DSOP_DOWNLEVEL_FILTER_ANONYMOUS = 0x80000040;
    private const uint DSOP_DOWNLEVEL_FILTER_BATCH = 0x80000080;
    private const uint DSOP_DOWNLEVEL_FILTER_CREATOR_OWNER = 0x80000100;
    private const uint DSOP_DOWNLEVEL_FILTER_CREATOR_GROUP = 0x80000200;
    private const uint DSOP_DOWNLEVEL_FILTER_DIALUP = 0x80000400;
    private const uint DSOP_DOWNLEVEL_FILTER_INTERACTIVE = 0x80000800;
    private const uint DSOP_DOWNLEVEL_FILTER_NETWORK = 0x80001000;
    private const uint DSOP_DOWNLEVEL_FILTER_SERVICE = 0x80002000;
    private const uint DSOP_DOWNLEVEL_FILTER_SYSTEM = 0x80004000;
    private const uint DSOP_DOWNLEVEL_FILTER_TERMINAL_SERVER = 0x80010000;
    private const uint DSOP_DOWNLEVEL_FILTER_ALL_WELLKNOWN_SIDS = 0x80020000;
    private const uint DSOP_DOWNLEVEL_FILTER_LOCAL_SERVICE = 0x80040000;
    private const uint DSOP_DOWNLEVEL_FILTER_NETWORK_SERVICE = 0x80080000;

    private const uint DSOP_FLAG_MULTISELECT = 0x00000001;

    private const uint DSOP_SCOPE_FLAG_STARTING_SCOPE = 0x00000001;
    private const uint DSOP_SCOPE_FLAG_WANT_PROVIDER_WINNT = 0x00000002;
    private const uint DSOP_SCOPE_FLAG_WANT_PROVIDER_LDAP = 0x00000004;
    private const uint DSOP_SCOPE_FLAG_WANT_SID_PATH = 0x00000010;
    private const uint DSOP_SCOPE_FLAG_DEFAULT_FILTER_USERS = 0x00000040;
    private const uint DSOP_SCOPE_FLAG_DEFAULT_FILTER_GROUPS = 0x00000080;
    private const uint DSOP_SCOPE_FLAG_DEFAULT_FILTER_COMPUTERS = 0x00000100;

    private const int S_OK = 0;
    private const string CFSTR_DSOP_DS_SELECTION_LIST = "CFSTR_DSOP_DS_SELECTION_LIST";

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeStorageMediumRaw
    {
        // TYMED as a plain uint (4-byte ABI, same as the CsWin32 TYMED enum) so the COM source
        // generator accepts this struct as an `out` parameter; it is reinterpreted to the CsWin32
        // Win32 STGMEDIUM projection in ReleaseStorageMedium, and only `unionmember` is read here.
        public uint tymed;
        public IntPtr unionmember;
        public IntPtr pUnkForRelease;
    }

    /// <summary>
    /// Shows the Windows native Directory Object Picker dialog to select
    /// users, groups, or other directory objects.
    /// </summary>
    public static List<DirectoryObject>? ShowDialog(
        IntPtr ownerHwnd,
        ObjectPickerTypes types = ObjectPickerTypes.UsersAndGroups,
        bool multiSelect = false)
        => ShowDialog(
            ownerHwnd,
            new DirectoryObjectPickerOptions
            {
                Types = types,
                MultiSelect = multiSelect
            });

    /// <summary>
    /// Shows the Windows native Directory Object Picker dialog to select
    /// users, groups, or other directory objects.
    /// </summary>
    public static unsafe List<DirectoryObject>? ShowDialog(
        IntPtr ownerHwnd,
        DirectoryObjectPickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        IDsObjectPicker? picker = null;
        IntPtr pScopeInfos = IntPtr.Zero;
        IntPtr apwzAttributesToFetch = IntPtr.Zero;
        IntPtr pAttributeString = IntPtr.Zero;

        try
        {
            // CoCreateInstance via ComActivator; throws COMException (e.g. REGDB_E_CLASSNOTREG) when the
            // picker coclass is unavailable, which the catch below turns into null.
            picker = ComActivator.CreateInstance<IDsObjectPicker>(CLSID_DsObjectPicker);

            var scopes = BuildScopes(options, out int scopeCount);
            int scopeSize = Unsafe.SizeOf<DSOP_SCOPE_INIT_INFO>();
            pScopeInfos = Marshal.AllocHGlobal(scopeSize * scopeCount);

            // Blittable struct array → unmanaged buffer via a Span (no reflection-based Marshal.StructureToPtr).
            var scopeSpan = new Span<DSOP_SCOPE_INIT_INFO>((void*)pScopeInfos, scopeCount);
            for (int i = 0; i < scopeCount; i++)
            {
                scopeSpan[i] = scopes[i];
            }

            pAttributeString = Marshal.StringToHGlobalUni("objectSid");
            apwzAttributesToFetch = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(apwzAttributesToFetch, pAttributeString);

            var initInfo = new DSOP_INIT_INFO
            {
                cbSize = (uint)Unsafe.SizeOf<DSOP_INIT_INFO>(),
                pwzTargetComputer = IntPtr.Zero,
                cDsScopeInfos = (uint)scopeCount,
                aDsScopeInfos = pScopeInfos,
                flOptions = options.MultiSelect ? DSOP_FLAG_MULTISELECT : 0,
                cAttributesToFetch = 1,
                apwzAttributesToFetch = apwzAttributesToFetch,
            };

            int hr = picker.Initialize(ref initInfo);
            if (hr != S_OK)
            {
                return null;
            }

            hr = picker.InvokeDialog(ownerHwnd, out var dataObject);
            if (hr != S_OK || dataObject is null)
            {
                return null;
            }

            return ExtractSelections(dataObject);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (pAttributeString != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pAttributeString);
            }

            if (apwzAttributesToFetch != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(apwzAttributesToFetch);
            }

            if (pScopeInfos != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pScopeInfos);
            }

            ComActivator.Release(picker);
        }
    }

    private static DSOP_SCOPE_INIT_INFO[] BuildScopes(DirectoryObjectPickerOptions options, out int count)
    {
        uint uplevelFilter = BuildUplevelFilter(options);
        uint downlevelFilter = BuildDownlevelFilter(options);
        uint defaultFilterFlags = BuildDefaultFilterFlags(options.Types);
        List<DSOP_SCOPE_INIT_INFO> scopes = [];
        bool hasStartingScope = false;

        uint BuildScopeFlags(bool wantWinNt, bool wantLdap)
        {
            uint scopeFlags = DSOP_SCOPE_FLAG_WANT_SID_PATH | defaultFilterFlags;
            if (!hasStartingScope)
            {
                scopeFlags |= DSOP_SCOPE_FLAG_STARTING_SCOPE;
                hasStartingScope = true;
            }

            if (wantWinNt)
            {
                scopeFlags |= DSOP_SCOPE_FLAG_WANT_PROVIDER_WINNT;
            }

            if (wantLdap)
            {
                scopeFlags |= DSOP_SCOPE_FLAG_WANT_PROVIDER_LDAP;
            }

            return scopeFlags;
        }

        if (options.IncludeLocalComputerScope)
        {
            scopes.Add(new DSOP_SCOPE_INIT_INFO
            {
                cbSize = (uint)Unsafe.SizeOf<DSOP_SCOPE_INIT_INFO>(),
                flType = DSOP_SCOPE_TYPE_TARGET_COMPUTER,
                flScope = BuildScopeFlags(wantWinNt: true, wantLdap: false),
                FilterFlags = new DSOP_FILTER_FLAGS
                {
                    Uplevel = new DSOP_UPLEVEL_FILTER_FLAGS
                    {
                        flBothModes = uplevelFilter,
                    },
                    flDownlevel = downlevelFilter,
                },
            });
        }

        uint domainScopeTypes = 0;
        if (options.IncludeDomainScopes)
        {
            domainScopeTypes |= DSOP_SCOPE_TYPE_UPLEVEL_JOINED_DOMAIN
                              | DSOP_SCOPE_TYPE_DOWNLEVEL_JOINED_DOMAIN
                              | DSOP_SCOPE_TYPE_ENTERPRISE_DOMAIN
                              | DSOP_SCOPE_TYPE_GLOBAL_CATALOG
                              | DSOP_SCOPE_TYPE_EXTERNAL_UPLEVEL_DOMAIN
                              | DSOP_SCOPE_TYPE_EXTERNAL_DOWNLEVEL_DOMAIN;
        }

        if (options.IncludeWorkgroupScope)
        {
            domainScopeTypes |= DSOP_SCOPE_TYPE_WORKGROUP;
        }

        if (options.IncludeUserEnteredScopes)
        {
            domainScopeTypes |= DSOP_SCOPE_TYPE_USER_ENTERED_UPLEVEL_SCOPE
                              | DSOP_SCOPE_TYPE_USER_ENTERED_DOWNLEVEL_SCOPE;
        }

        if (domainScopeTypes != 0)
        {
            scopes.Add(new DSOP_SCOPE_INIT_INFO
            {
                cbSize = (uint)Unsafe.SizeOf<DSOP_SCOPE_INIT_INFO>(),
                flType = domainScopeTypes,
                flScope = BuildScopeFlags(wantWinNt: true, wantLdap: true),
                FilterFlags = new DSOP_FILTER_FLAGS
                {
                    Uplevel = new DSOP_UPLEVEL_FILTER_FLAGS
                    {
                        flBothModes = uplevelFilter,
                    },
                    flDownlevel = downlevelFilter,
                },
            });
        }

        if (scopes.Count == 0)
        {
            scopes.Add(new DSOP_SCOPE_INIT_INFO
            {
                cbSize = (uint)Unsafe.SizeOf<DSOP_SCOPE_INIT_INFO>(),
                flType = DSOP_SCOPE_TYPE_TARGET_COMPUTER,
                flScope = BuildScopeFlags(wantWinNt: true, wantLdap: false),
                FilterFlags = new DSOP_FILTER_FLAGS
                {
                    Uplevel = new DSOP_UPLEVEL_FILTER_FLAGS
                    {
                        flBothModes = uplevelFilter,
                    },
                    flDownlevel = downlevelFilter,
                },
            });
        }

        count = scopes.Count;
        return scopes.ToArray();
    }

    private static uint BuildUplevelFilter(DirectoryObjectPickerOptions options)
    {
        ObjectPickerTypes types = options.Types;
        uint filter = options.IncludeWellKnownPrincipals ? DSOP_FILTER_WELL_KNOWN_PRINCIPALS : 0;

        if (types.HasFlag(ObjectPickerTypes.Users))
        {
            filter |= DSOP_FILTER_USERS;
        }

        if (types.HasFlag(ObjectPickerTypes.Groups))
        {
            filter |= DSOP_FILTER_BUILTIN_GROUPS
                    | DSOP_FILTER_UNIVERSAL_GROUPS_DL
                    | DSOP_FILTER_UNIVERSAL_GROUPS_SE
                    | DSOP_FILTER_GLOBAL_GROUPS_DL
                    | DSOP_FILTER_GLOBAL_GROUPS_SE
                    | DSOP_FILTER_DOMAIN_LOCAL_GROUPS_DL
                    | DSOP_FILTER_DOMAIN_LOCAL_GROUPS_SE;
        }

        if (types.HasFlag(ObjectPickerTypes.Computers))
        {
            filter |= DSOP_FILTER_COMPUTERS;
        }

        return filter;
    }

    private static uint BuildDownlevelFilter(DirectoryObjectPickerOptions options)
    {
        ObjectPickerTypes types = options.Types;
        uint filter = 0;

        if (options.BuiltInPrincipalsOnly)
        {
            // Match native WF.msc Computer authorization dialog.
            // Only these four specific well-known SID flags are used in the downlevel filter.
            // The dialog title "Groups or Built-in security principals" comes from the uplevel filter
            // (DSOP_FILTER_BUILTIN_GROUPS | DSOP_FILTER_WELL_KNOWN_PRINCIPALS), not the downlevel filter.
            filter |= DSOP_DOWNLEVEL_FILTER_AUTHENTICATED_USER
                    | DSOP_DOWNLEVEL_FILTER_BATCH
                    | DSOP_DOWNLEVEL_FILTER_CREATOR_GROUP
                    | DSOP_DOWNLEVEL_FILTER_INTERACTIVE;
            return filter;
        }

        if (options.IncludeDownlevelWellKnownPrincipals)
        {
            filter |= DSOP_DOWNLEVEL_FILTER_ALL_WELLKNOWN_SIDS;
        }

        if (types.HasFlag(ObjectPickerTypes.Users))
        {
            filter |= DSOP_DOWNLEVEL_FILTER_USERS;
        }

        if (types.HasFlag(ObjectPickerTypes.Groups))
        {
            filter |= DSOP_DOWNLEVEL_FILTER_LOCAL_GROUPS
                    | DSOP_DOWNLEVEL_FILTER_GLOBAL_GROUPS;
        }

        if (types.HasFlag(ObjectPickerTypes.Computers))
        {
            filter |= DSOP_DOWNLEVEL_FILTER_COMPUTERS;
        }

        return filter;
    }

    private static uint BuildDefaultFilterFlags(ObjectPickerTypes types)
    {
        uint flags = 0;

        if (types.HasFlag(ObjectPickerTypes.Users))
        {
            flags |= DSOP_SCOPE_FLAG_DEFAULT_FILTER_USERS;
        }

        if (types.HasFlag(ObjectPickerTypes.Groups))
        {
            flags |= DSOP_SCOPE_FLAG_DEFAULT_FILTER_GROUPS;
        }

        if (types.HasFlag(ObjectPickerTypes.Computers))
        {
            flags |= DSOP_SCOPE_FLAG_DEFAULT_FILTER_COMPUTERS;
        }

        return flags;
    }

    private static unsafe List<DirectoryObject>? ExtractSelections(IDsSelectionDataObject dataObject)
    {
        IntPtr pData = IntPtr.Zero;
        NativeStorageMediumRaw medium = default;

        try
        {
            uint cfFormat = Win32PInvoke.RegisterClipboardFormat(CFSTR_DSOP_DS_SELECTION_LIST);
            if (cfFormat == 0)
            {
                return null;
            }

            var formatEtc = new FORMATETC_Native
            {
                cfFormat = (short)cfFormat,
                ptd = IntPtr.Zero,
                dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = (uint)TYMED.TYMED_HGLOBAL,
            };

            if (dataObject.GetData(in formatEtc, out medium) != S_OK)
            {
                return null;
            }

            pData = (IntPtr)Win32PInvoke.GlobalLock((HGLOBAL)medium.unionmember);
            if (pData == IntPtr.Zero)
            {
                return null;
            }

            uint cItems = (uint)Unsafe.ReadUnaligned<int>((void*)pData);
            if (cItems == 0)
            {
                return [];
            }

            int selectionSize = Unsafe.SizeOf<DS_SELECTION>();
            int headerSize = 8;

            var results = new List<DirectoryObject>((int)cItems);

            for (int i = 0; i < cItems; i++)
            {
                var selection = Unsafe.Read<DS_SELECTION>((void*)(pData + headerSize + (i * selectionSize)));

                string name = ReadString(selection.pwzName);
                string adsPath = ReadString(selection.pwzADsPath);

                string sid = ExtractFetchedSid(selection.pvarFetchedAttributes);
                if (string.IsNullOrEmpty(sid))
                {
                    sid = ResolveAccountSid(name, adsPath);
                }

                results.Add(new DirectoryObject
                {
                    Name = name,
                    AdsPath = adsPath,
                    ObjectClass = ReadString(selection.pwzClass),
                    Upn = ReadString(selection.pwzUPN),
                    Sid = sid,
                });
            }

            return results;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (pData != IntPtr.Zero)
            {
                _ = Win32PInvoke.GlobalUnlock((HGLOBAL)medium.unionmember);
            }

            if (medium.unionmember != IntPtr.Zero)
            {
                ReleaseStorageMedium(ref medium);
            }

            ComActivator.Release(dataObject);
        }
    }

    private static void ReleaseStorageMedium(ref NativeStorageMediumRaw medium)
    {
        // Our blittable NativeStorageMediumRaw is layout-identical to the CsWin32 Win32 STGMEDIUM
        // projection; reinterpret once to release the storage with the generated API.
        ref Win32Com.STGMEDIUM nativeMedium = ref Unsafe.As<NativeStorageMediumRaw, Win32Com.STGMEDIUM>(ref medium);
        Win32PInvoke.ReleaseStgMedium(ref nativeMedium);
    }



    private static unsafe string ExtractFetchedSid(IntPtr pvarFetchedAttributes)
    {
        if (pvarFetchedAttributes == IntPtr.Zero) return string.Empty;

        try
        {
            // The fetched "objectSid" attribute comes back as a VARIANT holding a SAFEARRAY of bytes.
            // Read it through the blittable Variant (no reflection-based Marshal.GetObjectForNativeVariant).
            // The VARIANT is owned by the selection blob, so read only — do not Clear it.
            Variant variant = Unsafe.Read<Variant>((void*)pvarFetchedAttributes);
            byte[]? sidBytes = variant.ToByteArray();
            if (sidBytes is { Length: > 0 })
            {
                return new SecurityIdentifier(sidBytes, 0).Value;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string ReadString(IntPtr ptr)
        => ptr != IntPtr.Zero ? Marshal.PtrToStringUni(ptr) ?? string.Empty : string.Empty;

    private static string ResolveAccountSid(string name, string adsPath)
    {
        bool hadQualifiedName = false;

        if (!string.IsNullOrEmpty(adsPath))
        {
            string? qualifiedName = ExtractAccountFromAdsPath(adsPath);
            if (qualifiedName is not null)
            {
                hadQualifiedName = qualifiedName.Contains('\\');

                string? sid = TryTranslateToSid(qualifiedName);
                if (sid is not null)
                {
                    return sid;
                }
            }

            string? sidFromPath = ExtractSidFromAdsPath(adsPath);
            if (sidFromPath is not null)
            {
                return sidFromPath;
            }
        }

        if (!hadQualifiedName && !string.IsNullOrEmpty(name))
        {
            string? sid = TryTranslateToSid(name);
            if (sid is not null)
            {
                return sid;
            }
        }

        return string.Empty;
    }

    private static string? TryTranslateToSid(string accountName)
    {
        try
        {
            var account = new NTAccount(accountName);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            return sid.Value;
        }
        catch (IdentityNotMappedException)
        {
            return null;
        }
        catch (SystemException)
        {
            return null;
        }
    }

    private static string? ExtractAccountFromAdsPath(string adsPath)
    {
        const string winNtPrefix = "WinNT://";
        if (adsPath.StartsWith(winNtPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string path = adsPath[winNtPrefix.Length..];
            int commaIdx = path.IndexOf(',');
            if (commaIdx >= 0)
            {
                path = path[..commaIdx];
            }

            string[] parts = path.Split('/');
            if (parts.Length >= 2)
            {
                return $"{parts[0]}\\{parts[^1]}";
            }

            if (parts.Length == 1 && !string.IsNullOrEmpty(parts[0]))
            {
                return parts[0];
            }

            return null;
        }

        const string ldapPrefix = "LDAP://";
        if (adsPath.StartsWith(ldapPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ExtractAccountFromLdapPath(adsPath[ldapPrefix.Length..]);
        }

        return null;
    }

    private static string? ExtractAccountFromLdapPath(string dn)
    {
        if (dn.StartsWith('<'))
        {
            return null;
        }

        string? cn = null;
        string? domain = null;

        foreach (var part in dn.Split(','))
        {
            var trimmed = part.Trim();
            if (cn is null && trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                cn = trimmed[3..];
            }
            else if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            {
                domain ??= trimmed[3..];
            }
        }

        if (cn is not null && domain is not null)
        {
            return $"{domain}\\{cn}";
        }

        return cn;
    }

    private static string? ExtractSidFromAdsPath(string adsPath)
    {
        const string winNtSidPrefix = "WinNT://S-";
        if (adsPath.StartsWith(winNtSidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string sidPart = adsPath["WinNT://".Length..];
            int commaIdx = sidPart.IndexOf(',');
            string candidate = commaIdx >= 0 ? sidPart[..commaIdx] : sidPart;
            return TryParseSidString(candidate);
        }

        const string ldapSidPrefix = "<SID=";
        int idx = adsPath.IndexOf(ldapSidPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int start = idx + ldapSidPrefix.Length;
            int end = adsPath.IndexOf('>', start);
            if (end > start)
            {
                string sidValue = adsPath[start..end];

                if (sidValue.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
                {
                    return TryParseSidString(sidValue);
                }

                return TryConvertBinarySid(sidValue);
            }
        }

        return null;
    }

    private static string? TryParseSidString(string candidate)
    {
        try
        {
            var sid = new SecurityIdentifier(candidate);
            return sid.Value;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? TryConvertBinarySid(string hex)
    {
        try
        {
            if (hex.Length < 2 || hex.Length % 2 != 0)
            {
                return null;
            }

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            var sid = new SecurityIdentifier(bytes, 0);
            return sid.Value;
        }
        catch
        {
            return null;
        }
    }
}
