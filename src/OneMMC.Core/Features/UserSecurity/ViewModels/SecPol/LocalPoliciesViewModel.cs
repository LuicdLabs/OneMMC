using Microsoft.Extensions.Logging;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.Services.SecPol;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.SecPol
{
    /// <summary>
    /// ViewModel for the Local Policies page (Audit Policy, User Rights Assignment, Security Options).
    /// </summary>
    public sealed partial class LocalPoliciesViewModel : SecurityPoliciesViewModelBase
    {
        public LocalPoliciesViewModel(
            SecurityPolicyService policyService,
            ILogger<LocalPoliciesViewModel> logger,
            IAdminService adminService)
            : base(policyService, logger, adminService, nameof(LocalPoliciesViewModel))
        {
            var L = LocalizationProvider.Current;
            AddCategory(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_AuditPolicy"), SecurityPolicyCategory.AuditPolicy);
            AddCategory(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_UserRightsAssignment"), SecurityPolicyCategory.UserRightsAssignment);
            AddCategory(L.GetString(ResourceFileNames.SecPol, "SecPol_Category_SecurityOptions"), SecurityPolicyCategory.SecurityOptions);
            SelectFirstCategory();
        }
    }
}
