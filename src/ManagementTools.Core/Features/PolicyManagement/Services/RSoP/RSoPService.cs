using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagementTools.Core.Features.PolicyManagement.Models.GpEdit;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Manager;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Parsers;
using ManagementTools.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.PolicyManagement.Services.RSoP
{
    /// <summary>
    /// Service that reads the Resultant Set of Policy (RSoP) data by leveraging the existing
    /// AdmxBundle and IPolicyService infrastructure in read-only mode.
    /// Only policies that have been actively configured (Enabled or Disabled) are considered "applied".
    /// </summary>
        public sealed class RSoPService : IDisposable
    {
        private readonly ILogger<RSoPService> _logger;
        private AdmxBundle? _admxBundle;
        private IPolicyService? _computerPolicyService;
        private IPolicyService? _userPolicyService;
        private bool _isInitialized;
        private bool _disposed;

        /// <summary>
        /// Gets whether the service has been successfully initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        public RSoPService(ILogger<RSoPService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Initializes the RSoP service by loading ADMX files and creating read-only policy services.
        /// Should be called from a background thread.
        /// </summary>
        /// <returns>True if initialization succeeded.</returns>
        public bool Initialize()
        {
            if (_isInitialized) return true;

            try
            {
                _admxBundle = new AdmxBundle();
                var policyDefinitionsPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\PolicyDefinitions");
                _admxBundle.LoadFolder(policyDefinitionsPath, CultureInfo.CurrentCulture.Name);

                _computerPolicyService = PolicyServiceFactory.CreateMachinePolicyService(PolicyServiceFactory.PolicyMode.PolFile);
                _userPolicyService = PolicyServiceFactory.CreateUserPolicyService(PolicyServiceFactory.PolicyMode.PolFile);

                if (_computerPolicyService is null || _userPolicyService is null)
                {
                    _logger.LogWarning("Failed to initialize POL-based policy services for RSoP, falling back to auto mode");
                    _computerPolicyService?.Dispose();
                    _userPolicyService?.Dispose();
                    _computerPolicyService = PolicyServiceFactory.CreateMachinePolicyService();
                    _userPolicyService = PolicyServiceFactory.CreateUserPolicyService();
                }

                if (_computerPolicyService is null || _userPolicyService is null)
                {
                    _logger.LogError("Failed to initialize one or both policy services");
                    return false;
                }

                _isInitialized = true;
                _logger.LogInformation("RSoP service initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize RSoP service");
                return false;
            }
        }

        /// <summary>
        /// Gets the top-level ADMX categories for building the tree view.
        /// </summary>
        public IEnumerable<PolicyManagerCategory> GetTopLevelCategories()
        {
            if (_admxBundle is null) return Enumerable.Empty<PolicyManagerCategory>();
            return _admxBundle.Categories.Values;
        }

        /// <summary>
        /// Gets only configured (Enabled or Disabled) policies for a given category.
        /// NotConfigured policies are excluded from the results.
        /// </summary>
        /// <param name="category">The category to get policies for.</param>
        /// <param name="isComputer">True for computer configuration, false for user.</param>
        /// <returns>List of configured policy results only.</returns>
        public List<RSoPPolicyResult> GetPoliciesForCategory(PolicyManagerCategory category, bool isComputer)
        {
            if (_admxBundle is null) return new List<RSoPPolicyResult>();

            var service = isComputer ? _computerPolicyService : _userPolicyService;
            if (service is null) return new List<RSoPPolicyResult>();

            var section = isComputer ? AdmxPolicySection.Machine : AdmxPolicySection.User;
            var results = new List<RSoPPolicyResult>();

            foreach (var policy in category.Policies
                .Where(p => p.RawPolicy.Section == section || p.RawPolicy.Section == AdmxPolicySection.Both))
            {
                try
                {
                    var state = service.GetPolicyState(policy);
                    // Only include policies that have been actively configured
                    if (state is PolicyState.Enabled or PolicyState.Disabled)
                    {
                        results.Add(new RSoPPolicyResult(policy, state, isComputer));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read state for policy: {PolicyName}", policy.DisplayName);
                }
            }

            return results.OrderBy(r => r.DisplayName).ToList();
        }

        /// <summary>
        /// Checks whether a category (or any of its descendants) has at least one
        /// configured (Enabled/Disabled) policy in the given section.
        /// Used to determine whether the category should appear in the RSoP tree.
        /// </summary>
        public bool HasConfiguredPoliciesInSection(PolicyManagerCategory category, AdmxPolicySection section)
        {
            var service = section == AdmxPolicySection.Machine ? _computerPolicyService : _userPolicyService;
            if (service is null) return false;

            // Check direct policies in this category
            foreach (var policy in category.Policies
                .Where(p => p.RawPolicy.Section == section || p.RawPolicy.Section == AdmxPolicySection.Both))
            {
                try
                {
                    var state = service.GetPolicyState(policy);
                    if (state is PolicyState.Enabled or PolicyState.Disabled)
                        return true;
                }
                catch { /* skip unreadable policies */ }
            }

            // Check child categories recursively
            return category.Children.Any(c => HasConfiguredPoliciesInSection(c, section));
        }

        /// <summary>
        /// Gets all configured (Enabled or Disabled) policies across all categories.
        /// </summary>
        /// <param name="isComputer">True for computer configuration, false for user.</param>
        /// <returns>All actively configured policies.</returns>
        public List<RSoPPolicyResult> GetAllConfiguredPolicies(bool isComputer)
        {
            if (_admxBundle is null) return new List<RSoPPolicyResult>();

            var service = isComputer ? _computerPolicyService : _userPolicyService;
            if (service is null) return new List<RSoPPolicyResult>();

            var section = isComputer ? AdmxPolicySection.Machine : AdmxPolicySection.User;
            var results = new List<RSoPPolicyResult>();

            foreach (var policy in _admxBundle.Policies.Values
                .Where(p => p.RawPolicy.Section == section || p.RawPolicy.Section == AdmxPolicySection.Both))
            {
                try
                {
                    var state = service.GetPolicyState(policy);
                    if (state is PolicyState.Enabled or PolicyState.Disabled)
                    {
                        results.Add(new RSoPPolicyResult(policy, state, isComputer));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read state for policy: {PolicyName}", policy.DisplayName);
                }
            }

            return results.OrderBy(r => r.DisplayName).ToList();
        }

        /// <summary>
        /// Gets the option values for a given policy (the detailed configuration parameters).
        /// </summary>
        public Dictionary<string, object> GetPolicyOptions(PolicyManagerPolicy policy, bool isComputer)
        {
            var service = isComputer ? _computerPolicyService : _userPolicyService;
            if (service is null) return new Dictionary<string, object>();

            try
            {
                return service.GetPolicyOptions(policy);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read options for policy: {PolicyName}", policy.DisplayName);
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Builds the full category path string for a given category.
        /// </summary>
        public static string BuildCategoryPath(PolicyManagerCategory? category)
        {
            if (category is null) return string.Empty;

            var parts = new List<string>();
            var current = category;
            while (current is not null)
            {
                parts.Insert(0, current.DisplayName);
                current = current.Parent;
            }
            return string.Join(" \\ ", parts);
        }



        public void Dispose()
        {
            if (_disposed) return;

            _computerPolicyService?.Dispose();
            _userPolicyService?.Dispose();
            _computerPolicyService = null;
            _userPolicyService = null;
            _admxBundle = null;
            _isInitialized = false;
            _disposed = true;
        }
    }
}
