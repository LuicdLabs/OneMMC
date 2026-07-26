using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Common/shared localized strings used across multiple features.
    /// Resources are loaded from Common.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Common Button Strings
        public string Common_OKButton => GetResource(ResourceFileNames.Common, "Common_OKButton");
        public string Common_CancelButton => GetResource(ResourceFileNames.Common, "Common_CancelButton");
        public string Common_YesButton => GetResource(ResourceFileNames.Common, "Common_YesButton");
        public string Common_NoButton => GetResource(ResourceFileNames.Common, "Common_NoButton");
        public string Common_OpenButton => GetResource(ResourceFileNames.Common, "Common_OpenButton");
        public string Common_CloseButton => GetResource(ResourceFileNames.Common, "Common_CloseButton");
        public string Common_SaveButton => GetResource(ResourceFileNames.Common, "Common_SaveButton");
        public string Common_CreateButton => GetResource(ResourceFileNames.Common, "Common_CreateButton");
        public string Common_BrowseButton => GetResource(ResourceFileNames.Common, "Common_BrowseButton");
        public string Common_AddButton => GetResource(ResourceFileNames.Common, "Common_AddButton");
        public string Common_AddButtonEllipsis => GetResource(ResourceFileNames.Common, "Common_AddButtonEllipsis");
        public string Common_RemoveButton => GetResource(ResourceFileNames.Common, "Common_RemoveButton");
        public string Common_DeleteButton => GetResource(ResourceFileNames.Common, "Common_DeleteButton");
        public string Common_ClearButton => GetResource(ResourceFileNames.Common, "Common_ClearButton");
        public string Common_EditButton => GetResource(ResourceFileNames.Common, "Common_EditButton");
        public string Common_ViewDetails => GetResource(ResourceFileNames.Common, "Common_ViewDetails");

        // Common Actions
        public string Common_Refresh => GetResource(ResourceFileNames.Common, "Common_Refresh");
        public string Common_Help => GetResource(ResourceFileNames.Common, "Common_Help");
        public string Common_LearnMore => GetResource(ResourceFileNames.Common, "Common_LearnMore");

        // Common Tooltips
        public string Common_MoreOptionsTooltip => GetResource(ResourceFileNames.Common, "Common_MoreOptionsTooltip");
        public string Common_EditTooltip => GetResource(ResourceFileNames.Common, "Common_EditTooltip");
        public string Common_DeleteTooltip => GetResource(ResourceFileNames.Common, "Common_DeleteTooltip");

        // Common Labels
        public string Common_SearchPlaceholder => GetResource(ResourceFileNames.Common, "Common_SearchPlaceholder");
        public string Common_SearchButton => GetResource(ResourceFileNames.Common, "Common_SearchButton");
        public string Common_Enabled => GetResource(ResourceFileNames.Common, "Common_Enabled");
        public string Common_Disabled => GetResource(ResourceFileNames.Common, "Common_Disabled");
        public string Common_State => GetResource(ResourceFileNames.Common, "Common_State");
        public string Common_Groups => GetResource(ResourceFileNames.Common, "Common_Groups");
        public string Common_Tasks => GetResource(ResourceFileNames.Common, "Common_Tasks");
        public string Common_Operations => GetResource(ResourceFileNames.Common, "Common_Operations");

        // Common Count Strings
        public string Common_CountItem_Singular => GetResource(ResourceFileNames.Common, "Common_CountItem_Singular");
        public string Common_CountItem_Plural => GetResource(ResourceFileNames.Common, "Common_CountItem_Plural");
        public string Common_CountRole_Singular => GetResource(ResourceFileNames.Common, "Common_CountRole_Singular");
        public string Common_CountRole_Plural => GetResource(ResourceFileNames.Common, "Common_CountRole_Plural");
        public string Common_CountTask_Singular => GetResource(ResourceFileNames.Common, "Common_CountTask_Singular");
        public string Common_CountTask_Plural => GetResource(ResourceFileNames.Common, "Common_CountTask_Plural");
        public string Common_CountOperation_Singular => GetResource(ResourceFileNames.Common, "Common_CountOperation_Singular");
        public string Common_CountOperation_Plural => GetResource(ResourceFileNames.Common, "Common_CountOperation_Plural");
        public string Common_CountScope_Singular => GetResource(ResourceFileNames.Common, "Common_CountScope_Singular");
        public string Common_CountScope_Plural => GetResource(ResourceFileNames.Common, "Common_CountScope_Plural");
        public string Common_CountSelected_Singular => GetResource(ResourceFileNames.Common, "Common_CountSelected_Singular");
        public string Common_CountSelected_Plural => GetResource(ResourceFileNames.Common, "Common_CountSelected_Plural");

        // Common Status Messages
        public string Common_LoadedSuccessfully => GetResource(ResourceFileNames.Common, "Common_LoadedSuccessfully");
        public string Common_ErrorTitle => GetResource(ResourceFileNames.Common, "Common_ErrorTitle");
        public string Common_SuccessTitle => GetResource(ResourceFileNames.Common, "Common_SuccessTitle");
        public string Common_InformationTitle => GetResource(ResourceFileNames.Common, "Common_InformationTitle");

        // Common Admin/Privilege Messages
        public string Common_AdminRequired_Title => GetResource(ResourceFileNames.Common, "Common_AdminRequired_Title");
        public string Common_AdminRequired_Message => GetResource(ResourceFileNames.Common, "Common_AdminRequired_Message");
        public string Common_AdminRequired_Close => GetResource(ResourceFileNames.Common, "Common_AdminRequired_Close");
        public string Common_AdminRequired_InfoBarMessage => GetResource(ResourceFileNames.Common, "Common_AdminRequired_InfoBarMessage");
        public string Common_RunAsAdministrator => GetResource(ResourceFileNames.Common, "Common_RunAsAdministrator");
        public string Common_AccessDenied_Generic => GetResource(ResourceFileNames.Common, "Common_AccessDenied_Generic");

        // Legal Document Dialog
        public string Common_Close => GetResource(ResourceFileNames.Common, "Common_Close");
        public string LegalDocument_LoadError => GetResource(ResourceFileNames.Common, "LegalDocument_LoadError");
    }
}
