using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.NetManagement;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PrintManagement.Services.Native;

/// <summary>
/// Native domain join status queries backed by CsWin32-generated bindings.
/// </summary>
internal static class DomainJoinNativeMethods
{
    private const uint NerrSuccess = 0;

    /// <summary>
    /// Tries to determine whether the local computer is joined to an Active Directory domain.
    /// </summary>
    internal static unsafe bool TryGetIsJoinedToDomain(out bool isJoinedToDomain)
    {
        PWSTR joinedName = default;
        NETSETUP_JOIN_STATUS joinStatus = default;

        uint result = Win32PInvoke.NetGetJoinInformation(null, &joinedName, &joinStatus);
        try
        {
            isJoinedToDomain = result == NerrSuccess &&
                joinStatus == NETSETUP_JOIN_STATUS.NetSetupDomainName;
            return result == NerrSuccess;
        }
        finally
        {
            if (joinedName.Value is not null)
            {
                _ = Win32PInvoke.NetApiBufferFree(joinedName.Value);
            }
        }
    }
}
