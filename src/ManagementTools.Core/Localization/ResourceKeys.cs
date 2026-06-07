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
    public const string FsMgmt = "FsMgmt";
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
/// Resource key constants for Software Restriction Policies.
/// </summary>
public static class SoftwareRestrictionKeys
{
    public const string SectionSecurityLevels = "SRP_Section_SecurityLevels";
    public const string SectionAdditionalRules = "SRP_Section_AdditionalRules";
    public const string SectionEnforcement = "SRP_Section_Enforcement";
    public const string SectionDesignatedFileTypes = "SRP_Section_DesignatedFileTypes";
    public const string SectionTrustedPublishers = "SRP_Section_TrustedPublishers";
    public const string SecurityLevelDisallowed = "SRP_SecurityLevel_Disallowed";
    public const string SecurityLevelBasicUser = "SRP_SecurityLevel_BasicUser";
    public const string SecurityLevelUnrestricted = "SRP_SecurityLevel_Unrestricted";
    public const string SecurityLevelDisallowedDescription = "SRP_SecurityLevel_Disallowed_Description";
    public const string SecurityLevelBasicUserDescription = "SRP_SecurityLevel_BasicUser_Description";
    public const string SecurityLevelUnrestrictedDescription = "SRP_SecurityLevel_Unrestricted_Description";
    public const string RuleKindPath = "SRP_RuleKind_Path";
    public const string RuleKindHash = "SRP_RuleKind_Hash";
    public const string RuleKindCertificate = "SRP_RuleKind_Certificate";
    public const string RuleKindNetworkZone = "SRP_RuleKind_NetworkZone";
    public const string RuleKindUnknown = "SRP_RuleKind_Unknown";
    public const string RuleUnsupportedForEdit = "SRP_Error_RuleUnsupportedForEdit";
    public const string CertificateRuleUnsupportedForCreate = "SRP_Error_CertificateRuleUnsupportedForCreate";
    public const string CertificateRuleInvalid = "SRP_Error_CertificateRuleInvalid";
    public const string NetworkZoneInvalid = "SRP_Error_NetworkZoneInvalid";
    public const string StatusPolicyCreated = "SRP_Status_PolicyCreated";
    public const string StatusPolicyDeleted = "SRP_Status_PolicyDeleted";
    public const string StatusPolicySaved = "SRP_Status_PolicySaved";
    public const string StatusRuleSaved = "SRP_Status_RuleSaved";
    public const string StatusRuleDeleted = "SRP_Status_RuleDeleted";
    public const string NoPolicyDefined = "SRP_NoPolicyDefined";
    public const string NoPolicyDefinedDescription = "SRP_NoPolicyDefined_Description";
    public const string DefaultMarker = "SRP_DefaultMarker";
    public const string DefaultMarkerFormat = "SRP_DefaultMarker_Format";
    public const string NoAdditionalRules = "SRP_NoAdditionalRules";
    public const string NoDesignatedFileTypes = "SRP_NoDesignatedFileTypes";
    public const string DesignatedFileTypeSetting = "SRP_DesignatedFileType_Setting";
    public const string EnforcementDefaultSecurityLevel = "SRP_Enforcement_DefaultSecurityLevel";
    public const string EnforcementUserScope = "SRP_Enforcement_UserScope";
    public const string EnforcementFileScope = "SRP_Enforcement_FileScope";
    public const string EnforcementCertificateRules = "SRP_Enforcement_CertificateRules";
    public const string UserScopeAllUsers = "SRP_UserScope_AllUsers";
    public const string UserScopeAllExceptAdministrators = "SRP_UserScope_AllExceptAdministrators";
    public const string FileScopeExecutableFilesOnly = "SRP_FileScope_ExecutableFilesOnly";
    public const string FileScopeAllSoftwareFiles = "SRP_FileScope_AllSoftwareFiles";
    public const string TrustedPublishersManagement = "SRP_TrustedPublishers_Management";
    public const string TrustedPublishersDefineSettings = "SRP_TrustedPublishers_DefineSettings";
    public const string TrustedPublishersPublisherRevocation = "SRP_TrustedPublishers_PublisherRevocation";
    public const string TrustedPublishersTimestampRevocation = "SRP_TrustedPublishers_TimestampRevocation";
    public const string TrustedPublishersAdditionalChecks = "SRP_TrustedPublishers_AdditionalChecks";
    public const string PublisherScopeEndUsers = "SRP_PublisherScope_EndUsers";
    public const string PublisherScopeLocalAdministrators = "SRP_PublisherScope_LocalAdministrators";
    public const string PublisherScopeEnterpriseAdministrators = "SRP_PublisherScope_EnterpriseAdministrators";
    public const string PublisherScopeEndUsersOption = "SRP_PublisherScope_EndUsers_Option";
    public const string PublisherScopeLocalAdministratorsOption = "SRP_PublisherScope_LocalAdministrators_Option";
    public const string PublisherScopeEnterpriseAdministratorsOption = "SRP_PublisherScope_EnterpriseAdministrators_Option";
    public const string TrustedPublishersPublisherRevocationOption = "SRP_TrustedPublishers_PublisherRevocation_Option";
    public const string TrustedPublishersTimestampRevocationOption = "SRP_TrustedPublishers_TimestampRevocation_Option";
    public const string CommandNewPolicy = "SRP_Command_NewPolicy";
    public const string CommandDeletePolicy = "SRP_Command_DeletePolicy";
    public const string CommandSetAsDefault = "SRP_Command_SetAsDefault";
    public const string CommandNewPathRule = "SRP_Command_NewPathRule";
    public const string CommandNewHashRule = "SRP_Command_NewHashRule";
    public const string CommandNewCertificateRule = "SRP_Command_NewCertificateRule";
    public const string CommandNewNetworkZoneRule = "SRP_Command_NewNetworkZoneRule";
    public const string ListHeaderName = "SRP_ListHeader_Name";
    public const string ListHeaderSetting = "SRP_ListHeader_Setting";
    public const string ListHeaderType = "SRP_ListHeader_Type";
    public const string ListHeaderSecurityLevel = "SRP_ListHeader_SecurityLevel";
    public const string ListHeaderDescription = "SRP_ListHeader_Description";
    public const string ListHeaderLastModified = "SRP_ListHeader_LastModified";
    public const string DialogEditEnforcementTitle = "SRP_Dialog_EditEnforcement_Title";
    public const string DialogEditFileTypesTitle = "SRP_Dialog_EditFileTypes_Title";
    public const string DialogEditTrustedPublishersTitle = "SRP_Dialog_EditTrustedPublishers_Title";
    public const string DialogEditRuleTitle = "SRP_Dialog_EditRule_Title";
    public const string DialogDeletePolicyTitle = "SRP_Dialog_DeletePolicy_Title";
    public const string DialogDeletePolicyMessage = "SRP_Dialog_DeletePolicy_Message";
    public const string DialogDeleteRuleTitle = "SRP_Dialog_DeleteRule_Title";
    public const string DialogDeleteRuleMessage = "SRP_Dialog_DeleteRule_Message";
    public const string DialogSetDefaultSecurityLevelTitle = "SRP_Dialog_SetDefaultSecurityLevel_Title";
    public const string DialogSetDefaultSecurityLevelMessageFormat = "SRP_Dialog_SetDefaultSecurityLevel_MessageFormat";
    public const string DialogRuleDetailsTitleFormat = "SRP_Dialog_RuleDetails_TitleFormat";
    public const string FieldPath = "SRP_Field_Path";
    public const string FieldHashFile = "SRP_Field_HashFile";
    public const string FieldCertificateFile = "SRP_Field_CertificateFile";
    public const string FieldNetworkZone = "SRP_Field_NetworkZone";
    public const string FieldSecurityLevel = "SRP_Field_SecurityLevel";
    public const string FieldDescription = "SRP_Field_Description";
    public const string FieldDesignatedFileTypes = "SRP_Field_DesignatedFileTypes";
    public const string FieldFileExtension = "SRP_Field_FileExtension";
    public const string ListHeaderExtension = "SRP_ListHeader_Extension";
    public const string ListHeaderFileType = "SRP_ListHeader_FileType";
    public const string FileTypeFallbackFormat = "SRP_FileTypeFallback_Format";
    public const string HelpDesignatedFileTypes = "SRP_Help_DesignatedFileTypes";
    public const string HelpDesignatedFileTypesAddRemove = "SRP_Help_DesignatedFileTypes_AddRemove";
    public const string HelpTrustedPublishers = "SRP_Help_TrustedPublishers";
    public const string DialogSelectPathFileTitle = "SRP_Dialog_SelectPathFile_Title";
    public const string DialogSelectPathFolderTitle = "SRP_Dialog_SelectPathFolder_Title";
    public const string DialogSelectHashFileTitle = "SRP_Dialog_SelectHashFile_Title";
    public const string DialogSelectCertificateFileTitle = "SRP_Dialog_SelectCertificateFile_Title";
    public const string FileDialogAllFiles = "SRP_FileDialog_AllFiles";
    public const string FileDialogCertificateFiles = "SRP_FileDialog_CertificateFiles";
    public const string FileDialogSignedFiles = "SRP_FileDialog_SignedFiles";
    public const string BrowseFile = "SRP_Browse_File";
    public const string BrowseFolder = "SRP_Browse_Folder";
    public const string NetworkZoneLocalComputer = "SRP_NetworkZone_LocalComputer";
    public const string NetworkZoneLocalComputerDescription = "SRP_NetworkZone_LocalComputer_Description";
    public const string NetworkZoneLocalIntranet = "SRP_NetworkZone_LocalIntranet";
    public const string NetworkZoneLocalIntranetDescription = "SRP_NetworkZone_LocalIntranet_Description";
    public const string NetworkZoneTrustedSites = "SRP_NetworkZone_TrustedSites";
    public const string NetworkZoneTrustedSitesDescription = "SRP_NetworkZone_TrustedSites_Description";
    public const string NetworkZoneInternet = "SRP_NetworkZone_Internet";
    public const string NetworkZoneInternetDescription = "SRP_NetworkZone_Internet_Description";
    public const string NetworkZoneRestrictedSites = "SRP_NetworkZone_RestrictedSites";
    public const string NetworkZoneRestrictedSitesDescription = "SRP_NetworkZone_RestrictedSites_Description";
    public const string CertificateValid = "SRP_CertificateValid";
    public const string CertificateExpiredOrNotYetValid = "SRP_CertificateExpiredOrNotYetValid";
    public const string IntendedPurposesAll = "SRP_IntendedPurposes_All";
    public const string DetailRuleKind = "SRP_Detail_RuleKind";
    public const string DetailSecurityLevel = "SRP_Detail_SecurityLevel";
    public const string DetailCertificateRulesEnabled = "SRP_Detail_CertificateRulesEnabled";
    public const string DetailSubject = "SRP_Detail_Subject";
    public const string DetailIssuer = "SRP_Detail_Issuer";
    public const string DetailSerialNumber = "SRP_Detail_SerialNumber";
    public const string DetailThumbprint = "SRP_Detail_Thumbprint";
    public const string DetailValidFrom = "SRP_Detail_ValidFrom";
    public const string DetailValidTo = "SRP_Detail_ValidTo";
    public const string DetailIntendedPurposes = "SRP_Detail_IntendedPurposes";
    public const string DetailFriendlyName = "SRP_Detail_FriendlyName";
    public const string DetailStatus = "SRP_Detail_Status";
    public const string DetailStoragePath = "SRP_Detail_StoragePath";
    public const string DetailLastModified = "SRP_Detail_LastModified";
    public const string DetailDescription = "SRP_Detail_Description";
    public const string DetailRawData = "SRP_Detail_RawData";
}

/// <summary>
/// Resource key constants for Public Key Policies.
/// </summary>
public static class PublicKeyPolicyKeys
{
    public const string NodeEncryptingFileSystem = "PKP_Node_EncryptingFileSystem";
    public const string NodeEncryptingFileSystemDescription = "PKP_Node_EncryptingFileSystem_Description";
    public const string NodeDataProtection = "PKP_Node_DataProtection";
    public const string NodeDataProtectionDescription = "PKP_Node_DataProtection_Description";
    public const string NodeBitLockerDriveEncryption = "PKP_Node_BitLockerDriveEncryption";
    public const string NodeBitLockerDriveEncryptionDescription = "PKP_Node_BitLockerDriveEncryption_Description";
    public const string NodeCertificateEnrollmentPolicy = "PKP_Node_CertificateEnrollmentPolicy";
    public const string NodeCertificateEnrollmentPolicyDescription = "PKP_Node_CertificateEnrollmentPolicy_Description";
    public const string NodeCertificatePathValidation = "PKP_Node_CertificatePathValidation";
    public const string NodeCertificatePathValidationDescription = "PKP_Node_CertificatePathValidation_Description";
    public const string NodeCertificateAutoEnrollment = "PKP_Node_CertificateAutoEnrollment";
    public const string NodeCertificateAutoEnrollmentDescription = "PKP_Node_CertificateAutoEnrollment_Description";
    public const string ColumnName = "PKP_Column_Name";
    public const string ColumnIssuedTo = "PKP_Column_IssuedTo";
    public const string ColumnIssuedBy = "PKP_Column_IssuedBy";
    public const string ColumnExpirationDate = "PKP_Column_ExpirationDate";
    public const string ColumnIntendedPurposes = "PKP_Column_IntendedPurposes";
    public const string ColumnFriendlyName = "PKP_Column_FriendlyName";
    public const string ColumnStatus = "PKP_Column_Status";
    public const string ColumnCertificateTemplate = "PKP_Column_CertificateTemplate";
    public const string ColumnSetting = "PKP_Column_Setting";
    public const string CommandProperties = "PKP_Command_Properties";
    public const string CommandViewCertificate = "PKP_Command_ViewCertificate";
    public const string CommandAddRecoveryAgent = "PKP_Command_AddRecoveryAgent";
    public const string DialogDeleteRecoveryAgentTitle = "PKP_Dialog_DeleteRecoveryAgent_Title";
    public const string DialogDeleteRecoveryAgentMessageFormat = "PKP_Dialog_DeleteRecoveryAgent_MessageFormat";
    public const string DialogSelectRecoveryAgentCertificateTitle = "PKP_Dialog_SelectRecoveryAgentCertificate_Title";
    public const string FileDialogCertificateFiles = "PKP_FileDialog_CertificateFiles";
    public const string FileDialogAllFiles = "PKP_FileDialog_AllFiles";
    public const string DialogPolicyPropertiesTitleFormat = "PKP_Dialog_PolicyProperties_TitleFormat";
    public const string DialogCertificateDetailsTitleFormat = "PKP_Dialog_CertificateDetails_TitleFormat";
    public const string NoRecoveryAgents = "PKP_NoRecoveryAgents";
    public const string GroupPolicyStorePathFormat = "PKP_GroupPolicyStorePath_Format";
    public const string StoreReadFailed = "PKP_StoreReadFailed";
    public const string CertificateBlobUnavailable = "PKP_CertificateBlobUnavailable";
    public const string RecoveryAgentCertificateNotSuitableEfs = "PKP_RecoveryAgent_CertificateNotSuitable_Efs";
    public const string RecoveryAgentCertificateNotSuitableDataRecovery = "PKP_RecoveryAgent_CertificateNotSuitable_DataRecovery";
    public const string RecoveryAgentSingleCertificateLimit = "PKP_RecoveryAgent_SingleCertificateLimit";
    public const string CertificateValid = "PKP_CertificateValid";
    public const string CertificateExpiredOrNotYetValid = "PKP_CertificateExpiredOrNotYetValid";
    public const string IntendedPurposesAll = "PKP_IntendedPurposes_All";
    public const string NotConfigured = "PKP_NotConfigured";
    public const string EnrollmentPolicyServer = "PKP_EnrollmentPolicyServer";
    public const string EnrollmentPolicyCmdletBacked = "PKP_EnrollmentPolicyCmdletBacked";
    public const string EnrollmentPolicyConfigurationModel = "PKP_EnrollmentPolicy_ConfigurationModel";
    public const string EnrollmentPolicyTabTitle = "PKP_EnrollmentPolicy_TabTitle";
    public const string EnrollmentPolicyHelp = "PKP_EnrollmentPolicy_Help";
    public const string EnrollmentPolicyNoServers = "PKP_EnrollmentPolicy_NoServers";
    public const string EnrollmentPolicyServerCount = "PKP_EnrollmentPolicy_ServerCount";
    public const string EnrollmentPolicyPolicySource = "PKP_EnrollmentPolicy_PolicySource";
    public const string EnrollmentPolicyServers = "PKP_EnrollmentPolicy_Servers";
    public const string EnrollmentPolicyServerDetailFormat = "PKP_EnrollmentPolicy_ServerDetail_Format";
    public const string EnrollmentPolicyServerDetailValueFormat = "PKP_EnrollmentPolicy_ServerDetailValue_Format";
    public const string EnrollmentPolicyServerName = "PKP_EnrollmentPolicy_ServerName";
    public const string EnrollmentPolicyServerUrl = "PKP_EnrollmentPolicy_ServerUrl";
    public const string EnrollmentPolicyServerPolicyId = "PKP_EnrollmentPolicy_ServerPolicyId";
    public const string EnrollmentPolicyServerAuthentication = "PKP_EnrollmentPolicy_ServerAuthentication";
    public const string EnrollmentPolicyServerCost = "PKP_EnrollmentPolicy_ServerCost";
    public const string EnrollmentPolicyServerOptions = "PKP_EnrollmentPolicy_ServerOptions";
    public const string EnrollmentPolicyServerAutoEnrollment = "PKP_EnrollmentPolicy_ServerAutoEnrollment";
    public const string EnrollmentPolicyServerUseClientId = "PKP_EnrollmentPolicy_ServerUseClientId";
    public const string EnrollmentPolicyServerAllowUntrustedCa = "PKP_EnrollmentPolicy_ServerAllowUntrustedCa";
    public const string EnrollmentPolicyServerEditorAddTitle = "PKP_EnrollmentPolicy_ServerEditor_AddTitle";
    public const string EnrollmentPolicyServerEditorEditTitle = "PKP_EnrollmentPolicy_ServerEditor_EditTitle";
    public const string EnrollmentPolicyServerValidationUrlRequired = "PKP_EnrollmentPolicy_ServerValidation_UrlRequired";
    public const string EnrollmentPolicyServerValidationDuplicateUrl = "PKP_EnrollmentPolicy_ServerValidation_DuplicateUrl";
    public const string EnrollmentPolicyAuthAnonymous = "PKP_EnrollmentPolicy_Auth_Anonymous";
    public const string EnrollmentPolicyAuthKerberos = "PKP_EnrollmentPolicy_Auth_Kerberos";
    public const string EnrollmentPolicyAuthUserName = "PKP_EnrollmentPolicy_Auth_UserName";
    public const string EnrollmentPolicyAuthClientCertificate = "PKP_EnrollmentPolicy_Auth_ClientCertificate";
    public const string PathValidationAiaRetrieval = "PKP_PathValidation_AiaRetrieval";
    public const string PathValidationRootAutoUpdate = "PKP_PathValidation_RootAutoUpdate";
    public const string PathValidationDisallowedAutoUpdate = "PKP_PathValidation_DisallowedAutoUpdate";
    public const string PathValidationTrustedRoots = "PKP_PathValidation_TrustedRoots";
    public const string PathValidationTrustedPublishers = "PKP_PathValidation_TrustedPublishers";
    public const string PathValidationIntermediateCas = "PKP_PathValidation_IntermediateCas";
    public const string PathValidationChainEnginePolicySource = "PKP_PathValidation_ChainEnginePolicySource";
    public const string PathValidationAuthRootPolicySource = "PKP_PathValidation_AuthRootPolicySource";
    public const string PathValidationStoresTab = "PKP_PathValidation_Tab_Stores";
    public const string PathValidationTrustedPublishersTab = "PKP_PathValidation_Tab_TrustedPublishers";
    public const string PathValidationNetworkRetrievalTab = "PKP_PathValidation_Tab_NetworkRetrieval";
    public const string PathValidationRevocationTab = "PKP_PathValidation_Tab_Revocation";
    public const string PathValidationDefineSettings = "PKP_PathValidation_DefineSettings";
    public const string PathValidationStoresHelp = "PKP_PathValidation_Stores_Help";
    public const string PathValidationTrustedPublishersHelp = "PKP_PathValidation_TrustedPublishers_Help";
    public const string PathValidationNetworkRetrievalHelp = "PKP_PathValidation_NetworkRetrieval_Help";
    public const string PathValidationRevocationHelp = "PKP_PathValidation_Revocation_Help";
    public const string PathValidationStoresDefined = "PKP_PathValidation_StoresDefined";
    public const string PathValidationTrustedPublishersDefined = "PKP_PathValidation_TrustedPublishersDefined";
    public const string PathValidationNetworkRetrievalDefined = "PKP_PathValidation_NetworkRetrievalDefined";
    public const string PathValidationRevocationDefined = "PKP_PathValidation_RevocationDefined";
    public const string PathValidationAllowUserTrustedRoots = "PKP_PathValidation_AllowUserTrustedRoots";
    public const string PathValidationAllowPeerTrust = "PKP_PathValidation_AllowPeerTrust";
    public const string PathValidationSelectCertificatePurposes = "PKP_PathValidation_SelectCertificatePurposes";
    public const string PathValidationPeerTrustPurposes = "PKP_PathValidation_PeerTrustPurposes";
    public const string PathValidationRootStoresMode = "PKP_PathValidation_RootStoresMode";
    public const string PathValidationThirdPartyAndEnterpriseRoots = "PKP_PathValidation_ThirdPartyAndEnterpriseRoots";
    public const string PathValidationOnlyEnterpriseRoots = "PKP_PathValidation_OnlyEnterpriseRoots";
    public const string PathValidationUpnConstraints = "PKP_PathValidation_UpnConstraints";
    public const string PathValidationTrustedPublisherManagement = "PKP_PathValidation_TrustedPublisherManagement";
    public const string PathValidationPublisherScopeEndUsers = "PKP_PathValidation_PublisherScope_EndUsers";
    public const string PathValidationPublisherScopeLocalAdministrators = "PKP_PathValidation_PublisherScope_LocalAdministrators";
    public const string PathValidationPublisherScopeEnterpriseAdministrators = "PKP_PathValidation_PublisherScope_EnterpriseAdministrators";
    public const string PathValidationTrustedPublisherRevocation = "PKP_PathValidation_TrustedPublisherRevocation";
    public const string PathValidationTrustedTimestampRevocation = "PKP_PathValidation_TrustedTimestampRevocation";
    public const string PathValidationUrlRetrievalTimeout = "PKP_PathValidation_UrlRetrievalTimeout";
    public const string PathValidationPathRetrievalTimeout = "PKP_PathValidation_PathRetrievalTimeout";
    public const string PathValidationCrossCertInterval = "PKP_PathValidation_CrossCertInterval";
    public const string PathValidationPreferCrlBeforeOcsp = "PKP_PathValidation_PreferCrlBeforeOcsp";
    public const string PathValidationCachedOcspThreshold = "PKP_PathValidation_CachedOcspThreshold";
    public const string PathValidationExtendRevocationLifetime = "PKP_PathValidation_ExtendRevocationLifetime";
    public const string PathValidationRevocationExtensionHours = "PKP_PathValidation_RevocationExtensionHours";
    public const string PathValidationAddPurpose = "PKP_PathValidation_AddPurpose";
    public const string PathValidationDeletePurpose = "PKP_PathValidation_DeletePurpose";
    public const string PathValidationCustomOidPlaceholder = "PKP_PathValidation_CustomOidPlaceholder";
    public const string CertificateCountFormat = "PKP_CertificateCount_Format";
    public const string RegistryRawValueFormat = "PKP_Registry_RawValue_Format";
    public const string AutoEnrollmentCmdletBacked = "PKP_AutoEnrollmentCmdletBacked";
    public const string AutoEnrollmentConfigurationModel = "PKP_AutoEnrollment_ConfigurationModel";
    public const string AutoEnrollmentTabTitle = "PKP_AutoEnrollment_TabTitle";
    public const string AutoEnrollmentHelp = "PKP_AutoEnrollment_Help";
    public const string AutoEnrollmentRenewExpired = "PKP_AutoEnrollment_RenewExpired";
    public const string AutoEnrollmentUpdateTemplates = "PKP_AutoEnrollment_UpdateTemplates";
    public const string AutoEnrollmentExpirationNotifications = "PKP_AutoEnrollment_ExpirationNotifications";
    public const string AutoEnrollmentBalloonNotifications = "PKP_AutoEnrollment_BalloonNotifications";
    public const string AutoEnrollmentStoreNames = "PKP_AutoEnrollment_StoreNames";
    public const string AutoEnrollmentPolicySource = "PKP_AutoEnrollment_PolicySource";
    public const string AutoEnrollmentRawValueFormat = "PKP_AutoEnrollment_RawValue_Format";
    public const string EfsTabGeneral = "PKP_Efs_Tab_General";
    public const string EfsTabCertificates = "PKP_Efs_Tab_Certificates";
    public const string EfsTabCache = "PKP_Efs_Tab_Cache";
    public const string EfsFileEncryption = "PKP_Efs_FileEncryption";
    public const string EfsFileEncryptionNotDefined = "PKP_Efs_FileEncryption_NotDefined";
    public const string EfsFileEncryptionAllow = "PKP_Efs_FileEncryption_Allow";
    public const string EfsFileEncryptionDontAllow = "PKP_Efs_FileEncryption_DontAllow";
    public const string EfsEllipticCurveCryptography = "PKP_Efs_EllipticCurveCryptography";
    public const string EfsEccAllow = "PKP_Efs_Ecc_Allow";
    public const string EfsEccRequire = "PKP_Efs_Ecc_Require";
    public const string EfsEccDontAllow = "PKP_Efs_Ecc_DontAllow";
    public const string EfsOptions = "PKP_Efs_Options";
    public const string EfsEncryptDocumentsFolder = "PKP_Efs_EncryptDocumentsFolder";
    public const string EfsRequireSmartCard = "PKP_Efs_RequireSmartCard";
    public const string EfsCreateSmartCardUserKey = "PKP_Efs_CreateSmartCardUserKey";
    public const string EfsKeyBackupNotifications = "PKP_Efs_KeyBackupNotifications";
    public const string EfsTemplateName = "PKP_Efs_TemplateName";
    public const string EfsSelfSignedCertificates = "PKP_Efs_SelfSignedCertificates";
    public const string EfsAllowSelfSigned = "PKP_Efs_AllowSelfSigned";
    public const string EfsRsaKeySize = "PKP_Efs_RsaKeySize";
    public const string EfsEccKeySize = "PKP_Efs_EccKeySize";
    public const string EfsClearCacheWhen = "PKP_Efs_ClearCacheWhen";
    public const string EfsClearCacheOnTimeout = "PKP_Efs_ClearCacheOnTimeout";
    public const string EfsCacheTimeout = "PKP_Efs_CacheTimeout";
    public const string EfsClearCacheOnLock = "PKP_Efs_ClearCacheOnLock";
    public const string EfsMinutes = "PKP_Efs_Minutes";
    public const string EfsPolicySource = "PKP_Efs_PolicySource";
    public const string DetailSubject = "PKP_Detail_Subject";
    public const string DetailIssuer = "PKP_Detail_Issuer";
    public const string DetailSerialNumber = "PKP_Detail_SerialNumber";
    public const string DetailThumbprint = "PKP_Detail_Thumbprint";
    public const string DetailValidFrom = "PKP_Detail_ValidFrom";
    public const string DetailValidTo = "PKP_Detail_ValidTo";
    public const string DetailIntendedPurposes = "PKP_Detail_IntendedPurposes";
    public const string DetailFriendlyName = "PKP_Detail_FriendlyName";
    public const string DetailStatus = "PKP_Detail_Status";
    public const string DetailCertificateTemplate = "PKP_Detail_CertificateTemplate";
    public const string DetailSignatureAlgorithm = "PKP_Detail_SignatureAlgorithm";
    public const string DetailPublicKeyAlgorithm = "PKP_Detail_PublicKeyAlgorithm";
    public const string DetailPublicKeySize = "PKP_Detail_PublicKeySize";
    public const string DetailPublicKeySizeFormat = "PKP_Detail_PublicKeySize_Format";
    public const string DetailStore = "PKP_Detail_Store";
}

/// <summary>
/// Resource key constants for IP Security Policies on Local Computer.
/// </summary>
public static class IPSecurityPolicyKeys
{
    public const string SectionPolicies = "IPSec_Section_Policies";
    public const string SectionFilterLists = "IPSec_Section_FilterLists";
    public const string SectionFilterActions = "IPSec_Section_FilterActions";
    public const string ColumnName = "IPSec_Column_Name";
    public const string ColumnDescription = "IPSec_Column_Description";
    public const string ColumnPolicyAssigned = "IPSec_Column_PolicyAssigned";
    public const string ColumnLastModified = "IPSec_Column_LastModified";
    public const string ColumnFilterCount = "IPSec_Column_FilterCount";
    public const string ColumnAction = "IPSec_Column_Action";
    public const string ColumnSecurityMethods = "IPSec_Column_SecurityMethods";
    public const string ColumnSource = "IPSec_Column_Source";
    public const string CommandViewDetails = "IPSec_Command_ViewDetails";
    public const string DialogDetailsTitleFormat = "IPSec_Dialog_Details_TitleFormat";
    public const string PageDescription = "IPSec_PageDescription";
    public const string PolicyAgentService = "IPSec_PolicyAgentService";
    public const string ServiceStatusFormat = "IPSec_ServiceStatus_Format";
    public const string ServiceStatusUnavailable = "IPSec_ServiceStatus_Unavailable";
    public const string ServiceStatusRunning = "IPSec_ServiceStatus_Running";
    public const string ServiceStatusStopped = "IPSec_ServiceStatus_Stopped";
    public const string ServiceStatusPaused = "IPSec_ServiceStatus_Paused";
    public const string ServiceStatusStartPending = "IPSec_ServiceStatus_StartPending";
    public const string ServiceStatusStopPending = "IPSec_ServiceStatus_StopPending";
    public const string ServiceStartAutomatic = "IPSec_ServiceStart_Automatic";
    public const string ServiceStartManual = "IPSec_ServiceStart_Manual";
    public const string ServiceStartDisabled = "IPSec_ServiceStart_Disabled";
    public const string NoLegacyPolicies = "IPSec_NoLegacyPolicies";
    public const string LegacyStoreNotPresent = "IPSec_LegacyStore_NotPresent";
    public const string LegacyStoreEmpty = "IPSec_LegacyStore_Empty";
    public const string LegacyStoreAccessDenied = "IPSec_LegacyStore_AccessDenied";
    public const string LegacyStoreReadFailed = "IPSec_LegacyStore_ReadFailed";
    public const string EmptyPolicies = "IPSec_Empty_Policies";
    public const string EmptyFilterLists = "IPSec_Empty_FilterLists";
    public const string EmptyFilterActions = "IPSec_Empty_FilterActions";
    public const string ValueYes = "IPSec_Value_Yes";
    public const string ValueNo = "IPSec_Value_No";
    public const string FilterActionPermit = "IPSec_FilterAction_Permit";
    public const string FilterActionBlock = "IPSec_FilterAction_Block";
    public const string FilterActionNegotiate = "IPSec_FilterAction_Negotiate";
    public const string RuleCountFormat = "IPSec_RuleCount_Format";
    public const string FilterCountFormat = "IPSec_FilterCount_Format";
    public const string SecurityMethodCountFormat = "IPSec_SecurityMethodCount_Format";
    public const string RegistryValueCountFormat = "IPSec_RegistryValueCount_Format";
    public const string NoConnectionSecurityRules = "IPSec_NoConnectionSecurityRules";
    public const string ConnectionSecurityProviderDescription = "IPSec_ConnectionSecurityProvider_Description";
    public const string ConnectionSecurityAccessDenied = "IPSec_ConnectionSecurity_AccessDenied";
    public const string ConnectionSecurityProviderUnavailable = "IPSec_ConnectionSecurityProvider_Unavailable";
    public const string ConnectionSecurityStateFormat = "IPSec_ConnectionSecurityState_Format";
    public const string UnnamedPolicy = "IPSec_UnnamedPolicy";
    public const string SecurityNone = "IPSec_Security_None";
    public const string SecurityRequest = "IPSec_Security_Request";
    public const string SecurityRequire = "IPSec_Security_Require";
    public const string ModeTransport = "IPSec_Mode_Transport";
    public const string ModeTunnel = "IPSec_Mode_Tunnel";
    public const string RowKindLegacyPolicy = "IPSec_RowKind_LegacyPolicy";
    public const string RowKindConnectionSecurityRule = "IPSec_RowKind_ConnectionSecurityRule";
    public const string RowKindInformation = "IPSec_RowKind_Information";
    public const string RowKindPolicy = "IPSec_RowKind_Policy";
    public const string RowKindFilterList = "IPSec_RowKind_FilterList";
    public const string RowKindFilterAction = "IPSec_RowKind_FilterAction";
    public const string DetailRowKind = "IPSec_Detail_RowKind";
    public const string DetailName = "IPSec_Detail_Name";
    public const string DetailDescription = "IPSec_Detail_Description";
    public const string DetailPolicyAssigned = "IPSec_Detail_PolicyAssigned";
    public const string DetailLastModified = "IPSec_Detail_LastModified";
    public const string DetailSource = "IPSec_Detail_Source";
    public const string DetailSummary = "IPSec_Detail_Summary";
    public const string DetailRegistryValues = "IPSec_Detail_RegistryValues";
    public const string DetailRules = "IPSec_Detail_Rules";
    public const string DetailMasterPfs = "IPSec_Detail_MasterPfs";
    public const string DetailQuickModeSessions = "IPSec_Detail_QuickModeSessions";
    public const string DetailMainModeLifetime = "IPSec_Detail_MainModeLifetime";
    public const string DetailDefaultResponseRule = "IPSec_Detail_DefaultResponseRule";
    public const string DetailPollingInterval = "IPSec_Detail_PollingInterval";
    public const string DetailSecurityMethods = "IPSec_Detail_SecurityMethods";
    public const string DetailFilterCount = "IPSec_Detail_FilterCount";
    public const string DetailFilters = "IPSec_Detail_Filters";
    public const string DetailAction = "IPSec_Detail_Action";
    public const string DetailQuickModePfs = "IPSec_Detail_QuickModePfs";
    public const string DetailAcceptUnsecuredInbound = "IPSec_Detail_AcceptUnsecuredInbound";
    public const string DetailAllowUnsecuredFallback = "IPSec_Detail_AllowUnsecuredFallback";
    public const string EditorValidationInvalid = "IPSec_Editor_ValidationInvalid";
    public const string EditorAssigned = "IPSec_Editor_Assigned";
    public const string EditorDefaultResponseRule = "IPSec_Editor_DefaultResponseRule";
    public const string EditorMasterPfs = "IPSec_Editor_MasterPfs";
    public const string EditorQuickModeSessions = "IPSec_Editor_QuickModeSessions";
    public const string EditorMainModeLifetime = "IPSec_Editor_MainModeLifetime";
    public const string EditorPollingInterval = "IPSec_Editor_PollingInterval";
    public const string EditorMainModeMethods = "IPSec_Editor_MainModeMethods";
    public const string EditorMethodsHelp = "IPSec_Editor_MethodsHelp";
    public const string EditorFilters = "IPSec_Editor_Filters";
    public const string EditorAddFilterTitle = "IPSec_Editor_AddFilterTitle";
    public const string EditorEditFilterTitle = "IPSec_Editor_EditFilterTitle";
    public const string EditorFilterSummaryFormat = "IPSec_Editor_FilterSummaryFormat";
    public const string EditorSourceAddress = "IPSec_Editor_SourceAddress";
    public const string EditorDestinationAddress = "IPSec_Editor_DestinationAddress";
    public const string EditorSourceMask = "IPSec_Editor_SourceMask";
    public const string EditorDestinationMask = "IPSec_Editor_DestinationMask";
    public const string EditorProtocol = "IPSec_Editor_Protocol";
    public const string EditorMirrored = "IPSec_Editor_Mirrored";
    public const string EditorSourcePort = "IPSec_Editor_SourcePort";
    public const string EditorDestinationPort = "IPSec_Editor_DestinationPort";
    public const string EditorFilterAction = "IPSec_Editor_FilterAction";
    public const string EditorQuickModePfs = "IPSec_Editor_QuickModePfs";
    public const string EditorAcceptUnsecuredInbound = "IPSec_Editor_AcceptUnsecuredInbound";
    public const string EditorAllowUnsecuredFallback = "IPSec_Editor_AllowUnsecuredFallback";
    public const string EditorQuickModeMethods = "IPSec_Editor_QuickModeMethods";
    public const string EditorEncryptionAlgorithm = "IPSec_Editor_EncryptionAlgorithm";
    public const string EditorHashAlgorithm = "IPSec_Editor_HashAlgorithm";
    public const string EditorDiffieHellmanGroup = "IPSec_Editor_DiffieHellmanGroup";
    public const string EditorSecurityMethodType = "IPSec_Editor_SecurityMethodType";
    public const string EditorEspEncryption = "IPSec_Editor_EspEncryption";
    public const string EditorEspAuthentication = "IPSec_Editor_EspAuthentication";
    public const string EditorAhHash = "IPSec_Editor_AhHash";
    public const string EditorLifetimeKilobytes = "IPSec_Editor_LifetimeKilobytes";
    public const string EditorLifetimeSeconds = "IPSec_Editor_LifetimeSeconds";
    public const string EditorAddSecurityMethod = "IPSec_Editor_AddSecurityMethod";
    public const string EditorAlgorithm3Des = "IPSec_Editor_Algorithm_3DES";
    public const string EditorAlgorithmDes = "IPSec_Editor_Algorithm_DES";
    public const string EditorAlgorithmNone = "IPSec_Editor_Algorithm_None";
    public const string EditorAlgorithmSha1 = "IPSec_Editor_Algorithm_SHA1";
    public const string EditorAlgorithmMd5 = "IPSec_Editor_Algorithm_MD5";
    public const string EditorDhGroupDh2048 = "IPSec_Editor_DhGroup_DH2048";
    public const string EditorDhGroupMedium = "IPSec_Editor_DhGroup_Medium";
    public const string EditorDhGroupLow = "IPSec_Editor_DhGroup_Low";
    public const string EditorMethodTypeEsp = "IPSec_Editor_MethodType_ESP";
    public const string EditorMethodTypeAh = "IPSec_Editor_MethodType_AH";
    public const string EditorMethodTypeAhEsp = "IPSec_Editor_MethodType_AH_ESP";
    public const string EditorRuleFilterList = "IPSec_Editor_RuleFilterList";
    public const string EditorRuleFilterAction = "IPSec_Editor_RuleFilterAction";
    public const string EditorConnectionType = "IPSec_Editor_ConnectionType";
    public const string EditorConnectionAll = "IPSec_Editor_ConnectionAll";
    public const string EditorConnectionLan = "IPSec_Editor_ConnectionLan";
    public const string EditorConnectionDialUp = "IPSec_Editor_ConnectionDialUp";
    public const string EditorRuleActive = "IPSec_Editor_RuleActive";
    public const string EditorTunnel = "IPSec_Editor_Tunnel";
    public const string EditorTunnelEndpoint = "IPSec_Editor_TunnelEndpoint";
    public const string EditorAuthenticationMethods = "IPSec_Editor_AuthenticationMethods";
    public const string EditorAuthenticationKind = "IPSec_Editor_AuthenticationKind";
    public const string EditorAuthenticationKerberos = "IPSec_Editor_AuthenticationKerberos";
    public const string EditorAuthenticationCertificate = "IPSec_Editor_AuthenticationCertificate";
    public const string EditorAuthenticationPsk = "IPSec_Editor_AuthenticationPsk";
    public const string EditorCertificateAuthorityName = "IPSec_Editor_CertificateAuthorityName";
    public const string EditorCertificateMapping = "IPSec_Editor_CertificateMapping";
    public const string EditorExcludeCertificateAuthorityName = "IPSec_Editor_ExcludeCertificateAuthorityName";
    public const string EditorPreSharedKey = "IPSec_Editor_PreSharedKey";
    public const string EditorPskReentryRequired = "IPSec_Editor_PskReentryRequired";
    public const string EditorPskConfigured = "IPSec_Editor_PskConfigured";
    public const string EditorAddOrReplaceAuthentication = "IPSec_Editor_AddOrReplaceAuthentication";
    public const string EditorMoveUp = "IPSec_Editor_MoveUp";
    public const string EditorMoveDown = "IPSec_Editor_MoveDown";
    public const string CommandNew = "IPSec_Command_New";
    public const string CommandEdit = "IPSec_Command_Edit";
    public const string CommandDelete = "IPSec_Command_Delete";
    public const string CommandAssign = "IPSec_Command_Assign";
    public const string CommandUnassign = "IPSec_Command_Unassign";
    public const string CommandAddRule = "IPSec_Command_AddRule";
    public const string CommandEditRule = "IPSec_Command_EditRule";
    public const string CommandDeleteRule = "IPSec_Command_DeleteRule";
    public const string CommandManageRules = "IPSec_Command_ManageRules";
    public const string CommandManageFiltersActions = "IPSec_Command_ManageFiltersActions";
    public const string DialogManageFiltersActionsTitle = "IPSec_Dialog_ManageFiltersActions_Title";
    public const string DeleteConfirmTitle = "IPSec_DeleteConfirm_Title";
    public const string DeletePolicyMessageFormat = "IPSec_DeletePolicy_MessageFormat";
    public const string DeleteFilterListMessageFormat = "IPSec_DeleteFilterList_MessageFormat";
    public const string DeleteFilterActionMessageFormat = "IPSec_DeleteFilterAction_MessageFormat";
    public const string DeleteRuleMessageFormat = "IPSec_DeleteRule_MessageFormat";
    public const string DialogCreatePolicyTitle = "IPSec_Dialog_CreatePolicy_Title";
    public const string DialogEditPolicyTitleFormat = "IPSec_Dialog_EditPolicy_TitleFormat";
    public const string DialogCreateFilterListTitle = "IPSec_Dialog_CreateFilterList_Title";
    public const string DialogEditFilterListTitleFormat = "IPSec_Dialog_EditFilterList_TitleFormat";
    public const string DialogCreateFilterActionTitle = "IPSec_Dialog_CreateFilterAction_Title";
    public const string DialogEditFilterActionTitleFormat = "IPSec_Dialog_EditFilterAction_TitleFormat";
    public const string DialogCreateRuleTitleFormat = "IPSec_Dialog_CreateRule_TitleFormat";
    public const string DialogEditRuleTitleFormat = "IPSec_Dialog_EditRule_TitleFormat";
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
/// Resource key constants for Shared Folders.
/// </summary>
public static class FsMgmtKeys
{
    public const string PageSearchPlaceholder = "FsMgmt_Page_SearchPlaceholder";
    public const string SharesHeader = "FsMgmt_Shares_Header";
    public const string SessionsHeader = "FsMgmt_Sessions_Header";
    public const string OpenFilesHeader = "FsMgmt_OpenFiles_Header";
    public const string ComputerNameLabel = "FsMgmt_ComputerName_Label";
    public const string ShareInformationHeader = "FsMgmt_ShareInformation_Header";
    public const string SharePathLabel = "FsMgmt_SharePath_Label";
    public const string FolderPathPlaceholder = "FsMgmt_FolderPath_Placeholder";
    public const string PermissionsSecurityHeader = "FsMgmt_PermissionsSecurity_Header";
    public const string SharesCountFormat = "FsMgmt_Shares_CountFormat";
    public const string SessionsCountFormat = "FsMgmt_Sessions_CountFormat";
    public const string OpenFilesCountFormat = "FsMgmt_OpenFiles_CountFormat";
    public const string EmptyShares = "FsMgmt_Empty_Shares";
    public const string EmptySessions = "FsMgmt_Empty_Sessions";
    public const string EmptyOpenFiles = "FsMgmt_Empty_OpenFiles";
    public const string ShareDetailsFormat = "FsMgmt_Share_DetailsFormat";
    public const string SessionDetailsFormat = "FsMgmt_Session_DetailsFormat";
    public const string OpenFileDetailsFormat = "FsMgmt_OpenFile_DetailsFormat";
    public const string ShareTypeDisk = "FsMgmt_ShareType_Disk";
    public const string ShareTypePrint = "FsMgmt_ShareType_Print";
    public const string ShareTypeDevice = "FsMgmt_ShareType_Device";
    public const string ShareTypeIpc = "FsMgmt_ShareType_Ipc";
    public const string ShareTypeUnknown = "FsMgmt_ShareType_Unknown";
    public const string NotAvailable = "FsMgmt_NotAvailable";
    public const string UserLimitMaximumAllowed = "FsMgmt_UserLimit_MaximumAllowed";
    public const string OfflineManual = "FsMgmt_Offline_Manual";
    public const string OfflineNone = "FsMgmt_Offline_None";
    public const string OfflineAutomatic = "FsMgmt_Offline_Automatic";
    public const string FilePermissionRead = "FsMgmt_FilePermission_Read";
    public const string FilePermissionWrite = "FsMgmt_FilePermission_Write";
    public const string FilePermissionReadWrite = "FsMgmt_FilePermission_ReadWrite";
    public const string GuestYes = "FsMgmt_Guest_Yes";
    public const string GuestNo = "FsMgmt_Guest_No";
    public const string PermissionAllow = "FsMgmt_Permissions_Allow";
    public const string PermissionDeny = "FsMgmt_Permissions_Deny";
    public const string PermissionRead = "FsMgmt_Permissions_Read";
    public const string PermissionChange = "FsMgmt_Permissions_Change";
    public const string PermissionFullControl = "FsMgmt_Permissions_FullControl";
    public const string PermissionAddButton = "FsMgmt_Permissions_AddButton";
    public const string PermissionSecurityTab = "FsMgmt_Permissions_SecurityTab";
    public const string PermissionShareTab = "FsMgmt_Permissions_ShareTab";
    public const string PermissionForFormat = "FsMgmt_Permissions_ForFormat";
    public const string PermissionObjectName = "FsMgmt_Permissions_ObjectName";
    public const string PermissionEditInstruction = "FsMgmt_Permissions_EditInstruction";
    public const string PermissionEditButton = "FsMgmt_Permissions_EditButton";
    public const string PermissionAdvancedInstruction = "FsMgmt_Permissions_AdvancedInstruction";
    public const string PermissionModify = "FsMgmt_Permissions_Modify";
    public const string PermissionReadExecute = "FsMgmt_Permissions_ReadExecute";
    public const string PermissionListFolder = "FsMgmt_Permissions_ListFolder";
    public const string PermissionWrite = "FsMgmt_Permissions_Write";
    public const string PermissionDelete = "FsMgmt_Permissions_Delete";
    public const string PermissionThisFolderOnly = "FsMgmt_Permissions_ThisFolderOnly";
    public const string PermissionThisFolderSubfoldersFiles = "FsMgmt_Permissions_ThisFolderSubfoldersFiles";
    public const string PermissionThisFolderSubfolders = "FsMgmt_Permissions_ThisFolderSubfolders";
    public const string PermissionThisFolderFiles = "FsMgmt_Permissions_ThisFolderFiles";
    public const string PermissionSubfoldersFilesOnly = "FsMgmt_Permissions_SubfoldersFilesOnly";
    public const string PermissionSubfoldersOnly = "FsMgmt_Permissions_SubfoldersOnly";
    public const string PermissionFilesOnly = "FsMgmt_Permissions_FilesOnly";
    public const string DurationDaysFormat = "FsMgmt_Duration_DaysFormat";
    public const string DurationHoursFormat = "FsMgmt_Duration_HoursFormat";
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

