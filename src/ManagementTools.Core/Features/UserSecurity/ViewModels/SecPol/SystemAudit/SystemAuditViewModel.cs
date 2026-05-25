using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.SystemAudit;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.SystemAudit;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.SystemAudit;

/// <summary>
/// ViewModel for the Local Group Policy Object System Audit page.
/// </summary>
public sealed partial class SystemAuditViewModel : ObservableObject
{
    private readonly IAdminService _adminService;
    private readonly ILogger<SystemAuditViewModel> _logger;
    private readonly SystemAuditPolicyService _systemAuditPolicyService;
    private List<AuditSubcategoryValue> _allSubcategories = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemAuditViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <param name="adminService">The administrator permission service.</param>
    /// <param name="systemAuditPolicyService">The Local Group Policy Object audit policy service.</param>
    public SystemAuditViewModel(
        ILogger<SystemAuditViewModel> logger,
        IAdminService adminService,
        SystemAuditPolicyService systemAuditPolicyService)
    {
        _logger = logger;
        _adminService = adminService;
        _systemAuditPolicyService = systemAuditPolicyService;

        LoadCategories();
        if (Categories.Count > 0)
        {
            SelectedCategory = Categories[0];
        }
    }

    /// <summary>
    /// Raised when an operation requires administrative privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>
    /// Gets the available audit categories.
    /// </summary>
    public ObservableCollection<AuditCategoryItem> Categories { get; } = [];

    /// <summary>
    /// Gets the current category's filtered audit items.
    /// </summary>
    public ObservableCollection<AuditSubcategoryValue> CurrentSubcategories { get; } = [];

    /// <summary>
    /// Gets or sets the selected audit category.
    /// </summary>
    [ObservableProperty]
    public partial AuditCategoryItem? SelectedCategory { get; set; }

    /// <summary>
    /// Gets or sets the selected audit item.
    /// </summary>
    [ObservableProperty]
    public partial AuditSubcategoryValue? SelectedSubcategory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the page is loading.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets or sets the error message shown by the page.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an error is currently displayed.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the filter text for subcategory search.
    /// </summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(AuditCategoryItem? value)
    {
        if (value is not null)
        {
            LoadSubcategoriesCommand.Execute(null);
        }
    }

    /// <summary>
    /// Reloads the current category.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync() => await LoadSubcategoriesAsync();

    /// <summary>
    /// Loads audit items for the selected category.
    /// </summary>
    [RelayCommand]
    private async Task LoadSubcategoriesAsync()
    {
        if (SelectedCategory is null)
            return;

        Guid? selectedGuid = SelectedSubcategory?.SubcategoryGuid;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        SelectedSubcategory = null;

        try
        {
            IReadOnlyList<AuditSubcategoryValue> items = await Task.Run(() =>
                _systemAuditPolicyService.GetSubcategories(SelectedCategory));
            _allSubcategories = items.ToList();
            ApplyFilter();

            if (selectedGuid.HasValue)
            {
                SelectedSubcategory = CurrentSubcategories.FirstOrDefault(item => item.SubcategoryGuid == selectedGuid.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SystemAuditViewModel] Failed to load subcategories for {Category}", SelectedCategory.DisplayName);
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Saves an edited audit item.
    /// </summary>
    /// <param name="subcategory">The updated audit item.</param>
    /// <returns><see langword="true"/> if the save succeeded; otherwise <see langword="false"/>.</returns>
    public async Task<bool> SaveSubcategoryAsync(AuditSubcategoryValue subcategory)
    {
        ArgumentNullException.ThrowIfNull(subcategory);

        if (!_adminService.IsRunningAsAdmin)
        {
            AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            return false;
        }

        try
        {
            await Task.Run(() => _systemAuditPolicyService.SaveSubcategory(subcategory));
            await LoadSubcategoriesAsync();
            SelectedSubcategory = CurrentSubcategories.FirstOrDefault(item => item.SubcategoryGuid == subcategory.SubcategoryGuid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SystemAuditViewModel] Failed to save {Subcategory}", subcategory.DisplayName);

            if (_adminService.IsPermissionError(ex))
            {
                ErrorMessage = LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = ex.Message;
            }

            HasError = true;
            return false;
        }
    }

    private void LoadCategories()
    {
        Categories.Clear();

        try
        {
            foreach (AuditCategoryItem category in _systemAuditPolicyService.GetCategories())
            {
                Categories.Add(category);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SystemAuditViewModel] Failed to load audit categories");
            HasError = true;
            ErrorMessage = ex.Message;
        }
    }

    private void ApplyFilter()
    {
        CurrentSubcategories.Clear();

        IEnumerable<AuditSubcategoryValue> items = string.IsNullOrWhiteSpace(FilterText)
            ? _allSubcategories
            : _allSubcategories.Where(item =>
                !string.IsNullOrWhiteSpace(item.DisplayName) &&
                item.DisplayName.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase));

        foreach (AuditSubcategoryValue item in items)
        {
            CurrentSubcategories.Add(item);
        }
    }
}
