using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.Services.SecPol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Handles Account Lockout Policy reading/writing via NetUserModalsGet/Set (level 3) and SAM APIs.
    /// </summary>
    internal sealed class AccountLockoutPolicyProvider : IPolicyProvider
    {
        private readonly ILogger<AccountLockoutPolicyProvider> _logger;

        public AccountLockoutPolicyProvider()
            : this(NullLogger<AccountLockoutPolicyProvider>.Instance)
        {
        }

        public AccountLockoutPolicyProvider(ILogger<AccountLockoutPolicyProvider> logger)
        {
            _logger = logger;
        }

        public SecurityPolicyCategory Category => SecurityPolicyCategory.AccountLockoutPolicy;

        #region Handler Dictionaries

        private delegate long UserModals3Reader(in SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info);
        private delegate void UserModals3Writer(ref SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info, long value);

        private static readonly Dictionary<string, UserModals3Reader> ReadHandlers = new(StringComparer.Ordinal)
        {
            ["LockoutDuration"] = static (in SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info) => info.usrmod3_lockout_duration / 60,
            ["LockoutThreshold"] = static (in SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info) => info.usrmod3_lockout_threshold,
            ["LockoutObservationWindow"] = static (in SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info) => info.usrmod3_lockout_observation_window / 60
        };

        private static readonly Dictionary<string, UserModals3Writer> WriteHandlers = new(StringComparer.Ordinal)
        {
            ["LockoutDuration"] = static (ref SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info, long value) => info.usrmod3_lockout_duration = (uint)(value * 60),
            ["LockoutThreshold"] = static (ref SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info, long value) => info.usrmod3_lockout_threshold = (uint)value,
            ["LockoutObservationWindow"] = static (ref SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info, long value) => info.usrmod3_lockout_observation_window = (uint)(value * 60)
        };

        #endregion

        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions()
        {
            return SecurityPolicyDefinitions.GetAccountLockoutPolicyDefinitions();
        }

        public SecurityPolicyValue ReadPolicy(SecurityPolicyDefinition definition)
        {
            var value = new SecurityPolicyValue { Definition = definition, IsDefined = true };

            // SAM-based lockout property
            if (definition.Key == "AllowAdminLockout")
            {
                uint props = PolicyNativeHelpers.ReadSamPasswordProperties();
                value.NumericValue = (props & DOMAIN_PASSWORD_LOCKOUT_ADMINS) != 0 ? 1 : 0;
                _logger.LogDebug("[AccountLockoutPolicyProvider] SAM property '{PolicyKey}' = {PolicyValue} (props=0x{Props:X8})", definition.Key, value.NumericValue, props);
                return value;
            }

            // Some lockout policies are registry-based
            if (!string.IsNullOrEmpty(definition.RegistryKeyPath))
            {
                return PolicyNativeHelpers.ReadRegistryValue(definition);
            }

            if (!PolicyNativeHelpers.TryGetUserModalsInfo(3, out SecurityPolicyNativeMethods.USER_MODALS_INFO_3 info, out int result))
            {
                _logger.LogDebug("[AccountLockoutPolicyProvider] NetUserModalsGet(3) failed: {Result}", result);
                value.IsDefined = false;
                return value;
            }

            if (ReadHandlers.TryGetValue(definition.Key, out var readHandler))
            {
                value.NumericValue = readHandler(info);
            }
            else
            {
                value.IsDefined = false;
            }

            _logger.LogDebug("[AccountLockoutPolicyProvider] '{PolicyKey}' = {PolicyValue}", definition.Key, value.NumericValue);
            return value;
        }

        public void WritePolicy(SecurityPolicyValue value)
        {
            // SAM-based lockout property
            if (value.Definition.Key == "AllowAdminLockout")
            {
                PolicyNativeHelpers.WriteSamPasswordProperty(DOMAIN_PASSWORD_LOCKOUT_ADMINS, value.NumericValue != 0);
                _logger.LogDebug("[AccountLockoutPolicyProvider] Wrote SAM property '{PolicyKey}' = {PolicyValue}", value.Definition.Key, value.NumericValue);
                return;
            }

            if (!string.IsNullOrEmpty(value.Definition.RegistryKeyPath))
            {
                PolicyNativeHelpers.WriteRegistryValue(value);
                return;
            }

            var info = PolicyNativeHelpers.GetUserModalsInfoOrThrow<SecurityPolicyNativeMethods.USER_MODALS_INFO_3>(3);

            if (!WriteHandlers.TryGetValue(value.Definition.Key, out var writeHandler))
                throw new NotSupportedException($"Lockout policy '{value.Definition.Key}' is not supported for writing.");

            writeHandler(ref info, value.NumericValue);

            PolicyNativeHelpers.SetUserModalsInfoOrThrow(3, info);
            _logger.LogDebug("[AccountLockoutPolicyProvider] Wrote '{PolicyKey}' = {PolicyValue}", value.Definition.Key, value.NumericValue);
        }
    }
}




