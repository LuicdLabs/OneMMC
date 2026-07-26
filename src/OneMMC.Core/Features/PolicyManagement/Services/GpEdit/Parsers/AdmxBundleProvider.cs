using System;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Parsers;

/// <summary>
/// Supplies a single shared <see cref="AdmxBundle"/> for the whole process.
/// </summary>
/// <remarks>
/// Parsing <c>%SYSTEMROOT%\PolicyDefinitions</c> yields roughly 250-300 ADMX/ADML file pairs, thousands
/// of policies, and every localized string for them — tens of megabytes of long-lived dictionaries.
/// Both the Group Policy editor and the RSoP service used to build their own bundle, so visiting either
/// page allocated a fresh copy and visiting both held two at once.
/// <para>
/// The definitions are read-only operating-system data, so one shared instance is enough. Callers must
/// treat the returned bundle as immutable and must never dispose or clear it; call
/// <see cref="Invalidate"/> if the on-disk definitions are known to have changed.
/// </para>
/// </remarks>
public sealed partial class AdmxBundleProvider
{
    private readonly ILogger<AdmxBundleProvider> _logger;
    private readonly object _gate = new();

    private AdmxBundle? _bundle;
    private string? _loadedCulture;

    public AdmxBundleProvider(ILogger<AdmxBundleProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the shared bundle, parsing the policy definitions on first use.
    /// </summary>
    /// <param name="cultureName">
    /// Culture whose ADML strings should be loaded. Defaults to the current culture when omitted. A
    /// request for a culture other than the one already loaded reloads the bundle.
    /// </param>
    /// <returns>The shared, fully loaded <see cref="AdmxBundle"/>.</returns>
    public AdmxBundle GetOrLoad(string? cultureName = null)
    {
        string culture = cultureName ?? CultureInfo.CurrentCulture.Name;

        lock (_gate)
        {
            if (_bundle is not null && string.Equals(_loadedCulture, culture, StringComparison.OrdinalIgnoreCase))
            {
                return _bundle;
            }

            string policyDefinitionsPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\PolicyDefinitions");
            var bundle = new AdmxBundle();
            bundle.LoadFolder(policyDefinitionsPath, culture);

            _bundle = bundle;
            _loadedCulture = culture;

            _logger.LogInformation(
                "Loaded shared ADMX bundle for culture {Culture}: {PolicyCount} policies, {CategoryCount} categories.",
                culture,
                bundle.Policies.Count,
                bundle.FlatCategories.Count);

            return bundle;
        }
    }

    /// <summary>
    /// Drops the cached bundle so the next <see cref="GetOrLoad"/> re-reads the policy definitions.
    /// </summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _bundle = null;
            _loadedCulture = null;
        }
    }
}
