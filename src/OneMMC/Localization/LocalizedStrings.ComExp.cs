using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for Component Services (ComExp) feature.
    /// Resources are loaded from ComExp.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Component Services Page
        public string ComExp_PageTitle => GetResource(ResourceFileNames.ComExp, "ComExp_PageTitle");
        public string ComExp_CurrentPC => GetResource(ResourceFileNames.ComExp, "ComExp_CurrentPC");
        public string ComExp_OpenLegacy => GetResource(ResourceFileNames.ComExp, "ComExp_OpenLegacy");

        // Sections
        public string ComExp_ComPlusDcom => GetResource(ResourceFileNames.ComExp, "ComExp_ComPlusDcom");
        public string ComExp_DistributedTransactionCoordinator => GetResource(ResourceFileNames.ComExp, "ComExp_DistributedTransactionCoordinator");

        // COM+ Applications
        public string ComExp_ComPlusApplications => GetResource(ResourceFileNames.ComExp, "ComExp_ComPlusApplications");
        public string ComExp_ComPlusApplications_Description => GetResource(ResourceFileNames.ComExp, "ComExp_ComPlusApplications_Description");
        public string ComExp_Application_Name => GetResource(ResourceFileNames.ComExp, "ComExp_Application_Name");
        public string ComExp_Application_Id => GetResource(ResourceFileNames.ComExp, "ComExp_Application_Id");
        public string ComExp_Application_Description => GetResource(ResourceFileNames.ComExp, "ComExp_Application_Description");
        public string ComExp_Application_Activation => GetResource(ResourceFileNames.ComExp, "ComExp_Application_Activation");
        public string ComExp_Application_AuthenticationLevel => GetResource(ResourceFileNames.ComExp, "ComExp_Application_AuthenticationLevel");

        // Activation Types
        public string ComExp_Activation_Library => GetResource(ResourceFileNames.ComExp, "ComExp_Activation_Library");
        public string ComExp_Activation_Server => GetResource(ResourceFileNames.ComExp, "ComExp_Activation_Server");

        // Authentication Levels
        public string ComExp_Auth_None => GetResource(ResourceFileNames.ComExp, "ComExp_Auth_None");
        public string ComExp_Auth_Connect => GetResource(ResourceFileNames.ComExp, "ComExp_Auth_Connect");
        public string ComExp_Auth_Call => GetResource(ResourceFileNames.ComExp, "ComExp_Auth_Call");
        public string ComExp_Auth_Packet => GetResource(ResourceFileNames.ComExp, "ComExp_Auth_Packet");
        public string ComExp_Auth_PacketIntegrity => GetResource(ResourceFileNames.ComExp, "ComExp_Auth_PacketIntegrity");
        public string ComExp_Auth_PacketPrivacy => GetResource(ResourceFileNames.ComExp, "ComExp_Auth_PacketPrivacy");

        // DCOM Config
        public string ComExp_DcomConfig => GetResource(ResourceFileNames.ComExp, "ComExp_DcomConfig");
        public string ComExp_DcomConfig_Description => GetResource(ResourceFileNames.ComExp, "ComExp_DcomConfig_Description");
        public string ComExp_Dcom_AppId => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_AppId");
        public string ComExp_Dcom_LocalService => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_LocalService");
        public string ComExp_Dcom_RunAs => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_RunAs");

        // Running Processes
        public string ComExp_RunningProcesses => GetResource(ResourceFileNames.ComExp, "ComExp_RunningProcesses");
        public string ComExp_RunningProcesses_Description => GetResource(ResourceFileNames.ComExp, "ComExp_RunningProcesses_Description");
        public string ComExp_Process_Id => GetResource(ResourceFileNames.ComExp, "ComExp_Process_Id");
        public string ComExp_Process_Name => GetResource(ResourceFileNames.ComExp, "ComExp_Process_Name");
        public string ComExp_Process_Description => GetResource(ResourceFileNames.ComExp, "ComExp_Process_Description");
        public string ComExp_Process_FilePath => GetResource(ResourceFileNames.ComExp, "ComExp_Process_FilePath");
        public string ComExp_Process_StartTime => GetResource(ResourceFileNames.ComExp, "ComExp_Process_StartTime");
        public string ComExp_Process_Executable => GetResource(ResourceFileNames.ComExp, "ComExp_Process_Executable");
        public string ComExp_Process_IsPaused => GetResource(ResourceFileNames.ComExp, "ComExp_Process_IsPaused");
        public string ComExp_Process_IsRecycling => GetResource(ResourceFileNames.ComExp, "ComExp_Process_IsRecycling");
        public string ComExp_Process_IsNTService => GetResource(ResourceFileNames.ComExp, "ComExp_Process_IsNTService");

        // DTC (Distributed Transaction Coordinator)
        public string ComExp_LocalDtc => GetResource(ResourceFileNames.ComExp, "ComExp_LocalDtc");
        public string ComExp_Dtc_TransactionList => GetResource(ResourceFileNames.ComExp, "ComExp_Dtc_TransactionList");
        public string ComExp_Dtc_TransactionStatistics => GetResource(ResourceFileNames.ComExp, "ComExp_Dtc_TransactionStatistics");
        public string ComExp_Dtc_Status => GetResource(ResourceFileNames.ComExp, "ComExp_Dtc_Status");
        public string ComExp_Dtc_ProcessId => GetResource(ResourceFileNames.ComExp, "ComExp_Dtc_ProcessId");
        public string ComExp_Dtc_StartTime => GetResource(ResourceFileNames.ComExp, "ComExp_Dtc_StartTime");

        // DTC Transaction List
        public string ComExp_Transaction_Status => GetResource(ResourceFileNames.ComExp, "ComExp_Transaction_Status");
        public string ComExp_Transaction_UnitOfWorkId => GetResource(ResourceFileNames.ComExp, "ComExp_Transaction_UnitOfWorkId");

        // DTC Statistics
        public string ComExp_Stats_Open => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_Open");
        public string ComExp_Stats_OpenMax => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_OpenMax");
        public string ComExp_Stats_InDoubt => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_InDoubt");
        public string ComExp_Stats_Committed => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_Committed");
        public string ComExp_Stats_Aborted => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_Aborted");
        public string ComExp_Stats_ForcedCommit => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_ForcedCommit");
        public string ComExp_Stats_ForcedAbort => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_ForcedAbort");
        public string ComExp_Stats_Heuristic => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_Heuristic");
        public string ComExp_Stats_ResponseTimeMin => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_ResponseTimeMin");
        public string ComExp_Stats_ResponseTimeAvg => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_ResponseTimeAvg");
        public string ComExp_Stats_ResponseTimeMax => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_ResponseTimeMax");

        // Status Messages
        public string ComExp_Loading => GetResource(ResourceFileNames.ComExp, "ComExp_Loading");
        public string ComExp_LoadingApplications => GetResource(ResourceFileNames.ComExp, "ComExp_LoadingApplications");
        public string ComExp_LoadingDcomApps => GetResource(ResourceFileNames.ComExp, "ComExp_LoadingDcomApps");
        public string ComExp_LoadingProcesses => GetResource(ResourceFileNames.ComExp, "ComExp_LoadingProcesses");
        public string ComExp_LoadingStatistics => GetResource(ResourceFileNames.ComExp, "ComExp_LoadingStatistics");
        public string ComExp_LoadedSuccess => GetResource(ResourceFileNames.ComExp, "ComExp_LoadedSuccess");
        public string ComExp_LoadedCount => GetResource(ResourceFileNames.ComExp, "ComExp_LoadedCount");
        public string ComExp_LoadFailed => GetResource(ResourceFileNames.ComExp, "ComExp_LoadFailed");
        public string ComExp_NoDataAvailable => GetResource(ResourceFileNames.ComExp, "ComExp_NoDataAvailable");

        // Actions
        public string ComExp_ViewButton => GetResource(ResourceFileNames.ComExp, "ComExp_ViewButton");
        public string ComExp_PropertiesButton => GetResource(ResourceFileNames.ComExp, "ComExp_PropertiesButton");
        public string ComExp_RefreshButton => GetResource(ResourceFileNames.ComExp, "ComExp_RefreshButton");

        // Common Values
        public string ComExp_Yes => GetResource(ResourceFileNames.ComExp, "ComExp_Yes");
        public string ComExp_No => GetResource(ResourceFileNames.ComExp, "ComExp_No");

        // Navigation Breadcrumbs
        public string ComExp_Breadcrumb_DcomConfig => GetResource(ResourceFileNames.ComExp, "ComExp_Breadcrumb_DcomConfig");
        public string ComExp_Breadcrumb_RunningProcesses => GetResource(ResourceFileNames.ComExp, "ComExp_Breadcrumb_RunningProcesses");
        public string ComExp_Breadcrumb_TransactionList => GetResource(ResourceFileNames.ComExp, "ComExp_Breadcrumb_TransactionList");
        public string ComExp_Breadcrumb_TransactionStatistics => GetResource(ResourceFileNames.ComExp, "ComExp_Breadcrumb_TransactionStatistics");

        // Application Details Dialog
        public string ComExp_Dialog_ApplicationDetails => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_ApplicationDetails");
        public string ComExp_Dialog_Close => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_Close");
        public string ComExp_Dialog_Name => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_Name");
        public string ComExp_Dialog_ID => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_ID");
        public string ComExp_Dialog_Description => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_Description");
        public string ComExp_Dialog_Activation => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_Activation");
        public string ComExp_Dialog_Authentication => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_Authentication");
        public string ComExp_Dialog_AccessChecks => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_AccessChecks");
        public string ComExp_Dialog_Identity => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_Identity");
        public string ComExp_Dialog_NotSet => GetResource(ResourceFileNames.ComExp, "ComExp_Dialog_NotSet");

        // Application Count
        public string ComExp_ApplicationsCount => GetResource(ResourceFileNames.ComExp, "ComExp_ApplicationsCount");

        // DTC Statistics Sections
        public string ComExp_Stats_Current => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_Current");
        public string ComExp_Stats_Aggregate => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_Aggregate");
        public string ComExp_Stats_Total => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_Total");
        public string ComExp_Stats_ResponseTimes => GetResource(ResourceFileNames.ComExp, "ComExp_Stats_ResponseTimes");

        // Format Strings
        public string ComExp_Format_NoId => GetResource(ResourceFileNames.ComExp, "ComExp_Format_NoId");
        public string ComExp_Format_Unknown => GetResource(ResourceFileNames.ComExp, "ComExp_Format_Unknown");
        public string ComExp_Format_Default => GetResource(ResourceFileNames.ComExp, "ComExp_Format_Default");

        // DCOM Properties
        public string ComExp_Dcom_DllSurrogate => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_DllSurrogate");
        public string ComExp_Dcom_ServiceParameters => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_ServiceParameters");

        // DCOM Config list + detail (General / Location / Security / Endpoints / Identity)
        public string ComExp_Dcom_SearchPlaceholder => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_SearchPlaceholder");
        public string ComExp_Dcom_Tab_General => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Tab_General");
        public string ComExp_Dcom_Tab_Location => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Tab_Location");
        public string ComExp_Dcom_Tab_Security => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Tab_Security");
        public string ComExp_Dcom_Tab_Endpoints => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Tab_Endpoints");
        public string ComExp_Dcom_Tab_Identity => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Tab_Identity");
        public string ComExp_Dcom_ApplicationName => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_ApplicationName");
        public string ComExp_Dcom_ApplicationType => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_ApplicationType");
        public string ComExp_Dcom_AuthenticationLevel => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_AuthenticationLevel");
        public string ComExp_Dcom_LocalPath => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_LocalPath");
        public string ComExp_Dcom_Type_LocalServer => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Type_LocalServer");
        public string ComExp_Dcom_Type_LocalService => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Type_LocalService");
        public string ComExp_Dcom_Type_Surrogate => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Type_Surrogate");
        public string ComExp_Dcom_Identity => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Identity");
        public string ComExp_Dcom_Identity_Interactive => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Identity_Interactive");
        public string ComExp_Dcom_Identity_Launching => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Identity_Launching");
        public string ComExp_Dcom_Identity_ThisUser => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_Identity_ThisUser");
        public string ComExp_Dcom_RunOnThisComputer => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_RunOnThisComputer");
        public string ComExp_Dcom_RunOnFollowingComputer => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_RunOnFollowingComputer");
        public string ComExp_Dcom_RemoteComputerName => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_RemoteComputerName");
        public string ComExp_Dcom_LaunchPermissions => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_LaunchPermissions");
        public string ComExp_Dcom_AccessPermissions => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_AccessPermissions");
        public string ComExp_Dcom_UseDefault => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_UseDefault");
        public string ComExp_Dcom_UseCustom => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_UseCustom");
        public string ComExp_Dcom_EndpointsEmpty => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_EndpointsEmpty");
        public string ComExp_Dcom_NoSelection => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_NoSelection");
        public string ComExp_Dcom_NoResults => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_NoResults");
        public string ComExp_Dcom_ReadOnlyNote => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_ReadOnlyNote");
        public string ComExp_Dcom_NotSet => GetResource(ResourceFileNames.ComExp, "ComExp_Dcom_NotSet");

        // Running Processes tree + detail
        public string ComExp_Run_SummaryFormat => GetResource(ResourceFileNames.ComExp, "ComExp_Run_SummaryFormat");
        public string ComExp_Run_NoProcesses => GetResource(ResourceFileNames.ComExp, "ComExp_Run_NoProcesses");
        public string ComExp_Run_SelectPrompt => GetResource(ResourceFileNames.ComExp, "ComExp_Run_SelectPrompt");
        public string ComExp_Run_PartitionId => GetResource(ResourceFileNames.ComExp, "ComExp_Run_PartitionId");
        public string ComExp_Run_ApplicationId => GetResource(ResourceFileNames.ComExp, "ComExp_Run_ApplicationId");
        public string ComExp_Run_InstanceId => GetResource(ResourceFileNames.ComExp, "ComExp_Run_InstanceId");
        public string ComExp_Run_Type => GetResource(ResourceFileNames.ComExp, "ComExp_Run_Type");
        public string ComExp_Run_Clsid => GetResource(ResourceFileNames.ComExp, "ComExp_Run_Clsid");
        public string ComExp_Run_ProgId => GetResource(ResourceFileNames.ComExp, "ComExp_Run_ProgId");
        public string ComExp_Run_Dll => GetResource(ResourceFileNames.ComExp, "ComExp_Run_Dll");
    }
}
