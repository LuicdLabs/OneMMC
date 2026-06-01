using System.Collections.ObjectModel;
using System.Security.AccessControl;
using System.Security.Principal;
using ManagementTools.Core.Features.PCManagement.Models.FsMgmt;
using ManagementTools.Core.Infrastructure.WindowsCapabilities;
using ManagementTools.Core.Localization;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.FsMgmt;

/// <summary>
/// Dialog content used to edit SMB share permissions.
/// </summary>
public sealed partial class SharePermissionsDialog : UserControl
{
    private const uint DeleteAccess = 0x00010000;
    private const uint ReadControlAccess = 0x00020000;
    private const uint SynchronizeAccess = 0x00100000;
    private const uint GenericReadAccess = 0x80000000;
    private const uint GenericWriteAccess = 0x40000000;
    private const uint GenericExecuteAccess = 0x20000000;
    private const uint GenericAllAccess = 0x10000000;
    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint GroupSecurityInformation = 0x00000002;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint SaclSecurityInformation = 0x00000008;
    private const uint DaclTreeSecurityInformation = 0x00004000;
    private const uint SaclTreeSecurityInformation = 0x00008000;
    private const uint FileReadData = 0x0001;
    private const uint FileWriteData = 0x0002;
    private const uint FileAppendData = 0x0004;
    private const uint FileReadExtendedAttributes = 0x0008;
    private const uint FileWriteExtendedAttributes = 0x0010;
    private const uint FileExecute = 0x0020;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileWriteAttributes = 0x0100;
    private const uint FileAllAccess = 0x001F01FF;
    private const uint FileGenericRead = ReadControlAccess | FileReadData | FileReadAttributes | FileReadExtendedAttributes | SynchronizeAccess;
    private const uint FileGenericWrite = ReadControlAccess | FileWriteData | FileAppendData | FileWriteAttributes | FileWriteExtendedAttributes | SynchronizeAccess;
    private const uint FileGenericExecute = ReadControlAccess | FileReadAttributes | FileExecute | SynchronizeAccess;

    private readonly string _folderPath;
    private bool _updatingSelection;
    private bool _securityTabAvailable;

    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Gets permission entries displayed by the dialog.
    /// </summary>
    public ObservableCollection<SharePermissionEntry> Entries { get; } = [];

    /// <summary>
    /// Gets NTFS permission entries displayed on the Security tab.
    /// </summary>
    public ObservableCollection<FileSystemPermissionEntry> SecurityEntries { get; } = [];

    /// <summary>
    /// Gets the resulting share security descriptor after the dialog is accepted.
    /// </summary>
    public string ResultSddl { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharePermissionsDialog"/> class.
    /// </summary>
    /// <param name="securityDescriptorSddl">The share security descriptor SDDL to edit.</param>
    /// <param name="folderPath">The folder path used to display NTFS security information.</param>
    public SharePermissionsDialog(string securityDescriptorSddl, string folderPath = "")
    {
        ResultSddl = string.IsNullOrWhiteSpace(securityDescriptorSddl)
            ? SharedFolderSecurityDescriptor.CreatePresetSddl(SharePermissionPreset.EveryoneRead)
            : securityDescriptorSddl;
        _folderPath = folderPath;

        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        ObjectNameTextBlock.Text = _folderPath;
        LoadEntries(ResultSddl);
        SelectFirstSharePermissionEntry();
        LoadSecurityEntries();
        SelectFirstSecurityEntry();
        UpdateSelectionControls();
        UpdateSecuritySelectionControls();
    }

    /// <summary>
    /// Shows the dialog inside a native modal window.
    /// </summary>
    /// <param name="ownerXamlRoot">The owner XAML root.</param>
    /// <returns>The modal dialog result.</returns>
    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.FsMgmt_Preset_Custom,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 430,
            Height = 590,
            IsPrimaryButtonLeading = true,
            OnPrimaryButtonClick = TryCommitResult
        });

        return modalWindow.ShowDialogAsync();
    }

    private void LoadEntries(string sddl)
    {
        Entries.Clear();
        foreach (SharePermissionEntry entry in SharedFolderSecurityDescriptor.ParseEntries(sddl))
        {
            Entries.Add(entry);
        }
    }

    private void LoadSecurityEntries()
    {
        SecurityEntries.Clear();
        bool isAvailable = !string.IsNullOrWhiteSpace(_folderPath) && Directory.Exists(_folderPath);
        if (!isAvailable)
        {
            UpdateSecurityTabAvailability(false);
            return;
        }

        try
        {
            DirectorySecurity security = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(_folderPath));
            AuthorizationRuleCollection rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier));

            Dictionary<string, FileSystemPermissionEntry> entries = [];
            foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
            {
                if (rule.IdentityReference is not SecurityIdentifier sid)
                {
                    continue;
                }

                if (!entries.TryGetValue(sid.Value, out FileSystemPermissionEntry? entry))
                {
                    entry = new FileSystemPermissionEntry
                    {
                        Name = ResolveSidName(sid),
                        Sid = sid.Value
                    };
                    entries.Add(sid.Value, entry);
                }

                if (rule.AccessControlType == AccessControlType.Deny)
                {
                    entry.DenyRights |= rule.FileSystemRights;
                }
                else
                {
                    entry.AllowRights |= rule.FileSystemRights;
                }
            }

            foreach (FileSystemPermissionEntry entry in entries.Values.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                SecurityEntries.Add(entry);
            }

            UpdateSecurityTabAvailability(true);
        }
        catch (Exception)
        {
            SecurityEntries.Clear();
            UpdateSecurityTabAvailability(false);
        }
    }

    private void PermissionsSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem { Tag: string tag })
        {
            return;
        }

        bool showSecurity = tag == "Security" && _securityTabAvailable;
        SharePermissionsPane.Visibility = showSecurity ? Visibility.Collapsed : Visibility.Visible;
        SecurityPane.Visibility = showSecurity ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PermissionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionControls();
    }

    private void PermissionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingSelection || PermissionsList.SelectedItem is not SharePermissionEntry entry)
        {
            return;
        }

        if (sender is not CheckBox checkBox || checkBox.Tag is not string tag)
        {
            return;
        }

        if (!TryParsePermissionTag(tag, out SharePermissionAccessType accessType, out ShareAccessRight accessRight))
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            entry.AccessType = accessType;
            entry.AccessRight = accessRight;
            UpdateSelectionControls();
            return;
        }

        if (entry.AccessType == accessType && CoversRight(entry.AccessRight, accessRight))
        {
            entry.AccessRight = accessRight switch
            {
                ShareAccessRight.FullControl => ShareAccessRight.Change,
                ShareAccessRight.Change => ShareAccessRight.Read,
                _ => ShareAccessRight.Read
            };
        }

        UpdateSelectionControls();
    }

    private void SecurityPrincipalsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSecuritySelectionControls();
    }

    private async void SecurityEditButton_Click(object sender, RoutedEventArgs e)
    {
        await EditFileSystemSecurityAsync(AclEditorPageType.Permissions);
    }

    private async void SecurityAdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        await EditFileSystemSecurityAsync(AclEditorPageType.AdvancedPermissions);
    }

    private async Task EditFileSystemSecurityAsync(AclEditorPageType pageType)
    {
        if (!_securityTabAvailable)
        {
            return;
        }

        try
        {
            DirectorySecurity currentSecurity = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(_folderPath));
            var request = CreateFileSystemAclEditorRequest(
                currentSecurity.GetSecurityDescriptorSddlForm(AccessControlSections.All),
                pageType);
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            AclEditorResult result = App.GetRequiredService<AclEditorService>().EditSecurity(hwnd, request);
            if (result.WasModified)
            {
                AccessControlSections sections = GetAccessControlSections(result.SecurityInformation);
                var updatedSecurity = new DirectorySecurity();
                updatedSecurity.SetSecurityDescriptorSddlForm(
                    result.SecurityDescriptor.GetSddlForm(sections),
                    sections);
                FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(_folderPath), updatedSecurity);
                LoadSecurityEntries();
                SelectFirstSecurityEntry();
                UpdateSecuritySelectionControls();
            }

            if (result.WasSecondaryModified && result.SecondarySecurityDescriptor is not null)
            {
                ResultSddl = result.SecondarySecurityDescriptor.GetSddlForm(AccessControlSections.Access);
                LoadEntries(ResultSddl);
                SelectFirstSharePermissionEntry();
                UpdateSelectionControls();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(ex.Message);
        }
    }

    private AclEditorRequest CreateFileSystemAclEditorRequest(string securityDescriptorSddl, AclEditorPageType pageType)
    {
        var request = new AclEditorRequest
        {
            ObjectName = _folderPath,
            PageTitle = string.Empty,
            SecurityDescriptorSddl = securityDescriptorSddl,
            ObjectInformationFlags = AclEditorObjectFlags.Advanced
                                     | AclEditorObjectFlags.Container
                                     | AclEditorObjectFlags.EditOwner
                                     | AclEditorObjectFlags.EditAudits
                                     | AclEditorObjectFlags.EditEffective,
            PageType = pageType,
            MapGenericAccess = MapFileSystemGenericAccess,
            SecondarySecurity = CreateShareAclEditorRequest(),
            EmptySecurityDescriptorFactory = static () => new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent,
                owner: null,
                group: null,
                systemAcl: null,
                discretionaryAcl: new RawAcl(2, 0))
        };

        request.AccessEntries.AddRange(CreateFileSystemAccessEntries());
        request.InheritTypes.AddRange(CreateFileSystemInheritEntries());
        return request;
    }

    private AclEditorSecondarySecurityRequest CreateShareAclEditorRequest()
    {
        var request = new AclEditorSecondarySecurityRequest
        {
            Name = LocalizedString(FsMgmtKeys.PermissionShareTab, FsMgmtKeys.PermissionShareTab),
            ObjectName = _folderPath,
            PageTitle = LocalizedString(FsMgmtKeys.PermissionShareTab, FsMgmtKeys.PermissionShareTab),
            SecurityDescriptorSddl = ResultSddl,
            ObjectInformationFlags = AclEditorObjectFlags.Advanced,
            MapGenericAccess = SharedFolderSecurityDescriptor.MapGenericAccess,
            EmptySecurityDescriptorFactory = static () => new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent,
                owner: null,
                group: null,
                systemAcl: null,
                discretionaryAcl: new RawAcl(2, 0))
        };

        request.AccessEntries.AddRange(CreateShareAccessEntries());
        return request;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        List<DirectoryObject>? selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            ObjectPickerTypes.UsersAndGroups,
            multiSelect: true);

        if (selections is null)
        {
            return;
        }

        foreach (DirectoryObject selection in selections)
        {
            if (string.IsNullOrWhiteSpace(selection.Sid)
                || Entries.Any(entry => string.Equals(entry.Sid, selection.Sid, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Entries.Add(new SharePermissionEntry
            {
                Name = string.IsNullOrWhiteSpace(selection.Name) ? selection.Sid : selection.Name,
                Sid = selection.Sid,
                AccessType = SharePermissionAccessType.Allow,
                AccessRight = ShareAccessRight.Read
            });
        }

        if (PermissionsList.SelectedItem is null && Entries.Count > 0)
        {
            PermissionsList.SelectedItem = Entries[^1];
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PermissionsList.SelectedItem is SharePermissionEntry entry)
        {
            Entries.Remove(entry);
        }

        UpdateSelectionControls();
    }

    private bool TryCommitResult()
    {
        ResultSddl = SharedFolderSecurityDescriptor.CreateSddl(Entries);
        return true;
    }

    private void UpdateSelectionControls()
    {
        _updatingSelection = true;
        try
        {
            var entry = PermissionsList.SelectedItem as SharePermissionEntry;
            RemoveButton.IsEnabled = entry is not null;
            SetSharePermissionCheckBoxesEnabled(entry is not null);
            PermissionsForTextBlock.Text = entry is null
                ? string.Empty
                : string.Format(LocalizedStrings.FsMgmt_Permissions_ForFormat, entry.Name);

            if (entry is null)
            {
                SetAllSharePermissionCheckBoxes(false, false);
                return;
            }

            SetSharePermissionCheckBoxStates(entry);
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private void UpdateSecuritySelectionControls()
    {
        var entry = SecurityPrincipalsList.SelectedItem as FileSystemPermissionEntry;
        SecurityPermissionsForTextBlock.Text = entry is null
            ? string.Empty
            : string.Format(LocalizedStrings.FsMgmt_Permissions_ForFormat, entry.Name);

        SetSecurityIconVisibility(entry);
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.Common_ErrorTitle,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            CloseButtonText = LocalizedStrings.Common_OKButton,
            DefaultButton = WindowDialogResult.None,
            Width = 480,
            Height = 220
        });

        await modalWindow.ShowDialogAsync();
    }

    private void UpdateSecurityTabAvailability(bool isAvailable)
    {
        _securityTabAvailable = isAvailable;
        SecurityTabItem.IsEnabled = isAvailable;
        SecurityEditButton.IsEnabled = isAvailable;
        SecurityAdvancedButton.IsEnabled = isAvailable;

        if (!isAvailable && PermissionsSelectorBar.SelectedItem == SecurityTabItem)
        {
            PermissionsSelectorBar.SelectedItem = SharePermissionsTabItem;
            SharePermissionsPane.Visibility = Visibility.Visible;
            SecurityPane.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectFirstSharePermissionEntry()
    {
        if (Entries.Count > 0)
        {
            PermissionsList.SelectedIndex = 0;
        }
    }

    private void SelectFirstSecurityEntry()
    {
        if (SecurityEntries.Count > 0)
        {
            SecurityPrincipalsList.SelectedIndex = 0;
        }
    }

    private void SetSharePermissionCheckBoxesEnabled(bool isEnabled)
    {
        AllowFullControlCheckBox.IsEnabled = isEnabled;
        AllowChangeCheckBox.IsEnabled = isEnabled;
        AllowReadCheckBox.IsEnabled = isEnabled;
        DenyFullControlCheckBox.IsEnabled = isEnabled;
        DenyChangeCheckBox.IsEnabled = isEnabled;
        DenyReadCheckBox.IsEnabled = isEnabled;
    }

    private void SetAllSharePermissionCheckBoxes(bool allow, bool deny)
    {
        AllowFullControlCheckBox.IsChecked = allow;
        AllowChangeCheckBox.IsChecked = allow;
        AllowReadCheckBox.IsChecked = allow;
        DenyFullControlCheckBox.IsChecked = deny;
        DenyChangeCheckBox.IsChecked = deny;
        DenyReadCheckBox.IsChecked = deny;
    }

    private void SetSharePermissionCheckBoxStates(SharePermissionEntry entry)
    {
        bool isAllow = entry.AccessType == SharePermissionAccessType.Allow;
        bool isDeny = entry.AccessType == SharePermissionAccessType.Deny;

        AllowFullControlCheckBox.IsChecked = isAllow && CoversRight(entry.AccessRight, ShareAccessRight.FullControl);
        AllowChangeCheckBox.IsChecked = isAllow && CoversRight(entry.AccessRight, ShareAccessRight.Change);
        AllowReadCheckBox.IsChecked = isAllow && CoversRight(entry.AccessRight, ShareAccessRight.Read);
        DenyFullControlCheckBox.IsChecked = isDeny && CoversRight(entry.AccessRight, ShareAccessRight.FullControl);
        DenyChangeCheckBox.IsChecked = isDeny && CoversRight(entry.AccessRight, ShareAccessRight.Change);
        DenyReadCheckBox.IsChecked = isDeny && CoversRight(entry.AccessRight, ShareAccessRight.Read);
    }

    private void SetSecurityIconVisibility(FileSystemPermissionEntry? entry)
    {
        SetIcon(SecurityAllowFullControlIcon, entry?.AllowsFullControl == true);
        SetIcon(SecurityDenyFullControlIcon, entry?.DeniesFullControl == true);
        SetIcon(SecurityAllowModifyIcon, entry?.AllowsModify == true);
        SetIcon(SecurityDenyModifyIcon, entry?.DeniesModify == true);
        SetIcon(SecurityAllowReadExecuteIcon, entry?.AllowsReadAndExecute == true);
        SetIcon(SecurityDenyReadExecuteIcon, entry?.DeniesReadAndExecute == true);
        SetIcon(SecurityAllowListFolderIcon, entry?.AllowsListFolder == true);
        SetIcon(SecurityDenyListFolderIcon, entry?.DeniesListFolder == true);
        SetIcon(SecurityAllowReadIcon, entry?.AllowsRead == true);
        SetIcon(SecurityDenyReadIcon, entry?.DeniesRead == true);
        SetIcon(SecurityAllowWriteIcon, entry?.AllowsWrite == true);
        SetIcon(SecurityDenyWriteIcon, entry?.DeniesWrite == true);
    }

    private static void SetIcon(SymbolIcon icon, bool isVisible)
    {
        icon.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool TryParsePermissionTag(
        string tag,
        out SharePermissionAccessType accessType,
        out ShareAccessRight accessRight)
    {
        accessType = SharePermissionAccessType.Allow;
        accessRight = ShareAccessRight.Read;

        string[] parts = tag.Split('|');
        if (parts.Length != 2)
        {
            return false;
        }

        accessType = parts[0] == nameof(SharePermissionAccessType.Deny)
            ? SharePermissionAccessType.Deny
            : SharePermissionAccessType.Allow;
        accessRight = parts[1] switch
        {
            nameof(ShareAccessRight.FullControl) => ShareAccessRight.FullControl,
            nameof(ShareAccessRight.Change) => ShareAccessRight.Change,
            _ => ShareAccessRight.Read
        };

        return true;
    }

    private static bool CoversRight(ShareAccessRight grantedRight, ShareAccessRight rowRight)
    {
        return grantedRight switch
        {
            ShareAccessRight.FullControl => true,
            ShareAccessRight.Change => rowRight is ShareAccessRight.Change or ShareAccessRight.Read,
            _ => rowRight == ShareAccessRight.Read
        };
    }

    private static IEnumerable<AclEditorAccessEntry> CreateFileSystemAccessEntries()
    {
        return
        [
            CreateFileSystemAccessEntry(FileAllAccess, FsMgmtKeys.PermissionFullControl),
            CreateFileSystemAccessEntry((uint)FileSystemRights.Modify, FsMgmtKeys.PermissionModify),
            CreateFileSystemAccessEntry(FileGenericRead | FileGenericExecute, FsMgmtKeys.PermissionReadExecute),
            CreateFileSystemAccessEntry((uint)FileSystemRights.ListDirectory, FsMgmtKeys.PermissionListFolder),
            CreateFileSystemAccessEntry(FileGenericRead, FsMgmtKeys.PermissionRead),
            CreateFileSystemAccessEntry(FileGenericWrite, FsMgmtKeys.PermissionWrite),
            CreateFileSystemAccessEntry(DeleteAccess, FsMgmtKeys.PermissionDelete)
        ];
    }

    private static IEnumerable<AclEditorAccessEntry> CreateShareAccessEntries()
    {
        return
        [
            CreateFileSystemAccessEntry(SharedFolderSecurityDescriptor.ShareFullControl, FsMgmtKeys.PermissionFullControl),
            CreateFileSystemAccessEntry(SharedFolderSecurityDescriptor.ShareChange, FsMgmtKeys.PermissionChange),
            CreateFileSystemAccessEntry(SharedFolderSecurityDescriptor.ShareRead, FsMgmtKeys.PermissionRead)
        ];
    }

    private static IEnumerable<AclEditorInheritType> CreateFileSystemInheritEntries()
    {
        return
        [
            CreateFileSystemInheritEntry(0, FsMgmtKeys.PermissionThisFolderOnly),
            CreateFileSystemInheritEntry(
                AclEditorAceFlags.ContainerInherit | AclEditorAceFlags.ObjectInherit,
                FsMgmtKeys.PermissionThisFolderSubfoldersFiles),
            CreateFileSystemInheritEntry(
                AclEditorAceFlags.ContainerInherit,
                FsMgmtKeys.PermissionThisFolderSubfolders),
            CreateFileSystemInheritEntry(
                AclEditorAceFlags.ObjectInherit,
                FsMgmtKeys.PermissionThisFolderFiles),
            CreateFileSystemInheritEntry(
                AclEditorAceFlags.ContainerInherit | AclEditorAceFlags.ObjectInherit | AclEditorAceFlags.InheritOnly,
                FsMgmtKeys.PermissionSubfoldersFilesOnly),
            CreateFileSystemInheritEntry(
                AclEditorAceFlags.ContainerInherit | AclEditorAceFlags.InheritOnly,
                FsMgmtKeys.PermissionSubfoldersOnly),
            CreateFileSystemInheritEntry(
                AclEditorAceFlags.ObjectInherit | AclEditorAceFlags.InheritOnly,
                FsMgmtKeys.PermissionFilesOnly)
        ];
    }

    private static AclEditorAccessEntry CreateFileSystemAccessEntry(uint mask, string resourceKey)
    {
        return new AclEditorAccessEntry
        {
            Mask = mask,
            Name = LocalizedString(resourceKey, resourceKey)
        };
    }

    private static AclEditorInheritType CreateFileSystemInheritEntry(uint flags, string resourceKey)
    {
        return new AclEditorInheritType
        {
            Flags = flags,
            Name = LocalizedString(resourceKey, resourceKey)
        };
    }

    private static uint MapFileSystemGenericAccess(uint mask)
    {
        if ((mask & GenericReadAccess) != 0)
        {
            mask = (mask & ~GenericReadAccess) | FileGenericRead;
        }

        if ((mask & GenericWriteAccess) != 0)
        {
            mask = (mask & ~GenericWriteAccess) | FileGenericWrite;
        }

        if ((mask & GenericExecuteAccess) != 0)
        {
            mask = (mask & ~GenericExecuteAccess) | FileGenericExecute;
        }

        if ((mask & GenericAllAccess) != 0)
        {
            mask = (mask & ~GenericAllAccess) | FileAllAccess;
        }

        return mask;
    }

    private static AccessControlSections GetAccessControlSections(uint securityInformation)
    {
        AccessControlSections sections = AccessControlSections.None;

        if ((securityInformation & OwnerSecurityInformation) != 0)
        {
            sections |= AccessControlSections.Owner;
        }

        if ((securityInformation & GroupSecurityInformation) != 0)
        {
            sections |= AccessControlSections.Group;
        }

        if ((securityInformation & (DaclSecurityInformation | DaclTreeSecurityInformation)) != 0)
        {
            sections |= AccessControlSections.Access;
        }

        if ((securityInformation & (SaclSecurityInformation | SaclTreeSecurityInformation)) != 0)
        {
            sections |= AccessControlSections.Audit;
        }

        return sections == AccessControlSections.None ? AccessControlSections.Access : sections;
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

    private static string LocalizedString(string key, string fallback)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
        {
            return fallback;
        }

        return value;
    }
}

/// <summary>
/// Describes a file-system security principal displayed on the Security tab.
/// </summary>
public sealed class FileSystemPermissionEntry
{
    /// <summary>Gets or sets the display name of the account or group.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the account SID.</summary>
    public string Sid { get; set; } = string.Empty;

    /// <summary>Gets or sets the accumulated allow rights.</summary>
    public FileSystemRights AllowRights { get; set; }

    /// <summary>Gets or sets the accumulated deny rights.</summary>
    public FileSystemRights DenyRights { get; set; }

    /// <summary>Gets whether full control is allowed.</summary>
    public bool AllowsFullControl => HasRight(AllowRights, FileSystemRights.FullControl);

    /// <summary>Gets whether full control is denied.</summary>
    public bool DeniesFullControl => HasRight(DenyRights, FileSystemRights.FullControl);

    /// <summary>Gets whether modify is allowed.</summary>
    public bool AllowsModify => HasRight(AllowRights, FileSystemRights.Modify);

    /// <summary>Gets whether modify is denied.</summary>
    public bool DeniesModify => HasRight(DenyRights, FileSystemRights.Modify);

    /// <summary>Gets whether read and execute is allowed.</summary>
    public bool AllowsReadAndExecute => HasRight(AllowRights, FileSystemRights.ReadAndExecute);

    /// <summary>Gets whether read and execute is denied.</summary>
    public bool DeniesReadAndExecute => HasRight(DenyRights, FileSystemRights.ReadAndExecute);

    /// <summary>Gets whether folder contents can be listed.</summary>
    public bool AllowsListFolder => HasRight(AllowRights, FileSystemRights.ListDirectory);

    /// <summary>Gets whether folder listing is denied.</summary>
    public bool DeniesListFolder => HasRight(DenyRights, FileSystemRights.ListDirectory);

    /// <summary>Gets whether read is allowed.</summary>
    public bool AllowsRead => HasRight(AllowRights, FileSystemRights.Read);

    /// <summary>Gets whether read is denied.</summary>
    public bool DeniesRead => HasRight(DenyRights, FileSystemRights.Read);

    /// <summary>Gets whether write is allowed.</summary>
    public bool AllowsWrite => HasRight(AllowRights, FileSystemRights.Write);

    /// <summary>Gets whether write is denied.</summary>
    public bool DeniesWrite => HasRight(DenyRights, FileSystemRights.Write);

    private static bool HasRight(FileSystemRights rights, FileSystemRights target)
    {
        return (rights & target) == target;
    }
}
