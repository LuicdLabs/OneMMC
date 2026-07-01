using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for Windows Firewall (WF) rules editor.
    /// Resources are loaded from WF.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // CommandBar
        public string WF_NewRule => GetResource(ResourceFileNames.WF, "WF_NewRule");
        public string WF_EnableRule => GetResource(ResourceFileNames.WF, "WF_EnableRule");
        public string WF_DisableRule => GetResource(ResourceFileNames.WF, "WF_DisableRule");
        public string WF_DeleteRule => GetResource(ResourceFileNames.WF, "WF_DeleteRule");
        public string WF_SearchRules => GetResource(ResourceFileNames.WF, "WF_SearchRules");
        public string WF_LoadingRules => GetResource(ResourceFileNames.WF, WFKeys.LoadingRules);

        // Column headers
        public string WF_Column_Name => GetResource(ResourceFileNames.WF, "WF_Column_Name");
        public string WF_Column_Enabled => GetResource(ResourceFileNames.WF, "WF_Column_Enabled");
        public string WF_Column_Action => GetResource(ResourceFileNames.WF, "WF_Column_Action");
        public string WF_Column_Protocol => GetResource(ResourceFileNames.WF, "WF_Column_Protocol");
        public string WF_Column_Profile => GetResource(ResourceFileNames.WF, "WF_Column_Profile");

        // Dialog titles
        public string WF_AddInboundRule_Title => GetResource(ResourceFileNames.WF, "WF_AddInboundRule_Title");
        public string WF_AddOutboundRule_Title => GetResource(ResourceFileNames.WF, "WF_AddOutboundRule_Title");
        public string WF_EditInboundRule_Title => GetResource(ResourceFileNames.WF, "WF_EditInboundRule_Title");
        public string WF_EditOutboundRule_Title => GetResource(ResourceFileNames.WF, "WF_EditOutboundRule_Title");

        // Direction labels
        public string WF_Direction_Inbound => GetResource(ResourceFileNames.WF, "WF_Direction_Inbound");
        public string WF_Direction_Outbound => GetResource(ResourceFileNames.WF, "WF_Direction_Outbound");

        // Dialog tabs
        public string WF_Tab_General => GetResource(ResourceFileNames.WF, "WF_Tab_General");
        public string WF_Tab_Programs => GetResource(ResourceFileNames.WF, "WF_Tab_Programs");
        public string WF_Tab_Scope => GetResource(ResourceFileNames.WF, "WF_Tab_Scope");
        public string WF_Tab_Advanced => GetResource(ResourceFileNames.WF, "WF_Tab_Advanced");

        // Dialog fields
        public string WF_Field_Name => GetResource(ResourceFileNames.WF, "WF_Field_Name");
        public string WF_Field_Name_Placeholder => GetResource(ResourceFileNames.WF, "WF_Field_Name_Placeholder");
        public string WF_Field_Description => GetResource(ResourceFileNames.WF, "WF_Field_Description");
        public string WF_Field_Action => GetResource(ResourceFileNames.WF, "WF_Field_Action");
        public string WF_Field_Protocol => GetResource(ResourceFileNames.WF, "WF_Field_Protocol");
        public string WF_Field_Enabled => GetResource(ResourceFileNames.WF, "WF_Field_Enabled");
        public string WF_Field_Program => GetResource(ResourceFileNames.WF, "WF_Field_Program");
        public string WF_Field_Program_Description => GetResource(ResourceFileNames.WF, "WF_Field_Program_Description");
        public string WF_Field_LocalPort => GetResource(ResourceFileNames.WF, "WF_Field_LocalPort");
        public string WF_Field_RemotePort => GetResource(ResourceFileNames.WF, "WF_Field_RemotePort");
        public string WF_Field_LocalAddress => GetResource(ResourceFileNames.WF, "WF_Field_LocalAddress");
        public string WF_Field_RemoteAddress => GetResource(ResourceFileNames.WF, "WF_Field_RemoteAddress");
        public string WF_Field_Profile => GetResource(ResourceFileNames.WF, "WF_Field_Profile");

        // Action values
        public string WF_Action_Allow => GetResource(ResourceFileNames.WF, "WF_Action_Allow");
        public string WF_Action_Block => GetResource(ResourceFileNames.WF, "WF_Action_Block");

        // Profile values
        public string WF_Profile_Domain => GetResource(ResourceFileNames.WF, "WF_Profile_Domain");
        public string WF_Profile_Private => GetResource(ResourceFileNames.WF, "WF_Profile_Private");
        public string WF_Profile_Public => GetResource(ResourceFileNames.WF, "WF_Profile_Public");

        // Validation errors
        public string WF_Error_NameRequired => GetResource(ResourceFileNames.WF, "WF_Error_NameRequired");
        public string WF_Error_LoadRules => GetResource(ResourceFileNames.WF, WFKeys.ErrorLoadRules);
        public string WF_Error_UpdateRule => GetResource(ResourceFileNames.WF, WFKeys.ErrorUpdateRule);
        public string WF_Error_DeleteRule => GetResource(ResourceFileNames.WF, WFKeys.ErrorDeleteRule);
        public string WF_Error_LoadConnectionSecurityRules => GetResource(ResourceFileNames.WF, WFKeys.ErrorLoadConnectionSecurityRules);
        public string WF_Error_CreateConnectionSecurityRule => GetResource(ResourceFileNames.WF, WFKeys.ErrorCreateConnectionSecurityRule);
        public string WF_Error_UpdateConnectionSecurityRule => GetResource(ResourceFileNames.WF, WFKeys.ErrorUpdateConnectionSecurityRule);

        // Page breadcrumb labels
        public string WF_InboundRules_PageTitle => GetResource(ResourceFileNames.WF, "WF_InboundRules_PageTitle");
        public string WF_OutboundRules_PageTitle => GetResource(ResourceFileNames.WF, "WF_OutboundRules_PageTitle");
        public string WF_Monitoring_PageTitle => GetResource(ResourceFileNames.WF, "WF_Monitoring_PageTitle");
        public string WF_ConnectionSecurityRules_PageTitle => GetResource(ResourceFileNames.WF, "WF_ConnectionSecurityRules_PageTitle");

        // Connection Security Rules
        public string WF_Rule_On => GetResource(ResourceFileNames.WF, "WF_Rule_On");

        // Rule Info page
        public string WF_Rule_Apply => GetResource(ResourceFileNames.WF, "WF_Rule_Apply");
        public string WF_Rule_Disable => GetResource(ResourceFileNames.WF, "WF_Rule_Disable");
        public string WF_Rule_Enable => GetResource(ResourceFileNames.WF, "WF_Rule_Enable");
        public string WF_DeleteRule_ConfirmationTitle => GetResource(ResourceFileNames.WF, WFKeys.DeleteRuleConfirmationTitle);
        public string WF_DeleteRule_ConfirmationMessage => GetResource(ResourceFileNames.WF, WFKeys.DeleteRuleConfirmationMessage);
        public string WF_DeleteRule_ConfirmButton => GetResource(ResourceFileNames.WF, WFKeys.DeleteRuleConfirmButton);
        public string WF_RuleUnavailable_Title => GetResource(ResourceFileNames.WF, WFKeys.RuleUnavailableTitle);
        public string WF_RuleUnavailable_Message => GetResource(ResourceFileNames.WF, WFKeys.RuleUnavailableMessage);
        public string WF_Validation_ProgramPathRequired => GetResource(ResourceFileNames.WF, WFKeys.ValidationProgramPathRequired);
        public string WF_Validation_CompartmentRequired => GetResource(ResourceFileNames.WF, WFKeys.ValidationCompartmentRequired);
        public string WF_Validation_CompartmentInvalid => GetResource(ResourceFileNames.WF, WFKeys.ValidationCompartmentInvalid);
        public string WF_Validation_ProtocolNumberInvalid => GetResource(ResourceFileNames.WF, WFKeys.ValidationProtocolNumberInvalid);

        // Section headers
        public string WF_Section_General => GetResource(ResourceFileNames.WF, "WF_Section_General");
        public string WF_Section_Action => GetResource(ResourceFileNames.WF, "WF_Section_Action");
        public string WF_Section_ProgramsServices => GetResource(ResourceFileNames.WF, "WF_Section_ProgramsServices");
        public string WF_Section_ProtocolsPorts => GetResource(ResourceFileNames.WF, "WF_Section_ProtocolsPorts");
        public string WF_Section_Scope => GetResource(ResourceFileNames.WF, "WF_Section_Scope");
        public string WF_Section_Advanced => GetResource(ResourceFileNames.WF, "WF_Section_Advanced");
        public string WF_Section_LocalPrincipals => GetResource(ResourceFileNames.WF, "WF_Section_LocalPrincipals");
        public string WF_Section_RemoteUsers => GetResource(ResourceFileNames.WF, "WF_Section_RemoteUsers");
        public string WF_Section_RemoteComputers => GetResource(ResourceFileNames.WF, "WF_Section_RemoteComputers");

        // Action section
        public string WF_Action_ConnectionAction => GetResource(ResourceFileNames.WF, "WF_Action_ConnectionAction");
        public string WF_Action_AllowIfSecure => GetResource(ResourceFileNames.WF, "WF_Action_AllowIfSecure");
        public string WF_Action_CustomizeAllowSecure => GetResource(ResourceFileNames.WF, "WF_Action_CustomizeAllowSecure");
        public string WF_AllowIfSecure_OverrideBlockRules_Desc => GetResource(ResourceFileNames.WF, "WF_AllowIfSecure_OverrideBlockRules_Desc");
        public string WF_AllowIfSecure_OverrideBlockRules_Desc_Inbound => GetResource(ResourceFileNames.WF, "WF_AllowIfSecure_OverrideBlockRules_Desc_Inbound");

        // Programs & Services
        public string WF_ProgramsServices_SpecifiedConditions => GetResource(ResourceFileNames.WF, "WF_ProgramsServices_SpecifiedConditions");
        public string WF_ProgramsServices_RuleApplies => GetResource(ResourceFileNames.WF, "WF_ProgramsServices_RuleApplies");
        public string WF_Field_Compartments => GetResource(ResourceFileNames.WF, "WF_Field_Compartments");
        public string WF_Field_ApplicationPackages => GetResource(ResourceFileNames.WF, "WF_Field_ApplicationPackages");
        public string WF_Field_Services => GetResource(ResourceFileNames.WF, "WF_Field_Services");
        public string WF_Program_AllPrograms => GetResource(ResourceFileNames.WF, "WF_Program_AllPrograms");
        public string WF_Program_ThisProgram => GetResource(ResourceFileNames.WF, "WF_Program_ThisProgram");
        public string WF_Program_Browse => GetResource(ResourceFileNames.WF, "WF_Program_Browse");
        public string WF_Program_PathPlaceholder => GetResource(ResourceFileNames.WF, "WF_Program_PathPlaceholder");
        public string WF_Compartments_All => GetResource(ResourceFileNames.WF, "WF_Compartments_All");
        public string WF_Compartments_This => GetResource(ResourceFileNames.WF, "WF_Compartments_This");

        // Protocols and ports
        public string WF_Protocol_Type => GetResource(ResourceFileNames.WF, "WF_Protocol_Type");
        public string WF_Protocol_Number => GetResource(ResourceFileNames.WF, "WF_Protocol_Number");
        public string WF_Protocol_ICMP => GetResource(ResourceFileNames.WF, "WF_Protocol_ICMP");
        public string WF_Port_AllPorts => GetResource(ResourceFileNames.WF, "WF_Port_AllPorts");
        public string WF_Port_SpecificPorts => GetResource(ResourceFileNames.WF, "WF_Port_SpecificPorts");
        public string WF_Port_InputLocalPort => GetResource(ResourceFileNames.WF, "WF_Port_InputLocalPort");
        public string WF_Port_InputRemotePort => GetResource(ResourceFileNames.WF, "WF_Port_InputRemotePort");

        // Scope
        public string WF_Scope_ManageIPAddress => GetResource(ResourceFileNames.WF, "WF_Scope_ManageIPAddress");

        // Advanced
        public string WF_Advanced_Profiles => GetResource(ResourceFileNames.WF, "WF_Advanced_Profiles");
        public string WF_Advanced_ProfilesDescription => GetResource(ResourceFileNames.WF, "WF_Advanced_ProfilesDescription");
        public string WF_Advanced_InterfaceTypes => GetResource(ResourceFileNames.WF, "WF_Advanced_InterfaceTypes");
        public string WF_Advanced_EdgeTraversal => GetResource(ResourceFileNames.WF, "WF_Advanced_EdgeTraversal");
        public string WF_Advanced_EdgeTraversalDescription => GetResource(ResourceFileNames.WF, "WF_Advanced_EdgeTraversalDescription");
        public string WF_Advanced_EdgeTraversalType => GetResource(ResourceFileNames.WF, "WF_Advanced_EdgeTraversalType");
        public string WF_EdgeTraversal_Block => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_Block");
        public string WF_EdgeTraversal_Allow => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_Allow");
        public string WF_EdgeTraversal_DeferToUser => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_DeferToUser");
        public string WF_EdgeTraversal_DeferToApp => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_DeferToApp");
        public string WF_EdgeTraversal_Block_Desc => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_Block_Desc");
        public string WF_EdgeTraversal_Allow_Desc => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_Allow_Desc");
        public string WF_EdgeTraversal_DeferToUser_Desc => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_DeferToUser_Desc");
        public string WF_EdgeTraversal_DeferToApp_Desc => GetResource(ResourceFileNames.WF, "WF_EdgeTraversal_DeferToApp_Desc");

        // Local Principals / Remote Users / Remote Computers
        public string WF_LocalPrincipals_Manage => GetResource(ResourceFileNames.WF, "WF_LocalPrincipals_Manage");
        public string WF_RemoteUsers_Manage => GetResource(ResourceFileNames.WF, "WF_RemoteUsers_Manage");
        public string WF_RemoteComputers_Manage => GetResource(ResourceFileNames.WF, "WF_RemoteComputers_Manage");

        // Connection Security Rules - Remote Computers
        public string WF_CSR_RemoteEndpoints_Manage => GetResource(ResourceFileNames.WF, "WF_CSR_RemoteEndpoints_Manage");

        // Connection Security Rules - Protocols and Ports
        public string WF_CSR_Endpoint1Port => GetResource(ResourceFileNames.WF, "WF_CSR_Endpoint1Port");
        public string WF_CSR_Endpoint1Port_Input => GetResource(ResourceFileNames.WF, "WF_CSR_Endpoint1Port_Input");
        public string WF_CSR_Endpoint2Port => GetResource(ResourceFileNames.WF, "WF_CSR_Endpoint2Port");
        public string WF_CSR_Endpoint2Port_Input => GetResource(ResourceFileNames.WF, "WF_CSR_Endpoint2Port_Input");

        // Connection Security Rules - Verification → Authentication
        public string WF_Section_Authentication => GetResource(ResourceFileNames.WF, "WF_Section_Authentication");
        public string WF_CSR_AuthenticationMode => GetResource(ResourceFileNames.WF, "WF_CSR_AuthenticationMode");
        public string WF_CSR_AuthMode_DoNotAuthenticate => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMode_DoNotAuthenticate");
        public string WF_CSR_AuthMode_RequestInboundOutbound => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMode_RequestInboundOutbound");
        public string WF_CSR_AuthMode_RequireInboundRequestOutbound => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMode_RequireInboundRequestOutbound");
        public string WF_CSR_AuthMode_RequireInboundOutbound => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMode_RequireInboundOutbound");
        public string WF_CSR_AuthMode_RequireInboundClearOutbound => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMode_RequireInboundClearOutbound");
        public string WF_CSR_Section_Methods => GetResource(ResourceFileNames.WF, "WF_CSR_Section_Methods");
        public string WF_CSR_AuthenticationMethod => GetResource(ResourceFileNames.WF, "WF_CSR_AuthenticationMethod");
        public string WF_CSR_AuthMethod_Default => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMethod_Default");
        public string WF_CSR_AuthMethod_ComputerAndUser => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMethod_ComputerAndUser");
        public string WF_CSR_AuthMethod_Computer => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMethod_Computer");
        public string WF_CSR_AuthMethod_User => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMethod_User");
        public string WF_CSR_AuthMethod_Advanced => GetResource(ResourceFileNames.WF, "WF_CSR_AuthMethod_Advanced");
        public string WF_CSR_CustomizeMethods => GetResource(ResourceFileNames.WF, "WF_CSR_CustomizeMethods");

        // Connection Security Rules - Advanced
        public string WF_CSR_ConfigureIPsec => GetResource(ResourceFileNames.WF, "WF_CSR_ConfigureIPsec");

        // Buttons
        public string WF_Button_Customize => GetResource(ResourceFileNames.WF, "WF_Button_Customize");
        public string WF_Button_Specify => GetResource(ResourceFileNames.WF, "WF_Button_Specify");
        public string WF_Button_Manage => GetResource(ResourceFileNames.WF, "WF_Button_Manage");
        public string WF_Button_Configure => GetResource(ResourceFileNames.WF, "WF_Button_Configure");
        public string WF_OpenLegacyFirewall => GetResource(ResourceFileNames.WF, "WF_OpenLegacyFirewall");
        public string WF_PredefinedRule_InfoBar_Title => GetResource(ResourceFileNames.WF, "WF_PredefinedRule_InfoBar_Title");
        public string WF_PredefinedRule_InfoBar_Message => GetResource(ResourceFileNames.WF, "WF_PredefinedRule_InfoBar_Message");

        // Scope dialogs
        public string WF_ServicesDialog_Title => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogTitle);
        public string WF_ServicesDialog_Description => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogDescription);
        public string WF_ServicesDialog_All => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogAll);
        public string WF_ServicesDialog_Only => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogOnly);
        public string WF_ServicesDialog_Specific => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogSpecific);
        public string WF_ServicesDialog_ShortName => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogShortName);
        public string WF_ServicesDialog_ShortNamePlaceholder => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogShortNamePlaceholder);
        public string WF_ServicesDialog_ShortNameColumn => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogShortNameColumn);
        public string WF_ServicesDialog_SelectionRequired => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogSelectionRequired);
        public string WF_ServicesDialog_ShortNameRequired => GetResource(ResourceFileNames.WF, WFKeys.ServicesDialogShortNameRequired);
        public string WF_ApplicationPackagesDialog_Title => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogTitle);
        public string WF_ApplicationPackagesDialog_Description => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogDescription);
        public string WF_ApplicationPackagesDialog_All => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogAll);
        public string WF_ApplicationPackagesDialog_PackagesOnly => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogPackagesOnly);
        public string WF_ApplicationPackagesDialog_Specific => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogSpecific);
        public string WF_ApplicationPackagesDialog_Sid => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogSid);
        public string WF_ApplicationPackagesDialog_SidPlaceholder => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogSidPlaceholder);
        public string WF_ApplicationPackagesDialog_UserColumn => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogUserColumn);
        public string WF_ApplicationPackagesDialog_SelectionRequired => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogSelectionRequired);
        public string WF_ApplicationPackagesDialog_SidRequired => GetResource(ResourceFileNames.WF, WFKeys.ApplicationPackagesDialogSidRequired);
        public string WF_RemoteComputersDialog_Title => GetResource(ResourceFileNames.WF, WFKeys.RemoteComputersDialogTitle);
        public string WF_RemoteComputersDialog_SecureRequired => GetResource(ResourceFileNames.WF, WFKeys.RemoteComputersDialogSecureRequired);
        public string WF_RemoteComputersDialog_Description => GetResource(ResourceFileNames.WF, WFKeys.RemoteComputersDialogDescription);
        public string WF_RemoteComputersDialog_Authorized => GetResource(ResourceFileNames.WF, WFKeys.RemoteComputersDialogAuthorized);
        public string WF_RemoteComputersDialog_Exception => GetResource(ResourceFileNames.WF, WFKeys.RemoteComputersDialogException);
        public string WF_RemoteComputersDialog_ExceptionDescription => GetResource(ResourceFileNames.WF, WFKeys.RemoteComputersDialogExceptionDescription);
        public string WF_RemoteComputersDialog_SelectionRequired => GetResource(ResourceFileNames.WF, WFKeys.RemoteComputersDialogSelectionRequired);
    }
}
