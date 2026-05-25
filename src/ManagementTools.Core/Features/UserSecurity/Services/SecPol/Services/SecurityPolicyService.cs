using System;
using System.Collections.Generic;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Orchestrates security policy reading and writing by delegating to
    /// category-specific <see cref="IPolicyProvider"/> implementations.
    /// <para>
    /// This is a thin façade that preserves the existing public API while internally
    /// using the Strategy pattern to decouple category-specific native API mechanics.
    /// </para>
    /// </summary>
    public sealed class SecurityPolicyService
    {
        #region Provider Registry

        private readonly Dictionary<SecurityPolicyCategory, IPolicyProvider> _providers;
        private readonly ILogger<SecurityPolicyService> _logger;

        public SecurityPolicyService(ILogger<SecurityPolicyService> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger;

            PolicyNativeHelpers.ConfigureLogger(loggerFactory.CreateLogger("SecPol.PolicyNativeHelpers"));
            SecurityPolicyResourceLoader.Instance.SetLogger(loggerFactory.CreateLogger<SecurityPolicyResourceLoader>());
            SceRegVlParser.Instance.SetLogger(loggerFactory.CreateLogger<SceRegVlParser>());

            var passwordProvider = new PasswordPolicyProvider(loggerFactory.CreateLogger<PasswordPolicyProvider>());
            var lockoutProvider = new AccountLockoutPolicyProvider(loggerFactory.CreateLogger<AccountLockoutPolicyProvider>());
            var auditProvider = new AuditPolicyProvider(loggerFactory.CreateLogger<AuditPolicyProvider>());
            var userRightsProvider = new UserRightsPolicyProvider(loggerFactory.CreateLogger<UserRightsPolicyProvider>());
            var securityOptionsProvider = new SecurityOptionsPolicyProvider(loggerFactory.CreateLogger<SecurityOptionsPolicyProvider>());

            _providers = new Dictionary<SecurityPolicyCategory, IPolicyProvider>
            {
                [SecurityPolicyCategory.PasswordPolicy] = passwordProvider,
                [SecurityPolicyCategory.AccountLockoutPolicy] = lockoutProvider,
                [SecurityPolicyCategory.AuditPolicy] = auditProvider,
                [SecurityPolicyCategory.UserRightsAssignment] = userRightsProvider,
                [SecurityPolicyCategory.SecurityOptions] = securityOptionsProvider
            };

            _logger.LogInformation("[SecurityPolicyService] Initialized with {ProviderCount} providers", _providers.Count);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Reads all policy values for the given category from the system.
        /// Definitions are obtained from the category's provider — for SecurityOptions,
        /// they are dynamically parsed from sceregvl.inf.
        /// </summary>
        public List<SecurityPolicyValue> ReadPolicies(SecurityPolicyCategory category)
        {
            _logger.LogDebug("[SecurityPolicyService] ReadPolicies: category={Category}", category);

            if (!_providers.TryGetValue(category, out var provider))
            {
                _logger.LogWarning("[SecurityPolicyService] No provider registered for category: {Category}", category);
                return new List<SecurityPolicyValue>();
            }

            var definitions = provider.GetDefinitions();
            var results = new List<SecurityPolicyValue>();

            foreach (var definition in definitions)
            {
                try
                {
                    var value = provider.ReadPolicy(definition);
                    results.Add(value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SecurityPolicyService] Error reading policy {PolicyKey}", definition.Key);
                    results.Add(new SecurityPolicyValue
                    {
                        Definition = definition,
                        IsDefined = false,
                        StringValue = $"Error: {ex.Message}"
                    });
                }
            }

            _logger.LogInformation("[SecurityPolicyService] ReadPolicies: read {PolicyCount} policies for {Category}", results.Count, category);
            return results;
        }

        /// <summary>
        /// Writes a single policy value to the system.
        /// The appropriate provider is selected based on the definition's category.
        /// </summary>
        public void WritePolicy(SecurityPolicyValue value)
        {
            _logger.LogDebug("[SecurityPolicyService] WritePolicy: key={PolicyKey}, type={PolicyType}", value.Definition.Key, value.Definition.PolicyType);

            if (!_providers.TryGetValue(value.Definition.Category, out var provider))
                throw new NotSupportedException($"Category {value.Definition.Category} is not supported for writing.");

            provider.WritePolicy(value);
        }

        /// <summary>
        /// Gets the provider for a specific category.
        /// Useful for accessing provider-specific functionality (e.g., cache invalidation).
        /// </summary>
        public IPolicyProvider? GetProvider(SecurityPolicyCategory category)
        {
            _providers.TryGetValue(category, out var provider);
            return provider;
        }

        /// <summary>
        /// Gets the definitions for a specific category from the registered provider.
        /// For SecurityOptions, this returns dynamically-parsed definitions from sceregvl.inf.
        /// </summary>
        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions(SecurityPolicyCategory category)
        {
            if (!_providers.TryGetValue(category, out var provider))
                return Array.Empty<SecurityPolicyDefinition>();

            return provider.GetDefinitions();
        }

        #endregion
    }
}



