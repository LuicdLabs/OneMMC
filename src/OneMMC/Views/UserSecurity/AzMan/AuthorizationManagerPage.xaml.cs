// ============================================================================
// AuthorizationManagerPage.xaml.cs
//
// Authorization Manager Main Page - Manage authorization stores
// Provides functionality to create, open, and close authorization stores
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using OneMMC.Localization;
using OneMMC.Services;
using OneMMC.Views.UserSecurity.AzMan.AuthStore;
using OneMMC.Views.UserSecurity.AzMan.Dialogs;
using CommunityToolkit.WinUI.Controls;
using System;
using System.Linq;
using OneMMC.Core.Features.UserSecurity.ViewModels.AzMan;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Views.UserSecurity.AzMan;

/// <summary>
/// Authorization Manager Main Page
/// </summary>
public sealed partial class AuthorizationManagerPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Owns the view model's lifetime. AuthorizationManagerViewModel is a transient IDisposable, so
    /// resolving it from the root provider would leave the container holding one instance (and its
    /// store graph) per visit. See doc/MemoryManagement.md.
    /// </summary>
    private readonly PageServiceScope _serviceScope = new();

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
        _viewModel = _serviceScope.GetRequiredService<AuthorizationManagerViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Initialize UI state
        UpdateUIState();

        this.Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // Always release: the drill-down page no longer borrows this view model or its service, and
        // NavigationCacheMode is Disabled, so navigating back rebuilds this page from scratch anyway.
        // Skipping disposal on drill-in used to leak one view model per Manager -> Store -> Back trip.
        _serviceScope.Dispose();
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

            var navigationParameter = new StoreNavigationParameter(store);

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
/// Store navigation parameter.
/// </summary>
/// <remarks>
/// Deliberately carries only the store descriptor. Navigation parameters are retained by
/// <see cref="Microsoft.UI.Xaml.Controls.Frame.BackStack"/> and by the breadcrumb trail, so putting a
/// live service or view model here pinned an open COM store, its STA thread, and the whole manager
/// object graph for the rest of the session. Pages resolve <c>AzManService</c> (a singleton) from DI
/// instead. See doc/MemoryManagement.md.
/// </remarks>
public class StoreNavigationParameter
{
    public AzAuthorizationStoreInfo Store { get; }

    public StoreNavigationParameter(AzAuthorizationStoreInfo store)
    {
        Store = store;
    }
}
