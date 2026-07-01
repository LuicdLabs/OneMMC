using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

using OneMMC.Localization;

namespace OneMMC.Views.ComExp;

public sealed partial class RunningProcessesPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public RunningProcessesViewModel ViewModel { get; }

    public RunningProcessesPage()
    {
        ViewModel = App.GetRequiredService<RunningProcessesViewModel>();
        InitializeComponent();
        Loaded += RunningProcessesPage_Loaded;
        Unloaded += (_, _) => Loaded -= RunningProcessesPage_Loaded;
    }

    private async void RunningProcessesPage_Loaded(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[RunningProcessesPage] Loaded.");
        await ViewModel.LoadProcessesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[RunningProcessesPage] Refresh requested.");
        await ViewModel.LoadProcessesAsync();
    }
}

