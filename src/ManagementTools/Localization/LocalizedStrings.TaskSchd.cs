using ManagementTools.Core.Localization;

namespace ManagementTools.Localization
{
    /// <summary>
    /// Localized strings for the Task Scheduler (taskschd.msc) feature.
    /// Resources are loaded from TaskSchd.resw.
    /// </summary>
    public partial class LocalizedStrings
    {
        private static string TaskSchd(string key) => GetResource(ResourceFileNames.TaskSchd, key);

        public string TaskSchd_LibraryRoot => TaskSchd(TaskSchdKeys.LibraryRoot);

        public string TaskSchd_State_Ready => TaskSchd(TaskSchdKeys.StateReady);
        public string TaskSchd_State_Running => TaskSchd(TaskSchdKeys.StateRunning);
        public string TaskSchd_State_Disabled => TaskSchd(TaskSchdKeys.StateDisabled);
        public string TaskSchd_State_Queued => TaskSchd(TaskSchdKeys.StateQueued);
        public string TaskSchd_State_Unknown => TaskSchd(TaskSchdKeys.StateUnknown);

        public string TaskSchd_Column_Name => TaskSchd(TaskSchdKeys.ColumnName);
        public string TaskSchd_Column_Status => TaskSchd(TaskSchdKeys.ColumnStatus);
        public string TaskSchd_Column_Triggers => TaskSchd(TaskSchdKeys.ColumnTriggers);
        public string TaskSchd_Column_NextRun => TaskSchd(TaskSchdKeys.ColumnNextRun);
        public string TaskSchd_Column_LastRun => TaskSchd(TaskSchdKeys.ColumnLastRun);
        public string TaskSchd_Column_LastResult => TaskSchd(TaskSchdKeys.ColumnLastResult);
        public string TaskSchd_Column_Author => TaskSchd(TaskSchdKeys.ColumnAuthor);

        public string TaskSchd_Command_CreateTask => TaskSchd(TaskSchdKeys.CommandCreateTask);
        public string TaskSchd_Command_Run => TaskSchd(TaskSchdKeys.CommandRun);
        public string TaskSchd_Command_End => TaskSchd(TaskSchdKeys.CommandEnd);
        public string TaskSchd_Command_Enable => TaskSchd(TaskSchdKeys.CommandEnable);
        public string TaskSchd_Command_Disable => TaskSchd(TaskSchdKeys.CommandDisable);
        public string TaskSchd_Command_Delete => TaskSchd(TaskSchdKeys.CommandDelete);
        public string TaskSchd_Command_Properties => TaskSchd(TaskSchdKeys.CommandProperties);
        public string TaskSchd_Command_Refresh => TaskSchd(TaskSchdKeys.CommandRefresh);
        public string TaskSchd_Command_NewFolder => TaskSchd(TaskSchdKeys.CommandNewFolder);
        public string TaskSchd_Command_DeleteFolder => TaskSchd(TaskSchdKeys.CommandDeleteFolder);
        public string TaskSchd_Command_ImportTask => TaskSchd(TaskSchdKeys.CommandImportTask);
        public string TaskSchd_Command_ExportTask => TaskSchd(TaskSchdKeys.CommandExportTask);
        public string TaskSchd_Command_ConnectComputer => TaskSchd(TaskSchdKeys.CommandConnectComputer);
        public string TaskSchd_Command_EnableAllHistory => TaskSchd(TaskSchdKeys.CommandEnableAllHistory);
        public string TaskSchd_Command_DisableAllHistory => TaskSchd(TaskSchdKeys.CommandDisableAllHistory);
        public string TaskSchd_Command_More => TaskSchd(TaskSchdKeys.CommandMore);
        public string TaskSchd_Command_Security => TaskSchd(TaskSchdKeys.CommandSecurity);

        public string TaskSchd_Tab_General => TaskSchd(TaskSchdKeys.TabGeneral);
        public string TaskSchd_Tab_Triggers => TaskSchd(TaskSchdKeys.TabTriggers);
        public string TaskSchd_Tab_Actions => TaskSchd(TaskSchdKeys.TabActions);
        public string TaskSchd_Tab_Conditions => TaskSchd(TaskSchdKeys.TabConditions);
        public string TaskSchd_Tab_Settings => TaskSchd(TaskSchdKeys.TabSettings);
        public string TaskSchd_Tab_History => TaskSchd(TaskSchdKeys.TabHistory);

        public string TaskSchd_General_Name => TaskSchd(TaskSchdKeys.GeneralName);
        public string TaskSchd_General_Location => TaskSchd(TaskSchdKeys.GeneralLocation);
        public string TaskSchd_General_Author => TaskSchd(TaskSchdKeys.GeneralAuthor);
        public string TaskSchd_General_Description => TaskSchd(TaskSchdKeys.GeneralDescription);
        public string TaskSchd_General_SecurityOptions => TaskSchd(TaskSchdKeys.GeneralSecurityOptions);
        public string TaskSchd_General_RunAsAccount => TaskSchd(TaskSchdKeys.GeneralRunAsAccount);
        public string TaskSchd_General_ChangeUser => TaskSchd(TaskSchdKeys.GeneralChangeUser);
        public string TaskSchd_General_RunOnlyLoggedOn => TaskSchd(TaskSchdKeys.GeneralRunOnlyLoggedOn);
        public string TaskSchd_General_RunWhetherLoggedOn => TaskSchd(TaskSchdKeys.GeneralRunWhetherLoggedOn);
        public string TaskSchd_General_DoNotStorePassword => TaskSchd(TaskSchdKeys.GeneralDoNotStorePassword);
        public string TaskSchd_General_RunHighestPrivileges => TaskSchd(TaskSchdKeys.GeneralRunHighestPrivileges);
        public string TaskSchd_General_Hidden => TaskSchd(TaskSchdKeys.GeneralHidden);
        public string TaskSchd_General_ConfigureFor => TaskSchd(TaskSchdKeys.GeneralConfigureFor);

        public string TaskSchd_Trigger_OnSchedule => TaskSchd(TaskSchdKeys.TriggerOnSchedule);
        public string TaskSchd_Trigger_AtLogon => TaskSchd(TaskSchdKeys.TriggerAtLogon);
        public string TaskSchd_Trigger_AtStartup => TaskSchd(TaskSchdKeys.TriggerAtStartup);
        public string TaskSchd_Trigger_OnIdle => TaskSchd(TaskSchdKeys.TriggerOnIdle);
        public string TaskSchd_Trigger_OnEvent => TaskSchd(TaskSchdKeys.TriggerOnEvent);
        public string TaskSchd_Trigger_AtCreation => TaskSchd(TaskSchdKeys.TriggerAtCreation);
        public string TaskSchd_Trigger_OnConnect => TaskSchd(TaskSchdKeys.TriggerOnConnect);
        public string TaskSchd_Trigger_OnDisconnect => TaskSchd(TaskSchdKeys.TriggerOnDisconnect);
        public string TaskSchd_Trigger_OnLock => TaskSchd(TaskSchdKeys.TriggerOnLock);
        public string TaskSchd_Trigger_OnUnlock => TaskSchd(TaskSchdKeys.TriggerOnUnlock);
        public string TaskSchd_Schedule_OneTime => TaskSchd(TaskSchdKeys.ScheduleOneTime);
        public string TaskSchd_Schedule_Daily => TaskSchd(TaskSchdKeys.ScheduleDaily);
        public string TaskSchd_Schedule_Weekly => TaskSchd(TaskSchdKeys.ScheduleWeekly);
        public string TaskSchd_Schedule_Monthly => TaskSchd(TaskSchdKeys.ScheduleMonthly);

        public string TaskSchd_TriggerCol_Details => TaskSchd(TaskSchdKeys.TriggerColDetails);
        public string TaskSchd_TriggerCol_Status => TaskSchd(TaskSchdKeys.TriggerColStatus);
        public string TaskSchd_Trigger_New => TaskSchd(TaskSchdKeys.TriggerNew);
        public string TaskSchd_Trigger_Edit => TaskSchd(TaskSchdKeys.TriggerEdit);
        public string TaskSchd_Trigger_Delete => TaskSchd(TaskSchdKeys.TriggerDelete);
        public string TaskSchd_Trigger_BeginLabel => TaskSchd(TaskSchdKeys.TriggerBeginLabel);
        public string TaskSchd_Trigger_Delay => TaskSchd(TaskSchdKeys.TriggerDelay);
        public string TaskSchd_Trigger_RandomDelay => TaskSchd(TaskSchdKeys.TriggerRandomDelay);
        public string TaskSchd_Trigger_RepeatEvery => TaskSchd(TaskSchdKeys.TriggerRepeatEvery);
        public string TaskSchd_Trigger_ForDuration => TaskSchd(TaskSchdKeys.TriggerForDuration);
        public string TaskSchd_Trigger_StopAllAtEnd => TaskSchd(TaskSchdKeys.TriggerStopAllAtEnd);
        public string TaskSchd_Trigger_StopIfLonger => TaskSchd(TaskSchdKeys.TriggerStopIfLonger);
        public string TaskSchd_Trigger_Activate => TaskSchd(TaskSchdKeys.TriggerActivate);
        public string TaskSchd_Trigger_Expire => TaskSchd(TaskSchdKeys.TriggerExpire);
        public string TaskSchd_Trigger_SyncTimeZones => TaskSchd(TaskSchdKeys.TriggerSyncTimeZones);
        public string TaskSchd_Trigger_Enabled => TaskSchd(TaskSchdKeys.TriggerEnabled);

        public string TaskSchd_Action_StartProgram => TaskSchd(TaskSchdKeys.ActionStartProgram);
        public string TaskSchd_Action_SendEmail => TaskSchd(TaskSchdKeys.ActionSendEmail);
        public string TaskSchd_Action_DisplayMessage => TaskSchd(TaskSchdKeys.ActionDisplayMessage);
        public string TaskSchd_ActionCol_Details => TaskSchd(TaskSchdKeys.ActionColDetails);
        public string TaskSchd_Action_New => TaskSchd(TaskSchdKeys.ActionNew);
        public string TaskSchd_Action_Edit => TaskSchd(TaskSchdKeys.ActionEdit);
        public string TaskSchd_Action_Delete => TaskSchd(TaskSchdKeys.ActionDelete);
        public string TaskSchd_Action_MoveUp => TaskSchd(TaskSchdKeys.ActionMoveUp);
        public string TaskSchd_Action_MoveDown => TaskSchd(TaskSchdKeys.ActionMoveDown);
        public string TaskSchd_Action_ProgramScript => TaskSchd(TaskSchdKeys.ActionProgramScript);
        public string TaskSchd_Action_AddArguments => TaskSchd(TaskSchdKeys.ActionAddArguments);
        public string TaskSchd_Action_StartIn => TaskSchd(TaskSchdKeys.ActionStartIn);
        public string TaskSchd_Action_Browse => TaskSchd(TaskSchdKeys.ActionBrowse);
        public string TaskSchd_Action_EmailFrom => TaskSchd(TaskSchdKeys.ActionEmailFrom);
        public string TaskSchd_Action_EmailTo => TaskSchd(TaskSchdKeys.ActionEmailTo);
        public string TaskSchd_Action_EmailSubject => TaskSchd(TaskSchdKeys.ActionEmailSubject);
        public string TaskSchd_Action_EmailText => TaskSchd(TaskSchdKeys.ActionEmailText);
        public string TaskSchd_Action_EmailAttachment => TaskSchd(TaskSchdKeys.ActionEmailAttachment);
        public string TaskSchd_Action_EmailServer => TaskSchd(TaskSchdKeys.ActionEmailServer);
        public string TaskSchd_Action_MessageTitle => TaskSchd(TaskSchdKeys.ActionMessageTitle);
        public string TaskSchd_Action_MessageBody => TaskSchd(TaskSchdKeys.ActionMessageBody);

        public string TaskSchd_Conditions_Idle => TaskSchd(TaskSchdKeys.ConditionsIdle);
        public string TaskSchd_Conditions_Power => TaskSchd(TaskSchdKeys.ConditionsPower);
        public string TaskSchd_Conditions_Network => TaskSchd(TaskSchdKeys.ConditionsNetwork);
        public string TaskSchd_Conditions_StartIfIdle => TaskSchd(TaskSchdKeys.ConditionsStartIfIdle);
        public string TaskSchd_Conditions_WaitForIdle => TaskSchd(TaskSchdKeys.ConditionsWaitForIdle);
        public string TaskSchd_Conditions_StopIfNotIdle => TaskSchd(TaskSchdKeys.ConditionsStopIfNotIdle);
        public string TaskSchd_Conditions_RestartOnIdle => TaskSchd(TaskSchdKeys.ConditionsRestartOnIdle);
        public string TaskSchd_Conditions_StartOnAcPower => TaskSchd(TaskSchdKeys.ConditionsStartOnAcPower);
        public string TaskSchd_Conditions_StopOnBattery => TaskSchd(TaskSchdKeys.ConditionsStopOnBattery);
        public string TaskSchd_Conditions_WakeToRun => TaskSchd(TaskSchdKeys.ConditionsWakeToRun);
        public string TaskSchd_Conditions_StartIfNetwork => TaskSchd(TaskSchdKeys.ConditionsStartIfNetwork);
        public string TaskSchd_Conditions_AnyConnection => TaskSchd(TaskSchdKeys.ConditionsAnyConnection);

        public string TaskSchd_Settings_AllowDemandStart => TaskSchd(TaskSchdKeys.SettingsAllowDemandStart);
        public string TaskSchd_Settings_StartWhenAvailable => TaskSchd(TaskSchdKeys.SettingsStartWhenAvailable);
        public string TaskSchd_Settings_RestartOnFailure => TaskSchd(TaskSchdKeys.SettingsRestartOnFailure);
        public string TaskSchd_Settings_RestartInterval => TaskSchd(TaskSchdKeys.SettingsRestartInterval);
        public string TaskSchd_Settings_RestartCount => TaskSchd(TaskSchdKeys.SettingsRestartCount);
        public string TaskSchd_Settings_StopIfLonger => TaskSchd(TaskSchdKeys.SettingsStopIfLonger);
        public string TaskSchd_Settings_ForceStop => TaskSchd(TaskSchdKeys.SettingsForceStop);
        public string TaskSchd_Settings_DeleteAfter => TaskSchd(TaskSchdKeys.SettingsDeleteAfter);
        public string TaskSchd_Settings_InstanceRule => TaskSchd(TaskSchdKeys.SettingsInstanceRule);
        public string TaskSchd_Instance_Parallel => TaskSchd(TaskSchdKeys.InstanceParallel);
        public string TaskSchd_Instance_Queue => TaskSchd(TaskSchdKeys.InstanceQueue);
        public string TaskSchd_Instance_IgnoreNew => TaskSchd(TaskSchdKeys.InstanceIgnoreNew);
        public string TaskSchd_Instance_StopExisting => TaskSchd(TaskSchdKeys.InstanceStopExisting);

        public string TaskSchd_Dialog_CreateTask => TaskSchd(TaskSchdKeys.DialogCreateTask);
        public string TaskSchd_Dialog_NewTrigger => TaskSchd(TaskSchdKeys.DialogNewTrigger);
        public string TaskSchd_Dialog_EditTrigger => TaskSchd(TaskSchdKeys.DialogEditTrigger);
        public string TaskSchd_Dialog_NewAction => TaskSchd(TaskSchdKeys.DialogNewAction);
        public string TaskSchd_Dialog_EditAction => TaskSchd(TaskSchdKeys.DialogEditAction);
        public string TaskSchd_Dialog_NewEventFilter => TaskSchd(TaskSchdKeys.DialogNewEventFilter);
        public string TaskSchd_Dialog_ConnectComputer => TaskSchd(TaskSchdKeys.DialogConnectComputer);
        public string TaskSchd_Button_Ok => TaskSchd(TaskSchdKeys.ButtonOk);
        public string TaskSchd_Button_Cancel => TaskSchd(TaskSchdKeys.ButtonCancel);
        public string TaskSchd_Button_Apply => TaskSchd(TaskSchdKeys.ButtonApply);

        public string TaskSchd_History_Disabled => TaskSchd(TaskSchdKeys.HistoryDisabled);
        public string TaskSchd_History_ColLevel => TaskSchd(TaskSchdKeys.HistoryColLevel);
        public string TaskSchd_History_ColDate => TaskSchd(TaskSchdKeys.HistoryColDate);
        public string TaskSchd_History_ColEvent => TaskSchd(TaskSchdKeys.HistoryColEvent);
        public string TaskSchd_History_ColCategory => TaskSchd(TaskSchdKeys.HistoryColCategory);

        public string TaskSchd_Connect_LocalComputer => TaskSchd(TaskSchdKeys.ConnectLocalComputer);
        public string TaskSchd_Connect_AnotherComputer => TaskSchd(TaskSchdKeys.ConnectAnotherComputer);
        public string TaskSchd_Connect_ComputerLabel => TaskSchd(TaskSchdKeys.ConnectComputerLabel);
        public string TaskSchd_NewFolder_Name => TaskSchd(TaskSchdKeys.NewFolderName);
    }
}
