using System.Globalization;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Validates legacy static IPsec mutations and builds policy-script tokens consumed by
/// <see cref="IPSecurityStaticPolicyNativeClient"/>.
/// </summary>
/// <remarks>
/// This type is side-effect free. It never opens the policy store, changes a policy store, or logs command values.
/// Returned lists begin with <c>ipsec static</c> and are converted into native policy-script lines at execution time.
/// </remarks>
public static class IPSecurityStaticPolicyCommandBuilder
{
    private const string AllSelector = "all";

    /// <summary>Builds an <c>add policy</c> command.</summary>
    /// <param name="options">The policy options.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildAddPolicy(IPSecurityPolicyCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options.Name));
        RejectCreateRename(options.NewName, nameof(options.NewName));
        ValidatePolicyOptions(options);

        List<string> arguments = Start("add", "policy");
        Add(arguments, "name", options.Name);
        AddOptional(arguments, "description", options.Description);
        AddOptional(arguments, "mmpfs", options.UseMasterPerfectForwardSecrecy);
        AddOptional(arguments, "qmpermm", options.QuickModeSessionsPerMainMode);
        AddOptional(arguments, "mmlifetime", options.MainModeLifetimeMinutes);
        AddOptional(arguments, "activatedefaultrule", options.IsDefaultResponseRuleActive);
        AddOptional(arguments, "pollinginterval", options.PollingIntervalMinutes);
        AddOptional(arguments, "assign", options.IsAssigned);
        AddMethods(arguments, "mmsecmethods", options.MainModeSecurityMethods);
        return arguments;
    }

    /// <summary>Builds a <c>set policy</c> command.</summary>
    /// <param name="options">The policy changes.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildSetPolicy(IPSecurityPolicyCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options.Name));
        ValidateOptionalName(options.NewName, nameof(options.NewName));
        ValidatePolicyOptions(options);
        RequireChange(
            options.NewName,
            options.Description,
            options.UseMasterPerfectForwardSecrecy,
            options.QuickModeSessionsPerMainMode,
            options.MainModeLifetimeMinutes,
            options.IsDefaultResponseRuleActive,
            options.PollingIntervalMinutes,
            options.IsAssigned,
            options.MainModeSecurityMethods);

        List<string> arguments = Start("set", "policy");
        Add(arguments, "name", options.Name);
        AddOptional(arguments, "newname", options.NewName);
        AddOptional(arguments, "description", options.Description);
        AddOptional(arguments, "mmpfs", options.UseMasterPerfectForwardSecrecy);
        AddOptional(arguments, "qmpermm", options.QuickModeSessionsPerMainMode);
        AddOptional(arguments, "mmlifetime", options.MainModeLifetimeMinutes);
        AddOptional(arguments, "activatedefaultrule", options.IsDefaultResponseRuleActive);
        AddOptional(arguments, "pollinginterval", options.PollingIntervalMinutes);
        AddOptional(arguments, "assign", options.IsAssigned);
        AddMethods(arguments, "mmsecmethods", options.MainModeSecurityMethods);
        return arguments;
    }

    /// <summary>Builds a command that assigns or unassigns one policy.</summary>
    /// <param name="policyName">The policy name.</param>
    /// <param name="isAssigned">Whether the policy should be assigned.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildAssignPolicy(string policyName, bool isAssigned)
    {
        ValidateName(policyName, nameof(policyName));
        List<string> arguments = Start("set", "policy");
        Add(arguments, "name", policyName);
        Add(arguments, "assign", YesNo(isAssigned));
        return arguments;
    }

    /// <summary>Builds a command that deletes one policy and its rules.</summary>
    /// <param name="policyName">The policy name.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildDeletePolicy(string policyName)
    {
        ValidateName(policyName, nameof(policyName));
        List<string> arguments = Start("delete", "policy");
        Add(arguments, "name", policyName);
        return arguments;
    }

    /// <summary>Builds an <c>add filterlist</c> command.</summary>
    /// <param name="options">The filter-list options.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildAddFilterList(IPSecurityFilterListCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options.Name));
        RejectCreateRename(options.NewName, nameof(options.NewName));
        ValidateOptionalText(options.Description, nameof(options.Description));

        List<string> arguments = Start("add", "filterlist");
        Add(arguments, "name", options.Name);
        AddOptional(arguments, "description", options.Description);
        return arguments;
    }

    /// <summary>Builds a <c>set filterlist</c> command.</summary>
    /// <param name="options">The filter-list changes.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildSetFilterList(IPSecurityFilterListCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options.Name));
        ValidateOptionalName(options.NewName, nameof(options.NewName));
        ValidateOptionalText(options.Description, nameof(options.Description));
        RequireChange(options.NewName, options.Description);

        List<string> arguments = Start("set", "filterlist");
        Add(arguments, "name", options.Name);
        AddOptional(arguments, "newname", options.NewName);
        AddOptional(arguments, "description", options.Description);
        return arguments;
    }

    /// <summary>Builds a command that deletes one filter list and its filters.</summary>
    /// <param name="filterListName">The filter-list name.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildDeleteFilterList(string filterListName)
    {
        ValidateName(filterListName, nameof(filterListName));
        List<string> arguments = Start("delete", "filterlist");
        Add(arguments, "name", filterListName);
        return arguments;
    }

    /// <summary>Builds an <c>add filter</c> command.</summary>
    /// <param name="options">The filter options.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildAddFilter(IPSecurityFilterCommandOptions options)
    {
        ValidateFilter(options, allowDescription: true);
        List<string> arguments = BuildFilterIdentity("add", options);
        AddOptional(arguments, "description", options.Description);
        return arguments;
    }

    /// <summary>Builds a command that deletes one exact-match filter.</summary>
    /// <param name="options">The exact filter identity. A description is not accepted because the store does not use it for matching.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildDeleteFilter(IPSecurityFilterCommandOptions options)
    {
        ValidateFilter(options, allowDescription: false);
        return BuildFilterIdentity("delete", options);
    }

    /// <summary>Builds an <c>add filteraction</c> command.</summary>
    /// <param name="options">The filter-action options.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildAddFilterAction(IPSecurityFilterActionCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options.Name));
        RejectCreateRename(options.NewName, nameof(options.NewName));
        if (options.Action is null)
        {
            throw new ArgumentException("An action is required when creating a filter action.", nameof(options));
        }

        ValidateFilterActionOptions(options);
        List<string> arguments = Start("add", "filteraction");
        Add(arguments, "name", options.Name);
        AddFilterActionOptions(arguments, options);
        return arguments;
    }

    /// <summary>Builds a <c>set filteraction</c> command.</summary>
    /// <param name="options">The filter-action changes.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildSetFilterAction(IPSecurityFilterActionCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options.Name));
        ValidateOptionalName(options.NewName, nameof(options.NewName));
        ValidateFilterActionOptions(options);
        RequireChange(
            options.NewName,
            options.Description,
            options.UseQuickModePerfectForwardSecrecy,
            options.AcceptUnsecuredInbound,
            options.AllowUnsecuredFallback,
            options.Action,
            options.QuickModeSecurityMethods);

        List<string> arguments = Start("set", "filteraction");
        Add(arguments, "name", options.Name);
        AddFilterActionOptions(arguments, options);
        return arguments;
    }

    /// <summary>Builds a command that deletes one filter action.</summary>
    /// <param name="filterActionName">The filter-action name.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildDeleteFilterAction(string filterActionName)
    {
        ValidateName(filterActionName, nameof(filterActionName));
        List<string> arguments = Start("delete", "filteraction");
        Add(arguments, "name", filterActionName);
        return arguments;
    }

    /// <summary>Builds an <c>add rule</c> command.</summary>
    /// <param name="options">The rule options.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildAddRule(IPSecurityRuleCommandOptions options)
    {
        ValidateRuleOptions(options, isCreate: true);
        List<string> arguments = Start("add", "rule");
        Add(arguments, "name", options.Name);
        Add(arguments, "policy", options.PolicyName);
        Add(arguments, "filterlist", options.FilterListName!);
        Add(arguments, "filteraction", options.FilterActionName!);
        AddRuleOptions(arguments, options, includeReferences: false);
        return arguments;
    }

    /// <summary>Builds a <c>set rule</c> command.</summary>
    /// <param name="options">The rule changes.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildSetRule(IPSecurityRuleCommandOptions options)
    {
        ValidateRuleOptions(options, isCreate: false);
        RequireChange(
            options.NewName,
            options.Description,
            options.FilterListName,
            options.FilterActionName,
            options.TunnelEndpoint,
            options.ConnectionType,
            options.IsActive,
            options.AuthenticationMethods);

        List<string> arguments = Start("set", "rule");
        Add(arguments, "name", options.Name);
        Add(arguments, "policy", options.PolicyName);
        AddRuleOptions(arguments, options, includeReferences: true);
        return arguments;
    }

    /// <summary>Builds a command that deletes one named rule from one policy.</summary>
    /// <param name="policyName">The owning policy name.</param>
    /// <param name="ruleName">The rule name.</param>
    /// <returns>The validated policy-script argument tokens.</returns>
    public static IReadOnlyList<string> BuildDeleteRule(string policyName, string ruleName)
    {
        ValidateName(policyName, nameof(policyName));
        ValidateName(ruleName, nameof(ruleName));
        List<string> arguments = Start("delete", "rule");
        Add(arguments, "name", ruleName);
        Add(arguments, "policy", policyName);
        return arguments;
    }

    private static void ValidatePolicyOptions(IPSecurityPolicyCommandOptions options)
    {
        ValidateOptionalText(options.Description, nameof(options.Description));
        ValidatePositive(options.QuickModeSessionsPerMainMode, nameof(options.QuickModeSessionsPerMainMode));
        ValidatePositive(options.MainModeLifetimeMinutes, nameof(options.MainModeLifetimeMinutes));
        ValidatePositive(options.PollingIntervalMinutes, nameof(options.PollingIntervalMinutes));
        ValidateMethods(options.MainModeSecurityMethods, nameof(options.MainModeSecurityMethods));

        if (options.UseMasterPerfectForwardSecrecy is true
            && options.QuickModeSessionsPerMainMode is not null
            && options.QuickModeSessionsPerMainMode != 1)
        {
            throw new ArgumentException(
                "Master perfect forward secrecy requires one quick-mode session per main-mode session.",
                nameof(options));
        }
    }

    private static void ValidateFilter(IPSecurityFilterCommandOptions options, bool allowDescription)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.FilterListName, nameof(options.FilterListName));
        ValidateTokenValue(options.SourceAddress, nameof(options.SourceAddress));
        ValidateTokenValue(options.DestinationAddress, nameof(options.DestinationAddress));
        ValidateOptionalTokenValue(options.Protocol, nameof(options.Protocol));
        ValidateOptionalTokenValue(options.SourceMask, nameof(options.SourceMask));
        ValidateOptionalTokenValue(options.DestinationMask, nameof(options.DestinationMask));
        ValidatePort(options.SourcePort, nameof(options.SourcePort));
        ValidatePort(options.DestinationPort, nameof(options.DestinationPort));

        if (!allowDescription && options.Description is not null)
        {
            throw new ArgumentException("A description is not part of an exact-match filter identity.", nameof(options));
        }

        ValidateOptionalText(options.Description, nameof(options.Description));
        if (IsAddressRange(options.SourceAddress) && options.SourceMask is not null)
        {
            throw new ArgumentException("A source mask cannot be combined with a source address range.", nameof(options));
        }

        if (IsAddressRange(options.DestinationAddress) && options.DestinationMask is not null)
        {
            throw new ArgumentException("A destination mask cannot be combined with a destination address range.", nameof(options));
        }

        bool sourceIsServer = IsServerType(options.SourceAddress);
        bool destinationIsServer = IsServerType(options.DestinationAddress);
        if ((sourceIsServer && !IsMe(options.DestinationAddress))
            || (destinationIsServer && !IsMe(options.SourceAddress)))
        {
            throw new ArgumentException("A server-type address must be paired with the current-computer address.", nameof(options));
        }

        string protocol = options.Protocol ?? "ANY";
        bool supportsPorts = protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("UDP", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("6", StringComparison.Ordinal)
            || protocol.Equals("17", StringComparison.Ordinal);
        if (!supportsPorts && (options.SourcePort is > 0 || options.DestinationPort is > 0))
        {
            throw new ArgumentException("Nonzero ports require TCP or UDP.", nameof(options));
        }

        if (!IsValidProtocol(protocol))
        {
            throw new ArgumentException("The protocol must be ANY, ICMP, TCP, UDP, RAW, or a number from 0 through 255.", nameof(options));
        }
    }

    private static List<string> BuildFilterIdentity(string verb, IPSecurityFilterCommandOptions options)
    {
        List<string> arguments = Start(verb, "filter");
        Add(arguments, "filterlist", options.FilterListName);
        Add(arguments, "srcaddr", options.SourceAddress);
        Add(arguments, "dstaddr", options.DestinationAddress);
        AddOptional(arguments, "protocol", options.Protocol);
        AddOptional(arguments, "mirrored", options.IsMirrored);
        AddOptional(arguments, "srcmask", options.SourceMask);
        AddOptional(arguments, "dstmask", options.DestinationMask);
        AddOptional(arguments, "srcport", options.SourcePort);
        AddOptional(arguments, "dstport", options.DestinationPort);
        return arguments;
    }

    private static void ValidateFilterActionOptions(IPSecurityFilterActionCommandOptions options)
    {
        ValidateOptionalText(options.Description, nameof(options.Description));
        ValidateMethods(options.QuickModeSecurityMethods, nameof(options.QuickModeSecurityMethods));

        if (options.Action is not null && !Enum.IsDefined(options.Action.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Action));
        }

        if (options.Action is IPSecurityFilterActionKind.Permit or IPSecurityFilterActionKind.Block
            && (options.UseQuickModePerfectForwardSecrecy is true
                || options.AcceptUnsecuredInbound is true
                || options.AllowUnsecuredFallback is true
                || options.QuickModeSecurityMethods is { Count: > 0 }))
        {
            throw new ArgumentException("Negotiation settings require the negotiate filter action.", nameof(options));
        }
    }

    private static void AddFilterActionOptions(List<string> arguments, IPSecurityFilterActionCommandOptions options)
    {
        AddOptional(arguments, "newname", options.NewName);
        AddOptional(arguments, "description", options.Description);
        AddOptional(arguments, "qmpfs", options.UseQuickModePerfectForwardSecrecy);
        AddOptional(arguments, "inpass", options.AcceptUnsecuredInbound);
        AddOptional(arguments, "soft", options.AllowUnsecuredFallback);
        if (options.Action is not null)
        {
            Add(arguments, "action", options.Action.Value.ToString().ToLowerInvariant());
        }

        AddMethods(arguments, "qmsecmethods", options.QuickModeSecurityMethods);
    }

    private static void ValidateRuleOptions(IPSecurityRuleCommandOptions options, bool isCreate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options.Name));
        ValidateName(options.PolicyName, nameof(options.PolicyName));
        ValidateOptionalName(options.NewName, nameof(options.NewName));
        ValidateOptionalText(options.Description, nameof(options.Description));
        ValidateOptionalName(options.FilterListName, nameof(options.FilterListName));
        ValidateOptionalName(options.FilterActionName, nameof(options.FilterActionName));
        ValidateOptionalTokenValue(options.TunnelEndpoint, nameof(options.TunnelEndpoint));

        if (isCreate)
        {
            RejectCreateRename(options.NewName, nameof(options.NewName));
            ValidateName(options.FilterListName, nameof(options.FilterListName));
            ValidateName(options.FilterActionName, nameof(options.FilterActionName));
        }

        if (options.ConnectionType is not null && !Enum.IsDefined(options.ConnectionType.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(options.ConnectionType));
        }

        ValidateAuthenticationMethods(options.AuthenticationMethods);
        if (!isCreate && options.AuthenticationMethods is { Count: 0 })
        {
            throw new ArgumentException(
                "An empty authentication list cannot be represented unambiguously by set rule.",
                nameof(options));
        }
    }

    private static void AddRuleOptions(
        List<string> arguments,
        IPSecurityRuleCommandOptions options,
        bool includeReferences)
    {
        AddOptional(arguments, "newname", options.NewName);
        AddOptional(arguments, "description", options.Description);
        if (includeReferences)
        {
            AddOptional(arguments, "filterlist", options.FilterListName);
            AddOptional(arguments, "filteraction", options.FilterActionName);
        }

        AddOptional(arguments, "tunnel", options.TunnelEndpoint);
        if (options.ConnectionType is not null)
        {
            Add(arguments, "conntype", options.ConnectionType.Value switch
            {
                IPSecurityRuleConnectionType.All => "all",
                IPSecurityRuleConnectionType.Lan => "lan",
                IPSecurityRuleConnectionType.DialUp => "dialup",
                _ => throw new ArgumentOutOfRangeException(nameof(options.ConnectionType))
            });
        }

        AddOptional(arguments, "activate", options.IsActive);
        if (options.AuthenticationMethods is not null)
        {
            foreach (IPSecurityRuleAuthenticationCommand method in options.AuthenticationMethods)
            {
                switch (method)
                {
                    case IPSecurityKerberosAuthenticationCommand:
                        Add(arguments, "kerberos", "yes");
                        break;
                    case IPSecurityPreSharedKeyAuthenticationCommand preSharedKey:
                        Add(arguments, "psk", preSharedKey.GetPreSharedKey());
                        break;
                    case IPSecurityCertificateAuthenticationCommand certificate:
                        string certificateValue = certificate.CertificateAuthorityName.Replace("\"", "\\'", StringComparison.Ordinal);
                        certificateValue = $"{certificateValue} certmap:{YesNo(certificate.EnableCertificateToAccountMapping)}"
                            + $" excludecaname:{YesNo(certificate.ExcludeCertificateAuthorityName)}";
                        Add(arguments, "rootca", certificateValue);
                        break;
                }
            }
        }
    }

    private static void ValidateAuthenticationMethods(IReadOnlyList<IPSecurityRuleAuthenticationCommand>? methods)
    {
        if (methods is null)
        {
            return;
        }

        bool hasKerberos = false;
        bool hasPreSharedKey = false;
        foreach (IPSecurityRuleAuthenticationCommand? method in methods)
        {
            switch (method)
            {
                case null:
                    throw new ArgumentException("Authentication methods cannot contain null entries.", nameof(methods));
                case IPSecurityKerberosAuthenticationCommand:
                    if (hasKerberos)
                    {
                        throw new ArgumentException("Kerberos authentication can be specified only once.", nameof(methods));
                    }

                    hasKerberos = true;
                    break;
                case IPSecurityPreSharedKeyAuthenticationCommand preSharedKey:
                    if (hasPreSharedKey)
                    {
                        throw new ArgumentException("Pre-shared-key authentication can be specified only once.", nameof(methods));
                    }

                    ValidateSensitiveValue(preSharedKey.GetPreSharedKey(), nameof(methods));
                    hasPreSharedKey = true;
                    break;
                case IPSecurityCertificateAuthenticationCommand certificate:
                    ValidateText(certificate.CertificateAuthorityName, nameof(methods), allowEmpty: false);
                    break;
                default:
                    throw new ArgumentException("The authentication method type is not supported.", nameof(methods));
            }
        }
    }

    private static bool IsValidProtocol(string protocol)
    {
        if (protocol.Equals("ANY", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("ICMP", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("UDP", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("RAW", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(protocol, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value is >= 0 and <= 255;
    }

    private static bool IsServerType(string address)
    {
        return address.Equals("WINS", StringComparison.OrdinalIgnoreCase)
            || address.Equals("DNS", StringComparison.OrdinalIgnoreCase)
            || address.Equals("DHCP", StringComparison.OrdinalIgnoreCase)
            || address.Equals("GATEWAY", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMe(string address)
    {
        return address.Equals("me", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAddressRange(string address)
    {
        return address.Contains('-');
    }

    private static void ValidateName(string? value, string parameterName)
    {
        ValidateText(value, parameterName, allowEmpty: false);
        if (!value!.Equals(value.Trim(), StringComparison.Ordinal)
            || value.Equals(AllSelector, StringComparison.OrdinalIgnoreCase)
            || value.Contains('=') || value.Contains('"'))
        {
            throw new ArgumentException("The name is invalid or ambiguous.", parameterName);
        }
    }

    private static void ValidateOptionalName(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateName(value, parameterName);
        }
    }

    private static void ValidateOptionalText(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateText(value, parameterName, allowEmpty: true);
        }
    }

    private static void ValidateOptionalTokenValue(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateTokenValue(value, parameterName);
        }
    }

    private static void ValidateTokenValue(string? value, string parameterName)
    {
        ValidateText(value, parameterName, allowEmpty: false);
        string validatedValue = value!;
        if (validatedValue.Any(char.IsWhiteSpace) || validatedValue.Contains('=') || validatedValue.Contains('"'))
        {
            throw new ArgumentException("The token value is invalid.", parameterName);
        }
    }

    private static void ValidateSensitiveValue(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsControl))
        {
            throw new ArgumentException("The sensitive value is invalid.", parameterName);
        }
    }

    private static void ValidateText(string? value, string parameterName, bool allowEmpty)
    {
        if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Any(char.IsControl))
        {
            throw new ArgumentException("The value is invalid.", parameterName);
        }
    }

    private static void ValidatePositive(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePort(int? value, string parameterName)
    {
        if (value is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateMethods(IReadOnlyList<string>? methods, string parameterName)
    {
        if (methods is null)
        {
            return;
        }

        if (methods.Count == 0)
        {
            throw new ArgumentException("A security-method list cannot be empty.", parameterName);
        }

        foreach (string? method in methods)
        {
            ValidateTokenValue(method, parameterName);
        }
    }

    private static void RejectCreateRename(string? newName, string parameterName)
    {
        if (newName is not null)
        {
            throw new ArgumentException("A create command cannot rename an object.", parameterName);
        }
    }

    private static void RequireChange(params object?[] values)
    {
        if (values.All(static value => value is null))
        {
            throw new ArgumentException("At least one change is required.", nameof(values));
        }
    }

    private static List<string> Start(string verb, string objectKind)
    {
        return ["ipsec", "static", verb, objectKind];
    }

    private static void Add(List<string> arguments, string key, string value)
    {
        arguments.Add($"{key}={value}");
    }

    private static void AddOptional(List<string> arguments, string key, string? value)
    {
        if (value is not null)
        {
            Add(arguments, key, value);
        }
    }

    private static void AddOptional(List<string> arguments, string key, bool? value)
    {
        if (value is not null)
        {
            Add(arguments, key, YesNo(value.Value));
        }
    }

    private static void AddOptional(List<string> arguments, string key, int? value)
    {
        if (value is not null)
        {
            Add(arguments, key, value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddMethods(List<string> arguments, string key, IReadOnlyList<string>? methods)
    {
        if (methods is not null)
        {
            Add(arguments, key, string.Join(' ', methods));
        }
    }

    private static string YesNo(bool value)
    {
        return value ? "yes" : "no";
    }
}
