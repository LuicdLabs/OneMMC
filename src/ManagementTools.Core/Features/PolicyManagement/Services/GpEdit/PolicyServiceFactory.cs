using System;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Manager;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Sources;
using ManagementTools.Core.Infrastructure.PolicyStorage;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit
{
    /// <summary>
    /// Factory for creating appropriate policy services based on system configuration.
    /// </summary>
    public static class PolicyServiceFactory
    {
        private static ILogger _logger = NullLogger.Instance;
        /// <summary>
        /// Policy service mode.
        /// </summary>
        public enum PolicyMode
        {
            /// <summary>Automatically detect the best mode based on Windows edition.</summary>
            Auto,
            /// <summary>Use direct registry access only (for Windows Home without gpedit.msc).</summary>
            Registry,
            /// <summary>Use POL file access only.</summary>
            PolFile,
            /// <summary>Use hybrid mode - writes to both Registry and POL file (recommended for Pro/Enterprise).</summary>
            Hybrid
        }

        /// <summary>
        /// Creates a policy service for machine (computer) policies.
        /// </summary>
        /// <param name="mode">The policy mode to use.</param>
        /// <returns>An initialized policy service, or null if initialization fails.</returns>
        public static IPolicyService? CreateMachinePolicyService(PolicyMode mode = PolicyMode.Auto, ILoggerFactory? loggerFactory = null)
        {
            return CreatePolicyService(isUser: false, mode, loggerFactory);
        }

        /// <summary>
        /// Creates a policy service for user policies.
        /// </summary>
        /// <param name="mode">The policy mode to use.</param>
        /// <returns>An initialized policy service, or null if initialization fails.</returns>
        public static IPolicyService? CreateUserPolicyService(PolicyMode mode = PolicyMode.Auto, ILoggerFactory? loggerFactory = null)
        {
            return CreatePolicyService(isUser: true, mode, loggerFactory);
        }

        /// <summary>
        /// Creates a policy service.
        /// </summary>
        /// <param name="isUser">True for user policy, false for machine policy.</param>
        /// <param name="mode">The policy mode to use.</param>
        /// <returns>An initialized policy service, or null if initialization fails.</returns>
        public static IPolicyService? CreatePolicyService(bool isUser, PolicyMode mode = PolicyMode.Auto, ILoggerFactory? loggerFactory = null)
        {
            var effectiveLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = effectiveLoggerFactory.CreateLogger(typeof(PolicyServiceFactory));
            RegistryPolicyProxy.ConfigureLogger(effectiveLoggerFactory.CreateLogger(typeof(RegistryPolicyProxy)));
            GroupPolicyObjectWrapper.ConfigureLogger(effectiveLoggerFactory.CreateLogger(typeof(GroupPolicyObjectWrapper)));
            RegistryPolicyService.ConfigureLogger(effectiveLoggerFactory.CreateLogger(typeof(RegistryPolicyService)));
            PolFilePolicyService.ConfigureLogger(effectiveLoggerFactory.CreateLogger(typeof(PolFilePolicyService)));
            HybridPolicyService.ConfigureLogger(effectiveLoggerFactory.CreateLogger(typeof(HybridPolicyService)));

            var effectiveMode = mode == PolicyMode.Auto ? DetectBestMode() : mode;

            IPolicyService service = effectiveMode switch
            {
                PolicyMode.Registry => new RegistryPolicyService(isUser),
                PolicyMode.PolFile => new PolFilePolicyService(isUser),
                PolicyMode.Hybrid => new HybridPolicyService(isUser),
                _ => new HybridPolicyService(isUser) // Default to hybrid for best compatibility
            };

            if (service.Initialize())
            {
                _logger.LogDebug($"[PolicyServiceFactory] Created {effectiveMode} service for {(isUser ? "User" : "Machine")} policy");
                return service;
            }
            else
            {
                _logger.LogDebug($"[PolicyServiceFactory] Failed to initialize {effectiveMode} service: {service.LastError}");
                service.Dispose();

                // Fallback chain: Hybrid -> Registry
                if (effectiveMode == PolicyMode.Hybrid)
                {
                    _logger.LogDebug("[PolicyServiceFactory] Falling back to Registry mode");
                    service = new RegistryPolicyService(isUser);
                    if (service.Initialize())
                    {
                        return service;
                    }
                    service.Dispose();
                }

                return null;
            }
        }

        /// <summary>
        /// Detects the best policy mode based on the Windows edition.
        /// </summary>
        /// <returns>The recommended policy mode.</returns>
        public static PolicyMode DetectBestMode()
        {
            // For systems with Group Policy infrastructure (Pro/Enterprise),
            // use Hybrid mode to ensure changes are visible in both registry and gpedit.msc
            if (HasGroupPolicyInfrastructure())
            {
                return PolicyMode.Hybrid;
            }
            
            // For Windows Home editions without gpedit.msc,
            // use Registry mode since there's no gpedit.msc to worry about
            return PolicyMode.Registry;
        }

        /// <summary>
        /// Checks if the system has full Group Policy infrastructure (gpedit.msc, GP service, etc.).
        /// </summary>
        /// <returns>True if the system supports Group Policy infrastructure.</returns>
        public static bool HasGroupPolicyInfrastructure()
        {
            return SystemInfo.HasGroupPolicyInfrastructure();
        }
    }
}


