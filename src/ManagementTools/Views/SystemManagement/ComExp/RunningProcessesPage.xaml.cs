using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Core.Features.SystemManagement.ViewModels.ComExp;

using ManagementTools.Localization;

namespace ManagementTools.Views.ComExp;

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
        ManagementTools.Services.Logging.UiLogger.LogDebug("[RunningProcessesPage] Loaded.");
        await ViewModel.LoadProcessesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ManagementTools.Services.Logging.UiLogger.LogDebug("[RunningProcessesPage] Refresh requested.");
        await ViewModel.LoadProcessesAsync();
    }
}

