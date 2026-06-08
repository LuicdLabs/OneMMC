using System.ComponentModel;
using System.Text;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.Native;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes read and write operations against the legacy static IPsec policy store through
/// <c>polstore.dll</c>.
/// </summary>
public sealed class IPSecurityStaticPolicyNativeClient
{
    /// <summary>
    /// Exports the local legacy IPsec policy store as a netsh-compatible policy script.
    /// </summary>
    /// <param name="errorCode">The native error code when the export fails.</param>
    /// <returns>The exported script, or <see langword="null"/> when the store cannot be read.</returns>
    internal string? TryExportPolicyScript(out int errorCode)
    {
        errorCode = 0;

        if (!IPSecurityPolicyNativeMethods.IsAvailable)
        {
            errorCode = unchecked((int)0x8007007E);
            return null;
        }

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr policyStore, out errorCode))
        {
            return null;
        }

        try
        {
            return IPSecurityPolicyNativeMethods.TryExportPolicies(policyStore, out errorCode);
        }
        finally
        {
            IPSecurityPolicyNativeMethods.CloseStore(policyStore);
        }
    }

    /// <summary>
    /// Applies one validated legacy static IPsec policy script line.
    /// </summary>
    /// <param name="arguments">
    /// Individual policy-script tokens beginning with <c>ipsec static</c> and followed by an
    /// <c>add</c>, <c>set</c>, or <c>delete</c> operation.
    /// </param>
    /// <exception cref="UnauthorizedAccessException">The local IPsec policy store cannot be opened.</exception>
    /// <exception cref="InvalidOperationException">The policy script line fails to apply.</exception>
    internal void ImportPolicyCommand(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!IPSecurityPolicyNativeMethods.IsAvailable)
        {
            throw new PlatformNotSupportedException(
                "This version of Windows does not expose the legacy IPsec policy store APIs.");
        }

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr policyStore, out int openError))
        {
            throw MapOpenFailure(openError);
        }

        try
        {
            string scriptLine = BuildPolicyScriptLine(arguments);
            int importError = IPSecurityPolicyNativeMethods.ImportPolicies(policyStore, scriptLine);
            if (importError != 0)
            {
                throw MapImportFailure(importError);
            }
        }
        finally
        {
            IPSecurityPolicyNativeMethods.CloseStore(policyStore);
        }
    }

    internal static string BuildPolicyScriptLine(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 4)
        {
            throw new ArgumentException("A legacy static IPsec command is incomplete.", nameof(arguments));
        }

        StringBuilder scriptLine = new();
        scriptLine.Append(arguments[2]);
        scriptLine.Append(' ');
        scriptLine.Append(arguments[3]);

        for (int index = 4; index < arguments.Count; index++)
        {
            scriptLine.Append(' ');
            scriptLine.Append(FormatScriptToken(arguments[index]));
        }

        return scriptLine.ToString();
    }

    private static string FormatScriptToken(string token)
    {
        int equalsIndex = token.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return token;
        }

        string key = token[..equalsIndex];
        string value = token[(equalsIndex + 1)..];
        if (!value.Any(static character => char.IsWhiteSpace(character) || character == '"'))
        {
            return token;
        }

        string escapedValue = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"{key}=\"{escapedValue}\"";
    }

    private static Exception MapOpenFailure(int errorCode)
    {
        if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(errorCode))
        {
            return new UnauthorizedAccessException("The local IPsec policy store could not be opened.");
        }

        return new Win32Exception(errorCode);
    }

    private static Exception MapImportFailure(int errorCode)
    {
        if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(errorCode))
        {
            return new UnauthorizedAccessException("The local IPsec policy store could not be opened.");
        }

        return new InvalidOperationException("The legacy IPsec policy script line could not be applied.");
    }
}
