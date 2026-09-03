using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;

/// <summary>
/// Edits a legacy static IPsec filter action without executing commands.
/// </summary>
public sealed partial class IPSecurityFilterActionEditorControl : UserControl
{
    private readonly IPSecurityEditorMode _mode;
    private readonly string _originalName;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Initializes a filter-action editor.
    /// </summary>
    /// <param name="mode">The editor mode.</param>
    /// <param name="filterAction">The filter action to edit, or <see langword="null"/> when creating one.</param>
    public IPSecurityFilterActionEditorControl(
        IPSecurityEditorMode mode,
        IPSecurityFilterActionDefinition? filterAction = null)
    {
        if (mode == IPSecurityEditorMode.Edit)
        {
            ArgumentNullException.ThrowIfNull(filterAction);
        }

        _mode = mode;
        _originalName = filterAction?.Name ?? string.Empty;

        InitializeComponent();
        NameTextBox.Text = filterAction?.Name ?? string.Empty;
        DescriptionTextBox.Text = filterAction?.Description ?? string.Empty;
        ActionComboBox.SelectedIndex = filterAction?.Action switch
        {
            IPSecurityFilterActionKind.Block => 1,
            IPSecurityFilterActionKind.Negotiate => 2,
            _ => 0
        };
        QuickModePfsToggleSwitch.IsOn = filterAction?.UseQuickModePerfectForwardSecrecy ?? false;
        AcceptUnsecuredInboundToggleSwitch.IsOn = filterAction?.AcceptUnsecuredInbound ?? false;
        AllowUnsecuredFallbackToggleSwitch.IsOn = filterAction?.AllowUnsecuredFallback ?? false;
        QuickModeMethodsEditor.SetMethods(filterAction?.QuickModeSecurityMethods ?? []);
        UpdateNegotiationState();
    }

    /// <summary>
    /// Builds and validates command options for the current filter-action values.
    /// </summary>
    /// <param name="options">The validated options, or <see langword="null"/> when validation fails.</param>
    /// <returns><see langword="true"/> when the options are valid.</returns>
    public bool TryBuildResult(out IPSecurityFilterActionCommandOptions? options)
    {
        string currentName = NameTextBox.Text;
        IPSecurityFilterActionKind action = GetAction();
        bool isNegotiate = action == IPSecurityFilterActionKind.Negotiate;
        options = new IPSecurityFilterActionCommandOptions
        {
            Name = _mode == IPSecurityEditorMode.Create ? currentName : _originalName,
            NewName = IPSecurityEditorValidation.RenamedValue(_mode, _originalName, currentName),
            Description = DescriptionTextBox.Text,
            Action = action,
            UseQuickModePerfectForwardSecrecy = isNegotiate && QuickModePfsToggleSwitch.IsOn,
            AcceptUnsecuredInbound = isNegotiate && AcceptUnsecuredInboundToggleSwitch.IsOn,
            AllowUnsecuredFallback = isNegotiate && AllowUnsecuredFallbackToggleSwitch.IsOn,
            QuickModeSecurityMethods = isNegotiate
                ? QuickModeMethodsEditor.GetMethods()
                : null
        };

        IPSecurityFilterActionCommandOptions candidate = options;
        bool isValid = IPSecurityEditorValidation.TryValidate(
            () =>
            {
                if (_mode == IPSecurityEditorMode.Create)
                {
                    _ = IPSecurityStaticPolicyCommandBuilder.BuildAddFilterAction(candidate);
                }
                else
                {
                    _ = IPSecurityStaticPolicyCommandBuilder.BuildSetFilterAction(candidate);
                }
            },
            ValidationInfoBar,
            LocalizedStrings.IPSec_Editor_ValidationInvalid);
        if (!isValid)
        {
            options = null;
        }

        return isValid;
    }

    private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateNegotiationState();
    }

    private void UpdateNegotiationState()
    {
        bool enabled = GetAction() == IPSecurityFilterActionKind.Negotiate;
        NegotiationPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        QuickModeMethodsEditor.IsEnabled = enabled;
    }

    private IPSecurityFilterActionKind GetAction()
    {
        return ActionComboBox.SelectedIndex switch
        {
            1 => IPSecurityFilterActionKind.Block,
            2 => IPSecurityFilterActionKind.Negotiate,
            _ => IPSecurityFilterActionKind.Permit
        };
    }
}
