using System.Globalization;
using System.Text;
using ManagementTools.Core.Abstractions.Services;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.Native;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Reads the legacy static local IPsec policy store through <c>polstore.dll</c>.
/// </summary>
/// <remarks>
/// The legacy local IPsec store has no supported managed API. The documented <c>IPSecExportPolicies</c>
/// export from <c>polstore.dll</c> returns the same policy script format produced by the deprecated
/// <c>netsh ipsec dump</c> command, which this service parses into typed models.
/// </remarks>
public sealed class IPSecurityStaticPolicyStoreService
{
    private readonly ILogger<IPSecurityStaticPolicyStoreService> _logger;
    private readonly IPSecurityStaticPolicyNativeClient _nativeClient;
    private readonly IAdminService _adminService;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyStoreService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="nativeClient">The legacy IPsec policy native client.</param>
    /// <param name="adminService">The administrator service.</param>
    public IPSecurityStaticPolicyStoreService(
        ILogger<IPSecurityStaticPolicyStoreService> logger,
        IPSecurityStaticPolicyNativeClient nativeClient,
        IAdminService adminService)
    {
        _logger = logger;
        _nativeClient = nativeClient;
        _adminService = adminService;
    }

    /// <summary>
    /// Loads the legacy static local IPsec policy store.
    /// </summary>
    /// <returns>A typed snapshot of policies, shared filter lists, and shared filter actions.</returns>
    public IPSecurityStaticStoreSnapshot LoadSnapshot()
    {
        if (!IPSecurityPolicyNativeMethods.IsAvailable)
        {
            _logger.LogWarning(
                "The legacy IPsec policy store APIs are not available on this system. " +
                "Returning an empty snapshot.");
            return new IPSecurityStaticStoreSnapshot();
        }

        string? policyScript = _nativeClient.TryExportPolicyScript(out int errorCode);
        if (policyScript is null)
        {
            if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(errorCode))
            {
                throw new UnauthorizedAccessException($"The local IPsec policy store could not be opened (native error 0x{errorCode:X8}).");
            }

            // ERROR_INVALID_DATA (0xD) from IPSecExportPolicies usually indicates a permission error when not running as admin,
            // or an empty policy store when elevated.
            if (errorCode == 0x0000000D)
            {
                if (!_adminService.IsRunningAsAdmin)
                {
                    throw new UnauthorizedAccessException($"The local IPsec policy store could not be exported due to insufficient privileges (native error 0x{errorCode:X8}).");
                }

                _logger.LogInformation(
                    "The local IPsec policy store export failed with native error 0x{ErrorCode:X8}. " +
                    "Attempting to read directly from the registry.",
                    errorCode);
                return LoadFromRegistry();
            }

            _logger.LogWarning(
                "The legacy IPsec policy store export failed with native error 0x{ErrorCode:X8}.",
                errorCode);
            throw new InvalidOperationException("The local IPsec policy store could not be read.");
        }

        if (string.IsNullOrWhiteSpace(policyScript))
        {
            return new IPSecurityStaticStoreSnapshot();
        }

        return ParseDump(policyScript);
    }

    internal static IPSecurityStaticStoreSnapshot ParseDump(string dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        Dictionary<string, PolicyBuilder> policies = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FilterListBuilder> filterLists = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FilterActionBuilder> filterActions = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in GetLogicalLines(dump))
        {
            IReadOnlyList<string> tokens = Tokenize(line);
            int mutationIndex = IndexOfToken(tokens, "add");
            if (mutationIndex < 0)
            {
                mutationIndex = IndexOfToken(tokens, "set");
            }

            if (mutationIndex < 0 || mutationIndex + 1 >= tokens.Count)
            {
                continue;
            }

            string objectKind = tokens[mutationIndex + 1];
            ParsedArguments arguments = ParsedArguments.Create(tokens, mutationIndex + 2);

            switch (objectKind.ToLowerInvariant())
            {
                case "policy":
                    ParsePolicy(arguments, policies);
                    break;
                case "filterlist":
                    ParseFilterList(arguments, filterLists);
                    break;
                case "filter":
                    ParseFilter(arguments, filterLists);
                    break;
                case "filteraction":
                    ParseFilterAction(arguments, filterActions);
                    break;
                case "rule":
                    ParseRule(arguments, policies);
                    break;
                case "defaultrule":
                    ParseDefaultRule(arguments, policies);
                    break;
            }
        }

        return new IPSecurityStaticStoreSnapshot
        {
            Policies = policies.Values
                .Select(static builder => builder.Build())
                .OrderBy(static policy => policy.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            FilterLists = filterLists.Values
                .Select(static builder => builder.Build())
                .OrderBy(static filterList => filterList.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            FilterActions = filterActions.Values
                .Select(static builder => builder.Build())
                .OrderBy(static filterAction => filterAction.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    private IPSecurityStaticStoreSnapshot LoadFromRegistry()
    {
        try
        {
            using var localKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\IPSec\Policy\Local");
            if (localKey is null)
            {
                return new IPSecurityStaticStoreSnapshot();
            }

            string activePolicyPath = localKey.GetValue("ActivePolicy") as string ?? string.Empty;

            List<IPSecurityPolicyDefinition> policies = [];
            List<IPSecurityFilterListDefinition> filterLists = [];
            List<IPSecurityFilterActionDefinition> filterActions = [];

            foreach (string subKeyName in localKey.GetSubKeyNames())
            {
                using var subKey = localKey.OpenSubKey(subKeyName);
                if (subKey is null) continue;

                string className = subKey.GetValue("className") as string ?? string.Empty;
                string ipsecName = subKey.GetValue("ipsecName") as string ?? string.Empty;
                string description = subKey.GetValue("description") as string ?? string.Empty;

                if (string.IsNullOrWhiteSpace(ipsecName))
                {
                    ipsecName = subKey.GetValue("name") as string ?? subKeyName;
                }

                if (className.Equals("ipsecPolicy", StringComparison.OrdinalIgnoreCase))
                {
                    bool isAssigned = false;
                    if (!string.IsNullOrEmpty(activePolicyPath))
                    {
                        if (activePolicyPath.EndsWith(subKeyName, StringComparison.OrdinalIgnoreCase))
                        {
                            isAssigned = true;
                        }
                    }

                    policies.Add(new IPSecurityPolicyDefinition
                    {
                        Name = ipsecName,
                        Description = description,
                        IsAssigned = isAssigned
                    });
                }
                else if (className.Equals("ipsecFilter", StringComparison.OrdinalIgnoreCase))
                {
                    filterLists.Add(new IPSecurityFilterListDefinition
                    {
                        Name = ipsecName,
                        Description = description
                    });
                }
                else if (className.Equals("ipsecNegotiationPolicy", StringComparison.OrdinalIgnoreCase))
                {
                    filterActions.Add(new IPSecurityFilterActionDefinition
                    {
                        Name = ipsecName,
                        Description = description
                    });
                }
            }

            return new IPSecurityStaticStoreSnapshot
            {
                Policies = policies.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                FilterLists = filterLists.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                FilterActions = filterActions.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read legacy IPsec policy store from registry.");
            return new IPSecurityStaticStoreSnapshot();
        }
    }

    private static void ParsePolicy(ParsedArguments arguments, IDictionary<string, PolicyBuilder> policies)
    {
        string name = arguments.GetValue("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        PolicyBuilder builder = GetOrAddPolicy(policies, name);
        if (arguments.Contains("description"))
        {
            builder.Description = arguments.GetValue("description");
        }

        if (arguments.Contains("assign"))
        {
            builder.IsAssigned = arguments.GetBoolean("assign");
        }

        if (arguments.Contains("mmpfs"))
        {
            builder.UseMasterPerfectForwardSecrecy = arguments.GetBoolean("mmpfs");
        }

        if (arguments.Contains("qmpermm"))
        {
            builder.QuickModeSessionsPerMainMode = arguments.GetInteger("qmpermm");
        }

        if (arguments.Contains("mmlifetime"))
        {
            builder.MainModeLifetimeMinutes = arguments.GetInteger("mmlifetime");
        }

        if (arguments.Contains("activatedefaultrule"))
        {
            builder.IsDefaultResponseRuleActive = arguments.GetBoolean("activatedefaultrule");
        }

        if (arguments.Contains("pollinginterval"))
        {
            builder.PollingIntervalMinutes = arguments.GetInteger("pollinginterval");
        }

        if (arguments.Contains("mmsecmethods"))
        {
            builder.MainModeSecurityMethods = SplitMethods(arguments.GetValue("mmsecmethods"));
        }
    }

    private static void ParseFilterList(ParsedArguments arguments, IDictionary<string, FilterListBuilder> filterLists)
    {
        string name = arguments.GetValue("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        FilterListBuilder builder = GetOrAddFilterList(filterLists, name);
        if (arguments.Contains("description"))
        {
            builder.Description = arguments.GetValue("description");
        }
    }

    private static void ParseFilter(ParsedArguments arguments, IDictionary<string, FilterListBuilder> filterLists)
    {
        string filterListName = arguments.GetValue("filterlist");
        if (string.IsNullOrWhiteSpace(filterListName))
        {
            return;
        }

        GetOrAddFilterList(filterLists, filterListName).Filters.Add(new IPSecurityFilterDefinition
        {
            FilterListName = filterListName,
            Description = arguments.GetValue("description"),
            SourceAddress = arguments.GetValue("srcaddr"),
            SourceMask = arguments.GetValue("srcmask"),
            DestinationAddress = arguments.GetValue("dstaddr"),
            DestinationMask = arguments.GetValue("dstmask"),
            Protocol = arguments.GetValue("protocol"),
            SourcePort = arguments.GetInteger("srcport"),
            DestinationPort = arguments.GetInteger("dstport"),
            IsMirrored = arguments.GetBoolean("mirrored")
        });
    }

    private static void ParseFilterAction(
        ParsedArguments arguments,
        IDictionary<string, FilterActionBuilder> filterActions)
    {
        string name = arguments.GetValue("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        FilterActionBuilder builder = GetOrAddFilterAction(filterActions, name);
        if (arguments.Contains("description"))
        {
            builder.Description = arguments.GetValue("description");
        }

        if (arguments.Contains("action"))
        {
            builder.Action = ParseAction(arguments.GetValue("action"));
        }

        if (arguments.Contains("qmpfs"))
        {
            builder.UseQuickModePerfectForwardSecrecy = arguments.GetBoolean("qmpfs");
        }

        if (arguments.Contains("inpass"))
        {
            builder.AcceptUnsecuredInbound = arguments.GetBoolean("inpass");
        }

        if (arguments.Contains("soft"))
        {
            builder.AllowUnsecuredFallback = arguments.GetBoolean("soft");
        }

        if (arguments.Contains("qmsecmethods"))
        {
            builder.QuickModeSecurityMethods = SplitMethods(arguments.GetValue("qmsecmethods"));
        }
    }

    private static void ParseRule(ParsedArguments arguments, IDictionary<string, PolicyBuilder> policies)
    {
        string policyName = arguments.GetValue("policy");
        if (string.IsNullOrWhiteSpace(policyName))
        {
            return;
        }

        string ruleName = arguments.GetValue("name");
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            return;
        }

        RuleBuilder builder = GetOrAddPolicy(policies, policyName).GetOrAddRule(ruleName);
        if (arguments.Contains("description"))
        {
            builder.Description = arguments.GetValue("description");
        }

        if (arguments.Contains("filterlist"))
        {
            builder.FilterListName = arguments.GetValue("filterlist");
        }

        if (arguments.Contains("filteraction"))
        {
            builder.FilterActionName = arguments.GetValue("filteraction");
        }

        if (arguments.Contains("tunnel"))
        {
            string tunnel = arguments.GetValue("tunnel");
            builder.TunnelEndpoint = tunnel.Equals("no", StringComparison.OrdinalIgnoreCase) ? string.Empty : tunnel;
        }

        if (arguments.Contains("conntype"))
        {
            builder.ConnectionType = arguments.GetValue("conntype");
        }

        if (arguments.Contains("activate"))
        {
            builder.IsActive = arguments.GetBoolean("activate");
        }

        if (arguments.ContainsAny("kerberos", "psk", "rootca"))
        {
            List<IPSecurityAuthenticationMethodDefinition> authenticationMethods = [];
            foreach ((string key, string value) in arguments.GetOrderedValues("kerberos", "psk", "rootca"))
            {
                if (key.Equals("kerberos", StringComparison.OrdinalIgnoreCase)
                    && ParsedArguments.IsTrue(value))
                {
                    authenticationMethods.Add(new IPSecurityAuthenticationMethodDefinition
                    {
                        Kind = IPSecurityAuthenticationMethodKind.Kerberos
                    });
                }
                else if (key.Equals("psk", StringComparison.OrdinalIgnoreCase)
                    && !value.Equals("no", StringComparison.OrdinalIgnoreCase))
                {
                    authenticationMethods.Add(new IPSecurityAuthenticationMethodDefinition
                    {
                        Kind = IPSecurityAuthenticationMethodKind.PreSharedKey
                    });
                }
                else if (key.Equals("rootca", StringComparison.OrdinalIgnoreCase))
                {
                    authenticationMethods.Add(ParseCertificateAuthority(value));
                }
            }

            builder.AuthenticationMethods = authenticationMethods;
        }
    }

    private static void ParseDefaultRule(ParsedArguments arguments, IDictionary<string, PolicyBuilder> policies)
    {
        string policyName = arguments.GetValue("policy");
        if (!string.IsNullOrWhiteSpace(policyName) && arguments.Contains("activate"))
        {
            GetOrAddPolicy(policies, policyName).IsDefaultResponseRuleActive = arguments.GetBoolean("activate");
        }
    }

    private static IPSecurityAuthenticationMethodDefinition ParseCertificateAuthority(string rootCa)
    {
        int optionIndex = rootCa.IndexOf(" certmap:", StringComparison.OrdinalIgnoreCase);
        if (optionIndex < 0)
        {
            optionIndex = rootCa.IndexOf(" excludecaname:", StringComparison.OrdinalIgnoreCase);
        }

        return new IPSecurityAuthenticationMethodDefinition
        {
            Kind = IPSecurityAuthenticationMethodKind.CertificateAuthority,
            Detail = optionIndex < 0 ? rootCa : rootCa[..optionIndex].Trim(),
            EnableCertificateToAccountMapping = ContainsYesOption(rootCa, "certmap"),
            ExcludeCertificateAuthorityName = ContainsYesOption(rootCa, "excludecaname")
        };
    }

    private static bool ContainsYesOption(string value, string optionName)
    {
        return value.Contains($"{optionName}:yes", StringComparison.OrdinalIgnoreCase);
    }

    private static PolicyBuilder GetOrAddPolicy(IDictionary<string, PolicyBuilder> policies, string name)
    {
        if (!policies.TryGetValue(name, out PolicyBuilder? builder))
        {
            builder = new PolicyBuilder { Name = name };
            policies.Add(name, builder);
        }

        return builder;
    }

    private static FilterListBuilder GetOrAddFilterList(
        IDictionary<string, FilterListBuilder> filterLists,
        string name)
    {
        if (!filterLists.TryGetValue(name, out FilterListBuilder? builder))
        {
            builder = new FilterListBuilder { Name = name };
            filterLists.Add(name, builder);
        }

        return builder;
    }

    private static FilterActionBuilder GetOrAddFilterAction(
        IDictionary<string, FilterActionBuilder> filterActions,
        string name)
    {
        if (!filterActions.TryGetValue(name, out FilterActionBuilder? builder))
        {
            builder = new FilterActionBuilder { Name = name };
            filterActions.Add(name, builder);
        }

        return builder;
    }

    private static IPSecurityFilterActionKind ParseAction(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "block" => IPSecurityFilterActionKind.Block,
            "negotiate" => IPSecurityFilterActionKind.Negotiate,
            _ => IPSecurityFilterActionKind.Permit
        };
    }

    private static IReadOnlyList<string> SplitMethods(string value)
    {
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> GetLogicalLines(string dump)
    {
        StringBuilder logicalLine = new();
        using StringReader reader = new(dump);

        while (reader.ReadLine() is { } rawLine)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            bool continues = line.EndsWith('^');
            if (continues)
            {
                line = line[..^1].TrimEnd();
            }

            if (logicalLine.Length > 0)
            {
                logicalLine.Append(' ');
            }

            logicalLine.Append(line);
            if (!continues)
            {
                yield return logicalLine.ToString();
                logicalLine.Clear();
            }
        }

        if (logicalLine.Length > 0)
        {
            yield return logicalLine.ToString();
        }
    }

    private static IReadOnlyList<string> Tokenize(string commandLine)
    {
        List<string> tokens = [];
        StringBuilder token = new();
        bool inQuotes = false;

        for (int index = 0; index < commandLine.Length; index++)
        {
            char character = commandLine[index];
            if (character == '\\' && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
            {
                token.Append('"');
                index++;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (token.Length > 0)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                }

                continue;
            }

            token.Append(character);
        }

        if (token.Length > 0)
        {
            tokens.Add(token.ToString());
        }

        return tokens;
    }

    private static int IndexOfToken(IReadOnlyList<string> tokens, string value)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed class ParsedArguments
    {
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<OrderedArgument> _orderedValues = [];

        private ParsedArguments()
        {
        }

        public static ParsedArguments Create(IReadOnlyList<string> tokens, int startIndex)
        {
            ParsedArguments arguments = new();
            string? lastKey = null;

            for (int index = startIndex; index < tokens.Count; index++)
            {
                string token = tokens[index];
                string key;
                string value;

                if (token.Equals("=", StringComparison.Ordinal))
                {
                    continue;
                }

                if (index + 2 < tokens.Count && tokens[index + 1].Equals("=", StringComparison.Ordinal))
                {
                    key = token;
                    value = tokens[index + 2];
                    index += 2;
                    lastKey = key;
                    arguments.Add(key, value);
                    continue;
                }

                int equalsIndex = token.IndexOf('=');
                if (equalsIndex >= 0)
                {
                    key = token[..equalsIndex].Trim();
                    value = token[(equalsIndex + 1)..].Trim();
                    if (value.Length == 0 && index + 1 < tokens.Count)
                    {
                        value = tokens[++index].Equals("=", StringComparison.Ordinal)
                            && index + 1 < tokens.Count
                                ? tokens[++index]
                                : tokens[index];
                    }

                    lastKey = key;
                    arguments.Add(key, value);
                    continue;
                }

                if (lastKey is not null)
                {
                    arguments.Append(lastKey, token);
                }
            }

            return arguments;
        }

        public bool Contains(string key)
        {
            return _values.ContainsKey(key);
        }

        public bool ContainsAny(params string[] keys)
        {
            return keys.Any(Contains);
        }

        public string GetValue(string key)
        {
            return _values.TryGetValue(key, out List<string>? values) && values.Count > 0
                ? values[0]
                : string.Empty;
        }

        public IReadOnlyList<string> GetValues(string key)
        {
            return _values.TryGetValue(key, out List<string>? values) ? values : [];
        }

        public bool GetBoolean(string key)
        {
            return IsTrue(GetValue(key));
        }

        public IReadOnlyList<(string Key, string Value)> GetOrderedValues(params string[] keys)
        {
            HashSet<string> requestedKeys = new(keys, StringComparer.OrdinalIgnoreCase);
            return _orderedValues
                .Where(argument => requestedKeys.Contains(argument.Key))
                .Select(argument => (argument.Key, argument.Value))
                .ToList();
        }

        public static bool IsTrue(string value)
        {
            return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        public int GetInteger(string key)
        {
            return int.TryParse(GetValue(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }

        private void Add(string key, string value)
        {
            if (!_values.TryGetValue(key, out List<string>? values))
            {
                values = [];
                _values.Add(key, values);
            }

            values.Add(value);
            _orderedValues.Add(new OrderedArgument(key, value));
        }

        private void Append(string key, string value)
        {
            List<string> values = _values[key];
            int lastIndex = values.Count - 1;
            values[lastIndex] = $"{values[lastIndex]} {value}";

            int orderedIndex = _orderedValues.Count - 1;
            OrderedArgument orderedArgument = _orderedValues[orderedIndex];
            _orderedValues[orderedIndex] = orderedArgument with
            {
                Value = $"{orderedArgument.Value} {value}"
            };
        }

        private readonly record struct OrderedArgument(string Key, string Value);
    }

    private sealed class PolicyBuilder
    {
        public required string Name { get; init; }

        public string Description { get; set; } = string.Empty;

        public bool IsAssigned { get; set; }

        public bool UseMasterPerfectForwardSecrecy { get; set; }

        public int QuickModeSessionsPerMainMode { get; set; }

        public int MainModeLifetimeMinutes { get; set; }

        public bool IsDefaultResponseRuleActive { get; set; }

        public int PollingIntervalMinutes { get; set; }

        public IReadOnlyList<string> MainModeSecurityMethods { get; set; } = [];

        private Dictionary<string, RuleBuilder> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);

        public RuleBuilder GetOrAddRule(string name)
        {
            if (!Rules.TryGetValue(name, out RuleBuilder? builder))
            {
                builder = new RuleBuilder
                {
                    Name = name,
                    PolicyName = Name
                };
                Rules.Add(name, builder);
            }

            return builder;
        }

        public IPSecurityPolicyDefinition Build()
        {
            return new IPSecurityPolicyDefinition
            {
                Name = Name,
                Description = Description,
                IsAssigned = IsAssigned,
                UseMasterPerfectForwardSecrecy = UseMasterPerfectForwardSecrecy,
                QuickModeSessionsPerMainMode = QuickModeSessionsPerMainMode,
                MainModeLifetimeMinutes = MainModeLifetimeMinutes,
                IsDefaultResponseRuleActive = IsDefaultResponseRuleActive,
                PollingIntervalMinutes = PollingIntervalMinutes,
                MainModeSecurityMethods = MainModeSecurityMethods,
                Rules = Rules.Values.Select(static rule => rule.Build()).ToList()
            };
        }
    }

    private sealed class FilterListBuilder
    {
        public required string Name { get; init; }

        public string Description { get; set; } = string.Empty;

        public List<IPSecurityFilterDefinition> Filters { get; } = [];

        public IPSecurityFilterListDefinition Build()
        {
            return new IPSecurityFilterListDefinition
            {
                Name = Name,
                Description = Description,
                Filters = Filters
            };
        }
    }

    private sealed class FilterActionBuilder
    {
        public required string Name { get; init; }

        public string Description { get; set; } = string.Empty;

        public IPSecurityFilterActionKind Action { get; set; }

        public bool UseQuickModePerfectForwardSecrecy { get; set; }

        public bool AcceptUnsecuredInbound { get; set; }

        public bool AllowUnsecuredFallback { get; set; }

        public IReadOnlyList<string> QuickModeSecurityMethods { get; set; } = [];

        public IPSecurityFilterActionDefinition Build()
        {
            return new IPSecurityFilterActionDefinition
            {
                Name = Name,
                Description = Description,
                Action = Action,
                UseQuickModePerfectForwardSecrecy = UseQuickModePerfectForwardSecrecy,
                AcceptUnsecuredInbound = AcceptUnsecuredInbound,
                AllowUnsecuredFallback = AllowUnsecuredFallback,
                QuickModeSecurityMethods = QuickModeSecurityMethods
            };
        }
    }

    private sealed class RuleBuilder
    {
        public required string Name { get; init; }

        public required string PolicyName { get; init; }

        public string Description { get; set; } = string.Empty;

        public string FilterListName { get; set; } = string.Empty;

        public string FilterActionName { get; set; } = string.Empty;

        public string TunnelEndpoint { get; set; } = string.Empty;

        public string ConnectionType { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public IReadOnlyList<IPSecurityAuthenticationMethodDefinition> AuthenticationMethods { get; set; } = [];

        public IPSecurityRuleDefinition Build()
        {
            return new IPSecurityRuleDefinition
            {
                Name = Name,
                PolicyName = PolicyName,
                Description = Description,
                FilterListName = FilterListName,
                FilterActionName = FilterActionName,
                TunnelEndpoint = TunnelEndpoint,
                ConnectionType = ConnectionType,
                IsActive = IsActive,
                AuthenticationMethods = AuthenticationMethods
            };
        }
    }
}
