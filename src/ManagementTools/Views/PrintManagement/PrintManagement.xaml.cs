using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;
using ManagementTools.Core.Features.PrintManagement.Services.PrintManagement;
using ManagementTools.Core.Features.PrintManagement.ViewModels.PrintManagement;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ManagementTools.Views;

/// <summary>
/// Print Management page providing management for local printers, drivers, ports, and forms.
/// </summary>
public sealed partial class PrintManagement : Page
{
    public PrintManagementViewModel ViewModel { get; }
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly IAdminService _adminService;
    private readonly ILogger<PrintManagement> _logger;
    private readonly PrintManagementService _printService;
    private bool _isDialogOpen;

    public PrintManagement()
    {
        ViewModel = App.GetRequiredService<PrintManagementViewModel>();
        _adminService = App.GetRequiredService<IAdminService>();
        _logger = App.GetRequiredService<ILogger<PrintManagement>>();
        _printService = App.GetRequiredService<PrintManagementService>();

        InitializeComponent();
        DataContext = ViewModel;
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;

        ApplyLocalizedText();

        // Load data when page is loaded
        Loaded += async (_, _) => await LoadAndPopulateAsync();
        Unloaded += (_, _) =>
        {
            App.ThemeChanged -= OnThemeChanged;
            ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        };
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    /// <summary>
    /// Loads data from ViewModel and refreshes template bindings.
    /// </summary>
    private async Task LoadAndPopulateAsync()
    {
        LoadingBar.Visibility = Visibility.Visible;
        try
        {
            await ViewModel.LoadDataAsync();
        }
        catch (Exception)
        {
            // LoadDataAsync has its own internal error handling; if it faults here, continue
            // so the UI can still show empty-state messages.
        }

        LoadingBar.Visibility = Visibility.Collapsed;
        UpdateSectionState();
    }

    private void ApplyLocalizedText()
    {
        ServerNameText.Text = string.Format(LocalizedStrings.PrintMgmt_CurrentPrintServerFormat, ViewModel.ComputerName);
        RefreshButton.Label = LocalizedStrings.Common_Refresh;
        PrintersExpander.Header = LocalizedStrings.PrintMgmt_PrintersHeader;
        DeployedPrintersExpander.Header = LocalizedStrings.PrintMgmt_DeployedPrintersHeader;
        DriversExpander.Header = LocalizedStrings.PrintMgmt_DriversHeader;
        PortsCard.Header = LocalizedStrings.PrintMgmt_ModifyPrintPorts;
        FormsCard.Header = LocalizedStrings.PrintMgmt_ViewEditPrintForms;
        PortsButton.Content = LocalizedStrings.PrintMgmt_PortsButton;
        FormsButton.Content = LocalizedStrings.PrintMgmt_FormsButton;
        NoPrintersText.Text = LocalizedStrings.PrintMgmt_NoPrintersFound;
        NoDriversText.Text = LocalizedStrings.PrintMgmt_NoDriversFound;
        NoDeployedPrintersText.Text = LocalizedStrings.PrintMgmt_NoDeployedPrintersFound;
    }

    private void UpdateSectionState()
    {
        PrintersExpander.Description = string.Format(LocalizedStrings.PrintMgmt_PrintersCountFormat, ViewModel.Printers.Count);
        DriversExpander.Description = string.Format(LocalizedStrings.PrintMgmt_DriversCountFormat, ViewModel.Drivers.Count);
        DeployedPrintersExpander.Description = string.Format(LocalizedStrings.PrintMgmt_DeployedPrintersCountFormat, ViewModel.DeployedPrinters.Count);

        NoPrintersText.Visibility = ViewModel.Printers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoDriversText.Visibility = ViewModel.Drivers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoDeployedPrintersText.Visibility = ViewModel.DeployedPrinters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Refreshes all print management data.
    /// </summary>
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await LoadAndPopulateAsync();

    /// <summary>
    /// Open Legacy Print Management
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void OpenPrintMgrLegacy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "printmanagement.msc",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            if (_adminService.IsPermissionError(ex))
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
            }
            else
            {
                _logger.LogError(ex, "Failed to open legacy Print Management.");
                await ShowErrorDialogAsync(string.Format(LocalizedStrings.PrintMgmt_ActionFailedFormat, ex.Message));
            }
        }
    }

    /// <summary>
    /// Shows the Ports ContentDialog with a ListView of ports.
    /// </summary>
    private async void PortsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDialogOpen) return;
        _isDialogOpen = true;

        try
        {
            var dialog = new PortsDialog(ViewModel, XamlRoot);
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019)) { }
        finally
        {
            _isDialogOpen = false;
        }
    }

    /// <summary>
    /// Shows the Forms ContentDialog with a ListView of forms.
    /// </summary>
    private async void FormsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDialogOpen) return;
        _isDialogOpen = true;

        try
        {
            var dialog = new FormsDialog(ViewModel, XamlRoot);
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019)) { }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private void PrinterMenuFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        PrinterInfo? printer = GetContextFromFlyout<PrinterInfo>(flyout);
        if (printer is null || flyout.Items.Count < 9)
        {
            return;
        }

        SetMenuText(flyout.Items[0], LocalizedStrings.PrintMgmt_MenuOpenPrinterQueue);
        SetMenuText(flyout.Items[2], LocalizedStrings.PrintMgmt_MenuDeployWithGroupPolicy);
        SetMenuText(flyout.Items[3], LocalizedStrings.PrintMgmt_MenuSetPrintingDefaults);
        SetMenuText(flyout.Items[4], LocalizedStrings.PrintMgmt_MenuPrintTestPage);
        SetMenuText(flyout.Items[5], LocalizedStrings.PrintMgmt_MenuProperties);
        SetMenuText(flyout.Items[7], LocalizedStrings.Common_DeleteButton);
        SetMenuText(flyout.Items[8], LocalizedStrings.PrintMgmt_MenuRename);

        if (flyout.Items[1] is MenuFlyoutItem pauseResumeItem)
        {
            pauseResumeItem.Text = printer.IsPaused
                ? LocalizedStrings.PrintMgmt_MenuResumePrinting
                : LocalizedStrings.PrintMgmt_MenuPausePrinting;
        }

        if (flyout.Items[2] is MenuFlyoutItem deployItem)
        {
            deployItem.IsEnabled = true;
        }
    }

    private void DriverMenuFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        PrintDriverInfo? driver = GetContextFromFlyout<PrintDriverInfo>(flyout);
        if (driver is null || flyout.Items.Count < 5)
        {
            return;
        }

        SetMenuText(flyout.Items[0], LocalizedStrings.PrintMgmt_MenuRemoveDriverPackage);
        SetMenuText(flyout.Items[2], LocalizedStrings.PrintMgmt_MenuProperties);
        SetMenuText(flyout.Items[4], LocalizedStrings.Common_DeleteButton);

        if (flyout.Items[0] is MenuFlyoutItem removePackageItem)
        {
            removePackageItem.Visibility = string.IsNullOrWhiteSpace(driver.InfName)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        if (flyout.Items[1] is MenuFlyoutSubItem isolationSubItem && isolationSubItem.Items.Count >= 4)
        {
            isolationSubItem.Text = LocalizedStrings.PrintMgmt_MenuSetDriverIsolation;
            SetMenuText(isolationSubItem.Items[0], LocalizedStrings.PrintMgmt_IsolationNone);
            SetMenuText(isolationSubItem.Items[1], LocalizedStrings.PrintMgmt_IsolationShared);
            SetMenuText(isolationSubItem.Items[2], LocalizedStrings.PrintMgmt_IsolationIsolated);
            SetMenuText(isolationSubItem.Items[3], LocalizedStrings.PrintMgmt_IsolationSystemDefault);

            string isolationMode = NormalizeIsolationMode(driver.IsolationMode);
            SetIsolationCheckState(isolationSubItem.Items[0], isolationMode == "None");
            SetIsolationCheckState(isolationSubItem.Items[1], isolationMode == "Shared");
            SetIsolationCheckState(isolationSubItem.Items[2], isolationMode == "Isolated");
            SetIsolationCheckState(isolationSubItem.Items[3], isolationMode == "System Default");
        }
    }

    private void DeployedPrinterMenuFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        PrinterInfo? printer = GetContextFromFlyout<PrinterInfo>(flyout);
        if (printer is null || flyout.Items.Count == 0)
        {
            return;
        }

        SetMenuText(flyout.Items[0], LocalizedStrings.PrintMgmt_MenuDeployWithGroupPolicy);

        if (flyout.Items[0] is MenuFlyoutItem deployItem)
        {
            deployItem.IsEnabled = true;
        }
    }

    private async void OpenPrinterQueue_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        await ExecuteActionAsync(() => _printService.OpenPrinterQueueAsync(printer.Name), refreshAfter: false);
    }

    private async void PauseResumePrinter_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        await ExecuteActionAsync(
            () => printer.IsPaused
                ? _printService.ResumePrinterAsync(printer.Name)
                : _printService.PausePrinterAsync(printer.Name));
    }

    private async void DeployPrinter_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        string? connectionPath = BuildPrinterConnectionPath(printer);
        if (string.IsNullOrWhiteSpace(connectionPath))
        {
            await ShowErrorDialogAsync(LocalizedStrings.PrintMgmt_DeployDialogUnavailable);
            return;
        }

        var dialog = new DeployPrinterDialog(printer.Name, connectionPath, XamlRoot);
        await dialog.LoadAsync();
        await ShowExclusiveDialogAsync(dialog);
    }

    private async void SetPrintingDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        await ExecuteActionAsync(
            () => _printService.ShowPrintingDefaultsAsync(GetWindowHandle(), printer.Name),
            refreshAfter: false);
    }

    private async void PrintTestPage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        await ExecuteActionAsync(() => _printService.PrintTestPageAsync(printer.Name), refreshAfter: false);
    }

    private async void PrinterProperties_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        await ExecuteActionAsync(
            () => _printService.ShowPrinterPropertiesAsync(GetWindowHandle(), printer.Name),
            refreshAfter: false);
    }

    private async void DeletePrinter_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        bool confirmed = await ShowConfirmationDialogAsync(
            string.Format(LocalizedStrings.PrintMgmt_DeletePrinterTitleFormat, printer.Name),
            string.Format(LocalizedStrings.PrintMgmt_DeletePrinterMessageFormat, printer.Name),
            LocalizedStrings.Common_DeleteButton);

        if (!confirmed)
        {
            return;
        }

        await ExecuteActionAsync(async () =>
        {
            EnsureAdministrator();
            await _printService.DeletePrinterAsync(printer.Name);
        });
    }

    private async void RenamePrinter_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrinterInfo? printer) || printer is null)
        {
            return;
        }

        string? newName = await ShowTextInputDialogAsync(
            string.Format(LocalizedStrings.PrintMgmt_RenamePrinterTitleFormat, printer.Name),
            LocalizedStrings.PrintMgmt_RenamePrinterInstruction,
            printer.Name);

        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, printer.Name, StringComparison.Ordinal))
        {
            return;
        }

        await ExecuteActionAsync(async () =>
        {
            EnsureAdministrator();
            await _printService.RenamePrinterAsync(printer.Name, newName.Trim());
        });
    }

    private async void RemoveDriverPackage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrintDriverInfo? driver) || driver is null)
        {
            return;
        }

        bool confirmed = await ShowConfirmationDialogAsync(
            string.Format(LocalizedStrings.PrintMgmt_RemoveDriverPackageTitleFormat, driver.Name),
            string.Format(LocalizedStrings.PrintMgmt_RemoveDriverPackageMessageFormat, driver.InfName),
            LocalizedStrings.Common_RemoveButton);

        if (!confirmed)
        {
            return;
        }

        await ExecuteActionAsync(async () =>
        {
            EnsureAdministrator();
            await _printService.RemoveDriverPackageAsync(driver);
        });
    }

    private async void SetDriverIsolationNone_Click(object sender, RoutedEventArgs e) =>
        await SetDriverIsolationAsync(sender, "None");

    private async void SetDriverIsolationShared_Click(object sender, RoutedEventArgs e) =>
        await SetDriverIsolationAsync(sender, "Shared");

    private async void SetDriverIsolationIsolated_Click(object sender, RoutedEventArgs e) =>
        await SetDriverIsolationAsync(sender, "Isolated");

    private async void SetDriverIsolationDefault_Click(object sender, RoutedEventArgs e) =>
        await SetDriverIsolationAsync(sender, "System Default");

    private async void DriverProperties_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrintDriverInfo? driver) || driver is null)
        {
            return;
        }

        var content = new StackPanel { Spacing = 8 };
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyNameLabel, driver.Name);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyInfLabel, driver.InfName);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyVersionLabel, driver.DriverVersion);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyEnvironmentLabel, driver.EnvironmentName);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyIsolationLabel, driver.IsolationMode);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyPathLabel, driver.DriverPath);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyDataFileLabel, driver.DataFile);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyConfigFileLabel, driver.ConfigFile);
        AddDetailLine(content, LocalizedStrings.PrintMgmt_DriverPropertyMonitorLabel, driver.MonitorName);

        var dialog = new ContentDialog
        {
            Title = string.Format(LocalizedStrings.PrintMgmt_DriverPropertiesTitleFormat, driver.Name),
            Content = new ScrollViewer { Content = content, MaxHeight = 500 },
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        await ShowExclusiveDialogAsync(dialog);
    }

    private async void DeleteDriver_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuItemContext(sender, out PrintDriverInfo? driver) || driver is null)
        {
            return;
        }

        bool confirmed = await ShowConfirmationDialogAsync(
            string.Format(LocalizedStrings.PrintMgmt_DeleteDriverTitleFormat, driver.Name),
            string.Format(LocalizedStrings.PrintMgmt_DeleteDriverMessageFormat, driver.Name),
            LocalizedStrings.Common_DeleteButton);

        if (!confirmed)
        {
            return;
        }

        await ExecuteActionAsync(async () =>
        {
            EnsureAdministrator();
            await _printService.DeleteDriverAsync(driver);
        });
    }

    private async Task SetDriverIsolationAsync(object sender, string isolationMode)
    {
        if (!TryGetMenuItemContext(sender, out PrintDriverInfo? driver) || driver is null)
        {
            return;
        }

        await ExecuteActionAsync(async () =>
        {
            EnsureAdministrator();
            await _printService.SetDriverIsolationModeAsync(driver.Name, isolationMode);
        });
    }

    private async Task ExecuteActionAsync(Func<Task> action, bool refreshAfter = true)
    {
        try
        {
            await action();
            if (refreshAfter)
            {
                await LoadAndPopulateAsync();
            }
        }
        catch (Exception ex)
        {
            await HandleActionExceptionAsync(ex);
        }
    }

    private async Task HandleActionExceptionAsync(Exception ex)
    {
        if (_adminService.IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
            return;
        }

        _logger.LogError(ex, "A print management action failed.");
        await ShowErrorDialogAsync(string.Format(LocalizedStrings.PrintMgmt_ActionFailedFormat, ex.Message));
    }

    private static void SetIsolationCheckState(MenuFlyoutItemBase item, bool isChecked)
    {
        if (item is MenuFlyoutItem menuItem)
        {
            menuItem.Icon = isChecked ? new FontIcon { Glyph = "\uE73E" } : null;
        }
    }

    private static void SetMenuText(MenuFlyoutItemBase item, string text)
    {
        switch (item)
        {
            case MenuFlyoutItem menuItem:
                menuItem.Text = text;
                break;

            case MenuFlyoutSubItem subItem:
                subItem.Text = text;
                break;
        }
    }

    private string? BuildPrinterConnectionPath(PrinterInfo printer)
    {
        if (!string.IsNullOrWhiteSpace(printer.ServerName))
        {
            string serverName = printer.ServerName.TrimStart('\\');
            string printerShare = string.IsNullOrWhiteSpace(printer.ShareName) ? printer.Name : printer.ShareName;
            return $@"\\{serverName}\{printerShare}";
        }

        if (printer.IsShared && !string.IsNullOrWhiteSpace(printer.ShareName))
        {
            return $@"\\{ViewModel.ComputerName}\{printer.ShareName}";
        }

        return null;
    }

    private static T? GetContextFromFlyout<T>(MenuFlyout flyout) where T : class
    {
        return flyout.Items
            .OfType<MenuFlyoutItem>()
            .Select(item => item.Tag)
            .OfType<T>()
            .FirstOrDefault();
    }

    private static bool TryGetMenuItemContext<T>(object sender, out T? value) where T : class
    {
        value = (sender as MenuFlyoutItem)?.Tag as T;
        return value is not null;
    }

    private void EnsureAdministrator()
    {
        if (!_adminService.IsRunningAsAdmin)
        {
            throw new UnauthorizedAccessException(LocalizedStrings.Common_AccessDenied_Generic);
        }
    }

    private IntPtr GetWindowHandle() => WindowNative.GetWindowHandle(App.MainWindowInstance);

    private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        ContentDialogResult? result = await ShowExclusiveDialogAsync(dialog);
        return result == ContentDialogResult.Primary;
    }

    private async Task<string?> ShowTextInputDialogAsync(string title, string message, string currentValue)
    {
        var textBox = new TextBox
        {
            Text = currentValue,
            MinWidth = 320
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords });
        content.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = LocalizedStrings.Common_SaveButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        ContentDialogResult? result = await ShowExclusiveDialogAsync(dialog);
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.Common_ErrorTitle,
            Content = message,
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        await ShowExclusiveDialogAsync(dialog);
    }

    private async Task<ContentDialogResult?> ShowExclusiveDialogAsync(ContentDialog dialog)
    {
        if (_isDialogOpen)
        {
            return null;
        }

        _isDialogOpen = true;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80000019))
        {
            return null;
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private static void AddDetailLine(Panel panel, string label, string? value)
    {
        panel.Children.Add(new TextBlock
        {
            Text = $"{label}: {value}",
            TextWrapping = TextWrapping.WrapWholeWords
        });
    }

    private static string NormalizeIsolationMode(string? isolationMode)
    {
        if (string.IsNullOrWhiteSpace(isolationMode))
        {
            return "System Default";
        }

        return isolationMode switch
        {
            "None" => "None",
            "Shared" => "Shared",
            "Isolated" => "Isolated",
            _ => "System Default"
        };
    }
}
