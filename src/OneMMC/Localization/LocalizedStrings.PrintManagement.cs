using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for Print Management.
    /// Resources are loaded from PrintManagement.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Print Management - Page Header
        public string PrintMgmt_CurrentPrintServerFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_CurrentPrintServerFormat");
        public string PrintMgmt_OpenLegacy => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_OpenLegacy");

        // Print Management - Section Headers
        public string PrintMgmt_PrintersHeader => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_PrintersHeader");
        public string PrintMgmt_DeployedPrintersHeader => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployedPrintersHeader");
        public string PrintMgmt_DriversHeader => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriversHeader");
        public string PrintMgmt_ModifyPrintPorts => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_ModifyPrintPorts");
        public string PrintMgmt_ViewEditPrintForms => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_ViewEditPrintForms");

        // Print Management - Section Descriptions
        public string PrintMgmt_PrintersCountFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_PrintersCountFormat");
        public string PrintMgmt_DeployedPrintersCountFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployedPrintersCountFormat");
        public string PrintMgmt_DriversCountFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriversCountFormat");

        // Print Management - Printer Info
        public string PrintMgmt_StatusFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_StatusFormat");
        public string PrintMgmt_DefaultPrinter => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DefaultPrinter");

        // Print Management - Driver Info
        public string PrintMgmt_DriverInfoFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverInfoFormat");
        public string PrintMgmt_IsolationSuffix => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_IsolationSuffix");

        // Print Management - Buttons
        public string PrintMgmt_PortsButton => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_PortsButton");
        public string PrintMgmt_FormsButton => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_FormsButton");

        // Print Management - Menu Items
        public string PrintMgmt_MenuOpenPrinterQueue => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuOpenPrinterQueue");
        public string PrintMgmt_MenuPausePrinting => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuPausePrinting");
        public string PrintMgmt_MenuResumePrinting => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuResumePrinting");
        public string PrintMgmt_MenuDeployWithGroupPolicy => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuDeployWithGroupPolicy");
        public string PrintMgmt_MenuSetPrintingDefaults => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuSetPrintingDefaults");
        public string PrintMgmt_MenuPrintTestPage => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuPrintTestPage");
        public string PrintMgmt_MenuProperties => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuProperties");
        public string PrintMgmt_MenuRename => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuRename");
        public string PrintMgmt_MenuRemoveDriverPackage => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuRemoveDriverPackage");
        public string PrintMgmt_MenuSetDriverIsolation => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_MenuSetDriverIsolation");
        public string PrintMgmt_IsolationNone => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_IsolationNone");
        public string PrintMgmt_IsolationShared => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_IsolationShared");
        public string PrintMgmt_IsolationIsolated => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_IsolationIsolated");
        public string PrintMgmt_IsolationSystemDefault => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_IsolationSystemDefault");

        // Print Management - Port Dialog
        public string PrintMgmt_PortsDialogTitle => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_PortsDialogTitle");
        public string PrintMgmt_PortsCountFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_PortsCountFormat");
        public string PrintMgmt_PortPrinterFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_PortPrinterFormat");
        public string PrintMgmt_NoPrinterAssigned => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_NoPrinterAssigned");

        // Print Management - Deployed Printers Format
        public string PrintMgmt_DeployedPrinterServer => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployedPrinterServer");
        public string PrintMgmt_DeployedPrinterPerUserGPO => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployedPrinterPerUserGPO");
        public string PrintMgmt_DeployedPrinterPerComputerGPO => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployedPrinterPerComputerGPO");

        // Print Management - Form Dialog
        public string PrintMgmt_FormsDialogTitle => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_FormsDialogTitle");
        public string PrintMgmt_FormsCountFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_FormsCountFormat");
        public string PrintMgmt_FormSizeFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_FormSizeFormat");

        // Print Management - Status Messages
        public string PrintMgmt_Loading => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_Loading");
        public string PrintMgmt_LoadedFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_LoadedFormat");
        public string PrintMgmt_ErrorLoadingFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_ErrorLoadingFormat");
        public string PrintMgmt_ActionFailedFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_ActionFailedFormat");
        public string PrintMgmt_ErrorDriverInUse => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_ErrorDriverInUse");
        public string PrintMgmt_ErrorDriverInBox => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_ErrorDriverInBox");



        // Print Management - Deployment Dialog
        public string PrintMgmt_DeployDialogTitleFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogTitleFormat");
        public string PrintMgmt_DeployDialogPrinterNameLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogPrinterNameLabel");
        public string PrintMgmt_DeployDialogGpoSectionHeader => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogGpoSectionHeader");
        public string PrintMgmt_DeployDialogGpoNameLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogGpoNameLabel");
        public string PrintMgmt_DeployDialogBrowseButton => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogBrowseButton");
        public string PrintMgmt_DeployDialogAddButton => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogAddButton");
        public string PrintMgmt_DeployDialogRemoveButton => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogRemoveButton");
        public string PrintMgmt_DeployDialogRemoveAllButton => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogRemoveAllButton");
        public string PrintMgmt_DeployDialogApplyButton => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogApplyButton");
        public string PrintMgmt_DeployDialogDeployToLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogDeployToLabel");
        public string PrintMgmt_DeployDialogPerUser => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogPerUser");
        public string PrintMgmt_DeployDialogPerMachine => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogPerMachine");
        public string PrintMgmt_DeployDialogListPrinterNameHeader => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogListPrinterNameHeader");
        public string PrintMgmt_DeployDialogListGpoHeader => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogListGpoHeader");
        public string PrintMgmt_DeployDialogListConnectionTypeHeader => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogListConnectionTypeHeader");
        public string PrintMgmt_DeployDialogConnectionTypePerUser => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogConnectionTypePerUser");
        public string PrintMgmt_DeployDialogConnectionTypePerMachine => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogConnectionTypePerMachine");
        public string PrintMgmt_DeployDialogDomainRequired => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogDomainRequired");
        public string PrintMgmt_GpoBrowseDialogTitle => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_GpoBrowseDialogTitle");
        public string PrintMgmt_GpoBrowseDialogEmpty => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_GpoBrowseDialogEmpty");
        public string PrintMgmt_DeployDialogDescription => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogDescription");
        public string PrintMgmt_DeployDialogConnectionPathLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogConnectionPathLabel");
        public string PrintMgmt_DeployDialogCurrentUser => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogCurrentUser");
        public string PrintMgmt_DeployDialogAllUsers => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogAllUsers");
        public string PrintMgmt_DeployDialogNote => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogNote");
        public string PrintMgmt_DeployDialogUnavailable => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeployDialogUnavailable");

        // Print Management - Confirmation / Input Dialogs
        public string PrintMgmt_DeletePrinterTitleFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeletePrinterTitleFormat");
        public string PrintMgmt_DeletePrinterMessageFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeletePrinterMessageFormat");
        public string PrintMgmt_RenamePrinterTitleFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_RenamePrinterTitleFormat");
        public string PrintMgmt_RenamePrinterInstruction => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_RenamePrinterInstruction");
        public string PrintMgmt_RemoveDriverPackageTitleFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_RemoveDriverPackageTitleFormat");
        public string PrintMgmt_RemoveDriverPackageMessageFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_RemoveDriverPackageMessageFormat");
        public string PrintMgmt_DeleteDriverTitleFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeleteDriverTitleFormat");
        public string PrintMgmt_DeleteDriverMessageFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DeleteDriverMessageFormat");

        // Print Management - Driver Properties Dialog
        public string PrintMgmt_DriverPropertiesTitleFormat => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertiesTitleFormat");
        public string PrintMgmt_DriverPropertyNameLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyNameLabel");
        public string PrintMgmt_DriverPropertyInfLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyInfLabel");
        public string PrintMgmt_DriverPropertyVersionLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyVersionLabel");
        public string PrintMgmt_DriverPropertyEnvironmentLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyEnvironmentLabel");
        public string PrintMgmt_DriverPropertyIsolationLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyIsolationLabel");
        public string PrintMgmt_DriverPropertyPathLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyPathLabel");
        public string PrintMgmt_DriverPropertyDataFileLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyDataFileLabel");
        public string PrintMgmt_DriverPropertyConfigFileLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyConfigFileLabel");
        public string PrintMgmt_DriverPropertyMonitorLabel => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_DriverPropertyMonitorLabel");

        // Print Management - Empty States
        public string PrintMgmt_NoPrintersFound => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_NoPrintersFound");
        public string PrintMgmt_NoDeployedPrintersFound => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_NoDeployedPrintersFound");
        public string PrintMgmt_NoDriversFound => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_NoDriversFound");
        public string PrintMgmt_NoPortsFound => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_NoPortsFound");
        public string PrintMgmt_NoFormsFound => GetResource(ResourceFileNames.PrintManagement, "PrintMgmt_NoFormsFound");
    }
}
