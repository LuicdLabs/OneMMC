using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views;

public sealed partial class NetworkNameDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public bool HasCustomName => NameRadioButton.IsChecked == true;

    public string? NetworkName => HasCustomName
        ? NetworkNameTextBox.Text.Trim()
        : null;

    public NetworkNameDialog()
    {
        InitializeComponent();
        UpdateState();
    }

    public void SetState(bool hasCustomName, string? networkName)
    {
        NameRadioButton.IsChecked = hasCustomName;
        NotConfiguredRadioButton.IsChecked = !hasCustomName;
        NetworkNameTextBox.Text = networkName ?? string.Empty;
        UpdateState();
    }

    private void SelectionRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateState();
    }

    private void NetworkNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateState();
    }

    private void UpdateState()
    {
        bool hasCustomName = NameRadioButton.IsChecked == true;
        NetworkNameTextBox.Visibility = hasCustomName ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = !hasCustomName || !string.IsNullOrWhiteSpace(NetworkNameTextBox.Text);
    }
}
