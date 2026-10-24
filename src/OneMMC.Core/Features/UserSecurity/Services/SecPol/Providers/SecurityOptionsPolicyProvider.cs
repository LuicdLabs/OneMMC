using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Reads and writes Security Options.
    /// <para>
    /// The catalog is assembled from the system itself, with no policy list in the
    /// application:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <see cref="SecurityOptionsCatalog"/> reads every registry-based policy, with
    ///     its type, name, unit and choices, from the Security Configuration Engine's
    ///     registry catalog ??the same source <c>secpol.msc</c> uses.
    ///   </item>
    ///   <item>
    ///     <see cref="SceRegVlParser"/> parses <c>sceregvl.inf</c> only if that catalog
    ///     key is unavailable, since the engine builds the key from that file.
    ///   </item>
    ///   <item>
    ///     <see cref="SecurityPolicyDefinitions.GetAccountOptionDefinitions"/> adds the
    ///     options that are not registry values; each is served by an
    ///     <see cref="ISpecialPolicyHandler"/>.
    ///   </item>
    /// </list>
    /// <para>
    /// A policy added by a Windows update therefore appears automatically, and display
    /// text always matches the running system's language.
    /// </para>
    /// </summary>
    internal sealed class SecurityOptionsPolicyProvider : IPolicyProvider
    {
        private readonly ILogger<SecurityOptionsPolicyProvider> _logger;
        private readonly Dictionary<string, ISpecialPolicyHandler> _specialHandlers;
        private readonly object _lock = new();

        private List<SecurityPolicyDefinition>? _definitions;

        public SecurityPolicyCategory Category => SecurityPolicyCategory.SecurityOptions;

        public SecurityOptionsPolicyProvider()
            : this(NullLogger<SecurityOptionsPolicyProvider>.Instance)
        {
        }

        public SecurityOptionsPolicyProvider(ILogger<SecurityOptionsPolicyProvider> logger)
        {
            _logger = logger ?? NullLogger<SecurityOptionsPolicyProvider>.Instance;
            SpecialPolicyHandlersLogging.Configure(_logger);
            _specialHandlers = BuildSpecialHandlers();
        }

        #region Special handler registry

        /// <summary>
        /// Builds the map of handlers for the options that are not registry values.
        /// Each handler declares the key it serves, so the mapping is checked by the
        /// compiler rather than by a lookup table.
        /// </summary>
        private static Dictionary<string, ISpecialPolicyHandler> BuildSpecialHandlers()
        {
            ISpecialPolicyHandler[] handlers =
            [
                new AdminAccountStatusHandler(),
                new GuestAccountStatusHandler(),
                new RenameAdministratorAccountHandler(),
                new RenameGuestAccountHandler(),
                new ForceLogoffHandler()
            ];

            var map = new Dictionary<string, ISpecialPolicyHandler>(handlers.Length, StringComparer.OrdinalIgnoreCase);
            foreach (ISpecialPolicyHandler handler in handlers)
            {
                map[handler.Key] = handler;
            }

            return map;
        }

        #endregion

        #region IPolicyProvider

        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions()
        {
            if (_definitions is not null)
                return _definitions;

            lock (_lock)
            {
                _definitions ??= BuildDefinitions();
                return _definitions;
            }
        }

        /// <summary>
        /// Drops the cached catalog so that the next read picks up policies added since,
        /// for example after a Windows update or a policy refresh.
        /// </summary>
        public void InvalidateCache()
        {
            lock (_lock)
            {
                _definitions = null;
                SceRegVlParser.Instance.InvalidateCache();
                _logger.LogDebug("[SecurityOptionsPolicyProvider] Cache invalidated");
            }
        }

        public SecurityPolicyValue ReadPolicy(SecurityPolicyDefinition definition)
        {
            if (_specialHandlers.TryGetValue(definition.Key, out ISpecialPolicyHandler? handler))
            {
                var value = new SecurityPolicyValue { Definition = definition, IsDefined = true };
                handler.Read(value);
                return value;
            }

            if (!string.IsNullOrEmpty(definition.RegistryKeyPath))
                return PolicyNativeHelpers.ReadRegistryValue(definition);

            _logger.LogDebug("[SecurityOptionsPolicyProvider] No handler or registry path for '{PolicyKey}'", definition.Key);
            return new SecurityPolicyValue { Definition = definition, IsDefined = false };
        }

        public void WritePolicy(SecurityPolicyValue value)
        {
            if (_specialHandlers.TryGetValue(value.Definition.Key, out ISpecialPolicyHandler? handler))
            {
                handler.Write(value);
                return;
            }

            if (!string.IsNullOrEmpty(value.Definition.RegistryKeyPath))
            {
                PolicyNativeHelpers.WriteRegistryValue(value);
                return;
            }

            throw new NotSupportedException($"Writing policy '{value.Definition.Key}' is not supported: no handler or registry path.");
        }

        #endregion

        #region Catalog assembly

        private List<SecurityPolicyDefinition> BuildDefinitions()
        {
            List<SecurityPolicyDefinition> definitions = SecurityOptionsCatalog.GetDefinitions();

            if (definitions.Count == 0)
            {
                // The catalog key is missing or unreadable; fall back to the file the
                // engine builds it from.
                IReadOnlyList<SecurityPolicyDefinition> parsed = SceRegVlParser.Instance.GetDefinitions();
                _logger.LogDebug(
                    "[SecurityOptionsPolicyProvider] Registry catalog unavailable; using {Count} definitions from sceregvl.inf",
                    parsed.Count);
                definitions = [.. parsed];
            }

            definitions.AddRange(SecurityPolicyDefinitions.GetAccountOptionDefinitions());

            _logger.LogDebug("[SecurityOptionsPolicyProvider] Catalog contains {Count} Security Options", definitions.Count);
            return definitions;
        }

        #endregion
    }
}
