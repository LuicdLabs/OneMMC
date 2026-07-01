using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Rules;

/// <summary>
/// Enumerates Windows app containers that can be used by firewall application package rules.
/// </summary>
public class FirewallAppContainerService
{
    private readonly ILogger<FirewallAppContainerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirewallAppContainerService"/> class.
    /// </summary>
    public FirewallAppContainerService()
        : this(NullLogger<FirewallAppContainerService>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FirewallAppContainerService"/> class.
    /// </summary>
    /// <param name="logger">Logger used for enumeration diagnostics.</param>
    public FirewallAppContainerService(ILogger<FirewallAppContainerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the app containers currently known to Windows Firewall.
    /// </summary>
    /// <returns>A stable, de-duplicated list of app containers sorted by name and owner.</returns>
    public IReadOnlyList<FirewallAppContainerInfo> GetAppContainers()
    {
        IntPtr nativeArray = IntPtr.Zero;

        try
        {
            uint error = AppContainerNativeMethods.NetworkIsolationEnumAppContainers(
                flags: 0,
                out uint count,
                out nativeArray);

            if (error != 0)
            {
                throw new Win32Exception((int)error);
            }

            int itemSize = Marshal.SizeOf<AppContainerNativeMethods.InetFirewallAppContainer>();
            List<FirewallAppContainerInfo> containers = [];

            for (int index = 0; index < count; index++)
            {
                IntPtr current = IntPtr.Add(nativeArray, index * itemSize);
                AppContainerNativeMethods.InetFirewallAppContainer nativeItem =
                    Marshal.PtrToStructure<AppContainerNativeMethods.InetFirewallAppContainer>(current);

                string appContainerSid = ConvertSidToString(nativeItem.AppContainerSid);
                string appContainerName = (nativeItem.AppContainerName ?? string.Empty).Trim();
                string displayName = (nativeItem.DisplayName ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(appContainerSid) ||
                    (string.IsNullOrWhiteSpace(appContainerName) && string.IsNullOrWhiteSpace(displayName)))
                {
                    continue;
                }

                string userSid = ConvertSidToString(nativeItem.UserSid);
                containers.Add(new FirewallAppContainerInfo
                {
                    AppContainerName = string.IsNullOrWhiteSpace(appContainerName) ? displayName : appContainerName,
                    DisplayName = displayName,
                    AppContainerSid = appContainerSid,
                    UserSid = userSid,
                    UserDisplayName = ResolveUserDisplayName(userSid)
                });
            }

            return containers
                .GroupBy(
                    container => $"{container.AppContainerSid}|{container.UserSid}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(container => container.AppContainerName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(container => container.UserDisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate Windows Firewall app containers.");
            return [];
        }
        finally
        {
            if (nativeArray != IntPtr.Zero)
            {
                AppContainerNativeMethods.NetworkIsolationFreeAppContainers(nativeArray);
            }
        }
    }

    private static string ConvertSidToString(IntPtr sid)
    {
        if (sid == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (!AppContainerNativeMethods.ConvertSidToStringSid(sid, out IntPtr sidStringPtr) ||
            sidStringPtr == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUni(sidStringPtr) ?? string.Empty;
        }
        finally
        {
            _ = AppContainerNativeMethods.LocalFree(sidStringPtr);
        }
    }

    private static string ResolveUserDisplayName(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            return string.Empty;
        }

        try
        {
            var sid = new SecurityIdentifier(userSid);
            IdentityReferenceCollection translatedIdentities = [sid];
            NTAccount? translatedAccount = translatedIdentities.Translate(typeof(NTAccount), false)
                .OfType<NTAccount>()
                .FirstOrDefault();

            return translatedAccount?.Value ?? userSid;
        }
        catch
        {
            return userSid;
        }
    }
}
