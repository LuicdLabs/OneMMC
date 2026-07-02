using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.SystemManagement.Services.TPM;

namespace OneMMC.Core.Features.SystemManagement.ViewModels.TPM
{
    public enum TpmStatusSeverity
    {
        Informational,
        Success,
        Warning,
        Error
    }

    public partial class TPMManagerViewModel : ObservableObject
    {
        private readonly TPMService _tpmService;
        private static ILocalizationProvider L => LocalizationProvider.Current;
        private SynchronizationContext? _syncContext;

        [ObservableProperty]
        public partial string TpmManufacturerName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string TpmManufacturerVersion { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string TpmSpecificationVersion { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string TpmReadyGlyph { get; set; } = "\uE73E"; // CheckMark

        [ObservableProperty]
        public partial string TpmEnabledGlyph { get; set; } = "\uE73E";

        [ObservableProperty]
        public partial string TpmActivatedGlyph { get; set; } = "\uE73E";

        [ObservableProperty]
        public partial string TpmOwnedGlyph { get; set; } = "\uE73E";

        [ObservableProperty]
        public partial string TpmStatusColorHex { get; set; } = "#008000"; // Green

        [ObservableProperty]
        public partial bool ShowStatusMessage { get; set; } = false;

        [ObservableProperty]
        public partial TpmStatusSeverity StatusSeverity { get; set; } = TpmStatusSeverity.Informational;

        [ObservableProperty]
        public partial string StatusTitle { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        public TPMManagerViewModel(TPMService tpmService)
        {
            _tpmService = tpmService;
            TpmManufacturerName = L.GetString(ResourceFileNames.TPM, TPMKeys.Loading);
            TpmManufacturerVersion = L.GetString(ResourceFileNames.TPM, TPMKeys.Loading);
            TpmSpecificationVersion = L.GetString(ResourceFileNames.TPM, TPMKeys.Loading);
            _syncContext = SynchronizationContext.Current;
            RefreshTPMStatus();
        }

        [RelayCommand]
        private void OpenTPMConsole()
        {
            try
            {
                if (!_tpmService.OpenTPMConsole())
                {
                    ShowStatus(TpmStatusSeverity.Error, L.GetString(ResourceFileNames.TPM, TPMKeys.Error), L.GetString(ResourceFileNames.TPM, TPMKeys.CannotOpenConsole));
                }
            }
            catch (Exception ex)
            {
                ShowStatus(TpmStatusSeverity.Error, L.GetString(ResourceFileNames.TPM, TPMKeys.Error), $"System Cannot Open TPM Console: {ex.Message}");
            }
        }

        [RelayCommand]
        private void RefreshTPMStatus()
        {
            Task.Run(() =>
            {
                try
                {
                    var info = _tpmService.GetTPMInformation();

                    _syncContext?.Post(_ =>
                    {
                        UpdateTPMStatus(info);

                        if (!info.IsAvailable)
                        {
                            ShowStatus(TpmStatusSeverity.Warning, L.GetString(ResourceFileNames.TPM, TPMKeys.Warning), info.ErrorMessage);
                        }
                    }, null);
                }
                catch (Exception ex)
                {
                    _syncContext?.Post(_ =>
                    {
                        ShowStatus(TpmStatusSeverity.Warning, L.GetString(ResourceFileNames.TPM, TPMKeys.Warning), $"System Cannot Get TPM Info: {ex.Message}");
                    }, null);
                }
            });
        }


        private void UpdateTPMStatus(TPMInfo info)
        {
            if (!info.IsAvailable)
            {
                TpmManufacturerName = info.ErrorMessage;
                TpmManufacturerVersion = string.Empty;
                TpmSpecificationVersion = string.Empty;
                TpmReadyGlyph = "\uE711";
                TpmEnabledGlyph = "\uE711";
                TpmActivatedGlyph = "\uE711";
                TpmOwnedGlyph = "\uE711";
                TpmStatusColorHex = "#FF0000"; // Red
                return;
            }

            TpmManufacturerName = info.ManufacturerName;
            TpmManufacturerVersion = info.ManufacturerVersion;
            TpmSpecificationVersion = info.SpecVersion;
            TpmEnabledGlyph = info.IsEnabled ? "\uE73E" : "\uE711";
            TpmActivatedGlyph = info.IsActivated ? "\uE73E" : "\uE711";
            TpmOwnedGlyph = info.IsOwned ? "\uE73E" : "\uE711";
            TpmReadyGlyph = info.IsReady ? "\uE73E" : "\uE711";
            TpmStatusColorHex = info.IsReady ? "#32CD32" : "#FFA500"; // LimeGreen / Orange
        }

        private void ShowStatus(TpmStatusSeverity severity, string title, string message)
        {
            StatusSeverity = severity;
            StatusTitle = title;
            StatusMessage = message;
            ShowStatusMessage = true;

            Task.Delay(5000).ContinueWith(_ =>
            {
                _syncContext?.Post(__ =>
                {
                    ShowStatusMessage = false;
                }, null);
            });
        }
    }
}
