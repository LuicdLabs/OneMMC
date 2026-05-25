// ============================================================================
// StoreAdvancedPropertiesDialog.xaml.cs
// 
// Store Advanced Properties Dialog - For editing advanced store settings
// ============================================================================

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Core.Features.UserSecurity.Services.AzMan;
using ManagementTools.Localization;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Store Advanced Properties Dialog
/// </summary>
public sealed partial class StoreAdvancedPropertiesDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly AzManService _service;
    private readonly string _storePath;
    private bool _generateAudits;

    // Default values
    private const int DEFAULT_DOMAIN_TIMEOUT = 15000;
    private const int DEFAULT_SCRIPT_ENGINE_TIMEOUT = 45000;
    private const int DEFAULT_MAX_SCRIPT_ENGINES = 120;

    /// <summary>
    /// Result properties
    /// </summary>
    public StoreAdvancedProperties? Result { get; private set; }

    /// <summary>
    /// Create dialog
    /// </summary>
    public StoreAdvancedPropertiesDialog(AzManService service, string storePath, string storeName)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _service = service;
        _storePath = storePath;

        Title = string.Format(LocalizedStrings.StoreAdvancedPropertiesDialog_TitleWithName, storeName);

        // Load current values
        _ = LoadPropertiesAsync();
    }

    /// <summary>
    /// Load current properties
    /// </summary>
    private async Task LoadPropertiesAsync()
    {
        try
        {
            var props = await _service.GetStoreAdvancedPropertiesAsync(_storePath);

            DomainTimeoutNumberBox.Value = props.DomainTimeout ?? DEFAULT_DOMAIN_TIMEOUT;
            ScriptEngineTimeoutNumberBox.Value = props.ScriptEngineTimeout ?? DEFAULT_SCRIPT_ENGINE_TIMEOUT;
            MaxScriptEnginesNumberBox.Value = props.MaxScriptEngines ?? DEFAULT_MAX_SCRIPT_ENGINES;
            _generateAudits = props.GenerateAudits ?? false;
            TargetMachineTextBox.Text = string.IsNullOrEmpty(props.TargetMachine)
                ? LocalizedStrings.StoreAdvancedPropertiesDialog_TargetMachine_Local
                : props.TargetMachine;
        }
        catch (Exception ex)
        {
            ShowError(string.Format(LocalizedStrings.StoreAdvancedPropertiesDialog_Error_LoadFailed, ex.Message));
            
            // Set defaults
            DomainTimeoutNumberBox.Value = DEFAULT_DOMAIN_TIMEOUT;
            ScriptEngineTimeoutNumberBox.Value = DEFAULT_SCRIPT_ENGINE_TIMEOUT;
            MaxScriptEnginesNumberBox.Value = DEFAULT_MAX_SCRIPT_ENGINES;
            _generateAudits = false;
        }
    }

    /// <summary>
    /// Reset to defaults button click
    /// </summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        DomainTimeoutNumberBox.Value = DEFAULT_DOMAIN_TIMEOUT;
        ScriptEngineTimeoutNumberBox.Value = DEFAULT_SCRIPT_ENGINE_TIMEOUT;
        MaxScriptEnginesNumberBox.Value = DEFAULT_MAX_SCRIPT_ENGINES;
    }

    /// <summary>
    /// Primary button (Save) click
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Validate values
        if (double.IsNaN(DomainTimeoutNumberBox.Value) || DomainTimeoutNumberBox.Value < 1000)
        {
            ShowError(LocalizedStrings.StoreAdvancedPropertiesDialog_Error_DomainTimeout);
            args.Cancel = true;
            return;
        }

        if (double.IsNaN(ScriptEngineTimeoutNumberBox.Value) || ScriptEngineTimeoutNumberBox.Value < 1000)
        {
            ShowError(LocalizedStrings.StoreAdvancedPropertiesDialog_Error_ScriptTimeout);
            args.Cancel = true;
            return;
        }

        if (double.IsNaN(MaxScriptEnginesNumberBox.Value) || MaxScriptEnginesNumberBox.Value < 1)
        {
            ShowError(LocalizedStrings.StoreAdvancedPropertiesDialog_Error_MaxScriptEngines);
            args.Cancel = true;
            return;
        }

        Result = new StoreAdvancedProperties
        {
            DomainTimeout = (int)DomainTimeoutNumberBox.Value,
            ScriptEngineTimeout = (int)ScriptEngineTimeoutNumberBox.Value,
            MaxScriptEngines = (int)MaxScriptEnginesNumberBox.Value,
            GenerateAudits = _generateAudits
        };

        ErrorInfoBar.IsOpen = false;
    }

    /// <summary>
    /// Show error message
    /// </summary>
    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }
}
