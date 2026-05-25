using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    internal static class SpecialPolicyHandlersLogging
    {
        internal static ILogger Logger { get; private set; } = NullLogger.Instance;

        internal static void Configure(ILogger logger)
        {
            Logger = logger ?? NullLogger.Instance;
        }
    }

    /// <summary>
    /// Handles the "Accounts: Administrator account status" policy.
    /// Reads/writes whether the built-in Administrator account (RID 500) is enabled.
    /// </summary>
    internal sealed class AdminAccountStatusHandler : ISpecialPolicyHandler
    {
        public string Key => "AdminAccountStatus";

        public void Read(SecurityPolicyValue value)
        {
            value.NumericValue = PolicyNativeHelpers.ReadBuiltInAccountEnabled(500) ? 1 : 0;
            SpecialPolicyHandlersLogging.Logger.LogDebug("[AdminAccountStatusHandler] Admin account status: {Status}", value.NumericValue != 0 ? "Enabled" : "Disabled");
        }

        public void Write(SecurityPolicyValue value)
        {
            PolicyNativeHelpers.WriteBuiltInAccountEnabled(500, value.NumericValue != 0);
            SpecialPolicyHandlersLogging.Logger.LogDebug("[AdminAccountStatusHandler] Wrote Admin account status = {Value}", value.NumericValue);
        }
    }

    /// <summary>
    /// Handles the "Accounts: Guest account status" policy.
    /// Reads/writes whether the built-in Guest account (RID 501) is enabled.
    /// </summary>
    internal sealed class GuestAccountStatusHandler : ISpecialPolicyHandler
    {
        public string Key => "GuestAccountStatus";

        public void Read(SecurityPolicyValue value)
        {
            value.NumericValue = PolicyNativeHelpers.ReadBuiltInAccountEnabled(501) ? 1 : 0;
            SpecialPolicyHandlersLogging.Logger.LogDebug("[GuestAccountStatusHandler] Guest account status: {Status}", value.NumericValue != 0 ? "Enabled" : "Disabled");
        }

        public void Write(SecurityPolicyValue value)
        {
            PolicyNativeHelpers.WriteBuiltInAccountEnabled(501, value.NumericValue != 0);
            SpecialPolicyHandlersLogging.Logger.LogDebug("[GuestAccountStatusHandler] Wrote Guest account status = {Value}", value.NumericValue);
        }
    }

    /// <summary>
    /// Handles the "Accounts: Rename administrator account" policy.
    /// Reads the current display name of the built-in Administrator account.
    /// Write is not supported through this interface.
    /// </summary>
    internal sealed class RenameAdministratorAccountHandler : ISpecialPolicyHandler
    {
        public string Key => "RenameAdministratorAccount";

        public void Read(SecurityPolicyValue value)
        {
            value.StringValue = PolicyNativeHelpers.ReadBuiltInAccountName(500);
            SpecialPolicyHandlersLogging.Logger.LogDebug("[RenameAdministratorAccountHandler] Admin account name: '{Name}'", value.StringValue);
        }

        public void Write(SecurityPolicyValue value)
        {
            SpecialPolicyHandlersLogging.Logger.LogDebug("[RenameAdministratorAccountHandler] Renaming built-in accounts is not supported through this interface.");
        }
    }

    /// <summary>
    /// Handles the "Accounts: Rename guest account" policy.
    /// Reads the current display name of the built-in Guest account.
    /// Write is not supported through this interface.
    /// </summary>
    internal sealed class RenameGuestAccountHandler : ISpecialPolicyHandler
    {
        public string Key => "RenameGuestAccount";

        public void Read(SecurityPolicyValue value)
        {
            value.StringValue = PolicyNativeHelpers.ReadBuiltInAccountName(501);
            SpecialPolicyHandlersLogging.Logger.LogDebug("[RenameGuestAccountHandler] Guest account name: '{Name}'", value.StringValue);
        }

        public void Write(SecurityPolicyValue value)
        {
            SpecialPolicyHandlersLogging.Logger.LogDebug("[RenameGuestAccountHandler] Renaming built-in accounts is not supported through this interface.");
        }
    }

    /// <summary>
    /// Handles the "Network security: Force logoff when logon hours expire" policy.
    /// Uses <c>NetUserModalsGet/Set</c> level 0 to read/write the forced logoff setting.
    /// </summary>
    internal sealed class ForceLogoffHandler : ISpecialPolicyHandler
    {
        public string Key => "ForceLogoffWhenHourExpire";

        public void Read(SecurityPolicyValue value)
        {
            if (PolicyNativeHelpers.TryGetUserModalsInfo(0, out SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info, out int netResult))
            {
                value.NumericValue = info.usrmod0_force_logoff != SecurityPolicyNativeMethods.TIMEQ_FOREVER ? 1 : 0;
                SpecialPolicyHandlersLogging.Logger.LogDebug("[ForceLogoffHandler] ForceLogoff: raw={RawValue}, enabled={EnabledValue}", info.usrmod0_force_logoff, value.NumericValue);
            }
            else
            {
                value.IsDefined = false;
                SpecialPolicyHandlersLogging.Logger.LogDebug("[ForceLogoffHandler] NetUserModalsGet(0) failed: {Result}", netResult);
            }
        }

        public void Write(SecurityPolicyValue value)
        {
            var info = PolicyNativeHelpers.GetUserModalsInfoOrThrow<SecurityPolicyNativeMethods.USER_MODALS_INFO_0>(0);
            info.usrmod0_force_logoff = value.NumericValue != 0 ? 0u : SecurityPolicyNativeMethods.TIMEQ_FOREVER;
            PolicyNativeHelpers.SetUserModalsInfoOrThrow(0, info);
            SpecialPolicyHandlersLogging.Logger.LogDebug("[ForceLogoffHandler] Wrote ForceLogoff = {Value}", value.NumericValue);
        }
    }

    /// <summary>
    /// Handles the "User Account Control: Behavior of the elevation prompt for
    /// administrators running with administrator protection" policy.
    /// This policy maps to <c>ConsentPromptBehaviorEnhancedAdmin</c> and has a
    /// narrower valid option set than <c>ConsentPromptBehaviorAdmin</c>.
    /// Implemented as a special handler to avoid enrichment-map key collisions.
    /// </summary>
    internal sealed class ConsentPromptBehaviorAdminAPHandler : ISpecialPolicyHandler
    {
        public string Key => "ConsentPromptBehaviorAdminAP";

        private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string ValueName = "ConsentPromptBehaviorEnhancedAdmin";

        public void Read(SecurityPolicyValue value)
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            var raw = key?.GetValue(ValueName);
            if (raw is int intVal)
            {
                value.NumericValue = intVal;
                value.IsDefined = true;
            }
            else
            {
                value.IsDefined = false;
            }
            SpecialPolicyHandlersLogging.Logger.LogDebug("[ConsentPromptBehaviorAdminAPHandler] Read: {Value}, defined={IsDefined}", value.NumericValue, value.IsDefined);
        }

        public void Write(SecurityPolicyValue value)
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key != null)
            {
                key.SetValue(ValueName, (int)value.NumericValue, RegistryValueKind.DWord);
                SpecialPolicyHandlersLogging.Logger.LogDebug("[ConsentPromptBehaviorAdminAPHandler] Wrote {Value}", value.NumericValue);
            }
            else
            {
                SpecialPolicyHandlersLogging.Logger.LogDebug("[ConsentPromptBehaviorAdminAPHandler] Failed to open registry key for writing");
            }
        }
    }
}



