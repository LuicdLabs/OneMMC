using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for Performance Monitor.
    /// Resources are loaded from PerfMon.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Performance Monitor Strings
        public string PerfMon_ViewMode_Graph => GetResource(ResourceFileNames.PerfMon, "PerfMon_ViewMode_Graph");
        public string PerfMon_ViewMode_Histogram => GetResource(ResourceFileNames.PerfMon, "PerfMon_ViewMode_Histogram");
        public string PerfMon_ViewMode_Report => GetResource(ResourceFileNames.PerfMon, "PerfMon_ViewMode_Report");
        public string PerfMon_File_SaveConfiguration => GetResource(ResourceFileNames.PerfMon, "PerfMon_File_SaveConfiguration");
        public string PerfMon_File_LoadConfiguration => GetResource(ResourceFileNames.PerfMon, "PerfMon_File_LoadConfiguration");

        public string PerfMon_Report_Generated => GetResource(ResourceFileNames.PerfMon, "PerfMon_Report_Generated");
        public string PerfMon_Report_Title => GetResource(ResourceFileNames.PerfMon, "PerfMon_Report_Title");
        public string PerfMon_CountersText => GetResource(ResourceFileNames.PerfMon, "PerfMon_CountersText");
        public string PerfMon_MonitorsText => GetResource(ResourceFileNames.PerfMon, "PerfMon_MonitorsText");
        public string PerfMon_HideAll => GetResource(ResourceFileNames.PerfMon, "PerfMon_HideAll");
        public string PerfMon_ShowAll => GetResource(ResourceFileNames.PerfMon, "PerfMon_ShowAll");
        public string PerfMon_Properties => GetResource(ResourceFileNames.PerfMon, "PerfMon_Properties");
        public string PerfMon_EnterName => GetResource(ResourceFileNames.PerfMon, "PerfMon_EnterName");
        public string PerfMon_NameLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_NameLabel");

        public string PerfMon_CsvFilesFilter => GetResource(ResourceFileNames.PerfMon, "PerfMon_CsvFilesFilter");
        public string PerfMon_CsvExtension => GetResource(ResourceFileNames.PerfMon, "PerfMon_CsvExtension");
        public string PerfMon_PmcfgFilesFilter => GetResource(ResourceFileNames.PerfMon, "PerfMon_PmcfgFilesFilter");
        public string PerfMon_PmcfgConfigurationFilter => GetResource(ResourceFileNames.PerfMon, "PerfMon_PmcfgConfigurationFilter");
        public string PerfMon_PmcfgExtension => GetResource(ResourceFileNames.PerfMon, "PerfMon_PmcfgExtension");

        public string PerfMon_AddCounters => GetResource(ResourceFileNames.PerfMon, "PerfMon_AddCounters");
        public string PerfMon_SelectCategory => GetResource(ResourceFileNames.PerfMon, "PerfMon_SelectCategory");
        public string PerfMon_SelectInstance => GetResource(ResourceFileNames.PerfMon, "PerfMon_SelectInstance");
        public string PerfMon_InstanceLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_InstanceLabel");
        public string PerfMon_CounterProperties => GetResource(ResourceFileNames.PerfMon, "PerfMon_CounterProperties");
        public string PerfMon_CounterLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_CounterLabel");
        public string PerfMon_ColorLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_ColorLabel");
        public string PerfMon_ConfigurationSaved => GetResource(ResourceFileNames.PerfMon, "PerfMon_ConfigurationSaved");
        public string PerfMon_ConfigurationLoaded => GetResource(ResourceFileNames.PerfMon, "PerfMon_ConfigurationLoaded");


        // Performance Monitor Statistics Labels
        public string PerfMon_LastLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_LastLabel");
        public string PerfMon_AverageLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_AverageLabel");
        public string PerfMon_MinimumLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_MinimumLabel");
        public string PerfMon_MaximumLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_MaximumLabel");
        public string PerfMon_DurationLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_DurationLabel");
        public string PerfMon_CurrentLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_CurrentLabel");

        // Performance Monitor Report Labels
        public string PerfMon_MonitoringDurationLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_MonitoringDurationLabel");
        public string PerfMon_ActiveCountersLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_ActiveCountersLabel");
        public string PerfMon_DataPointsLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_DataPointsLabel");
        public string PerfMon_CounterStatisticsLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_CounterStatisticsLabel");

        // Performance Monitor Empty State Messages
        public string PerfMon_NoDataToReport => GetResource(ResourceFileNames.PerfMon, "PerfMon_NoDataToReport");
        public string PerfMon_AddCountersToGenerateReport => GetResource(ResourceFileNames.PerfMon, "PerfMon_AddCountersToGenerateReport");

        // Performance Monitor Monitor List Messages
        public string PerfMon_NoCountersAdded => GetResource(ResourceFileNames.PerfMon, "PerfMon_NoCountersAdded");
        public string PerfMon_ClickAddToStartMonitoring => GetResource(ResourceFileNames.PerfMon, "PerfMon_ClickAddToStartMonitoring");

        // Performance Monitor Section Headers
        public string PerfMon_QuickCountersHeader => GetResource(ResourceFileNames.PerfMon, "PerfMon_QuickCountersHeader");
        public string PerfMon_MonitorListHeader => GetResource(ResourceFileNames.PerfMon, "PerfMon_MonitorListHeader");

        // Performance Monitor Dialogs
        public string PerfMon_AddCountersTitle => GetResource(ResourceFileNames.PerfMon, "PerfMon_AddCountersTitle");
        public string PerfMon_CounterPropertiesTitle => GetResource(ResourceFileNames.PerfMon, "PerfMon_CounterPropertiesTitle");
        public string PerfMon_CounterUnavailableTitle => GetResource(ResourceFileNames.PerfMon, "PerfMon_CounterUnavailableTitle");
        public string PerfMon_CounterUnavailableMessage => GetResource(ResourceFileNames.PerfMon, "PerfMon_CounterUnavailableMessage");

        // Performance Monitor UI Labels
        public string PerfMon_UpdateInterval => GetResource(ResourceFileNames.PerfMon, "PerfMon_UpdateInterval");
        public string PerfMon_OneSecond => GetResource(ResourceFileNames.PerfMon, "PerfMon_OneSecond");
        public string PerfMon_TwoSeconds => GetResource(ResourceFileNames.PerfMon, "PerfMon_TwoSeconds");
        public string PerfMon_FiveSeconds => GetResource(ResourceFileNames.PerfMon, "PerfMon_FiveSeconds");
        public string PerfMon_TenSeconds => GetResource(ResourceFileNames.PerfMon, "PerfMon_TenSeconds");
        public string PerfMon_YAxisTopLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_YAxisTopLabel");
        public string PerfMon_YAxisMidLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_YAxisMidLabel");
        public string PerfMon_YAxisBottomLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_YAxisBottomLabel");

        public string PerfMon_SearchCounters => GetResource(ResourceFileNames.PerfMon, "PerfMon_SearchCounters");
        public string PerfMon_CommonCountersDescription => GetResource(ResourceFileNames.PerfMon, "PerfMon_CommonCountersDescription");
        public string PerfMon_SelectCategoryLabel => GetResource(ResourceFileNames.PerfMon, "PerfMon_SelectCategoryLabel");
        public string PerfMon_Pause => GetResource(ResourceFileNames.PerfMon, "PerfMon_Pause");
        public string PerfMon_Resume => GetResource(ResourceFileNames.PerfMon, "PerfMon_Resume");
    }
}
