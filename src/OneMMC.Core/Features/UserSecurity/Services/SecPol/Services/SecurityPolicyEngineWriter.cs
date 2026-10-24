using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Writes an Account or Local policy the way secpol.msc does: through the Security Configuration
    /// Engine's local-policy update, <c>SceUpdateSecurityProfile(NULL, area, profile, SCE_UPDATE_LOCAL_POLICY)</c>.
    /// The engine merges the change into the local security policy database and applies it
    /// immediately, so it is recorded where secpol.msc reads it and survives a policy refresh.
    /// <para>
    /// secpol.msc passes a "changes" profile that carries only the edited setting. This writer builds the
    /// same thing without laying out the undocumented <c>SCE_PROFILE_INFO</c> structure: it writes the
    /// single setting as a security template, lets the engine parse it (<c>SceOpenProfile</c> +
    /// <c>SceGetSecurityProfileInfo</c>), and passes the engine's own structure straight back. The only
    /// field touched is the type tag at offset 0, which the local-policy update checks.
    /// </para>
    /// </summary>
    public sealed class SecurityPolicyEngineWriter
    {
        private const string WorkDirectoryPrefix = "OneMMC-SecPol-";
        private const string ChangesFileName = "changes.inf";

        private readonly ILogger<SecurityPolicyEngineWriter> _logger;

        public SecurityPolicyEngineWriter(ILogger<SecurityPolicyEngineWriter> logger)
        {
            _logger = logger ?? NullLogger<SecurityPolicyEngineWriter>.Instance;
        }

        /// <summary>
        /// Writes a policy value through the local-policy update.
        /// </summary>
        /// <returns>
        /// True when the engine accepted and applied the change; false when the value cannot be expressed
        /// as a local-policy change, so the caller should write it directly.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">The caller is not allowed to change local policy.</exception>
        /// <exception cref="InvalidOperationException">The engine rejected the change.</exception>
        public bool TryWrite(SecurityPolicyValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!SecurityTemplateMapping.TryMap(value, out SecurityTemplateEdit edit))
            {
                _logger.LogDebug("[SecurityPolicyEngineWriter] No template mapping for '{PolicyKey}'; caller writes directly", value.Definition.Key);
                return false;
            }

            // Local policy cannot be returned to Not Defined; secpol.msc offers no such operation either.
            if (edit.IsRemoval)
            {
                _logger.LogDebug("[SecurityPolicyEngineWriter] '{PolicyKey}' is being cleared; caller handles it", value.Definition.Key);
                return false;
            }

            uint area = edit.Section.Equals(SecurityTemplateSections.PrivilegeRights, StringComparison.OrdinalIgnoreCase)
                ? AREA_PRIVILEGES
                : AREA_SECURITY_POLICY;

            string workDirectory = CreatePrivateWorkDirectory();
            try
            {
                string changesPath = Path.Combine(workDirectory, ChangesFileName);
                SecurityTemplateDocument document = SecurityTemplateDocument.Load(changesPath);
                document.Set(edit.Section, edit.Key, edit.Value!);
                document.Save(changesPath);

                ApplyChanges(changesPath, area);
            }
            finally
            {
                DeleteWorkDirectory(workDirectory);
            }

            _logger.LogInformation(
                "[SecurityPolicyEngineWriter] Applied '{PolicyKey}' as [{Section}] {Key} to local security policy",
                value.Definition.Key, edit.Section, edit.Key);
            return true;
        }

        /// <summary>
        /// Parses the changes template through the engine and merges it into the local security policy.
        /// </summary>
        private void ApplyChanges(string changesPath, uint area)
        {
            int status = SceOpenProfile(changesPath, SCE_INF_FORMAT, out IntPtr template);
            ThrowIfFailed(status, nameof(SceOpenProfile));

            IntPtr profile = IntPtr.Zero;
            try
            {
                status = SceGetSecurityProfileInfo(template, SCE_PROFILE_TEMPLATE, area, out profile, out IntPtr errorLog);
                if (errorLog != IntPtr.Zero)
                    _logger.LogDebug("[SecurityPolicyEngineWriter] The engine reported template parse diagnostics");
                ThrowIfFailed(status, nameof(SceGetSecurityProfileInfo));

                // The local-policy update only accepts an engine-tagged profile (see SCE_PROFILE_TAG_ENGINE).
                Marshal.WriteInt32(profile, SCE_PROFILE_TAG_ENGINE);

                status = SceUpdateSecurityProfile(IntPtr.Zero, area, profile, SCE_UPDATE_LOCAL_POLICY);
                ThrowIfFailed(status, nameof(SceUpdateSecurityProfile));
            }
            finally
            {
                if (profile != IntPtr.Zero)
                    SceFreeProfileMemory(profile);
                SceCloseProfile(ref template);
            }
        }

        private static void ThrowIfFailed(int status, string operation)
        {
            if (status == SCESTATUS_SUCCESS)
                return;

            if (status == SCESTATUS_ACCESS_DENIED)
                throw new UnauthorizedAccessException($"{operation} was denied access to the local security policy (SCESTATUS {status}).");

            throw new InvalidOperationException($"{operation} failed with SCESTATUS {status}.");
        }

        /// <summary>
        /// Creates a uniquely named directory under the Windows temporary directory that only
        /// Administrators and SYSTEM can open.
        /// <para>
        /// The changes template is parsed by an elevated process. In the user's own <c>%TEMP%</c> a
        /// non-elevated process of the same user could replace it between write and parse and so inject
        /// settings into local policy. Under <c>%windir%\Temp</c> ordinary users cannot rename or delete
        /// another account's entries, and this directory's protected DACL admits neither the user nor a
        /// filtered (non-elevated) token, whose Administrators membership is deny-only.
        /// </para>
        /// </summary>
        private static string CreatePrivateWorkDirectory()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (WellKnownSidType sidType in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(sidType, domainSid: null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            // A fresh GUID per write; an existing directory of that name is never reused, since its
            // security would not be ours.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string path = Path.Combine(root, WorkDirectoryPrefix + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(path))
                    continue;

                FileSystemAclExtensions.CreateDirectory(security, path);
                return path;
            }

            throw new IOException("Could not create a private working directory for the security policy change.");
        }

        private void DeleteWorkDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[SecurityPolicyEngineWriter] Could not delete working directory {Path}", path);
            }
        }
    }
}
