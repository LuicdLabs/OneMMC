using System;
using System.Collections.Generic;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Reads and writes the Account and Local policies. Definitions come from the system's own policy
    /// metadata (<see cref="SecurityPolicyDefinitions"/> and the registry catalog); values are read and
    /// written through the Security Configuration Engine by <see cref="SecurityPolicyEngine"/>, as
    /// secpol.msc does.
    /// </summary>
    public sealed class SecurityPolicyService
    {
        private readonly ILogger<SecurityPolicyService> _logger;
        private readonly SecurityPolicyEngine _engine;
        private readonly Lazy<List<SecurityPolicyDefinition>> _securityOptions;

        /// <summary>Initializes the policy catalog and the shared engine.</summary>
        public SecurityPolicyService(
            ILogger<SecurityPolicyService> logger,
            ILoggerFactory loggerFactory,
            SecurityPolicyEngine engine)
        {
            _logger = logger;
            _engine = engine;

            PolicyNativeHelpers.ConfigureLogger(loggerFactory.CreateLogger("SecPol.PolicyNativeHelpers"));
            SecurityPolicyResourceLoader.Instance.SetLogger(loggerFactory.CreateLogger<SecurityPolicyResourceLoader>());
            SceRegVlParser.Instance.SetLogger(loggerFactory.CreateLogger<SceRegVlParser>());
            SecurityOptionsCatalog.ConfigureLogger(loggerFactory.CreateLogger("SecPol.SecurityOptionsCatalog"));
            SecurityPolicyExplainIndex.ConfigureLogger(loggerFactory.CreateLogger("SecPol.SecurityPolicyExplainIndex"));
            SecurityPolicyDefinitions.ConfigureLogger(loggerFactory.CreateLogger("SecPol.SecurityPolicyDefinitions"));

            _securityOptions = new Lazy<List<SecurityPolicyDefinition>>(BuildSecurityOptionDefinitions);
        }

        /// <summary>
        /// Reads a category's account/LSA settings with one engine read and its registry payloads directly.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">The caller cannot read the local security policy.</exception>
        /// <exception cref="InvalidOperationException">The engine failed to read the settings.</exception>
        public List<SecurityPolicyValue> ReadPolicies(SecurityPolicyCategory category)
        {
            IReadOnlyList<SecurityPolicyDefinition> definitions = GetDefinitions(category);
            var results = new List<SecurityPolicyValue>(definitions.Count);

            if (definitions.Count == 0)
                return results;

            SecurityTemplate template = _engine.Read(category);

            foreach (SecurityPolicyDefinition definition in definitions)
                results.Add(SecurityTemplateMapping.ReadValue(definition, template));

            _logger.LogInformation("[SecurityPolicyService] ReadPolicies: read {PolicyCount} policies for {Category}", results.Count, category);
            return results;
        }

        /// <summary>Writes a single policy value to the local security policy.</summary>
        public void WritePolicy(SecurityPolicyValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _logger.LogDebug("[SecurityPolicyService] WritePolicy: key={PolicyKey}, type={PolicyType}", value.Definition.Key, value.Definition.PolicyType);
            _engine.Write(value);
        }

        private IReadOnlyList<SecurityPolicyDefinition> GetDefinitions(SecurityPolicyCategory category) => category switch
        {
            SecurityPolicyCategory.PasswordPolicy => SecurityPolicyDefinitions.GetPasswordPolicyDefinitions(),
            SecurityPolicyCategory.AccountLockoutPolicy => SecurityPolicyDefinitions.GetAccountLockoutPolicyDefinitions(),
            SecurityPolicyCategory.AuditPolicy => SecurityPolicyDefinitions.GetAuditPolicyDefinitions(),
            SecurityPolicyCategory.UserRightsAssignment => SecurityPolicyDefinitions.GetUserRightsAssignmentDefinitions(),
            SecurityPolicyCategory.SecurityOptions => _securityOptions.Value,
            _ => Array.Empty<SecurityPolicyDefinition>()
        };

        /// <summary>
        /// Assembles Security Options from the system itself: every registry policy from the Security
        /// Configuration Engine's registry catalog (the source secpol.msc uses), falling back to parsing
        /// <c>sceregvl.inf</c>, from which the engine builds that catalog, only if the key is unavailable;
        /// then the account options that are not registry values.
        /// </summary>
        private List<SecurityPolicyDefinition> BuildSecurityOptionDefinitions()
        {
            List<SecurityPolicyDefinition> definitions = SecurityOptionsCatalog.GetDefinitions();

            if (definitions.Count == 0)
            {
                IReadOnlyList<SecurityPolicyDefinition> parsed = SceRegVlParser.Instance.GetDefinitions();
                _logger.LogDebug("[SecurityPolicyService] Registry catalog unavailable; using {Count} definitions from sceregvl.inf", parsed.Count);
                definitions = [.. parsed];
            }

            definitions.AddRange(SecurityPolicyDefinitions.GetAccountOptionDefinitions());

            _logger.LogDebug("[SecurityPolicyService] Catalog contains {Count} Security Options", definitions.Count);
            return definitions;
        }
    }
}
