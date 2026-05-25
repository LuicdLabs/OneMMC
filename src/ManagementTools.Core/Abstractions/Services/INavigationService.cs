namespace ManagementTools.Core.Abstractions.Services;

public interface INavigationService
{
    void Navigate(string pageKey);
    void GoBack();
}
