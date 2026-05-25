using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;
using ManagementTools.Core.Features.PCManagement.Services.DiskMgmt;

namespace ManagementTools.Core.Features.PCManagement.ViewModels.DiskMgmt
{
    public class DiskManagementViewModel : INotifyPropertyChanged
    {
        private readonly DiskManagementService _diskService;
        private ObservableCollection<PhysicalDiskInfo> _physicalDisks;
        private ObservableCollection<CDROMInfo> _cdromDrives;
        private ObservableCollection<IDiskItem> _allDiskItems;
        private ObservableCollection<VolumeInfo> _volumes;
        private ObservableCollection<StoragePoolInfo> _storagePools;
        private PhysicalDiskInfo? _selectedDisk;
        private PartitionInfo? _selectedPartition;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private int _driveCount;
        private int _totalPartitions;
        private ulong _totalCapacity;
        private ulong _totalFreeSpace;

        public DiskManagementViewModel(DiskManagementService diskService)
        {
            _diskService = diskService;
            _physicalDisks = new ObservableCollection<PhysicalDiskInfo>();
            _cdromDrives = new ObservableCollection<CDROMInfo>();
            _allDiskItems = new ObservableCollection<IDiskItem>();
            _volumes = new ObservableCollection<VolumeInfo>();
            _storagePools = new ObservableCollection<StoragePoolInfo>();
        }

        public ObservableCollection<PhysicalDiskInfo> PhysicalDisks
        {
            get => _physicalDisks;
            set
            {
                _physicalDisks = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<CDROMInfo> CDROMDrives
        {
            get => _cdromDrives;
            set
            {
                _cdromDrives = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<IDiskItem> AllDiskItems
        {
            get => _allDiskItems;
            set
            {
                _allDiskItems = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<VolumeInfo> Volumes
        {
            get => _volumes;
            set
            {
                _volumes = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<StoragePoolInfo> StoragePools
        {
            get => _storagePools;
            set
            {
                _storagePools = value;
                OnPropertyChanged();
            }
        }

        public PhysicalDiskInfo? SelectedDisk
        {
            get => _selectedDisk;
            set
            {
                _selectedDisk = value;
                OnPropertyChanged();
            }
        }

        public PartitionInfo? SelectedPartition
        {
            get => _selectedPartition;
            set
            {
                _selectedPartition = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public int DriveCount
        {
            get => _driveCount;
            set
            {
                _driveCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DriveCountText));
            }
        }

        public int TotalPartitions
        {
            get => _totalPartitions;
            set
            {
                _totalPartitions = value;
                OnPropertyChanged();
            }
        }

        public ulong TotalCapacity
        {
            get => _totalCapacity;
            set
            {
                _totalCapacity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalCapacityText));
            }
        }

        public ulong TotalFreeSpace
        {
            get => _totalFreeSpace;
            set
            {
                _totalFreeSpace = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalFreeSpaceText));
            }
        }

        public string DriveCountText => DriveCount == 1 ? "1 drive" : $"{DriveCount} drives";
        public string TotalCapacityText => FormatSize(TotalCapacity);
        public string TotalFreeSpaceText => FormatSize(TotalFreeSpace);

        /// <summary>
        /// Load all disk information
        /// </summary>
        public async Task LoadDisksAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading disk information...";

            try
            {
                var disks = await Task.Run(() => _diskService.GetPhysicalDisks());
                var cdroms = await Task.Run(() => _diskService.GetCDROMDrives());
                var volumes = await Task.Run(() => _diskService.GetVolumes());
                var pools = await Task.Run(() => _diskService.GetStoragePools());

                PhysicalDisks = new ObservableCollection<PhysicalDiskInfo>(disks);
                CDROMDrives = new ObservableCollection<CDROMInfo>(cdroms);
                Volumes = new ObservableCollection<VolumeInfo>(volumes);
                StoragePools = new ObservableCollection<StoragePoolInfo>(pools);

                // Build unified disk items list (physical disks first, then CD-ROMs)
                var allItems = new List<IDiskItem>();
                allItems.AddRange(disks);
                allItems.AddRange(cdroms);
                AllDiskItems = new ObservableCollection<IDiskItem>(allItems);

                DriveCount = disks.Count + cdroms.Count;
                TotalPartitions = disks.Sum(d => d.PartitionInfos.Count(p => !p.IsUnallocated));
                
                // Use Aggregate to avoid overflow when summing ulong values
                TotalCapacity = disks.Aggregate(0UL, (sum, d) => sum + d.Size);
                TotalFreeSpace = volumes.Aggregate(0UL, (sum, v) => sum + v.FreeSpace);

                var poolText = pools.Count > 0 ? $", {pools.Count} storage pool(s)" : "";
                StatusMessage = $"Loaded {disks.Count} disk(s), {TotalPartitions} partition(s), {cdroms.Count} CD/DVD drive(s){poolText}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading disks: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Refresh disk information
        /// </summary>
        public async Task RefreshAsync()
        {
            IsLoading = true;
            StatusMessage = "Refreshing...";

            try
            {
                await Task.Run(() => _diskService.RescanDisks());
                await LoadDisksAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error refreshing: {ex.Message}";
                IsLoading = false;
            }
        }

        /// <summary>
        /// Create a new VHD/VHDX file
        /// </summary>
        public async Task<(bool Success, string Message)> CreateVHDAsync(string path, ulong sizeInBytes, bool isVhdx = true, bool isDynamic = true)
        {
            IsLoading = true;
            StatusMessage = "Creating virtual hard disk...";

            try
            {
                var result = await Task.Run(() => _diskService.CreateVHD(path, sizeInBytes, isVhdx, isDynamic));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error creating VHD: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Attach (mount) a VHD/VHDX file
        /// </summary>
        public async Task<(bool Success, string Message)> AttachVHDAsync(string path, bool readOnly = false)
        {
            IsLoading = true;
            StatusMessage = "Attaching virtual hard disk...";

            try
            {
                var result = await Task.Run(() => _diskService.AttachVHD(path, readOnly));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error attaching VHD: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Detach (unmount) a VHD/VHDX file
        /// </summary>
        public async Task<(bool Success, string Message)> DetachVHDAsync(string path)
        {
            IsLoading = true;
            StatusMessage = "Detaching virtual hard disk...";

            try
            {
                var result = await Task.Run(() => _diskService.DetachVHD(path));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error detaching VHD: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Eject CD-ROM
        /// </summary>
        public async Task<(bool Success, string Message)> EjectCDROMAsync(string driveLetter)
        {
            IsLoading = true;
            StatusMessage = "Ejecting media...";

            try
            {
                var result = await Task.Run(() => _diskService.EjectCDROM(driveLetter));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error ejecting media: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<(bool Success, string Message)> ChangeCDROMDriveLetterAsync(string currentDriveLetter, string newDriveLetter)
        {
            IsLoading = true;
            StatusMessage = "Changing drive letter...";

            try
            {
                var result = await Task.Run(() => _diskService.ChangeCDROMDriveLetter(currentDriveLetter, newDriveLetter));

                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<(bool Success, string Message)> RemoveCDROMDriveLetterAsync(string driveLetter)
        {
            IsLoading = true;
            StatusMessage = "Removing drive letter...";

            try
            {
                var result = await Task.Run(() => _diskService.RemoveCDROMDriveLetter(driveLetter));

                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<(bool Success, string Message)> AssignCDROMDriveLetterAsync(string currentDriveLetter, string newDriveLetter)
        {
            IsLoading = true;
            StatusMessage = "Assigning drive letter...";

            try
            {
                var result = await Task.Run(() => _diskService.AssignCDROMDriveLetter(currentDriveLetter, newDriveLetter));

                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }


        /// <summary>
        /// Load CD-ROM (close tray)
        /// </summary>
        public async Task<(bool Success, string Message)> LoadCDROMAsync(string driveLetter)
        {
            IsLoading = true;
            StatusMessage = "Loading media...";

            try
            {
                var result = await Task.Run(() => _diskService.LoadCDROM(driveLetter));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error loading media: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Open the system Disk Management console
        /// </summary>
        public void OpenDiskManagementConsole()
        {
            try
            {
                _diskService.OpenDiskManagementConsole();
                StatusMessage = "Disk Management console opened";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening Disk Management: {ex.Message}";
            }
        }

        /// <summary>
        /// Get volume properties for a drive letter
        /// </summary>
        public VolumeProperties? GetVolumeProperties(string driveLetter)
        {
            return _diskService.GetVolumeProperties(driveLetter);
        }

        /// <summary>
        /// Get partition style for a disk
        /// </summary>
        public string GetDiskPartitionStyle(uint diskIndex)
        {
            return _diskService.GetDiskPartitionStyle(diskIndex);
        }

        /// <summary>
        /// Check if disk is online
        /// </summary>
        public bool IsDiskOnline(uint diskIndex)
        {
            return _diskService.IsDiskOnline(diskIndex);
        }

        /// <summary>
        /// Set disk online/offline
        /// </summary>
        public async Task<(bool Success, string Message)> SetDiskOnlineOfflineAsync(uint diskIndex, bool online)
        {
            IsLoading = true;
            StatusMessage = online ? "Bringing disk online..." : "Taking disk offline...";

            try
            {
                var result = await Task.Run(() => _diskService.SetDiskOnlineOffline(diskIndex, online));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Format a volume (requires elevation)
        /// </summary>
        public async Task<(bool Success, string Message)> FormatVolumeAsync(string driveLetter, string fileSystem, string label, bool quickFormat)
        {
            IsLoading = true;
            StatusMessage = "Formatting volume...";

            try
            {
                var result = await Task.Run(() => _diskService.FormatVolume(driveLetter, fileSystem, label, quickFormat));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error formatting volume: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Change drive letter
        /// </summary>
        public async Task<(bool Success, string Message)> ChangeDriveLetterAsync(string currentDriveLetter, string newDriveLetter)
        {
            IsLoading = true;
            StatusMessage = "Changing drive letter...";

            try
            {
                var result = await Task.Run(() => _diskService.ChangeDriveLetter(currentDriveLetter, newDriveLetter));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error changing drive letter: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }



        /// <summary>
        /// Initialize a disk
        /// </summary>
        public async Task<(bool Success, string Message)> InitializeDiskAsync(uint diskIndex, bool useGPT = true)
        {
            IsLoading = true;
            StatusMessage = $"Initializing disk {diskIndex}...";

            try
            {
                var result = await Task.Run(() => _diskService.InitializeDisk(diskIndex, useGPT));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error initializing disk: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Create a simple volume
        /// </summary>
        public async Task<(bool Success, string Message)> CreateSimpleVolumeAsync(uint diskIndex, ulong sizeInMB = 0, string? driveLetter = null, string fileSystem = "NTFS", string label = "", bool quickFormat = true, ulong? offset = null)
        {
            IsLoading = true;
            StatusMessage = "Creating volume...";

            try
            {
                var result = await Task.Run(() => _diskService.CreateSimpleVolume(diskIndex, sizeInMB, driveLetter, fileSystem, label, quickFormat, offset));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error creating volume: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Delete a volume
        /// </summary>
        public async Task<(bool Success, string Message)> DeleteVolumeAsync(uint diskIndex, uint partitionIndex)
        {
            IsLoading = true;
            StatusMessage = "Deleting volume...";

            try
            {
                var result = await Task.Run(() => _diskService.DeleteVolume(diskIndex, partitionIndex));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error deleting volume: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Extend a volume
        /// </summary>
        public async Task<(bool Success, string Message)> ExtendVolumeAsync(string driveLetter, ulong sizeInMB = 0)
        {
            IsLoading = true;
            StatusMessage = "Extending volume...";

            try
            {
                var result = await Task.Run(() => _diskService.ExtendVolume(driveLetter, sizeInMB));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error extending volume: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Shrink a volume
        /// </summary>
        public async Task<(bool Success, string Message)> ShrinkVolumeAsync(string driveLetter, ulong sizeInMB)
        {
            IsLoading = true;
            StatusMessage = "Shrinking volume...";

            try
            {
                var result = await Task.Run(() => _diskService.ShrinkVolume(driveLetter, sizeInMB));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error shrinking volume: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Query shrinkable space
        /// </summary>
        public async Task<(bool Success, ulong ShrinkableSpaceMB, string Message)> QueryShrinkableSpaceAsync(string driveLetter)
        {
            try
            {
                var result = await Task.Run(() => _diskService.QueryShrinkableSpace(driveLetter));
                return (result.Success, result.Value, result.Message);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>
        /// Query shrinkable space by disk and partition index (for partitions without drive letter)
        /// </summary>
        public async Task<(bool Success, ulong ShrinkableSpaceMB, string Message)> QueryShrinkableSpaceByIndexAsync(uint diskIndex, uint partitionIndex)
        {
            try
            {
                var result = await Task.Run(() => _diskService.QueryShrinkableSpaceByIndex(diskIndex, partitionIndex));
                return (result.Success, result.Value, result.Message);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>
        /// Query extendable space
        /// </summary>
        public async Task<(bool Success, ulong ExtendableSpaceMB, string Message)> QueryExtendableSpaceAsync(string driveLetter)
        {
            try
            {
                var result = await Task.Run(() => _diskService.QueryExtendableSpace(driveLetter));
                return (result.Success, result.Value, result.Message);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>
        /// Query extendable space by disk and partition index (for partitions without drive letter)
        /// </summary>
        public async Task<(bool Success, ulong ExtendableSpaceMB, string Message)> QueryExtendableSpaceByIndexAsync(uint diskIndex, uint partitionIndex)
        {
            try
            {
                var result = await Task.Run(() => _diskService.QueryExtendableSpaceByIndex(diskIndex, partitionIndex));
                return (result.Success, result.Value, result.Message);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>
        /// Remove drive letter by disk and partition index
        /// </summary>
        public async Task<(bool Success, string Message)> RemoveDriveLetterByIndexAsync(uint diskIndex, uint partitionIndex)
        {
            IsLoading = true;
            StatusMessage = "Removing drive letter...";

            try
            {
                var result = await Task.Run(() => _diskService.RemoveDriveLetterByIndex(diskIndex, partitionIndex));
                
                if (result.Success)
                {
                    await RefreshAsync();
                }
                
                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                IsLoading = false;
                StatusMessage = string.Empty;
            }
        }

        /// <summary>
        /// Mark partition as active
        /// </summary>
        public async Task<(bool Success, string Message)> MarkPartitionActiveAsync(uint diskIndex, uint partitionIndex)
        {
            IsLoading = true;
            StatusMessage = "Marking partition as active...";

            try
            {
                var result = await Task.Run(() => _diskService.MarkPartitionActive(diskIndex, partitionIndex));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error marking partition as active: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Assign drive letter
        /// </summary>
        public async Task<(bool Success, string Message)> AssignDriveLetterAsync(uint diskIndex, uint partitionIndex, string driveLetter)
        {
            IsLoading = true;
            StatusMessage = "Assigning drive letter...";

            try
            {
                var result = await Task.Run(() => _diskService.AssignDriveLetter(diskIndex, partitionIndex, driveLetter));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error assigning drive letter: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Remove drive letter
        /// </summary>
        public async Task<(bool Success, string Message)> RemoveDriveLetterAsync(string driveLetter)
        {
            IsLoading = true;
            StatusMessage = "Removing drive letter...";

            try
            {
                var result = await Task.Run(() => _diskService.RemoveDriveLetter(driveLetter));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error removing drive letter: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Manage drive letter (unified method for assign/change)
        /// </summary>
        public async Task<(bool Success, string Message)> ManageDriveLetterAsync(
            PartitionInfo partition, 
            string newDriveLetter)
        {
            IsLoading = true;
            
            try
            {
                if (string.IsNullOrEmpty(newDriveLetter))
                {
                    return (false, "Drive letter cannot be empty.");
                }

                // Assign or change drive letter
                if (string.IsNullOrEmpty(partition.DriveLetter))
                {
                    // Assign new drive letter
                    StatusMessage = "Assigning drive letter...";
                    var result = await Task.Run(() => _diskService.AssignDriveLetter(partition.DiskIndex, partition.Index, newDriveLetter));
                    
                    if (result.Success)
                    {
                        StatusMessage = result.Message;
                        await RefreshAsync();
                    }
                    else
                    {
                        StatusMessage = $"Failed: {result.Message}";
                    }
                    
                    return (result.Success, result.Message);
                }
                else
                {
                    // Change existing drive letter
                    StatusMessage = "Changing drive letter...";
                    var result = await Task.Run(() => _diskService.ChangeDriveLetter(partition.DriveLetter, newDriveLetter));
                    
                    if (result.Success)
                    {
                        StatusMessage = result.Message;
                        await RefreshAsync();
                    }
                    else
                    {
                        StatusMessage = $"Failed: {result.Message}";
                    }
                    
                    return (result.Success, result.Message);
                }
            }
            catch (Exception ex)
            {
                var message = $"Error managing drive letter: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Mount volume to folder
        /// </summary>
        public async Task<(bool Success, string Message)> MountVolumeToFolderAsync(string driveLetter, string folderPath)
        {
            IsLoading = true;
            StatusMessage = "Mounting volume to folder...";

            try
            {
                var result = await Task.Run(() => _diskService.MountVolumeToFolder(driveLetter, folderPath));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error mounting volume: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Clean disk
        /// </summary>
        public async Task<(bool Success, string Message)> CleanDiskAsync(uint diskIndex)
        {
            IsLoading = true;
            StatusMessage = "Cleaning disk...";

            try
            {
                var result = await Task.Run(() => _diskService.CleanDisk(diskIndex));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error cleaning disk: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Set disk read-only
        /// </summary>
        public async Task<(bool Success, string Message)> SetDiskReadOnlyAsync(uint diskIndex, bool readOnly)
        {
            IsLoading = true;
            StatusMessage = readOnly ? "Setting disk as read-only..." : "Clearing read-only attribute...";

            try
            {
                var result = await Task.Run(() => _diskService.SetDiskReadOnly(diskIndex, readOnly));
                
                if (result.Success)
                {
                    StatusMessage = result.Message;
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.Message}";
                }

                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var message = $"Error: {ex.Message}";
                StatusMessage = message;
                return (false, message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Get available drive letters
        /// </summary>
        public List<char> GetAvailableDriveLetters()
        {
            return _diskService.GetAvailableDriveLetters();
        }

        /// <summary>
        /// Get unallocated space for a disk
        /// </summary>
        public List<UnallocatedSpace> GetUnallocatedSpace(uint diskIndex)
        {
            return _diskService.GetUnallocatedSpace(diskIndex);
        }

        /// <summary>
        /// Check if disk needs initialization
        /// </summary>
        public bool DiskNeedsInitialization(uint diskIndex)
        {
            return _diskService.DiskNeedsInitialization(diskIndex);
        }

        /// <summary>
        /// Check if disk is read-only
        /// </summary>
        public bool IsDiskReadOnly(uint diskIndex)
        {
            return _diskService.IsDiskReadOnly(diskIndex);
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


