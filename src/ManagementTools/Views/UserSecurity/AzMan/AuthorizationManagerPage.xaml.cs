// ============================================================================
// AuthorizationManagerPage.xaml.cs
//
// Authorization Manager Main Page - Manage authorization stores
// Provides functionality to create, open, and close authorization stores
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using ManagementTools.Localization;
using ManagementTools.Services;
using ManagementTools.Views.UserSecurity.AzMan.AuthStore;
using ManagementTools.Views.UserSecurity.AzMan.Dialogs;
using CommunityToolkit.WinUI.Controls;
using System;
using System.Linq;
using ManagementTools.Core.Features.UserSecurity.ViewModels.AzMan;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;

namespace ManagementTools.Views.UserSecurity.AzMan;

/// <summary>
/// Authorization Manager Main Page
/// </summary>
public sealed partial class AuthorizationManagerPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// ViewModel instance
    /// </summary>
    private readonly AuthorizationManagerViewModel _viewModel;

    /// <summary>
    /// Search text
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Store count text
    /// </summary>
    public string StoreCountText => _viewModel.StoreCountText;

    public AuthorizationManagerPage()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;

        // Initialize ViewModel
        _viewModel = App.GetRequiredService<AuthorizationManagerViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Initialize UI state
        UpdateUIState();

        this.Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (Frame?.CurrentSourcePageType != typeof(AuthorizationStorePage))
        {
            _viewModel.Dispose();
        }
    }

    #region Event Handlers

    /// <summary>
    /// Theme change handler
    /// </summary>
    private void OnThemeChanged(ElementTheme theme)
    {
        this.RequestedTheme = theme;
    }

    /// <summary>
    /// ViewModel property change handler
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AuthorizationManagerViewModel.IsLoading):
                    LoadingRing.IsActive = _viewModel.IsLoading;
                    break;

                case nameof(AuthorizationManagerViewModel.HasError):
                case nameof(AuthorizationManagerViewModel.StatusMessage):
                    UpdateStatusBar();
                    break;

                case nameof(AuthorizationManagerViewModel.StoreCountText):
                    UpdateUIState();
                    break;
            }
        });
    }

    /// <summary>
    /// Search text changed
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.SearchText = sender.Text;
            UpdateUIState();
        }
    }

    /// <summary>
    /// Add button click - Create new authorization store
    /// </summary>
    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateStoreDialog
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = this.RequestedTheme
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.Result != null)
        {
            var store = await _viewModel.CreateStoreAsync(dialog.Result);
            if (store != null)
            {
                UpdateUIState();
                ShowStatus(string.Format(LocalizedStrings.AuthorizationManagerPage_Status_CreateSuccess, store.Name), false);
            }
        }
    }

    /// <summary>
    /// Open button click - Open existing authorization store
    /// </summary>
    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenStoreDialog
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = this.RequestedTheme
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.Result != null)
        {
            var store = await _viewModel.OpenStoreAsync(dialog.Result);
            if (store != null)
            {
                UpdateUIState();
                ShowStatus(string.Format(LocalizedStrings.AuthorizationManagerPage_Status_OpenSuccess, store.Name), false);
            }
        }
    }

    /// <summary>
    /// Refresh button click
    /// </summary>
    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAllAsync();
        UpdateUIState();
    }

    /// <summary>
    /// Help button click
    /// </summary>
    private async void OnHelpClick(object sender, RoutedEventArgs e)
    {
        // Open Microsoft Learn documentation
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://learn.microsoft.com/en-us/windows/win32/secauthz/authorization-manager"));
    }

    /// <summary>
    /// Store card click - Navigate to store details page
    /// </summary>
    private void OnStoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsCard card && card.Tag is AzAuthorizationStoreInfo store)
        {
            if (this.Frame == null)
            {
                // Frame is not available, cannot navigate
                return;
            }

            var navigationParameter = new StoreNavigationParameter(_viewModel.Service, store, _viewModel);

            // Add breadcrumb and navigate
            BreadcrumbNavigationService.AddBreadcrumb(store.Name, typeof(AuthorizationStorePage), navigationParameter);
            this.Frame.Navigate(
                typeof(AuthorizationStorePage), 
                navigationParameter,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }
    }

    /// <summary>
    /// Close store button click
    /// </summary>
    private void OnCloseStoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzAuthorizationStoreInfo store)
        {
            _viewModel.CloseStore(store);
            UpdateUIState();
            ShowStatus(string.Format(LocalizedStrings.AuthorizationManagerPage_Status_CloseStore, store.Name), false);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Update UI state
    /// </summary>
    private void UpdateUIState()
    {
        // Update ItemsRepeater data source
        StoreItemsRepeater.ItemsSource = _viewModel.FilteredStores;

        // Update store count text
        StoreCountTextBlock.Text = _viewModel.StoreCountText;

        // Update empty state visibility
        bool hasStores = _viewModel.FilteredStores.Count > 0;
        EmptyStatePanel.Visibility = hasStores ? Visibility.Collapsed : Visibility.Visible;
        StoreListScrollViewer.Visibility = hasStores ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Update status bar
    /// </summary>
    private void UpdateStatusBar()
    {
        if (_viewModel.HasError)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = LocalizedStrings.Common_ErrorTitle;
            StatusInfoBar.Message = _viewModel.StatusMessage;
            StatusInfoBar.IsOpen = true;
        }
        else if (!string.IsNullOrEmpty(_viewModel.StatusMessage))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = LocalizedStrings.Common_SuccessTitle;
            StatusInfoBar.Message = _viewModel.StatusMessage;
            StatusInfoBar.IsOpen = true;
        }
    }

    /// <summary>
    /// Show status message
    /// </summary>
    private void ShowStatus(string message, bool isError)
    {
        StatusInfoBar.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        StatusInfoBar.Title = isError ? LocalizedStrings.Common_ErrorTitle : LocalizedStrings.Common_SuccessTitle;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    #endregion
}

/// <summary>
/// Store navigation parameter
/// </summary>
public class StoreNavigationParameter
{
    public Core.Features.UserSecurity.Services.AzMan.AzManService Service { get; }
    public AzAuthorizationStoreInfo Store { get; }
    public AuthorizationManagerViewModel? ManagerViewModel { get; }

    public StoreNavigationParameter(Core.Features.UserSecurity.Services.AzMan.AzManService service, AzAuthorizationStoreInfo store, AuthorizationManagerViewModel? managerViewModel = null)
    {
        Service = service;
        Store = store;
        ManagerViewModel = managerViewModel;
    }
}
