using Microsoft.Extensions.Logging;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.Services.SecPol;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.SecPol
{
    /// <summary>
    /// ViewModel for the Account Policies page (Password Policy, Account Lockout Policy).
    /// </summary>
    public sealed partial class AccountPoliciesViewModel : SecurityPoliciesViewModelBase
    {
        public AccountPoliciesViewModel(
            SecurityPolicyService policyService,
            ILogger<AccountPoliciesViewModel> logger,
            IAdminService adminService)
            : base(policyService, logger, adminService, nameof(AccountPoliciesViewModel))
        {
            var L = LocalizationProvider.Current;
            AddCategory(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_PasswordPolicy"), SecurityPolicyCategory.PasswordPolicy);
            AddCategory(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_AccountLockoutPolicy"), SecurityPolicyCategory.AccountLockoutPolicy);
            SelectFirstCategory();
        }
    }
}
