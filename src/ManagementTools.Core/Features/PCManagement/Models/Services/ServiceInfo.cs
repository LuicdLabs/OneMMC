using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.PCManagement.Models.Services
{
    public partial class ServiceInfo : ObservableObject
    {
        private string name = string.Empty;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }

        private string displayName = string.Empty;
        public string DisplayName
        {
            get => displayName;
            set => SetProperty(ref displayName, value);
        }

        private string description = string.Empty;
        public string Description
        {
            get => description;
            set => SetProperty(ref description, value);
        }

        private string status = string.Empty; // Running, Stopped, Paused, etc.
        public string Status
        {
            get => status;
            set
            {
                if (SetProperty(ref status, value))
                {
                    OnPropertyChanged(nameof(LocalizedStatus));
                }
            }
        }

        public string LocalizedStatus => Status switch
        {
            "Running" => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_Running"),
            "Stopped" => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_Stopped"),
            "Paused" => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_Paused"),
            "StartPending" => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_StartPending"),
            "StopPending" => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_StopPending"),
            "ContinuePending" => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_ContinuePending"),
            "PausePending" => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_PausePending"),
            _ => LocalizationProvider.Current.GetString(ResourceFileNames.Services, "Service_Status_Unknown")
        };

        private string startupType = string.Empty; // Automatic, Manual, Disabled, Delayed Start
        public string StartupType
        {
            get => startupType;
            set => SetProperty(ref startupType, value);
        }

        private string logOnAs = string.Empty;
        public string LogOnAs
        {
            get => logOnAs;
            set => SetProperty(ref logOnAs, value);
        }

        private string binaryPath = string.Empty;
        public string BinaryPath
        {
            get => binaryPath;
            set => SetProperty(ref binaryPath, value);
        }

        private int processId;
        public int ProcessId
        {
            get => processId;
            set => SetProperty(ref processId, value);
        }

        private List<string> dependencies = new();
        public List<string> Dependencies
        {
            get => dependencies;
            set => SetProperty(ref dependencies, value);
        }

        private List<string> dependents = new();
        public List<string> Dependents
        {
            get => dependents;
            set => SetProperty(ref dependents, value);
        }

        private string firstFailureAction = string.Empty;
        public string FirstFailureAction
        {
            get => firstFailureAction;
            set => SetProperty(ref firstFailureAction, value);
        }

        private string secondFailureAction = string.Empty;
        public string SecondFailureAction
        {
            get => secondFailureAction;
            set => SetProperty(ref secondFailureAction, value);
        }

        private string subsequentFailureAction = string.Empty;
        public string SubsequentFailureAction
        {
            get => subsequentFailureAction;
            set => SetProperty(ref subsequentFailureAction, value);
        }

        private double resetFailCountDays;
        public double ResetFailCountDays
        {
            get => resetFailCountDays;
            set => SetProperty(ref resetFailCountDays, value);
        }

        public ServiceInfo()
        {
        }
    }
}
