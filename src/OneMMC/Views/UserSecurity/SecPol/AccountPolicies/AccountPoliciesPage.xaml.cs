using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

public sealed partial class AccountPoliciesPage : Page
{
    public AccountPoliciesPage()
    {
        ViewModel = App.GetRequiredService<AccountPoliciesViewModel>();
        InitializeComponent();
        DataContext = ViewModel;

        Unloaded += (_, _) =>
        {
            ViewModel.StopLoading();
            DataContext = null;
        };
    }

    public AccountPoliciesViewModel ViewModel { get; }
}
