using ManagementTools.Core.Features.PolicyManagement.ViewModels.RSoP;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using System.Collections.Generic;

namespace ManagementTools.Views;

public sealed partial class PolicyDetailsDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public PolicyDetailsViewModel ViewModel { get; }

    public PolicyDetailsDialog()
    {
        ViewModel = new PolicyDetailsViewModel();
        this.InitializeComponent();
        this.Opened += PolicyDetailsDialog_Opened;
    }

    private void PolicyDetailsDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Initialize SelectorBar and pages
        DetailSelectorBar.SelectionChanged -= DetailSelectorBar_SelectionChanged;
        DetailSelectorBar.SelectedItem = DetailSelectorBar.Items[0];
        GeneralPage.Visibility = Visibility.Visible;
        RegistryPage.Visibility = Visibility.Collapsed;
        ExplainPage.Visibility = Visibility.Collapsed;
        DetailSelectorBar.SelectionChanged += DetailSelectorBar_SelectionChanged;
    }

    /// <summary>
    /// Initializes the dialog with policy details.
    /// </summary>
    public void Initialize(RSoPPolicyItem policy, Dictionary<string, object> options)
    {
        // Set localized labels
        ViewModel.SetLabels(
            LocalizedStrings.RSoP_Detail_State,
            LocalizedStrings.RSoP_SourceGPO,
            LocalizedStrings.RSoP_Detail_Category,
            LocalizedStrings.RSoP_Detail_SupportedOn,
            LocalizedStrings.RSoP_Detail_RegistryKey,
            LocalizedStrings.RSoP_Detail_RegistryValue,
            LocalizedStrings.Policy_Options_Title
        );

        // Initialize ViewModel with data
        ViewModel.Initialize(policy, options);
    }

    private void DetailSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // Hide all pages
        GeneralPage.Visibility = Visibility.Collapsed;
        RegistryPage.Visibility = Visibility.Collapsed;
        ExplainPage.Visibility = Visibility.Collapsed;

        // Show selected page
        if (sender.SelectedItem is SelectorBarItem selectedItem && selectedItem.Tag is string tag)
        {
            switch (tag)
            {
                case "General":
                    GeneralPage.Visibility = Visibility.Visible;
                    break;
                case "Registry":
                    RegistryPage.Visibility = Visibility.Visible;
                    break;
                case "Explain":
                    ExplainPage.Visibility = Visibility.Visible;
                    break;
            }
        }
    }
}

