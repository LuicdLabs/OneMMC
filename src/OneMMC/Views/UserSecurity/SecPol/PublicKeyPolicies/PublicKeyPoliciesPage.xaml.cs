using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.PublicKeyPolicies;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

public sealed partial class PublicKeyPoliciesPage : Page
{
    private const string CertificateFilesFilterPattern = "*.cer;*.crt;*.der;*.pem";
    private const string AllFilesFilterPattern = "*.*";
    private const string RecoveryAgentCertificatePickerSettingsIdentifier = "PublicKeyPoliciesRecoveryAgentCertificatePicker";

    private readonly IFileDialogService _fileDialogService;
    private readonly IAdminService _adminService;
    private readonly ILogger<PublicKeyPoliciesPage> _logger;
    private bool _hasLoaded;

    internal readonly LocalizedStrings LocalizedStrings = LocalizedStrings.Instance;

    public PublicKeyPoliciesViewModel ViewModel { get; }

    public PublicKeyPoliciesPage()
    {
        _logger = App.GetRequiredService<ILogger<PublicKeyPoliciesPage>>();
        _fileDialogService = App.GetRequiredService<IFileDialogService>();
        _adminService = App.GetRequiredService<IAdminService>();
        ViewModel = App.GetRequiredService<PublicKeyPoliciesViewModel>();

        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _logger.LogDebug("[PublicKeyPoliciesPage] Page loaded.");
        _hasLoaded = true;
        await ViewModel.LoadAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        DataContext = null;
        Loaded -= OnPageLoaded;
        Unloaded -= OnPageUnloaded;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private async void PolicySettingsCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PublicKeyPolicyNode node })
        {
            await ShowNodePropertiesAsync(node);
        }
    }

    private async void ViewCertificateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PublicKeyPolicyRow row })
        {
            await ShowCertificateDetailsAsync(row);
        }
    }

    private async void AddRecoveryAgentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PublicKeyPolicyNode node })
        {
            await AddRecoveryAgentCertificateAsync(node);
        }
    }

    private async void DeleteRecoveryAgentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PublicKeyPolicyRow row })
        {
            await DeleteRecoveryAgentCertificateAsync(row);
        }
    }

    private async Task ShowCertificateDetailsAsync(PublicKeyPolicyRow? row)
    {
        if (row?.CanViewDetails != true)
        {
            return;
        }

        if (row.CertificateRawData is null)
        {
            await ShowCertificateViewerErrorAsync(LocalizedStrings.PKP_CertificateBlobUnavailable);
            return;
        }

        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(row.CertificateRawData);
            nint ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            X509Certificate2UI.DisplayCertificate(certificate, ownerWindowHandle);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Unable to show native certificate dialog for {Thumbprint}.", row.Thumbprint);
            await ShowCertificateViewerErrorAsync(LocalizedStrings.PKP_CertificateBlobUnavailable);
        }
    }

    private async Task ShowCertificateViewerErrorAsync(string message)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.Common_ErrorTitle,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            DefaultButton = WindowDialogResult.None,
            Width = 420,
            Height = 220
        });

        _ = await modalWindow.ShowDialogAsync();
    }

    private async Task AddRecoveryAgentCertificateAsync(PublicKeyPolicyNode node)
    {
        if (!node.IsRecoveryAgentNode)
        {
            return;
        }

        if (!await EnsureAdministratorAsync())
        {
            return;
        }

        nint ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? certificatePath = await _fileDialogService.OpenFileAsync(
            ownerWindowHandle,
            CreateCertificateFilesFilter(),
            LocalizedStrings.PKP_Dialog_SelectRecoveryAgentCertificate_Title,
            null,
            LocalizedStrings.Common_OpenButton,
            RecoveryAgentCertificatePickerSettingsIdentifier);
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            return;
        }

        bool added = await ViewModel.AddRecoveryAgentCertificateAsync(node.Kind, certificatePath);
        if (!added && ViewModel.HasError && !string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
        {
            await ShowAddRecoveryAgentErrorAsync(ViewModel.ErrorMessage);
        }
    }

    private async Task ShowAddRecoveryAgentErrorAsync(string message)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.PKP_Command_AddRecoveryAgent,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            CloseButtonText = LocalizedStrings.Common_OKButton,
            DefaultButton = WindowDialogResult.None,
            Width = 420,
            Height = 220
        });

        _ = await modalWindow.ShowDialogAsync();
    }

    private async Task DeleteRecoveryAgentCertificateAsync(PublicKeyPolicyRow row)
    {
        if (!PublicKeyPoliciesViewModel.CanDeleteRecoveryAgent(row))
        {
            return;
        }

        if (!await EnsureAdministratorAsync())
        {
            return;
        }

        string displayName = string.IsNullOrWhiteSpace(row.CertificateIssuedToDisplay)
            ? row.Thumbprint
            : row.CertificateIssuedToDisplay;
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.PKP_Dialog_DeleteRecoveryAgent_Title,
            Content = new TextBlock
            {
                Text = string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizedStrings.PKP_Dialog_DeleteRecoveryAgent_MessageFormat,
                    displayName),
                TextWrapping = TextWrapping.Wrap
            },
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.None,
            IsPrimaryButtonLeading = true,
            Width = 480,
            Height = 240
        });

        if (await modalWindow.ShowDialogAsync() == WindowDialogResult.Primary)
        {
            await ViewModel.DeleteRecoveryAgentCertificateAsync(row);
        }
    }

    private async Task<bool> EnsureAdministratorAsync()
    {
        if (_adminService.IsRunningAsAdmin)
        {
            return true;
        }

        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
        return false;
    }

    private async Task ShowNodePropertiesAsync(PublicKeyPolicyNode? node)
    {
        if (node?.CanViewProperties != true)
        {
            return;
        }

        if (node.EfsSettings is EfsPolicySettings efsSettings)
        {
            await ShowEfsPropertiesAsync(node.DisplayName, efsSettings);
            return;
        }

        if (node.EnrollmentPolicySettings is CertificateEnrollmentPolicySettings enrollmentPolicySettings)
        {
            await ShowEnrollmentPolicyPropertiesAsync(node.DisplayName, enrollmentPolicySettings);
            return;
        }

        if (node.PathValidationSettings is CertificatePathValidationSettings pathValidationSettings)
        {
            await ShowPathValidationPropertiesAsync(node.DisplayName, pathValidationSettings);
            return;
        }

        if (node.AutoEnrollmentSettings is CertificateAutoEnrollmentSettings autoEnrollmentSettings)
        {
            await ShowAutoEnrollmentPropertiesAsync(node.DisplayName, autoEnrollmentSettings);
            return;
        }

        string title = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.PKP_Dialog_PolicyProperties_TitleFormat,
            node.DisplayName);
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = CreateDetailsContent(node.DisplayName, node.Details),
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            DefaultButton = WindowDialogResult.None,
            Width = 760,
            Height = 560
        });

        _ = await modalWindow.ShowDialogAsync();
    }

    private async Task ShowEnrollmentPolicyPropertiesAsync(
        string nodeName,
        CertificateEnrollmentPolicySettings settings)
    {
        var editor = new CertificateEnrollmentPolicyPropertiesControl(settings);

        string title = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.PKP_Dialog_PolicyProperties_TitleFormat,
            nodeName);
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = 680,
            Height = 560
        });

        if (await modalWindow.ShowDialogAsync() == WindowDialogResult.Primary
            && await EnsureAdministratorAsync())
        {
            await ViewModel.SaveCertificateEnrollmentPolicyAsync(editor.GetSettings());
        }
    }

    private async Task ShowEfsPropertiesAsync(
        string nodeName,
        EfsPolicySettings settings)
    {
        var editor = new EfsPropertiesControl(settings);

        string title = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.PKP_Dialog_PolicyProperties_TitleFormat,
            nodeName);
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = 760,
            Height = 700
        });

        if (await modalWindow.ShowDialogAsync() == WindowDialogResult.Primary
            && await EnsureAdministratorAsync())
        {
            await ViewModel.SaveEfsPolicyAsync(editor.GetSettings());
        }
    }

    private async Task ShowPathValidationPropertiesAsync(
        string nodeName,
        CertificatePathValidationSettings settings)
    {
        var editor = new CertificatePathValidationPropertiesControl(settings);

        string title = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.PKP_Dialog_PolicyProperties_TitleFormat,
            nodeName);
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = 760,
            Height = 650
        });

        if (await modalWindow.ShowDialogAsync() == WindowDialogResult.Primary
            && await EnsureAdministratorAsync())
        {
            await ViewModel.SaveCertificatePathValidationAsync(editor.GetSettings());
        }
    }

    private async Task ShowAutoEnrollmentPropertiesAsync(
        string nodeName,
        CertificateAutoEnrollmentSettings settings)
    {
        var editor = new CertificateAutoEnrollmentPropertiesControl(settings);

        string title = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.PKP_Dialog_PolicyProperties_TitleFormat,
            nodeName);
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = 620,
            Height = 500
        });

        if (await modalWindow.ShowDialogAsync() == WindowDialogResult.Primary
            && await EnsureAdministratorAsync())
        {
            await ViewModel.SaveAutoEnrollmentAsync(editor.GetSettings());
        }
    }

    private static UIElement CreateDetailsContent(string title, IEnumerable<PublicKeyPolicyDetailItem> details)
    {
        StackPanel panel = new()
        {
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        foreach (PublicKeyPolicyDetailItem detail in details)
        {
            panel.Children.Add(CreateDetailRow(detail));
        }

        return panel;
    }

    private static Grid CreateDetailRow(PublicKeyPolicyDetailItem detail)
    {
        Grid row = new()
        {
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new TextBlock
        {
            Text = detail.Name,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center
        });

        TextBox valueBox = new()
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            Text = detail.Value,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueBox, 1);
        row.Children.Add(valueBox);

        return row;
    }

    private string CreateCertificateFilesFilter()
    {
        return $"{LocalizedStrings.PKP_FileDialog_CertificateFiles}\0{CertificateFilesFilterPattern}\0"
            + $"{LocalizedStrings.PKP_FileDialog_AllFiles}\0{AllFilesFilterPattern}\0";
    }
}
