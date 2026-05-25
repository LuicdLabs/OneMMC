using ManagementTools.Core.Localization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ManagementTools.Localization
{
    /// <summary>
    /// Centralized localization service for managing resource strings.
    /// Provides a single point of access for all localized resources.
    /// Supports multiple resource files (resw) organized by feature area.
    /// </summary>
    /// <remarks>
    /// Resource files are organized as follows:
    /// - Resources.resw - App-level strings
    /// - Common.resw - Shared/common strings
    /// - Navigation.resw - Navigation and menu strings
    /// - Settings.resw - Settings page strings
    /// - DeviceManager.resw - Device Manager strings
    /// - DiskManagement.resw - Disk Management strings
    /// - Services.resw - Services page strings
    /// - TPM.resw - TPM Management strings
    /// - Policy.resw - Group Policy strings
    /// - LusrMgr.resw - Local Users and Groups strings
    /// - PerfMon.resw - Performance Monitor strings
    /// - AzMan.resw - Authorization Manager strings
    /// </remarks>
    public sealed class LocalizationService
    {
        private static LocalizationService? _instance;
        private static readonly object _lock = new();

        private readonly ResourceManager _resourceManager;
        private readonly ResourceMap _resourceMap;

        /// <summary>
        /// Gets the singleton instance of the LocalizationService.
        /// </summary>
        public static LocalizationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LocalizationService();
                    }
                }
                return _instance;
            }
        }

        private LocalizationService()
        {
            _resourceManager = new ResourceManager();
            _resourceMap = _resourceManager.MainResourceMap;
        }

        /// <summary>
        /// Gets a localized string from the default Resources file.
        /// </summary>
        /// <param name="key">The resource key without the file prefix.</param>
        /// <returns>The localized string value.</returns>
        public string GetString(string key)
        {
            return GetString(ResourceFileNames.Resources, key);
        }

        /// <summary>
        /// Gets a localized string from a specific resource file.
        /// </summary>
        /// <param name="resourceFile">The resource file name (use <see cref="ResourceFiles"/> constants).</param>
        /// <param name="key">The resource key.</param>
        /// <returns>The localized string value.</returns>
        public string GetString(string resourceFile, string key)
        {
            try
            {
                return _resourceMap.GetValue($"{resourceFile}/{key}").ValueAsString;
            }
            catch
            {
                // Try fallback to main Resources if not found in specific file
                if (resourceFile != ResourceFileNames.Resources)
                {
                    try
                    {
                        return _resourceMap.GetValue($"{ResourceFileNames.Resources}/{key}").ValueAsString;
                    }
                    catch
                    {
                        // Fall through to return error key
                    }
                }
                return $"[{resourceFile}/{key}]";
            }
        }

        /// <summary>
        /// Gets a formatted localized string from the default Resources file.
        /// </summary>
        /// <param name="key">The resource key.</param>
        /// <param name="args">Format arguments.</param>
        /// <returns>The formatted localized string.</returns>
        public string GetFormattedString(string key, params object[] args)
        {
            return GetFormattedString(ResourceFileNames.Resources, key, args);
        }

        /// <summary>
        /// Gets a formatted localized string from a specific resource file.
        /// </summary>
        /// <param name="resourceFile">The resource file name (use <see cref="ResourceFiles"/> constants).</param>
        /// <param name="key">The resource key.</param>
        /// <param name="args">Format arguments.</param>
        /// <returns>The formatted localized string.</returns>
        public string GetFormattedString(string resourceFile, string key, params object[] args)
        {
            var template = GetString(resourceFile, key);
            return string.Format(template, args);
        }

        /// <summary>
        /// Checks if a resource key exists in the default Resources file.
        /// </summary>
        /// <param name="key">The resource key to check.</param>
        /// <returns>True if the key exists, false otherwise.</returns>
        public bool HasKey(string key)
        {
            return HasKey(ResourceFileNames.Resources, key);
        }

        /// <summary>
        /// Checks if a resource key exists in a specific resource file.
        /// </summary>
        /// <param name="resourceFile">The resource file name (use <see cref="ResourceFiles"/> constants).</param>
        /// <param name="key">The resource key to check.</param>
        /// <returns>True if the key exists, false otherwise.</returns>
        public bool HasKey(string resourceFile, string key)
        {
            try
            {
                _ = _resourceMap.GetValue($"{resourceFile}/{key}");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
