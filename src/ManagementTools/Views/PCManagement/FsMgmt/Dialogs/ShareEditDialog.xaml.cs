using System;
using ManagementTools.Core.Abstractions.Services;
using ManagementTools.Core.Features.PCManagement.Models.FsMgmt;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.FsMgmt;

/// <summary>
/// Dialog content used to create or edit an SMB share.
/// </summary>
public sealed partial class ShareEditDialog : ContentDialog
{
    private readonly SharedFolderShare? _share;
    private readonly bool _isEditMode;
    private string _securityDescriptorSddl;

    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Gets the share definition accepted by the user.
    /// </summary>
    public SharedFolderShareDefinition Definition { get; private set; } = new();

    /// <summary>
    /// Gets whether this dialog is showing a Windows administrative share in read-only mode.
    /// </summary>
    public bool IsReadOnly { get; }

    public ShareEditDialog(SharedFolderShare? share = null)
    {
        _share = share;
        _isEditMode = share is not null;
        IsReadOnly = share?.IsAdministrative == true;
        _securityDescriptorSddl = string.IsNullOrWhiteSpace(share?.SecurityDescriptorSddl)
            ? SharedFolderSecurityDescriptor.CreatePresetSddl(SharePermissionPreset.EveryoneRead)
            : share.SecurityDescriptorSddl;

        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        ConfigureDialog();
    }

    /// <summary>
    /// Shows the dialog inside a native modal window.
    /// </summary>
    /// <param name="ownerXamlRoot">The owner XAML root.</param>
    /// <returns>The modal dialog result.</returns>
    public Task<ContentDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        XamlRoot = ownerXamlRoot;
        return ShowAsync().AsTask();
    }

    private void ConfigureDialog()
    {
        Title = GetDialogTitle();
        
        PrimaryButtonText = IsReadOnly
            ? string.Empty
            : _isEditMode
                ? LocalizedStrings.Common_OKButton
                : LocalizedStrings.Common_AddButton;

        CloseButtonText = IsReadOnly
            ? LocalizedStrings.Common_OKButton
            : LocalizedStrings.Common_CancelButton;

        DefaultButton = IsReadOnly ? ContentDialogButton.Close : ContentDialogButton.Primary;

        ComputerNameTextBlock.Text = Environment.MachineName;
        FolderPathBox.PlaceholderText = LocalizedStrings.FsMgmt_FolderPath_Placeholder;
        PermissionsHeaderTextBlock.Text = _isEditMode
            ? LocalizedStrings.FsMgmt_PermissionsSecurity_Header
            : LocalizedStrings.FsMgmt_PermissionsPreset_Label;

        ComputerNamePanel.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
        UserLimitPanel.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
        PermissionsPresetComboBox.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;

        if (_share is null)
        {
            MaximumAllowedRadio.IsChecked = true;
            SetOfflineSelection(ShareOfflineSetting.AutomaticOptimized);
            OptimizeCheckBox.IsChecked = true;
            PermissionsPresetComboBox.SelectedIndex = 0;
        }
        else
        {
            FolderPathBox.Text = _share.Path;
            ShareNameBox.Text = _share.Name;
            DescriptionBox.Text = _share.Description;
            MaximumAllowedRadio.IsChecked = _share.MaxUses == SharedFoldersConstants.UnlimitedUses;
            LimitedUsersRadio.IsChecked = _share.MaxUses != SharedFoldersConstants.UnlimitedUses;
            UserLimitBox.Value = _share.MaxUses == SharedFoldersConstants.UnlimitedUses ? 10 : _share.MaxUses;
            SetOfflineSelection(_share.OfflineSetting);
            OptimizeCheckBox.IsChecked = _share.OptimizeForPerformance;
            PermissionsPresetComboBox.SelectedIndex = 3;
        }

        ConfigureReadOnlyState();
        UpdateDisplayedSharePath();
        UpdateUserLimitState();
        UpdateOfflineState();
        UpdatePermissionsState();
        UpdatePathDependentState();
    }

    private void ConfigureReadOnlyState()
    {
        FolderPathBox.IsReadOnly = _isEditMode || IsReadOnly;
        BrowseButton.IsEnabled = !_isEditMode && !IsReadOnly;
        ShareNameBox.IsReadOnly = _isEditMode || IsReadOnly;

        if (!IsReadOnly)
        {
            return;
        }

        AdminShareInfoBar.IsOpen = true;
        DescriptionBox.IsReadOnly = true;
        MaximumAllowedRadio.IsEnabled = false;
        LimitedUsersRadio.IsEnabled = false;
        UserLimitBox.IsEnabled = false;
        OfflineComboBox.IsEnabled = false;
        OptimizeCheckBox.IsEnabled = false;
        PermissionsPresetComboBox.IsEnabled = false;
        CustomizePermissionsButton.IsEnabled = false;
        PermissionsPanel.Opacity = 0.55;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? selectedPath = await App.GetRequiredService<IFileDialogService>().PickFolderAsync(
            hwnd,
            title: LocalizedStrings.FsMgmt_FolderPath_Label,
            settingsIdentifier: "FsMgmtShareFolderPicker");

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        FolderPathBox.Text = selectedPath;
        if (string.IsNullOrWhiteSpace(ShareNameBox.Text))
        {
            string shareName = Path.GetFileName(
                selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            ShareNameBox.Text = string.IsNullOrWhiteSpace(shareName)
                ? selectedPath.TrimEnd('\\', ':')
                : shareName;
        }

        UpdateDisplayedSharePath();
    }

    private void ShareNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDisplayedSharePath();
    }

    private void FolderPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePathDependentState();
    }

    private void UpdatePathDependentState()
    {
        bool hasPath = !string.IsNullOrWhiteSpace(FolderPathBox.Text);
        SetPermissionsPanelEnabled(hasPath);
        IsPrimaryButtonEnabled = hasPath;
    }

    private void SetPermissionsPanelEnabled(bool enabled)
    {
        foreach (UIElement child in PermissionsPanel.Children)
        {
            if (child is Control control)
            {
                control.IsEnabled = enabled;
            }
        }
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

    private void UserLimitRadio_Checked(object sender, RoutedEventArgs e)
    {
        UpdateUserLimitState();
    }

    private void OfflineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOfflineState();
    }

    private void PermissionsPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePermissionsState();
    }

    private async void CustomizePermissionsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SharePermissionsDialog(
            _securityDescriptorSddl,
            FolderPathBox.Text);

        if (await dialog.ShowDialogAsync(XamlRoot) == WindowDialogResult.Primary)
        {
            _securityDescriptorSddl = dialog.ResultSddl;
            PermissionsPresetComboBox.SelectedIndex = 3;
            UpdatePermissionsState();
        }
    }

    private void ShareEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!IsReadOnly)
        {
            if (!TryCommitDefinition())
            {
                args.Cancel = true;
            }
        }
    }

    private bool TryCommitDefinition()
    {
        ValidationInfoBar.IsOpen = false;

        if (string.IsNullOrWhiteSpace(FolderPathBox.Text))
        {
            ShowValidation(LocalizedStrings.FsMgmt_Validation_FolderPathRequired);
            return false;
        }

        if (!IsValidWindowsPath(FolderPathBox.Text.Trim()))
        {
            ShowValidation(LocalizedStrings.FsMgmt_Validation_FolderPathInvalid);
            return false;
        }

        if (string.IsNullOrWhiteSpace(ShareNameBox.Text))
        {
            ShowValidation(LocalizedStrings.FsMgmt_Validation_ShareNameRequired);
            return false;
        }

        SharePermissionPreset preset = GetSelectedPreset();
        if (preset != SharePermissionPreset.Custom)
        {
            _securityDescriptorSddl = SharedFolderSecurityDescriptor.CreatePresetSddl(preset);
        }

        Definition = new SharedFolderShareDefinition
        {
            Path = FolderPathBox.Text.Trim(),
            Name = ShareNameBox.Text.Trim(),
            Description = DescriptionBox.Text.Trim(),
            MaxUses = !_isEditMode || MaximumAllowedRadio.IsChecked == true
                ? SharedFoldersConstants.UnlimitedUses
                : GetLimitedUserCount(),
            OfflineSetting = GetSelectedOfflineSetting(),
            SecurityDescriptorSddl = _securityDescriptorSddl
        };

        return true;
    }

    private void UpdateDisplayedSharePath()
    {
        string shareName = ShareNameBox.Text.Trim();
        SharePathBox.Text = string.IsNullOrWhiteSpace(shareName)
            ? $"\\\\{Environment.MachineName}\\"
            : $@"\\{Environment.MachineName}\{shareName}";
    }

    private void UpdateUserLimitState()
    {
        UserLimitBox.IsEnabled = !IsReadOnly && _isEditMode && LimitedUsersRadio.IsChecked == true;
    }

    private void UpdateOfflineState()
    {
        bool automatic = GetSelectedTag(OfflineComboBox) == nameof(ShareOfflineSetting.Automatic);
        OptimizeCheckBox.IsEnabled = !IsReadOnly && automatic;
        if (!automatic)
        {
            OptimizeCheckBox.IsChecked = false;
        }
    }

    private void UpdatePermissionsState()
    {
        bool custom = GetSelectedPreset() == SharePermissionPreset.Custom;
        CustomizePermissionsRow.Visibility = custom || IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        CustomizePermissionsButton.IsEnabled = !IsReadOnly && custom;
    }

    private void SetOfflineSelection(ShareOfflineSetting setting)
    {
        string tag = setting switch
        {
            ShareOfflineSetting.None => nameof(ShareOfflineSetting.None),
            ShareOfflineSetting.Automatic or ShareOfflineSetting.AutomaticOptimized => nameof(ShareOfflineSetting.Automatic),
            _ => nameof(ShareOfflineSetting.Manual)
        };

        SetSelectedTag(OfflineComboBox, tag);
    }

    private ShareOfflineSetting GetSelectedOfflineSetting()
    {
        string tag = GetSelectedTag(OfflineComboBox);
        if (tag == nameof(ShareOfflineSetting.None))
        {
            return ShareOfflineSetting.None;
        }

        if (tag == nameof(ShareOfflineSetting.Automatic))
        {
            return OptimizeCheckBox.IsChecked == true
                ? ShareOfflineSetting.AutomaticOptimized
                : ShareOfflineSetting.Automatic;
        }

        return ShareOfflineSetting.Manual;
    }

    private SharePermissionPreset GetSelectedPreset()
    {
        return GetSelectedTag(PermissionsPresetComboBox) switch
        {
            nameof(SharePermissionPreset.AdministratorsFullOthersRead) => SharePermissionPreset.AdministratorsFullOthersRead,
            nameof(SharePermissionPreset.AdministratorsFullOthersNone) => SharePermissionPreset.AdministratorsFullOthersNone,
            nameof(SharePermissionPreset.Custom) => SharePermissionPreset.Custom,
            _ => SharePermissionPreset.EveryoneRead
        };
    }

    private uint GetLimitedUserCount()
    {
        if (double.IsNaN(UserLimitBox.Value) || double.IsInfinity(UserLimitBox.Value))
        {
            return 1;
        }

        double clampedValue = Math.Clamp(UserLimitBox.Value, 1, int.MaxValue);
        return (uint)clampedValue;
    }

    private void ShowValidation(string message)
    {
        ValidationInfoBar.Message = message;
        ValidationInfoBar.IsOpen = true;
    }

    private string GetDialogTitle()
    {
        return _share is null
            ? LocalizedStrings.FsMgmt_AddShare_Title
            : string.Format(LocalizedStrings.FsMgmt_ShareProperties_TitleFormat, _share.Name);
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
