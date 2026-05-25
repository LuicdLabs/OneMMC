using ManagementTools.Core.Localization;
namespace ManagementTools.Localization
{
    /// <summary>
    /// Localized strings for Local Security Policy (SecPol).
    /// Resources are loaded from SecPol.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // SecPol Hub
        public string SecPol_AccountPolicies_Header => GetResource(ResourceFileNames.SecPol, "SecPol_AccountPolicies_Header");
        public string SecPol_AccountPolicies_Description => GetResource(ResourceFileNames.SecPol, "SecPol_AccountPolicies_Description");
        public string SecPol_LocalPolicies_Header => GetResource(ResourceFileNames.SecPol, "SecPol_LocalPolicies_Header");
        public string SecPol_LocalPolicies_Description => GetResource(ResourceFileNames.SecPol, "SecPol_LocalPolicies_Description");
        public string SecPol_Firewall_Header => GetResource(ResourceFileNames.SecPol, "SecPol_Firewall_Header");
        public string SecPol_NetworkListManager_Header => GetResource(ResourceFileNames.SecPol, "SecPol_NetworkListManager_Header");
        public string SecPol_NetworkListManager_Description => GetResource(ResourceFileNames.SecPol, "SecPol_NetworkListManager_Description");
        public string SecPol_PublicKeyPolicies_Header => GetResource(ResourceFileNames.SecPol, "SecPol_PublicKeyPolicies_Header");
        public string SecPol_SoftwareRestriction_Header => GetResource(ResourceFileNames.SecPol, "SecPol_SoftwareRestriction_Header");
        public string SecPol_AppLocker_Header => GetResource(ResourceFileNames.SecPol, "SecPol_AppLocker_Header");
        public string SecPol_IPSecurity_Header => GetResource(ResourceFileNames.SecPol, "SecPol_IPSecurity_Header");
        public string SecPol_IPSecurity_Description => GetResource(ResourceFileNames.SecPol, "SecPol_IPSecurity_Description");
        public string SecPol_SystemAudit_Header => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_Header");

        // SecPol List Headers
        public string SecPol_Header_Policy => GetResource(ResourceFileNames.SecPol, "SecPol_Header_Policy");
        public string SecPol_Header_SecuritySetting => GetResource(ResourceFileNames.SecPol, "SecPol_Header_SecuritySetting");

        // SecPol Editor Dialog
        public string SecPol_Tab_General => GetResource(ResourceFileNames.SecPol, "SecPol_Tab_General");
        public string SecPol_Tab_Explain => GetResource(ResourceFileNames.SecPol, "SecPol_Tab_Explain");
        public string SecPol_DefineThisPolicy => GetResource(ResourceFileNames.SecPol, "SecPol_DefineThisPolicy");
        public string SecPol_ApplyButton => GetResource(ResourceFileNames.SecPol, "SecPol_ApplyButton");
        public string SecPol_AuditSettings => GetResource(ResourceFileNames.SecPol, "SecPol_AuditSettings");
        public string SecPol_AuditSuccess => GetResource(ResourceFileNames.SecPol, "SecPol_AuditSuccess");
        public string SecPol_AuditFailure => GetResource(ResourceFileNames.SecPol, "SecPol_AuditFailure");
        public string SecPol_SystemAudit_PropertiesTitleFormat => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_PropertiesTitleFormat");
        public string SecPol_SystemAudit_ConfigureAuditEvents => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_ConfigureAuditEvents");
        public string SecPol_SystemAudit_ConfigureButton => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_ConfigureButton");
        public string SecPol_SystemAudit_ExplainFallback => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_ExplainFallback");
        public string SecPol_SystemAudit_GlobalObjectPromptFormat => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_GlobalObjectPromptFormat");
        public string SecPol_SystemAudit_GlobalObjectExplainFallback => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_GlobalObjectExplainFallback");
        public string SecPol_SystemAudit_GlobalFileSacl => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_GlobalFileSacl");
        public string SecPol_SystemAudit_GlobalRegistrySacl => GetResource(ResourceFileNames.SecPol, "SecPol_SystemAudit_GlobalRegistrySacl");
        public string SecPol_AssignedAccounts => GetResource(ResourceFileNames.SecPol, "SecPol_AssignedAccounts");
        public string SecPol_NotDefined => GetResource(ResourceFileNames.SecPol, "SecPol_NotDefined");
        public string SecPol_AddAccount_Title => GetResource(ResourceFileNames.SecPol, "SecPol_AddAccount_Title");
        public string SecPol_AddAccount_Placeholder => GetResource(ResourceFileNames.SecPol, "SecPol_AddAccount_Placeholder");
        public string SecPol_Error_EnterValidNumber => GetResource(ResourceFileNames.SecPol, "SecPol_Error_EnterValidNumber");
        public string SecPol_Error_ValueOutOfRange => GetResource(ResourceFileNames.SecPol, "SecPol_Error_ValueOutOfRange");

        // SecPol Category Names
        public string SecPol_Category_PasswordPolicy => GetResource(ResourceFileNames.SecPol, "SecPol_Category_PasswordPolicy");
        public string SecPol_Category_AccountLockoutPolicy => GetResource(ResourceFileNames.SecPol, "SecPol_Category_AccountLockoutPolicy");
        public string SecPol_Category_AuditPolicy => GetResource(ResourceFileNames.SecPol, "SecPol_Category_AuditPolicy");
        public string SecPol_Category_UserRightsAssignment => GetResource(ResourceFileNames.SecPol, "SecPol_Category_UserRightsAssignment");
        public string SecPol_Category_SecurityOptions => GetResource(ResourceFileNames.SecPol, "SecPol_Category_SecurityOptions");

        // SecPol Export
        public string SecPol_ExportButton => GetResource(ResourceFileNames.SecPol, "SecPol_ExportButton");
        public string SecPol_ExportFilter => GetResource(ResourceFileNames.SecPol, "SecPol_ExportFilter");

        // Network List Manager Policies
        public string NetworkListManager_UnidentifiedNetworks_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UnidentifiedNetworksHeader);
        public string NetworkListManager_UnidentifiedNetworks_Description => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UnidentifiedNetworksDescription);
        public string NetworkListManager_IdentifyingNetworks_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.IdentifyingNetworksHeader);
        public string NetworkListManager_IdentifyingNetworks_Description => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.IdentifyingNetworksDescription);
        public string NetworkListManager_AllNetworks_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksHeader);
        public string NetworkListManager_AllNetworks_Description => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksDescription);
        public string NetworkListManager_NetworkName_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkNameHeader);
        public string NetworkListManager_NetworkNameUserPermissions_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkNameUserPermissionsHeader);
        public string NetworkListManager_NetworkIcon_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkIconHeader);
        public string NetworkListManager_NetworkIconUserPermissions_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkIconUserPermissionsHeader);
        public string NetworkListManager_NetworkLocationType_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkLocationTypeHeader);
        public string NetworkListManager_NetworkLocationTypeUserPermissions_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkLocationTypeUserPermissionsHeader);
        public string NetworkListManager_AllNetworksName_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksNameHeader);
        public string NetworkListManager_AllNetworksLocation_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksLocationHeader);
        public string NetworkListManager_AllNetworksIcon_Header => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksIconHeader);
        public string NetworkListManager_Name_Option => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NameOption);
        public string NetworkListManager_Icon_Option => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.IconOption);
        public string NetworkListManager_Private_Option => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.PrivateOption);
        public string NetworkListManager_Public_Option => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.PublicOption);
        public string NetworkListManager_UserCanChangeName => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UserCanChangeName);
        public string NetworkListManager_UserCannotChangeName => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UserCannotChangeName);
        public string NetworkListManager_UserCanChangeIcon => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UserCanChangeIcon);
        public string NetworkListManager_UserCannotChangeIcon => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UserCannotChangeIcon);
        public string NetworkListManager_UserCanChangeLocation => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UserCanChangeLocation);
        public string NetworkListManager_UserCannotChangeLocation => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.UserCannotChangeLocation);
        public string NetworkListManager_ConfigureButton => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.ConfigureButton);
        public string NetworkListManager_ChangeIconButton => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.ChangeIconButton);
        public string NetworkListManager_IconConfigured => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.IconConfigured);
        public string NetworkListManager_NetworkNameDialog_Title => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkNameDialogTitle);
        public string NetworkListManager_NetworkNameDialog_Description => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkNameDialogDescription);
        public string NetworkListManager_NetworkNameDialog_GroupTitle => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkNameDialogGroupTitle);
        public string NetworkListManager_NetworkNameDialog_Placeholder => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkNameDialogPlaceholder);
        public string NetworkListManager_NetworkIconDialog_Title => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkIconDialogTitle);
        public string NetworkListManager_NetworkIconDialog_Description => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkIconDialogDescription);
        public string NetworkListManager_NetworkIconDialog_GroupTitle => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkIconDialogGroupTitle);
        public string NetworkListManager_NetworkIconDialog_PreviewLabel => GetResource(ResourceFileNames.SecPol, NetworkListManagerKeys.NetworkIconDialogPreviewLabel);
    }
}
