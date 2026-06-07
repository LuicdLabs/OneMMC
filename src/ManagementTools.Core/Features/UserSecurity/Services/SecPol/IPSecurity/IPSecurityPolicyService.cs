using System.Globalization;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Builds the localized IP Security Policies presentation model from the legacy static policy store.
/// </summary>
public sealed class IPSecurityPolicyService
{
    private readonly IPSecurityStaticPolicyStoreService _staticPolicyStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityPolicyService"/> class.
    /// </summary>
    /// <param name="staticPolicyStore">The legacy static local IPsec policy store.</param>
    public IPSecurityPolicyService(IPSecurityStaticPolicyStoreService staticPolicyStore)
    {
        _staticPolicyStore = staticPolicyStore;
    }

    /// <summary>
    /// Loads the IP Security Policies snapshot.
    /// </summary>
    /// <returns>The loaded policy snapshot.</returns>
    public IPSecurityPolicySnapshot LoadSnapshot()
    {
        IPSecurityStaticStoreSnapshot snapshot = _staticPolicyStore.LoadSnapshot();
        return new IPSecurityPolicySnapshot
        {
            Policies = snapshot.Policies.Select(CreatePolicyRow).ToList(),
            FilterLists = snapshot.FilterLists.Select(CreateFilterListRow).ToList(),
            FilterActions = snapshot.FilterActions.Select(CreateFilterActionRow).ToList()
        };
    }

    private static IPSecurityPolicyRow CreatePolicyRow(IPSecurityPolicyDefinition policy)
    {
        string rulesSummary = string.Format(
            CultureInfo.CurrentCulture,
            GetString(IPSecurityPolicyKeys.RuleCountFormat),
            policy.Rules.Count);

        return new IPSecurityPolicyRow
        {
            Kind = IPSecurityPolicyRowKind.Policy,
            Name = policy.Name,
            Description = policy.Description,
            PolicyAssigned = FormatBoolean(policy.IsAssigned),
            LastModified = FormatLastModified(policy.LastModifiedTime),
            Summary = rulesSummary,
            Policy = policy,
            Details =
            [
                CreateDetail(IPSecurityPolicyKeys.DetailRowKind, GetString(IPSecurityPolicyKeys.RowKindPolicy)),
                CreateDetail(IPSecurityPolicyKeys.DetailName, policy.Name),
                CreateDetail(IPSecurityPolicyKeys.DetailDescription, policy.Description),
                CreateDetail(IPSecurityPolicyKeys.DetailPolicyAssigned, FormatBoolean(policy.IsAssigned)),
                CreateDetail(IPSecurityPolicyKeys.DetailRules, FormatRules(policy.Rules)),
                CreateDetail(IPSecurityPolicyKeys.DetailMasterPfs, FormatBoolean(policy.UseMasterPerfectForwardSecrecy)),
                CreateDetail(IPSecurityPolicyKeys.DetailQuickModeSessions, FormatInteger(policy.QuickModeSessionsPerMainMode)),
                CreateDetail(IPSecurityPolicyKeys.DetailMainModeLifetime, FormatInteger(policy.MainModeLifetimeMinutes)),
                CreateDetail(IPSecurityPolicyKeys.DetailDefaultResponseRule, FormatBoolean(policy.IsDefaultResponseRuleActive)),
                CreateDetail(IPSecurityPolicyKeys.DetailPollingInterval, FormatInteger(policy.PollingIntervalMinutes)),
                CreateDetail(IPSecurityPolicyKeys.DetailSecurityMethods, string.Join(Environment.NewLine, policy.MainModeSecurityMethods))
            ]
        };
    }

    private static IPSecurityPolicyRow CreateFilterListRow(IPSecurityFilterListDefinition filterList)
    {
        string filterCount = string.Format(
            CultureInfo.CurrentCulture,
            GetString(IPSecurityPolicyKeys.FilterCountFormat),
            filterList.Filters.Count);

        return new IPSecurityPolicyRow
        {
            Kind = IPSecurityPolicyRowKind.FilterList,
            Name = filterList.Name,
            Description = filterList.Description,
            PolicyAssigned = filterCount,
            Summary = FormatFilters(filterList.Filters),
            FilterList = filterList,
            Details =
            [
                CreateDetail(IPSecurityPolicyKeys.DetailRowKind, GetString(IPSecurityPolicyKeys.RowKindFilterList)),
                CreateDetail(IPSecurityPolicyKeys.DetailName, filterList.Name),
                CreateDetail(IPSecurityPolicyKeys.DetailDescription, filterList.Description),
                CreateDetail(IPSecurityPolicyKeys.DetailFilterCount, filterCount),
                CreateDetail(IPSecurityPolicyKeys.DetailFilters, FormatFilters(filterList.Filters))
            ]
        };
    }

    private static IPSecurityPolicyRow CreateFilterActionRow(IPSecurityFilterActionDefinition filterAction)
    {
        string methodCount = string.Format(
            CultureInfo.CurrentCulture,
            GetString(IPSecurityPolicyKeys.SecurityMethodCountFormat),
            filterAction.QuickModeSecurityMethods.Count);

        return new IPSecurityPolicyRow
        {
            Kind = IPSecurityPolicyRowKind.FilterAction,
            Name = filterAction.Name,
            Description = filterAction.Description,
            PolicyAssigned = FormatFilterAction(filterAction.Action),
            LastModified = methodCount,
            Summary = string.Join(Environment.NewLine, filterAction.QuickModeSecurityMethods),
            FilterAction = filterAction,
            Details =
            [
                CreateDetail(IPSecurityPolicyKeys.DetailRowKind, GetString(IPSecurityPolicyKeys.RowKindFilterAction)),
                CreateDetail(IPSecurityPolicyKeys.DetailName, filterAction.Name),
                CreateDetail(IPSecurityPolicyKeys.DetailDescription, filterAction.Description),
                CreateDetail(IPSecurityPolicyKeys.DetailAction, FormatFilterAction(filterAction.Action)),
                CreateDetail(IPSecurityPolicyKeys.DetailQuickModePfs, FormatBoolean(filterAction.UseQuickModePerfectForwardSecrecy)),
                CreateDetail(IPSecurityPolicyKeys.DetailAcceptUnsecuredInbound, FormatBoolean(filterAction.AcceptUnsecuredInbound)),
                CreateDetail(IPSecurityPolicyKeys.DetailAllowUnsecuredFallback, FormatBoolean(filterAction.AllowUnsecuredFallback)),
                CreateDetail(IPSecurityPolicyKeys.DetailSecurityMethods, string.Join(Environment.NewLine, filterAction.QuickModeSecurityMethods))
            ]
        };
    }

    private static string FormatRules(IReadOnlyList<IPSecurityRuleDefinition> rules)
    {
        return string.Join(
            Environment.NewLine,
            rules.Select(static rule =>
                $"{rule.Name}: {rule.FilterListName} -> {rule.FilterActionName} ({rule.ConnectionType})"));
    }

    private static string FormatFilters(IReadOnlyList<IPSecurityFilterDefinition> filters)
    {
        return string.Join(
            Environment.NewLine,
            filters.Select(static filter =>
                $"{filter.SourceAddress} -> {filter.DestinationAddress}; {filter.Protocol}; {filter.SourcePort}/{filter.DestinationPort}"));
    }

    private static string FormatFilterAction(IPSecurityFilterActionKind action)
    {
        return action switch
        {
            IPSecurityFilterActionKind.Block => GetString(IPSecurityPolicyKeys.FilterActionBlock),
            IPSecurityFilterActionKind.Negotiate => GetString(IPSecurityPolicyKeys.FilterActionNegotiate),
            _ => GetString(IPSecurityPolicyKeys.FilterActionPermit)
        };
    }

    private static string FormatBoolean(bool value)
    {
        return GetString(value ? IPSecurityPolicyKeys.ValueYes : IPSecurityPolicyKeys.ValueNo);
    }

    private static string FormatLastModified(DateTimeOffset? lastModified)
    {
        return lastModified is { } value
            ? value.LocalDateTime.ToString(CultureInfo.CurrentCulture)
            : string.Empty;
    }

    private static string FormatInteger(int value)
    {
        return value.ToString(CultureInfo.CurrentCulture);
    }

    private static IPSecurityPolicyDetailItem CreateDetail(string nameKey, string value)
    {
        return new IPSecurityPolicyDetailItem
        {
            Name = GetString(nameKey),
            Value = value
        };
    }

    private static string GetString(string key)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
