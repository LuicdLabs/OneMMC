using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

using OneMMC.Localization;

namespace OneMMC.Views.ComExp;

public sealed partial class DtcTransactionListPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DtcTransactionListViewModel ViewModel { get; }

    public DtcTransactionListPage()
    {
        ViewModel = App.GetRequiredService<DtcTransactionListViewModel>();
        InitializeComponent();
        Loaded += DtcTransactionListPage_Loaded;
        Unloaded += (_, _) => Loaded -= DtcTransactionListPage_Loaded;
    }

    private async void DtcTransactionListPage_Loaded(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DtcTransactionListPage] Loaded.");
        await ViewModel.LoadTransactionsAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DtcTransactionListPage] Refresh requested.");
        await ViewModel.LoadTransactionsAsync();
    }
}

