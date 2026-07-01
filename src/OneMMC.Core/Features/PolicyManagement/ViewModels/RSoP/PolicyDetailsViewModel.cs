using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;

namespace OneMMC.Core.Features.PolicyManagement.ViewModels.RSoP;

/// <summary>
/// ViewModel for the Policy Details Dialog.
/// </summary>
public partial class PolicyDetailsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StateLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StateValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceGpoLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceGpoValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupportedOnLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupportedOnValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSupportedOn { get; set; }

    [ObservableProperty]
    public partial string RegistryKeyLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegistryKeyValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegistryValueLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegistryValueValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRegistryValue { get; set; }

    [ObservableProperty]
    public partial string OptionsTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasOptions { get; set; }

    [ObservableProperty]
    public partial List<PolicyOptionItem> Options { get; set; } = new();

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDescription { get; set; }

    /// <summary>
    /// Initializes the ViewModel with policy data.
    /// </summary>
    public void Initialize(RSoPPolicyItem policy, Dictionary<string, object> options)
    {
        Title = policy.DisplayName;
        
        // General tab
        StateValue = policy.StateString;
        SourceGpoValue = policy.SourceGPO;
        CategoryValue = policy.CategoryPath;
        SupportedOnValue = policy.SupportedOn;
        HasSupportedOn = !string.IsNullOrEmpty(policy.SupportedOn);

        // Registry tab
        RegistryKeyValue = policy.RegistryKeyPath;
        RegistryValueValue = policy.RegistryValueName;
        HasRegistryValue = !string.IsNullOrEmpty(policy.RegistryValueName);

        // Options
        if (policy.State == PolicyState.Enabled && options.Count > 0)
        {
            HasOptions = true;
            Options = options.Select(kvp => new PolicyOptionItem
            {
                Name = kvp.Key,
                Value = kvp.Value?.ToString() ?? string.Empty
            }).ToList();
        }
        else
        {
            HasOptions = false;
            Options = new List<PolicyOptionItem>();
        }

        // Explain tab
        Description = policy.Description;
        HasDescription = !string.IsNullOrEmpty(policy.Description);
    }

    /// <summary>
    /// Sets the localized labels.
    /// </summary>
    public void SetLabels(
        string stateLabel,
        string sourceGpoLabel,
        string categoryLabel,
        string supportedOnLabel,
        string registryKeyLabel,
        string registryValueLabel,
        string optionsTitle)
    {
        StateLabel = stateLabel;
        SourceGpoLabel = sourceGpoLabel;
        CategoryLabel = categoryLabel;
        SupportedOnLabel = supportedOnLabel;
        RegistryKeyLabel = registryKeyLabel;
        RegistryValueLabel = registryValueLabel;
        OptionsTitle = optionsTitle;
    }
}

/// <summary>
/// Represents a policy option item for display.
/// </summary>
public class PolicyOptionItem
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}


