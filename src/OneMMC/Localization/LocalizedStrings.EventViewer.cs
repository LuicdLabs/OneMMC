using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for Event Viewer page.
    /// Resources are loaded from EventViewer.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Tree
        public string EventViewer_Tree_WindowsLogs => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tree_WindowsLogs");
        public string EventViewer_Tree_AppServicesLogs => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tree_AppServicesLogs");
        public string EventViewer_Tree_Application => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tree_Application");
        public string EventViewer_Tree_Security => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tree_Security");
        public string EventViewer_Tree_Setup => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tree_Setup");
        public string EventViewer_Tree_System => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tree_System");
        public string EventViewer_Tree_ForwardedEvents => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tree_ForwardedEvents");

        // Levels
        public string EventViewer_Level_Critical => GetResource(ResourceFileNames.EventViewer, "EventViewer_Level_Critical");
        public string EventViewer_Level_Error => GetResource(ResourceFileNames.EventViewer, "EventViewer_Level_Error");
        public string EventViewer_Level_Warning => GetResource(ResourceFileNames.EventViewer, "EventViewer_Level_Warning");
        public string EventViewer_Level_Information => GetResource(ResourceFileNames.EventViewer, "EventViewer_Level_Information");
        public string EventViewer_Level_Verbose => GetResource(ResourceFileNames.EventViewer, "EventViewer_Level_Verbose");

        // Details panel
        public string EventViewer_Tab_General => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tab_General");
        public string EventViewer_Tab_Details => GetResource(ResourceFileNames.EventViewer, "EventViewer_Tab_Details");
        public string EventViewer_Detail_LogName => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_LogName");
        public string EventViewer_Detail_Source => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_Source");
        public string EventViewer_Detail_EventId => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_EventId");
        public string EventViewer_Detail_Level => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_Level");
        public string EventViewer_Detail_User => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_User");
        public string EventViewer_Detail_OpCode => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_OpCode");
        public string EventViewer_Detail_Logged => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_Logged");
        public string EventViewer_Detail_TaskCategory => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_TaskCategory");
        public string EventViewer_Detail_Keywords => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_Keywords");
        public string EventViewer_Detail_Computer => GetResource(ResourceFileNames.EventViewer, "EventViewer_Detail_Computer");

        // Filter
        public string EventViewer_Filter_All => GetResource(ResourceFileNames.EventViewer, "EventViewer_Filter_All");
        public string EventViewer_SearchPlaceholder => GetResource(ResourceFileNames.EventViewer, "EventViewer_SearchPlaceholder");

        // Commands / Actions
        public string EventViewer_ClearLog => GetResource(ResourceFileNames.EventViewer, "EventViewer_ClearLog");
        public string EventViewer_ExportLog => GetResource(ResourceFileNames.EventViewer, "EventViewer_ExportLog");
        public string EventViewer_LogProperties => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProperties");
        public string EventViewer_OpenLegacy => GetResource(ResourceFileNames.EventViewer, "EventViewer_OpenLegacy");

        // Status
        public string EventViewer_Status_Loading => GetResource(ResourceFileNames.EventViewer, "EventViewer_Status_Loading");
        public string EventViewer_Status_NoEvents => GetResource(ResourceFileNames.EventViewer, "EventViewer_Status_NoEvents");

        // Clear log dialog
        public string EventViewer_ClearLog_ConfirmTitle => GetResource(ResourceFileNames.EventViewer, "EventViewer_ClearLog_ConfirmTitle");
        public string EventViewer_ClearLog_ConfirmMessage => GetResource(ResourceFileNames.EventViewer, "EventViewer_ClearLog_ConfirmMessage");
        public string EventViewer_ClearLog_SaveFirst => GetResource(ResourceFileNames.EventViewer, "EventViewer_ClearLog_SaveFirst");

        // Log Properties dialog
        public string EventViewer_LogProp_Title => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProp_Title");
        public string EventViewer_LogProp_FullName => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProp_FullName");
        public string EventViewer_LogProp_LogPath => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProp_LogPath");
        public string EventViewer_LogProp_LogSize => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProp_LogSize");
        public string EventViewer_LogProp_MaxSize => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProp_MaxSize");
        public string EventViewer_LogProp_Enabled => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProp_Enabled");
        public string EventViewer_LogProp_LogMode => GetResource(ResourceFileNames.EventViewer, "EventViewer_LogProp_LogMode");

        // Load more
        public string EventViewer_LoadMore => GetResource(ResourceFileNames.EventViewer, "EventViewer_LoadMore");
    }
}
