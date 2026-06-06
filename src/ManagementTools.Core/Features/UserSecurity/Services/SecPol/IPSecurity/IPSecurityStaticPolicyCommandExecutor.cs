using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes validated mutation commands against the legacy static local IPsec policy store.
/// </summary>
/// <remarks>
/// The legacy policy store has no supported managed write API. This executor applies validated
/// policy-script lines through <c>polstore.dll</c> and never logs command arguments because those
/// values may contain pre-shared keys.
/// </remarks>
public sealed class IPSecurityStaticPolicyCommandExecutor
{
    private static readonly HashSet<string> AllowedVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "add", "set", "delete" };

    private static readonly HashSet<string> AllowedObjectKinds =
        new(StringComparer.OrdinalIgnoreCase) { "policy", "filterlist", "filter", "filteraction", "rule" };

    private readonly ILogger<IPSecurityStaticPolicyCommandExecutor> _logger;
    private readonly IPSecurityStaticPolicyNativeClient _nativeClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyCommandExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="nativeClient">The legacy IPsec policy native client.</param>
    public IPSecurityStaticPolicyCommandExecutor(
        ILogger<IPSecurityStaticPolicyCommandExecutor> logger,
        IPSecurityStaticPolicyNativeClient nativeClient)
    {
        _logger = logger;
        _nativeClient = nativeClient;
    }

    /// <summary>
    /// Executes a validated legacy static IPsec mutation command.
    /// </summary>
    /// <param name="arguments">
    /// Individual policy-script tokens beginning with <c>ipsec static</c> and followed by an
    /// <c>add</c>, <c>set</c>, or <c>delete</c> operation.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel the command.</param>
    /// <returns>A task that completes when the command succeeds.</returns>
    /// <exception cref="ArgumentException">The command is outside the allowed mutation surface.</exception>
    /// <exception cref="UnauthorizedAccessException">The local IPsec policy store cannot be opened.</exception>
    /// <exception cref="InvalidOperationException">The command fails.</exception>
    internal Task ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCommand(arguments);

        try
        {
            _nativeClient.ImportPolicyCommand(arguments);
        }
        catch (UnauthorizedAccessException)
        {
            LogFailure(arguments);
            throw;
        }
        catch (InvalidOperationException)
        {
            LogFailure(arguments);
            throw;
        }

        return Task.CompletedTask;
    }

    private static void ValidateCommand(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4
            || !arguments[0].Equals("ipsec", StringComparison.OrdinalIgnoreCase)
            || !arguments[1].Equals("static", StringComparison.OrdinalIgnoreCase)
            || !AllowedVerbs.Contains(arguments[2])
            || !AllowedObjectKinds.Contains(arguments[3]))
        {
            throw new ArgumentException(
                "Only legacy static IPsec add, set, and delete commands are allowed.",
                nameof(arguments));
        }

        if (arguments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("IPsec command tokens cannot be empty.", nameof(arguments));
        }
    }

    private void LogFailure(IReadOnlyList<string> arguments)
    {
        _logger.LogWarning(
            "The legacy IPsec static {Verb} {ObjectKind} command failed. Arguments and output were omitted because the command may contain policy secrets.",
            arguments[2],
            arguments[3]);
    }
}
