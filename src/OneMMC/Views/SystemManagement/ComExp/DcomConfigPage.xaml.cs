using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

using OneMMC.Localization;

namespace OneMMC.Views.ComExp;

public sealed partial class DcomConfigPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DcomConfigViewModel ViewModel { get; }

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
}

