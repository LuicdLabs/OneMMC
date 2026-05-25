using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Infrastructure.WindowsCapabilities;

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
    TakeOwnership = 5
}

/// <summary>
/// Defines object-information flags used by the native ACL editor.
/// </summary>
public static class AclEditorObjectFlags
{
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
/// Contains the configuration required to open the native ACL editor.
/// </summary>
public sealed class AclEditorRequest
{
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
}

/// <summary>
/// Opens the native Windows ACL editor for callers that provide security descriptor state.
/// </summary>
public sealed class AclEditorService
{
    private const int S_OK = 0;
    private const uint SiAccessGeneral = 0x00020000;
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
        int hr = AclEditorNativeMethods.EditSecurityAdvanced(
            ownerWindowHandle,
            securityInformation,
            request.PageType);

        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return new AclEditorResult
        {
            WasModified = securityInformation.WasModified,
            SecurityDescriptor = securityInformation.SecurityDescriptor
        };
    }

    [ComVisible(true)]
    private sealed class EditableSecurityInformation : AclEditorNativeMethods.ISecurityInformation, IDisposable
    {
        private readonly AclEditorRequest _request;
        private readonly List<IntPtr> _allocatedStrings = [];
        private readonly ILogger _logger;
        private readonly IntPtr _objectNamePointer;
        private readonly IntPtr _pageTitlePointer;
        private readonly AclEditorNativeMethods.SiAccess[] _accessEntries;
        private readonly IntPtr _accessEntriesPointer;
        private readonly AclEditorNativeMethods.SiInheritType[] _inheritEntries;
        private readonly IntPtr _inheritEntriesPointer;

        public EditableSecurityInformation(AclEditorRequest request, ILogger logger)
        {
            _request = request;
            _logger = logger;
            SecurityDescriptor = CreateDescriptor(request);

            string objectName = string.IsNullOrWhiteSpace(request.ObjectName) ? request.PageTitle : request.ObjectName;
            string pageTitle = string.IsNullOrWhiteSpace(request.PageTitle) ? objectName : request.PageTitle;
            _objectNamePointer = AllocateString(objectName);
            _pageTitlePointer = AllocateString(pageTitle);

            _accessEntries = CreateAccessEntries(request.AccessEntries);
            _accessEntriesPointer = AllocateStructureArray(_accessEntries);

            _inheritEntries = CreateInheritEntries(request.InheritTypes);
            _inheritEntriesPointer = AllocateStructureArray(_inheritEntries);
        }

        public RawSecurityDescriptor SecurityDescriptor { get; private set; }

        public bool WasModified { get; private set; }

        public void Dispose()
        {
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
        }

        public int GetObjectInformation(out AclEditorNativeMethods.SiObjectInfo objectInfo)
        {
            objectInfo = new AclEditorNativeMethods.SiObjectInfo
            {
                Flags = _request.ObjectInformationFlags,
                Instance = IntPtr.Zero,
                ServerName = IntPtr.Zero,
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

                SecurityDescriptor = new RawSecurityDescriptor(descriptorBytes, 0);
                WasModified = true;
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

        private IntPtr AllocateString(string value)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(value);
            _allocatedStrings.Add(pointer);
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

        private AclEditorNativeMethods.SiAccess[] CreateAccessEntries(IEnumerable<AclEditorAccessEntry> entries)
        {
            return entries
                .Select(entry => new AclEditorNativeMethods.SiAccess
                {
                    Guid = IntPtr.Zero,
                    Mask = entry.Mask,
                    Name = AllocateString(entry.Name),
                    Flags = entry.IsGeneral ? SiAccessGeneral : 0
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
/// This remains hand-authored because the workflow relies on a managed
/// <c>ISecurityInformation</c> callback object whose COM marshalling is more
/// predictable when kept in one dedicated wrapper.
/// </remarks>
internal static class AclEditorNativeMethods
{
    [DllImport("aclui.dll", ExactSpelling = true)]
    internal static extern int EditSecurityAdvanced(
        IntPtr hwndOwner,
        [MarshalAs(UnmanagedType.Interface)] ISecurityInformation securityInformation,
        AclEditorPageType pageType);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MakeSelfRelativeSD(
        IntPtr absoluteSecurityDescriptor,
        IntPtr selfRelativeSecurityDescriptor,
        ref uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LocalAlloc(uint flags, UIntPtr bytes);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SiObjectInfo
    {
        public uint Flags;
        public IntPtr Instance;
        public IntPtr ServerName;
        public IntPtr ObjectName;
        public IntPtr PageTitle;
        public Guid ObjectType;
    }

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

    [ComVisible(true)]
    [Guid("965FC360-16FF-11d0-91CB-00AA00BBB723")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISecurityInformation
    {
        [PreserveSig]
        int GetObjectInformation(out SiObjectInfo objectInfo);

        [PreserveSig]
        int GetSecurity(uint requestedInformation, out IntPtr securityDescriptor, [MarshalAs(UnmanagedType.Bool)] bool defaultSecurity);

        [PreserveSig]
        int SetSecurity(uint securityInformation, IntPtr securityDescriptor);

        [PreserveSig]
        int GetAccessRights(IntPtr objectTypeGuid, uint flags, out IntPtr access, out uint accessCount, out uint defaultAccess);

        [PreserveSig]
        int MapGeneric(IntPtr objectTypeGuid, ref byte aceFlags, ref uint mask);

        [PreserveSig]
        int GetInheritTypes(out IntPtr inheritTypes, out uint inheritTypeCount);

        [PreserveSig]
        int PropertySheetPageCallback(IntPtr hwnd, uint message, AclEditorPageType pageType);
    }
}
