using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Network;

/// <summary>
/// Dialog for adding or editing a single IP address entry.
/// Set <see cref="ShowPredefined"/> to true when the context is Remote IP / Endpoint
/// (i.e. the "Predefined set of computers" option should be available).
/// </summary>
public sealed partial class IPAddressEntryDialog : UserControl
{
    private readonly Action<ElementTheme> _themeChangedHandler;

    public bool ShowPredefined { get; set; }

    public string? ResultValue { get; private set; }

    public IPAddressEntryDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += IPAddressEntryDialog_Loaded;
        Unloaded += IPAddressEntryDialog_Unloaded;
    }

    private void IPAddressEntryDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void IPAddressEntryDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private void RootPanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply ShowPredefined visibility now that controls are initialized
        var vis = ShowPredefined ? Visibility.Visible : Visibility.Collapsed;
        PredefinedRadio.Visibility = vis;
        PredefinedComboBox.Visibility = vis;

        // Set default selection after controls are ready
        SubnetRadio.IsChecked = true;
    }

    /// <summary>Pre-populate the dialog when editing an existing entry.</summary>
    public void SetExistingValue(string value)
    {
        // Defer until Loaded so controls are guaranteed initialized
        RootPanel.Loaded += (_, _) => ApplyExistingValue(value);
    }

    private void ApplyExistingValue(string value)
    {
        string[] predefined = [
            "Default gateway", "WINS servers", "DHCP servers", "DNS servers",
            "Local subnet", "Intranet", "Remote Corp Network", "Internet",
            "PlayTo Renderers", "Captive Portal Addresses"
        ];

        foreach (var item in predefined)
        {
            if (value.Equals(item, System.StringComparison.OrdinalIgnoreCase))
            {
                PredefinedRadio.IsChecked = true;
                for (int i = 0; i < PredefinedComboBox.Items.Count; i++)
                {
                    if (PredefinedComboBox.Items[i] is ComboBoxItem cbi &&
                        string.Equals(cbi.Tag?.ToString(), item, StringComparison.OrdinalIgnoreCase))
                    {
                        PredefinedComboBox.SelectedIndex = i;
                        break;
                    }
                }
                return;
            }
        }

        var dashIdx = value.IndexOf('-');
        if (dashIdx > 0)
        {
            RangeRadio.IsChecked = true;
            RangeFromTextBox.Text = value[..dashIdx].Trim();
            RangeToTextBox.Text = value[(dashIdx + 1)..].Trim();
            return;
        }

        SubnetRadio.IsChecked = true;
        SubnetTextBox.Text = value;
    }

    private void SubnetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (SubnetTextBox is null) return;
        SubnetTextBox.IsEnabled = true;
        RangeFromTextBox.IsEnabled = false;
        RangeToTextBox.IsEnabled = false;
        PredefinedComboBox.IsEnabled = false;
    }

    private void RangeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (RangeFromTextBox is null) return;
        SubnetTextBox.IsEnabled = false;
        RangeFromTextBox.IsEnabled = true;
        RangeToTextBox.IsEnabled = true;
        PredefinedComboBox.IsEnabled = false;
    }

    private void PredefinedRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (PredefinedComboBox is null) return;
        SubnetTextBox.IsEnabled = false;
        RangeFromTextBox.IsEnabled = false;
        RangeToTextBox.IsEnabled = false;
        PredefinedComboBox.IsEnabled = true;
        if (PredefinedComboBox.SelectedIndex < 0)
            PredefinedComboBox.SelectedIndex = 0;
    }

    public void CommitResult()
    {
        if (SubnetRadio.IsChecked == true)
            ResultValue = SubnetTextBox.Text.Trim();
        else if (RangeRadio.IsChecked == true)
            ResultValue = $"{RangeFromTextBox.Text.Trim()} - {RangeToTextBox.Text.Trim()}";
        else if (PredefinedRadio.IsChecked == true)
            ResultValue = (PredefinedComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }
}
