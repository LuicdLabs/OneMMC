using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for Services page.
    /// Resources are loaded from Services.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Service Details Dialog Strings
        public string Service_General => GetResource(ResourceFileNames.Services, "Service_General");
        public string Service_LogOn => GetResource(ResourceFileNames.Services, "Service_LogOn");
        public string Service_Recovery => GetResource(ResourceFileNames.Services, "Service_Recovery");
        public string Service_Dependencies => GetResource(ResourceFileNames.Services, "Service_Dependencies");
        public string Service_Name => GetResource(ResourceFileNames.Services, "Service_Name");
        public string Service_DisplayName => GetResource(ResourceFileNames.Services, "Service_DisplayName");
        public string Service_Description => GetResource(ResourceFileNames.Services, "Service_Description");
        public string Service_Status => GetResource(ResourceFileNames.Services, "Service_Status");
        public string Service_StartupType => GetResource(ResourceFileNames.Services, "Service_StartupType");
        public string Service_LogOn_Account => GetResource(ResourceFileNames.Services, "Service_LogOn_Account");
        public string Service_Recovery_Options => GetResource(ResourceFileNames.Services, "Service_Recovery_Options");

        // Services page UI strings
        public string Services_SearchPlaceholder => GetResource(ResourceFileNames.Services, "Services_SearchPlaceholder");
        public string Services_Start => GetResource(ResourceFileNames.Services, "Services_Start");
        public string Services_Stop => GetResource(ResourceFileNames.Services, "Services_Stop");
        public string Services_Restart => GetResource(ResourceFileNames.Services, "Services_Restart");
        public string Services_Properties => GetResource(ResourceFileNames.Services, "Services_Properties");

        // Services Details dialog strings
        public string Service_AccountName_Header => GetResource(ResourceFileNames.Services, "Service_AccountName_Header");
        public string Service_Password_Header => GetResource(ResourceFileNames.Services, "Service_Password_Header");
        public string Service_Account_Placeholder => GetResource(ResourceFileNames.Services, "Service_Account_Placeholder");
        public string Service_Password_Placeholder => GetResource(ResourceFileNames.Services, "Service_Password_Placeholder");
        public string Service_SaveStartupType => GetResource(ResourceFileNames.Services, "Service_SaveStartupType");
        public string Service_ApplyLogOn => GetResource(ResourceFileNames.Services, "Service_ApplyLogOn");
        public string Service_ApplyRecovery => GetResource(ResourceFileNames.Services, "Service_ApplyRecovery");
        public string Service_Startup_Auto => GetResource(ResourceFileNames.Services, "Service_Startup_Auto");
        public string Service_Startup_AutoDelayed => GetResource(ResourceFileNames.Services, "Service_Startup_AutoDelayed");
        public string Service_Startup_Manual => GetResource(ResourceFileNames.Services, "Service_Startup_Manual");
        public string Service_Recovery_SelectResponse => GetResource(ResourceFileNames.Services, "Service_Recovery_SelectResponse");
        public string Service_Recovery_FirstFailure => GetResource(ResourceFileNames.Services, "Service_Recovery_FirstFailure");
        public string Service_Recovery_SecondFailure => GetResource(ResourceFileNames.Services, "Service_Recovery_SecondFailure");
        public string Service_Recovery_SubsequentFailure => GetResource(ResourceFileNames.Services, "Service_Recovery_SubsequentFailure");
        public string Service_Recovery_TakeNoAction => GetResource(ResourceFileNames.Services, "Service_Recovery_TakeNoAction");
        public string Service_Recovery_RestartService => GetResource(ResourceFileNames.Services, "Service_Recovery_RestartService");
        public string Service_Recovery_RunProgram => GetResource(ResourceFileNames.Services, "Service_Recovery_RunProgram");
        public string Service_Recovery_RestartComputer => GetResource(ResourceFileNames.Services, "Service_Recovery_RestartComputer");
        public string Service_Recovery_ResetFailCountHeader => GetResource(ResourceFileNames.Services, "Service_Recovery_ResetFailCountHeader");
        public string Service_Dependencies_DependsOn => GetResource(ResourceFileNames.Services, "Service_Dependencies_DependsOn");
        public string Service_Dependencies_FollowingCannotRunText => GetResource(ResourceFileNames.Services, "Service_Dependencies_FollowingCannotRunText");
        public string Service_Dependents_DependsOn => GetResource(ResourceFileNames.Services, "Service_Dependents_DependsOn");
        public string Service_Dependents_FollowingCannotRunText => GetResource(ResourceFileNames.Services, "Service_Dependents_FollowingCannotRunText");
        public string Service_LogOn_LocalSystem => GetResource(ResourceFileNames.Services, "Service_LogOn_LocalSystem");
        public string Service_LogOn_ThisAccount => GetResource(ResourceFileNames.Services, "Service_LogOn_ThisAccount");
        public string Service_Info_LogonChangeMessage => GetResource(ResourceFileNames.Services, "Service_Info_LogonChangeMessage");
        public string Service_NoDescription => GetResource(ResourceFileNames.Services, "Service_NoDescription");
        public string Service_Status_Unknown => GetResource(ResourceFileNames.Services, "Service_Status_Unknown");
        public string Service_Status_Running => GetResource(ResourceFileNames.Services, "Service_Status_Running");
        public string Service_Status_Stopped => GetResource(ResourceFileNames.Services, "Service_Status_Stopped");
        public string Service_Status_Paused => GetResource(ResourceFileNames.Services, "Service_Status_Paused");
        public string Service_Status_StartPending => GetResource(ResourceFileNames.Services, "Service_Status_StartPending");
        public string Service_Status_StopPending => GetResource(ResourceFileNames.Services, "Service_Status_StopPending");
        public string Service_Status_ContinuePending => GetResource(ResourceFileNames.Services, "Service_Status_ContinuePending");
        public string Service_Status_PausePending => GetResource(ResourceFileNames.Services, "Service_Status_PausePending");
    }
}
