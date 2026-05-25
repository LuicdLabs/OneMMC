using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ManagementTools.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public MainWindowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    [RelayCommand]
    private void NavigateToPage(string tag)
    {
        if (!string.IsNullOrEmpty(tag))
        {
            _navigationService.Navigate(tag);
        }
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.Navigate("SettingsPage");
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.GoBack();
    }
}
