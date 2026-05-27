namespace ManagementTools.Core.Localization;

/// <summary>
/// Resource File Names
/// </summary>
public static class ResourceFileNames
{
    public const string Resources = "Resources";
    public const string Common = "Common";
    public const string Certificates = "Certificates";
    public const string Navigation = "Navigation";
    public const string Settings = "Settings";
    public const string DeviceManager = "DeviceManager";
    public const string DiskManagement = "DiskManagement";
    public const string Services = "Services";
    public const string TPM = "TPM";
    public const string Policy = "Policy";
    public const string SecPol = "SecPol";
    public const string LusrMgr = "LusrMgr";
    public const string PerfMon = "PerfMon";
    public const string AzMan = "AzMan";
    public const string ComExp = "ComExp";
    public const string EventViewer = "EventViewer";
    public const string PrintManagement = "PrintManagement";
    public const string WF = "WF";
}

/// <summary>
/// Resource key constants for certificate management pages.
/// </summary>
public static class CertificateKeys
{
    public const string LocalComputerScopeFormat = "Certificates_LocalComputerScopeFormat";
    public const string CurrentUserScopeFormat = "Certificates_CurrentUserScopeFormat";
    public const string ImportStoreCommand = "Certificates_ImportStoreCommand";
    public const string ExportStoreCommand = "Certificates_ExportStoreCommand";
    public const string ExportItemCommand = "Certificates_ExportItemCommand";
    public const string PropertiesCommand = "Certificates_PropertiesCommand";
    public const string PropertiesTitleFormat = "Certificates_PropertiesTitleFormat";
    public const string MoreButton = "Certificates_MoreButton";
    public const string SectionCertificates = "Certificates_SectionCertificates";
    public const string SectionCRLs = "Certificates_SectionCRLs";
    public const string SectionCTLs = "Certificates_SectionCTLs";
    public const string EmptyCertificates = "Certificates_EmptyCertificates";
    public const string EmptyCRLs = "Certificates_EmptyCRLs";
    public const string EmptyCTLs = "Certificates_EmptyCTLs";
    public const string CertificateIssuerFormat = "Certificates_CertificateIssuerFormat";
    public const string CertificateValidityFormat = "Certificates_CertificateValidityFormat";
    public const string ContextValidityFormat = "Certificates_ContextValidityFormat";
    public const string CertificateSummaryFormat = "Certificates_CertificateSummaryFormat";
    public const string CrlSummaryFormat = "Certificates_CrlSummaryFormat";
    public const string CtlSummaryFormat = "Certificates_CtlSummaryFormat";
    public const string ItemHashFormat = "Certificates_ItemHashFormat";
    public const string NotAvailable = "Certificates_NotAvailable";
    public const string DeleteConfirmTitle = "Certificates_DeleteConfirmTitle";
    public const string DeleteConfirmMessage = "Certificates_DeleteConfirmMessage";
}

/// <summary>
/// Resource Key Constants - Local Security Policy display values
/// </summary>
public static class SecPolKeys
{
    public const string ValueNotDefined = "SecPol_Value_NotDefined";
    public const string ValueEnabled = "SecPol_Value_Enabled";
    public const string ValueDisabled = "SecPol_Value_Disabled";
    public const string ValueEmpty = "SecPol_Value_Empty";
    public const string BitmaskNoMinimum = "SecPol_Bitmask_NoMinimum";

    public const string AuditNoAuditing = "SecPol_Audit_NoAuditing";
    public const string AuditSuccess = "SecPol_Audit_Success";
    public const string AuditFailure = "SecPol_Audit_Failure";
    public const string AuditNotConfigured = "SecPol_Audit_NotConfigured";
    public const string SystemAuditConfigured = "SecPol_SystemAudit_Configured";
    public const string SystemAuditGlobalObjectAccessAuditing = "SecPol_SystemAudit_GlobalObjectAccessAuditing";
    public const string SystemAuditGlobalObjectFileSystem = "SecPol_SystemAudit_GlobalObject_FileSystem";
    public const string SystemAuditGlobalObjectRegistry = "SecPol_SystemAudit_GlobalObject_Registry";
    public const string SystemAuditGlobalFileSacl = "SecPol_SystemAudit_GlobalFileSacl";
    public const string SystemAuditGlobalRegistrySacl = "SecPol_SystemAudit_GlobalRegistrySacl";
    public const string SystemAuditAclFullControl = "SecPol_SystemAudit_Acl_FullControl";
    public const string SystemAuditAclReadAndExecute = "SecPol_SystemAudit_Acl_ReadAndExecute";
    public const string SystemAuditAclRead = "SecPol_SystemAudit_Acl_Read";
    public const string SystemAuditAclWrite = "SecPol_SystemAudit_Acl_Write";
    public const string SystemAuditAclDelete = "SecPol_SystemAudit_Acl_Delete";
    public const string SystemAuditAclThisFolderOnly = "SecPol_SystemAudit_Acl_ThisFolderOnly";
    public const string SystemAuditAclThisFolderSubfoldersFiles = "SecPol_SystemAudit_Acl_ThisFolderSubfoldersFiles";
    public const string SystemAuditAclThisFolderAndSubfolders = "SecPol_SystemAudit_Acl_ThisFolderAndSubfolders";
    public const string SystemAuditAclThisFolderAndFiles = "SecPol_SystemAudit_Acl_ThisFolderAndFiles";
    public const string SystemAuditAclSubfoldersAndFilesOnly = "SecPol_SystemAudit_Acl_SubfoldersAndFilesOnly";
    public const string SystemAuditAclSubfoldersOnly = "SecPol_SystemAudit_Acl_SubfoldersOnly";
    public const string SystemAuditAclFilesOnly = "SecPol_SystemAudit_Acl_FilesOnly";
    public const string SystemAuditAclThisKeyOnly = "SecPol_SystemAudit_Acl_ThisKeyOnly";
    public const string SystemAuditAclThisKeyAndSubkeys = "SecPol_SystemAudit_Acl_ThisKeyAndSubkeys";
    public const string SystemAuditAclSubkeysOnly = "SecPol_SystemAudit_Acl_SubkeysOnly";

    public const string UnitDays = "SecPol_Unit_Days";
    public const string UnitMinutes = "SecPol_Unit_Minutes";
    public const string UnitSeconds = "SecPol_Unit_Seconds";
    public const string UnitLogons = "SecPol_Unit_Logons";
    public const string UnitCharacters = "SecPol_Unit_Characters";
    public const string UnitPasswordsRemembered = "SecPol_Unit_PasswordsRemembered";

    public const string DropdownScRemoveOptionNone = "SecPol_Dropdown_ScRemoveOption_0";
    public const string DropdownScRemoveOptionLock = "SecPol_Dropdown_ScRemoveOption_1";
    public const string DropdownScRemoveOptionLogoff = "SecPol_Dropdown_ScRemoveOption_2";
    public const string DropdownScRemoveOptionDisconnect = "SecPol_Dropdown_ScRemoveOption_3";

    public const string DropdownConsentAdmin0 = "SecPol_Dropdown_ConsentPromptBehaviorAdmin_0";
    public const string DropdownConsentAdmin1 = "SecPol_Dropdown_ConsentPromptBehaviorAdmin_1";
    public const string DropdownConsentAdmin2 = "SecPol_Dropdown_ConsentPromptBehaviorAdmin_2";
    public const string DropdownConsentAdmin3 = "SecPol_Dropdown_ConsentPromptBehaviorAdmin_3";
    public const string DropdownConsentAdmin4 = "SecPol_Dropdown_ConsentPromptBehaviorAdmin_4";
    public const string DropdownConsentAdmin5 = "SecPol_Dropdown_ConsentPromptBehaviorAdmin_5";

    public const string DropdownConsentUser0 = "SecPol_Dropdown_ConsentPromptBehaviorUser_0";
    public const string DropdownConsentUser1 = "SecPol_Dropdown_ConsentPromptBehaviorUser_1";
    public const string DropdownConsentUser3 = "SecPol_Dropdown_ConsentPromptBehaviorUser_3";

    public const string DropdownForceGuest0 = "SecPol_Dropdown_ForceGuest_0";
    public const string DropdownForceGuest1 = "SecPol_Dropdown_ForceGuest_1";

    public const string DropdownLdapClientIntegrity0 = "SecPol_Dropdown_LDAPClientIntegrity_0";
    public const string DropdownLdapClientIntegrity1 = "SecPol_Dropdown_LDAPClientIntegrity_1";
    public const string DropdownLdapClientIntegrity2 = "SecPol_Dropdown_LDAPClientIntegrity_2";

    public const string DropdownLmCompatibilityLevel0 = "SecPol_Dropdown_LmCompatibilityLevel_0";
    public const string DropdownLmCompatibilityLevel1 = "SecPol_Dropdown_LmCompatibilityLevel_1";
    public const string DropdownLmCompatibilityLevel2 = "SecPol_Dropdown_LmCompatibilityLevel_2";
    public const string DropdownLmCompatibilityLevel3 = "SecPol_Dropdown_LmCompatibilityLevel_3";
    public const string DropdownLmCompatibilityLevel4 = "SecPol_Dropdown_LmCompatibilityLevel_4";
    public const string DropdownLmCompatibilityLevel5 = "SecPol_Dropdown_LmCompatibilityLevel_5";

    public const string DropdownAllocateDASD0 = "SecPol_Dropdown_AllocateDASD_0";
    public const string DropdownAllocateDASD1 = "SecPol_Dropdown_AllocateDASD_1";
    public const string DropdownAllocateDASD2 = "SecPol_Dropdown_AllocateDASD_2";

    public const string DropdownTypeOfAdminApprovalMode1 = "SecPol_Dropdown_TypeOfAdminApprovalMode_1";
    public const string DropdownTypeOfAdminApprovalMode2 = "SecPol_Dropdown_TypeOfAdminApprovalMode_2";

    public const string DropdownBlockMicrosoftAccounts0 = "SecPol_Dropdown_BlockMicrosoftAccounts_0";
    public const string DropdownBlockMicrosoftAccounts1 = "SecPol_Dropdown_BlockMicrosoftAccounts_1";
    public const string DropdownBlockMicrosoftAccounts3 = "SecPol_Dropdown_BlockMicrosoftAccounts_3";

    public const string DropdownDontDisplayLockedUserId1 = "SecPol_Dropdown_DontDisplayLockedUserId_1";
    public const string DropdownDontDisplayLockedUserId2 = "SecPol_Dropdown_DontDisplayLockedUserId_2";
    public const string DropdownDontDisplayLockedUserId3 = "SecPol_Dropdown_DontDisplayLockedUserId_3";

    // LDAPClientConfidentiality dropdown
    public const string DropdownLdapClientConfidentiality0 = "SecPol_Dropdown_LDAPClientConfidentiality_0";
    public const string DropdownLdapClientConfidentiality1 = "SecPol_Dropdown_LDAPClientConfidentiality_1";
    public const string DropdownLdapClientConfidentiality2 = "SecPol_Dropdown_LDAPClientConfidentiality_2";

    // ConsentPromptBehaviorAdminAP dropdown (Admin Protection mode)
    public const string DropdownConsentAdminAP1 = "SecPol_Dropdown_ConsentPromptBehaviorAdminAP_1";
    public const string DropdownConsentAdminAP2 = "SecPol_Dropdown_ConsentPromptBehaviorAdminAP_2";

    // ForceKeyProtection dropdown
    public const string DropdownForceKeyProtection0 = "SecPol_Dropdown_ForceKeyProtection_0";
    public const string DropdownForceKeyProtection1 = "SecPol_Dropdown_ForceKeyProtection_1";
    public const string DropdownForceKeyProtection2 = "SecPol_Dropdown_ForceKeyProtection_2";

    // LDAPServerIntegrity dropdown
    public const string DropdownLdapServerIntegrity1 = "SecPol_Dropdown_LDAPServerIntegrity_1";
    public const string DropdownLdapServerIntegrity2 = "SecPol_Dropdown_LDAPServerIntegrity_2";

    // LDAPServerIntegrityEnforced dropdown
    public const string DropdownLdapServerIntegrityEnforced0 = "SecPol_Dropdown_LDAPServerIntegrityEnforced_0";
    public const string DropdownLdapServerIntegrityEnforced1 = "SecPol_Dropdown_LDAPServerIntegrityEnforced_1";
    public const string DropdownLdapServerIntegrityEnforced2 = "SecPol_Dropdown_LDAPServerIntegrityEnforced_2";

    // LDAPEnforceChannelBinding dropdown
    public const string DropdownLdapEnforceChannelBinding0 = "SecPol_Dropdown_LDAPEnforceChannelBinding_0";
    public const string DropdownLdapEnforceChannelBinding1 = "SecPol_Dropdown_LDAPEnforceChannelBinding_1";
    public const string DropdownLdapEnforceChannelBinding2 = "SecPol_Dropdown_LDAPEnforceChannelBinding_2";

    // SPNTargetNameValidationLevel dropdown
    public const string DropdownSPNTargetNameValidation0 = "SecPol_Dropdown_SPNTargetNameValidationLevel_0";
    public const string DropdownSPNTargetNameValidation1 = "SecPol_Dropdown_SPNTargetNameValidationLevel_1";
    public const string DropdownSPNTargetNameValidation2 = "SecPol_Dropdown_SPNTargetNameValidationLevel_2";

    // S4U2SelfFlags dropdown
    public const string DropdownS4U2SelfFlags0 = "SecPol_Dropdown_S4U2SelfFlags_0";
    public const string DropdownS4U2SelfFlags1 = "SecPol_Dropdown_S4U2SelfFlags_1";
    public const string DropdownS4U2SelfFlags2 = "SecPol_Dropdown_S4U2SelfFlags_2";

    // NTLM restrict outgoing traffic dropdown
    public const string DropdownRestrictSendingNTLM0 = "SecPol_Dropdown_RestrictSendingNTLMTraffic_0";
    public const string DropdownRestrictSendingNTLM1 = "SecPol_Dropdown_RestrictSendingNTLMTraffic_1";
    public const string DropdownRestrictSendingNTLM2 = "SecPol_Dropdown_RestrictSendingNTLMTraffic_2";

    // NTLM restrict in domain dropdown
    public const string DropdownRestrictNTLMInDomain0 = "SecPol_Dropdown_RestrictNTLMInDomain_0";
    public const string DropdownRestrictNTLMInDomain1 = "SecPol_Dropdown_RestrictNTLMInDomain_1";
    public const string DropdownRestrictNTLMInDomain3 = "SecPol_Dropdown_RestrictNTLMInDomain_3";
    public const string DropdownRestrictNTLMInDomain5 = "SecPol_Dropdown_RestrictNTLMInDomain_5";
    public const string DropdownRestrictNTLMInDomain7 = "SecPol_Dropdown_RestrictNTLMInDomain_7";

    // NTLM restrict incoming traffic dropdown
    public const string DropdownRestrictReceivingNTLM0 = "SecPol_Dropdown_RestrictReceivingNTLMTraffic_0";
    public const string DropdownRestrictReceivingNTLM1 = "SecPol_Dropdown_RestrictReceivingNTLMTraffic_1";
    public const string DropdownRestrictReceivingNTLM2 = "SecPol_Dropdown_RestrictReceivingNTLMTraffic_2";

    // NTLM audit in domain dropdown
    public const string DropdownAuditNTLMInDomain0 = "SecPol_Dropdown_AuditNTLMInDomain_0";
    public const string DropdownAuditNTLMInDomain1 = "SecPol_Dropdown_AuditNTLMInDomain_1";
    public const string DropdownAuditNTLMInDomain3 = "SecPol_Dropdown_AuditNTLMInDomain_3";
    public const string DropdownAuditNTLMInDomain5 = "SecPol_Dropdown_AuditNTLMInDomain_5";
    public const string DropdownAuditNTLMInDomain7 = "SecPol_Dropdown_AuditNTLMInDomain_7";

    // NTLM audit incoming traffic dropdown
    public const string DropdownAuditReceivingNTLM0 = "SecPol_Dropdown_AuditReceivingNTLMTraffic_0";
    public const string DropdownAuditReceivingNTLM1 = "SecPol_Dropdown_AuditReceivingNTLMTraffic_1";
    public const string DropdownAuditReceivingNTLM2 = "SecPol_Dropdown_AuditReceivingNTLMTraffic_2";

    // Unit for MaxDevicePasswordFailedAttempts
    public const string UnitInvalidLogonAttempts = "SecPol_Unit_InvalidLogonAttempts";
}

/// <summary>
/// Resource key constants for Network List Manager Policies.
/// </summary>
public static class NetworkListManagerKeys
{
    public const string UnidentifiedNetworksHeader = "NetworkListManager_UnidentifiedNetworks_Header";
    public const string UnidentifiedNetworksDescription = "NetworkListManager_UnidentifiedNetworks_Description";
    public const string IdentifyingNetworksHeader = "NetworkListManager_IdentifyingNetworks_Header";
    public const string IdentifyingNetworksDescription = "NetworkListManager_IdentifyingNetworks_Description";
    public const string AllNetworksHeader = "NetworkListManager_AllNetworks_Header";
    public const string AllNetworksDescription = "NetworkListManager_AllNetworks_Description";
    public const string NetworkNameHeader = "NetworkListManager_NetworkName_Header";
    public const string NetworkNameUserPermissionsHeader = "NetworkListManager_NetworkNameUserPermissions_Header";
    public const string NetworkIconHeader = "NetworkListManager_NetworkIcon_Header";
    public const string NetworkIconUserPermissionsHeader = "NetworkListManager_NetworkIconUserPermissions_Header";
    public const string NetworkLocationTypeHeader = "NetworkListManager_NetworkLocationType_Header";
    public const string NetworkLocationTypeUserPermissionsHeader = "NetworkListManager_NetworkLocationTypeUserPermissions_Header";
    public const string AllNetworksNameHeader = "NetworkListManager_AllNetworksName_Header";
    public const string AllNetworksLocationHeader = "NetworkListManager_AllNetworksLocation_Header";
    public const string AllNetworksIconHeader = "NetworkListManager_AllNetworksIcon_Header";
    public const string NameOption = "NetworkListManager_Name_Option";
    public const string IconOption = "NetworkListManager_Icon_Option";
    public const string PrivateOption = "NetworkListManager_Private_Option";
    public const string PublicOption = "NetworkListManager_Public_Option";
    public const string UserCanChangeName = "NetworkListManager_UserCanChangeName";
    public const string UserCannotChangeName = "NetworkListManager_UserCannotChangeName";
    public const string UserCanChangeIcon = "NetworkListManager_UserCanChangeIcon";
    public const string UserCannotChangeIcon = "NetworkListManager_UserCannotChangeIcon";
    public const string UserCanChangeLocation = "NetworkListManager_UserCanChangeLocation";
    public const string UserCannotChangeLocation = "NetworkListManager_UserCannotChangeLocation";
    public const string ConfigureButton = "NetworkListManager_ConfigureButton";
    public const string ChangeIconButton = "NetworkListManager_ChangeIconButton";
    public const string IconConfigured = "NetworkListManager_IconConfigured";
    public const string NetworkNameDialogTitle = "NetworkListManager_NetworkNameDialog_Title";
    public const string NetworkNameDialogDescription = "NetworkListManager_NetworkNameDialog_Description";
    public const string NetworkNameDialogGroupTitle = "NetworkListManager_NetworkNameDialog_GroupTitle";
    public const string NetworkNameDialogPlaceholder = "NetworkListManager_NetworkNameDialog_Placeholder";
    public const string NetworkIconDialogTitle = "NetworkListManager_NetworkIconDialog_Title";
    public const string NetworkIconDialogDescription = "NetworkListManager_NetworkIconDialog_Description";
    public const string NetworkIconDialogGroupTitle = "NetworkListManager_NetworkIconDialog_GroupTitle";
    public const string NetworkIconDialogPreviewLabel = "NetworkListManager_NetworkIconDialog_PreviewLabel";
}

/// <summary>
/// Resource Key Constants - Device Manager
/// </summary>
public static class DeviceManagerKeys
{
    public const string HiddenDevices = "DeviceManager_HiddenDevices";
    public const string DeviceCountPrefix = "DeviceManager_DeviceCountPrefix";
    public const string DeviceCountSuffix = "DeviceManager_DeviceCountSuffix";
    
    // Device Status Descriptions
    public const string StatusWorking = "DeviceManager_StatusWorking";
    public const string StatusConfigError = "DeviceManager_StatusConfigError";
    public const string StatusDriverCorrupt = "DeviceManager_StatusDriverCorrupt";
    public const string StatusCannotStart = "DeviceManager_StatusCannotStart";
    public const string StatusNoResources = "DeviceManager_StatusNoResources";
    public const string StatusRestartRequired = "DeviceManager_StatusRestartRequired";
    public const string StatusReinstallDriver = "DeviceManager_StatusReinstallDriver";
    public const string StatusRegistryCorrupt = "DeviceManager_StatusRegistryCorrupt";
    public const string StatusRemoving = "DeviceManager_StatusRemoving";
    public const string StatusDisabled = "DeviceManager_StatusDisabled";
    public const string StatusNotPresent = "DeviceManager_StatusNotPresent";
    public const string StatusNoDriver = "DeviceManager_StatusNoDriver";
    public const string StatusDisabledFirmware = "DeviceManager_StatusDisabledFirmware";
    public const string StatusCannotLoadDriver = "DeviceManager_StatusCannotLoadDriver";
    public const string StatusDriverDisabled = "DeviceManager_StatusDriverDisabled";
    public const string StatusCannotDetermineResources = "DeviceManager_StatusCannotDetermineResources";
    public const string StatusCannotDetermineConfig = "DeviceManager_StatusCannotDetermineConfig";
    public const string StatusInsufficientFirmware = "DeviceManager_StatusInsufficientFirmware";
    public const string StatusInterruptConflict = "DeviceManager_StatusInterruptConflict";
    public const string StatusCannotInitializeDriver = "DeviceManager_StatusCannotInitializeDriver";
    public const string StatusDriverInMemory = "DeviceManager_StatusDriverInMemory";
    public const string StatusDriverCorruptOrMissing = "DeviceManager_StatusDriverCorruptOrMissing";
    public const string StatusRegistryMissing = "DeviceManager_StatusRegistryMissing";
    public const string StatusHardwareNotFound = "DeviceManager_StatusHardwareNotFound";
    public const string StatusDuplicateDevice = "DeviceManager_StatusDuplicateDevice";
    public const string StatusDeviceReportedProblem = "DeviceManager_StatusDeviceReportedProblem";
    public const string StatusApplicationClosed = "DeviceManager_StatusApplicationClosed";
    public const string StatusNotConnected = "DeviceManager_StatusNotConnected";
    public const string StatusSystemShutdown = "DeviceManager_StatusSystemShutdown";
    public const string StatusSafeRemoval = "DeviceManager_StatusSafeRemoval";
    public const string StatusSoftwareBlocked = "DeviceManager_StatusSoftwareBlocked";
    public const string StatusRegistryTooBig = "DeviceManager_StatusRegistryTooBig";
    public const string StatusInvalidSignature = "DeviceManager_StatusInvalidSignature";
    public const string StatusUnknownError = "DeviceManager_StatusUnknownError";
}

/// <summary>
/// Resource Key Constants - TPM
/// </summary>
public static class TPMKeys
{
    public const string Loading = "TPM_Loading";
    public const string Success = "TPM_Success";
    public const string Error = "TPM_Error";
    public const string Warning = "TPM_Warning";
    public const string CannotOpenConsole = "TPM_CannotOpenConsole";
    public const string CannotGetInfo = "TPM_CannotGetInfo";
    public const string NoXamlRootError = "TPM_NoXamlRootError";
    public const string ClearTPMTitle = "TPM_ClearTPMTitle";
    public const string ClearTPMConfirmMessage = "TPM_ClearTPMConfirmMessage";
    public const string ClearTPMConfirmPrimary = "TPM_ClearTPMConfirmPrimary";
    public const string ClearTPMConfirmCancel = "TPM_ClearTPMConfirmCancel";
    public const string ClearTPMStarted = "TPM_ClearTPMStarted";
    public const string ClearTPMSucceeded = "TPM_ClearTPMSucceeded";
    public const string ClearTPMRequiresAdmin = "TPM_ClearTPMRequiresAdmin";
    public const string ClearTPM_NoWin32Tpm = "TPM_ClearTPM_NoWin32Tpm";
    public const string ClearTPM_NoMethod = "TPM_ClearTPM_NoMethod";
    public const string ClearTPM_NeedsParameters = "TPM_ClearTPM_NeedsParameters";
    public const string ClearTPM_InvokeFailed = "TPM_ClearTPM_InvokeFailed";
    public const string ClearTPMError = "TPM_ClearTPMError";
    public const string VersionFormat = "TPM_VersionFormat";
    public const string NotAvailable = "TPM_NotAvailable";
    public const string AccessDenied = "TPM_AccessDenied";
    public const string ClearAllMethodsFailed = "TPM_ClearAllMethodsFailed";
    public const string WmiAccessDenied = "TPM_WmiAccessDenied";
}

/// <summary>
/// Resource Key Constants - Authorization Manager
/// </summary>
public static class AzManKeys
{
    public const string StoreCount_Singular = "AuthorizationManager_StoreCount_Singular";
    public const string StoreCount_Plural = "AuthorizationManager_StoreCount_Plural";
    public const string Status_CreatingStore = "AuthorizationManager_Status_CreatingStore";
    public const string Status_CreateSuccess = "AuthorizationManager_Status_CreateSuccess";
    public const string Status_OpeningStore = "AuthorizationManager_Status_OpeningStore";
    public const string Status_OpenSuccess = "AuthorizationManager_Status_OpenSuccess";
    public const string Status_CloseStore = "AuthorizationManager_Status_CloseStore";
    public const string Status_DeletingStore = "AuthorizationManager_Status_DeletingStore";
    public const string Status_DeleteSuccess = "AuthorizationManager_Status_DeleteSuccess";
    public const string Status_Refreshing = "AuthorizationManager_Status_Refreshing";
    public const string Status_RefreshComplete = "AuthorizationManager_Status_RefreshComplete";
    public const string Status_RefreshingAll = "AuthorizationManager_Status_RefreshingAll";

    // Authorization Store
    public const string Store_Status_CreatingApplication = "AuthorizationStore_Status_CreatingApplication";
    public const string Store_Status_CreateApplicationSuccess = "AuthorizationStore_Status_CreateApplicationSuccess";
    public const string Store_Status_DeletingApplication = "AuthorizationStore_Status_DeletingApplication";
    public const string Store_Status_DeleteApplicationSuccess = "AuthorizationStore_Status_DeleteApplicationSuccess";
    public const string Store_Status_CreatingGroup = "AuthorizationStore_Status_CreatingGroup";
    public const string Store_Status_CreateGroupSuccess = "AuthorizationStore_Status_CreateGroupSuccess";
    public const string Store_Status_DeletingGroup = "AuthorizationStore_Status_DeletingGroup";
    public const string Store_Status_DeleteGroupSuccess = "AuthorizationStore_Status_DeleteGroupSuccess";
    public const string Store_Status_UpdateGroupSuccess = "AuthorizationStore_Status_UpdateGroupSuccess";
    public const string Store_Status_UpdatingGroup = "AuthorizationStore_Status_UpdatingGroup";

    // Access Denied
    public const string AccessDenied = "AzMan_AccessDenied";
}

/// <summary>
/// Resource Key Constants - Disk Management
/// </summary>
public static class DiskMgmtKeys
{
    // Access Denied messages
    public const string AccessDenied_Operation = "DiskMgmt_AccessDenied_Operation";
    public const string AccessDenied_AdminRequired = "DiskMgmt_AccessDenied_AdminRequired";
}

/// <summary>
/// Resource Key Constants - Common
/// </summary>
public static class CommonKeys
{
    public const string LoadingData = "Common_LoadingData";
    public const string LoadedSuccessfully = "Common_LoadedSuccessfully";
    
    // Generic status messages for WinUI 3
    public const string Creating = "Common_Creating";
    public const string Deleting = "Common_Deleting";
    public const string Updating = "Common_Updating";
    public const string OperationCompleted = "Common_OperationCompleted";
    public const string OperationFailed = "Common_OperationFailed";
    
    // Admin / Privilege Messages
    public const string AdminRequired_Title = "Common_AdminRequired_Title";
    public const string AdminRequired_Message = "Common_AdminRequired_Message";
    public const string AccessDenied_Generic = "Common_AccessDenied_Generic";

    // Count messages
    public const string CountItem_Singular = "Common_CountItem_Singular";
    public const string CountItem_Plural = "Common_CountItem_Plural";
    public const string CountRole_Singular = "Common_CountRole_Singular";
    public const string CountRole_Plural = "Common_CountRole_Plural";
    public const string CountTask_Singular = "Common_CountTask_Singular";
    public const string CountTask_Plural = "Common_CountTask_Plural";
    public const string CountOperation_Singular = "Common_CountOperation_Singular";
    public const string CountOperation_Plural = "Common_CountOperation_Plural";
    public const string CountScope_Singular = "Common_CountScope_Singular";
    public const string CountScope_Plural = "Common_CountScope_Plural";
}

/// <summary>
/// Resource key constants for Windows Firewall.
/// </summary>
public static class WFKeys
{
    public const string LoadingRules = "WF_LoadingRules";
    public const string ErrorLoadRules = "WF_Error_LoadRules";
    public const string ErrorUpdateRule = "WF_Error_UpdateRule";
    public const string ErrorDeleteRule = "WF_Error_DeleteRule";
    public const string ErrorLoadConnectionSecurityRules = "WF_Error_LoadConnectionSecurityRules";
    public const string ErrorCreateConnectionSecurityRule = "WF_Error_CreateConnectionSecurityRule";
    public const string ErrorUpdateConnectionSecurityRule = "WF_Error_UpdateConnectionSecurityRule";
    public const string DeleteRuleConfirmationTitle = "WF_DeleteRule_ConfirmationTitle";
    public const string DeleteRuleConfirmationMessage = "WF_DeleteRule_ConfirmationMessage";
    public const string DeleteRuleConfirmButton = "WF_DeleteRule_ConfirmButton";
    public const string RuleUnavailableTitle = "WF_RuleUnavailable_Title";
    public const string RuleUnavailableMessage = "WF_RuleUnavailable_Message";
    public const string ValidationProgramPathRequired = "WF_Validation_ProgramPathRequired";
    public const string ValidationCompartmentRequired = "WF_Validation_CompartmentRequired";
    public const string ValidationCompartmentInvalid = "WF_Validation_CompartmentInvalid";
    public const string ValidationProtocolNumberInvalid = "WF_Validation_ProtocolNumberInvalid";
    public const string ValidationCertificateAuthorityPathRequired = "WF_Validation_CertificateAuthorityPathRequired";
    public const string ValidationCustomAuthenticationRequired = "WF_Validation_CustomAuthenticationRequired";
    public const string ValidationPresharedKeyRequired = "WF_Validation_PresharedKeyRequired";
    public const string ValidationPresharedKeyCannotUseSecondAuthentication = "WF_Validation_PresharedKeyCannotUseSecondAuthentication";
    public const string ValidationSecondAuthenticationCategoryMismatch = "WF_Validation_SecondAuthenticationCategoryMismatch";
    public const string ValidationFollowRenewalRequiresThumbprint = "WF_Validation_FollowRenewalRequiresThumbprint";
    public const string ValidationCertificateNameRestrictionIncomplete = "WF_Validation_CertificateNameRestrictionIncomplete";
    public const string ValidationCustomEkuRequired = "WF_Validation_CustomEkuRequired";
    public const string ValidationAddressInterfaceConflict = "WF_Validation_AddressInterfaceConflict";
    public const string ValidationTunnelEndpointFamiliesRequired = "WF_Validation_TunnelEndpointFamiliesRequired";
    public const string ValidationIkev2FirstAuthOnly = "WF_Validation_Ikev2FirstAuthOnly";
    public const string ValidationIkev2UnsupportedAuth = "WF_Validation_Ikev2UnsupportedAuth";
    public const string ValidationPredefinedRuleGroupRequired = "WF_Validation_PredefinedRuleGroupRequired";
    public const string ValidationPredefinedRuleRequired = "WF_Validation_PredefinedRuleRequired";
    public const string ValidationLocalPortsRequired = "WF_Validation_LocalPortsRequired";
    public const string ValidationRemotePortsRequired = "WF_Validation_RemotePortsRequired";
    public const string ServicesDialogTitle = "WF_ServicesDialog_Title";
    public const string ServicesDialogDescription = "WF_ServicesDialog_Description";
    public const string ServicesDialogAll = "WF_ServicesDialog_All";
    public const string ServicesDialogOnly = "WF_ServicesDialog_Only";
    public const string ServicesDialogSpecific = "WF_ServicesDialog_Specific";
    public const string ServicesDialogShortName = "WF_ServicesDialog_ShortName";
    public const string ServicesDialogShortNamePlaceholder = "WF_ServicesDialog_ShortNamePlaceholder";
    public const string ServicesDialogShortNameColumn = "WF_ServicesDialog_ShortNameColumn";
    public const string ServicesDialogSelectionRequired = "WF_ServicesDialog_SelectionRequired";
    public const string ServicesDialogShortNameRequired = "WF_ServicesDialog_ShortNameRequired";
    public const string ApplicationPackagesDialogTitle = "WF_ApplicationPackagesDialog_Title";
    public const string ApplicationPackagesDialogDescription = "WF_ApplicationPackagesDialog_Description";
    public const string ApplicationPackagesDialogAll = "WF_ApplicationPackagesDialog_All";
    public const string ApplicationPackagesDialogPackagesOnly = "WF_ApplicationPackagesDialog_PackagesOnly";
    public const string ApplicationPackagesDialogSpecific = "WF_ApplicationPackagesDialog_Specific";
    public const string ApplicationPackagesDialogSid = "WF_ApplicationPackagesDialog_Sid";
    public const string ApplicationPackagesDialogSidPlaceholder = "WF_ApplicationPackagesDialog_SidPlaceholder";
    public const string ApplicationPackagesDialogUserColumn = "WF_ApplicationPackagesDialog_UserColumn";
    public const string ApplicationPackagesDialogSelectionRequired = "WF_ApplicationPackagesDialog_SelectionRequired";
    public const string ApplicationPackagesDialogSidRequired = "WF_ApplicationPackagesDialog_SidRequired";
    public const string RemoteComputersDialogTitle = "WF_RemoteComputersDialog_Title";
    public const string RemoteComputersDialogSecureRequired = "WF_RemoteComputersDialog_SecureRequired";
    public const string RemoteComputersDialogDescription = "WF_RemoteComputersDialog_Description";
    public const string RemoteComputersDialogAuthorized = "WF_RemoteComputersDialog_Authorized";
    public const string RemoteComputersDialogException = "WF_RemoteComputersDialog_Exception";
    public const string RemoteComputersDialogExceptionDescription = "WF_RemoteComputersDialog_ExceptionDescription";
    public const string RemoteComputersDialogSelectionRequired = "WF_RemoteComputersDialog_SelectionRequired";
}

/// <summary>
/// Resource Key Constants - Group Policy
/// </summary>
public static class PolicyKeys
{
    public const string WindowsEditionNotice_Title = "GroupPolicy_WindowsEditionNotice_Title";
    public const string WindowsEditionNotice_Message = "GroupPolicy_WindowsEditionNotice_Message";

    // Access Denied messages (used by GpEdit services and ViewModel)
    public const string AccessDenied_Title = "Policy_AccessDenied_Title";
    public const string AccessDenied_Machine = "Policy_AccessDenied_Machine";
    public const string AccessDenied_User = "Policy_AccessDenied_User";

    // Tree labels
    public const string TreeComputerConfiguration = "Policy_Tree_ComputerConfiguration";
    public const string TreeUserConfiguration = "Policy_Tree_UserConfiguration";
    public const string TreeAdministrativeTemplates = "Policy_Tree_AdministrativeTemplates";

    // State labels
    public const string StateNotConfigured = "Policy_State_NotConfigured";
    public const string StateUnknown = "Policy_State_Unknown";
}

/// <summary>
/// Resource Key Constants - Resultant Set of Policy (RSoP)
/// </summary>
public static class RSoPKeys
{
    public const string Loading = "RSoP_Loading";
    public const string ErrorTitle = "RSoP_ErrorTitle";
    public const string ErrorLoadFailed = "RSoP_ErrorLoadFailed";

    // State filter
    public const string StateFilterAll = "RSoP_StateFilterAll";

    // Statistics
    public const string StatsFormat = "RSoP_StatsFormat";

    // Export
    public const string ExportButton = "RSoP_ExportButton";
    public const string ExportSuccess = "RSoP_ExportSuccess";
    public const string ExportFailed = "RSoP_ExportFailed";

    // Detail dialog
    public const string DetailTabGeneral = "RSoP_DetailTab_General";
    public const string DetailTabRegistry = "RSoP_DetailTab_Registry";
    public const string DetailTabExplain = "RSoP_DetailTab_Explain";
    public const string DetailState = "RSoP_Detail_State";
    public const string DetailRegistryKey = "RSoP_Detail_RegistryKey";
    public const string DetailRegistryValue = "RSoP_Detail_RegistryValue";
    public const string DetailSupportedOn = "RSoP_Detail_SupportedOn";
    public const string DetailCategory = "RSoP_Detail_Category";

    // Source
    public const string SourceLocalPolicy = "RSoP_SourceLocalPolicy";
}

/// <summary>
/// Resource Key Constants - Event Viewer
/// </summary>
public static class EventViewerKeys
{
    // Tree nodes
    public const string TreeWindowsLogs = "EventViewer_Tree_WindowsLogs";
    public const string TreeAppServicesLogs = "EventViewer_Tree_AppServicesLogs";
    public const string TreeApplication = "EventViewer_Tree_Application";
    public const string TreeSecurity = "EventViewer_Tree_Security";
    public const string TreeSetup = "EventViewer_Tree_Setup";
    public const string TreeSystem = "EventViewer_Tree_System";
    public const string TreeForwardedEvents = "EventViewer_Tree_ForwardedEvents";

    // Event levels
    public const string LevelCritical = "EventViewer_Level_Critical";
    public const string LevelError = "EventViewer_Level_Error";
    public const string LevelWarning = "EventViewer_Level_Warning";
    public const string LevelInformation = "EventViewer_Level_Information";
    public const string LevelVerbose = "EventViewer_Level_Verbose";

    // Details panel
    public const string TabGeneral = "EventViewer_Tab_General";
    public const string TabDetails = "EventViewer_Tab_Details";
    public const string DetailLogName = "EventViewer_Detail_LogName";
    public const string DetailSource = "EventViewer_Detail_Source";
    public const string DetailEventId = "EventViewer_Detail_EventId";
    public const string DetailLevel = "EventViewer_Detail_Level";
    public const string DetailUser = "EventViewer_Detail_User";
    public const string DetailOpCode = "EventViewer_Detail_OpCode";
    public const string DetailLogged = "EventViewer_Detail_Logged";
    public const string DetailTaskCategory = "EventViewer_Detail_TaskCategory";
    public const string DetailKeywords = "EventViewer_Detail_Keywords";
    public const string DetailComputer = "EventViewer_Detail_Computer";

    // Filter
    public const string FilterAll = "EventViewer_Filter_All";
    public const string SearchPlaceholder = "EventViewer_SearchPlaceholder";

    // Commands / Actions
    public const string Refresh = "EventViewer_Refresh";
    public const string ClearLog = "EventViewer_ClearLog";
    public const string ExportLog = "EventViewer_ExportLog";
    public const string LogProperties = "EventViewer_LogProperties";
    public const string OpenLegacy = "EventViewer_OpenLegacy";

    // Status messages
    public const string StatusLoading = "EventViewer_Status_Loading";
    public const string StatusLoadedFormat = "EventViewer_Status_LoadedFormat";
    public const string StatusNoEvents = "EventViewer_Status_NoEvents";
    public const string StatusMonitoring = "EventViewer_Status_Monitoring";

    // Clear log dialog
    public const string ClearLogConfirmTitle = "EventViewer_ClearLog_ConfirmTitle";
    public const string ClearLogConfirmMessage = "EventViewer_ClearLog_ConfirmMessage";
    public const string ClearLogSaveFirst = "EventViewer_ClearLog_SaveFirst";
    public const string ClearLogSuccess = "EventViewer_ClearLog_Success";

    // Log Properties dialog
    public const string LogPropTitle = "EventViewer_LogProp_Title";
    public const string LogPropFullName = "EventViewer_LogProp_FullName";
    public const string LogPropLogPath = "EventViewer_LogProp_LogPath";
    public const string LogPropLogSize = "EventViewer_LogProp_LogSize";
    public const string LogPropMaxSize = "EventViewer_LogProp_MaxSize";
    public const string LogPropEnabled = "EventViewer_LogProp_Enabled";
    public const string LogPropLogMode = "EventViewer_LogProp_LogMode";

    // Error messages
    public const string ErrorAccessDenied = "EventViewer_Error_AccessDenied";
    public const string ErrorLoadFailed = "EventViewer_Error_LoadFailed";

    // Load more
    public const string LoadMore = "EventViewer_LoadMore";
}

