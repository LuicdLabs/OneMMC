using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Models.Services;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using OneMMC.Core.Features.PCManagement.ViewModels.Services;
using OneMMC.Localization;

namespace OneMMC.Views
{
    public sealed partial class ServicesDetailsDialog : ContentDialog
    {
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

        private ServiceInfo _service;
        private ServicesViewModel _viewModel;

        // Dirty-tracking snapshots captured on Opened; updated after each successful save so the
        // user can iterate within the same dialog without re-triggering already-applied changes.
        // Initialized in Opened before any save path runs.
        private string _initialStartupTag = string.Empty;
        private string _initialLogOnAccount = "LocalSystem";  // normalized account name (or "LocalSystem")
        private string _initialLogOnPassword = string.Empty;  // not compared; only sent when account changes
        private string _initialRecoveryFirst = "none";
        private string _initialRecoverySecond = "none";
        private string _initialRecoverySubsequent = "none";
        private double _initialRecoveryResetDays;

        public ServicesDetailsDialog(ServiceInfo service, ServicesViewModel viewModel)
        {
            this.InitializeComponent();
            _service = service;
            _viewModel = viewModel;

            this.Opened += ServiceDetailsDialog_Opened;

            // Set dialog title
            ServiceTitle.Text = service.DisplayName ?? service.Name;

            // Populate general information
            ServiceNameText.Text = service.Name;
            DisplayNameText.Text = service.DisplayName ?? service.Name;
            DescriptionText.Text = service.Description ?? LocalizedStrings.Service_NoDescription;
            StatusText.Text = service.LocalizedStatus;
            // StartupType is represented by `StartupTypeBox` in the dialog; selection is initialized on Opened

            // Initialize SelectorBar
            ServiceDetailsSelectorBar.SelectedItem = ServiceDetailsSelectorBar.Items[0];
        }

        private void ServiceDetailsDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            // Initialize SelectorBar and pages
            ServiceDetailsSelectorBar.SelectionChanged -= ServiceDetailsSelectorBar_SelectionChanged;
            ServiceDetailsSelectorBar.SelectedItem = ServiceDetailsSelectorBar.Items[0];
            GeneralPage.Visibility = Visibility.Visible;
            LogOnPage.Visibility = Visibility.Collapsed;
            RecoveryPage.Visibility = Visibility.Collapsed;
            DependenciesPage.Visibility = Visibility.Collapsed;
            ServiceDetailsSelectorBar.SelectionChanged += ServiceDetailsSelectorBar_SelectionChanged;

            // Populate controls
            // Items are ComboBoxItem; select the item whose Tag or Content matches the service startup type
            StartupTypeBox.SelectedItem = StartupTypeBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag?.ToString() ?? i.Content?.ToString()) == _service.StartupType);

            // Initialize LogOn radio state from the service's current account.
            string normalizedCurrent = NormalizeLogOnAccount(_service.LogOnAs);
            if (normalizedCurrent == "LocalSystem")
            {
                LocalSystemRadio.IsChecked = true;
                ThisAccountRadio.IsChecked = false;
                AccountBox.Text = string.Empty;
                PasswordBox.Password = string.Empty;
            }
            else
            {
                ThisAccountRadio.IsChecked = true;
                LocalSystemRadio.IsChecked = false;
                AccountBox.Text = _service.LogOnAs;
                PasswordBox.Password = string.Empty;
            }

            RefreshRecoveryComboBoxes();

            // Capture initial snapshots for dirty-tracking.
            _initialStartupTag = (StartupTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? (StartupTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? string.Empty;
            _initialLogOnAccount = normalizedCurrent;
            _initialLogOnPassword = PasswordBox.Password;
            _initialRecoveryFirst = (FirstFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            _initialRecoverySecond = (SecondFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            _initialRecoverySubsequent = (SubsequentFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            _initialRecoveryResetDays = ResetFailCountBox.Value;

            // Reset transient UI state for a fresh open.
            SavingProgressBar.Visibility = Visibility.Collapsed;
            SavingErrorInfoBar.IsOpen = false;
            IsPrimaryButtonEnabled = true;
            IsSecondaryButtonEnabled = true;
        }

        /// <summary>
        /// Handles the OK button: defers close, shows progress, and saves every changed page
        /// (Startup Type, LogOn, Recovery). On any failure the dialog stays open and an Error
        /// InfoBar reports the failing section and message.
        /// </summary>
        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_service == null || _viewModel == null) return;

            var deferral = args.GetDeferral();

            // Enter saving state: show progress, lock buttons, clear previous error.
            SavingErrorInfoBar.IsOpen = false;
            SavingProgressBar.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;

            _ = SaveChangedSectionsAsync().ContinueWith(
                t =>
                {
                    SavingProgressBar.Visibility = Visibility.Collapsed;
                    IsPrimaryButtonEnabled = true;
                    IsSecondaryButtonEnabled = true;

                    // Per-section failures are reported as a non-success result; orchestration
                    // faults surface as a faulted task. Either way, keep the dialog open so the
                    // user can correct and retry.
                    if (t.IsFaulted)
                    {
                        args.Cancel = true;
                        ShowSaveError(string.Format(LocalizedStrings.Service_SaveFailed_General, t.Exception?.Message ?? string.Empty));
                    }
                    else if (t.Result is { Success: false })
                    {
                        args.Cancel = true;
                    }

                    deferral.Complete();
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Saves each changed section in turn and returns the first failure (or a success).
        /// Snapshots of successfully-applied sections are advanced so retrying OK won't redo them.
        /// </summary>
        private async Task<OperationResult> SaveChangedSectionsAsync()
        {
            // 1. Startup Type
            string currentStartupTag = (StartupTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? (StartupTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? string.Empty;
            if (!string.IsNullOrEmpty(currentStartupTag) && currentStartupTag != _initialStartupTag)
            {
                var result = await _viewModel.UpdateServiceStartupTypeAsync(_service.Name, currentStartupTag);
                if (!result.Success) return StampSection(result, LocalizedStrings.Service_SaveFailed_StartupType);
                _initialStartupTag = currentStartupTag;
            }

            // 2. LogOn account
            (string currentAccount, string? currentPassword) = GetCurrentLogOn();
            if (!string.Equals(currentAccount, _initialLogOnAccount, StringComparison.OrdinalIgnoreCase))
            {
                var result = await _viewModel.UpdateServiceLogOnAsync(_service.Name, currentAccount, currentPassword);
                if (!result.Success) return StampSection(result, LocalizedStrings.Service_SaveFailed_LogOn);
                _initialLogOnAccount = currentAccount;
                _initialLogOnPassword = currentPassword ?? string.Empty;
            }

            // 3. Recovery options
            string curFirst = (FirstFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            string curSecond = (SecondFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            string curSubsequent = (SubsequentFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            double curResetDays = ResetFailCountBox.Value;
            if (curFirst != _initialRecoveryFirst
                || curSecond != _initialRecoverySecond
                || curSubsequent != _initialRecoverySubsequent
                || curResetDays != _initialRecoveryResetDays)
            {
                var result = await _viewModel.UpdateServiceRecoveryAsync(
                    _service.Name, curFirst, curSecond, curSubsequent, curResetDays);
                if (!result.Success) return StampSection(result, LocalizedStrings.Service_SaveFailed_Recovery);
                _initialRecoveryFirst = curFirst;
                _initialRecoverySecond = curSecond;
                _initialRecoverySubsequent = curSubsequent;
                _initialRecoveryResetDays = curResetDays;
            }

            return OperationResult.Ok(string.Empty);
        }

        /// <summary>
        /// Formats the failing result with the per-section message template and surfaces it in
        /// the dialog's Error InfoBar so the user can correct and retry.
        /// </summary>
        private OperationResult StampSection(OperationResult result, string sectionFormat)
        {
            string message = string.Format(sectionFormat, result.Message);
            var stamped = new OperationResult(result.Success, message, result.ErrorCode, result.PartialSuccess, result.IsAccessDenied);
            ShowSaveError(message);
            return stamped;
        }

        private void ShowSaveError(string message)
        {
            SavingErrorInfoBar.Message = message;
            SavingErrorInfoBar.IsOpen = true;
        }

        /// <summary>
        /// Returns the normalized account name and password to send for the current LogOn selection.
        /// LocalSystem maps to the well-known "LocalSystem" account name.
        /// </summary>
        private (string account, string? password) GetCurrentLogOn()
        {
            if (ThisAccountRadio.IsChecked == true)
            {
                return (AccountBox.Text.Trim(), PasswordBox.Password);
            }
            return ("LocalSystem", null);
        }

        /// <summary>
        /// Normalizes a service's current LogOn account for comparison. The SCM/WMI reports
        /// LocalSystem aliases (e.g. ".\\LocalSystem", "NT AUTHORITY\\SYSTEM"); collapse them.
        /// </summary>
        private static string NormalizeLogOnAccount(string logOnAs)
        {
            if (string.IsNullOrWhiteSpace(logOnAs)) return "LocalSystem";
            string v = logOnAs.Trim();
            if (v.EndsWith("LocalSystem", StringComparison.OrdinalIgnoreCase)
                || v.EndsWith("SYSTEM", StringComparison.OrdinalIgnoreCase)
                || v.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                return "LocalSystem";
            }
            return v;
        }

        private void RefreshRecoveryComboBoxes()
        {
            if (_service == null) return;
            var firstTag = _service.FirstFailureAction ?? "none";
            FirstFailureBox.SelectedItem = FirstFailureBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == firstTag);
            var secondTag = _service.SecondFailureAction ?? "none";
            SecondFailureBox.SelectedItem = SecondFailureBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == secondTag);
            var subsequentTag = _service.SubsequentFailureAction ?? "none";
            SubsequentFailureBox.SelectedItem = SubsequentFailureBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == subsequentTag);
            ResetFailCountBox.Value = _service.ResetFailCountDays;
        }

        private void BrowseAccount_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            var selections = DirectoryObjectPickerService.ShowDialog(
                hwnd,
                ObjectPickerTypes.Users,
                multiSelect: false);

            if (selections is { Count: > 0 })
            {
                AccountBox.Text = selections[0].Name;
            }
        }

        private void ServiceDetailsSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            // Hide all pages
            GeneralPage.Visibility = Visibility.Collapsed;
            LogOnPage.Visibility = Visibility.Collapsed;
            RecoveryPage.Visibility = Visibility.Collapsed;
            DependenciesPage.Visibility = Visibility.Collapsed;

            // Show selected page
            if (sender.SelectedItem is SelectorBarItem selectedItem && selectedItem.Tag is string tag)
            {
                switch (tag)
                {
                    case "General":
                        GeneralPage.Visibility = Visibility.Visible;
                        break;
                    case "LogOn":
                        LogOnPage.Visibility = Visibility.Visible;
                        break;
                    case "Recovery":
                        RecoveryPage.Visibility = Visibility.Visible;
                        break;
                    case "Dependencies":
                        DependenciesPage.Visibility = Visibility.Visible;
                        break;
                }
            }
        }
    }
}
