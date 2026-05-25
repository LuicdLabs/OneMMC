using System;
using System.IO;
using System.Threading.Tasks;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

public class AuthorizationRuleDialogResult
{
    public string BizRule { get; set; } = string.Empty;
    public string BizRuleLanguage { get; set; } = "VBScript";
    public string ScriptPath { get; set; } = string.Empty;
    public bool ReloadRuleIntoStore { get; set; }
    public bool ClearRuleFromStore { get; set; }
}

public sealed partial class AuthorizationRuleDialog : UserControl
{
    private string _bizRule;
    private bool _reloadRuleIntoStore;
    private bool _clearRuleFromStore;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    private AuthorizationRuleDialog(string bizRule, string bizRuleLanguage, string scriptPath, bool reloadRuleIntoStore, bool clearRuleFromStore)
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;

        _bizRule = bizRule ?? string.Empty;

        ScriptPathTextBox.Text = scriptPath ?? string.Empty;
        ScriptSourceCodeTextBox.Text = _bizRule;

        if (string.IsNullOrWhiteSpace(_bizRule) && !string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath))
        {
            try
            {
                _bizRule = File.ReadAllText(scriptPath);
                ScriptSourceCodeTextBox.Text = _bizRule;
            }
            catch { }
        }

        if (string.Equals(bizRuleLanguage, "JScript", StringComparison.OrdinalIgnoreCase))
            JScriptRadioButton.IsChecked = true;
        else
            VBScriptRadioButton.IsChecked = true;

        _reloadRuleIntoStore = reloadRuleIntoStore;
        _clearRuleFromStore = clearRuleFromStore;
    }

    /// <summary>
    /// Shows the dialog as a modal window and returns the result.
    /// </summary>
    public static async Task<AuthorizationRuleDialogResult?> ShowDialogAsync(
        XamlRoot ownerXamlRoot,
        string bizRule,
        string bizRuleLanguage,
        string scriptPath,
        bool reloadRuleIntoStore,
        bool clearRuleFromStore)
    {
        var dialog = new AuthorizationRuleDialog(bizRule, bizRuleLanguage, scriptPath, reloadRuleIntoStore, clearRuleFromStore);

        AuthorizationRuleDialogResult? result = null;
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.Instance.AuthorizationRuleDialog_Title,
            Content = dialog,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Instance.Common_OKButton,
            CloseButtonText = LocalizedStrings.Instance.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 640,
            Height = 560,
            OnPrimaryButtonClick = () =>
            {
                result = new AuthorizationRuleDialogResult
                {
                    BizRule = dialog._bizRule,
                    BizRuleLanguage = dialog.JScriptRadioButton.IsChecked == true ? "JScript" : "VBScript",
                    ScriptPath = dialog.ScriptPathTextBox.Text.Trim(),
                    ReloadRuleIntoStore = dialog._reloadRuleIntoStore,
                    ClearRuleFromStore = dialog._clearRuleFromStore
                };
                return true;
            }
        });

        var windowResult = await modalWindow.ShowDialogAsync();
        return windowResult == WindowDialogResult.Primary ? result : null;
    }

    private async void OnBrowseScriptPathClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string filter = $"{_localizedStrings.AuthorizationRuleDialog_ScriptFiles_Filter}\0*.vbs;*.js;*.txt\0VBScript Files\0*.vbs\0JScript Files\0*.js\0All Files\0*.*\0";
        var path = await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(hwnd, filter, _localizedStrings.AuthorizationRuleDialog_SelectScriptFile_Title);
        if (string.IsNullOrWhiteSpace(path))
            return;

        ScriptPathTextBox.Text = path;

        try
        {
            _bizRule = File.ReadAllText(path);
            ScriptSourceCodeTextBox.Text = _bizRule;
        }
        catch { }
    }

    private void OnReloadRuleClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ScriptPathTextBox.Text))
            return;

        _reloadRuleIntoStore = true;
        _clearRuleFromStore = false;
    }

    private void OnClearRuleClick(object sender, RoutedEventArgs e)
    {
        _clearRuleFromStore = true;
        _reloadRuleIntoStore = false;
        _bizRule = string.Empty;
        ScriptSourceCodeTextBox.Text = string.Empty;
        ScriptPathTextBox.Text = string.Empty;
    }
}