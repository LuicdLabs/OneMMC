namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.SystemAudit;

internal static class SystemAuditPersistenceConstants
{
    internal const string AuditCsvHeader = "Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value";
    internal const string SystemPolicyTarget = "System";
    internal const string FileGlobalSaclName = "FileGlobalSacl";
    internal const string RegistryGlobalSaclName = "RegistryGlobalSacl";
    internal const string AuditMachineExtensionPair = "[{F3CCC681-B74C-4060-9F26-CD84525DCA2A}{0F3F3735-573D-9804-99E4-AB2A69BA5FD4}]";
}
