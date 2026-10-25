using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Localization;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Reads and writes Account and Local policies the way secpol.msc does: through the Security
    /// Configuration Engine (scecli.dll), which owns the SAM, LSA and registry mechanics behind them.
    /// <list type="bullet">
    ///   <item>
    ///     Reads take the system's current settings with
    ///     <c>SceGetSecurityProfileInfo(NULL, SCE_ENGINE_SYSTEM, area)</c> and have the engine write
    ///     them out as a security template (<c>SceWriteSecurityProfileInfo</c>), the same text
    ///     <c>secedit /export</c> produces.
    ///   </item>
    ///   <item>
    ///     Writes use the local-policy update secpol.msc makes,
    ///     <c>SceUpdateSecurityProfile(NULL, area, profile, SCE_UPDATE_LOCAL_POLICY)</c>, with a
    ///     "changes" profile carrying only the edited setting. The engine merges it into the local
    ///     security policy database and applies it immediately.
    ///   </item>
    /// </list>
    /// <para>
    /// The engine's settings go through a template file rather than the undocumented <c>SCE_PROFILE_INFO</c>
    /// layout: the engine parses and produces the structure itself. Registry payloads are read directly
    /// by the mapping layer because the engine's text export loses multi-string item boundaries.
    /// The only profile field touched is the
    /// type tag at offset 0, which the local-policy update checks.
    /// </para>
    /// </summary>
    public sealed class SecurityPolicyEngine
    {
        private const string WorkDirectoryPrefix = "OneMMC-SecPol-";
        private const string TemplateFileName = "policy.inf";

        private readonly ILogger<SecurityPolicyEngine> _logger;
        private readonly IAdminService _adminService;

        /// <summary>Initializes the engine with logging and the shared administrator service.</summary>
        public SecurityPolicyEngine(ILogger<SecurityPolicyEngine> logger, IAdminService adminService)
        {
            _logger = logger ?? NullLogger<SecurityPolicyEngine>.Instance;
            _adminService = adminService;
        }

        /// <summary>Reads the system's current settings for a category's area.</summary>
        /// <exception cref="UnauthorizedAccessException">The caller is not allowed to read local policy.</exception>
        /// <exception cref="InvalidOperationException">The engine failed to read the settings.</exception>
        internal SecurityTemplate Read(SecurityPolicyCategory category)
        {
            EnsureAdministrator();
            uint area = SecurityTemplateMapping.AreaFor(category);
            string workDirectory = CreatePrivateWorkDirectory();
            try
            {
                string path = Path.Combine(workDirectory, TemplateFileName);
                ExportSystemSettings(path, area);
                return SecurityTemplate.Load(path);
            }
            finally
            {
                DeleteWorkDirectory(workDirectory);
            }
        }

        /// <summary>Writes a policy value through the local-policy update.</summary>
        /// <exception cref="NotSupportedException">The policy has no security template line.</exception>
        /// <exception cref="UnauthorizedAccessException">The caller is not allowed to change local policy.</exception>
        /// <exception cref="InvalidOperationException">The engine rejected the change.</exception>
        public void Write(SecurityPolicyValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            EnsureAdministrator();

            if (!SecurityTemplateMapping.TryMap(value, out SecurityTemplateEdit edit))
                throw new NotSupportedException($"Policy '{value.Definition.Key}' has no security template setting.");

            string workDirectory = CreatePrivateWorkDirectory();
            try
            {
                string path = Path.Combine(workDirectory, TemplateFileName);
                SecurityTemplate.WriteSingleSetting(path, edit);
                ApplyChanges(path, SecurityTemplateMapping.AreaFor(edit.Section));
            }
            finally
            {
                DeleteWorkDirectory(workDirectory);
            }

            _logger.LogInformation(
                "[SecurityPolicyEngine] Applied '{PolicyKey}' as [{Section}] {Key} to local security policy",
                value.Definition.Key, edit.Section, edit.Key);
        }

        /// <summary>Has the engine write the system's current settings for an area to a template.</summary>
        private void ExportSystemSettings(string path, uint area)
        {
            int status = SceGetSecurityProfileInfo(IntPtr.Zero, SCE_ENGINE_SYSTEM, area, out IntPtr profile, out IntPtr errorLog);
            if (errorLog != IntPtr.Zero)
                _logger.LogDebug("[SecurityPolicyEngine] The engine reported diagnostics reading the system settings");

            try
            {
                ThrowIfFailed(status, nameof(SceGetSecurityProfileInfo));

                status = SceWriteSecurityProfileInfo(path, area, profile, out _);
                ThrowIfFailed(status, nameof(SceWriteSecurityProfileInfo));
            }
            finally
            {
                if (profile != IntPtr.Zero)
                    SceFreeProfileMemory(profile);
            }
        }

        /// <summary>
        /// Parses the changes template through the engine and merges it into the local security policy.
        /// </summary>
        private void ApplyChanges(string changesPath, uint area)
        {
            IntPtr template = IntPtr.Zero;
            IntPtr profile = IntPtr.Zero;
            try
            {
                int status = SceOpenProfile(changesPath, SCE_INF_FORMAT, out template);
                ThrowIfFailed(status, nameof(SceOpenProfile));

                status = SceGetSecurityProfileInfo(template, SCE_PROFILE_TEMPLATE, area, out profile, out IntPtr errorLog);
                if (errorLog != IntPtr.Zero)
                    _logger.LogDebug("[SecurityPolicyEngine] The engine reported template parse diagnostics");
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
                if (template != IntPtr.Zero)
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

        private void EnsureAdministrator()
        {
            if (!_adminService.IsRunningAsAdmin)
                throw new UnauthorizedAccessException(LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic));
        }

        /// <summary>
        /// Creates a uniquely named directory under the Windows temporary directory that only
        /// Administrators and SYSTEM can open.
        /// <para>
        /// The template is written and parsed by an elevated process. In the user's own <c>%TEMP%</c> a
        /// non-elevated process of the same user could replace a changes template between write and
        /// parse and so inject settings into local policy, or read the exported policy. Under
        /// <c>%windir%\Temp</c> ordinary users cannot rename or delete another account's entries, and
        /// this directory's protected DACL admits neither the user nor a filtered (non-elevated) token,
        /// whose Administrators membership is deny-only.
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

            // A fresh GUID per operation; an existing directory of that name is never reused, since its
            // security would not be ours.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string path = Path.Combine(root, WorkDirectoryPrefix + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(path))
                    continue;

                FileSystemAclExtensions.CreateDirectory(security, path);
                return path;
            }

            throw new IOException("Could not create a private working directory for the security policy.");
        }

        private void DeleteWorkDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[SecurityPolicyEngine] Could not delete working directory {Path}", path);
            }
        }
    }
}
