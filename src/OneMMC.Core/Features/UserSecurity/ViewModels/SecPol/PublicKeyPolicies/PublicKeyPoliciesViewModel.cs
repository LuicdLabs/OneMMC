using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.PublicKeyPolicies;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.PublicKeyPolicies;

/// <summary>
/// View model for the Public Key Policies page.
/// </summary>
public sealed partial class PublicKeyPoliciesViewModel : ObservableObject
{
    private readonly PublicKeyPolicyService _policyService;
    private readonly ILogger<PublicKeyPoliciesViewModel> _logger;
    private readonly IAdminService _adminService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicKeyPoliciesViewModel"/> class.
    /// </summary>
    /// <param name="policyService">The Public Key Policies service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="adminService">The administrator service.</param>
    public PublicKeyPoliciesViewModel(
        PublicKeyPolicyService policyService,
        ILogger<PublicKeyPoliciesViewModel> logger,
        IAdminService adminService)
    {
        _policyService = policyService;
        _logger = logger;
        _adminService = adminService;
    }

    /// <summary>
    /// Raised when a policy operation requires administrator privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>
    /// Gets Public Key Policies nodes.
    /// </summary>
    public ObservableCollection<PublicKeyPolicyNode> Nodes { get; } = [];

    /// <summary>
    /// Gets the recovery-agent nodes (EFS, Data Protection, BitLocker), in display order.
    /// </summary>
    public ObservableCollection<PublicKeyPolicyNode> RecoveryAgentNodes { get; } = [];

    /// <summary>
    /// Gets the editable certificate-services nodes (enrollment policy, path validation, auto-enrollment).
    /// </summary>
    public ObservableCollection<PublicKeyPolicyNode> CertificateServiceNodes { get; } = [];

    /// <summary>
    /// Gets a value indicating whether any policy nodes have loaded.
    /// </summary>
    public bool HasNodes => Nodes.Count > 0;

    /// <summary>
    /// Gets or sets a value indicating whether policies are loading.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an error is visible.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the current error message.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Determines whether the given recovery-agent row can be deleted.
    /// </summary>
    /// <param name="row">The recovery-agent certificate row.</param>
    /// <returns><see langword="true"/> when the row represents a deletable certificate.</returns>
    public static bool CanDeleteRecoveryAgent(PublicKeyPolicyRow? row) =>
        row?.Kind == PublicKeyPolicyRowKind.Certificate
        && !string.IsNullOrWhiteSpace(row.Source)
        && !string.IsNullOrWhiteSpace(row.Thumbprint);

    /// <summary>
    /// Loads Public Key Policies.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            IReadOnlyList<PublicKeyPolicyNode> nodes = await Task.Run(() => _policyService.LoadNodes());
            Nodes.Clear();
            RecoveryAgentNodes.Clear();
            CertificateServiceNodes.Clear();
            foreach (PublicKeyPolicyNode node in nodes)
            {
                Nodes.Add(node);
                if (node.IsRecoveryAgentNode)
                {
                    RecoveryAgentNodes.Add(node);
                }
                else if (node.HasEditableSettings)
                {
                    CertificateServiceNodes.Add(node);
                }
            }

            OnPropertyChanged(nameof(HasNodes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PublicKeyPoliciesViewModel] Failed to load Public Key Policies.");
            ErrorMessage = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException
                ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
                : ex.Message;
            HasError = true;

            if (_adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException)
            {
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes Public Key Policies.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// Saves local machine certificate enrollment policy server-list settings.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveCertificateEnrollmentPolicyAsync(CertificateEnrollmentPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(() => _policyService.SaveCertificateEnrollmentPolicy(settings));
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PublicKeyPoliciesViewModel] Failed to save certificate enrollment policy.");
            bool isPermissionError = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException;
            ErrorMessage = isPermissionError
                ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
                : ex.Message;
            HasError = true;

            if (isPermissionError)
            {
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
    }

    /// <summary>
    /// Saves local machine certificate auto-enrollment settings.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveAutoEnrollmentAsync(CertificateAutoEnrollmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(() => _policyService.SaveAutoEnrollment(settings));
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PublicKeyPoliciesViewModel] Failed to save certificate auto-enrollment policy.");
            bool isPermissionError = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException;
            ErrorMessage = isPermissionError
                ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
                : ex.Message;
            HasError = true;

            if (isPermissionError)
            {
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
    }

    /// <summary>
    /// Saves local machine certificate path validation settings.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveCertificatePathValidationAsync(CertificatePathValidationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(() => _policyService.SaveCertificatePathValidation(settings));
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PublicKeyPoliciesViewModel] Failed to save certificate path validation policy.");
            bool isPermissionError = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException;
            ErrorMessage = isPermissionError
                ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
                : ex.Message;
            HasError = true;

            if (isPermissionError)
            {
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
    }

    /// <summary>
    /// Saves local machine Encrypting File System settings.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveEfsPolicyAsync(EfsPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(() => _policyService.SaveEfsPolicy(settings));
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PublicKeyPoliciesViewModel] Failed to save Encrypting File System policy.");
            bool isPermissionError = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException;
            ErrorMessage = isPermissionError
                ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
                : ex.Message;
            HasError = true;

            if (isPermissionError)
            {
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
    }

    /// <summary>
    /// Adds a recovery agent certificate to the selected node.
    /// </summary>
    /// <param name="nodeKind">The selected recovery-agent node kind.</param>
    /// <param name="certificatePath">The certificate file path.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> AddRecoveryAgentCertificateAsync(
        PublicKeyPolicyNodeKind nodeKind,
        string certificatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);

        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(() => _policyService.AddRecoveryAgentCertificate(nodeKind, certificatePath));
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PublicKeyPoliciesViewModel] Failed to add recovery agent certificate.");
            bool isPermissionError = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException;
            ErrorMessage = isPermissionError
                ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
                : ex.Message;
            HasError = true;

            if (isPermissionError)
            {
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
    }

    /// <summary>
    /// Deletes the selected recovery agent certificate.
    /// </summary>
    /// <param name="row">The selected recovery agent row.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> DeleteRecoveryAgentCertificateAsync(PublicKeyPolicyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(() => _policyService.DeleteRecoveryAgentCertificate(row));
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PublicKeyPoliciesViewModel] Failed to delete recovery agent certificate.");
            bool isPermissionError = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException;
            ErrorMessage = isPermissionError
                ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
                : ex.Message;
            HasError = true;

            if (isPermissionError)
            {
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
    }

    private static string GetString(string key, string resourceFileName)
    {
        string value = LocalizationProvider.Current.GetString(resourceFileName, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
