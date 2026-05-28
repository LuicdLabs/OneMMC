using System;
using ManagementTools.Core.Features.Certificates.Models;
using ManagementTools.Core.Features.Certificates.Services;
using ManagementTools.Core.Features.Certificates.ViewModels;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace ManagementTools.Views;

public sealed partial class CurrentUserCertificatesPage : Page
{
    private readonly IAdminService _adminService;
    private readonly CertificateNativeUiService _certificateNativeUiService;
    private readonly ILogger<CurrentUserCertificatesPage> _logger;

    public CurrentUserCertificatesPage()
    {
        _adminService = App.GetRequiredService<IAdminService>();
        _certificateNativeUiService = App.GetRequiredService<CertificateNativeUiService>();
        _logger = App.GetRequiredService<ILogger<CurrentUserCertificatesPage>>();
        ViewModel = App.GetRequiredService<CurrentUserCertificatesViewModel>();

        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
        Unloaded += OnUnloaded;
    }

    public CurrentUserCertificatesViewModel ViewModel { get; }

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Stores.Count == 0)
        {
            await ViewModel.LoadStoresCommand.ExecuteAsync(null);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        ViewModel.ClearCachedData();
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.FilterText = sender.Text;
        }
    }

    private async void RefreshAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadStoresCommand.ExecuteAsync(null);
    }

    private async void ImportStoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CertificateStoreNode store })
        {
            return;
        }

        try
        {
            if (_certificateNativeUiService.ImportToStore(store.StoreLocation, store.StoreName, GetOwnerWindowHandle()))
            {
                await ViewModel.RefreshStoreAsync(store.StoreName);
            }
        }
        catch (Exception ex)
        {
            await HandleOperationExceptionAsync(ex);
        }
    }

    private async void ExportStoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CertificateStoreNode store })
        {
            return;
        }

        try
        {
            _certificateNativeUiService.ExportStore(store.StoreLocation, store.StoreName, GetOwnerWindowHandle());
        }
        catch (Exception ex)
        {
            await HandleOperationExceptionAsync(ex);
        }
    }

    private void StoreOperationsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement owner)
        {
            return;
        }

        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };

        flyout.Items.Add(CreateStoreCommandSubMenu(
            LocalizedStrings.Certificates_ImportStoreCommand,
            ImportStoreButton_Click));
        flyout.Items.Add(CreateStoreCommandSubMenu(
            LocalizedStrings.Certificates_ExportStoreCommand,
            ExportStoreButton_Click));
        flyout.ShowAt(owner);
    }

    private MenuFlyoutSubItem CreateStoreCommandSubMenu(string text, RoutedEventHandler clickHandler)
    {
        var subMenu = new MenuFlyoutSubItem
        {
            Text = text,
            IsEnabled = ViewModel.Stores.Count > 0
        };

        foreach (CertificateStoreNode store in ViewModel.Stores)
        {
            var storeItem = new MenuFlyoutItem
            {
                Text = store.DisplayName,
                Tag = store
            };

            storeItem.Click += clickHandler;
            subMenu.Items.Add(storeItem);
        }

        return subMenu;
    }

    private async void OpenEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CertificateEntry entry })
        {
            await OpenEntryAsync(entry, openProperties: false);
        }
    }

    private async void PropertiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CertificateEntry entry })
        {
            await OpenEntryAsync(entry, openProperties: true);
        }
    }

    private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CertificateEntry entry })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.Certificates_DeleteConfirmTitle,
            Content = string.Format(LocalizedStrings.Certificates_DeleteConfirmMessage, entry.DisplayName),
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = App.CurrentTheme,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteEntryAsync(entry);
        }
    }

    private async void EntryContainer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CertificateEntry entry })
        {
            await OpenEntryAsync(entry, openProperties: false);
        }
    }

    private async Task OpenEntryAsync(CertificateEntry entry, bool openProperties)
    {
        try
        {
            bool propertiesChanged = _certificateNativeUiService.OpenEntry(
                entry,
                GetOwnerWindowHandle(),
                openProperties,
                ViewModel.CanWriteToStores);

            if (propertiesChanged)
            {
                await ViewModel.RefreshStoreAsync(entry.StoreName);
            }
        }
        catch (Exception ex)
        {
            await HandleOperationExceptionAsync(ex);
        }
    }

    private async Task HandleOperationExceptionAsync(Exception ex)
    {
        _logger.LogError(ex, "Certificate operation failed on the current-user page.");
        ViewModel.HasError = true;
        ViewModel.ErrorMessage = _adminService.IsPermissionError(ex)
            ? LocalizedStrings.Common_AccessDenied_Generic
            : ex.Message;

        if (_adminService.IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
        }
    }

    private static nint GetOwnerWindowHandle() =>
        App.MainWindowInstance is null ? nint.Zero : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
}
