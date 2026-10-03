using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using OneMMC.Helpers;
using OneMMC.Localization;
using CommunityToolkit.WinUI.Controls;
using WinRT.Interop;
using OneMMC.Core.Features.PCManagement.ViewModels.DiskMgmt;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;

namespace OneMMC.Views;

public sealed partial class DiskManagementPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DiskManagementViewModel ViewModel { get; }

    public DiskManagementPage()
    {
        ViewModel = App.GetRequiredService<DiskManagementViewModel>();
        InitializeComponent();
        this.Loaded += DiskManagementPage_Loaded;
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        this.Unloaded += (_, _) =>
        {
            this.Loaded -= DiskManagementPage_Loaded;
            App.ThemeChanged -= OnThemeChanged;
            DataContext = null;
        };
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        this.RequestedTheme = theme;
    }

    private async void DiskManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadDisksAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private void OpenDiskMgmtButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenDiskManagementConsole();
    }

    #region VHD Operations

    private async void CreateVHDButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var dialog = new DiskMgmt.CreateVHDDialog();
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var sizeInBytes = dialog.VHDSizeInMB * 1024 * 1024;
            var createResult = await ViewModel.CreateVHDAsync(
                dialog.VHDPath,
                sizeInBytes,
                isVhdx: true,
                isDynamic: !dialog.IsFixedSize
            );
            
            await ShowResultDialogAsync(
                createResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_VhdCreateFailed,
                createResult.Message
            );
        }
    }

    private async void AttachVHDButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var dialog = new DiskMgmt.AttachVHDDialog();
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var attachResult = await ViewModel.AttachVHDAsync(
                dialog.VHDPath,
                dialog.IsReadOnly
            );
            
            await ShowResultDialogAsync(
                attachResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_VhdAttachFailed,
                attachResult.Message
            );
        }
    }

    private async void DetachVHDButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? selectedPath = null;

        selectedPath = await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            $"{LocalizedStrings.DiskMgmt_FileFilter_Vhd}\0*.vhdx;*.vhd\0{LocalizedStrings.Common_FileFilter_AllFiles}\0*.*\0",
            LocalizedStrings.DiskMgmt_SelectVhdToDetach);

        if (!string.IsNullOrEmpty(selectedPath))
        {
            var result = await ViewModel.DetachVHDAsync(selectedPath);
            if (!result.Success)
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_VhdDetachFailed, result.Message);
            }
            else
            {
                await ShowResultDialogAsync(LocalizedStrings.Common_SuccessTitle, result.Message);
            }
        }
    }

    #endregion

    #region CD-ROM Operations

    private async void EjectCDROMButton_Click(object sender, RoutedEventArgs e)
    {
        CDROMInfo? cdrom = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is CDROMInfo c1)
        {
            cdrom = c1;
        }
        else if (sender is Button button && button.Tag is CDROMInfo c2)
        {
            cdrom = c2;
        }

        if (cdrom != null)
        {
            var result = await ViewModel.EjectCDROMAsync(cdrom.Drive);
            if (!result.Success)
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_EjectFailed, result.Message);
            }
        }
    }

    #endregion

    #region Disk and Partition Operations

    private void DiskPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        PhysicalDiskInfo? disk = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PhysicalDiskInfo d1)
        {
            disk = d1;
        }
        else if (sender is Button button && button.Tag is PhysicalDiskInfo d2)
        {
            disk = d2;
        }

        if (disk != null)
        {
            ShowDiskProperties(disk);
        }
    }

    private void PartitionPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        PartitionInfo? partition = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PartitionInfo p1)
        {
            partition = p1;
        }
        else if (sender is Button button && button.Tag is PartitionInfo p2)
        {
            partition = p2;
        }

        if (partition != null)
        {
            ShowVolumeProperties(partition);
        }
    }

    private void OpenInExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        PartitionInfo? partition = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PartitionInfo p1)
        {
            partition = p1;
        }
        else if (sender is Button button && button.Tag is PartitionInfo p2)
        {
            partition = p2;
        }

        if (partition != null && !string.IsNullOrEmpty(partition.DriveLetter))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = partition.DriveLetter + "\\",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                OneMMC.Services.Logging.UiLogger.LogDebug($"Error opening explorer: {ex.Message}");
            }
        }
    }

    private async void ManageDriveLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var partition = GetPartitionFromSender(sender);
        if (partition == null) return;

        var availableLetters = ViewModel.GetAvailableDriveLetters();
        var letterStrings = availableLetters.Select(c => $"{c}:").ToList();
        var dialog = new DiskMgmt.ManageDriveLetterDialog(partition, letterStrings);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var newLetter = dialog.SelectedDriveLetter;
            if (!string.IsNullOrEmpty(newLetter))
            {
                var manageResult = await ViewModel.ManageDriveLetterAsync(partition, newLetter);
                string failTitle = string.IsNullOrEmpty(partition.DriveLetter)
                    ? LocalizedStrings.DiskMgmt_Result_AssignDriveLetterFailed
                    : LocalizedStrings.DiskMgmt_Result_ChangeDriveLetterFailed;
                await ShowResultDialogAsync(
                    manageResult.Success ? LocalizedStrings.Common_SuccessTitle : failTitle,
                    manageResult.Message
                );
            }
        }
    }

    private async void FormatVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var partition = GetPartitionFromSender(sender);
        if (partition == null) return;

        var dialog = new DiskMgmt.FormatVolumeDialog(partition);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrEmpty(partition.DriveLetter))
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_ErrorTitle, LocalizedStrings.DiskMgmt_Msg_CannotFormatWithoutLetter);
                return;
            }

            var formatResult = await ViewModel.FormatVolumeAsync(
                partition.DriveLetter,
                dialog.FileSystem,
                dialog.VolumeLabel,
                dialog.QuickFormat
            );
            
            await ShowResultDialogAsync(
                formatResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_FormatFailed,
                formatResult.Message
            );
        }
    }

    private void CDROMPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        CDROMInfo? cdrom = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is CDROMInfo c1)
        {
            cdrom = c1;
        }
        else if (sender is Button button && button.Tag is CDROMInfo c2)
        {
            cdrom = c2;
        }

        if (cdrom != null)
        {
            ShowCDROMProperties(cdrom);
        }
    }

    private async void ManageCDROMDriveLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        CDROMInfo? cdrom = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is CDROMInfo c1)
        {
            cdrom = c1;
        }
        else if (sender is Button button && button.Tag is CDROMInfo c2)
        {
            cdrom = c2;
        }

        if (cdrom == null || string.IsNullOrEmpty(cdrom.Drive))
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_ErrorTitle, LocalizedStrings.DiskMgmt_Msg_InvalidCdrom);
            return;
        }

        var dialog = new DiskMgmt.ManageDriveLetterDialog(cdrom.Drive);
        ApplyContentDialogDefaults(dialog);
        
        var dialogResult = await dialog.ShowAsync();
        if (dialogResult == ContentDialogResult.Primary)
        {
            var newLetter = dialog.SelectedDriveLetter;
            if (!string.IsNullOrEmpty(newLetter))
            {
                var result = await ViewModel.ChangeCDROMDriveLetterAsync(cdrom.Drive, newLetter);
                await ShowResultDialogAsync(
                    result.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_ChangeDriveLetterFailed,
                    result.Message);
            }
        }
    }

    #endregion

    #region Properties Dialogs

    private async void ShowDiskProperties(PhysicalDiskInfo disk)
    {
        var dialog = new DiskMgmt.DiskPropertiesDialog(disk);
        ApplyContentDialogDefaults(dialog);
        await dialog.ShowAsync();
    }

    private async void ShowVolumeProperties(PartitionInfo partition)
    {
        var dialog = new DiskMgmt.VolumePropertiesDialog(partition);
        ApplyContentDialogDefaults(dialog);
        await dialog.ShowAsync();
    }

    private async void ShowCDROMProperties(CDROMInfo cdrom)
    {
        var dialog = new DiskMgmt.CDROMPropertiesDialog(cdrom);
        ApplyContentDialogDefaults(dialog);
        await dialog.ShowAsync();
    }

    private void ApplyContentDialogDefaults(ContentDialog dialog)
    {
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.RequestedTheme = App.CurrentTheme;
        dialog.XamlRoot = this.XamlRoot;
    }

    private async System.Threading.Tasks.Task ShowResultDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock 
            { 
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = LocalizedStrings.Common_OKButton
        };
        ApplyContentDialogDefaults(dialog);
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Shows operation result â€” redirects to admin dialog when access denied, otherwise shows normal result.
    /// </summary>
    private async System.Threading.Tasks.Task ShowOperationResultAsync(OperationResult result, string failTitle)
    {
        if (result.IsAccessDenied)
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return;
        }

        await ShowResultDialogAsync(
            result.Success ? LocalizedStrings.Common_SuccessTitle : failTitle,
            result.Message);
    }

    /// <summary>
    /// Pre-flight admin check â€” returns true if admin, false (with dialog) if not.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> EnsureAdminAsync()
    {
        if (App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
            return true;

        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        return false;
    }

    private static string FormatSize(ulong bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion

    #region Disk Operations

    private async void InitializeDiskButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var disk = GetPhysicalDiskFromSender(sender);
        if (disk == null) return;

        var dialog = new DiskMgmt.InitializeDiskDialog(disk);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var initResult = await ViewModel.InitializeDiskAsync(disk.Index, dialog.UseGPT);
            await ShowResultDialogAsync(
                initResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_InitializeFailed,
                initResult.Message
            );
        }
    }



    private async void CreateSimpleVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var disk = GetPhysicalDiskFromSender(sender);
        if (disk == null) return;

        var availableLetters = ViewModel.GetAvailableDriveLetters();
        var letterStrings = availableLetters.Select(c => $"{c}:").ToList();
        var dialog = new DiskMgmt.CreateSimpleVolumeDialog(disk, letterStrings);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var createResult = await ViewModel.CreateSimpleVolumeAsync(
                disk.Index,
                dialog.VolumeSizeInMB,
                dialog.SelectedDriveLetter!,
                dialog.FileSystem,
                dialog.VolumeLabel,
                dialog.QuickFormat
            );
            
            await ShowResultDialogAsync(
                createResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_CreateVolumeFailed,
                createResult.Message
            );
        }
    }

    private async void CreateVolumeOnUnallocated_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var partition = GetPartitionFromSender(sender);
        if (partition == null || !partition.IsUnallocated) return;

        var disk = ViewModel.PhysicalDisks.FirstOrDefault(d => d.Index == partition.DiskIndex);
        if (disk == null) return;

        var availableLetters = ViewModel.GetAvailableDriveLetters();
        var letterStrings = availableLetters.Select(c => $"{c}:").ToList();
        
        var dialog = new DiskMgmt.CreateSimpleVolumeDialog(disk, letterStrings, partition.Size);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var createResult = await ViewModel.CreateSimpleVolumeAsync(
                disk.Index,
                dialog.VolumeSizeInMB,
                dialog.SelectedDriveLetter!,
                dialog.FileSystem,
                dialog.VolumeLabel,
                dialog.QuickFormat,
                partition.StartingOffset
            );
            
            await ShowResultDialogAsync(
                createResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_CreateVolumeFailed,
                createResult.Message
            );
        }
    }

    private async void SetDiskOnlineButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        PhysicalDiskInfo? disk = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PhysicalDiskInfo d1)
        {
            disk = d1;
        }
        else if (sender is Button button && button.Tag is PhysicalDiskInfo d2)
        {
            disk = d2;
        }

        if (disk != null)
        {
            var result = await ViewModel.SetDiskOnlineOfflineAsync(disk.Index, true);
            if (!result.Success)
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_SetOnlineFailed, result.Message);
            }
        }
    }

    private async void SetDiskOfflineButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        PhysicalDiskInfo? disk = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PhysicalDiskInfo d1)
        {
            disk = d1;
        }
        else if (sender is Button button && button.Tag is PhysicalDiskInfo d2)
        {
            disk = d2;
        }

        if (disk != null)
        {
            var result = await ViewModel.SetDiskOnlineOfflineAsync(disk.Index, false);
            if (!result.Success)
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_SetOfflineFailed, result.Message);
            }
        }
    }

    private async void SetDiskReadOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        PhysicalDiskInfo? disk = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PhysicalDiskInfo d1)
        {
            disk = d1;
        }
        else if (sender is Button button && button.Tag is PhysicalDiskInfo d2)
        {
            disk = d2;
        }

        if (disk != null)
        {
            var result = await ViewModel.SetDiskReadOnlyAsync(disk.Index, true);
            if (!result.Success)
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_SetReadOnlyFailed, result.Message);
            }
            else
            {
                await ShowResultDialogAsync(LocalizedStrings.Common_SuccessTitle, result.Message);
            }
        }
    }

    private async void ClearDiskReadOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        PhysicalDiskInfo? disk = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PhysicalDiskInfo d1)
        {
            disk = d1;
        }
        else if (sender is Button button && button.Tag is PhysicalDiskInfo d2)
        {
            disk = d2;
        }

        if (disk != null)
        {
            var result = await ViewModel.SetDiskReadOnlyAsync(disk.Index, false);
            if (!result.Success)
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_ClearReadOnlyFailed, result.Message);
            }
            else
            {
                await ShowResultDialogAsync(LocalizedStrings.Common_SuccessTitle, result.Message);
            }
        }
    }

    private async void CleanDiskButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var disk = GetPhysicalDiskFromSender(sender);
        if (disk == null) return;

        var dialog = new DiskMgmt.CleanDiskDialog(disk);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.IsConfirmed)
        {
            var cleanResult = await ViewModel.CleanDiskAsync(disk.Index);
            await ShowResultDialogAsync(
                cleanResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_CleanFailed,
                cleanResult.Message
            );
        }
    }

    #endregion

    #region Volume Operations - Extended

    private async void DeleteVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var partition = GetPartitionFromSender(sender);
        if (partition == null) return;

        var dialog = new DiskMgmt.DeleteVolumeDialog(partition);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.IsConfirmed)
        {
            var deleteResult = await ViewModel.DeleteVolumeAsync(
                partition.DiskIndex, partition.Index);
            
            await ShowResultDialogAsync(
                deleteResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_DeleteVolumeFailed,
                deleteResult.Message
            );
        }
    }



    private async void ExtendVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var partition = GetPartitionFromSender(sender);
        if (partition == null) return;

        // Check if this is a special partition type that doesn't support resizing
        if (partition.IsMsrPartition || partition.IsEfiSystemPartition ||
            partition.IsRecoveryPartition || partition.IsOemRecoveryPartition)
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_OperationNotSupported, 
                LocalizedStrings.DiskMgmt_Msg_ResizeNotSupported);
            return;
        }

        // Query actual extendable space from WMI
        (bool Success, ulong ExtendableSpaceMB, string Message) queryResult;
        
        if (string.IsNullOrEmpty(partition.DriveLetter))
        {
            // Use index-based query for partitions without drive letter
            queryResult = await ViewModel.QueryExtendableSpaceByIndexAsync(partition.DiskIndex, partition.Index);
        }
        else
        {
            queryResult = await ViewModel.QueryExtendableSpaceAsync(partition.DriveLetter);
        }

        if (!queryResult.Success)
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_QueryFailed, queryResult.Message);
            return;
        }

        if (queryResult.ExtendableSpaceMB == 0)
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_NoSpaceAvailable, 
                LocalizedStrings.DiskMgmt_Msg_NoExtendSpace);
            return;
        }

        var dialog = new DiskMgmt.ExtendVolumeDialog(partition, queryResult.ExtendableSpaceMB);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrEmpty(partition.DriveLetter))
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_OperationNotSupported, 
                    LocalizedStrings.DiskMgmt_Msg_ExtendNeedsLetter);
                return;
            }

            var extendResult = await ViewModel.ExtendVolumeAsync(
                partition.DriveLetter, dialog.ExtendSizeInMB);
            
            await ShowResultDialogAsync(
                extendResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_ExtendFailed,
                extendResult.Message
            );
        }
    }



    private async void ShrinkVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var partition = GetPartitionFromSender(sender);
        if (partition == null) return;

        // Check if this is a special partition type that doesn't support resizing
        if (partition.IsMsrPartition || partition.IsEfiSystemPartition ||
            partition.IsRecoveryPartition || partition.IsOemRecoveryPartition)
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_OperationNotSupported, 
                LocalizedStrings.DiskMgmt_Msg_ResizeNotSupported);
            return;
        }

        // Query actual shrinkable space from WMI
        (bool Success, ulong ShrinkableSpaceMB, string Message) queryResult;
        
        if (string.IsNullOrEmpty(partition.DriveLetter))
        {
            // Use index-based query for partitions without drive letter
            queryResult = await ViewModel.QueryShrinkableSpaceByIndexAsync(partition.DiskIndex, partition.Index);
        }
        else
        {
            queryResult = await ViewModel.QueryShrinkableSpaceAsync(partition.DriveLetter);
        }

        if (!queryResult.Success)
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_QueryFailed, queryResult.Message);
            return;
        }

        if (queryResult.ShrinkableSpaceMB == 0)
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_NoSpaceAvailable, 
                LocalizedStrings.DiskMgmt_Msg_NoShrinkSpace);
            return;
        }

        var dialog = new DiskMgmt.ShrinkVolumeDialog(partition, queryResult.ShrinkableSpaceMB);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (dialog.ShrinkSizeInMB == 0)
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_ErrorTitle, 
                    LocalizedStrings.DiskMgmt_Msg_ShrinkSizeZero);
                return;
            }

            if (string.IsNullOrEmpty(partition.DriveLetter))
            {
                await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_Result_OperationNotSupported, 
                    LocalizedStrings.DiskMgmt_Msg_ShrinkNeedsLetter);
                return;
            }

            var shrinkResult = await ViewModel.ShrinkVolumeAsync(
                partition.DriveLetter, dialog.ShrinkSizeInMB);
            
            await ShowResultDialogAsync(
                shrinkResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_ShrinkFailed,
                shrinkResult.Message
            );
        }
    }




    private async void RemoveDriveLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        PartitionInfo? partition = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PartitionInfo p1)
        {
            partition = p1;
        }
        else if (sender is Button button && button.Tag is PartitionInfo p2)
        {
            partition = p2;
        }

        if (partition == null) return;

        if (string.IsNullOrEmpty(partition.DriveLetter))
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_ErrorTitle, LocalizedStrings.DiskMgmt_Msg_NoDriveLetter);
            return;
        }

        var dialog = new DiskMgmt.RemoveDriveLetterDialog(partition);
        ApplyContentDialogDefaults(dialog);
        
        var dialogResult = await dialog.ShowAsync();
        if (dialogResult == ContentDialogResult.Primary)
        {
            var result = await ViewModel.RemoveDriveLetterAsync(partition.DriveLetter);
            await ShowResultDialogAsync(
                result.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_RemoveDriveLetterFailed,
                result.Message
            );
        }
    }

    private async void MountToFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var partition = GetPartitionFromSender(sender);
        if (partition == null) return;

        if (string.IsNullOrEmpty(partition.DriveLetter))
        {
            await ShowResultDialogAsync(LocalizedStrings.DiskMgmt_ErrorTitle, LocalizedStrings.DiskMgmt_Msg_CannotMountWithoutLetter);
            return;
        }

        var dialog = new DiskMgmt.MountToFolderDialog(partition);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var mountResult = await ViewModel.MountVolumeToFolderAsync(
                partition.DriveLetter, dialog.MountPath);
            
            await ShowResultDialogAsync(
                mountResult.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_MountFailed,
                mountResult.Message
            );
        }
    }



    private async void MarkPartitionActiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        PartitionInfo? partition = null;
        
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PartitionInfo p1)
        {
            partition = p1;
        }
        else if (sender is Button button && button.Tag is PartitionInfo p2)
        {
            partition = p2;
        }

        if (partition == null) return;

        var dialog = new DiskMgmt.MarkPartitionActiveDialog();
        ApplyContentDialogDefaults(dialog);
        
        var dialogResult = await dialog.ShowAsync();
        if (dialogResult == ContentDialogResult.Primary)
        {
            var result = await ViewModel.MarkPartitionActiveAsync(partition.DiskIndex, partition.Index);
            await ShowResultDialogAsync(
                result.Success ? LocalizedStrings.Common_SuccessTitle : LocalizedStrings.DiskMgmt_Result_MarkActiveFailed,
                result.Message
            );
        }
    }

    #endregion

    #region Helper Methods

    private PhysicalDiskInfo? GetPhysicalDiskFromSender(object sender)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PhysicalDiskInfo disk1)
            return disk1;
        if (sender is Button button && button.Tag is PhysicalDiskInfo disk2)
            return disk2;
        return null;
    }

    private PartitionInfo? GetPartitionFromSender(object sender)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is PartitionInfo part1)
            return part1;
        if (sender is Button button && button.Tag is PartitionInfo part2)
            return part2;
        return null;
    }

    #endregion
}



