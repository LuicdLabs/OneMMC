using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes validated mutation commands against the legacy static local IPsec policy store.
/// </summary>
/// <remarks>
/// The legacy policy store has no supported managed write API. This executor is intentionally
/// limited to the documented <c>netsh ipsec static add/set/delete</c> surface and never logs
/// command arguments or command output because those values may contain pre-shared keys.
/// </remarks>
public sealed class IPSecurityStaticPolicyCommandExecutor
{
    private const string NetshExecutableName = "netsh.exe";
    private const string ErrorPrefix = "ERR IPsec[";
    private const string StoreOpenError = "ERR IPsec[05073]";

    private static readonly HashSet<string> AllowedVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "add", "set", "delete" };

    private static readonly HashSet<string> AllowedObjectKinds =
        new(StringComparer.OrdinalIgnoreCase) { "policy", "filterlist", "filter", "filteraction", "rule" };

    private readonly ILogger<IPSecurityStaticPolicyCommandExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyCommandExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public IPSecurityStaticPolicyCommandExecutor(ILogger<IPSecurityStaticPolicyCommandExecutor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes a validated legacy static IPsec mutation command.
    /// </summary>
    /// <param name="arguments">
    /// Individual <c>netsh</c> argument tokens beginning with <c>ipsec static</c> and followed
    /// by an <c>add</c>, <c>set</c>, or <c>delete</c> operation.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel the command.</param>
    /// <returns>A task that completes when the command succeeds.</returns>
    /// <exception cref="ArgumentException">The command is outside the allowed mutation surface.</exception>
    /// <exception cref="UnauthorizedAccessException">The local IPsec policy store cannot be opened.</exception>
    /// <exception cref="InvalidOperationException">The command fails.</exception>
    internal async Task ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = NetshExecutableName,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Windows IPsec policy command could not be started.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask);

        string output = $"{standardOutputTask.Result}\n{standardErrorTask.Result}";
        if (output.Contains(StoreOpenError, StringComparison.OrdinalIgnoreCase))
        {
            LogFailure(arguments, process.ExitCode);
            throw new UnauthorizedAccessException("The local IPsec policy store could not be opened.");
        }

        if (process.ExitCode != 0 || output.Contains(ErrorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            LogFailure(arguments, process.ExitCode);
            throw new InvalidOperationException("The Windows IPsec policy command failed.");
        }
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

    private void LogFailure(IReadOnlyList<string> arguments, int exitCode)
    {
        _logger.LogWarning(
            "The legacy IPsec static {Verb} {ObjectKind} command failed with exit code {ExitCode}. Arguments and output were omitted because the command may contain policy secrets.",
            arguments[2],
            arguments[3],
            exitCode);
    }
}
