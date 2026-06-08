using System.Runtime.InteropServices;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.Native;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes validated mutation commands against the legacy static local IPsec policy store
/// using the native <c>polstore.dll</c> struct-based CRUD APIs.
/// </summary>
/// <remarks>
/// <para>
/// Filter list and filter action mutations with complete, verified struct layouts are performed
/// by calling <c>IPSecCreate*Data</c>, <c>IPSecSet*Data</c>, and <c>IPSecDelete*Data</c>
/// from <c>polstore.dll</c> with <c>IPSEC_REGISTRY_PROVIDER = 0</c>.
/// The struct memory layouts were verified against the Windows 11 build of polstore.dll by
/// enumerating objects created via <c>netsh ipsec static</c> and inspecting the in-memory
/// representation returned by the enum APIs.
/// </para>
/// <para>
/// Policy, filter, and rule mutations are applied via the native
/// <c>IPSecImportPolicies</c> API, which accepts the same script-line format used by
/// <c>netsh ipsec static</c>. No external processes are spawned.
/// </para>
/// <para>
/// Name-to-GUID resolution is achieved by enumerating all objects of a type and matching
/// the <c>pszIpsecName</c> field.
/// </para>
/// </remarks>
public sealed class IPSecurityStaticPolicyCommandExecutor
{
    private static readonly HashSet<string> AllowedVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "add", "set", "delete" };

    private static readonly HashSet<string> AllowedObjectKinds =
        new(StringComparer.OrdinalIgnoreCase) { "policy", "filterlist", "filter", "filteraction", "rule" };

    /// <summary>Well-known NegPol action GUID: Block.</summary>
    private static readonly Guid NegPolActionBlock = new("3f91a819-7647-11d1-864d-d46a00000000");

    /// <summary>Well-known NegPol action GUID: Permit.</summary>
    private static readonly Guid NegPolActionPermit = new("3f91a81c-7647-11d1-864d-d46a00000000");

    /// <summary>Well-known NegPol action GUID: Negotiate security.</summary>
    private static readonly Guid NegPolActionNegotiate = new("8a171dd3-77e3-11d1-8659-a04f00000000");

    /// <summary>Well-known NegPol type GUID: Default.</summary>
    private static readonly Guid NegPolTypeDefault = new("62f49e10-6c37-11d1-864c-14a300000000");

    /// <summary>Well-known NegPol type GUID: Negotiate.</summary>
    private static readonly Guid NegPolTypeNegotiate = new("62f49e13-6c37-11d1-864c-14a300000000");

    // Struct sizes (x64, verified by memory dumps)
    private const int FilterDataSize = 56;
    private const int NegPolDataSize = 88;

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
    /// Executes a validated legacy static IPsec mutation command via native struct APIs.
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

        string verb = arguments[2];
        string objectKind = arguments[3];
        if (RequiresImportPoliciesExecution(objectKind))
        {
            ExecuteViaImportPolicies(arguments);
            return Task.CompletedTask;
        }

        Dictionary<string, string> parameters = ParseParameters(arguments, startIndex: 4);

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr hStore, out int errorCode))
        {
            if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(errorCode) || errorCode == 5)
            {
                throw new UnauthorizedAccessException(
                    "The local IPsec policy store cannot be modified because the operation requires elevation.");
            }

            throw new InvalidOperationException(
                $"Failed to open the legacy IPsec policy store (native error 0x{errorCode:X8}).");
        }

        try
        {
            DispatchCommand(hStore, verb, objectKind, parameters);
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
            IPSecurityPolicyNativeMethods.CloseStore(hStore);
        }

        return Task.CompletedTask;
    }

    private void DispatchCommand(IntPtr hStore, string verb, string objectKind, Dictionary<string, string> parameters)
    {
        switch (objectKind.ToLowerInvariant())
        {
            case "filterlist":
                DispatchFilterList(hStore, verb, parameters);
                break;
            case "filteraction":
                DispatchFilterAction(hStore, verb, parameters);
                break;
            default:
                throw new ArgumentException($"Unsupported native object kind: {objectKind}");
        }
    }

    // ===== Filter List =====

    private void DispatchFilterList(IntPtr hStore, string verb, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");

        switch (verb.ToLowerInvariant())
        {
            case "add":
                AddFilterList(hStore, name, parameters);
                break;
            case "set":
                SetFilterList(hStore, name, parameters);
                break;
            case "delete":
                DeleteFilterList(hStore, name);
                break;
        }
    }

    private void AddFilterList(IntPtr hStore, string name, Dictionary<string, string> parameters)
    {
        Guid filterId = Guid.NewGuid();
        string? description = parameters.GetValueOrDefault("description");

        IntPtr pName = Marshal.StringToHGlobalUni(name);
        IntPtr pDesc = description is not null ? Marshal.StringToHGlobalUni(description) : IntPtr.Zero;
        IntPtr pData = Marshal.AllocHGlobal(FilterDataSize);
        try
        {
            ZeroMemory(pData, FilterDataSize);
            Marshal.Copy(filterId.ToByteArray(), 0, pData, 16);
            Marshal.WriteInt32(pData, 16, 0); // dwNumFilterSpecs
            Marshal.WriteIntPtr(pData, 24, IntPtr.Zero); // ppFilterSpecs
            Marshal.WriteInt32(pData, 32, GetUnixTimestamp());
            Marshal.WriteIntPtr(pData, 40, pName);
            Marshal.WriteIntPtr(pData, 48, pDesc);

            int hr = IPSecurityPolicyNativeMethods.CreateFilterData(hStore, pData);
            ThrowOnError(hr, "add filterlist");
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
            Marshal.FreeHGlobal(pName);
            if (pDesc != IntPtr.Zero) Marshal.FreeHGlobal(pDesc);
        }
    }

    private void SetFilterList(IntPtr hStore, string name, Dictionary<string, string> parameters)
    {
        (_, IntPtr filterPtr) = FindFilterByName(hStore, name);
        if (filterPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter list '{name}' not found.");
        }

        string? newName = parameters.GetValueOrDefault("newname");
        string? description = parameters.GetValueOrDefault("description");

        IntPtr pName = Marshal.StringToHGlobalUni(newName ?? name);
        IntPtr pDesc = description is not null ? Marshal.StringToHGlobalUni(description) : IntPtr.Zero;
        IntPtr pData = Marshal.AllocHGlobal(FilterDataSize);
        try
        {
            unsafe { Buffer.MemoryCopy((void*)filterPtr, (void*)pData, FilterDataSize, FilterDataSize); }
            Marshal.WriteInt32(pData, 32, GetUnixTimestamp());
            Marshal.WriteIntPtr(pData, 40, pName);
            if (pDesc != IntPtr.Zero)
            {
                Marshal.WriteIntPtr(pData, 48, pDesc);
            }

            int hr = IPSecurityPolicyNativeMethods.SetFilterData(hStore, pData);
            ThrowOnError(hr, "set filterlist");
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
            Marshal.FreeHGlobal(pName);
            if (pDesc != IntPtr.Zero) Marshal.FreeHGlobal(pDesc);
        }
    }

    private void DeleteFilterList(IntPtr hStore, string name)
    {
        (Guid filterId, _) = FindFilterByName(hStore, name);
        int hr = IPSecurityPolicyNativeMethods.DeleteFilterData(hStore, filterId);
        ThrowOnError(hr, "delete filterlist");
    }

    // ===== Filter Action =====

    private void DispatchFilterAction(IntPtr hStore, string verb, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");

        switch (verb.ToLowerInvariant())
        {
            case "add":
                AddFilterAction(hStore, name, parameters);
                break;
            case "set":
                SetFilterAction(hStore, name, parameters);
                break;
            case "delete":
                DeleteFilterAction(hStore, name);
                break;
        }
    }

    private void AddFilterAction(IntPtr hStore, string name, Dictionary<string, string> parameters)
    {
        Guid negPolId = Guid.NewGuid();
        string? description = parameters.GetValueOrDefault("description");
        string action = GetRequired(parameters, "action");

        Guid actionGuid = ResolveNegPolAction(action);
        Guid typeGuid = actionGuid == NegPolActionNegotiate ? NegPolTypeNegotiate : NegPolTypeDefault;

        IntPtr pName = Marshal.StringToHGlobalUni(name);
        IntPtr pDesc = description is not null ? Marshal.StringToHGlobalUni(description) : IntPtr.Zero;
        IntPtr pData = Marshal.AllocHGlobal(NegPolDataSize);
        try
        {
            ZeroMemory(pData, NegPolDataSize);
            Marshal.Copy(negPolId.ToByteArray(), 0, pData, 16);
            Marshal.Copy(actionGuid.ToByteArray(), 0, pData + 16, 16);
            Marshal.Copy(typeGuid.ToByteArray(), 0, pData + 32, 16);
            Marshal.WriteInt32(pData, 48, 0); // dwSecurityMethodCount
            Marshal.WriteIntPtr(pData, 56, IntPtr.Zero); // pIpsecSecurityMethods
            Marshal.WriteInt32(pData, 64, GetUnixTimestamp());
            Marshal.WriteIntPtr(pData, 72, pName);
            Marshal.WriteIntPtr(pData, 80, pDesc);

            int hr = IPSecurityPolicyNativeMethods.CreateNegPolData(hStore, pData);
            ThrowOnError(hr, "add filteraction");
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
            Marshal.FreeHGlobal(pName);
            if (pDesc != IntPtr.Zero) Marshal.FreeHGlobal(pDesc);
        }
    }

    private void SetFilterAction(IntPtr hStore, string name, Dictionary<string, string> parameters)
    {
        (_, IntPtr negPolPtr) = FindNegPolByName(hStore, name);
        if (negPolPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter action '{name}' not found.");
        }

        string? newName = parameters.GetValueOrDefault("newname");
        string? description = parameters.GetValueOrDefault("description");
        string? action = parameters.GetValueOrDefault("action");

        IntPtr pName = Marshal.StringToHGlobalUni(newName ?? name);
        IntPtr pDesc = description is not null ? Marshal.StringToHGlobalUni(description) : IntPtr.Zero;
        IntPtr pData = Marshal.AllocHGlobal(NegPolDataSize);
        try
        {
            unsafe { Buffer.MemoryCopy((void*)negPolPtr, (void*)pData, NegPolDataSize, NegPolDataSize); }

            if (action is not null)
            {
                Guid actionGuid = ResolveNegPolAction(action);
                Guid typeGuid = actionGuid == NegPolActionNegotiate ? NegPolTypeNegotiate : NegPolTypeDefault;
                Marshal.Copy(actionGuid.ToByteArray(), 0, pData + 16, 16);
                Marshal.Copy(typeGuid.ToByteArray(), 0, pData + 32, 16);
            }

            Marshal.WriteInt32(pData, 64, GetUnixTimestamp());
            Marshal.WriteIntPtr(pData, 72, pName);
            if (pDesc != IntPtr.Zero)
            {
                Marshal.WriteIntPtr(pData, 80, pDesc);
            }

            int hr = IPSecurityPolicyNativeMethods.SetNegPolData(hStore, pData);
            ThrowOnError(hr, "set filteraction");
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
            Marshal.FreeHGlobal(pName);
            if (pDesc != IntPtr.Zero) Marshal.FreeHGlobal(pDesc);
        }
    }

    private void DeleteFilterAction(IntPtr hStore, string name)
    {
        (Guid negPolId, _) = FindNegPolByName(hStore, name);
        int hr = IPSecurityPolicyNativeMethods.DeleteNegPolData(hStore, negPolId);
        ThrowOnError(hr, "delete filteraction");
    }

    // ===== Name Resolution via Enum =====

    private static (Guid id, IntPtr ptr) FindFilterByName(IntPtr hStore, string name)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(4);
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumFilterData(hStore, ppp, pCount);
            if (hr != 0) return (Guid.Empty, IntPtr.Zero);

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            if (count == 0 || pp == IntPtr.Zero) return (Guid.Empty, IntPtr.Zero);

            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;
                IntPtr pName = Marshal.ReadIntPtr(p, 40); // FilterData.pszIpsecName offset
                if (pName == IntPtr.Zero) continue;
                string? filterName = Marshal.PtrToStringUni(pName);
                if (name.Equals(filterName, StringComparison.OrdinalIgnoreCase))
                {
                    Guid id = Marshal.PtrToStructure<Guid>(p);
                    return (id, p);
                }
            }

            throw new InvalidOperationException($"Filter list '{name}' not found in the local IPsec store.");
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static (Guid id, IntPtr ptr) FindNegPolByName(IntPtr hStore, string name)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(4);
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumNegPolData(hStore, ppp, pCount);
            if (hr != 0) return (Guid.Empty, IntPtr.Zero);

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            if (count == 0 || pp == IntPtr.Zero) return (Guid.Empty, IntPtr.Zero);

            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;
                IntPtr pName = Marshal.ReadIntPtr(p, 72); // NegPolData.pszIpsecName offset
                if (pName == IntPtr.Zero) continue;
                string? negPolName = Marshal.PtrToStringUni(pName);
                if (name.Equals(negPolName, StringComparison.OrdinalIgnoreCase))
                {
                    Guid id = Marshal.PtrToStructure<Guid>(p);
                    return (id, p);
                }
            }

            throw new InvalidOperationException($"Filter action '{name}' not found in the local IPsec store.");
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    // ===== Helpers =====

    private static bool RequiresImportPoliciesExecution(string objectKind)
    {
        return objectKind.Equals("policy", StringComparison.OrdinalIgnoreCase)
            || objectKind.Equals("filter", StringComparison.OrdinalIgnoreCase)
            || objectKind.Equals("rule", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies a policy/filter/rule mutation via the native <c>IPSecImportPolicies</c> API
    /// in <c>polstore.dll</c>, which accepts the same script-line format that
    /// <c>netsh ipsec static</c> uses internally.
    /// </summary>
    private void ExecuteViaImportPolicies(IReadOnlyList<string> arguments)
    {
        string scriptLine = IPSecurityStaticPolicyNativeClient.BuildPolicyScriptLine(arguments);

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr hStore, out int openError))
        {
            if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(openError) || openError == 5)
            {
                throw new UnauthorizedAccessException(
                    "The local IPsec policy store cannot be modified because the operation requires elevation.");
            }

            throw new InvalidOperationException(
                $"Failed to open the legacy IPsec policy store (native error 0x{openError:X8}).");
        }

        try
        {
            int hr = IPSecurityPolicyNativeMethods.ImportPolicies(hStore, scriptLine);
            ThrowOnError(hr, $"{arguments[2]} {arguments[3]}");
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
            IPSecurityPolicyNativeMethods.CloseStore(hStore);
        }
    }

    private static Guid ResolveNegPolAction(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "block" => NegPolActionBlock,
            "permit" => NegPolActionPermit,
            "negotiate" => NegPolActionNegotiate,
            _ => throw new ArgumentException($"Unsupported filter action: {action}")
        };
    }

    private static Dictionary<string, string> ParseParameters(IReadOnlyList<string> arguments, int startIndex)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = startIndex; i < arguments.Count; i++)
        {
            string arg = arguments[i];
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                result[arg[..eq]] = arg[(eq + 1)..];
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

    private static int GetUnixTimestamp()
    {
        return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static string DumpHex(IntPtr ptr, int length)
    {
        byte[] buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);
        var sb = new System.Text.StringBuilder(length * 4);
        for (int row = 0; row < length; row += 16)
        {
            sb.Append($"+{row,3:D3}  ");
            int end = Math.Min(row + 16, length);
            for (int col = row; col < end; col++)
            {
                sb.Append($"{buffer[col]:X2} ");
                if (col == row + 7) sb.Append(' ');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void ZeroMemory(IntPtr ptr, int size)
    {
        for (int i = 0; i < size; i++)
        {
            Marshal.WriteByte(ptr, i, 0);
        }
    }

    private static void ThrowOnError(int hr, string operation)
    {
        if (hr == 0) return;

        if (hr == 5 || hr == unchecked((int)0x80070005))
        {
            throw new UnauthorizedAccessException(
                $"The local IPsec policy store cannot be modified because the operation requires elevation.");
        }

        throw new InvalidOperationException(
            $"The legacy IPsec policy {operation} command failed with native error 0x{hr:X8}.");
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
