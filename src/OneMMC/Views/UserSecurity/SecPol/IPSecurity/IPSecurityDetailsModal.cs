using System.Globalization;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

/// <summary>
/// Displays the read-only details of an IP Security Policies row.
/// </summary>
/// <remarks>
/// Hosted in a <see cref="ContentDialog"/>: the view is read-only and opens nothing on top of
/// itself, so there is no reason to create a top-level window for it.
/// </remarks>
public sealed class IPSecurityDetailsModal
{
    private const int DialogWidth = 760;
    private const int DialogHeight = 560;
    private const double FieldSpacing = 12;

    private readonly ContentDialog _dialog;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityDetailsModal"/> class.
    /// </summary>
    /// <param name="row">The selected IP Security Policies row.</param>
    /// <param name="ownerXamlRoot">The XAML root that owns the dialog.</param>
    public IPSecurityDetailsModal(IPSecurityPolicyRow row, XamlRoot? ownerXamlRoot = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        LocalizedStrings localizedStrings = LocalizedStrings.Instance;
        _dialog = new ContentDialog
        {
            Title = string.Format(
                CultureInfo.CurrentCulture,
                localizedStrings.IPSec_Dialog_Details_TitleFormat,
                row.Name),
            Content = CreateContent(row, localizedStrings),
            XamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            CloseButtonText = localizedStrings.Common_CloseButton
        };
        _dialog.Resources["ContentDialogMaxWidth"] = (double)DialogWidth;
        _dialog.Resources["ContentDialogMaxHeight"] = (double)DialogHeight;
    }

    /// <summary>
    /// Shows the dialog and completes when it is dismissed.
    /// </summary>
    /// <returns>The dialog result.</returns>
    public async Task<ContentDialogResult> ShowAsync()
    {
        return await _dialog.ShowAsync();
    }

    private static UIElement CreateContent(IPSecurityPolicyRow row, LocalizedStrings localizedStrings)
    {
        StackPanel panel = new()
        {
            Spacing = FieldSpacing
        };

        panel.Children.Add(CreateReadOnlyField(localizedStrings.IPSec_Column_Name, row.Name));
        panel.Children.Add(CreateReadOnlyField(localizedStrings.IPSec_Column_Description, row.Description));

        foreach (IPSecurityPolicyDetailItem detail in row.Details)
        {
            if (IsBasicFieldDuplicate(detail, row, localizedStrings))
            {
                continue;
            }

            panel.Children.Add(CreateReadOnlyField(detail.Name, detail.Value));
        }

        return panel;
    }

    private static TextBox CreateReadOnlyField(string name, string value)
    {
        return new TextBox
        {
            AcceptsReturn = true,
            Header = name,
            IsReadOnly = true,
            Text = value,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static bool IsBasicFieldDuplicate(
        IPSecurityPolicyDetailItem detail,
        IPSecurityPolicyRow row,
        LocalizedStrings localizedStrings)
    {
        bool duplicatesName =
            string.Equals(detail.Name, localizedStrings.IPSec_Column_Name, StringComparison.CurrentCulture)
            && string.Equals(detail.Value, row.Name, StringComparison.Ordinal);
        bool duplicatesDescription =
            string.Equals(detail.Name, localizedStrings.IPSec_Column_Description, StringComparison.CurrentCulture)
            && string.Equals(detail.Value, row.Description, StringComparison.Ordinal);

        return duplicatesName || duplicatesDescription;
    }
}
