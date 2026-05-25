using ManagementTools.Core.Localization;
namespace ManagementTools.Localization
{
    /// <summary>
    /// Localized strings for Group Policy.
    /// Resources are loaded from Policy.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Group Policy / Policy Editor Strings
        public string GroupPolicy_WindowsEditionNotice_Title => GetResource(ResourceFileNames.Policy, "GroupPolicy_WindowsEditionNotice_Title");
        public string GroupPolicy_WindowsEditionNotice_Message => GetResource(ResourceFileNames.Policy, "GroupPolicy_WindowsEditionNotice_Message");
        public string PolicyListHeader_Policy => GetResource(ResourceFileNames.Policy, "PolicyListHeader_Policy");
        public string PolicyListHeader_State => GetResource(ResourceFileNames.Policy, "PolicyListHeader_State");
        public string Policy_State_Label => GetResource(ResourceFileNames.Policy, "Policy_State_Label");
        public string Policy_State_NotConfigured => GetResource(ResourceFileNames.Policy, "Policy_State_NotConfigured");
        public string Policy_Selector_General => GetResource(ResourceFileNames.Policy, "Policy_Selector_General");
        public string Policy_Selector_Explain => GetResource(ResourceFileNames.Policy, "Policy_Selector_Explain");
        public string Policy_Options_Title => GetResource(ResourceFileNames.Policy, "Policy_Options_Title");
        public string Policy_NoOptionsText => GetResource(ResourceFileNames.Policy, "Policy_NoOptionsText");
        public string Policy_SupportedOn_Prefix => GetResource(ResourceFileNames.Policy, "Policy_SupportedOn_Prefix");
        public string Policy_HiveInfo_Machine => GetResource(ResourceFileNames.Policy, "Policy_HiveInfo_Machine");
        public string Policy_HiveInfo_User => GetResource(ResourceFileNames.Policy, "Policy_HiveInfo_User");
        public string Policy_ValidationErrors_Header => GetResource(ResourceFileNames.Policy, "Policy_ValidationErrors_Header");
        public string Policy_SaveFailed_Title => GetResource(ResourceFileNames.Policy, "Policy_SaveFailed_Title");
        public string Policy_SaveFailed_MessageFormat => GetResource(ResourceFileNames.Policy, "Policy_SaveFailed_MessageFormat");

        // Validation error templates
        public string Policy_Error_PleaseEnter_Format => GetResource(ResourceFileNames.Policy, "Policy_Error_PleaseEnter_Format");
        public string Policy_Error_InvalidFormat_Format => GetResource(ResourceFileNames.Policy, "Policy_Error_InvalidFormat_Format");
        public string Policy_Error_EnterValidNumber_Format => GetResource(ResourceFileNames.Policy, "Policy_Error_EnterValidNumber_Format");
        public string Policy_Error_ValueOutOfRange_Format => GetResource(ResourceFileNames.Policy, "Policy_Error_ValueOutOfRange_Format");
        public string Policy_Error_PleaseSelect_Format => GetResource(ResourceFileNames.Policy, "Policy_Error_PleaseSelect_Format");
        public string Policy_Error_PleaseSelectOrEnter_Format => GetResource(ResourceFileNames.Policy, "Policy_Error_PleaseSelectOrEnter_Format");

        // Resultant Set of Policy (RSoP) Strings
        public string RSoP_SourceGPO => GetResource(ResourceFileNames.Policy, "RSoP_SourceGPO");
        public string RSoP_Loading => GetResource(ResourceFileNames.Policy, "RSoP_Loading");
        public string RSoP_ErrorTitle => GetResource(ResourceFileNames.Policy, "RSoP_ErrorTitle");
        public string RSoP_ErrorLoadFailed => GetResource(ResourceFileNames.Policy, "RSoP_ErrorLoadFailed");
        public string RSoP_StateFilterAll => GetResource(ResourceFileNames.Policy, "RSoP_StateFilterAll");
        public string RSoP_StatsFormat => GetResource(ResourceFileNames.Policy, "RSoP_StatsFormat");
        public string RSoP_ExportButton => GetResource(ResourceFileNames.Policy, "RSoP_ExportButton");
        public string RSoP_ExportSuccess => GetResource(ResourceFileNames.Policy, "RSoP_ExportSuccess");
        public string RSoP_ExportFailed => GetResource(ResourceFileNames.Policy, "RSoP_ExportFailed");
        public string RSoP_DetailTab_General => GetResource(ResourceFileNames.Policy, "RSoP_DetailTab_General");
        public string RSoP_DetailTab_Registry => GetResource(ResourceFileNames.Policy, "RSoP_DetailTab_Registry");
        public string RSoP_DetailTab_Explain => GetResource(ResourceFileNames.Policy, "RSoP_DetailTab_Explain");
        public string RSoP_Detail_State => GetResource(ResourceFileNames.Policy, "RSoP_Detail_State");
        public string RSoP_Detail_RegistryKey => GetResource(ResourceFileNames.Policy, "RSoP_Detail_RegistryKey");
        public string RSoP_Detail_RegistryValue => GetResource(ResourceFileNames.Policy, "RSoP_Detail_RegistryValue");
        public string RSoP_Detail_SupportedOn => GetResource(ResourceFileNames.Policy, "RSoP_Detail_SupportedOn");
        public string RSoP_Detail_Category => GetResource(ResourceFileNames.Policy, "RSoP_Detail_Category");
        public string RSoP_SourceLocalPolicy => GetResource(ResourceFileNames.Policy, "RSoP_SourceLocalPolicy");
    }
}
