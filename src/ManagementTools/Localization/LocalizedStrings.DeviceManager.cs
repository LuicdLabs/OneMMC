using ManagementTools.Core.Localization;
namespace ManagementTools.Localization
{
    /// <summary>
    /// Localized strings for Device Manager.
    /// Resources are loaded from DeviceManager.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Device Manager Strings
        public string DeviceManager_SearchPlaceholder => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_SearchPlaceholder");
        public string DeviceManager_RefreshTooltip => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_RefreshTooltip");
        public string DeviceManager_OpenSystemDeviceManager => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_OpenSystemDeviceManager");
        public string DeviceManager_ShowHiddenDevices => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_ShowHiddenDevices");
        public string DeviceManager_DeviceCountPrefix => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceCountPrefix");
        public string DeviceManager_DeviceCountSuffix => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceCountSuffix");
        public string DeviceManager_EnableDevice => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_EnableDevice");
        public string DeviceManager_DisableDevice => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DisableDevice");
        public string DeviceManager_UninstallDevice => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_UninstallDevice");
        public string DeviceManager_GeneralInformation => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_GeneralInformation");
        public string DeviceManager_DeviceName => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceName");
        public string DeviceManager_DeviceType => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceType");
        public string DeviceManager_Manufacturer => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_Manufacturer");
        public string DeviceManager_Status => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_Status");
        public string DeviceManager_DeviceID => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceID");
        public string DeviceManager_DeviceIDDescription => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceIDDescription");
        
        public string DeviceManager_DriverInformation => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DriverInformation");
        public string DeviceManager_DriverVersion => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DriverVersion");
        public string DeviceManager_DriverDate => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DriverDate");
        public string DeviceManager_DriverProvider => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DriverProvider");
        public string DeviceManager_INFName => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_INFName");
        public string DeviceManager_DigitalSignature => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DigitalSignature");
        public string DeviceManager_Signer => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_Signer");
        public string DeviceManager_HardwareIDs => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_HardwareIDs");
        public string DeviceManager_HardwareID => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_HardwareID");
        public string DeviceManager_CompatibleID => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_CompatibleID");
        
        public string DeviceManager_HiddenDevices => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_HiddenDevices");
        public string DeviceManager_ConfirmDisableDeviceTitle => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_ConfirmDisableDeviceTitle");
        public string DeviceManager_ConfirmDisableDeviceContent => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_ConfirmDisableDeviceContent");
        public string DeviceManager_DisableButton => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DisableButton");
        public string DeviceManager_ConfirmUninstallDeviceTitle => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_ConfirmUninstallDeviceTitle");
        public string DeviceManager_ConfirmUninstallDeviceContent => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_ConfirmUninstallDeviceContent");
        public string DeviceManager_UninstallButton => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_UninstallButton");
        public string DeviceManager_EnableDeviceError => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_EnableDeviceError");
        public string DeviceManager_DisableDeviceError => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DisableDeviceError");
        public string DeviceManager_UninstallDeviceError => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_UninstallDeviceError");
        public string DeviceManager_DevicePropertiesError => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DevicePropertiesError");
        

        // Device Manager Status Strings
        public string DeviceManager_StatusWorking => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusWorking");
        public string DeviceManager_StatusConfigError => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusConfigError");
        public string DeviceManager_StatusDriverCorrupt => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDriverCorrupt");
        public string DeviceManager_StatusCannotStart => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusCannotStart");
        public string DeviceManager_StatusNoResources => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusNoResources");
        public string DeviceManager_StatusRestartRequired => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusRestartRequired");
        public string DeviceManager_StatusReinstallDriver => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusReinstallDriver");
        public string DeviceManager_StatusRegistryCorrupt => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusRegistryCorrupt");
        public string DeviceManager_StatusRemoving => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusRemoving");
        public string DeviceManager_StatusDisabled => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDisabled");
        public string DeviceManager_StatusNotPresent => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusNotPresent");
        public string DeviceManager_StatusNoDriver => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusNoDriver");
        public string DeviceManager_StatusDisabledFirmware => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDisabledFirmware");
        public string DeviceManager_StatusCannotLoadDriver => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusCannotLoadDriver");
        public string DeviceManager_StatusDriverDisabled => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDriverDisabled");
        public string DeviceManager_StatusCannotDetermineResources => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusCannotDetermineResources");
        public string DeviceManager_StatusCannotDetermineConfig => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusCannotDetermineConfig");
        public string DeviceManager_StatusInsufficientFirmware => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusInsufficientFirmware");
        public string DeviceManager_StatusInterruptConflict => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusInterruptConflict");
        public string DeviceManager_StatusCannotInitializeDriver => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusCannotInitializeDriver");
        public string DeviceManager_StatusDriverInMemory => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDriverInMemory");
        public string DeviceManager_StatusDriverCorruptOrMissing => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDriverCorruptOrMissing");
        public string DeviceManager_StatusRegistryMissing => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusRegistryMissing");
        public string DeviceManager_StatusHardwareNotFound => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusHardwareNotFound");
        public string DeviceManager_StatusDuplicateDevice => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDuplicateDevice");
        public string DeviceManager_StatusDeviceReportedProblem => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusDeviceReportedProblem");
        public string DeviceManager_StatusApplicationClosed => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusApplicationClosed");
        public string DeviceManager_StatusNotConnected => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusNotConnected");
        public string DeviceManager_StatusSystemShutdown => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusSystemShutdown");
        public string DeviceManager_StatusSafeRemoval => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusSafeRemoval");
        public string DeviceManager_StatusSoftwareBlocked => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusSoftwareBlocked");
        public string DeviceManager_StatusRegistryTooBig => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusRegistryTooBig");
        public string DeviceManager_StatusInvalidSignature => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusInvalidSignature");
        public string DeviceManager_StatusUnknownError => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_StatusUnknownError");
        public string DeviceManager_Unknown => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_Unknown");
        public string DeviceManager_NoDeviceSelected => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_NoDeviceSelected");
        public string DeviceManager_BooleanYes => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_BooleanYes");
        public string DeviceManager_BooleanNo => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_BooleanNo");
        public string DeviceManager_DeviceCountFormat => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceCountFormat");
        public string DeviceManager_DeviceCountZero => GetResource(ResourceFileNames.DeviceManager, "DeviceManager_DeviceCountZero");
    }
}
