using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

public sealed partial class LocalPoliciesPage : Page
{
    public LocalPoliciesPage()
    {
        ViewModel = App.GetRequiredService<LocalPoliciesViewModel>();
        InitializeComponent();
        DataContext = ViewModel;

        Unloaded += (_, _) =>
        {
            ViewModel.StopLoading();
            DataContext = null;
        };
    }

    public LocalPoliciesViewModel ViewModel { get; }
}
