using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Scope;

public sealed partial class ApplicationPackagesDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly FirewallAppContainerService _appContainerService;
    private List<PackageItem> _allPackages = new();
    private readonly Action<ElementTheme> _themeChangedHandler;
    private string _pendingLocalAppPackageId = string.Empty;

    public string SelectedPackageSid =>
        PackageSidRadio.IsChecked == true ? PackageSidBox.Text : string.Empty;

    public bool ApplyToAllPackages => AllPackagesRadio.IsChecked == true;
    public bool ApplyToPackagesOnly => PackagesOnlyRadio.IsChecked == true;
    public bool ApplyToSpecificPackage => SpecificPackageRadio.IsChecked == true;

    public PackageItem? SelectedPackage =>
        PackageListView.SelectedItem as PackageItem;

    public string LocalAppPackageIdExpression
    {
        get
        {
            if (PackageSidRadio.IsChecked == true)
            {
                return NormalizeLocalAppPackageId(PackageSidBox.Text);
            }

            if (SpecificPackageRadio.IsChecked == true)
            {
                return NormalizeLocalAppPackageId(SelectedPackage?.LocalAppPackageId);
            }

            if (PackagesOnlyRadio.IsChecked == true)
            {
                return ApplicationPackagesOnlyWildcard;
            }

            return string.Empty;
        }
    }

    public ApplicationPackagesDialog()
    {
        _appContainerService = App.GetRequiredService<FirewallAppContainerService>();
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += ApplicationPackagesDialog_Loaded;
        Unloaded += ApplicationPackagesDialog_Unloaded;
        PrimaryButtonClick += ApplicationPackagesDialog_PrimaryButtonClick;

        SpecificPackageRadio.Checked += (_, _) =>
        {
            PackageListView.IsEnabled = true;
            PackageSidBox.IsEnabled = false;
            HideValidationInfoBar();
        };
        AllPackagesRadio.Checked += (_, _) =>
        {
            PackageListView.IsEnabled = false;
            PackageSidBox.IsEnabled = false;
            HideValidationInfoBar();
        };
        PackagesOnlyRadio.Checked += (_, _) =>
        {
            PackageListView.IsEnabled = false;
            PackageSidBox.IsEnabled = false;
            HideValidationInfoBar();
        };
        PackageSidRadio.Checked += (_, _) =>
        {
            PackageListView.IsEnabled = false;
            PackageSidBox.IsEnabled = true;
            HideValidationInfoBar();
        };

        PackageListView.SelectionChanged += PackageListView_SelectionChanged;
    }

    public void ApplyLocalAppPackageId(string localAppPackageId)
    {
        _pendingLocalAppPackageId = NormalizeLocalAppPackageId(localAppPackageId);
        TryApplyPendingSelection();
    }

    private async void ApplicationPackagesDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
        await LoadPackagesAsync();
    }

    private void ApplicationPackagesDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private async Task LoadPackagesAsync()
    {
        _allPackages = await Task.Run(() => _appContainerService.GetAppContainers()
            .Select(container => new PackageItem
            {
                Name = container.AppContainerName,
                User = container.UserDisplayName,
                LocalAppPackageId = container.AppContainerSid
            })
            .OrderBy(package => package.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(package => package.User, StringComparer.CurrentCultureIgnoreCase)
            .ToList());

        PackageListView.ItemsSource = _allPackages;
        TryApplyPendingSelection();
    }

    private void PackageListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HideValidationInfoBar();
    }

    private void ApplicationPackagesDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SpecificPackageRadio.IsChecked == true && SelectedPackage is null)
        {
            ShowValidationInfoBar(LocalizedStrings.WF_ApplicationPackagesDialog_SelectionRequired);
            args.Cancel = true;
            return;
        }

        if (PackageSidRadio.IsChecked == true && string.IsNullOrWhiteSpace(PackageSidBox.Text))
        {
            ShowValidationInfoBar(LocalizedStrings.WF_ApplicationPackagesDialog_SidRequired);
            args.Cancel = true;
        }
    }

    private void TryApplyPendingSelection()
    {
        if (string.IsNullOrWhiteSpace(_pendingLocalAppPackageId))
        {
            AllPackagesRadio.IsChecked = true;
            return;
        }

        if (string.Equals(_pendingLocalAppPackageId, ApplicationPackagesOnlyWildcard, StringComparison.OrdinalIgnoreCase))
        {
            PackagesOnlyRadio.IsChecked = true;
            return;
        }

        if (_allPackages.Count > 0)
        {
            PackageItem? sidMatch = _allPackages.FirstOrDefault(item =>
                string.Equals(item.LocalAppPackageId, _pendingLocalAppPackageId, StringComparison.OrdinalIgnoreCase));
            if (sidMatch is not null)
            {
                SpecificPackageRadio.IsChecked = true;
                PackageListView.SelectedItem = sidMatch;
                PackageListView.ScrollIntoView(sidMatch);
                return;
            }

            PackageItem? nameMatch = _allPackages.FirstOrDefault(item =>
                string.Equals(item.Name, _pendingLocalAppPackageId, StringComparison.OrdinalIgnoreCase));
            if (nameMatch is not null)
            {
                SpecificPackageRadio.IsChecked = true;
                PackageListView.SelectedItem = nameMatch;
                PackageListView.ScrollIntoView(nameMatch);
                return;
            }
        }

        PackageSidRadio.IsChecked = true;
        PackageSidBox.Text = _pendingLocalAppPackageId;
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

    private const string AllApplicationPackagesSid = "S-1-15-2-1";
    private const string ApplicationPackagesOnlyWildcard = "*";

    private static string NormalizeLocalAppPackageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        if (string.Equals(normalized, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (string.Equals(normalized, ApplicationPackagesOnlyWildcard, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationPackagesOnlyWildcard;
        }

        if (string.Equals(normalized, "AllApplicationPackages", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ALL APPLICATION PACKAGES", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "APPLICATION PACKAGE AUTHORITY\\ALL APPLICATION PACKAGES", StringComparison.OrdinalIgnoreCase))
        {
            return AllApplicationPackagesSid;
        }

        return normalized;
    }

}

/// <summary>
/// Application package row in the packages picker. Namespace-level so it can be
/// referenced from XAML via x:DataType for compiled (AOT-safe) bindings.
/// </summary>
public class PackageItem
{
    public string Name { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string LocalAppPackageId { get; set; } = string.Empty;
}
