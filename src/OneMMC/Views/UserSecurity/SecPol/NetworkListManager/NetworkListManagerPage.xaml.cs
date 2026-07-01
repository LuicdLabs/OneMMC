using System.Collections.Generic;
using OneMMC.Core.Features.UserSecurity.ViewModels.NetworkListManager;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

public sealed partial class NetworkListManagerPage : Page
{
    private readonly Dictionary<string, int> _selectionSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<NetworkListManagerPage> _logger;
    private readonly IconPickerService _iconPickerService;
    private bool _hasLoaded;
    private bool _suppressComboSaves;

    internal readonly LocalizedStrings LocalizedStrings = LocalizedStrings.Instance;

    public NetworkListManagerViewModel ViewModel { get; }

    public NetworkListManagerPage()
    {
        _logger = App.GetRequiredService<ILogger<NetworkListManagerPage>>();
        _iconPickerService = App.GetRequiredService<IconPickerService>();
        ViewModel = App.GetRequiredService<NetworkListManagerViewModel>();

        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _logger.LogDebug("[NetworkListManagerPage] Page loaded.");
        _hasLoaded = true;
        await ReloadViewModelAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        _selectionSnapshot.Clear();
        DataContext = null;
        Loaded -= OnPageLoaded;
        Unloaded -= OnPageUnloaded;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[NetworkListManagerPage] Refresh requested.");
        await ReloadViewModelAsync();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ViewModel.FilterText = sender.Text;
        RefreshSelectionSnapshot();
    }

    private async void PolicyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboSaves || ViewModel.IsLoading)
        {
            return;
        }

        if (sender is not ComboBox comboBox
            || comboBox.Tag is not string settingName
            || comboBox.DataContext is not NetworkListPolicyNodeViewModel node
            || comboBox.SelectedIndex < 0)
        {
            return;
        }

        string snapshotKey = GetSelectionSnapshotKey(node, settingName);
        if (_selectionSnapshot.TryGetValue(snapshotKey, out int originalIndex) && originalIndex == comboBox.SelectedIndex)
        {
            return;
        }

        _suppressComboSaves = true;

        try
        {
            _logger.LogDebug("[NetworkListManagerPage] Saving {SettingName} for {SignatureId}.", settingName, node.SignatureId);
            switch (settingName)
            {
                case "NamePermission":
                    await ViewModel.SaveNamePermissionAsync(node, comboBox.SelectedIndex);
                    break;
                case "IconPermission":
                    await ViewModel.SaveIconPermissionAsync(node, comboBox.SelectedIndex);
                    break;
                case "LocationType":
                    await ViewModel.SaveLocationTypeAsync(node, comboBox.SelectedIndex);
                    break;
                case "LocationPermission":
                    await ViewModel.SaveLocationPermissionAsync(node, comboBox.SelectedIndex);
                    break;
            }

            RefreshSelectionSnapshot();
        }
        finally
        {
            _suppressComboSaves = false;
        }
    }

    private async void NetworkNameConfigure_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NetworkListPolicyNodeViewModel node)
        {
            return;
        }

        NetworkNameDialog dialog = new();
        PrepareDialog(dialog);
        dialog.SetState(node.HasCustomName, node.NetworkName);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _suppressComboSaves = true;

            try
            {
                await ViewModel.SaveNetworkNameAsync(node, dialog.HasCustomName, dialog.NetworkName);
                RefreshSelectionSnapshot();
            }
            finally
            {
                _suppressComboSaves = false;
            }
        }
    }

    private async void NetworkIconConfigure_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NetworkListPolicyNodeViewModel node)
        {
            return;
        }

        NetworkIconDialog dialog = new(_iconPickerService);
        PrepareDialog(dialog);
        await dialog.SetStateAsync(node.IconPayload);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _suppressComboSaves = true;

            try
            {
                await ViewModel.SaveNetworkIconAsync(node, dialog.IconPayload);
                RefreshSelectionSnapshot();
            }
            finally
            {
                _suppressComboSaves = false;
            }
        }
    }

    private async Task ReloadViewModelAsync()
    {
        _suppressComboSaves = true;

        try
        {
            await ViewModel.LoadAsync();
            RefreshSelectionSnapshot();
        }
        finally
        {
            _suppressComboSaves = false;
        }
    }

    private void RefreshSelectionSnapshot()
    {
        _selectionSnapshot.Clear();

        foreach (NetworkListPolicyNodeViewModel node in ViewModel.Nodes)
        {
            _selectionSnapshot[GetSelectionSnapshotKey(node, "NamePermission")] = node.NamePermissionIndex;
            _selectionSnapshot[GetSelectionSnapshotKey(node, "IconPermission")] = node.IconPermissionIndex;
            _selectionSnapshot[GetSelectionSnapshotKey(node, "LocationType")] = node.LocationTypeIndex;
            _selectionSnapshot[GetSelectionSnapshotKey(node, "LocationPermission")] = node.LocationPermissionIndex;
        }
    }

    private void PrepareDialog(ContentDialog dialog)
    {
        dialog.XamlRoot = this.XamlRoot;
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.RequestedTheme = App.CurrentTheme;
    }

    private static string GetSelectionSnapshotKey(NetworkListPolicyNodeViewModel node, string settingName) =>
        $"{node.SignatureId}|{settingName}";
}
