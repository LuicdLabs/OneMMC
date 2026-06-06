using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Applies validated mutations to the legacy static local IPsec policy store.
/// </summary>
/// <remarks>
/// The service exposes only typed operations built by <see cref="IPSecurityStaticPolicyCommandBuilder"/>.
/// It never accepts arbitrary command tokens.
/// </remarks>
public sealed class IPSecurityStaticPolicyMutationService
{
    private readonly IPSecurityStaticPolicyCommandExecutor _executor;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyMutationService"/> class.
    /// </summary>
    /// <param name="executor">The restricted legacy static IPsec command executor.</param>
    public IPSecurityStaticPolicyMutationService(IPSecurityStaticPolicyCommandExecutor executor)
    {
        _executor = executor;
    }

    /// <summary>Adds a policy.</summary>
    public Task AddPolicyAsync(IPSecurityPolicyCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildAddPolicy(options), cancellationToken);

    /// <summary>Updates a policy.</summary>
    public Task SetPolicyAsync(IPSecurityPolicyCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildSetPolicy(options), cancellationToken);

    /// <summary>Assigns or unassigns a policy.</summary>
    public Task AssignPolicyAsync(string policyName, bool isAssigned, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildAssignPolicy(policyName, isAssigned), cancellationToken);

    /// <summary>Deletes a policy.</summary>
    public Task DeletePolicyAsync(string policyName, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildDeletePolicy(policyName), cancellationToken);

    /// <summary>Adds a shared filter list.</summary>
    public Task AddFilterListAsync(IPSecurityFilterListCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildAddFilterList(options), cancellationToken);

    /// <summary>Adds a shared filter list and all of its filters as one compensated operation.</summary>
    public async Task AddFilterListWithFiltersAsync(
        IPSecurityFilterListCommandOptions options,
        IReadOnlyList<IPSecurityFilterCommandOptions> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(filters);

        IReadOnlyList<string> addListCommand = IPSecurityStaticPolicyCommandBuilder.BuildAddFilterList(options);
        IReadOnlyList<IReadOnlyList<string>> addFilterCommands = filters
            .Select(IPSecurityStaticPolicyCommandBuilder.BuildAddFilter)
            .ToList();

        await ExecuteAsync(addListCommand, cancellationToken);
        try
        {
            foreach (IReadOnlyList<string> command in addFilterCommands)
            {
                await ExecuteAsync(command, cancellationToken);
            }
        }
        catch (Exception mutationException)
        {
            try
            {
                await DeleteFilterListAsync(options.Name, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The filter-list creation and its compensation both failed.",
                    mutationException,
                    rollbackException);
            }

            throw;
        }
    }

    /// <summary>Updates a shared filter list.</summary>
    public Task SetFilterListAsync(IPSecurityFilterListCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildSetFilterList(options), cancellationToken);

    /// <summary>
    /// Updates a shared filter list and replaces its filters, restoring the before-image when possible.
    /// </summary>
    public async Task SetFilterListWithFiltersAsync(
        IPSecurityFilterListDefinition original,
        IPSecurityFilterListCommandOptions options,
        IReadOnlyList<IPSecurityFilterCommandOptions> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(filters);

        string targetName = options.NewName ?? original.Name;
        List<IPSecurityFilterCommandOptions> originalFilters = original.Filters
            .Select(ToFilterOptions)
            .ToList();
        List<IPSecurityFilterCommandOptions> targetFilters = filters
            .Select(filter => WithFilterListName(filter, targetName))
            .ToList();

        IReadOnlyList<IReadOnlyList<string>> deleteOriginalCommands = originalFilters
            .Select(IPSecurityStaticPolicyCommandBuilder.BuildDeleteFilter)
            .ToList();
        IReadOnlyList<string> setListCommand = IPSecurityStaticPolicyCommandBuilder.BuildSetFilterList(options);
        IReadOnlyList<IReadOnlyList<string>> addTargetCommands = targetFilters
            .Select(IPSecurityStaticPolicyCommandBuilder.BuildAddFilter)
            .ToList();

        try
        {
            foreach (IReadOnlyList<string> command in deleteOriginalCommands)
            {
                await ExecuteAsync(command, cancellationToken);
            }

            await ExecuteAsync(setListCommand, cancellationToken);
            foreach (IReadOnlyList<string> command in addTargetCommands)
            {
                await ExecuteAsync(command, cancellationToken);
            }
        }
        catch (Exception mutationException)
        {
            List<Exception> rollbackExceptions = [];
            await TryRollbackFilterListAsync(
                original,
                targetName,
                originalFilters,
                targetFilters,
                rollbackExceptions);

            if (rollbackExceptions.Count > 0)
            {
                throw new AggregateException(
                    "The filter-list update failed and one or more compensation operations also failed.",
                    [mutationException, .. rollbackExceptions]);
            }

            throw;
        }
    }

    /// <summary>Deletes a shared filter list.</summary>
    public Task DeleteFilterListAsync(string filterListName, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildDeleteFilterList(filterListName), cancellationToken);

    /// <summary>Adds a filter to a shared filter list.</summary>
    public Task AddFilterAsync(IPSecurityFilterCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildAddFilter(options), cancellationToken);

    /// <summary>Deletes an exact-match filter from a shared filter list.</summary>
    public Task DeleteFilterAsync(IPSecurityFilterCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildDeleteFilter(ToFilterIdentity(options)), cancellationToken);

    /// <summary>
    /// Replaces an exact-match filter and attempts to restore the original filter if the add operation fails.
    /// </summary>
    public async Task ReplaceFilterAsync(
        IPSecurityFilterCommandOptions original,
        IPSecurityFilterCommandOptions replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);

        await DeleteFilterAsync(original, cancellationToken);
        try
        {
            await AddFilterAsync(replacement, cancellationToken);
        }
        catch (Exception replacementException)
        {
            try
            {
                await AddFilterAsync(original, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The filter replacement and its compensation both failed.",
                    replacementException,
                    rollbackException);
            }

            throw;
        }
    }

    /// <summary>Adds a shared filter action.</summary>
    public Task AddFilterActionAsync(IPSecurityFilterActionCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildAddFilterAction(options), cancellationToken);

    /// <summary>Updates a shared filter action.</summary>
    public Task SetFilterActionAsync(IPSecurityFilterActionCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildSetFilterAction(options), cancellationToken);

    /// <summary>Deletes a shared filter action.</summary>
    public Task DeleteFilterActionAsync(string filterActionName, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildDeleteFilterAction(filterActionName), cancellationToken);

    /// <summary>Adds a rule to a policy.</summary>
    public Task AddRuleAsync(IPSecurityRuleCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildAddRule(options), cancellationToken);

    /// <summary>Updates a rule in a policy.</summary>
    public Task SetRuleAsync(IPSecurityRuleCommandOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildSetRule(options), cancellationToken);

    /// <summary>Deletes a named rule from a policy.</summary>
    public Task DeleteRuleAsync(
        string policyName,
        string ruleName,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(IPSecurityStaticPolicyCommandBuilder.BuildDeleteRule(policyName, ruleName), cancellationToken);

    private Task ExecuteAsync(IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        return _executor.ExecuteAsync(command, cancellationToken);
    }

    private async Task TryRollbackFilterListAsync(
        IPSecurityFilterListDefinition original,
        string targetName,
        IReadOnlyList<IPSecurityFilterCommandOptions> originalFilters,
        IReadOnlyList<IPSecurityFilterCommandOptions> targetFilters,
        ICollection<Exception> rollbackExceptions)
    {
        foreach (IPSecurityFilterCommandOptions filter in targetFilters)
        {
            try
            {
                await DeleteFilterAsync(filter, CancellationToken.None);
            }
            catch (Exception ex)
            {
                rollbackExceptions.Add(ex);
            }
        }

        if (!targetName.Equals(original.Name, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await SetFilterListAsync(new IPSecurityFilterListCommandOptions
                {
                    Name = targetName,
                    NewName = original.Name,
                    Description = original.Description
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                rollbackExceptions.Add(ex);
            }
        }
        else
        {
            try
            {
                await SetFilterListAsync(new IPSecurityFilterListCommandOptions
                {
                    Name = original.Name,
                    Description = original.Description
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                rollbackExceptions.Add(ex);
            }
        }

        foreach (IPSecurityFilterCommandOptions filter in originalFilters)
        {
            try
            {
                await AddFilterAsync(WithFilterListName(filter, original.Name), CancellationToken.None);
            }
            catch (Exception ex)
            {
                rollbackExceptions.Add(ex);
            }
        }
    }

    private static IPSecurityFilterCommandOptions ToFilterIdentity(IPSecurityFilterCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new IPSecurityFilterCommandOptions
        {
            FilterListName = options.FilterListName,
            SourceAddress = options.SourceAddress,
            DestinationAddress = options.DestinationAddress,
            Protocol = options.Protocol,
            IsMirrored = options.IsMirrored,
            SourceMask = options.SourceMask,
            DestinationMask = options.DestinationMask,
            SourcePort = options.SourcePort,
            DestinationPort = options.DestinationPort
        };
    }

    private static IPSecurityFilterCommandOptions ToFilterOptions(IPSecurityFilterDefinition filter)
    {
        return new IPSecurityFilterCommandOptions
        {
            FilterListName = filter.FilterListName,
            SourceAddress = filter.SourceAddress,
            DestinationAddress = filter.DestinationAddress,
            Description = EmptyToNull(filter.Description),
            Protocol = EmptyToNull(filter.Protocol),
            IsMirrored = filter.IsMirrored,
            SourceMask = EmptyToNull(filter.SourceMask),
            DestinationMask = EmptyToNull(filter.DestinationMask),
            SourcePort = filter.SourcePort == 0 ? null : filter.SourcePort,
            DestinationPort = filter.DestinationPort == 0 ? null : filter.DestinationPort
        };
    }

    private static IPSecurityFilterCommandOptions WithFilterListName(
        IPSecurityFilterCommandOptions filter,
        string filterListName)
    {
        return new IPSecurityFilterCommandOptions
        {
            FilterListName = filterListName,
            SourceAddress = filter.SourceAddress,
            DestinationAddress = filter.DestinationAddress,
            Description = filter.Description,
            Protocol = filter.Protocol,
            IsMirrored = filter.IsMirrored,
            SourceMask = filter.SourceMask,
            DestinationMask = filter.DestinationMask,
            SourcePort = filter.SourcePort,
            DestinationPort = filter.DestinationPort
        };
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
