using System;
using System.Collections.Generic;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PCManagement.Services.DevMgmt;

/// <summary>
/// Maps WMI <c>Win32_PnPEntity.PNPClass</c> values to Device Manager-style display names.
/// Unknown classes keep the raw class name so vendor-specific categories still appear.
/// </summary>
internal static class DeviceCategoryDisplayNames
{
    private static readonly Dictionary<string, string> ResourceKeySuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1394"] = "1394",
        ["1394Debug"] = "1394Debug",
        ["61883"] = "61883",
        ["Adapter"] = "Adapter",
        ["APMSupport"] = "APMSupport",
        ["AudioEndpoint"] = "AudioEndpoint",
        ["AudioProcessingObject"] = "AudioProcessingObject",
        ["AVC"] = "AVC",
        ["Battery"] = "Battery",
        ["Biometric"] = "Biometric",
        ["Bluetooth"] = "Bluetooth",
        ["Camera"] = "Camera",
        ["CDROM"] = "CDROM",
        ["Computer"] = "Computer",
        ["ComputeAccelerator"] = "ComputeAccelerator",
        ["Decoder"] = "Decoder",
        ["DiskDrive"] = "DiskDrive",
        ["Display"] = "Display",
        ["Dot4"] = "Dot4",
        ["Dot4Print"] = "Dot4Print",
        ["Enum1394"] = "Enum1394",
        ["Extension"] = "Extension",
        ["FDC"] = "FDC",
        ["Firmware"] = "Firmware",
        ["FloppyDisk"] = "FloppyDisk",
        ["HDC"] = "HDC",
        ["HIDClass"] = "HIDClass",
        ["Image"] = "Image",
        ["Infrared"] = "Infrared",
        ["Keyboard"] = "Keyboard",
        ["LegacyDriver"] = "LegacyDriver",
        ["Media"] = "Media",
        ["MediumChanger"] = "MediumChanger",
        ["Modem"] = "Modem",
        ["Monitor"] = "Monitor",
        ["Mouse"] = "Mouse",
        ["MTD"] = "MTD",
        ["Multifunction"] = "Multifunction",
        ["MultiportSerial"] = "MultiportSerial",
        ["Net"] = "Net",
        ["NetClient"] = "NetClient",
        ["NetService"] = "NetService",
        ["NetTrans"] = "NetTrans",
        ["NoDriver"] = "NoDriver",
        ["NvmeDisk"] = "NvmeDisk",
        ["PCMCIA"] = "PCMCIA",
        ["PNPPrinters"] = "PNPPrinters",
        ["Ports"] = "Ports",
        ["Printer"] = "Printer",
        ["PrinterUpgrade"] = "PrinterUpgrade",
        ["PrintQueue"] = "PrintQueue",
        ["Processor"] = "Processor",
        ["SBP2"] = "SBP2",
        ["SCSIAdapter"] = "SCSIAdapter",
        ["SDHost"] = "SDHost",
        ["SecurityAccelerator"] = "SecurityAccelerator",
        ["Securitydevices"] = "SecurityDevices",
        ["Sensor"] = "Sensor",
        ["SmartCardReader"] = "SmartCardReader",
        ["SoftwareComponent"] = "SoftwareComponent",
        ["SoftwareDevice"] = "SoftwareDevice",
        ["Sound"] = "Sound",
        ["System"] = "System",
        ["TapeDrive"] = "TapeDrive",
        ["UCM"] = "UCM",
        ["Unknown"] = "Unknown",
        ["USB"] = "USB",
        ["USBDevice"] = "USBDevice",
        ["Volume"] = "Volume",
        ["VolumeSnapshot"] = "VolumeSnapshot",
        ["WCEUSBS"] = "WCEUSBS",
        ["WPD"] = "WPD",
    };

    /// <summary>
    /// Returns the localized Device Manager name for <paramref name="className"/>,
    /// or the original class name when no mapping exists.
    /// </summary>
    public static string GetDisplayName(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return className ?? string.Empty;
        }

        if (!ResourceKeySuffixes.TryGetValue(className, out var suffix))
        {
            return className;
        }

        var key = DeviceManagerKeys.CategoryKeyPrefix + suffix;
        var localized = LocalizationProvider.Current.GetString(ResourceFileNames.DeviceManager, key);

        // Missing resources come back as "[DeviceManager/DeviceManager_Category_Foo]".
        if (string.IsNullOrEmpty(localized) ||
            string.Equals(localized, $"[{ResourceFileNames.DeviceManager}/{key}]", StringComparison.Ordinal))
        {
            return className;
        }

        return localized;
    }
}
