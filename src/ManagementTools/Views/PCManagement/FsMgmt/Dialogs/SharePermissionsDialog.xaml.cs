using System.Collections.ObjectModel;
using System.Security.AccessControl;
using ManagementTools.Core.Features.PCManagement.Models.FsMgmt;
using ManagementTools.Core.Infrastructure.WindowsCapabilities;
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
    private readonly string _objectName;
    private bool _updatingSelection;

    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Gets permission entries displayed by the dialog.
    /// </summary>
    public ObservableCollection<SharePermissionEntry> Entries { get; } = [];

    /// <summary>
    /// Gets the resulting share security descriptor after the dialog is accepted.
    /// </summary>
    public string ResultSddl { get; private set; }

    public SharePermissionsDialog(string securityDescriptorSddl, string objectName)
    {
        ResultSddl = string.IsNullOrWhiteSpace(securityDescriptorSddl)
            ? SharedFolderSecurityDescriptor.CreatePresetSddl(SharePermissionPreset.EveryoneRead)
            : securityDescriptorSddl;
        _objectName = objectName;

        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        LoadEntries(ResultSddl);
        UpdateSelectionControls();
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
            Title = LocalizedStrings.FsMgmt_Permissions_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 820,
            Height = 620,
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

    private void PermissionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionControls();
    }

    private void PermissionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || PermissionsList.SelectedItem is not SharePermissionEntry entry)
        {
            return;
        }

        entry.AccessType = GetSelectedTag(AccessTypeComboBox) == nameof(SharePermissionAccessType.Deny)
            ? SharePermissionAccessType.Deny
            : SharePermissionAccessType.Allow;
        entry.AccessRight = GetSelectedTag(AccessRightComboBox) switch
        {
            nameof(ShareAccessRight.FullControl) => ShareAccessRight.FullControl,
            nameof(ShareAccessRight.Change) => ShareAccessRight.Change,
            _ => ShareAccessRight.Read
        };
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

    private async void AdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        ResultSddl = SharedFolderSecurityDescriptor.CreateSddl(Entries);

        try
        {
            var request = CreateAclEditorRequest();
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            AclEditorResult result = App.GetRequiredService<AclEditorService>().EditSecurity(hwnd, request);
            if (result.WasModified)
            {
                ResultSddl = result.SecurityDescriptor.GetSddlForm(AccessControlSections.Access);
                LoadEntries(ResultSddl);
                UpdateSelectionControls();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(ex.Message);
        }
    }

    private bool TryCommitResult()
    {
        ResultSddl = SharedFolderSecurityDescriptor.CreateSddl(Entries);
        return true;
    }

    private AclEditorRequest CreateAclEditorRequest()
    {
        var request = new AclEditorRequest
        {
            ObjectName = _objectName,
            PageTitle = LocalizedStrings.FsMgmt_Permissions_Title,
            SecurityDescriptorSddl = ResultSddl,
            ObjectInformationFlags = AclEditorObjectFlags.Advanced
                                     | AclEditorObjectFlags.PageTitle
                                     | AclEditorObjectFlags.NoAclProtect
                                     | AclEditorObjectFlags.NoTreeApply,
            PageType = AclEditorPageType.Permissions,
            MapGenericAccess = SharedFolderSecurityDescriptor.MapGenericAccess,
            EmptySecurityDescriptorFactory = static () => new RawSecurityDescriptor(
                SharedFolderSecurityDescriptor.CreatePresetSddl(SharePermissionPreset.EveryoneRead))
        };

        request.AccessEntries.Add(new AclEditorAccessEntry
        {
            Mask = SharedFolderSecurityDescriptor.ShareFullControl,
            Name = LocalizedStrings.FsMgmt_Permissions_FullControl
        });
        request.AccessEntries.Add(new AclEditorAccessEntry
        {
            Mask = SharedFolderSecurityDescriptor.ShareChange,
            Name = LocalizedStrings.FsMgmt_Permissions_Change
        });
        request.AccessEntries.Add(new AclEditorAccessEntry
        {
            Mask = SharedFolderSecurityDescriptor.ShareRead,
            Name = LocalizedStrings.FsMgmt_Permissions_Read
        });

        return request;
    }

    private void UpdateSelectionControls()
    {
        _updatingSelection = true;
        try
        {
            var entry = PermissionsList.SelectedItem as SharePermissionEntry;
            RemoveButton.IsEnabled = entry is not null;
            AccessTypeComboBox.IsEnabled = entry is not null;
            AccessRightComboBox.IsEnabled = entry is not null;

            if (entry is null)
            {
                AccessTypeComboBox.SelectedIndex = -1;
                AccessRightComboBox.SelectedIndex = -1;
                return;
            }

            SetSelectedTag(AccessTypeComboBox, entry.AccessType.ToString());
            SetSelectedTag(AccessRightComboBox, entry.AccessRight.ToString());
        }
        finally
        {
            _updatingSelection = false;
        }
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

    private static string GetSelectedTag(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : string.Empty;
    }

    private static void SetSelectedTag(ComboBox comboBox, string tag)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem { Tag: string itemTag } && itemTag == tag)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }
}
