using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Core.Features.SystemManagement.ViewModels.ComExp;

using ManagementTools.Localization;

namespace ManagementTools.Views.ComExp;

public sealed partial class DtcStatisticsPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DtcStatisticsViewModel ViewModel { get; }

    public DtcStatisticsPage()
    {
        ViewModel = App.GetRequiredService<DtcStatisticsViewModel>();
        InitializeComponent();
        Loaded += DtcStatisticsPage_Loaded;
        Unloaded += (_, _) => Loaded -= DtcStatisticsPage_Loaded;
    }

    private async void DtcStatisticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ManagementTools.Services.Logging.UiLogger.LogDebug("[DtcStatisticsPage] Loaded.");
        await ViewModel.LoadStatisticsAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ManagementTools.Services.Logging.UiLogger.LogDebug("[DtcStatisticsPage] Refresh requested.");
        await ViewModel.LoadStatisticsAsync();
    }
}

