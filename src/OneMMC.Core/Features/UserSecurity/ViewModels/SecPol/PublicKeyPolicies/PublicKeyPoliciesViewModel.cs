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
    private List<PublicKeyPolicyRow> _allRows = [];

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
    /// Gets rows shown for the selected node.
    /// </summary>
    public ObservableCollection<PublicKeyPolicyRow> CurrentRows { get; } = [];

    /// <summary>
    /// Gets or sets the selected node.
    /// </summary>
    [ObservableProperty]
    public partial PublicKeyPolicyNode? SelectedNode { get; set; }

    /// <summary>
    /// Gets a value indicating whether the selected node shows recovery-agent certificates.
    /// </summary>
    public bool IsRecoveryAgentNodeSelected => SelectedNode?.Kind is PublicKeyPolicyNodeKind.EncryptingFileSystem
        or PublicKeyPolicyNodeKind.DataProtection
        or PublicKeyPolicyNodeKind.BitLockerDriveEncryption;

    /// <summary>
    /// Gets a value indicating whether the selected node shows name/value policy settings.
    /// </summary>
    public bool IsSettingNodeSelected => !IsRecoveryAgentNodeSelected;

    /// <summary>
    /// Gets or sets the selected row.
    /// </summary>
    [ObservableProperty]
    public partial PublicKeyPolicyRow? SelectedRow { get; set; }

    /// <summary>
    /// Gets a value indicating whether the selected row has certificate details.
    /// </summary>
    public bool CanViewSelectedCertificate => SelectedRow?.CanViewDetails == true;

    /// <summary>
    /// Gets a value indicating whether a recovery agent certificate can be added to the selected node.
    /// </summary>
    public bool CanAddRecoveryAgent => IsRecoveryAgentNodeSelected;

    /// <summary>
    /// Gets a value indicating whether the selected recovery agent certificate can be deleted.
    /// </summary>
    public bool CanDeleteSelectedRecoveryAgent => IsRecoveryAgentNodeSelected
        && SelectedRow?.Kind == PublicKeyPolicyRowKind.Certificate
        && !string.IsNullOrWhiteSpace(SelectedRow.Source)
        && !string.IsNullOrWhiteSpace(SelectedRow.Thumbprint);

    /// <summary>
    /// Gets a value indicating whether the selected node has read-only properties.
    /// </summary>
    public bool CanViewSelectedNodeProperties => SelectedNode?.CanViewProperties == true;

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
    /// Gets or sets the current filter text.
    /// </summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

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
            PublicKeyPolicyNodeKind? selectedKind = SelectedNode?.Kind;
            IReadOnlyList<PublicKeyPolicyNode> nodes = await Task.Run(() => _policyService.LoadNodes());
            Nodes.Clear();
            foreach (PublicKeyPolicyNode node in nodes)
            {
                Nodes.Add(node);
            }

            SelectedNode = selectedKind is PublicKeyPolicyNodeKind kind
                ? Nodes.FirstOrDefault(node => node.Kind == kind) ?? Nodes.FirstOrDefault()
                : Nodes.FirstOrDefault();
            RefreshCurrentRows();
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

    private void RefreshCurrentRows()
    {
        SelectedRow = null;
        _allRows = SelectedNode?.Rows.ToList() ?? [];
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        CurrentRows.Clear();

        IEnumerable<PublicKeyPolicyRow> rows = string.IsNullOrWhiteSpace(FilterText)
            ? _allRows
            : _allRows.Where(row =>
                row.Name.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Setting.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.IssuedTo.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.IssuedBy.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.FriendlyName.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Status.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.CertificateTemplate.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Thumbprint.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase));

        foreach (PublicKeyPolicyRow row in rows)
        {
            CurrentRows.Add(row);
        }
    }

    partial void OnSelectedNodeChanged(PublicKeyPolicyNode? value)
    {
        OnPropertyChanged(nameof(IsRecoveryAgentNodeSelected));
        OnPropertyChanged(nameof(IsSettingNodeSelected));
        OnPropertyChanged(nameof(CanViewSelectedNodeProperties));
        OnPropertyChanged(nameof(CanAddRecoveryAgent));
        OnPropertyChanged(nameof(CanDeleteSelectedRecoveryAgent));
        RefreshCurrentRows();
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedRowChanged(PublicKeyPolicyRow? value)
    {
        OnPropertyChanged(nameof(CanViewSelectedCertificate));
        OnPropertyChanged(nameof(CanDeleteSelectedRecoveryAgent));
    }

    private static string GetString(string key, string resourceFileName)
    {
        string value = LocalizationProvider.Current.GetString(resourceFileName, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
