using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ManagementTools.Core.Localization;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Handles Security Options reading/writing.
    /// <para>
    /// Policy definitions are loaded from three sources, merged at runtime:
    /// <list type="number">
    ///   <item>
    ///     <b>sceregvl.inf</b> ??dynamically parsed via <see cref="SceRegVlParser"/>;
    ///     provides localized display names and dropdown options.
    ///   </item>
    ///   <item>
    ///     <b>SecurityOptionsDefinitions.json</b> ??embedded resource containing
    ///     enrichment metadata (stable keys, resource IDs, numeric constraints,
    ///     AllowNotDefined flags). Replaces the former hardcoded enrichment map.
    ///   </item>
    ///   <item>
    ///     Special (non-registry) policies ??driven by <see cref="ISpecialPolicyHandler"/>
    ///     implementations, identified in JSON via the <c>SpecialHandler</c> field.
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// Display names are <b>never</b> hardcoded. They are loaded at runtime from
    /// <c>wsecedit.dll</c> via <see cref="SecurityPolicyResourceLoader"/>, ensuring
    /// correct localization on any Windows language edition.
    /// </para>
    /// </summary>
    internal sealed class SecurityOptionsPolicyProvider : IPolicyProvider
    {
        private static ILogger<SecurityOptionsPolicyProvider> _logger = NullLogger<SecurityOptionsPolicyProvider>.Instance;

        public SecurityPolicyCategory Category => SecurityPolicyCategory.SecurityOptions;

        private List<SecurityPolicyDefinition>? _mergedDefinitions;
        private readonly object _lock = new();

        /// <summary>
        /// Enrichment data loaded from <c>SecurityOptionsDefinitions.json</c>,
        /// keyed by <c>"RegistryKeyPath\RegistryValueName"</c> (case-insensitive)
        /// for registry-based entries.
        /// </summary>
        private readonly Dictionary<string, PolicyDefinitionDto> _registryEnrichmentMap;

        /// <summary>
        /// Special (non-registry) definition DTOs loaded from JSON,
        /// keyed by <see cref="PolicyDefinitionDto.Key"/>.
        /// </summary>
        private readonly List<PolicyDefinitionDto> _specialDtos;

        /// <summary>
        /// Strongly-typed handlers for special policies, keyed by handler name.
        /// </summary>
        private readonly Dictionary<string, ISpecialPolicyHandler> _specialHandlers;

        private static readonly HashSet<string> KnownDropdownResourceKeys = BuildKnownDropdownResourceKeys();

        public SecurityOptionsPolicyProvider()
            : this(NullLogger<SecurityOptionsPolicyProvider>.Instance)
        {
        }

        public SecurityOptionsPolicyProvider(ILogger<SecurityOptionsPolicyProvider> logger)
        {
            _logger = logger ?? NullLogger<SecurityOptionsPolicyProvider>.Instance;
            SpecialPolicyHandlersLogging.Configure(_logger);
            var allDtos = LoadDefinitionsFromJson();
            ValidateDefinitionsIntegrity(allDtos);

            // Separate registry-based and special DTOs
            _registryEnrichmentMap = new Dictionary<string, PolicyDefinitionDto>(StringComparer.OrdinalIgnoreCase);
            _specialDtos = new List<PolicyDefinitionDto>();

            foreach (var dto in allDtos)
            {
                if (dto.IsSpecial)
                {
                    _specialDtos.Add(dto);
                }
                else
                {
                    var regPath = dto.GetRegistryFullPath();
                    if (regPath != null)
                    {
                        _registryEnrichmentMap[regPath] = dto;
                    }
                }
            }

            _specialHandlers = BuildSpecialHandlers();

            _logger.LogDebug($"[SecurityOptionsPolicyProvider] Loaded {_registryEnrichmentMap.Count} registry enrichments, " +
                            $"{_specialDtos.Count} special definitions, {_specialHandlers.Count} handlers from JSON");
        }

        #region JSON Loading

        /// <summary>
        /// Loads all policy definition DTOs from the embedded
        /// <c>SecurityOptionsDefinitions.json</c> resource.
        /// </summary>
        private List<PolicyDefinitionDto> LoadDefinitionsFromJson()
        {
            var assembly = typeof(SecurityOptionsPolicyProvider).Assembly;
            const string resourceName = "ManagementTools.Core.Features.UserSecurity.Services.SecPol.Resources.SecurityOptionsDefinitions.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                _logger.LogDebug($"[SecurityOptionsPolicyProvider] Embedded resource not found: {resourceName}");
                return new List<PolicyDefinitionDto>();
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var dtos = JsonSerializer.Deserialize<List<PolicyDefinitionDto>>(json, options);
            _logger.LogDebug($"[SecurityOptionsPolicyProvider] Deserialized {dtos?.Count ?? 0} definitions from JSON");
            return dtos ?? new List<PolicyDefinitionDto>();
        }

        /// <summary>
        /// Validates JSON definition integrity so policy regressions fail fast
        /// instead of silently producing incorrect UI/write behavior.
        /// </summary>
        private static void ValidateDefinitionsIntegrity(List<PolicyDefinitionDto> dtos)
        {
            var keySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var regSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.Key))
                    throw new InvalidOperationException("SecurityOptionsDefinitions contains an entry with empty Key.");

                if (!keySet.Add(dto.Key))
                    throw new InvalidOperationException($"SecurityOptionsDefinitions contains duplicate Key: '{dto.Key}'.");

                var regPath = dto.GetRegistryFullPath();
                if (regPath != null && !regSet.Add(regPath) && string.IsNullOrWhiteSpace(dto.SpecialHandler))
                    throw new InvalidOperationException($"SecurityOptionsDefinitions contains duplicate registry mapping: '{regPath}'.");

                if (dto.PolicyType.Equals("BitmaskFlags", StringComparison.OrdinalIgnoreCase) && dto.DropdownOptions != null)
                {
                    var bitValues = new HashSet<long>();
                    foreach (var option in dto.DropdownOptions)
                    {
                        if (option.Value <= 0)
                            throw new InvalidOperationException($"Bitmask policy '{dto.Key}' contains non-positive option value: {option.Value}.");

                        if (!bitValues.Add(option.Value))
                            throw new InvalidOperationException($"Bitmask policy '{dto.Key}' contains duplicate option value: {option.Value}.");
                    }
                }
            }

            ValidateAdminProtectionDefinition(dtos);
            ValidateKerberosDefinition(dtos);
        }

        private static void ValidateAdminProtectionDefinition(List<PolicyDefinitionDto> dtos)
        {
            var ap = dtos.FirstOrDefault(d => d.Key.Equals("ConsentPromptBehaviorAdminAP", StringComparison.OrdinalIgnoreCase));
            if (ap == null)
                throw new InvalidOperationException("Missing required policy definition: ConsentPromptBehaviorAdminAP.");

            if (!ap.PolicyType.Equals("Dropdown", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ConsentPromptBehaviorAdminAP must be Dropdown.");

            if (!string.Equals(ap.RegistryValueName, "ConsentPromptBehaviorEnhancedAdmin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ConsentPromptBehaviorAdminAP must map to registry value 'ConsentPromptBehaviorEnhancedAdmin'.");

            var values = new HashSet<long>((ap.DropdownOptions ?? new List<PolicyDropdownOptionDto>()).Select(o => o.Value));
            if (values.Count != 2 || !values.SetEquals(new[] { 1L, 2L }))
                throw new InvalidOperationException("ConsentPromptBehaviorAdminAP must contain exactly option values {1,2}.");
        }

        private static void ValidateKerberosDefinition(List<PolicyDefinitionDto> dtos)
        {
            var kerb = dtos.FirstOrDefault(d => d.Key.Equals("SupportedEncryptionTypes", StringComparison.OrdinalIgnoreCase));
            if (kerb == null)
                throw new InvalidOperationException("Missing required policy definition: SupportedEncryptionTypes.");

            if (!kerb.PolicyType.Equals("BitmaskFlags", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SupportedEncryptionTypes must be BitmaskFlags.");

            if (!string.Equals(kerb.RegistryKeyPath, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\Kerberos\\Parameters", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(kerb.RegistryValueName, "SupportedEncryptionTypes", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SupportedEncryptionTypes registry mapping is invalid.");
            }

            // Microsoft guidance indicates that when all six checkboxes are selected,
            // the policy writes 0x7FFFFFFF. Therefore "Future encryption types" is
            // the mask 0x7FFFFFE0 (not the legacy single bit 0x20).
            var expected = new HashSet<long> { 1, 2, 4, 8, 16, 2147483616 };
            var actual = new HashSet<long>((kerb.DropdownOptions ?? new List<PolicyDropdownOptionDto>()).Select(o => o.Value));
            if (!actual.SetEquals(expected))
                throw new InvalidOperationException("SupportedEncryptionTypes options must be {1,2,4,8,16,2147483616}.");
        }

        #endregion

        #region Special Handler Registry

        /// <summary>
        /// Builds the map of strongly-typed special policy handlers.
        /// Each handler's <see cref="ISpecialPolicyHandler.Key"/> is used as the
        /// dictionary key, ensuring compile-time safety.
        /// </summary>
        private static Dictionary<string, ISpecialPolicyHandler> BuildSpecialHandlers()
        {
            var handlers = new ISpecialPolicyHandler[]
            {
                new AdminAccountStatusHandler(),
                new GuestAccountStatusHandler(),
                new RenameAdministratorAccountHandler(),
                new RenameGuestAccountHandler(),
                new ForceLogoffHandler(),
                new ConsentPromptBehaviorAdminAPHandler()
            };

            var map = new Dictionary<string, ISpecialPolicyHandler>(handlers.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var handler in handlers)
            {
                map[handler.Key] = handler;
            }
            return map;
        }

        #endregion

        #region IPolicyProvider

        /// <summary>
        /// Returns definitions by merging dynamically-parsed entries from <c>sceregvl.inf</c>
        /// with JSON-driven enrichment data and special non-registry definitions.
        /// </summary>
        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions()
        {
            if (_mergedDefinitions != null)
                return _mergedDefinitions;

            lock (_lock)
            {
                if (_mergedDefinitions != null)
                    return _mergedDefinitions;

                _mergedDefinitions = BuildMergedDefinitions();
                _logger.LogDebug($"[SecurityOptionsPolicyProvider] Merged definitions: {_mergedDefinitions.Count} total");
                return _mergedDefinitions;
            }
        }

        /// <summary>
        /// Invalidates the cached definitions, forcing a re-merge on the next
        /// <see cref="GetDefinitions"/> call. Also invalidates the
        /// <see cref="SceRegVlParser"/> cache so <c>sceregvl.inf</c> is re-parsed.
        /// </summary>
        public void InvalidateCache()
        {
            lock (_lock)
            {
                _mergedDefinitions = null;
                SceRegVlParser.Instance.InvalidateCache();
                _logger.LogDebug("[SecurityOptionsPolicyProvider] Cache invalidated");
            }
        }

        public SecurityPolicyValue ReadPolicy(SecurityPolicyDefinition definition)
        {
            var value = new SecurityPolicyValue { Definition = definition, IsDefined = true };

            // Use strongly-typed handler for special policies
            if (_specialHandlers.TryGetValue(definition.Key, out var handler))
            {
                handler.Read(value);
                return value;
            }

            // Registry-based policies
            if (!string.IsNullOrEmpty(definition.RegistryKeyPath))
            {
                return PolicyNativeHelpers.ReadRegistryValue(definition);
            }

            value.IsDefined = false;
            _logger.LogDebug($"[SecurityOptionsPolicyProvider] Unknown policy with no handler and no registry path: {definition.Key}");
            return value;
        }

        public void WritePolicy(SecurityPolicyValue value)
        {
            // Use strongly-typed handler for special policies
            if (_specialHandlers.TryGetValue(value.Definition.Key, out var handler))
            {
                handler.Write(value);
                return;
            }

            // Registry-based policies
            if (!string.IsNullOrEmpty(value.Definition.RegistryKeyPath))
            {
                PolicyNativeHelpers.WriteRegistryValue(value);
                return;
            }

            throw new NotSupportedException($"Writing policy '{value.Definition.Key}' is not supported ??no handler or registry path configured.");
        }

        #endregion

        #region Definition Merging

        /// <summary>
        /// Builds the merged list of definitions:
        /// <list type="number">
        ///   <item>Parse dynamic entries from <c>sceregvl.inf</c>.</item>
        ///   <item>
        ///     If dynamic entries exist, enrich them with metadata from
        ///     <see cref="_registryEnrichmentMap"/> (stable keys, resource IDs,
        ///     numeric constraints, etc.) and add special definitions.
        ///   </item>
        ///   <item>
        ///     If <c>sceregvl.inf</c> is missing or yields zero entries,
        ///     fall back to definitions generated from the JSON enrichment data
        ///     plus the special definitions.
        ///   </item>
        /// </list>
        /// </summary>
        private List<SecurityPolicyDefinition> BuildMergedDefinitions()
        {
            var dynamicDefs = SceRegVlParser.Instance.GetDefinitions();
            var specialDefs = BuildSpecialDefinitions();

            // ?? Fallback: if sceregvl.inf is missing or empty ??
            if (dynamicDefs.Count == 0)
            {
                _logger.LogDebug("[SecurityOptionsPolicyProvider] sceregvl.inf yielded 0 entries ??using JSON fallback");
                var fallback = GenerateFallbackDefinitions();
                fallback.AddRange(specialDefs);
                return fallback;
            }

            var merged = new List<SecurityPolicyDefinition>();
            var coveredRegPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ?? Step 1: dynamic definitions are the primary source ??
            foreach (var dynDef in dynamicDefs)
            {
                string regPath = $"{dynDef.RegistryKeyPath}\\{dynDef.RegistryValueName}";

                if (_registryEnrichmentMap.TryGetValue(regPath, out var enrichmentDto))
                {
                    merged.Add(EnrichDefinition(dynDef, enrichmentDto));
                }
                else
                {
                    // Newly discovered by sceregvl.inf ??automatically supported
                    merged.Add(dynDef);
                }
                coveredRegPaths.Add(regPath);
            }

            // ?? Step 2: add enrichment entries NOT covered by sceregvl.inf ??
            //   (edge case: stripped/modified INF, or policies only on certain SKUs)
            foreach (var (regPath, dto) in _registryEnrichmentMap)
            {
                if (!coveredRegPaths.Contains(regPath))
                {
                    merged.Add(BuildDefinitionFromDto(dto, PolicyDataSource.Hardcoded));
                }
            }

            // ?? Step 3: add special non-registry definitions ??
            merged.AddRange(specialDefs);

            _logger.LogDebug($"[SecurityOptionsPolicyProvider] Merged: {dynamicDefs.Count} dynamic + " +
                            $"{merged.Count - dynamicDefs.Count} extras = {merged.Count} total");

            return merged;
        }

        /// <summary>
        /// Enriches a dynamically-parsed definition with metadata from the JSON enrichment.
        /// Display name comes from <c>sceregvl.inf</c> (already resolved via <c>SHLoadIndirectString</c>).
        /// Falls back to <c>wsecedit.dll</c> resource if the dynamic name is empty.
        /// </summary>
        private static SecurityPolicyDefinition EnrichDefinition(SecurityPolicyDefinition dynamic, PolicyDefinitionDto enrichment)
        {
            var policyType = ParsePolicyType(enrichment.PolicyType);

            // Display name: prefer dynamic (localized from sceregvl.inf), fall back to wsecedit.dll resource
            var displayName = !string.IsNullOrEmpty(dynamic.DisplayName)
                ? dynamic.DisplayName
                : SecurityPolicyResourceLoader.Instance.LoadDisplayName(enrichment.ExplainResourceId) ?? enrichment.Key;

            return new SecurityPolicyDefinition
            {
                Key = enrichment.Key,
                DisplayName = displayName,
                Category = SecurityPolicyCategory.SecurityOptions,
                PolicyType = policyType,
                RegistryKeyPath = dynamic.RegistryKeyPath,
                RegistryValueName = dynamic.RegistryValueName,
                MinValue = enrichment.MinValue,
                MaxValue = enrichment.MaxValue > 0 ? enrichment.MaxValue : long.MaxValue,
                Unit = enrichment.Unit ?? string.Empty,
                // Dropdown options come from sceregvl.inf [Strings], already localized.
                // Falls back to JSON options with app-resource localization.
                DropdownOptions = GetEnrichedDropdownOptions(dynamic, enrichment),
                AllowNotDefined = enrichment.AllowNotDefined,
                ExplainResourceId = enrichment.ExplainResourceId,
                DataSource = PolicyDataSource.SceRegVl
            };
        }

        /// <summary>
        /// Returns the best available dropdown options.
        /// For <see cref="SecurityPolicyType.BitmaskFlags"/> policies, merges dynamic
        /// options from <c>sceregvl.inf</c> with any additional flags defined in JSON
        /// (e.g., "Future encryption types"). For other types, prefers dynamic options
        /// and falls back to JSON.
        /// </summary>
        private static List<PolicyDropdownOption> GetEnrichedDropdownOptions(
            SecurityPolicyDefinition dynamic, PolicyDefinitionDto enrichment)
        {
            if (dynamic.DropdownOptions.Count > 0)
            {
                // For BitmaskFlags, merge JSON flags that are missing from the dynamic set.
                // This ensures flags like "Future encryption types" (added by secpol.msc
                // but not always present in sceregvl.inf) are always available.
                var policyType = Enum.TryParse<SecurityPolicyType>(enrichment.PolicyType, true, out var pt)
                    ? pt : SecurityPolicyType.Numeric;

                if (policyType == SecurityPolicyType.BitmaskFlags && enrichment.DropdownOptions?.Count > 0)
                {
                    var dynamicValues = new HashSet<long>();
                    foreach (var opt in dynamic.DropdownOptions)
                    {
                        if (opt.Value is long lv) dynamicValues.Add(lv);
                        else if (opt.Value is int iv) dynamicValues.Add(iv);
                    }

                    var extraOptions = ConvertDropdownOptions(enrichment.DropdownOptions)
                        .Where(o =>
                        {
                            long v = 0;
                            if (o.Value is long lv) v = lv;
                            else if (o.Value is int iv) v = iv;
                            return v != 0 && !dynamicValues.Contains(v);
                        })
                        .ToList();

                    if (extraOptions.Count > 0)
                    {
                        LocalizeDropdownOptions(enrichment.Key, extraOptions);
                        var merged = new List<PolicyDropdownOption>(dynamic.DropdownOptions);
                        merged.AddRange(extraOptions);
                        _logger.LogDebug($"[SecurityOptionsPolicyProvider] Merged {extraOptions.Count} extra BitmaskFlags option(s) from JSON for '{enrichment.Key}'");
                        return merged;
                    }
                }

                return dynamic.DropdownOptions;
            }

            var options = ConvertDropdownOptions(enrichment.DropdownOptions);
            LocalizeDropdownOptions(enrichment.Key, options);
            return options;
        }

        /// <summary>
        /// Builds a full <see cref="SecurityPolicyDefinition"/> from a JSON DTO.
        /// Used for fallback and for enrichment entries not covered by <c>sceregvl.inf</c>.
        /// Display name is loaded from <c>wsecedit.dll</c> at runtime.
        /// </summary>
        private static SecurityPolicyDefinition BuildDefinitionFromDto(PolicyDefinitionDto dto, PolicyDataSource dataSource)
        {
            var dropdownOptions = ConvertDropdownOptions(dto.DropdownOptions);
            LocalizeDropdownOptions(dto.Key, dropdownOptions);

            return new SecurityPolicyDefinition
            {
                Key = dto.Key,
                DisplayName = SecurityPolicyResourceLoader.Instance.LoadDisplayName(dto.ExplainResourceId) ?? dto.Key,
                Category = SecurityPolicyCategory.SecurityOptions,
                PolicyType = ParsePolicyType(dto.PolicyType),
                RegistryKeyPath = dto.RegistryKeyPath ?? string.Empty,
                RegistryValueName = dto.RegistryValueName ?? string.Empty,
                MinValue = dto.MinValue,
                MaxValue = dto.MaxValue > 0 ? dto.MaxValue : long.MaxValue,
                Unit = dto.Unit ?? string.Empty,
                DropdownOptions = dropdownOptions,
                AllowNotDefined = dto.AllowNotDefined,
                ExplainResourceId = dto.ExplainResourceId,
                DataSource = dataSource
            };
        }

        /// <summary>
        /// Builds <see cref="SecurityPolicyDefinition"/> objects for all special
        /// (non-registry) policies from the JSON DTOs.
        /// Display names are loaded from <c>wsecedit.dll</c> at runtime.
        /// </summary>
        private List<SecurityPolicyDefinition> BuildSpecialDefinitions()
        {
            var loader = SecurityPolicyResourceLoader.Instance;
            var defs = new List<SecurityPolicyDefinition>(_specialDtos.Count);

            foreach (var dto in _specialDtos)
            {
                var dropdownOptions = ConvertDropdownOptions(dto.DropdownOptions);
                LocalizeDropdownOptions(dto.Key, dropdownOptions);

                var def = new SecurityPolicyDefinition
                {
                    Key = dto.Key,
                    DisplayName = loader.LoadDisplayName(dto.ExplainResourceId) ?? dto.Key,
                    Category = SecurityPolicyCategory.SecurityOptions,
                    PolicyType = ParsePolicyType(dto.PolicyType),
                    ExplainResourceId = dto.ExplainResourceId,
                    DataSource = PolicyDataSource.Hardcoded,
                    DropdownOptions = dropdownOptions,
                    AllowNotDefined = dto.AllowNotDefined,
                    MinValue = dto.MinValue,
                    MaxValue = dto.MaxValue > 0 ? dto.MaxValue : long.MaxValue,
                    Unit = dto.Unit ?? string.Empty
                };
                defs.Add(def);
            }

            return defs;
        }

        /// <summary>
        /// Generates full definitions from the JSON enrichment data when
        /// <c>sceregvl.inf</c> is unavailable.
        /// Display names are loaded from <c>wsecedit.dll</c> string resources.
        /// </summary>
        private List<SecurityPolicyDefinition> GenerateFallbackDefinitions()
        {
            var defs = new List<SecurityPolicyDefinition>(_registryEnrichmentMap.Count);
            foreach (var (_, dto) in _registryEnrichmentMap)
            {
                defs.Add(BuildDefinitionFromDto(dto, PolicyDataSource.Hardcoded));
            }
            return defs;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Parses a <see cref="SecurityPolicyType"/> from the JSON string representation.
        /// </summary>
        private static SecurityPolicyType ParsePolicyType(string policyType)
        {
            return Enum.TryParse<SecurityPolicyType>(policyType, ignoreCase: true, out var result)
                ? result
                : SecurityPolicyType.Numeric;
        }

        /// <summary>
        /// Converts JSON dropdown option DTOs to <see cref="PolicyDropdownOption"/> instances.
        /// Prefers <c>DisplayResourceId</c> for localized names; falls back to
        /// <c>DisplayName</c> from JSON.
        /// </summary>
        private static List<PolicyDropdownOption> ConvertDropdownOptions(List<PolicyDropdownOptionDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return new List<PolicyDropdownOption>();

            var loader = SecurityPolicyResourceLoader.Instance;
            return dtos.Select(dto =>
            {
                string displayName;
                if (dto.DisplayResourceId > 0)
                {
                    displayName = loader.LoadDisplayName(dto.DisplayResourceId) ?? dto.DisplayName ?? $"ID:{dto.DisplayResourceId}";
                }
                else
                {
                    displayName = dto.DisplayName ?? $"Value:{dto.Value}";
                }

                return new PolicyDropdownOption
                {
                    DisplayName = displayName,
                    Value = dto.Value
                };
            }).ToList();
        }

        /// <summary>
        /// Attempts to localize dropdown option display names using the app's
        /// own resource system (<c>SecPol.resw</c>). This provides localized
        /// dropdown labels for policies that are not listed in <c>sceregvl.inf</c>
        /// and whose JSON definitions only carry English fallback names.
        /// <para>
        /// Resource key convention: <c>SecPol_Dropdown_{PolicyKey}_{Value}</c>
        /// </para>
        /// </summary>
        private static void LocalizeDropdownOptions(string policyKey, List<PolicyDropdownOption> options)
        {
            if (options.Count == 0)
                return;

            var provider = LocalizationProvider.Current;
            foreach (var option in options)
            {
                string valueStr = option.Value is long lv ? lv.ToString()
                    : option.Value is int iv ? iv.ToString()
                    : option.Value?.ToString() ?? "0";

                string resourceKey = $"SecPol_Dropdown_{policyKey}_{valueStr}";

                // Avoid querying unknown keys to prevent noisy first-chance COMExceptions
                // from MRT when the resource does not exist.
                if (!KnownDropdownResourceKeys.Contains(resourceKey))
                    continue;

                string localized = provider.GetString(ResourceFileNames.SecPol, resourceKey);

                if (!string.IsNullOrWhiteSpace(localized)
                    && !(localized.StartsWith("[") && localized.EndsWith("]")))
                {
                    option.DisplayName = localized;
                }
            }
        }

        private static HashSet<string> BuildKnownDropdownResourceKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fields = typeof(SecPolKeys).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                if (field.FieldType != typeof(string))
                    continue;

                if (field.GetRawConstantValue() is string key &&
                    key.StartsWith("SecPol_Dropdown_", StringComparison.Ordinal))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        #endregion
    }
}




