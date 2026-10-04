using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

using OneMMC.Localization;

namespace OneMMC.Views.ComExp;

public sealed partial class DcomConfigPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DcomConfigViewModel ViewModel { get; }

    private bool _isDialogOpen;

    public DcomConfigPage()
    {
        ViewModel = App.GetRequiredService<DcomConfigViewModel>();
        InitializeComponent();
        Loaded += DcomConfigPage_Loaded;
        Unloaded += (_, _) => Loaded -= DcomConfigPage_Loaded;
    }

    private async void DcomConfigPage_Loaded(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DcomConfigPage] Loaded.");
        await ViewModel.LoadApplicationsAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DcomConfigPage] Refresh requested.");
        await ViewModel.LoadApplicationsAsync();
    }

    private async void PropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenPropertiesDialogAsync();
    }

    private async void ApplicationsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await OpenPropertiesDialogAsync();
    }

    private async Task OpenPropertiesDialogAsync()
    {
        if (_isDialogOpen)
        {
            return;
        }

        if (ViewModel.SelectedApplication is null)
        {
            return;
        }

        OneMMC.Services.Logging.UiLogger.LogDebug("[DcomConfigPage] DCOM properties requested.");
        _isDialogOpen = true;
        try
        {
            var dialog = new DcomPropertiesDialog(ViewModel.SelectedApplication)
            {
                RequestedTheme = App.CurrentTheme,
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
        {
            OneMMC.Services.Logging.UiLogger.LogDebug($"[DcomConfigPage] Properties dialog already open: {ex.Message}");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }
}
