using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Core.Features.PCManagement.Services.DiskMgmt.Common;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using CommunityToolkit.WinUI.Controls;
using WinRT.Interop;
using ManagementTools.Core.Features.PCManagement.ViewModels.DiskMgmt;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;

namespace ManagementTools.Views;

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
        this.Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;
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
                createResult.Success ? "Success" : "VHD Creation Failed",
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
                attachResult.Success ? "Success" : "VHD Attach Failed",
                attachResult.Message
            );
        }
    }

    private async void DetachVHDButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAdminAsync()) return;

        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? selectedPath = null;

        selectedPath = await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            "Virtual Hard Disk\0*.vhdx;*.vhd\0All Files\0*.*\0",
            "Select Virtual Hard Disk to Detach");

        if (!string.IsNullOrEmpty(selectedPath))
        {
            var result = await ViewModel.DetachVHDAsync(selectedPath);
            if (!result.Success)
            {
                await ShowResultDialogAsync("Detach VHD Failed", result.Message);
            }
            else
            {
                await ShowResultDialogAsync("Success", result.Message);
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
                await ShowResultDialogAsync("Eject Failed", result.Message);
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
                ManagementTools.Services.Logging.UiLogger.LogDebug($"Error opening explorer: {ex.Message}");
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
                string operationType = string.IsNullOrEmpty(partition.DriveLetter) ? "Assign" : "Change";
                await ShowResultDialogAsync(
                    manageResult.Success ? "Success" : $"{operationType} Drive Letter Failed",
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
                await ShowResultDialogAsync("Error", "Cannot format a partition without a drive letter.");
                return;
            }

            var formatResult = await ViewModel.FormatVolumeAsync(
                partition.DriveLetter,
                dialog.FileSystem,
                dialog.VolumeLabel,
                dialog.QuickFormat
            );
            
            await ShowResultDialogAsync(
                formatResult.Success ? "Success" : "Format Volume Failed",
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
            await ShowResultDialogAsync("Error", "Invalid CD-ROM drive.");
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
                    result.Success ? "Success" : "Change Drive Letter Failed",
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
            CloseButtonText = "OK"
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
            result.Success ? "Success" : failTitle,
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
                initResult.Success ? "Success" : "Initialize Disk Failed",
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
                createResult.Success ? "Success" : "Create Volume Failed",
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
                createResult.Success ? "Success" : "Create Volume Failed",
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
                await ShowResultDialogAsync("Set Online Failed", result.Message);
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
                await ShowResultDialogAsync("Set Offline Failed", result.Message);
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
                await ShowResultDialogAsync("Set Read-Only Failed", result.Message);
            }
            else
            {
                await ShowResultDialogAsync("Success", result.Message);
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
                await ShowResultDialogAsync("Clear Read-Only Failed", result.Message);
            }
            else
            {
                await ShowResultDialogAsync("Success", result.Message);
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
                cleanResult.Success ? "Success" : "Clean Disk Failed",
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
                deleteResult.Success ? "Success" : "Delete Volume Failed",
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
        if (partition.IsMsrPartition || partition.IsEfiSystemPartition || partition.IsRecoveryPartition)
        {
            await ShowResultDialogAsync("Operation Not Supported", 
                "This partition type (System/Reserved/Recovery) does not support resizing operations.");
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
            await ShowResultDialogAsync("Query Failed", queryResult.Message);
            return;
        }

        if (queryResult.ExtendableSpaceMB == 0)
        {
            await ShowResultDialogAsync("No Space Available", 
                "No unallocated space available for extension.");
            return;
        }

        var dialog = new DiskMgmt.ExtendVolumeDialog(partition, queryResult.ExtendableSpaceMB);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrEmpty(partition.DriveLetter))
            {
                await ShowResultDialogAsync("Operation Not Supported", 
                    "Extending partitions without drive letters is not yet supported. Please assign a drive letter first.");
                return;
            }

            var extendResult = await ViewModel.ExtendVolumeAsync(
                partition.DriveLetter, dialog.ExtendSizeInMB);
            
            await ShowResultDialogAsync(
                extendResult.Success ? "Success" : "Extend Volume Failed",
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
        if (partition.IsMsrPartition || partition.IsEfiSystemPartition || partition.IsRecoveryPartition)
        {
            await ShowResultDialogAsync("Operation Not Supported", 
                "This partition type (System/Reserved/Recovery) does not support resizing operations.");
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
            await ShowResultDialogAsync("Query Failed", queryResult.Message);
            return;
        }

        if (queryResult.ShrinkableSpaceMB == 0)
        {
            await ShowResultDialogAsync("No Space Available", 
                "No shrinkable space available. This may be due to unmovable files.");
            return;
        }

        var dialog = new DiskMgmt.ShrinkVolumeDialog(partition, queryResult.ShrinkableSpaceMB);
        ApplyContentDialogDefaults(dialog);
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (dialog.ShrinkSizeInMB == 0)
            {
                await ShowResultDialogAsync("Error", 
                    "Shrink size must be greater than 0.");
                return;
            }

            if (string.IsNullOrEmpty(partition.DriveLetter))
            {
                await ShowResultDialogAsync("Operation Not Supported", 
                    "Shrinking partitions without drive letters is not yet supported. Please assign a drive letter first.");
                return;
            }

            var shrinkResult = await ViewModel.ShrinkVolumeAsync(
                partition.DriveLetter, dialog.ShrinkSizeInMB);
            
            await ShowResultDialogAsync(
                shrinkResult.Success ? "Success" : "Shrink Volume Failed",
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
            await ShowResultDialogAsync("Error", "This partition does not have a drive letter.");
            return;
        }

        var dialog = new DiskMgmt.RemoveDriveLetterDialog(partition);
        ApplyContentDialogDefaults(dialog);
        
        var dialogResult = await dialog.ShowAsync();
        if (dialogResult == ContentDialogResult.Primary)
        {
            var result = await ViewModel.RemoveDriveLetterAsync(partition.DriveLetter);
            await ShowResultDialogAsync(
                result.Success ? "Success" : "Remove Drive Letter Failed",
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
            await ShowResultDialogAsync("Error", "Cannot mount a partition without a drive letter.");
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
                mountResult.Success ? "Success" : "Mount to Folder Failed",
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
                result.Success ? "Success" : "Mark Active Failed",
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



