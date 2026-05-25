using System.Runtime.InteropServices;
using System.Security.AccessControl;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.SystemAudit;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.SystemAudit;

/// <summary>
/// Reads effective system audit state directly from native audit APIs for verification.
/// </summary>
public static class SystemAuditNativeVerifier
{
    private const string FileObjectTypeName = "File";
    private const string RegistryObjectTypeName = "Key";

    /// <summary>
    /// Reads the effective audit policy flags for a subcategory.
    /// </summary>
    /// <param name="subcategoryGuid">The audit subcategory GUID.</param>
    /// <returns>The result of the effective policy query.</returns>
    public static EffectiveAuditPolicyResult QueryEffectiveAuditPolicy(Guid subcategoryGuid)
    {
        try
        {
            PolicyNativeHelpers.EnsurePrivilegeEnabled(SecurityPolicyNativeMethods.SE_SECURITY_NAME);

            if (!SecurityPolicyNativeMethods.AuditQuerySystemPolicy([subcategoryGuid], 1, out IntPtr policyBuffer))
            {
                return EffectiveAuditPolicyResult.Failed(Marshal.GetLastWin32Error().ToString());
            }

            try
            {
                var policy = Marshal.PtrToStructure<SecurityPolicyNativeMethods.AUDIT_POLICY_INFORMATION>(policyBuffer);
                AuditPolicyFlags flags = AuditPolicyFlags.None;
                if ((policy.AuditingInformation & SecurityPolicyNativeMethods.POLICY_AUDIT_EVENT_SUCCESS) != 0)
                {
                    flags |= AuditPolicyFlags.Success;
                }

                if ((policy.AuditingInformation & SecurityPolicyNativeMethods.POLICY_AUDIT_EVENT_FAILURE) != 0)
                {
                    flags |= AuditPolicyFlags.Failure;
                }

                return EffectiveAuditPolicyResult.CreateSuccess(flags, policy.AuditingInformation);
            }
            finally
            {
                SecurityPolicyNativeMethods.AuditFree(policyBuffer);
            }
        }
        catch (Exception ex)
        {
            return EffectiveAuditPolicyResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Reads the effective Global Object Access Auditing SACL for a resource manager.
    /// </summary>
    /// <param name="resourceType">The resource manager type.</param>
    /// <returns>The result of the effective SACL query.</returns>
    public static EffectiveGlobalSaclResult QueryEffectiveGlobalSacl(SystemAuditResourceType resourceType)
    {
        try
        {
            PolicyNativeHelpers.EnsurePrivilegeEnabled(SecurityPolicyNativeMethods.SE_SECURITY_NAME);

            string objectTypeName = resourceType switch
            {
                SystemAuditResourceType.FileSystem => FileObjectTypeName,
                SystemAuditResourceType.Registry => RegistryObjectTypeName,
                _ => throw new ArgumentOutOfRangeException(nameof(resourceType))
            };

            if (!SecurityPolicyNativeMethods.AuditQueryGlobalSacl(objectTypeName, out IntPtr aclPointer))
            {
                return EffectiveGlobalSaclResult.Failed(Marshal.GetLastWin32Error().ToString());
            }

            try
            {
                if (aclPointer == IntPtr.Zero)
                {
                    return EffectiveGlobalSaclResult.CreateSuccess(string.Empty);
                }

                var aclHeader = Marshal.PtrToStructure<AclHeader>(aclPointer);
                byte[] aclBytes = new byte[aclHeader.AclSize];
                Marshal.Copy(aclPointer, aclBytes, 0, aclBytes.Length);

                var descriptor = new RawSecurityDescriptor(ControlFlags.SystemAclPresent, null, null, new RawAcl(aclBytes, 0), null);
                return EffectiveGlobalSaclResult.CreateSuccess(descriptor.GetSddlForm(AccessControlSections.Audit));
            }
            finally
            {
                SecurityPolicyNativeMethods.LocalFree(aclPointer);
            }
        }
        catch (Exception ex)
        {
            return EffectiveGlobalSaclResult.Failed(ex.Message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AclHeader
    {
        public byte AclRevision;
        public byte Sbz1;
        public ushort AclSize;
        public ushort AceCount;
        public ushort Sbz2;
    }
}

/// <summary>
/// Represents the result of reading an effective audit policy.
/// </summary>
public sealed class EffectiveAuditPolicyResult
{
    private EffectiveAuditPolicyResult()
    {
    }

    /// <summary>
    /// Gets whether the query succeeded.
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Gets the effective audit flags when the query succeeded.
    /// </summary>
    public AuditPolicyFlags Flags { get; init; }

    /// <summary>
    /// Gets the raw auditing information value returned by the native API.
    /// </summary>
    public uint RawAuditingInformation { get; init; }

    /// <summary>
    /// Gets the failure detail when the query did not succeed.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static EffectiveAuditPolicyResult CreateSuccess(AuditPolicyFlags flags, uint rawAuditingInformation) => new()
    {
        Succeeded = true,
        Flags = flags,
        RawAuditingInformation = rawAuditingInformation
    };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static EffectiveAuditPolicyResult Failed(string errorMessage) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage ?? string.Empty
    };
}

/// <summary>
/// Represents the result of reading an effective Global Object Access Auditing SACL.
/// </summary>
public sealed class EffectiveGlobalSaclResult
{
    private EffectiveGlobalSaclResult()
    {
    }

    /// <summary>
    /// Gets whether the query succeeded.
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Gets the effective SDDL when the query succeeded.
    /// </summary>
    public string Sddl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the failure detail when the query did not succeed.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static EffectiveGlobalSaclResult CreateSuccess(string sddl) => new()
    {
        Succeeded = true,
        Sddl = sddl ?? string.Empty
    };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static EffectiveGlobalSaclResult Failed(string errorMessage) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage ?? string.Empty
    };
}
