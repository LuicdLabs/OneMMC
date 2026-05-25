using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.SystemAudit;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.SystemAudit;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views;

/// <summary>
/// Shows the property sheet for an advanced audit policy subcategory.
/// </summary>
public sealed partial class SystemAuditPropertiesDialog : ContentDialog
{
    private AuditSubcategoryValue? _workingSubcategory;

    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Gets the edited subcategory after the dialog is accepted.
    /// </summary>
    public AuditSubcategoryValue? EditedSubcategory { get; private set; }

    /// <summary>
    /// Gets whether the dialog was accepted with OK.
    /// </summary>
    public bool WasAccepted { get; private set; }

    /// <summary>
    /// Gets whether the selected item is one of the Global Object Access Auditing policy entries.
    /// </summary>
    public bool IsGlobalObjectAccessPolicy { get; private set; }

    public SystemAuditPropertiesDialog()
    {
        PrimaryButtonClick += SystemAuditPropertiesDialog_PrimaryButtonClick;
        CloseButtonClick += SystemAuditPropertiesDialog_CloseButtonClick;
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the dialog from the selected audit category and subcategory.
    /// </summary>
    public void SetSubcategory(AuditCategoryItem? category, AuditSubcategoryValue subcategory)
    {
        ArgumentNullException.ThrowIfNull(subcategory);

        _workingSubcategory = subcategory.Clone();
        IsGlobalObjectAccessPolicy = _workingSubcategory.IsGlobalObjectAccessPolicy ||
            category?.IsGlobalObjectAccessAuditing == true;

        Title = string.Format(LocalizedStrings.SecPol_SystemAudit_PropertiesTitleFormat, subcategory.DisplayName);
        PolicyNameText.Text = subcategory.DisplayName;
        ExplainText.Text = subcategory.HasExplainText ? subcategory.ExplainText : string.Empty;

        if (IsGlobalObjectAccessPolicy)
        {
            ConfigureGlobalObjectAccessPanel();
            return;
        }

        ConfigureAuditPolicyPanel();
    }

    private void ConfigureAuditPolicyPanel()
    {
        AuditPolicyPanel.Visibility = Visibility.Visible;
        GlobalObjectAccessPanel.Visibility = Visibility.Collapsed;

        ConfigureAuditEventsCheckBox.IsChecked = _workingSubcategory!.IsDefined;
        SuccessCheckBox.IsChecked = _workingSubcategory.Flags.HasFlag(AuditPolicyFlags.Success);
        FailureCheckBox.IsChecked = _workingSubcategory.Flags.HasFlag(AuditPolicyFlags.Failure);
        UpdateAuditEventsEnabledState();
    }

    private void ConfigureGlobalObjectAccessPanel()
    {
        AuditPolicyPanel.Visibility = Visibility.Collapsed;
        GlobalObjectAccessPanel.Visibility = Visibility.Visible;

        GlobalObjectAccessPromptText.Text = string.Format(
            LocalizedStrings.SecPol_SystemAudit_GlobalObjectPromptFormat,
            ToSentenceCase(_workingSubcategory!.DisplayName));
        DefineGlobalPolicyCheckBox.IsChecked = _workingSubcategory.IsDefined;
        ConfigureGlobalPolicyButton.IsEnabled = _workingSubcategory.IsDefined;
    }

    private void PropertiesSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem { Tag: string tag })
            return;

        bool showPolicy = tag == "Policy";
        PolicyScrollViewer.Visibility = showPolicy ? Visibility.Visible : Visibility.Collapsed;
        ExplainScrollViewer.Visibility = showPolicy ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConfigureAuditEventsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAuditEventsEnabledState();
    }

    private void DefineGlobalPolicyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = DefineGlobalPolicyCheckBox.IsChecked == true;
        ConfigureGlobalPolicyButton.IsEnabled = enabled;
        if (_workingSubcategory is not null)
        {
            _workingSubcategory.IsDefined = enabled;
        }
    }

    private async void ConfigureGlobalPolicyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingSubcategory is null)
            return;

        try
        {
            IntPtr ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            App.GetRequiredService<SystemAuditAclEditorService>()
                .EditGlobalObjectAccessPolicy(_workingSubcategory, ownerWindowHandle);
            DefineGlobalPolicyCheckBox.IsChecked = _workingSubcategory.IsDefined;
            ConfigureGlobalPolicyButton.IsEnabled = DefineGlobalPolicyCheckBox.IsChecked == true;
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(ex.Message);
        }
    }

    private void SystemAuditPropertiesDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_workingSubcategory is null)
            return;

        if (IsGlobalObjectAccessPolicy)
        {
            _workingSubcategory.IsDefined = DefineGlobalPolicyCheckBox.IsChecked == true;
            EditedSubcategory = _workingSubcategory.Clone();
        }
        else
        {
            _workingSubcategory.IsDefined = ConfigureAuditEventsCheckBox.IsChecked == true;

            AuditPolicyFlags flags = AuditPolicyFlags.None;
            if (_workingSubcategory.IsDefined)
            {
                if (SuccessCheckBox.IsChecked == true)
                {
                    flags |= AuditPolicyFlags.Success;
                }

                if (FailureCheckBox.IsChecked == true)
                {
                    flags |= AuditPolicyFlags.Failure;
                }
            }

            _workingSubcategory.Flags = flags;
            EditedSubcategory = _workingSubcategory.Clone();
        }

        WasAccepted = true;
    }

    private void SystemAuditPropertiesDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        WasAccepted = false;
        EditedSubcategory = null;
    }

    private void UpdateAuditEventsEnabledState()
    {
        bool enabled = ConfigureAuditEventsCheckBox.IsChecked == true;
        SuccessCheckBox.IsEnabled = enabled;
        FailureCheckBox.IsEnabled = enabled;
        AuditEventsPanel.Opacity = enabled ? 1.0 : 0.45;
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = RequestedTheme,
            Title = LocalizedStrings.Common_ErrorTitle,
            Content = message,
            CloseButtonText = LocalizedStrings.Common_OKButton,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };

        await dialog.ShowAsync();
    }

    private static string ToSentenceCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    /// <summary>
    /// Manually forwards pointer wheel events to the ScrollViewer so that mouse scroll works
    /// inside a ContentDialog, which otherwise swallows PointerWheelChanged events.
    /// </summary>
    private void ScrollViewer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        var delta = e.GetCurrentPoint(scrollViewer).Properties.MouseWheelDelta;
        // A standard wheel notch is 120 units; map to a reasonable pixel offset.
        double offset = scrollViewer.VerticalOffset - (delta / 120.0 * 48.0);
        scrollViewer.ChangeView(null, offset, null);
        e.Handled = true;
    }
}
