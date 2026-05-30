using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.PCManagement.Models.FsMgmt;

/// <summary>
/// Identifies the type of SMB share returned by the Windows Server service.
/// </summary>
public enum SharedFolderShareType
{
    /// <summary>A disk directory share.</summary>
    DiskTree,

    /// <summary>A printer queue share.</summary>
    PrintQueue,

    /// <summary>A communication device share.</summary>
    Device,

    /// <summary>An IPC share.</summary>
    Ipc,

    /// <summary>An unrecognized share type.</summary>
    Unknown
}

/// <summary>
/// Client-side caching mode used by SMB shares.
/// </summary>
public enum ShareOfflineSetting
{
    /// <summary>Only user-selected files and programs are available offline.</summary>
    Manual,

    /// <summary>No files or programs are available offline.</summary>
    None,

    /// <summary>Files and programs opened by users are automatically available offline.</summary>
    Automatic,

    /// <summary>Automatic offline availability with the optimized program caching mode.</summary>
    AutomaticOptimized
}

/// <summary>
/// Built-in permission presets shown by the share creation workflow.
/// </summary>
public enum SharePermissionPreset
{
    /// <summary>All users have read-only access.</summary>
    EveryoneRead,

    /// <summary>Administrators have full access and other users have read-only access.</summary>
    AdministratorsFullOthersRead,

    /// <summary>Administrators have full access and other users have no access.</summary>
    AdministratorsFullOthersNone,

    /// <summary>The caller supplies a custom security descriptor.</summary>
    Custom
}

/// <summary>
/// Share access levels exposed by the fsmgmt.msc permissions dialog.
/// </summary>
public enum ShareAccessRight
{
    /// <summary>Read access.</summary>
    Read,

    /// <summary>Change access.</summary>
    Change,

    /// <summary>Full control access.</summary>
    FullControl
}

/// <summary>
/// Indicates whether a permission entry allows or denies access.
/// </summary>
public enum SharePermissionAccessType
{
    /// <summary>Allow access.</summary>
    Allow,

    /// <summary>Deny access.</summary>
    Deny
}

/// <summary>
/// Describes a shared folder entry displayed on the Shared Folders page.
/// </summary>
public sealed class SharedFolderShare
{
    /// <summary>Gets or initializes the share name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets or initializes the local folder path.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets or initializes the share description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets or initializes the share type.</summary>
    public SharedFolderShareType Type { get; init; } = SharedFolderShareType.Unknown;

    /// <summary>Gets or initializes the current client connection count.</summary>
    public uint CurrentUses { get; init; }

    /// <summary>Gets or initializes the maximum allowed client connections.</summary>
    public uint MaxUses { get; init; } = SharedFoldersConstants.UnlimitedUses;

    /// <summary>Gets or initializes whether this is a special Windows administrative share.</summary>
    public bool IsAdministrative { get; init; }

    /// <summary>Gets or initializes the offline files setting.</summary>
    public ShareOfflineSetting OfflineSetting { get; init; }

    /// <summary>Gets or initializes the share security descriptor in SDDL form.</summary>
    public string SecurityDescriptorSddl { get; init; } = string.Empty;

    /// <summary>Gets a localized share type display name.</summary>
    public string TypeDisplayName => Type switch
    {
        SharedFolderShareType.DiskTree => GetString(FsMgmtKeys.ShareTypeDisk),
        SharedFolderShareType.PrintQueue => GetString(FsMgmtKeys.ShareTypePrint),
        SharedFolderShareType.Device => GetString(FsMgmtKeys.ShareTypeDevice),
        SharedFolderShareType.Ipc => GetString(FsMgmtKeys.ShareTypeIpc),
        _ => GetString(FsMgmtKeys.ShareTypeUnknown)
    };

    /// <summary>Gets a localized offline setting display name.</summary>
    public string OfflineSettingDisplayName => OfflineSetting switch
    {
        ShareOfflineSetting.None => GetString(FsMgmtKeys.OfflineNone),
        ShareOfflineSetting.Automatic => GetString(FsMgmtKeys.OfflineAutomatic),
        ShareOfflineSetting.AutomaticOptimized => GetString(FsMgmtKeys.OfflineAutomatic),
        _ => GetString(FsMgmtKeys.OfflineManual)
    };

    /// <summary>Gets whether optimized offline caching is enabled.</summary>
    public bool OptimizeForPerformance => OfflineSetting == ShareOfflineSetting.AutomaticOptimized;

    /// <summary>Gets a localized maximum users display value.</summary>
    public string MaxUsesDisplayName => MaxUses == SharedFoldersConstants.UnlimitedUses
        ? GetString(FsMgmtKeys.UserLimitMaximumAllowed)
        : MaxUses.ToString();

    /// <summary>Gets the localized secondary line used by the share card.</summary>
    public string DetailsLine => string.Format(
        GetString(FsMgmtKeys.ShareDetailsFormat),
        string.IsNullOrWhiteSpace(Path) ? GetString(FsMgmtKeys.NotAvailable) : Path,
        CurrentUses);

    private static string GetString(string key) =>
        LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, key);
}

/// <summary>
/// Describes an SMB session connected to the local Server service.
/// </summary>
public sealed class SharedFolderSession
{
    /// <summary>Gets or initializes the client computer name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>Gets or initializes the user name.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Gets or initializes the number of open resources in the session.</summary>
    public uint OpenCount { get; init; }

    /// <summary>Gets or initializes the active duration.</summary>
    public TimeSpan ActiveTime { get; init; }

    /// <summary>Gets or initializes the idle duration.</summary>
    public TimeSpan IdleTime { get; init; }

    /// <summary>Gets or initializes the client type.</summary>
    public string ClientType { get; init; } = string.Empty;

    /// <summary>Gets or initializes the transport name.</summary>
    public string Transport { get; init; } = string.Empty;

    /// <summary>Gets or initializes whether the session was established by a guest account.</summary>
    public bool IsGuest { get; init; }

    /// <summary>Gets the localized secondary line used by the session card.</summary>
    public string DetailsLine => string.Format(
        LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.SessionDetailsFormat),
        OpenCount,
        SharedFoldersFormatting.FormatDuration(ActiveTime),
        SharedFoldersFormatting.FormatDuration(IdleTime),
        LocalizationProvider.Current.GetString(
            ResourceFileNames.FsMgmt,
            IsGuest ? FsMgmtKeys.GuestYes : FsMgmtKeys.GuestNo));
}

/// <summary>
/// Describes a file, pipe, or device resource opened remotely through SMB.
/// </summary>
public sealed class SharedFolderOpenFile
{
    /// <summary>Gets or initializes the server-assigned file identifier.</summary>
    public uint Id { get; init; }

    /// <summary>Gets or initializes the opened path.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets or initializes the user name.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Gets or initializes the open permission bitmask.</summary>
    public uint Permissions { get; init; }

    /// <summary>Gets or initializes the lock count.</summary>
    public uint LockCount { get; init; }

    /// <summary>Gets the localized permission display name.</summary>
    public string PermissionDisplayName => SharedFoldersFormatting.FormatFilePermissions(Permissions);

    /// <summary>Gets the localized secondary line used by the open file card.</summary>
    public string DetailsLine => string.Format(
        LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.OpenFileDetailsFormat),
        PermissionDisplayName,
        LockCount);
}

/// <summary>
/// Represents editable share settings supplied by the UI.
/// </summary>
public sealed class SharedFolderShareDefinition
{
    /// <summary>Gets or sets the local folder path for a new share.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the share name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the share description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum number of allowed users.</summary>
    public uint MaxUses { get; set; } = SharedFoldersConstants.UnlimitedUses;

    /// <summary>Gets or sets the offline files setting.</summary>
    public ShareOfflineSetting OfflineSetting { get; set; }

    /// <summary>Gets or sets the share security descriptor in SDDL form.</summary>
    public string SecurityDescriptorSddl { get; set; } =
        SharedFolderSecurityDescriptor.CreatePresetSddl(SharePermissionPreset.EveryoneRead);
}

/// <summary>
/// Represents one share permission row in the custom permissions dialog.
/// </summary>
public sealed class SharePermissionEntry : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _sid = string.Empty;
    private ShareAccessRight _accessRight = ShareAccessRight.Read;
    private SharePermissionAccessType _accessType = SharePermissionAccessType.Allow;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets or sets the display name of the account or group.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Gets or sets the account SID.</summary>
    public string Sid
    {
        get => _sid;
        set => SetProperty(ref _sid, value);
    }

    /// <summary>Gets or sets the access right.</summary>
    public ShareAccessRight AccessRight
    {
        get => _accessRight;
        set
        {
            if (SetProperty(ref _accessRight, value))
            {
                OnPropertyChanged(nameof(AccessRightDisplayName));
            }
        }
    }

    /// <summary>Gets or sets whether the entry allows or denies access.</summary>
    public SharePermissionAccessType AccessType
    {
        get => _accessType;
        set
        {
            if (SetProperty(ref _accessType, value))
            {
                OnPropertyChanged(nameof(AccessTypeDisplayName));
            }
        }
    }

    /// <summary>Gets the localized access right display name.</summary>
    public string AccessRightDisplayName => AccessRight switch
    {
        ShareAccessRight.FullControl => GetString(FsMgmtKeys.PermissionFullControl),
        ShareAccessRight.Change => GetString(FsMgmtKeys.PermissionChange),
        _ => GetString(FsMgmtKeys.PermissionRead)
    };

    /// <summary>Gets the localized access type display name.</summary>
    public string AccessTypeDisplayName => AccessType == SharePermissionAccessType.Deny
        ? GetString(FsMgmtKeys.PermissionDeny)
        : GetString(FsMgmtKeys.PermissionAllow);

    /// <summary>Creates a copy of the permission entry.</summary>
    public SharePermissionEntry Clone() => new()
    {
        Name = Name,
        Sid = Sid,
        AccessRight = AccessRight,
        AccessType = AccessType
    };

    private bool SetProperty<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string GetString(string key) =>
        LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, key);
}

/// <summary>
/// Shared constants used by Shared Folders models and services.
/// </summary>
public static class SharedFoldersConstants
{
    /// <summary>Represents the native unlimited users value.</summary>
    public const uint UnlimitedUses = 0xFFFFFFFF;
}

/// <summary>
/// Creates and parses share security descriptors.
/// </summary>
public static class SharedFolderSecurityDescriptor
{
    /// <summary>Access mask for share read permission.</summary>
    public const uint ShareRead = 0x001200A9;

    /// <summary>Access mask for share change permission.</summary>
    public const uint ShareChange = 0x001301BF;

    /// <summary>Access mask for share full control permission.</summary>
    public const uint ShareFullControl = 0x001F01FF;

    private const string EveryoneSid = "S-1-1-0";
    private const string BuiltinAdministratorsSid = "S-1-5-32-544";

    /// <summary>
    /// Creates an SDDL security descriptor for a built-in permission preset.
    /// </summary>
    /// <param name="preset">The preset to create.</param>
    /// <returns>The SDDL descriptor.</returns>
    public static string CreatePresetSddl(SharePermissionPreset preset)
    {
        return preset switch
        {
            SharePermissionPreset.AdministratorsFullOthersRead =>
                CreateSddl(
                [
                    CreateEntry(BuiltinAdministratorsSid, ShareAccessRight.FullControl),
                    CreateEntry(EveryoneSid, ShareAccessRight.Read)
                ]),
            SharePermissionPreset.AdministratorsFullOthersNone =>
                CreateSddl([CreateEntry(BuiltinAdministratorsSid, ShareAccessRight.FullControl)]),
            _ => CreateSddl([CreateEntry(EveryoneSid, ShareAccessRight.Read)])
        };
    }

    /// <summary>
    /// Converts an SDDL descriptor to self-relative binary form for NetShare APIs.
    /// </summary>
    /// <param name="sddl">The SDDL descriptor.</param>
    /// <returns>The self-relative security descriptor bytes.</returns>
    public static byte[] ToSelfRelativeBinary(string sddl)
    {
        string descriptorText = string.IsNullOrWhiteSpace(sddl)
            ? CreatePresetSddl(SharePermissionPreset.EveryoneRead)
            : sddl;

        var descriptor = new RawSecurityDescriptor(descriptorText);
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }

    /// <summary>
    /// Parses a descriptor into permission rows suitable for the custom permissions dialog.
    /// </summary>
    /// <param name="sddl">The descriptor in SDDL form.</param>
    /// <returns>Permission rows.</returns>
    public static ObservableCollection<SharePermissionEntry> ParseEntries(string sddl)
    {
        if (string.IsNullOrWhiteSpace(sddl))
        {
            sddl = CreatePresetSddl(SharePermissionPreset.EveryoneRead);
        }

        var descriptor = new RawSecurityDescriptor(sddl);
        var entries = new ObservableCollection<SharePermissionEntry>();
        if (descriptor.DiscretionaryAcl is null)
        {
            return entries;
        }

        foreach (GenericAce ace in descriptor.DiscretionaryAcl)
        {
            if (ace is not CommonAce commonAce)
            {
                continue;
            }

            if (commonAce.AceQualifier is not AceQualifier.AccessAllowed and not AceQualifier.AccessDenied)
            {
                continue;
            }

            entries.Add(new SharePermissionEntry
            {
                Name = ResolveSidName(commonAce.SecurityIdentifier),
                Sid = commonAce.SecurityIdentifier.Value,
                AccessRight = AccessMaskToRight((uint)commonAce.AccessMask),
                AccessType = commonAce.AceQualifier == AceQualifier.AccessAllowed
                    ? SharePermissionAccessType.Allow
                    : SharePermissionAccessType.Deny
            });
        }

        return entries;
    }

    /// <summary>
    /// Creates an SDDL descriptor from permission rows.
    /// </summary>
    /// <param name="entries">The permission entries.</param>
    /// <returns>The descriptor in SDDL form.</returns>
    public static string CreateSddl(IEnumerable<SharePermissionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var aces = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Sid))
            .OrderBy(entry => entry.AccessType == SharePermissionAccessType.Allow ? 1 : 0)
            .Select(CreateAce)
            .ToList();

        var acl = new RawAcl(2, aces.Count);
        foreach (GenericAce ace in aces)
        {
            acl.InsertAce(acl.Count, ace);
        }

        var descriptor = new RawSecurityDescriptor(
            ControlFlags.DiscretionaryAclPresent,
            owner: null,
            group: null,
            systemAcl: null,
            discretionaryAcl: acl);

        return descriptor.GetSddlForm(AccessControlSections.Access);
    }

    /// <summary>
    /// Maps generic access flags returned by the native ACL editor to share-specific masks.
    /// </summary>
    /// <param name="mask">The incoming mask.</param>
    /// <returns>The mapped access mask.</returns>
    public static uint MapGenericAccess(uint mask)
    {
        const uint genericRead = 0x80000000;
        const uint genericWrite = 0x40000000;
        const uint genericExecute = 0x20000000;
        const uint genericAll = 0x10000000;

        if ((mask & genericRead) != 0)
        {
            mask = (mask & ~genericRead) | ShareRead;
        }

        if ((mask & genericWrite) != 0)
        {
            mask = (mask & ~genericWrite) | (ShareChange & ~ShareRead);
        }

        if ((mask & genericExecute) != 0)
        {
            mask = (mask & ~genericExecute) | ShareRead;
        }

        if ((mask & genericAll) != 0)
        {
            mask = (mask & ~genericAll) | ShareFullControl;
        }

        return mask;
    }

    /// <summary>
    /// Gets the native access mask for a display access right.
    /// </summary>
    /// <param name="right">The access right.</param>
    /// <returns>The native access mask.</returns>
    public static uint GetAccessMask(ShareAccessRight right) => right switch
    {
        ShareAccessRight.FullControl => ShareFullControl,
        ShareAccessRight.Change => ShareChange,
        _ => ShareRead
    };

    private static SharePermissionEntry CreateEntry(string sid, ShareAccessRight right) => new()
    {
        Name = ResolveSidName(new SecurityIdentifier(sid)),
        Sid = sid,
        AccessRight = right,
        AccessType = SharePermissionAccessType.Allow
    };

    private static CommonAce CreateAce(SharePermissionEntry entry)
    {
        var sid = new SecurityIdentifier(entry.Sid);
        var qualifier = entry.AccessType == SharePermissionAccessType.Allow
            ? AceQualifier.AccessAllowed
            : AceQualifier.AccessDenied;

        return new CommonAce(
            AceFlags.None,
            qualifier,
            unchecked((int)GetAccessMask(entry.AccessRight)),
            sid,
            isCallback: false,
            opaque: null);
    }

    private static ShareAccessRight AccessMaskToRight(uint mask)
    {
        if ((mask & ShareFullControl) == ShareFullControl)
        {
            return ShareAccessRight.FullControl;
        }

        if ((mask & ShareChange) == ShareChange)
        {
            return ShareAccessRight.Change;
        }

        return ShareAccessRight.Read;
    }

    private static string ResolveSidName(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
        catch (SystemException)
        {
            return sid.Value;
        }
    }
}

internal static class SharedFoldersFormatting
{
    private const uint FileRead = 0x1;
    private const uint FileWrite = 0x2;
    private const uint FileCreate = 0x4;

    internal static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1)
        {
            return string.Format(
                LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.DurationDaysFormat),
                (int)value.TotalDays,
                value.Hours,
                value.Minutes);
        }

        return string.Format(
            LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.DurationHoursFormat),
            (int)value.TotalHours,
            value.Minutes,
            value.Seconds);
    }

    internal static string FormatFilePermissions(uint permissions)
    {
        var provider = LocalizationProvider.Current;
        bool canRead = (permissions & FileRead) != 0;
        bool canWrite = (permissions & FileWrite) != 0;
        bool canCreate = (permissions & FileCreate) != 0;

        return (canRead, canWrite || canCreate) switch
        {
            (true, true) => provider.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.FilePermissionReadWrite),
            (true, false) => provider.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.FilePermissionRead),
            (false, true) => provider.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.FilePermissionWrite),
            _ => provider.GetString(ResourceFileNames.FsMgmt, FsMgmtKeys.NotAvailable)
        };
    }
}
