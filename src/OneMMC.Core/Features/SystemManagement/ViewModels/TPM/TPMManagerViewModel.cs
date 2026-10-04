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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowUnavailableBanner))]
        public partial bool IsTpmStatusLoaded { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanClearTpm))]
        [NotifyPropertyChangedFor(nameof(ShowUnavailableBanner))]
        [NotifyPropertyChangedFor(nameof(IsUnavailableBannerClosable))]
        public partial bool IsTpmAvailable { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanClearTpm))]
        [NotifyPropertyChangedFor(nameof(IsUnavailableBannerClosable))]
        public partial bool IsTpmAccessDenied { get; set; }

        [ObservableProperty]
        public partial string ClearTpmDescription { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string UnavailableBannerTitle { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string UnavailableBannerMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial TpmStatusSeverity UnavailableBannerSeverity { get; set; } = TpmStatusSeverity.Error;

        /// <summary>
        /// Clearing is blocked only when we know there is no TPM. Access-denied still leaves
        /// the action enabled so the existing administrator-elevation flow can run.
        /// </summary>
        public bool CanClearTpm => IsTpmAvailable || IsTpmAccessDenied;

        /// <summary>
        /// Persistent banner shown after the first query when no TPM can be used.
        /// </summary>
        public bool ShowUnavailableBanner => IsTpmStatusLoaded && !IsTpmAvailable;

        /// <summary>
        /// The missing-TPM notice must stay visible; other notices remain dismissible.
        /// </summary>
        public bool IsUnavailableBannerClosable => IsTpmAccessDenied;

        public TPMManagerViewModel(TPMService tpmService)
        {
            _tpmService = tpmService;
            TpmManufacturerName = L.GetString(ResourceFileNames.TPM, TPMKeys.Loading);
            TpmManufacturerVersion = L.GetString(ResourceFileNames.TPM, TPMKeys.Loading);
            TpmSpecificationVersion = L.GetString(ResourceFileNames.TPM, TPMKeys.Loading);
            ClearTpmDescription = L.GetString(ResourceFileNames.TPM, TPMKeys.ClearTPMDescription);
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
                ShowStatus(
                    TpmStatusSeverity.Error,
                    L.GetString(ResourceFileNames.TPM, TPMKeys.Error),
                    $"{L.GetString(ResourceFileNames.TPM, TPMKeys.CannotOpenConsole)}: {ex.Message}");
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

                    _syncContext?.Post(_ => UpdateTPMStatus(info), null);
                }
                catch (Exception ex)
                {
                    _syncContext?.Post(_ =>
                    {
                        ShowStatus(
                            TpmStatusSeverity.Warning,
                            L.GetString(ResourceFileNames.TPM, TPMKeys.Warning),
                            $"{L.GetString(ResourceFileNames.TPM, TPMKeys.CannotGetInfo)}: {ex.Message}");
                    }, null);
                }
            });
        }


        private void UpdateTPMStatus(TPMInfo info)
        {
            IsTpmAvailable = info.IsAvailable;
            IsTpmAccessDenied = info.IsAccessDenied;
            IsTpmStatusLoaded = true;

            if (!info.IsAvailable)
            {
                var placeholder = L.GetString(ResourceFileNames.TPM, TPMKeys.UnavailableValue);
                TpmManufacturerName = placeholder;
                TpmManufacturerVersion = placeholder;
                TpmSpecificationVersion = placeholder;
                TpmReadyGlyph = "\uE711";
                TpmEnabledGlyph = "\uE711";
                TpmActivatedGlyph = "\uE711";
                TpmOwnedGlyph = "\uE711";
                TpmStatusColorHex = "#FF0000"; // Red

                if (info.IsAccessDenied)
                {
                    ClearTpmDescription = L.GetString(ResourceFileNames.TPM, TPMKeys.ClearTPMDescription);
                    UnavailableBannerTitle = L.GetString(ResourceFileNames.TPM, TPMKeys.AccessDeniedTitle);
                    UnavailableBannerMessage = L.GetString(ResourceFileNames.TPM, TPMKeys.AccessDenied);
                    UnavailableBannerSeverity = TpmStatusSeverity.Warning;
                }
                else if (info.IsQueryFailed)
                {
                    ClearTpmDescription = L.GetString(ResourceFileNames.TPM, TPMKeys.ClearTPMDisabledDescription);
                    UnavailableBannerTitle = L.GetString(ResourceFileNames.TPM, TPMKeys.Warning);
                    UnavailableBannerMessage = string.IsNullOrEmpty(info.ErrorMessage)
                        ? L.GetString(ResourceFileNames.TPM, TPMKeys.CannotGetInfo)
                        : info.ErrorMessage;
                    UnavailableBannerSeverity = TpmStatusSeverity.Warning;
                }
                else
                {
                    ClearTpmDescription = L.GetString(ResourceFileNames.TPM, TPMKeys.ClearTPMDisabledDescription);
                    UnavailableBannerTitle = L.GetString(ResourceFileNames.TPM, TPMKeys.NotAvailableTitle);
                    UnavailableBannerMessage = L.GetString(ResourceFileNames.TPM, TPMKeys.NotAvailableMessage);
                    UnavailableBannerSeverity = TpmStatusSeverity.Warning;
                }

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
            ClearTpmDescription = L.GetString(ResourceFileNames.TPM, TPMKeys.ClearTPMDescription);
            UnavailableBannerTitle = string.Empty;
            UnavailableBannerMessage = string.Empty;
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
