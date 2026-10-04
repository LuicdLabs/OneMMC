using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using OneMMC.Localization;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;

/// <summary>
/// Edits a legacy static IPsec policy without executing policy-store commands.
/// </summary>
public sealed partial class IPSecurityPolicyEditorControl : UserControl
{
    private const int DefaultQuickModeSessions = 1;
    private const int DefaultMainModeLifetimeMinutes = 480;
    private const int DefaultPollingIntervalMinutes = 180;

    private readonly IPSecurityEditorMode _mode;
    private readonly string _originalName;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Initializes a policy editor.
    /// </summary>
    /// <param name="mode">The editor mode.</param>
    /// <param name="policy">The policy to edit, or <see langword="null"/> when creating a policy.</param>
    public IPSecurityPolicyEditorControl(
        IPSecurityEditorMode mode,
        IPSecurityPolicyDefinition? policy = null)
    {
        if (mode == IPSecurityEditorMode.Edit)
        {
            ArgumentNullException.ThrowIfNull(policy);
        }

        _mode = mode;
        _originalName = policy?.Name ?? string.Empty;

        InitializeComponent();
        NameTextBox.Text = policy?.Name ?? string.Empty;
        DescriptionTextBox.Text = policy?.Description ?? string.Empty;
        AssignedToggleSwitch.IsOn = policy?.IsAssigned ?? false;
        DefaultResponseRuleToggleSwitch.IsOn = policy?.IsDefaultResponseRuleActive ?? false;
        MasterPfsToggleSwitch.IsOn = policy?.UseMasterPerfectForwardSecrecy ?? false;
        QuickModeSessionsNumberBox.Value = policy?.QuickModeSessionsPerMainMode > 0
            ? policy.QuickModeSessionsPerMainMode
            : DefaultQuickModeSessions;
        MainModeLifetimeNumberBox.Value = policy?.MainModeLifetimeMinutes > 0
            ? policy.MainModeLifetimeMinutes
            : DefaultMainModeLifetimeMinutes;
        PollingIntervalNumberBox.Value = policy?.PollingIntervalMinutes > 0
            ? policy.PollingIntervalMinutes
            : DefaultPollingIntervalMinutes;
        MainModeMethodsEditor.SetMethods(policy?.MainModeSecurityMethods ?? []);
        UpdateMasterPfsState();
    }

    /// <summary>
    /// Builds and validates command options for the current policy values.
    /// </summary>
    /// <param name="options">The validated options, or <see langword="null"/> when validation fails.</param>
    /// <returns><see langword="true"/> when the options are valid.</returns>
    public bool TryBuildResult(out IPSecurityPolicyCommandOptions? options)
    {
        string currentName = NameTextBox.Text;
        options = new IPSecurityPolicyCommandOptions
        {
            Name = _mode == IPSecurityEditorMode.Create ? currentName : _originalName,
            NewName = IPSecurityEditorValidation.RenamedValue(_mode, _originalName, currentName),
            Description = DescriptionTextBox.Text,
            UseMasterPerfectForwardSecrecy = MasterPfsToggleSwitch.IsOn,
            QuickModeSessionsPerMainMode = IPSecurityEditorValidation.GetInteger(QuickModeSessionsNumberBox),
            MainModeLifetimeMinutes = IPSecurityEditorValidation.GetInteger(MainModeLifetimeNumberBox),
            IsDefaultResponseRuleActive = DefaultResponseRuleToggleSwitch.IsOn,
            PollingIntervalMinutes = IPSecurityEditorValidation.GetInteger(PollingIntervalNumberBox),
            IsAssigned = AssignedToggleSwitch.IsOn,
            MainModeSecurityMethods = MainModeMethodsEditor.GetMethods()
        };

        IPSecurityPolicyCommandOptions candidate = options;
        bool isValid = IPSecurityEditorValidation.TryValidate(
            () =>
            {
                if (_mode == IPSecurityEditorMode.Create)
                {
                    _ = IPSecurityCommandBuilder.BuildAddPolicy(candidate);
                }
                else
                {
                    _ = IPSecurityCommandBuilder.BuildSetPolicy(candidate);
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

    private void MasterPfsToggleSwitch_Toggled(object sender, object e)
    {
        UpdateMasterPfsState();
    }

    private void UpdateMasterPfsState()
    {
        bool masterPfsEnabled = MasterPfsToggleSwitch.IsOn;
        if (masterPfsEnabled)
        {
            QuickModeSessionsNumberBox.Value = DefaultQuickModeSessions;
        }

        QuickModeSessionsNumberBox.IsEnabled = !masterPfsEnabled;
    }
}
