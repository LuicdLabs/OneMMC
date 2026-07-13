using System;
using System.Collections.Generic;
using System.Security.AccessControl;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SystemAudit;
using OneMMC.Core.Infrastructure.WindowsCapabilities;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SystemAudit;

/// <summary>
/// Opens the native Windows ACL editor for Global Object Access Auditing policies.
/// </summary>
public sealed class SystemAuditAclEditorService
{
    private const uint DeleteAccess = 0x00010000;
    private const uint ReadControlAccess = 0x00020000;
    private const uint WriteDacAccess = 0x00040000;
    private const uint WriteOwnerAccess = 0x00080000;
    private const uint SynchronizeAccess = 0x00100000;

    private const uint GenericReadAccess = 0x80000000;
    private const uint GenericWriteAccess = 0x40000000;
    private const uint GenericExecuteAccess = 0x20000000;
    private const uint GenericAllAccess = 0x10000000;

    private const uint FileReadData = 0x0001;
    private const uint FileWriteData = 0x0002;
    private const uint FileAppendData = 0x0004;
    private const uint FileReadExtendedAttributes = 0x0008;
    private const uint FileWriteExtendedAttributes = 0x0010;
    private const uint FileExecute = 0x0020;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileWriteAttributes = 0x0100;
    private const uint FileAllAccess = 0x001F01FF;
    private const uint FileGenericRead = ReadControlAccess | FileReadData | FileReadAttributes | FileReadExtendedAttributes | SynchronizeAccess;
    private const uint FileGenericWrite = ReadControlAccess | FileWriteData | FileAppendData | FileWriteAttributes | FileWriteExtendedAttributes | SynchronizeAccess;
    private const uint FileGenericExecute = ReadControlAccess | FileReadAttributes | FileExecute | SynchronizeAccess;

    private const uint KeyRead = 0x00020019;
    private const uint KeyWrite = 0x00020006;
    private const uint KeyAllAccess = 0x000F003F;

    private readonly AclEditorService _aclEditorService;
    private readonly ILogger<SystemAuditAclEditorService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemAuditAclEditorService"/> class.
    /// </summary>
    /// <param name="aclEditorService">The common ACL editor service.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public SystemAuditAclEditorService(
        AclEditorService aclEditorService,
        ILogger<SystemAuditAclEditorService> logger)
    {
        _aclEditorService = aclEditorService;
        _logger = logger;
    }

    /// <summary>
    /// Opens the Windows ACL editor on the Auditing page for the supplied Global Object Access item.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows' own Global Object Access Auditing UI (Local Security Policy, under Advanced Audit
    /// Policy Configuration / Global Object Access Auditing / File system or Registry / Configure)
    /// shows an "Advanced Security Settings for Global File SACL" (or "...Global Registry SACL")
    /// dialog that contains ONLY an Auditing tab: no Permissions tab, and no Name/Owner header. That
    /// is because a global SACL is an audit-only object which has no DACL (and no owner) to edit.
    /// </para>
    /// <para>
    /// This method instead drives the public Windows access-control editor (aclui) through
    /// <see cref="AclEditorService.EditSecurity"/>, which for any non-basic page invokes the native
    /// <c>EditSecurityAdvanced</c> function. <c>EditSecurityAdvanced</c> renders the STANDARD
    /// advanced security property sheet, and that sheet ALWAYS includes a Permissions (DACL) tab in
    /// addition to the Auditing tab. Consequently our dialog shows an extra Permissions tab that the
    /// native dialog does not, and on that tab aclui prints its built-in "No permissions have been
    /// assigned for this object / this is a potential security risk..." text, because the audit-only
    /// security descriptor built by <see cref="AclEditorRequest.EmptySecurityDescriptorFactory"/>
    /// deliberately carries a SACL but no DACL.
    /// </para>
    /// <para>
    /// There is no public aclui flag or function that removes the Permissions page from the advanced
    /// sheet. The relevant SI_OBJECT_INFO dwFlags were all evaluated and ruled out:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>SI_EDIT_PERMS</c> is <c>0x00000000</c>, the always-on default. Its documentation states
    ///     the basic security page ALWAYS displays the DACL controls, and that they can only be
    ///     disabled with <c>SI_READONLY</c>, never hidden. There is no "no permissions page" bit.
    ///   </description></item>
    ///   <item><description>
    ///     <c>SI_NO_ADDITIONAL_PERMISSION</c> (<c>0x00200000</c>) hides only the "Special Permissions"
    ///     sub-tab shown inside the advanced Permissions page, not the Permissions page itself.
    ///   </description></item>
    ///   <item><description>
    ///     <c>SI_READONLY</c> / <c>SI_VIEW_ONLY</c> only make the pages non-editable; they do not
    ///     remove the Permissions tab, and they would also freeze the Auditing tab we need editable.
    ///   </description></item>
    ///   <item><description>
    ///     <c>CreateSecurityPage</c> (the only other public aclui entry point) likewise creates the
    ///     BASIC DACL page. aclui exposes no "auditing-only" page factory.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Native secpol produces its audit-only dialog through a wsecedit-internal UI that is not
    /// reachable from the public aclui surface, so third-party callers cannot reproduce it by tuning
    /// flags. Fully matching the native dialog would require replacing this aclui call with a bespoke
    /// auditing editor: a SACL entry list (Principal / Type / Access / Inherited from) plus an
    /// "Auditing Entry" add/edit dialog, built from the access masks and inherit types already
    /// defined in this class (see <see cref="CreateAccessEntries"/> and <see cref="CreateInheritEntries"/>),
    /// the SDDL round-trip in <see cref="ApplyResult"/>, and the existing directory object picker.
    /// That larger change is intentionally deferred; this path knowingly shows the extra Permissions
    /// tab as an accepted, documented deviation from native.
    /// </para>
    /// <para>
    /// References (Microsoft Learn, header aclui.h): the SI_OBJECT_INFO dwFlags table
    /// (ns-aclui-si_object_info), EditSecurityAdvanced (nf-aclui-editsecurityadvanced), and
    /// CreateSecurityPage (nf-aclui-createsecuritypage).
    /// </para>
    /// </remarks>
    /// <param name="subcategory">The item whose SACL is being edited.</param>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    public void EditGlobalObjectAccessPolicy(AuditSubcategoryValue subcategory, IntPtr ownerWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(subcategory);

        SystemAuditResourceType resourceType = subcategory.ResourceType;
        string objectName = GetGlobalSaclObjectName(resourceType);
        AclEditorRequest request = new()
        {
            ObjectName = objectName,
            PageTitle = objectName,
            SecurityDescriptorSddl = subcategory.GlobalSaclSddl,

            // Requests the advanced sheet's Auditing page. Note: aclui's EditSecurityAdvanced still
            // adds a Permissions (DACL) tab that native's audit-only Global File/Registry SACL dialog
            // omits, and no aclui flag can remove it. See the detailed remarks on this method.
            PageType = AclEditorPageType.Auditing,

            // EditAudits shows the Auditing page; NoAclProtect/NoTreeApply hide the inheritance
            // controls that do not apply to a global SACL. EditOwner is intentionally absent so no
            // Owner page is offered. (None of these suppress the always-present Permissions tab.)
            ObjectInformationFlags =
                AclEditorObjectFlags.Advanced
                | AclEditorObjectFlags.EditAudits
                | AclEditorObjectFlags.Container
                | AclEditorObjectFlags.PageTitle
                | AclEditorObjectFlags.NoAclProtect
                | AclEditorObjectFlags.NoTreeApply,
            MapGenericAccess = mask => MapGenericAccess(mask, resourceType),
            EmptySecurityDescriptorFactory = static () =>
                new RawSecurityDescriptor(ControlFlags.SystemAclPresent, null, null, new RawAcl(2, 0), null)
        };

        request.AccessEntries.AddRange(CreateAccessEntries(resourceType));
        request.InheritTypes.AddRange(CreateInheritEntries(resourceType));

        AclEditorResult result = _aclEditorService.EditSecurity(ownerWindowHandle, request);
        if (result.WasModified)
        {
            ApplyResult(subcategory, result.SecurityDescriptor);
        }
    }

    private void ApplyResult(AuditSubcategoryValue target, RawSecurityDescriptor descriptor)
    {
        if (descriptor.SystemAcl is not { Count: > 0 } sacl)
        {
            _logger.LogDebug("[SystemAuditAclEditorService] Clearing empty global object access SACL for {DisplayName}.", target.DisplayName);
            target.IsDefined = false;
            target.GlobalSaclSddl = string.Empty;
            target.GlobalSaclBinary = null;
            return;
        }

        target.IsDefined = true;
        target.GlobalSaclSddl = descriptor.GetSddlForm(AccessControlSections.Audit);
        target.GlobalSaclBinary = GetSaclBinary(sacl);
    }

    private static byte[]? GetSaclBinary(RawAcl? sacl)
    {
        if (sacl is null)
        {
            return null;
        }

        byte[] bytes = new byte[sacl.BinaryLength];
        sacl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static uint MapGenericAccess(uint mask, SystemAuditResourceType resourceType)
    {
        uint genericRead;
        uint genericWrite;
        uint genericExecute;
        uint genericAll;

        if (resourceType == SystemAuditResourceType.Registry)
        {
            genericRead = KeyRead;
            genericWrite = KeyWrite;
            genericExecute = KeyRead;
            genericAll = KeyAllAccess;
        }
        else
        {
            genericRead = FileGenericRead;
            genericWrite = FileGenericWrite;
            genericExecute = FileGenericExecute;
            genericAll = FileAllAccess;
        }

        if ((mask & GenericReadAccess) != 0)
        {
            mask = (mask & ~GenericReadAccess) | genericRead;
        }

        if ((mask & GenericWriteAccess) != 0)
        {
            mask = (mask & ~GenericWriteAccess) | genericWrite;
        }

        if ((mask & GenericExecuteAccess) != 0)
        {
            mask = (mask & ~GenericExecuteAccess) | genericExecute;
        }

        if ((mask & GenericAllAccess) != 0)
        {
            mask = (mask & ~GenericAllAccess) | genericAll;
        }

        return mask;
    }

    private static IEnumerable<AclEditorAccessEntry> CreateAccessEntries(SystemAuditResourceType resourceType)
    {
        return resourceType switch
        {
            SystemAuditResourceType.Registry =>
            [
                CreateAccessEntry(KeyAllAccess, SecPolKeys.SystemAuditAclFullControl),
                CreateAccessEntry(KeyRead, SecPolKeys.SystemAuditAclRead),
                CreateAccessEntry(KeyWrite, SecPolKeys.SystemAuditAclWrite),
                CreateAccessEntry(DeleteAccess, SecPolKeys.SystemAuditAclDelete)
            ],
            _ =>
            [
                CreateAccessEntry(FileAllAccess, SecPolKeys.SystemAuditAclFullControl),
                CreateAccessEntry(FileGenericRead | FileGenericExecute, SecPolKeys.SystemAuditAclReadAndExecute),
                CreateAccessEntry(FileGenericRead, SecPolKeys.SystemAuditAclRead),
                CreateAccessEntry(FileGenericWrite, SecPolKeys.SystemAuditAclWrite),
                CreateAccessEntry(DeleteAccess, SecPolKeys.SystemAuditAclDelete)
            ]
        };
    }

    private static IEnumerable<AclEditorInheritType> CreateInheritEntries(SystemAuditResourceType resourceType)
    {
        return resourceType switch
        {
            SystemAuditResourceType.Registry =>
            [
                CreateInheritEntry(0, SecPolKeys.SystemAuditAclThisKeyOnly),
                CreateInheritEntry(AclEditorAceFlags.ContainerInherit, SecPolKeys.SystemAuditAclThisKeyAndSubkeys),
                CreateInheritEntry(AclEditorAceFlags.ContainerInherit | AclEditorAceFlags.InheritOnly, SecPolKeys.SystemAuditAclSubkeysOnly)
            ],
            _ =>
            [
                CreateInheritEntry(0, SecPolKeys.SystemAuditAclThisFolderOnly),
                CreateInheritEntry(AclEditorAceFlags.ContainerInherit | AclEditorAceFlags.ObjectInherit, SecPolKeys.SystemAuditAclThisFolderSubfoldersFiles),
                CreateInheritEntry(AclEditorAceFlags.ContainerInherit, SecPolKeys.SystemAuditAclThisFolderAndSubfolders),
                CreateInheritEntry(AclEditorAceFlags.ObjectInherit, SecPolKeys.SystemAuditAclThisFolderAndFiles),
                CreateInheritEntry(AclEditorAceFlags.ContainerInherit | AclEditorAceFlags.ObjectInherit | AclEditorAceFlags.InheritOnly, SecPolKeys.SystemAuditAclSubfoldersAndFilesOnly),
                CreateInheritEntry(AclEditorAceFlags.ContainerInherit | AclEditorAceFlags.InheritOnly, SecPolKeys.SystemAuditAclSubfoldersOnly),
                CreateInheritEntry(AclEditorAceFlags.ObjectInherit | AclEditorAceFlags.InheritOnly, SecPolKeys.SystemAuditAclFilesOnly)
            ]
        };
    }

    private static AclEditorAccessEntry CreateAccessEntry(uint mask, string resourceKey)
    {
        return new AclEditorAccessEntry
        {
            Mask = mask,
            Name = LocalizedString(resourceKey, resourceKey)
        };
    }

    private static AclEditorInheritType CreateInheritEntry(uint flags, string resourceKey)
    {
        return new AclEditorInheritType
        {
            Flags = flags,
            Name = LocalizedString(resourceKey, resourceKey)
        };
    }

    private static string LocalizedString(string key, string fallback)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
        {
            return fallback;
        }

        return value;
    }

    private static string GetGlobalSaclObjectName(SystemAuditResourceType resourceType)
    {
        return resourceType == SystemAuditResourceType.Registry
            ? LocalizedString(SecPolKeys.SystemAuditGlobalRegistrySacl, "Global Registry SACL")
            : LocalizedString(SecPolKeys.SystemAuditGlobalFileSacl, "Global File SACL");
    }
}
