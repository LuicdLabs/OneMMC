using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using OneMMC.Localization;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;

/// <summary>
/// Edits one exact-match legacy static IPsec filter without executing commands.
/// </summary>
public sealed partial class IPSecurityFilterEditorControl : UserControl
{
    private readonly string _filterListName;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Initializes an empty filter editor.
    /// </summary>
    /// <param name="filterListName">The owning filter-list name.</param>
    public IPSecurityFilterEditorControl(string filterListName)
        : this(
            new IPSecurityFilterCommandOptions
            {
                FilterListName = filterListName,
                SourceAddress = string.Empty,
                DestinationAddress = string.Empty,
                Protocol = "ANY",
                IsMirrored = true
            })
    {
    }

    /// <summary>
    /// Initializes a filter editor from a store definition.
    /// </summary>
    /// <param name="filter">The filter to edit.</param>
    public IPSecurityFilterEditorControl(IPSecurityFilterDefinition filter)
        : this(IPSecurityEditorValidation.ToFilterOptions(filter))
    {
    }

    /// <summary>
    /// Initializes a filter editor from command options.
    /// </summary>
    /// <param name="filter">The filter values to edit.</param>
    public IPSecurityFilterEditorControl(IPSecurityFilterCommandOptions filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filterListName = filter.FilterListName;

        InitializeComponent();
        DescriptionTextBox.Text = filter.Description ?? string.Empty;
        SourceAddressTextBox.Text = filter.SourceAddress;
        SourceMaskTextBox.Text = filter.SourceMask ?? string.Empty;
        DestinationAddressTextBox.Text = filter.DestinationAddress;
        DestinationMaskTextBox.Text = filter.DestinationMask ?? string.Empty;
        ProtocolComboBox.Text = filter.Protocol ?? "ANY";
        MirroredCheckBox.IsChecked = filter.IsMirrored ?? true;
        SourcePortNumberBox.Value = filter.SourcePort ?? 0;
        DestinationPortNumberBox.Value = filter.DestinationPort ?? 0;
    }

    /// <summary>
    /// Builds and validates command options for the current filter values.
    /// </summary>
    /// <param name="options">The validated options, or <see langword="null"/> when validation fails.</param>
    /// <returns><see langword="true"/> when the options are valid.</returns>
    public bool TryBuildResult(out IPSecurityFilterCommandOptions? options)
    {
        options = new IPSecurityFilterCommandOptions
        {
            FilterListName = _filterListName,
            SourceAddress = SourceAddressTextBox.Text,
            DestinationAddress = DestinationAddressTextBox.Text,
            Description = IPSecurityEditorValidation.OptionalText(DescriptionTextBox.Text),
            Protocol = IPSecurityEditorValidation.OptionalText(ProtocolComboBox.Text),
            IsMirrored = MirroredCheckBox.IsChecked == true,
            SourceMask = IPSecurityEditorValidation.OptionalText(SourceMaskTextBox.Text),
            DestinationMask = IPSecurityEditorValidation.OptionalText(DestinationMaskTextBox.Text),
            SourcePort = IPSecurityEditorValidation.GetOptionalPort(SourcePortNumberBox),
            DestinationPort = IPSecurityEditorValidation.GetOptionalPort(DestinationPortNumberBox)
        };

        IPSecurityFilterCommandOptions candidate = options;
        bool isValid = IPSecurityEditorValidation.TryValidate(
            () => _ = IPSecurityStaticPolicyCommandBuilder.BuildAddFilter(candidate),
            ValidationInfoBar,
            LocalizedStrings.IPSec_Editor_ValidationInvalid);
        if (!isValid)
        {
            options = null;
        }

        return isValid;
    }
}
