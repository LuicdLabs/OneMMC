using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Scope;

public sealed partial class ServicesDialog : UserControl
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly Action<ElementTheme> _themeChangedHandler;
    private readonly ILogger<ServicesDialog> _logger;
    private List<ServiceItem> _allServices = new();
    private string _appliedServiceExpression = string.Empty;

    public string SelectedServiceShortName =>
        ServiceShortNameRadio.IsChecked == true ? ServiceShortNameInputBox.Text : string.Empty;

    public bool ApplyToAllServices => AllServicesRadio.IsChecked == true;
    public bool ApplyOnlyToServices => OnlyServicesRadio.IsChecked == true;
    public bool ApplyToSpecificService => SpecificServiceRadio.IsChecked == true;

    public ServiceItem? SelectedService =>
        ServiceListView.SelectedItem as ServiceItem;

    public ServicesDialog()
    {
        _logger = App.GetRequiredService<ILogger<ServicesDialog>>();
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;

        SpecificServiceRadio.Checked += (_, _) =>
        {
            ServiceListView.IsEnabled = true;
            ServiceShortNameInputBox.IsEnabled = false;
            HideValidationInfoBar();
        };
        AllServicesRadio.Checked += (_, _) =>
        {
            ServiceListView.IsEnabled = false;
            ServiceShortNameInputBox.IsEnabled = false;
            HideValidationInfoBar();
        };
        OnlyServicesRadio.Checked += (_, _) =>
        {
            ServiceListView.IsEnabled = false;
            ServiceShortNameInputBox.IsEnabled = false;
            HideValidationInfoBar();
        };
        ServiceShortNameRadio.Checked += (_, _) =>
        {
            ServiceListView.IsEnabled = false;
            ServiceShortNameInputBox.IsEnabled = true;
            HideValidationInfoBar();
        };

        ServiceListView.SelectionChanged += ServiceListView_SelectionChanged;
        Loaded += ServicesDialog_Loaded;
        Unloaded += ServicesDialog_Unloaded;
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_ServicesDialog_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 860,
            Height = 680,
            OnPrimaryButtonClick = ValidateSelection
        });

        return modalWindow.ShowDialogAsync();
    }

    public void ApplyServiceExpression(string? serviceExpression)
    {
        string normalizedExpression = serviceExpression?.Trim() ?? string.Empty;
        _appliedServiceExpression = normalizedExpression;

        if (string.IsNullOrWhiteSpace(normalizedExpression))
        {
            AllServicesRadio.IsChecked = true;
            return;
        }

        if (string.Equals(normalizedExpression, "*", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedExpression, "AnyService", StringComparison.OrdinalIgnoreCase))
        {
            OnlyServicesRadio.IsChecked = true;
            return;
        }

        ServiceItem? matchingService = _allServices.FirstOrDefault(service =>
            string.Equals(service.ShortName, normalizedExpression, StringComparison.OrdinalIgnoreCase));

        if (matchingService is not null)
        {
            SpecificServiceRadio.IsChecked = true;
            ServiceListView.SelectedItem = matchingService;
            ServiceListView.ScrollIntoView(matchingService);
            return;
        }

        ServiceShortNameRadio.IsChecked = true;
        ServiceShortNameInputBox.Text = normalizedExpression;
    }

    private async void ServicesDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
        await LoadServicesAsync();
    }

    private void ServicesDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private async System.Threading.Tasks.Task LoadServicesAsync()
    {
        try
        {
            _allServices = await System.Threading.Tasks.Task.Run(() =>
            {
                var services = ServiceController.GetServices()
                    .Select(s => new ServiceItem
                    {
                        DisplayName = s.DisplayName,
                        ShortName = s.ServiceName
                    })
                    .OrderBy(s => s.DisplayName)
                    .ToList();
                return services;
            });

            ServiceListView.ItemsSource = _allServices;
            ApplyServiceExpression(_appliedServiceExpression);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Windows services for ServicesDialog.");
        }
    }

    private void ServiceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HideValidationInfoBar();
    }

    private bool ValidateSelection()
    {
        if (ApplyToSpecificService && SelectedService is null)
        {
            ShowValidationInfoBar(LocalizedStrings.WF_ServicesDialog_SelectionRequired);
            return false;
        }

        if (ServiceShortNameRadio.IsChecked == true &&
            string.IsNullOrWhiteSpace(ServiceShortNameInputBox.Text))
        {
            ShowValidationInfoBar(LocalizedStrings.WF_ServicesDialog_ShortNameRequired);
            return false;
        }

        HideValidationInfoBar();
        return true;
    }

    private void ShowValidationInfoBar(string message)
    {
        ValidationInfoBar.Message = message;
        ValidationInfoBar.IsOpen = true;
    }

    private void HideValidationInfoBar()
    {
        ValidationInfoBar.IsOpen = false;
    }

    public class ServiceItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
    }
}
