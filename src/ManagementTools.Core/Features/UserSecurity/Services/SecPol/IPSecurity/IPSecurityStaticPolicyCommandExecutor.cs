using System.Runtime.InteropServices;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.Native;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes validated mutation commands against the legacy static local IPsec policy store
/// using the native <c>polstore.dll</c> policy import API.
/// </summary>
/// <remarks>
/// The executor uses only native <c>polstore.dll</c> APIs. Most mutations go through
/// <c>IPSecImportPolicies</c>; policy deletion uses the direct delete-by-GUID APIs because
/// some stores reject the equivalent script form with <c>ERROR_INVALID_PARAMETER</c>.
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
    /// Executes a validated legacy static IPsec mutation command via the native policy import API.
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

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr policyStore, out int errorCode))
        {
            ThrowOpenStoreFailure(errorCode);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ExecuteCore(policyStore, arguments);
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
        finally
        {
            IPSecurityPolicyNativeMethods.CloseStore(policyStore);
        }

        return Task.CompletedTask;
    }

    private static void ExecuteCore(IntPtr policyStore, IReadOnlyList<string> arguments)
    {
        if (arguments[2].Equals("delete", StringComparison.OrdinalIgnoreCase)
            && arguments[3].Equals("policy", StringComparison.OrdinalIgnoreCase))
        {
            DeletePolicy(policyStore, ParseParameters(arguments, startIndex: 4));
            return;
        }

        string scriptLine = IPSecurityStaticPolicyNativeClient.BuildPolicyScriptLine(arguments);
        int hr = IPSecurityPolicyNativeMethods.ImportPolicies(policyStore, scriptLine);
        ThrowOnError(hr, $"{arguments[2]} {arguments[3]}");
    }

    private static void ThrowOpenStoreFailure(int errorCode)
    {
        if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(errorCode) || errorCode == 5)
        {
            throw new UnauthorizedAccessException(
                "The local IPsec policy store cannot be modified because the operation requires elevation.");
        }

        throw new InvalidOperationException(
            $"Failed to open the legacy IPsec policy store (native error 0x{errorCode:X8}).");
    }

    private static void ThrowOnError(int hr, string operation)
    {
        if (hr == 0)
        {
            return;
        }

        if (hr == 5 || hr == unchecked((int)0x80070005))
        {
            throw new UnauthorizedAccessException(
                "The local IPsec policy store cannot be modified because the operation requires elevation.");
        }

        throw new InvalidOperationException(
            $"The legacy IPsec policy {operation} command failed with native error 0x{hr:X8}.");
    }

    private static void DeletePolicy(IntPtr policyStore, Dictionary<string, string> parameters)
    {
        string policyName = GetRequired(parameters, "name");
        (Guid policyId, _) = FindPolicyByName(policyStore, policyName);
        if (policyId == Guid.Empty)
        {
            return;
        }

        IgnoreUnassignFailure(policyStore, policyId);
        DeleteAllRules(policyStore, policyId);
        ThrowOnError(IPSecurityPolicyNativeMethods.DeletePolicyData(policyStore, policyId), "delete policy");
    }

    private static void DeleteAllRules(IntPtr policyStore, Guid policyId)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumNFAData(policyStore, policyId, ppp, pCount);
            if (hr != 0)
            {
                ThrowOnError(hr, "enumerate policy rules");
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int index = 0; index < count; index++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * index);
                if (p == IntPtr.Zero)
                {
                    continue;
                }

                Guid nfaId = ReadGuid(p, 8);
                ThrowOnError(IPSecurityPolicyNativeMethods.DeleteNFAData(policyStore, policyId, nfaId), "delete rule");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static void IgnoreUnassignFailure(IntPtr policyStore, Guid policyId)
    {
        int hr = IPSecurityPolicyNativeMethods.UnassignPolicy(policyStore, policyId);
        if (hr == 0)
        {
            return;
        }

        if (hr == 5 || hr == unchecked((int)0x80070005))
        {
            ThrowOnError(hr, "unassign policy");
        }
    }

    private static (Guid Id, IntPtr Ptr) FindPolicyByName(IntPtr policyStore, string name)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumPolicyData(policyStore, ppp, pCount);
            if (hr != 0)
            {
                return (Guid.Empty, IntPtr.Zero);
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int index = 0; index < count; index++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * index);
                if (p == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr pName = Marshal.ReadIntPtr(p, 48);
                string? policyName = pName != IntPtr.Zero ? Marshal.PtrToStringUni(pName) : null;
                if (name.Equals(policyName, StringComparison.OrdinalIgnoreCase))
                {
                    return (ReadGuid(p, 0), p);
                }
            }

            return (Guid.Empty, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static Dictionary<string, string> ParseParameters(IReadOnlyList<string> arguments, int startIndex)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        for (int index = startIndex; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            int equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0)
            {
                result[argument[..equalsIndex]] = argument[(equalsIndex + 1)..];
            }
        }

        return result;
    }

    private static string GetRequired(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out string? value) || string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        }

        return value;
    }

    private static Guid ReadGuid(IntPtr structPtr, int offset)
    {
        byte[] bytes = new byte[16];
        Marshal.Copy(structPtr + offset, bytes, 0, 16);
        return new Guid(bytes);
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
