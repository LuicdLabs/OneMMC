using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes validated mutation commands against the legacy static local IPsec policy store.
/// </summary>
/// <remarks>
/// The legacy policy store has no supported managed write API, and the native <c>polstore.dll</c>
/// <c>IPSecOpenPolicyStore</c> function fails with <c>ERROR_INVALID_DATA</c> (13) on modern Windows
/// builds for the local registry store type. This executor applies validated policy-script lines
/// by shelling out to the official <c>netsh.exe</c> tool, avoiding the broken native API while
/// adhering to the requirement to use the operating system's exact execution path.
/// </remarks>
public sealed class IPSecurityStaticPolicyCommandExecutor
{
    private static readonly HashSet<string> AllowedVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "add", "set", "delete" };

    private static readonly HashSet<string> AllowedObjectKinds =
        new(StringComparer.OrdinalIgnoreCase) { "policy", "filterlist", "filter", "filteraction", "rule" };

    private readonly ILogger<IPSecurityStaticPolicyCommandExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyCommandExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public IPSecurityStaticPolicyCommandExecutor(
        ILogger<IPSecurityStaticPolicyCommandExecutor> logger)
    {
        _logger = logger;
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
    internal async Task ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCommand(arguments);

        try
        {
            using Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            foreach (string arg in arguments)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken);
                string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);

                if (error.Contains("elevation", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("elevation", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    process.ExitCode == 5)
                {
                    throw new UnauthorizedAccessException("The local IPsec policy store cannot be modified because the operation requires elevation.");
                }

                throw new InvalidOperationException($"The legacy IPsec policy command failed with exit code {process.ExitCode}.");
            }
        }
        catch (Win32Exception ex)
        {
            LogFailure(arguments);
            throw new InvalidOperationException("Failed to launch netsh.exe.", ex);
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
