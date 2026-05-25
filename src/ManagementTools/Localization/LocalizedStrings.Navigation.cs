using ManagementTools.Core.Localization;
namespace ManagementTools.Localization
{
    /// <summary>
    /// Localized strings for Navigation and Menu items.
    /// Resources are loaded from Navigation.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // NavigationViewItem Content
        public string Navigation_PC_Management => GetResource(ResourceFileNames.Navigation, "Navigation_PC_Management");
        public string Navigation_Policy_Policies => GetResource(ResourceFileNames.Navigation, "Navigation_Policy_Policies");
        public string Navigation_Certificates_Credential => GetResource(ResourceFileNames.Navigation, "Navigation_Certificates_Credential");
        public string Navigation_User_Security => GetResource(ResourceFileNames.Navigation, "Navigation_User_Security");
        public string Navigation_Print_Management => GetResource(ResourceFileNames.Navigation, "Navigation_Print_Management");
        public string Navigation_System_Management => GetResource(ResourceFileNames.Navigation, "Navigation_System_Management");
        public string Navigation_Settings => GetResource(ResourceFileNames.Navigation, "Navigation_Settings");

        // Page Titles
        public string PageTitle_CurrentUserCertificates => GetResource(ResourceFileNames.Navigation, "PageTitle_CurrentUserCertificates");
        public string PageTitle_LocalCertificates => GetResource(ResourceFileNames.Navigation, "PageTitle_LocalCertificates");
        public string PageTitle_DeviceManager => GetResource(ResourceFileNames.Navigation, "PageTitle_DeviceManager");
        public string PageTitle_DiskManagement => GetResource(ResourceFileNames.Navigation, "PageTitle_DiskManagement");
        public string PageTitle_EventViewer => GetResource(ResourceFileNames.Navigation, "PageTitle_EventViewer");
        public string PageTitle_LocalUsersGroups => GetResource(ResourceFileNames.Navigation, "PageTitle_LocalUsersGroups");
        public string PageTitle_PerformanceMonitor => GetResource(ResourceFileNames.Navigation, "PageTitle_PerformanceMonitor");
        public string PageTitle_Services => GetResource(ResourceFileNames.Navigation, "PageTitle_Services");
        public string PageTitle_SharedFolders => GetResource(ResourceFileNames.Navigation, "PageTitle_SharedFolders");
        public string PageTitle_GroupPolicyEditor => GetResource(ResourceFileNames.Navigation, "PageTitle_GroupPolicyEditor");
        public string PageTitle_ResultantSetOfPolicy => GetResource(ResourceFileNames.Navigation, "PageTitle_ResultantSetOfPolicy");
        public string PageTitle_PrintManagement => GetResource(ResourceFileNames.Navigation, "PageTitle_PrintManagement");
        public string PageTitle_ComponentServices => GetResource(ResourceFileNames.Navigation, "PageTitle_ComponentServices");
        public string PageTitle_HyperVManager => GetResource(ResourceFileNames.Navigation, "PageTitle_HyperVManager");
        public string PageTitle_TPMManagement => GetResource(ResourceFileNames.Navigation, "PageTitle_TPMManagement");
        public string PageTitle_WindowsFirewall => GetResource(ResourceFileNames.Navigation, "PageTitle_WindowsFirewall");
        public string PageTitle_AuthorizationManager => GetResource(ResourceFileNames.Navigation, "PageTitle_AuthorizationManager");
        public string PageTitle_LocalSecurityPolicy => GetResource(ResourceFileNames.Navigation, "PageTitle_LocalSecurityPolicy");
        public string PageTitle_TaskScheduler => GetResource(ResourceFileNames.Navigation, "PageTitle_TaskScheduler");
    }
}
