using System.Collections.ObjectModel;
using System.Globalization;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using OneMMC.Core.Localization;
using OneMMC.Localization;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

/// <summary>
/// Read-only viewer for Software Restriction Policies rules whose values are decoded from
/// policy storage (for example certificate rules), showing every decoded detail field.
/// </summary>
public sealed partial class SoftwareRestrictionRuleDetailsDialog : ContentDialog
{
    /// <summary>Gets the localized strings accessor used by compiled bindings.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>Gets the detail rows displayed by the dialog.</summary>
    public ObservableCollection<SoftwareRestrictionDetailItem> Details { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareRestrictionRuleDetailsDialog"/> class.
    /// </summary>
    /// <param name="rule">The rule whose decoded details are shown.</param>
    public SoftwareRestrictionRuleDetailsDialog(SoftwareRestrictionRule rule)
    {
        foreach (SoftwareRestrictionDetailItem detail in rule.Details)
        {
            Details.Add(detail);
        }

        InitializeComponent();
        Title = string.Format(
            CultureInfo.CurrentCulture,
            GetString(SoftwareRestrictionKeys.DialogRuleDetailsTitleFormat),
            rule.DisplayValue);
        RuleValueText.Text = rule.DisplayValue;
    }

    private static string GetString(string key)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
