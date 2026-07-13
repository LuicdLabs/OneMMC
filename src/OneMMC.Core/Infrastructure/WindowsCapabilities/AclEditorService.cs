using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Infrastructure.WindowsCapabilities;

/// <summary>
/// Identifies the initial page displayed by the native Windows ACL editor.
/// </summary>
public enum AclEditorPageType : uint
{
    /// <summary>
    /// Opens the basic permissions page.
    /// </summary>
    Permissions = 0,

    /// <summary>
    /// Opens the advanced permissions page.
    /// </summary>
    AdvancedPermissions = 1,

    /// <summary>
    /// Opens the auditing page.
    /// </summary>
    Auditing = 2,

    /// <summary>
    /// Opens the owner page.
    /// </summary>
    Owner = 3,

    /// <summary>
    /// Opens the effective access page.
    /// </summary>
    EffectiveAccess = 4,

    /// <summary>
    /// Opens the take ownership page.
    /// </summary>
    TakeOwnership = 5,

    /// <summary>
    /// Opens the share permissions page.
    /// </summary>
    Share = 6
}

/// <summary>
/// Identifies the securable Windows object type represented by the edited resource.
/// </summary>
public enum AclEditorResourceObjectType : uint
{
    /// <summary>
    /// No specific object type is associated with the resource.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The resource is a file or directory.
    /// </summary>
    FileObject = 1,

    /// <summary>
    /// The resource is a registry key.
    /// </summary>
    RegistryKey = 3
}

internal enum AclEditorActivatedPageType : uint
{
    ShowDefault = 0,
    ShowPermissionsActivated = 1,
    ShowAuditingActivated = 2,
    ShowOwnerActivated = 3,
    ShowEffectiveAccessActivated = 4,
    ShowShareActivated = 5
}

/// <summary>
/// Defines object-information flags used by the native ACL editor.
/// </summary>
public static class AclEditorObjectFlags
{
    /// <summary>Allows editing the owner.</summary>
    public const uint EditOwner = 0x00000001;

    /// <summary>Allows editing audit ACEs.</summary>
    public const uint EditAudits = 0x00000002;

    /// <summary>Marks the target as a container.</summary>
    public const uint Container = 0x00000004;

    /// <summary>Shows the advanced editor.</summary>
    public const uint Advanced = 0x00000010;

    /// <summary>Hides ACL protection controls.</summary>
    public const uint NoAclProtect = 0x00000200;

    /// <summary>Disables tree apply behavior.</summary>
    public const uint NoTreeApply = 0x00000400;

    /// <summary>Uses the provided page title.</summary>
    public const uint PageTitle = 0x00000800;

    /// <summary>Shows the effective access page when supported by the editor callback.</summary>
    public const uint EditEffective = 0x00020000;

    /// <summary>Shows elevation UI for owner edits.</summary>
    public const uint OwnerElevationRequired = 0x04000000;
}

/// <summary>
/// Defines ACE inherit flag values used by the native ACL editor.
/// </summary>
public static class AclEditorAceFlags
{
    /// <summary>Object inherit ACE flag.</summary>
    public const uint ObjectInherit = 0x01;

    /// <summary>Container inherit ACE flag.</summary>
    public const uint ContainerInherit = 0x02;

    /// <summary>Inherit only ACE flag.</summary>
    public const uint InheritOnly = 0x08;
}

/// <summary>
/// Describes an access-right row shown by the native ACL editor.
/// </summary>
public sealed class AclEditorAccessEntry
{
    /// <summary>
    /// Gets or sets the access mask represented by this entry.
    /// </summary>
    public uint Mask { get; set; }

    /// <summary>
    /// Gets or sets the localized display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a general access entry.
    /// </summary>
    public bool IsGeneral { get; set; } = true;

    /// <summary>
    /// Gets or sets the ACE inheritance flags (<see cref="AclEditorAceFlags.ContainerInherit"/> /
    /// <see cref="AclEditorAceFlags.ObjectInherit"/> / <see cref="AclEditorAceFlags.InheritOnly"/>)
    /// that this access right implies. The native editor uses them to decide whether an existing
    /// ACE lights up this basic-page row and to choose the inheritance applied when the right is
    /// granted, so they must match the object's default propagation (for a folder, container and
    /// object inherit) to mirror Windows Explorer.
    /// </summary>
    public uint InheritFlags { get; set; }

    /// <summary>
    /// Gets or sets whether the access right applies only to containers (maps to
    /// <c>SI_ACCESS_CONTAINER</c>); such rights are shown on the basic page only for containers.
    /// </summary>
    public bool AppliesToContainersOnly { get; set; }
}

/// <summary>
/// Describes an inheritance option shown by the native ACL editor.
/// </summary>
public sealed class AclEditorInheritType
{
    /// <summary>
    /// Gets or sets the ACE inherit flags.
    /// </summary>
    public uint Flags { get; set; }

    /// <summary>
    /// Gets or sets the localized display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Maps generic access bits to resource-specific access masks for native ACL APIs.
/// </summary>
public sealed class AclEditorGenericMapping
{
    /// <summary>
    /// Gets or sets the mask that represents generic read access.
    /// </summary>
    public uint GenericRead { get; set; }

    /// <summary>
    /// Gets or sets the mask that represents generic write access.
    /// </summary>
    public uint GenericWrite { get; set; }

    /// <summary>
    /// Gets or sets the mask that represents generic execute access.
    /// </summary>
    public uint GenericExecute { get; set; }

    /// <summary>
    /// Gets or sets the mask that represents generic all access.
    /// </summary>
    public uint GenericAll { get; set; }
}

/// <summary>
/// Contains the configuration required to open the native ACL editor.
/// </summary>
public sealed class AclEditorRequest
{
    /// <summary>
    /// Gets or sets the object display name shown by the editor.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full resource name used by native editor integrations that need a canonical path.
    /// </summary>
    public string FullResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the page title displayed by the editor.
    /// </summary>
    public string PageTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the initial self-relative security descriptor in SDDL form.
    /// </summary>
    public string SecurityDescriptorSddl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the editor object-information flags.
    /// </summary>
    public uint ObjectInformationFlags { get; set; } =
        AclEditorObjectFlags.Advanced
        | AclEditorObjectFlags.Container
        | AclEditorObjectFlags.PageTitle;

    /// <summary>
    /// Gets or sets the initial page type.
    /// </summary>
    public AclEditorPageType PageType { get; set; } = AclEditorPageType.Permissions;

    /// <summary>
    /// Gets the access-right entries shown by the editor.
    /// </summary>
    public List<AclEditorAccessEntry> AccessEntries { get; } = [];

    /// <summary>
    /// Gets the inheritance entries shown by the editor.
    /// </summary>
    public List<AclEditorInheritType> InheritTypes { get; } = [];

    /// <summary>
    /// Gets or sets a function used to map generic access bits to resource-specific bits.
    /// </summary>
    public Func<uint, uint>? MapGenericAccess { get; set; }

    /// <summary>
    /// Gets or sets the generic access mapping used by native inheritance and effective-access helpers.
    /// </summary>
    public AclEditorGenericMapping? GenericMapping { get; set; }

    /// <summary>
    /// Gets or sets the securable object type represented by the edited resource.
    /// </summary>
    public AclEditorResourceObjectType ResourceObjectType { get; set; } = AclEditorResourceObjectType.Unknown;

    /// <summary>
    /// Gets or sets the descriptor created when the initial SDDL cannot be parsed.
    /// </summary>
    public Func<RawSecurityDescriptor> EmptySecurityDescriptorFactory { get; set; } =
        static () => new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, new RawAcl(2, 0));

    /// <summary>
    /// Gets or sets an optional secondary security context shown by the native editor.
    /// </summary>
    public AclEditorSecondarySecurityRequest? SecondarySecurity { get; set; }
}

/// <summary>
/// Describes a secondary security context shown by the native ACL editor.
/// </summary>
public sealed class AclEditorSecondarySecurityRequest
{
    /// <summary>
    /// Gets or sets the secondary security context display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the object name displayed by the editor.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the page title displayed by the editor.
    /// </summary>
    public string PageTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the initial self-relative security descriptor in SDDL form.
    /// </summary>
    public string SecurityDescriptorSddl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the editor object-information flags.
    /// </summary>
    public uint ObjectInformationFlags { get; set; } = AclEditorObjectFlags.Advanced;

    /// <summary>
    /// Gets the access-right entries shown by the editor.
    /// </summary>
    public List<AclEditorAccessEntry> AccessEntries { get; } = [];

    /// <summary>
    /// Gets the inheritance entries shown by the editor.
    /// </summary>
    public List<AclEditorInheritType> InheritTypes { get; } = [];

    /// <summary>
    /// Gets or sets a function used to map generic access bits to resource-specific bits.
    /// </summary>
    public Func<uint, uint>? MapGenericAccess { get; set; }

    /// <summary>
    /// Gets or sets the descriptor created when the initial SDDL cannot be parsed.
    /// </summary>
    public Func<RawSecurityDescriptor> EmptySecurityDescriptorFactory { get; set; } =
        static () => new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, new RawAcl(2, 0));
}

/// <summary>
/// Represents the result returned from the native ACL editor.
/// </summary>
public sealed class AclEditorResult
{
    /// <summary>
    /// Gets whether the user confirmed a change with OK or Apply.
    /// </summary>
    public bool WasModified { get; init; }

    /// <summary>
    /// Gets the resulting security descriptor.
    /// </summary>
    public RawSecurityDescriptor SecurityDescriptor { get; init; } =
        new(ControlFlags.DiscretionaryAclPresent, null, null, null, new RawAcl(2, 0));

    /// <summary>
    /// Gets the security-information flags requested when the primary descriptor was changed.
    /// </summary>
    public uint SecurityInformation { get; init; }

    /// <summary>
    /// Gets whether the optional secondary descriptor was modified.
    /// </summary>
    public bool WasSecondaryModified { get; init; }

    /// <summary>
    /// Gets the optional secondary security descriptor.
    /// </summary>
    public RawSecurityDescriptor? SecondarySecurityDescriptor { get; init; }
}

/// <summary>
/// Native SI_OBJECT_INFO layout returned to the Windows ACL editor callback.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AclEditorSiObjectInfo
{
    /// <summary>Object-information flags.</summary>
    public uint Flags;

    /// <summary>Optional caller-defined instance value.</summary>
    public IntPtr Instance;

    /// <summary>Optional server name pointer.</summary>
    public IntPtr ServerName;

    /// <summary>Object display name pointer.</summary>
    public IntPtr ObjectName;

    /// <summary>Property page title pointer.</summary>
    public IntPtr PageTitle;

    /// <summary>Object type GUID.</summary>
    public Guid ObjectType;
}

/// <summary>
/// Callback interface consumed by the native Windows ACL editor (aclui
/// <c>ISecurityInformation</c>). Source-generated so the managed implementation gets an
/// AOT-compatible COM-callable wrapper (built-in COM interop is unavailable under Native AOT).
/// </summary>
[GeneratedComInterface]
[Guid("965FC360-16FF-11d0-91CB-00AA00BBB723")]
public partial interface IAclEditorSecurityInformation
{
    /// <summary>Gets display metadata for the edited object.</summary>
    [PreserveSig]
    int GetObjectInformation(out AclEditorSiObjectInfo objectInfo);

    /// <summary>Gets the current security descriptor.</summary>
    [PreserveSig]
    int GetSecurity(uint requestedInformation, out IntPtr securityDescriptor, [MarshalAs(UnmanagedType.Bool)] bool defaultSecurity);

    /// <summary>Accepts an updated security descriptor from the editor.</summary>
    [PreserveSig]
    int SetSecurity(uint securityInformation, IntPtr securityDescriptor);

    /// <summary>Gets the editable access-right rows.</summary>
    [PreserveSig]
    int GetAccessRights(IntPtr objectTypeGuid, uint flags, out IntPtr access, out uint accessCount, out uint defaultAccess);

    /// <summary>Maps generic access rights to resource-specific rights.</summary>
    [PreserveSig]
    int MapGeneric(IntPtr objectTypeGuid, ref byte aceFlags, ref uint mask);

    /// <summary>Gets inheritance choices for container objects.</summary>
    [PreserveSig]
    int GetInheritTypes(out IntPtr inheritTypes, out uint inheritTypeCount);

    /// <summary>Receives property-sheet page lifecycle callbacks.</summary>
    [PreserveSig]
    int PropertySheetPageCallback(IntPtr hwnd, uint message, AclEditorPageType pageType);
}

/// <summary>
/// Extension callback for secondary security descriptors (aclui <c>ISecurityInformation4</c>).
/// </summary>
[GeneratedComInterface]
[Guid("EA961070-CD14-4621-ACE4-F63C03E583E4")]
public partial interface IAclEditorSecurityInformation4
{
    /// <summary>Gets secondary security objects shown by the editor.</summary>
    [PreserveSig]
    int GetSecondarySecurity(out IntPtr securityObjects, out uint securityObjectCount);
}

/// <summary>
/// Callback used by the editor to calculate effective permissions (aclui <c>IEffectivePermission</c>).
/// </summary>
[GeneratedComInterface]
[Guid("3853DC76-9F35-407c-88A1-D19344365FBC")]
public partial interface IAclEditorEffectivePermission
{
    /// <summary>Calculates granted access for the selected security principal.</summary>
    [PreserveSig]
    int GetEffectivePermission(
        IntPtr objectTypeGuid,
        IntPtr userSid,
        IntPtr serverName,
        IntPtr securityDescriptor,
        out IntPtr objectTypeList,
        out uint objectTypeListLength,
        out IntPtr grantedAccessList,
        out uint grantedAccessListLength);
}

/// <summary>
/// Callback used by the native editor to retrieve the canonical resource path
/// (aclui <c>ISecurityInformation3</c>).
/// </summary>
[GeneratedComInterface]
[Guid("E2CDC9CC-31BD-4F8F-8C8B-B641AF516A1A")]
public partial interface IAclEditorSecurityInformation3
{
    /// <summary>Gets the full resource path represented by the edited object.</summary>
    [PreserveSig]
    int GetFullResourceName(out IntPtr resourceName);

    /// <summary>Opens a second editor instance when the shell requests elevated editing.</summary>
    [PreserveSig]
    int OpenElevatedEditor(IntPtr hwnd, AclEditorPageType pageType);
}

/// <summary>
/// Callback used by the native editor to resolve inherited ACE sources
/// (aclui <c>ISecurityObjectTypeInfo</c>).
/// </summary>
[GeneratedComInterface]
[Guid("FC3066EB-79EF-444B-9111-D18A75EBF2FA")]
public partial interface IAclEditorSecurityObjectTypeInfo
{
    /// <summary>Returns the ancestor source for each ACE in the supplied ACL.</summary>
    [PreserveSig]
    int GetInheritSource(uint securityInformation, IntPtr acl, out IntPtr inheritArray);
}

/// <summary>
/// Opens the native Windows ACL editor for callers that provide security descriptor state.
/// </summary>
public sealed partial class AclEditorService
{
    private const int S_OK = 0;
    private const uint SiAccessGeneral = 0x00020000;
    private const uint SiAccessContainer = 0x00040000;
    private const uint LmemFixed = 0x0000;

    private readonly ILogger<AclEditorService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AclEditorService"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics.</param>
    public AclEditorService(ILogger<AclEditorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens the native Windows ACL editor.
    /// </summary>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <param name="request">The editor request.</param>
    /// <returns>The resulting descriptor and whether it was modified.</returns>
    public AclEditorResult EditSecurity(nint ownerWindowHandle, AclEditorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var securityInformation = new EditableSecurityInformation(request, _logger);

        if (request.PageType == AclEditorPageType.Permissions)
        {
            // The basic "Permissions for <object>" dialog is produced only by aclui's EditSecurity
            // entry point. EditSecurityAdvanced targets the advanced property sheet and rejects the
            // basic SI_PAGE_PERM page with E_INVALIDARG ("Value does not fall within the expected
            // range"), so the basic page must be shown through EditSecurity instead.
            if (!AclEditorNativeMethods.EditSecurity(ownerWindowHandle, securityInformation, out int errorHResult)
                && errorHResult < 0)
            {
                Marshal.ThrowExceptionForHR(errorHResult);
            }
        }
        else
        {
            int hr = AclEditorNativeMethods.EditSecurityAdvanced(
                ownerWindowHandle,
                securityInformation,
                GetNativePageType(request.PageType));

            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
        }

        return new AclEditorResult
        {
            WasModified = securityInformation.WasModified,
            SecurityDescriptor = securityInformation.SecurityDescriptor,
            SecurityInformation = securityInformation.SecurityInformation,
            WasSecondaryModified = securityInformation.WasSecondaryModified,
            SecondarySecurityDescriptor = securityInformation.SecondarySecurityDescriptor
        };
    }

    /// <summary>
    /// Maps an <see cref="AclEditorPageType"/> to the <c>uSIPage</c> (SI_PAGE_TYPE) argument passed
    /// to the native <c>EditSecurityAdvanced</c> function.
    /// </summary>
    /// <remarks>
    /// The advanced editor's TAB SET is fixed by the object-information flags (and always contains a
    /// Permissions/DACL page); <c>uSIPage</c> only selects which of those tabs opens activated. Each
    /// advanced case therefore keeps <c>SI_PAGE_PERM</c> as the base page and carries the tab to
    /// activate in the high word via <see cref="CombinePageActivation"/>. The basic
    /// <see cref="AclEditorPageType.Permissions"/> page is shown through <c>EditSecurity</c> by the
    /// caller and never reaches this method, so it falls through to the raw enum value.
    /// </remarks>
    private static uint GetNativePageType(AclEditorPageType pageType)
    {
        return pageType switch
        {
            AclEditorPageType.AdvancedPermissions => CombinePageActivation(AclEditorPageType.Permissions, AclEditorActivatedPageType.ShowPermissionsActivated),
            AclEditorPageType.Auditing => CombinePageActivation(AclEditorPageType.Permissions, AclEditorActivatedPageType.ShowAuditingActivated),
            AclEditorPageType.Owner => CombinePageActivation(AclEditorPageType.Permissions, AclEditorActivatedPageType.ShowOwnerActivated),
            AclEditorPageType.EffectiveAccess => CombinePageActivation(AclEditorPageType.Permissions, AclEditorActivatedPageType.ShowEffectiveAccessActivated),
            AclEditorPageType.Share => CombinePageActivation(AclEditorPageType.Permissions, AclEditorActivatedPageType.ShowShareActivated),
            _ => (uint)pageType
        };
    }

    /// <summary>
    /// Packs a base page and the page to activate into the single <c>uSIPage</c> value understood by
    /// <c>EditSecurityAdvanced</c>, mirroring the aclui.h <c>COMBINE_PAGE_ACTIVATION</c> macro: the
    /// base <c>SI_PAGE_TYPE</c> occupies the low word and the <c>SI_PAGE_ACTIVATED</c> value the high
    /// word.
    /// </summary>
    private static uint CombinePageActivation(AclEditorPageType pageType, AclEditorActivatedPageType activationType)
    {
        return ((uint)activationType << 16) | (uint)pageType;
    }

    /// <summary>
    /// COM callback object consumed by the native ACL editor. Its COM-callable wrapper is
    /// produced by the source-generated ComWrappers (<see cref="Interop.ComActivator.ComWrappers"/>).
    /// </summary>
    [GeneratedComClass]
    public sealed partial class EditableSecurityInformation :
        IAclEditorSecurityInformation,
        IAclEditorSecurityInformation4,
        IAclEditorEffectivePermission,
        IAclEditorSecurityInformation3,
        IAclEditorSecurityObjectTypeInfo,
        IDisposable
    {
        private readonly AclEditorRequest _request;
        private readonly List<IntPtr> _allocatedStrings = [];
        private readonly ILogger _logger;
        private readonly IntPtr _serverNamePointer;
        private readonly IntPtr _objectNamePointer;
        private readonly IntPtr _pageTitlePointer;
        private readonly AclEditorNativeMethods.SiAccess[] _accessEntries;
        private readonly IntPtr _accessEntriesPointer;
        private readonly AclEditorNativeMethods.SiInheritType[] _inheritEntries;
        private readonly IntPtr _inheritEntriesPointer;
        private readonly EditableSecurityInformation? _secondarySecurityInformation;
        private readonly IntPtr _secondarySecurityInformationPointer;
        private readonly IntPtr _guidNullPointer;
        private readonly IntPtr _defaultObjectTypeListPointer;

        private const uint OwnerSecurityInformation = 0x00000001;
        private const uint GroupSecurityInformation = 0x00000002;
        private const uint DaclSecurityInformation = 0x00000004;
        private const uint SaclSecurityInformation = 0x00000008;

        private const ControlFlags OwnerControlFlags = ControlFlags.OwnerDefaulted;
        private const ControlFlags GroupControlFlags = ControlFlags.GroupDefaulted;

        private const ControlFlags DaclControlFlags =
            ControlFlags.DiscretionaryAclPresent
            | ControlFlags.DiscretionaryAclDefaulted
            | ControlFlags.DiscretionaryAclUntrusted
            | ControlFlags.DiscretionaryAclAutoInheritRequired
            | ControlFlags.DiscretionaryAclAutoInherited
            | ControlFlags.DiscretionaryAclProtected;

        private const ControlFlags SaclControlFlags =
            ControlFlags.SystemAclPresent
            | ControlFlags.SystemAclDefaulted
            | ControlFlags.SystemAclAutoInheritRequired
            | ControlFlags.SystemAclAutoInherited
            | ControlFlags.SystemAclProtected;

        internal EditableSecurityInformation(AclEditorRequest request, ILogger logger)
        {
            _request = request;
            _logger = logger;
            SecurityDescriptor = CreateDescriptor(request);

            string objectName = string.IsNullOrWhiteSpace(request.ObjectName) ? request.PageTitle : request.ObjectName;
            string pageTitle = string.IsNullOrWhiteSpace(request.PageTitle) ? objectName : request.PageTitle;
            _serverNamePointer = AllocateString(string.Empty);
            _objectNamePointer = AllocateString(objectName);
            _pageTitlePointer = AllocateString(pageTitle);

            _accessEntries = CreateAccessEntries(request.AccessEntries);
            _accessEntriesPointer = AllocateStructureArray(_accessEntries);

            _inheritEntries = CreateInheritEntries(request.InheritTypes);
            _inheritEntriesPointer = AllocateStructureArray(_inheritEntries);

            _guidNullPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(Guid.Empty, _guidNullPointer, false);
            _defaultObjectTypeListPointer = Marshal.AllocHGlobal(Marshal.SizeOf<AclEditorNativeMethods.ObjectTypeList>());
            Marshal.StructureToPtr(
                new AclEditorNativeMethods.ObjectTypeList
                {
                    Level = 0,
                    Siblings = 0,
                    ObjectType = _guidNullPointer
                },
                _defaultObjectTypeListPointer,
                false);

            if (request.SecondarySecurity is not null)
            {
                _secondarySecurityInformation = new EditableSecurityInformation(
                    CreateSecondaryRequest(request.SecondarySecurity),
                    logger);

                // The native editor expects an ISecurityInformation* in SI_SECURITY_OBJECT, so QI
                // the source-generated CCW to that exact interface (replaces the built-in-interop
                // Marshal.GetComInterfaceForObject, which Native AOT does not support).
                nint unknown = Interop.ComActivator.ComWrappers.GetOrCreateComInterfaceForObject(
                    _secondarySecurityInformation, CreateComInterfaceFlags.None);
                try
                {
                    int hr = Marshal.QueryInterface(unknown, in AclEditorNativeMethods.ISecurityInformationIid, out _secondarySecurityInformationPointer);
                    if (hr < 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                }
                finally
                {
                    Marshal.Release(unknown);
                }
            }
        }

        public RawSecurityDescriptor SecurityDescriptor { get; private set; }

        public bool WasModified { get; private set; }

        public uint SecurityInformation { get; private set; }

        public bool WasSecondaryModified => _secondarySecurityInformation?.WasModified == true;

        public RawSecurityDescriptor? SecondarySecurityDescriptor => _secondarySecurityInformation?.SecurityDescriptor;

        public void Dispose()
        {
            if (_secondarySecurityInformationPointer != IntPtr.Zero)
            {
                Marshal.Release(_secondarySecurityInformationPointer);
            }

            _secondarySecurityInformation?.Dispose();

            foreach (IntPtr pointer in _allocatedStrings)
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            if (_accessEntriesPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_accessEntriesPointer);
            }

            if (_inheritEntriesPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_inheritEntriesPointer);
            }

            if (_defaultObjectTypeListPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_defaultObjectTypeListPointer);
            }

            if (_guidNullPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_guidNullPointer);
            }
        }

        public int GetObjectInformation(out AclEditorSiObjectInfo objectInfo)
        {
            objectInfo = new AclEditorSiObjectInfo
            {
                Flags = _request.ObjectInformationFlags,
                Instance = IntPtr.Zero,
                ServerName = _serverNamePointer,
                ObjectName = _objectNamePointer,
                PageTitle = _pageTitlePointer,
                ObjectType = Guid.Empty
            };

            return S_OK;
        }

        public int GetSecurity(uint requestedInformation, out IntPtr securityDescriptor, bool defaultSecurity)
        {
            byte[] descriptorBytes = SerializeDescriptor(SecurityDescriptor);
            _logger.LogDebug(
                "[AclEditorService] GetSecurity called (requestedInfo=0x{Info:X}), returning {Bytes} bytes.",
                requestedInformation,
                descriptorBytes.Length);

            securityDescriptor = AclEditorNativeMethods.LocalAlloc(LmemFixed, (UIntPtr)descriptorBytes.Length);
            if (securityDescriptor == IntPtr.Zero)
            {
                return Marshal.GetHRForLastWin32Error();
            }

            Marshal.Copy(descriptorBytes, 0, securityDescriptor, descriptorBytes.Length);
            return S_OK;
        }

        public int SetSecurity(uint securityInformation, IntPtr securityDescriptor)
        {
            _logger.LogDebug("[AclEditorService] SetSecurity called with securityInformation=0x{Info:X}", securityInformation);
            if (securityDescriptor == IntPtr.Zero)
            {
                return S_OK;
            }

            try
            {
                // The native ACL editor may return an absolute-form descriptor.
                // RawSecurityDescriptor requires self-relative form, so convert first.
                byte[] descriptorBytes = ConvertToSelfRelative(securityDescriptor);
                if (descriptorBytes.Length == 0)
                {
                    return Marshal.GetHRForLastWin32Error();
                }

                // Per the ISecurityInformation::SetSecurity contract, only the parts named by
                // securityInformation are authoritative; every other component must be preserved
                // from the object's existing descriptor. The editor issues owner-only (or
                // DACL-only) updates, so replacing the whole descriptor dropped the DACL when only
                // the owner changed (and vice versa) - that is why owner changes never took effect.
                var incoming = new RawSecurityDescriptor(descriptorBytes, 0);
                SecurityDescriptor = MergeSecurityDescriptor(SecurityDescriptor, incoming, securityInformation);
                WasModified = true;
                SecurityInformation |= securityInformation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AclEditorService] Failed to consume edited security descriptor for {PageTitle}.", _request.PageTitle);
                return Marshal.GetHRForException(ex);
            }

            return S_OK;
        }

        public int GetAccessRights(IntPtr objectTypeGuid, uint flags, out IntPtr access, out uint accessCount, out uint defaultAccess)
        {
            access = _accessEntriesPointer;
            accessCount = (uint)_accessEntries.Length;
            defaultAccess = 0;
            return S_OK;
        }

        public int MapGeneric(IntPtr objectTypeGuid, ref byte aceFlags, ref uint mask)
        {
            if (_request.MapGenericAccess is not null)
            {
                mask = _request.MapGenericAccess(mask);
            }

            return S_OK;
        }

        public int GetInheritTypes(out IntPtr inheritTypes, out uint inheritTypeCount)
        {
            inheritTypes = _inheritEntriesPointer;
            inheritTypeCount = (uint)_inheritEntries.Length;
            return S_OK;
        }

        public int PropertySheetPageCallback(IntPtr hwnd, uint message, AclEditorPageType pageType)
        {
            return S_OK;
        }

        public int GetSecondarySecurity(out IntPtr securityObjects, out uint securityObjectCount)
        {
            _logger.LogDebug("[AclEditorService] GetSecondarySecurity requested for {PageTitle}.", _request.PageTitle);
            securityObjects = IntPtr.Zero;
            securityObjectCount = 0;

            if (_request.SecondarySecurity is null || _secondarySecurityInformationPointer == IntPtr.Zero)
            {
                return S_OK;
            }

            int size = Marshal.SizeOf<AclEditorNativeMethods.SecurityObject>();
            securityObjects = AclEditorNativeMethods.LocalAlloc(LmemFixed, (UIntPtr)size);
            if (securityObjects == IntPtr.Zero)
            {
                return Marshal.GetHRForLastWin32Error();
            }

            string name = string.IsNullOrWhiteSpace(_request.SecondarySecurity.Name)
                ? _request.SecondarySecurity.PageTitle
                : _request.SecondarySecurity.Name;
            IntPtr namePointer = AllocateLocalString(name);
            if (namePointer == IntPtr.Zero)
            {
                _ = AclEditorNativeMethods.LocalFree(securityObjects);
                securityObjects = IntPtr.Zero;
                return Marshal.GetHRForLastWin32Error();
            }

            Marshal.StructureToPtr(
                new AclEditorNativeMethods.SecurityObject
                {
                    Name = namePointer,
                    Data = _secondarySecurityInformationPointer,
                    DataLength = 0,
                    Data2 = IntPtr.Zero,
                    Data2Length = 0,
                    Id = AclEditorNativeMethods.SecurityObjectIdShare,
                    IsWellKnown = 1
                },
                securityObjects,
                false);

            securityObjectCount = 1;
            return S_OK;
        }

        public int GetEffectivePermission(
            IntPtr objectTypeGuid,
            IntPtr userSid,
            IntPtr serverName,
            IntPtr securityDescriptor,
            out IntPtr objectTypeList,
            out uint objectTypeListLength,
            out IntPtr grantedAccessList,
            out uint grantedAccessListLength)
        {
            _logger.LogDebug("[AclEditorService] GetEffectivePermission requested for {PageTitle}.", _request.PageTitle);
            objectTypeList = _defaultObjectTypeListPointer;
            objectTypeListLength = 1;
            grantedAccessList = IntPtr.Zero;
            grantedAccessListLength = 0;

            if (userSid == IntPtr.Zero || securityDescriptor == IntPtr.Zero)
            {
                return AclEditorNativeMethods.EInvalidArg;
            }

            try
            {
                uint grantedAccess = CalculateEffectiveAccess(userSid, securityDescriptor);
                grantedAccessList = AclEditorNativeMethods.LocalAlloc(LmemFixed, (UIntPtr)sizeof(uint));
                if (grantedAccessList == IntPtr.Zero)
                {
                    return Marshal.GetHRForLastWin32Error();
                }

                Marshal.WriteInt32(grantedAccessList, unchecked((int)grantedAccess));
                grantedAccessListLength = 1;
                return S_OK;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AclEditorService] Failed to calculate effective access for {PageTitle}.", _request.PageTitle);
                return Marshal.GetHRForException(ex);
            }
        }

        public int GetFullResourceName(out IntPtr resourceName)
        {
            string fullResourceName = string.IsNullOrWhiteSpace(_request.FullResourceName)
                ? _request.ObjectName
                : _request.FullResourceName;

            resourceName = AllocateLocalString(fullResourceName);
            if (resourceName == IntPtr.Zero && fullResourceName.Length > 0)
            {
                return AclEditorNativeMethods.EOutOfMemory;
            }

            _logger.LogDebug("[AclEditorService] GetFullResourceName requested for {ResourceName}.", fullResourceName);
            return S_OK;
        }

        public int OpenElevatedEditor(IntPtr hwnd, AclEditorPageType pageType)
        {
            _logger.LogDebug(
                "[AclEditorService] OpenElevatedEditor requested for {PageTitle} on page {PageType}.",
                _request.PageTitle,
                pageType);

            return AclEditorNativeMethods.EditSecurityAdvanced(
                hwnd,
                this,
                GetNativePageType(pageType));
        }

        public int GetInheritSource(uint securityInformation, IntPtr acl, out IntPtr inheritArray)
        {
            inheritArray = IntPtr.Zero;

            if (acl == IntPtr.Zero
                || _request.ResourceObjectType == AclEditorResourceObjectType.Unknown
                || _request.GenericMapping is null
                || string.IsNullOrWhiteSpace(_request.FullResourceName))
            {
                return AclEditorNativeMethods.ENotImpl;
            }

            var genericMapping = new AclEditorNativeMethods.GenericMapping
            {
                GenericRead = _request.GenericMapping.GenericRead,
                GenericWrite = _request.GenericMapping.GenericWrite,
                GenericExecute = _request.GenericMapping.GenericExecute,
                GenericAll = _request.GenericMapping.GenericAll
            };

            ushort aceCount = Marshal.PtrToStructure<AclEditorNativeMethods.Acl>(acl).AceCount;
            if (aceCount == 0)
            {
                return S_OK;
            }

            int inheritEntrySize = Marshal.SizeOf<AclEditorNativeMethods.InheritedFrom>();
            inheritArray = AclEditorNativeMethods.LocalAlloc(LmemFixed, (UIntPtr)(inheritEntrySize * aceCount));
            if (inheritArray == IntPtr.Zero)
            {
                return Marshal.GetHRForLastWin32Error();
            }

            uint status = AclEditorNativeMethods.GetInheritanceSourceW(
                _request.FullResourceName,
                (uint)_request.ResourceObjectType,
                securityInformation,
                (_request.ObjectInformationFlags & AclEditorObjectFlags.Container) != 0,
                IntPtr.Zero,
                0,
                acl,
                IntPtr.Zero,
                ref genericMapping,
                inheritArray);
            if (status != 0)
            {
                _ = AclEditorNativeMethods.LocalFree(inheritArray);
                inheritArray = IntPtr.Zero;
                _logger.LogDebug(
                    "[AclEditorService] GetInheritanceSourceW failed for {ResourceName}. Status={Status}.",
                    _request.FullResourceName,
                    status);
                return unchecked((int)(0x80070000u | status));
            }

            _logger.LogDebug(
                "[AclEditorService] GetInheritSource requested for {ResourceName}.",
                _request.FullResourceName);
            return S_OK;
        }

        private IntPtr AllocateString(string value)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(value);
            _allocatedStrings.Add(pointer);
            return pointer;
        }

        private static IntPtr AllocateLocalString(string value)
        {
            char[] characters = $"{value}\0".ToCharArray();
            IntPtr pointer = AclEditorNativeMethods.LocalAlloc(
                LmemFixed,
                (UIntPtr)(characters.Length * sizeof(char)));
            if (pointer != IntPtr.Zero)
            {
                Marshal.Copy(characters, 0, pointer, characters.Length);
            }

            return pointer;
        }

        private static RawSecurityDescriptor CreateDescriptor(AclEditorRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.SecurityDescriptorSddl))
            {
                try
                {
                    return new RawSecurityDescriptor(request.SecurityDescriptorSddl);
                }
                catch
                {
                    // Malformed persisted text should not prevent the editor from opening.
                }
            }

            return request.EmptySecurityDescriptorFactory();
        }

        private static AclEditorRequest CreateSecondaryRequest(AclEditorSecondarySecurityRequest secondary)
        {
            var request = new AclEditorRequest
            {
                ObjectName = secondary.ObjectName,
                FullResourceName = secondary.ObjectName,
                PageTitle = secondary.PageTitle,
                SecurityDescriptorSddl = secondary.SecurityDescriptorSddl,
                ObjectInformationFlags = secondary.ObjectInformationFlags,
                PageType = AclEditorPageType.Permissions,
                MapGenericAccess = secondary.MapGenericAccess,
                EmptySecurityDescriptorFactory = secondary.EmptySecurityDescriptorFactory
            };

            request.AccessEntries.AddRange(secondary.AccessEntries);
            request.InheritTypes.AddRange(secondary.InheritTypes);
            return request;
        }

        private IntPtr AllocateStructureArray<T>(T[] values) where T : struct
        {
            if (values.Length == 0)
            {
                return IntPtr.Zero;
            }

            int size = Marshal.SizeOf<T>();
            IntPtr buffer = Marshal.AllocHGlobal(size * values.Length);
            for (int index = 0; index < values.Length; index++)
            {
                Marshal.StructureToPtr(values[index], IntPtr.Add(buffer, index * size), false);
            }

            return buffer;
        }

        private static byte[] SerializeDescriptor(RawSecurityDescriptor descriptor)
        {
            byte[] bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            return bytes;
        }

        /// <summary>
        /// Merges the components named by <paramref name="securityInformation"/> from
        /// <paramref name="incoming"/> into <paramref name="current"/>, preserving every other
        /// component (owner, group, DACL, SACL, and their control bits). This satisfies the merge
        /// requirement of <see cref="IAclEditorSecurityInformation.SetSecurity"/>.
        /// </summary>
        private static RawSecurityDescriptor MergeSecurityDescriptor(
            RawSecurityDescriptor current,
            RawSecurityDescriptor incoming,
            uint securityInformation)
        {
            bool setOwner = (securityInformation & OwnerSecurityInformation) != 0;
            bool setGroup = (securityInformation & GroupSecurityInformation) != 0;
            bool setDacl = (securityInformation & DaclSecurityInformation) != 0;
            bool setSacl = (securityInformation & SaclSecurityInformation) != 0;

            SecurityIdentifier? owner = setOwner ? incoming.Owner : current.Owner;
            SecurityIdentifier? group = setGroup ? incoming.Group : current.Group;
            RawAcl? systemAcl = setSacl ? incoming.SystemAcl : current.SystemAcl;
            RawAcl? discretionaryAcl = setDacl ? incoming.DiscretionaryAcl : current.DiscretionaryAcl;

            ControlFlags controlFlags = current.ControlFlags;
            if (setOwner)
            {
                controlFlags = MergeControlBits(controlFlags, incoming.ControlFlags, OwnerControlFlags);
            }

            if (setGroup)
            {
                controlFlags = MergeControlBits(controlFlags, incoming.ControlFlags, GroupControlFlags);
            }

            if (setDacl)
            {
                controlFlags = MergeControlBits(controlFlags, incoming.ControlFlags, DaclControlFlags);
            }

            if (setSacl)
            {
                controlFlags = MergeControlBits(controlFlags, incoming.ControlFlags, SaclControlFlags);
            }

            // RawSecurityDescriptor is always stored self-relative.
            controlFlags |= ControlFlags.SelfRelative;
            return new RawSecurityDescriptor(controlFlags, owner, group, systemAcl, discretionaryAcl);
        }

        private static ControlFlags MergeControlBits(ControlFlags baseFlags, ControlFlags sourceFlags, ControlFlags mask)
        {
            return (baseFlags & ~mask) | (sourceFlags & mask);
        }

        /// <summary>
        /// Converts a security descriptor pointer (which may be in absolute form) to
        /// a self-relative byte array that <see cref="RawSecurityDescriptor"/> can parse.
        /// </summary>
        private static byte[] ConvertToSelfRelative(IntPtr securityDescriptor)
        {
            // First try: query the required buffer size.
            uint bufferLength = 0;
            if (AclEditorNativeMethods.MakeSelfRelativeSD(securityDescriptor, IntPtr.Zero, ref bufferLength))
            {
                // Already self-relative and zero-length is unexpected; fall through.
            }

            if (bufferLength == 0)
            {
                // Fallback: descriptor is likely already self-relative.
                uint length = AclEditorNativeMethods.GetSecurityDescriptorLength(securityDescriptor);
                if (length == 0)
                {
                    return [];
                }

                byte[] raw = new byte[length];
                Marshal.Copy(securityDescriptor, raw, 0, raw.Length);
                return raw;
            }

            IntPtr selfRelativeBuffer = Marshal.AllocHGlobal((int)bufferLength);
            try
            {
                if (!AclEditorNativeMethods.MakeSelfRelativeSD(securityDescriptor, selfRelativeBuffer, ref bufferLength))
                {
                    return [];
                }

                byte[] result = new byte[bufferLength];
                Marshal.Copy(selfRelativeBuffer, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(selfRelativeBuffer);
            }
        }

        private uint CalculateEffectiveAccess(IntPtr userSid, IntPtr securityDescriptor)
        {
            SecurityIdentifier targetSid = new(userSid);
            SecurityIdentifier everyoneSid = new(WellKnownSidType.WorldSid, null);
            byte[] descriptorBytes = ConvertToSelfRelative(securityDescriptor);
            if (descriptorBytes.Length == 0)
            {
                return 0;
            }

            RawSecurityDescriptor descriptor = new(descriptorBytes, 0);
            if (descriptor.DiscretionaryAcl is null)
            {
                return 0;
            }

            uint allowed = 0;
            uint denied = 0;
            foreach (GenericAce ace in descriptor.DiscretionaryAcl)
            {
                if (ace is not CommonAce commonAce
                    || commonAce.SecurityIdentifier != targetSid && commonAce.SecurityIdentifier != everyoneSid)
                {
                    continue;
                }

                uint mask = _request.MapGenericAccess?.Invoke(unchecked((uint)commonAce.AccessMask))
                    ?? unchecked((uint)commonAce.AccessMask);
                if (commonAce.AceQualifier == AceQualifier.AccessDenied)
                {
                    denied |= mask;
                }
                else if (commonAce.AceQualifier == AceQualifier.AccessAllowed)
                {
                    allowed |= mask & ~denied;
                }
            }

            return allowed;
        }

        private static uint BuildAccessFlags(AclEditorAccessEntry entry)
        {
            uint flags = entry.IsGeneral ? SiAccessGeneral : 0u;
            if (entry.AppliesToContainersOnly)
            {
                flags |= SiAccessContainer;
            }

            // The low word carries the ACE inheritance flags (CONTAINER_INHERIT_ACE etc.) that the
            // native editor matches against existing ACEs and applies to newly granted rights.
            flags |= entry.InheritFlags;
            return flags;
        }

        private AclEditorNativeMethods.SiAccess[] CreateAccessEntries(IEnumerable<AclEditorAccessEntry> entries)
        {
            return entries
                .Select(entry => new AclEditorNativeMethods.SiAccess
                {
                    Guid = IntPtr.Zero,
                    Mask = entry.Mask,
                    Name = AllocateString(entry.Name),
                    Flags = BuildAccessFlags(entry)
                })
                .ToArray();
        }

        private AclEditorNativeMethods.SiInheritType[] CreateInheritEntries(IEnumerable<AclEditorInheritType> entries)
        {
            return entries
                .Select(entry => new AclEditorNativeMethods.SiInheritType
                {
                    Guid = IntPtr.Zero,
                    Flags = entry.Flags,
                    Name = AllocateString(entry.Name)
                })
                .ToArray();
        }
    }
}

/// <summary>
/// Native interop for the Windows ACL editor.
/// </summary>
/// <remarks>
/// This remains hand-authored because the workflow passes a MANAGED
/// <c>ISecurityInformation</c> callback object into aclui: the interface types are this
/// project's <c>[GeneratedComInterface]</c> definitions (source-generated COM-callable
/// wrapper, AOT-compatible), which CsWin32's marshal-free projection of
/// <c>EditSecurityAdvanced</c> could not accept. It is a single cohesive native workflow
/// (managed COM callback, hand-laid <c>SI_*</c> structs, manual <c>LocalAlloc</c> memory), so the
/// remaining imports stay here rather than fragmenting into a second CsWin32 marshalling model;
/// they use source-generated <c>[LibraryImport]</c> for Native AOT instead of runtime-marshalled
/// <c>[DllImport]</c>.
/// </remarks>
internal static partial class AclEditorNativeMethods
{
    internal const int EInvalidArg = unchecked((int)0x80070057);
    internal const int ENotImpl = unchecked((int)0x80004001);
    internal const int EOutOfMemory = unchecked((int)0x8007000E);
    internal const uint SecurityObjectIdShare = 2;

    /// <summary>
    /// The ISecurityInformation interface identifier (aclui). aclui types the callback parameter
    /// as <c>ISecurityInformation*</c> and calls its methods without a QueryInterface, so the
    /// source-generated wrapper's IUnknown must be queried to this exact interface first.
    /// </summary>
    internal static readonly Guid ISecurityInformationIid = new("965FC360-16FF-11d0-91CB-00AA00BBB723");

    [LibraryImport("aclui.dll")]
    internal static partial int EditSecurityAdvanced(
        IntPtr hwndOwner,
        IntPtr securityInformation,
        uint pageType);

    [LibraryImport("aclui.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EditSecurity(IntPtr hwndOwner, IntPtr securityInformation);

    /// <summary>
    /// Opens the BASIC access control editor (the "Permissions for &lt;object&gt;" dialog) with a
    /// source-generated COM-callable wrapper for <paramref name="securityInformation"/>. Unlike
    /// <see cref="EditSecurityAdvanced(IntPtr, AclEditorService.EditableSecurityInformation, uint)"/>,
    /// aclui's EditSecurity presents the basic SI_PAGE_PERM page; the advanced entry point rejects
    /// that page with E_INVALIDARG. On failure the resulting HRESULT is returned through
    /// <paramref name="errorHResult"/> (0 when the call succeeds).
    /// </summary>
    internal static bool EditSecurity(
        IntPtr hwndOwner,
        AclEditorService.EditableSecurityInformation securityInformation,
        out int errorHResult)
    {
        errorHResult = 0;
        nint unknown = OneMMC.Core.Infrastructure.Interop.ComActivator.ComWrappers
            .GetOrCreateComInterfaceForObject(securityInformation, CreateComInterfaceFlags.None);
        try
        {
            int hr = Marshal.QueryInterface(unknown, in ISecurityInformationIid, out nint callback);
            if (hr < 0)
            {
                errorHResult = hr;
                return false;
            }

            try
            {
                if (EditSecurity(hwndOwner, callback))
                {
                    return true;
                }

                // EditSecurity returns nonzero even when the user cancels; a zero return is a real
                // failure, so surface the Win32 error as an HRESULT (S_OK when none was recorded).
                errorHResult = Marshal.GetHRForLastWin32Error();
                return false;
            }
            finally
            {
                Marshal.Release(callback);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>
    /// Invokes the ACL editor with a source-generated COM-callable wrapper for
    /// <paramref name="securityInformation"/> - the Native-AOT-compatible replacement for the
    /// built-in interface marshalling the old P/Invoke signature relied on (IL2050).
    /// aclui types the parameter as <c>ISecurityInformation*</c> and calls its methods without
    /// a QI, so the wrapper's IUnknown must be queried to that exact interface first.
    /// </summary>
    internal static int EditSecurityAdvanced(
        IntPtr hwndOwner,
        AclEditorService.EditableSecurityInformation securityInformation,
        uint pageType)
    {
        nint unknown = OneMMC.Core.Infrastructure.Interop.ComActivator.ComWrappers
            .GetOrCreateComInterfaceForObject(securityInformation, CreateComInterfaceFlags.None);
        try
        {
            int hr = Marshal.QueryInterface(unknown, in ISecurityInformationIid, out nint callback);
            if (hr < 0)
            {
                return hr;
            }

            try
            {
                return EditSecurityAdvanced(hwndOwner, callback, pageType);
            }
            finally
            {
                Marshal.Release(callback);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    internal static partial uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MakeSelfRelativeSD(
        IntPtr absoluteSecurityDescriptor,
        IntPtr selfRelativeSecurityDescriptor,
        ref uint bufferLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr LocalAlloc(uint flags, UIntPtr bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr LocalFree(IntPtr memory);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetInheritanceSourceW(
        string objectName,
        uint objectType,
        uint securityInformation,
        [MarshalAs(UnmanagedType.Bool)] bool isContainer,
        IntPtr objectClassGuids,
        uint guidCount,
        IntPtr acl,
        IntPtr objectManagerFunctions,
        ref GenericMapping genericMapping,
        IntPtr inheritArray);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SiAccess
    {
        public IntPtr Guid;
        public uint Mask;
        public IntPtr Name;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SiInheritType
    {
        public IntPtr Guid;
        public uint Flags;
        public IntPtr Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityObject
    {
        public IntPtr Name;
        public IntPtr Data;
        public uint DataLength;
        public IntPtr Data2;
        public uint Data2Length;
        public uint Id;
        public byte IsWellKnown;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ObjectTypeList
    {
        public ushort Level;
        public ushort Siblings;
        public IntPtr ObjectType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Acl
    {
        public byte Revision;
        public byte Reserved;
        public ushort Size;
        public ushort AceCount;
        public ushort Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InheritedFrom
    {
        public int GenerationGap;
        public IntPtr AncestorName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GenericMapping
    {
        public uint GenericRead;
        public uint GenericWrite;
        public uint GenericExecute;
        public uint GenericAll;
    }
}
