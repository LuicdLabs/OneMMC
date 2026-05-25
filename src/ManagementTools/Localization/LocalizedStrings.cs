using ManagementTools.Core.Localization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ManagementTools.Localization
{
    /// <summary>
    /// Base class for localized strings providing common functionality.
    /// This partial class serves as the entry point for all localized strings.
    /// The class is split into multiple partial files organized by feature area.
    /// </summary>
    /// <remarks>
    /// File organization:
    /// - LocalizedStrings.cs - Base class and common strings
    /// - LocalizedStrings.Navigation.cs - Navigation and menu strings
    /// - LocalizedStrings.Settings.cs - Settings page strings
    /// - LocalizedStrings.DeviceManager.cs - Device Manager strings
    /// - LocalizedStrings.DiskManagement.cs - Disk Management strings
    /// - LocalizedStrings.Services.cs - Services page strings
    /// - LocalizedStrings.TPM.cs - TPM Management strings
    /// - LocalizedStrings.Policy.cs - Group Policy strings
    /// - LocalizedStrings.LusrMgr.cs - Local Users and Groups strings
    /// - LocalizedStrings.PerfMon.cs - Performance Monitor strings
    /// - LocalizedStrings.AzMan.cs - Authorization Manager strings
    /// - LocalizedStrings.Common.cs - Common/shared strings
    /// 
    /// Each partial class file corresponds to a .resw resource file:
    /// - Strings/[locale]/Resources.resw - App-level strings
    /// - Strings/[locale]/Navigation.resw - Navigation strings
    /// - Strings/[locale]/DiskManagement.resw - Disk management strings
    /// - etc.
    /// </remarks>
    public partial class LocalizedStrings
    {
        /// <summary>
        /// Shared singleton instance. Because every property is a computed getter backed by
        /// static resource lookups, a single instance can serve the entire application.
        /// </summary>
        public static LocalizedStrings Instance { get; } = new();

        private static readonly ResourceManager resourceManager = new();

        /// <summary>
        /// Gets a string resource value by key from the default Resources file.
        /// </summary>
        /// <param name="key">The resource key.</param>
        /// <returns>The localized string value.</returns>
        protected static string GetResource(string key) =>
            LocalizationService.Instance.GetString(ResourceFileNames.Resources, key);

        /// <summary>
        /// Gets a string resource value by key from a specific resource file.
        /// </summary>
        /// <param name="resourceFile">The resource file name (use <see cref="ResourceFiles"/> constants).</param>
        /// <param name="key">The resource key.</param>
        /// <returns>The localized string value.</returns>
        protected static string GetResource(string resourceFile, string key) =>
            LocalizationService.Instance.GetString(resourceFile, key);

        // App-level strings
        public string AppTitle => GetResource("AppTitle");
        public string TextBlock_WelcomeMessage => GetResource("TextBlock_WelcomeMessage");

        // App-level error strings
        public string App_Error_Title => GetResource("App_Error_Title");
        public string App_Error_MessageFormat => GetResource("App_Error_MessageFormat");
    }
}
