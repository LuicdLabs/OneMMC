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
    /// Handles Password Policy reading/writing via NetUserModalsGet/Set (level 0) and SAM APIs.
    /// </summary>
    internal sealed class PasswordPolicyProvider : IPolicyProvider
    {
        private readonly ILogger<PasswordPolicyProvider> _logger;

        public PasswordPolicyProvider()
            : this(NullLogger<PasswordPolicyProvider>.Instance)
        {
        }

        public PasswordPolicyProvider(ILogger<PasswordPolicyProvider> logger)
        {
            _logger = logger;
        }

        public SecurityPolicyCategory Category => SecurityPolicyCategory.PasswordPolicy;

        #region Handler Dictionaries

        private delegate long UserModals0Reader(in SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info);
        private delegate void UserModals0Writer(ref SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info, long value);

        private static readonly Dictionary<string, UserModals0Reader> ReadHandlers = new(StringComparer.Ordinal)
        {
            ["PasswordHistoryLength"] = static (in SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info) => info.usrmod0_password_hist_len,
            ["MaxPasswordAge"] = static (in SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info) => info.usrmod0_max_passwd_age == SecurityPolicyNativeMethods.TIMEQ_FOREVER ? 0 : info.usrmod0_max_passwd_age / 86400,
            ["MinPasswordAge"] = static (in SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info) => info.usrmod0_min_passwd_age / 86400,
            ["MinPasswordLength"] = static (in SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info) => info.usrmod0_min_passwd_len
        };

        private static readonly Dictionary<string, UserModals0Writer> WriteHandlers = new(StringComparer.Ordinal)
        {
            ["PasswordHistoryLength"] = static (ref SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info, long value) => info.usrmod0_password_hist_len = (uint)value,
            ["MaxPasswordAge"] = static (ref SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info, long value) => info.usrmod0_max_passwd_age = value == 0 ? SecurityPolicyNativeMethods.TIMEQ_FOREVER : (uint)(value * 86400),
            ["MinPasswordAge"] = static (ref SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info, long value) => info.usrmod0_min_passwd_age = (uint)(value * 86400),
            ["MinPasswordLength"] = static (ref SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info, long value) => info.usrmod0_min_passwd_len = (uint)value
        };

        #endregion

        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions()
        {
            return SecurityPolicyDefinitions.GetPasswordPolicyDefinitions();
        }

        public SecurityPolicyValue ReadPolicy(SecurityPolicyDefinition definition)
        {
            var value = new SecurityPolicyValue { Definition = definition, IsDefined = true };

            // SAM-based password properties
            if (definition.Key is "PasswordComplexity" or "ClearTextPassword")
            {
                uint props = PolicyNativeHelpers.ReadSamPasswordProperties();
                uint flag = definition.Key == "PasswordComplexity"
                    ? DOMAIN_PASSWORD_COMPLEX
                    : DOMAIN_PASSWORD_STORE_CLEARTEXT;
                value.NumericValue = (props & flag) != 0 ? 1 : 0;
                _logger.LogDebug("[PasswordPolicyProvider] SAM property '{PolicyKey}' = {PolicyValue} (props=0x{Props:X8})", definition.Key, value.NumericValue, props);
                return value;
            }

            // Some password policies are stored in registry, not in SAM
            if (!string.IsNullOrEmpty(definition.RegistryKeyPath))
            {
                return PolicyNativeHelpers.ReadRegistryValue(definition);
            }

            if (!PolicyNativeHelpers.TryGetUserModalsInfo(0, out SecurityPolicyNativeMethods.USER_MODALS_INFO_0 info, out int result))
            {
                _logger.LogDebug("[PasswordPolicyProvider] NetUserModalsGet(0) failed: {Result}", result);
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

            _logger.LogDebug("[PasswordPolicyProvider] '{PolicyKey}' = {PolicyValue}", definition.Key, value.NumericValue);
            return value;
        }

        public void WritePolicy(SecurityPolicyValue value)
        {
            // SAM-based password properties
            if (value.Definition.Key is "PasswordComplexity" or "ClearTextPassword")
            {
                uint flag = value.Definition.Key == "PasswordComplexity"
                    ? DOMAIN_PASSWORD_COMPLEX
                    : DOMAIN_PASSWORD_STORE_CLEARTEXT;
                PolicyNativeHelpers.WriteSamPasswordProperty(flag, value.NumericValue != 0);
                _logger.LogDebug("[PasswordPolicyProvider] Wrote SAM property '{PolicyKey}' = {PolicyValue}", value.Definition.Key, value.NumericValue);
                return;
            }

            if (!string.IsNullOrEmpty(value.Definition.RegistryKeyPath))
            {
                PolicyNativeHelpers.WriteRegistryValue(value);
                return;
            }

            var info = PolicyNativeHelpers.GetUserModalsInfoOrThrow<SecurityPolicyNativeMethods.USER_MODALS_INFO_0>(0);

            if (!WriteHandlers.TryGetValue(value.Definition.Key, out var writeHandler))
                throw new NotSupportedException($"Password policy '{value.Definition.Key}' is not supported for writing.");

            writeHandler(ref info, value.NumericValue);

            PolicyNativeHelpers.SetUserModalsInfoOrThrow(0, info);
            _logger.LogDebug("[PasswordPolicyProvider] Wrote '{PolicyKey}' = {PolicyValue}", value.Definition.Key, value.NumericValue);
        }
    }
}




