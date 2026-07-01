using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PCManagement.ViewModels.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
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
        
        // Subscribe to admin permission required event
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;

        this.Unloaded += (_, _) =>
        {
            ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
            ViewModel.ClearCachedData();
            DataContext = null;
            this.Loaded -= ServicesPage_Loaded;
        };
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    }

    private async void ServicesPage_Loaded(object sender, RoutedEventArgs e)
    {
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
