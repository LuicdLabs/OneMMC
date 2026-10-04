using System.Collections.ObjectModel;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;

/// <summary>
/// Edits legacy static IPsec security-method tokens in a structured form.
/// </summary>
public sealed partial class IPSecuritySecurityMethodsEditorControl : UserControl
{
    private const double DefaultQuickModeLifetimeKilobytes = 100000;
    private const double DefaultQuickModeLifetimeSeconds = 3600;

    private string _header = string.Empty;
    private IPSecuritySecurityMethodMode _methodMode;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>Gets the editable security-method token list.</summary>
    public ObservableCollection<string> Methods { get; } = [];

    /// <summary>Gets or sets the editor heading.</summary>
    public string Header
    {
        get => _header;
        set
        {
            _header = value ?? string.Empty;
            if (HeaderTextBlock is not null)
            {
                HeaderTextBlock.Text = _header;
            }
        }
    }

    /// <summary>Gets or sets the security-method syntax mode.</summary>
    public IPSecuritySecurityMethodMode MethodMode
    {
        get => _methodMode;
        set
        {
            _methodMode = value;
            if (MainModePanel is not null)
            {
                UpdateMode();
            }
        }
    }

    /// <summary>Initializes a new instance of the <see cref="IPSecuritySecurityMethodsEditorControl"/> class.</summary>
    public IPSecuritySecurityMethodsEditorControl()
    {
        InitializeComponent();
        InitializeOptions();
        LifetimeKilobytesNumberBox.Value = DefaultQuickModeLifetimeKilobytes;
        LifetimeSecondsNumberBox.Value = DefaultQuickModeLifetimeSeconds;
        UpdateMode();
        UpdateSelectionState();
        Methods.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    /// <summary>
    /// Shows or hides the list's empty state. Driven from code-behind so the token collection stays
    /// a plain <see cref="ObservableCollection{T}"/> with no wrapper view model.
    /// </summary>
    private void UpdateEmptyState()
    {
        EmptyMethodsText.Visibility = Methods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Replaces the editable token list.</summary>
    /// <param name="methods">The security-method tokens to show.</param>
    public void SetMethods(IEnumerable<string> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        Methods.Clear();
        foreach (string method in methods.Where(static method => !string.IsNullOrWhiteSpace(method)))
        {
            Methods.Add(method);
        }

        UpdateSelectionState();
    }

    /// <summary>Gets the current security-method tokens for netsh command construction.</summary>
    /// <returns>The ordered tokens, or <see langword="null"/> to let netsh keep its default.</returns>
    public IReadOnlyList<string>? GetMethods()
    {
        return Methods.Count == 0 ? null : Methods.ToList();
    }

    private void InitializeOptions()
    {
        MainEncryptionComboBox.ItemsSource = new List<MethodOption>
        {
            new(LocalizedStrings.IPSec_Editor_Algorithm3Des, "3DES"),
            new(LocalizedStrings.IPSec_Editor_AlgorithmDes, "DES")
        };
        MainHashComboBox.ItemsSource = CreateHashOptions();
        DiffieHellmanGroupComboBox.ItemsSource = new List<MethodOption>
        {
            new(LocalizedStrings.IPSec_Editor_DhGroupDh2048, "3"),
            new(LocalizedStrings.IPSec_Editor_DhGroupMedium, "2"),
            new(LocalizedStrings.IPSec_Editor_DhGroupLow, "1")
        };
        QuickMethodTypeComboBox.ItemsSource = new List<MethodOption>
        {
            new(LocalizedStrings.IPSec_Editor_MethodTypeEsp, "ESP"),
            new(LocalizedStrings.IPSec_Editor_MethodTypeAh, "AH"),
            new(LocalizedStrings.IPSec_Editor_MethodTypeAhEsp, "AHESP")
        };
        EspEncryptionComboBox.ItemsSource = new List<MethodOption>
        {
            new(LocalizedStrings.IPSec_Editor_Algorithm3Des, "3DES"),
            new(LocalizedStrings.IPSec_Editor_AlgorithmDes, "DES"),
            new(LocalizedStrings.IPSec_Editor_AlgorithmNone, "None")
        };
        EspAuthenticationComboBox.ItemsSource = new List<MethodOption>
        {
            new(LocalizedStrings.IPSec_Editor_AlgorithmSha1, "SHA1"),
            new(LocalizedStrings.IPSec_Editor_AlgorithmMd5, "MD5"),
            new(LocalizedStrings.IPSec_Editor_AlgorithmNone, "None")
        };
        AhHashComboBox.ItemsSource = CreateHashOptions();

        MainEncryptionComboBox.SelectedIndex = 0;
        MainHashComboBox.SelectedIndex = 0;
        DiffieHellmanGroupComboBox.SelectedIndex = 0;
        QuickMethodTypeComboBox.SelectedIndex = 0;
        EspEncryptionComboBox.SelectedIndex = 0;
        EspAuthenticationComboBox.SelectedIndex = 0;
        AhHashComboBox.SelectedIndex = 0;
    }

    private List<MethodOption> CreateHashOptions()
    {
        return
        [
            new(LocalizedStrings.IPSec_Editor_AlgorithmSha1, "SHA1"),
            new(LocalizedStrings.IPSec_Editor_AlgorithmMd5, "MD5")
        ];
    }

    private void UpdateMode()
    {
        HeaderTextBlock.Text = Header;
        MainModePanel.Visibility = MethodMode == IPSecuritySecurityMethodMode.MainMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        QuickModePanel.Visibility = MethodMode == IPSecuritySecurityMethodMode.QuickMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateQuickModeState();
    }

    private void MethodsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionState();
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedMethod(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedMethod(1);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        int selectedIndex = MethodsListView.SelectedIndex;
        if (selectedIndex < 0)
        {
            return;
        }

        Methods.RemoveAt(selectedIndex);
        MethodsListView.SelectedIndex = Math.Min(selectedIndex, Methods.Count - 1);
        UpdateSelectionState();
    }

    private void QuickMethodTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateQuickModeState();
    }

    private void AddMethodButton_Click(object sender, RoutedEventArgs e)
    {
        Methods.Add(MethodMode == IPSecuritySecurityMethodMode.MainMode
            ? BuildMainModeMethod()
            : BuildQuickModeMethod());
        MethodsListView.SelectedIndex = Methods.Count - 1;
        UpdateSelectionState();
    }

    private void MoveSelectedMethod(int delta)
    {
        int selectedIndex = MethodsListView.SelectedIndex;
        int targetIndex = selectedIndex + delta;
        if (selectedIndex < 0 || targetIndex < 0 || targetIndex >= Methods.Count)
        {
            return;
        }

        string method = Methods[selectedIndex];
        Methods.RemoveAt(selectedIndex);
        Methods.Insert(targetIndex, method);
        MethodsListView.SelectedIndex = targetIndex;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        int selectedIndex = MethodsListView?.SelectedIndex ?? -1;
        bool hasSelection = selectedIndex >= 0;
        MoveUpButton.IsEnabled = hasSelection && selectedIndex > 0;
        MoveDownButton.IsEnabled = hasSelection && selectedIndex < Methods.Count - 1;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void UpdateQuickModeState()
    {
        if (QuickMethodTypeComboBox is null)
        {
            return;
        }

        string methodType = GetSelectedValue(QuickMethodTypeComboBox);
        bool usesAh = methodType is "AH" or "AHESP";
        bool usesEsp = methodType is "ESP" or "AHESP";

        AhHashComboBox.IsEnabled = usesAh;
        EspEncryptionComboBox.IsEnabled = usesEsp;
        EspAuthenticationComboBox.IsEnabled = usesEsp;
    }

    private string BuildMainModeMethod()
    {
        return $"{GetSelectedValue(MainEncryptionComboBox)}-{GetSelectedValue(MainHashComboBox)}-{GetSelectedValue(DiffieHellmanGroupComboBox)}";
    }

    private string BuildQuickModeMethod()
    {
        string methodType = GetSelectedValue(QuickMethodTypeComboBox);
        string lifetime = $"{GetPositiveInteger(LifetimeKilobytesNumberBox)}k/{GetPositiveInteger(LifetimeSecondsNumberBox)}s";
        string ahMethod = $"AH[{GetSelectedValue(AhHashComboBox)}]";
        string espMethod = $"ESP[{GetSelectedValue(EspEncryptionComboBox)},{GetSelectedValue(EspAuthenticationComboBox)}]";

        return methodType switch
        {
            "AH" => $"{ahMethod}:{lifetime}",
            "AHESP" => $"{ahMethod}+{espMethod}:{lifetime}",
            _ => $"{espMethod}:{lifetime}"
        };
    }

    private static int GetPositiveInteger(NumberBox numberBox)
    {
        return double.IsNaN(numberBox.Value)
            ? 1
            : Math.Max(1, checked((int)numberBox.Value));
    }

    private static string GetSelectedValue(ComboBox comboBox)
    {
        return comboBox.SelectedItem is MethodOption option
            ? option.Value
            : string.Empty;
    }

    private sealed record MethodOption(string DisplayName, string Value)
    {
        public override string ToString()
        {
            return DisplayName;
        }
    }
}

/// <summary>
/// Determines which legacy netsh security-method syntax an editor instance should build.
/// </summary>
public enum IPSecuritySecurityMethodMode
{
    /// <summary>Main-mode IKE security methods.</summary>
    MainMode,

    /// <summary>Quick-mode IPsec security methods.</summary>
    QuickMode
}
