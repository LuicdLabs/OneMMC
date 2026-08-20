using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PCManagement.ViewModels.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using OneMMC.Helpers;
using Microsoft.Extensions.Logging;

namespace OneMMC.Views;

public sealed partial class ServicesPage : Page
{
    private readonly ILogger<ServicesPage> _logger;
    private bool _isDialogOpen;
    public ServicesViewModel ViewModel { get; }
    public OneMMC.Localization.LocalizedStrings LocalizedStrings { get; } = OneMMC.Localization.LocalizedStrings.Instance;

    public ServicesPage()
    {
        _logger = App.GetRequiredService<ILogger<ServicesPage>>();
        ViewModel = App.GetRequiredService<ServicesViewModel>();

        InitializeComponent();
        this.Loaded += ServicesPage_Loaded;
    }

    /// <summary>
    /// Attaches per-visit state.
    /// </summary>
    /// <remarks>
    /// This page sets <see cref="NavigationCacheMode.Enabled"/>, so the constructor runs once per
    /// session while this runs on every visit. Subscribing here (and unsubscribing in
    /// <see cref="OnNavigatedFrom"/>) keeps the handler balanced across visits; doing it in the
    /// constructor and <c>Unloaded</c> detached it permanently after the first navigation away.
    /// Same pattern as <c>DeviceManagerPage</c>. See <c>doc/MemoryManagement.md</c>.
    /// </remarks>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
    }

    /// <inheritdoc cref="OnNavigatedTo"/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    }

    private async void ServicesPage_Loaded(object sender, RoutedEventArgs e)
    {
        // The page is cached, so this runs on every visit. Only enumerate when there is nothing to
        // show: re-running it rebuilt every item container, and discarded containers are not
        // released. The toolbar refresh and pull-to-refresh remain the way to re-read services.
        // Same guard as DeviceManagerPage. See doc/MemoryManagement.md.
        if (ViewModel.Services.Count == 0)
        {
            await ViewModel.LoadServicesCommand.ExecuteAsync(null);
        }
    }

    private async void ServicesPullToRefresh_RefreshRequested(Microsoft.UI.Xaml.Controls.RefreshContainer sender, Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs args)
    {
        var def = args.GetDeferral();
        try
        {
            await ViewModel.LoadServicesCommand.ExecuteAsync(null);
        }
        finally
        {
            def.Complete();
        }
    }

    // Dialog-related UI and handlers moved into ServicesDetailsDialog

    private async void ServiceList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedService != null)
        {
            await ShowServiceDetailsDialogAsync();
        }
    }
   
    private async void PropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedService != null)
        {
            await ShowServiceDetailsDialogAsync();
        }
    }

    private async System.Threading.Tasks.Task ShowServiceDetailsDialogAsync()
    {
        // Prevent opening multiple dialogs
        if (_isDialogOpen) return;
        if (ViewModel.SelectedService == null) return;

        _isDialogOpen = true;
        try
        {
            // Load detailed info
            await ViewModel.LoadServiceDetailsAsync();

            var dialog = new ServicesDetailsDialog(ViewModel.SelectedService, ViewModel);
            dialog.RequestedTheme = App.CurrentTheme;
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
        {
            _logger.LogDebug(ex, "ContentDialog already open, ignoring service details dialog request.");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }
}
