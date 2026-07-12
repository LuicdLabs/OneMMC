using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using OneMMC.Core.Features.PCManagement.Models.FsMgmt;
using OneMMC.Core.Infrastructure.WindowsCapabilities;
using Microsoft.Extensions.Logging;
using Windows.Win32.Foundation;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.FsMgmt;

/// <summary>
/// Provides Shared Folders operations by calling the native SMB Server
/// network-management APIs used by fsmgmt.msc.
/// </summary>
public sealed class SharedFoldersService
{
    private const uint NerrSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint NerrClientNameNotFound = 2312;
    private const uint MaxPreferredLength = 0xFFFFFFFF;

    private const uint StypeDiskTree = 0x00000000;
    private const uint StypePrintQueue = 0x00000001;
    private const uint StypeDevice = 0x00000002;
    private const uint StypeIpc = 0x00000003;
    private const uint StypeSpecial = 0x80000000;
    private const uint StypeTypeMask = 0x000000FF;

    private const uint ShareInfoLevel502 = 502;
    private const uint ShareInfoLevel1004 = 1004;
    private const uint ShareInfoLevel1005 = 1005;
    private const uint ShareInfoLevel1006 = 1006;
    private const uint ShareInfoLevel1501 = 1501;
    private const uint SessionInfoLevel502 = 502;
    private const uint FileInfoLevel3 = 3;
    private const uint SessionGuestFlag = 0x00000001;

    private const uint CscMask = 0x0030;
    private const uint CscCacheManualReint = 0x0000;
    private const uint CscCacheAutoReint = 0x0010;
    private const uint CscCacheVdo = 0x0020;
    private const uint CscCacheNone = 0x0030;

    private readonly ILogger<SharedFoldersService> _logger;
    private int _lastLoggedResolvedCount = -1;
    private int _lastLoggedSessionCount = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedFoldersService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public SharedFoldersService(ILogger<SharedFoldersService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enumerates SMB shares on the local computer.
    /// </summary>
    /// <returns>The shares returned by the Server service.</returns>
    public Task<IReadOnlyList<SharedFolderShare>> GetSharesAsync() =>
        Task.Run<IReadOnlyList<SharedFolderShare>>(EnumerateShares);

    /// <summary>
    /// Enumerates SMB sessions on the local computer, resolving each client's raw transport address
    /// to a friendly computer name where possible.
    /// </summary>
    /// <returns>The active SMB sessions.</returns>
    public async Task<IReadOnlyList<SharedFolderSession>> GetSessionsAsync()
    {
        IReadOnlyList<SharedFolderSession> sessions =
            await Task.Run<IReadOnlyList<SharedFolderSession>>(EnumerateSessions).ConfigureAwait(false);
        await ResolveClientNamesAsync(sessions).ConfigureAwait(false);
        return sessions;
    }

    /// <summary>
    /// Enumerates files opened remotely through SMB.
    /// </summary>
    /// <returns>The open remote resources.</returns>
    public Task<IReadOnlyList<SharedFolderOpenFile>> GetOpenFilesAsync() =>
        Task.Run<IReadOnlyList<SharedFolderOpenFile>>(EnumerateOpenFiles);

    /// <summary>
    /// Creates a new disk-tree SMB share.
    /// </summary>
    /// <param name="definition">The share definition.</param>
    public Task CreateShareAsync(SharedFolderShareDefinition definition) =>
        Task.Run(() => CreateShare(definition));

    /// <summary>
    /// Updates mutable settings for an existing SMB share.
    /// </summary>
    /// <param name="shareName">The existing share name.</param>
    /// <param name="definition">The updated settings.</param>
    public Task UpdateShareAsync(string shareName, SharedFolderShareDefinition definition) =>
        Task.Run(() => UpdateShare(shareName, definition));

    /// <summary>
    /// Deletes an SMB share.
    /// </summary>
    /// <param name="shareName">The share name.</param>
    public Task DeleteShareAsync(string shareName) =>
        Task.Run(() => DeleteShare(shareName));

    /// <summary>
    /// Disconnects one SMB session.
    /// </summary>
    /// <param name="session">The session to disconnect.</param>
    public Task DisconnectSessionAsync(SharedFolderSession session) =>
        Task.Run(() => DisconnectSession(session.ClientName, session.UserName));

    /// <summary>
    /// Disconnects all SMB sessions.
    /// </summary>
    public Task DisconnectAllSessionsAsync() =>
        Task.Run(() => DisconnectSession(clientName: null, userName: null));

    /// <summary>
    /// Closes one open SMB file.
    /// </summary>
    /// <param name="fileId">The server-assigned file identifier.</param>
    public Task CloseOpenFileAsync(uint fileId) =>
        Task.Run(() => CloseOpenFile(fileId));

    /// <summary>
    /// Closes all currently enumerated open SMB files.
    /// </summary>
    public async Task CloseAllOpenFilesAsync()
    {
        IReadOnlyList<SharedFolderOpenFile> files = await GetOpenFilesAsync();
        foreach (SharedFolderOpenFile file in files)
        {
            CloseOpenFile(file.Id);
        }
    }

    private unsafe IReadOnlyList<SharedFolderShare> EnumerateShares()
    {
        var result = new List<SharedFolderShare>();
        uint resumeHandle = 0;
        uint status;

        do
        {
            byte* buffer = null;
            status = Win32PInvoke.NetShareEnum(
                servername: default,
                ShareInfoLevel502,
                out buffer,
                MaxPreferredLength,
                out uint entriesRead,
                out _,
                ref resumeHandle);

            try
            {
                if (status != NerrSuccess && status != ErrorMoreData)
                {
                    ThrowNativeError(status, "NetShareEnum");
                }

                int entrySize = Marshal.SizeOf<ShareInfo502>();
                for (uint index = 0; index < entriesRead; index++)
                {
                    var info = Marshal.PtrToStructure<ShareInfo502>((IntPtr)(buffer + (index * entrySize)));
                    string name = ReadString(info.NetName);
                    ShareOfflineSetting offlineSetting = GetShareOfflineSetting(name);

                    result.Add(new SharedFolderShare
                    {
                        Name = name,
                        Path = ReadString(info.Path),
                        Description = ReadString(info.Remark),
                        Type = GetShareType(info.Type),
                        CurrentUses = info.CurrentUses,
                        MaxUses = info.MaxUses,
                        IsAdministrative = IsAdministrativeShare(name, info.Type),
                        OfflineSetting = offlineSetting,
                        SecurityDescriptorSddl = ReadSecurityDescriptorSddl(info.SecurityDescriptor)
                    });
                }
            }
            finally
            {
                FreeNetApiBuffer(buffer);
            }
        }
        while (status == ErrorMoreData);

        return result
            .OrderByDescending(share => !share.IsAdministrative)
            .ThenBy(share => share.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private unsafe IReadOnlyList<SharedFolderSession> EnumerateSessions()
    {
        var result = new List<SharedFolderSession>();
        uint resumeHandle = 0;
        uint status;

        do
        {
            byte* buffer = null;
            status = Win32PInvoke.NetSessionEnum(
                servername: default,
                UncClientName: default,
                username: default,
                SessionInfoLevel502,
                out buffer,
                MaxPreferredLength,
                out uint entriesRead,
                out _,
                ref resumeHandle);

            try
            {
                if (status != NerrSuccess && status != ErrorMoreData)
                {
                    ThrowNativeError(status, "NetSessionEnum");
                }

                int entrySize = Marshal.SizeOf<SessionInfo502>();
                for (uint index = 0; index < entriesRead; index++)
                {
                    var info = Marshal.PtrToStructure<SessionInfo502>((IntPtr)(buffer + (index * entrySize)));
                    result.Add(new SharedFolderSession
                    {
                        ClientName = ReadString(info.ClientName),
                        UserName = ReadString(info.UserName),
                        OpenCount = info.OpenCount,
                        ActiveTime = TimeSpan.FromSeconds(info.ActiveTimeSeconds),
                        IdleTime = TimeSpan.FromSeconds(info.IdleTimeSeconds),
                        IsGuest = (info.UserFlags & SessionGuestFlag) != 0,
                        ClientType = ReadString(info.ClientTypeName),
                        Transport = ReadString(info.Transport)
                    });
                }
            }
            finally
            {
                FreeNetApiBuffer(buffer);
            }
        }
        while (status == ErrorMoreData);

        return result
            .OrderBy(session => session.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Fills in <see cref="SharedFolderSession.ResolvedClientName"/> for each session by resolving
    /// the raw client address reported by the Server service to a friendly computer name. Failures
    /// are non-fatal: the sessions are still returned with their raw client addresses.
    /// </summary>
    private async Task ResolveClientNamesAsync(IReadOnlyList<SharedFolderSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        string[] clientNames = sessions
            .Select(session => session.ClientName)
            .Where(clientName => !string.IsNullOrWhiteSpace(clientName))
            .ToArray();

        if (clientNames.Length == 0)
        {
            return;
        }

        try
        {
            IReadOnlyDictionary<string, string> resolved =
                await SmbClientNameResolver.ResolveAsync(clientNames).ConfigureAwait(false);

            int resolvedCount = 0;
            foreach (SharedFolderSession session in sessions)
            {
                if (!string.IsNullOrWhiteSpace(session.ClientName)
                    && resolved.TryGetValue(session.ClientName, out string? name)
                    && !string.IsNullOrWhiteSpace(name))
                {
                    session.ResolvedClientName = name;
                    resolvedCount++;
                }
            }

            // Live monitoring resolves names every second; only log when the outcome changes so the
            // debug log is not flooded with identical steady-state lines.
            if (resolvedCount != _lastLoggedResolvedCount || sessions.Count != _lastLoggedSessionCount)
            {
                _lastLoggedResolvedCount = resolvedCount;
                _lastLoggedSessionCount = sessions.Count;
                _logger.LogDebug(
                    "Resolved {ResolvedCount} of {SessionCount} SMB session client names.",
                    resolvedCount,
                    sessions.Count);
            }
        }
        catch (Exception ex)
        {
            // Name resolution only enriches the display; never let it hide the sessions themselves.
            _logger.LogDebug(ex, "Resolving SMB session client names failed; showing raw client addresses.");
        }
    }

    private unsafe IReadOnlyList<SharedFolderOpenFile> EnumerateOpenFiles()
    {
        var result = new List<SharedFolderOpenFile>();
        nuint resumeHandle = 0;
        uint status;

        do
        {
            byte* buffer = null;
            status = Win32PInvoke.NetFileEnum(
                servername: default,
                basepath: default,
                username: default,
                FileInfoLevel3,
                out buffer,
                MaxPreferredLength,
                out uint entriesRead,
                out _,
                ref resumeHandle);

            try
            {
                if (status != NerrSuccess && status != ErrorMoreData)
                {
                    ThrowNativeError(status, "NetFileEnum");
                }

                int entrySize = Marshal.SizeOf<FileInfo3>();
                for (uint index = 0; index < entriesRead; index++)
                {
                    var info = Marshal.PtrToStructure<FileInfo3>((IntPtr)(buffer + (index * entrySize)));
                    result.Add(new SharedFolderOpenFile
                    {
                        Id = info.Id,
                        Permissions = info.Permissions,
                        LockCount = info.LockCount,
                        Path = ReadString(info.PathName),
                        UserName = ReadString(info.UserName)
                    });
                }
            }
            finally
            {
                FreeNetApiBuffer(buffer);
            }
        }
        while (status == ErrorMoreData);

        return result
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private unsafe void CreateShare(SharedFolderShareDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateShareDefinition(definition, requirePath: true);

        IntPtr netName = IntPtr.Zero;
        IntPtr remark = IntPtr.Zero;
        IntPtr path = IntPtr.Zero;
        IntPtr securityDescriptor = IntPtr.Zero;

        try
        {
            netName = Marshal.StringToHGlobalUni(definition.Name);
            remark = Marshal.StringToHGlobalUni(definition.Description ?? string.Empty);
            path = Marshal.StringToHGlobalUni(definition.Path);
            securityDescriptor = AllocateSecurityDescriptor(definition.SecurityDescriptorSddl);

            var info = new ShareInfo502
            {
                NetName = netName,
                Type = StypeDiskTree,
                Remark = remark,
                Permissions = 0,
                MaxUses = definition.MaxUses,
                CurrentUses = 0,
                Path = path,
                Password = IntPtr.Zero,
                Reserved = 0,
                SecurityDescriptor = securityDescriptor
            };

            uint parmError;
            uint status = Win32PInvoke.NetShareAdd(default, ShareInfoLevel502, (byte*)&info, &parmError);
            if (status != NerrSuccess)
            {
                ThrowNativeError(status, $"NetShareAdd({definition.Name})");
            }

            SetShareOfflineSetting(definition.Name, definition.OfflineSetting);
        }
        finally
        {
            FreeHGlobal(netName);
            FreeHGlobal(remark);
            FreeHGlobal(path);
            FreeHGlobal(securityDescriptor);
        }
    }

    private unsafe void UpdateShare(string shareName, SharedFolderShareDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareName);
        ArgumentNullException.ThrowIfNull(definition);

        SetShareRemark(shareName, definition.Description ?? string.Empty);
        SetShareMaxUses(shareName, definition.MaxUses);
        SetShareSecurityDescriptor(shareName, definition.SecurityDescriptorSddl);
        SetShareOfflineSetting(shareName, definition.OfflineSetting);
    }

    private unsafe void DeleteShare(string shareName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareName);

        fixed (char* shareNamePointer = shareName)
        {
            uint status = Win32PInvoke.NetShareDel(default, new PWSTR(shareNamePointer));
            if (status != NerrSuccess)
            {
                ThrowNativeError(status, $"NetShareDel({shareName})");
            }
        }
    }

    private void DisconnectSession(string? clientName, string? userName)
    {
        uint status = InvokeNetSessionDel(clientName, userName);
        if (status == NerrSuccess)
        {
            return;
        }

        // NetSessionDel reports NERR_ClientNameNotFound (2312) when no session matches the supplied
        // client name. That is common here because the client name is frequently a transport
        // address (for example an IPv6 literal such as "[fe80::4725:c576:5cba:9c94]"), and because
        // short-lived loopback sessions are often torn down between enumeration and this call.
        if (status == NerrClientNameNotFound)
        {
            // "Disconnect all" (no client name) with nothing to remove is a no-op, not a failure.
            if (string.IsNullOrEmpty(clientName))
            {
                return;
            }

            // If the session is already gone, the requested outcome has been achieved.
            if (!SessionExists(clientName, userName))
            {
                _logger.LogDebug("SMB session for client '{ClientName}' was already disconnected.", clientName);
                return;
            }

            // The session persists but the exact client name did not match. Retry once with the
            // address stripped of its surrounding brackets, which some clients register without.
            string unbracketed = StripBrackets(clientName);
            if (!string.Equals(unbracketed, clientName, StringComparison.Ordinal))
            {
                status = InvokeNetSessionDel(unbracketed, userName);
                if (status == NerrSuccess
                    || (status == NerrClientNameNotFound && !SessionExists(clientName, userName)))
                {
                    return;
                }
            }
        }

        ThrowNativeError(status, "NetSessionDel");
    }

    private unsafe uint InvokeNetSessionDel(string? clientName, string? userName)
    {
        fixed (char* clientNamePointer = clientName)
        fixed (char* userNamePointer = userName)
        {
            return Win32PInvoke.NetSessionDel(
                default,
                clientNamePointer is null ? default : new PWSTR(clientNamePointer),
                userNamePointer is null ? default : new PWSTR(userNamePointer));
        }
    }

    private bool SessionExists(string clientName, string? userName)
    {
        try
        {
            foreach (SharedFolderSession session in EnumerateSessions())
            {
                if (string.Equals(session.ClientName, clientName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(session.UserName, userName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            // If existence cannot be confirmed, assume the session is still present so the caller
            // surfaces the original failure rather than silently reporting a false success.
            _logger.LogDebug(ex, "Could not re-enumerate SMB sessions while verifying disconnect.");
            return true;
        }
    }

    private static string StripBrackets(string clientName)
    {
        string trimmed = clientName.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']'
            ? trimmed[1..^1]
            : trimmed;
    }

    private void CloseOpenFile(uint fileId)
    {
        uint status = Win32PInvoke.NetFileClose(default, fileId);
        if (status != NerrSuccess)
        {
            ThrowNativeError(status, $"NetFileClose({fileId})");
        }
    }

    private unsafe ShareOfflineSetting GetShareOfflineSetting(string shareName)
    {
        if (string.IsNullOrWhiteSpace(shareName))
        {
            return ShareOfflineSetting.Manual;
        }

        byte* buffer = null;
        fixed (char* shareNamePointer = shareName)
        {
            uint status = Win32PInvoke.NetShareGetInfo(
                default,
                new PWSTR(shareNamePointer),
                ShareInfoLevel1005,
                out buffer);

            try
            {
                if (status != NerrSuccess || buffer is null)
                {
                    _logger.LogDebug("NetShareGetInfo(1005) failed for {ShareName}. Status={Status}", shareName, status);
                    return ShareOfflineSetting.Manual;
                }

                var info = Marshal.PtrToStructure<ShareInfo1005>((IntPtr)buffer);
                uint cscFlags = info.Flags & CscMask;
                return cscFlags switch
                {
                    CscCacheNone => ShareOfflineSetting.None,
                    CscCacheAutoReint => ShareOfflineSetting.Automatic,
                    CscCacheVdo => ShareOfflineSetting.AutomaticOptimized,
                    _ => ShareOfflineSetting.Manual
                };
            }
            finally
            {
                FreeNetApiBuffer(buffer);
            }
        }
    }

    private unsafe void SetShareRemark(string shareName, string remark)
    {
        IntPtr remarkPointer = IntPtr.Zero;
        try
        {
            remarkPointer = Marshal.StringToHGlobalUni(remark);
            var info = new ShareInfo1004 { Remark = remarkPointer };
            SetShareInfo(shareName, ShareInfoLevel1004, (byte*)&info, $"NetShareSetInfo(1004,{shareName})");
        }
        finally
        {
            FreeHGlobal(remarkPointer);
        }
    }

    private unsafe void SetShareMaxUses(string shareName, uint maxUses)
    {
        var info = new ShareInfo1006 { MaxUses = maxUses };
        SetShareInfo(shareName, ShareInfoLevel1006, (byte*)&info, $"NetShareSetInfo(1006,{shareName})");
    }

    private unsafe void SetShareSecurityDescriptor(string shareName, string sddl)
    {
        IntPtr securityDescriptor = IntPtr.Zero;
        try
        {
            securityDescriptor = AllocateSecurityDescriptor(sddl);
            var info = new ShareInfo1501 { SecurityDescriptor = securityDescriptor };
            SetShareInfo(shareName, ShareInfoLevel1501, (byte*)&info, $"NetShareSetInfo(1501,{shareName})");
        }
        finally
        {
            FreeHGlobal(securityDescriptor);
        }
    }

    private unsafe void SetShareOfflineSetting(string shareName, ShareOfflineSetting setting)
    {
        uint flags = setting switch
        {
            ShareOfflineSetting.None => CscCacheNone,
            ShareOfflineSetting.Automatic => CscCacheAutoReint,
            ShareOfflineSetting.AutomaticOptimized => CscCacheVdo,
            _ => CscCacheManualReint
        };

        var info = new ShareInfo1005 { Flags = flags };
        SetShareInfo(shareName, ShareInfoLevel1005, (byte*)&info, $"NetShareSetInfo(1005,{shareName})");
    }

    private unsafe void SetShareInfo(string shareName, uint level, byte* buffer, string operationName)
    {
        fixed (char* shareNamePointer = shareName)
        {
            uint parmError;
            uint status = Win32PInvoke.NetShareSetInfo(
                default,
                new PWSTR(shareNamePointer),
                level,
                buffer,
                &parmError);

            if (status != NerrSuccess)
            {
                ThrowNativeError(status, operationName);
            }
        }
    }

    private static void ValidateShareDefinition(SharedFolderShareDefinition definition, bool requirePath)
    {
        if (requirePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Path);
            if (!IsValidWindowsPath(definition.Path))
            {
                throw new ArgumentException("The folder path must be a valid Windows path (e.g. C:\\Share or \\\\server\\share).", nameof(definition));
            }
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
    }

    private static bool IsValidWindowsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        if ((path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) && path.Length > 2)
        {
            return true;
        }

        return false;
    }

    private static SharedFolderShareType GetShareType(uint nativeType)
    {
        return (nativeType & StypeTypeMask) switch
        {
            StypeDiskTree => SharedFolderShareType.DiskTree,
            StypePrintQueue => SharedFolderShareType.PrintQueue,
            StypeDevice => SharedFolderShareType.Device,
            StypeIpc => SharedFolderShareType.Ipc,
            _ => SharedFolderShareType.Unknown
        };
    }

    private static bool IsAdministrativeShare(string shareName, uint nativeType)
    {
        if ((nativeType & StypeSpecial) == StypeSpecial)
        {
            return true;
        }

        return shareName.EndsWith('$')
            && (shareName.Equals("ADMIN$", StringComparison.OrdinalIgnoreCase)
                || shareName.Equals("IPC$", StringComparison.OrdinalIgnoreCase)
                || (shareName.Length == 2 && char.IsLetter(shareName[0])));
    }

    private static unsafe void FreeNetApiBuffer(byte* buffer)
    {
        if (buffer is not null)
        {
            _ = Win32PInvoke.NetApiBufferFree(buffer);
        }
    }

    private static void FreeHGlobal(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr AllocateSecurityDescriptor(string sddl)
    {
        byte[] bytes = SharedFolderSecurityDescriptor.ToSelfRelativeBinary(sddl);
        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static string ReadSecurityDescriptorSddl(IntPtr securityDescriptor)
    {
        if (securityDescriptor == IntPtr.Zero)
        {
            return SharedFolderSecurityDescriptor.CreatePresetSddl(SharePermissionPreset.EveryoneRead);
        }

        uint length = AclEditorNativeMethods.GetSecurityDescriptorLength(securityDescriptor);
        if (length == 0)
        {
            return SharedFolderSecurityDescriptor.CreatePresetSddl(SharePermissionPreset.EveryoneRead);
        }

        byte[] bytes = new byte[length];
        Marshal.Copy(securityDescriptor, bytes, 0, bytes.Length);
        var descriptor = new RawSecurityDescriptor(bytes, 0);
        return descriptor.GetSddlForm(AccessControlSections.Access);
    }

    private static string ReadString(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty;

    private static void ThrowNativeError(uint status, string operationName)
    {
        var exception = new Win32Exception(unchecked((int)status));
        exception.Data["OperationName"] = operationName;
        throw exception;
    }

    // CsWin32 projects the NetApi32 buffers as byte pointers for these levels, so the
    // documented NET_API_STATUS layouts are declared locally for typed marshaling.
    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo502
    {
        public IntPtr NetName;
        public uint Type;
        public IntPtr Remark;
        public uint Permissions;
        public uint MaxUses;
        public uint CurrentUses;
        public IntPtr Path;
        public IntPtr Password;
        public uint Reserved;
        public IntPtr SecurityDescriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1004
    {
        public IntPtr Remark;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1005
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1006
    {
        public uint MaxUses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1501
    {
        public IntPtr SecurityDescriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfo502
    {
        public IntPtr ClientName;
        public IntPtr UserName;
        public uint OpenCount;
        public uint ActiveTimeSeconds;
        public uint IdleTimeSeconds;
        public uint UserFlags;
        public IntPtr ClientTypeName;
        public IntPtr Transport;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo3
    {
        public uint Id;
        public uint Permissions;
        public uint LockCount;
        public IntPtr PathName;
        public IntPtr UserName;
    }
}
