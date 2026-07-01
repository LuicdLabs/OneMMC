using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using OneMMC.Core.Features.PCManagement.Models.Services;
using OneMMC.Core.Features.PCManagement.ViewModels.Services;
using OneMMC.Localization;

namespace OneMMC.Views
{
    public sealed partial class ServicesDetailsDialog : ContentDialog
    {
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

        private ServiceInfo _service;
        private ServicesViewModel _viewModel;

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

            RefreshRecoveryComboBoxes();
        }

        private async void SaveStartupType_Click(object sender, RoutedEventArgs e)
        {
            if (_service == null || _viewModel == null) return;
            var selectedItem = StartupTypeBox.SelectedItem as ComboBoxItem;
            var selected = selectedItem?.Tag?.ToString() ?? selectedItem?.Content?.ToString();
            if (!string.IsNullOrEmpty(selected))
            {
                await _viewModel.UpdateServiceStartupTypeAsync(_service.Name, selected);
            }
        }

        private async void ApplyLogOn_Click(object sender, RoutedEventArgs e)
        {
            if (_service == null || _viewModel == null) return;
            string username;
            string? password = null;
            if (ThisAccountRadio.IsChecked == true)
            {
                username = AccountBox.Text;
                password = PasswordBox.Password;
                if (string.IsNullOrWhiteSpace(username)) return;
            }
            else
            {
                username = "LocalSystem";
            }
            await _viewModel.UpdateServiceLogOnAsync(_service.Name, username, password);
        }

        private async void ApplyRecovery_Click(object sender, RoutedEventArgs e)
        {
            if (_service == null || _viewModel == null) return;
            var firstAction = (FirstFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            var secondAction = (SecondFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            var subsequentAction = (SubsequentFailureBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            var resetDays = ResetFailCountBox.Value;
            await _viewModel.UpdateServiceRecoveryAsync(_service.Name, firstAction, secondAction, subsequentAction, resetDays);
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

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // Handle OK button click if needed
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
