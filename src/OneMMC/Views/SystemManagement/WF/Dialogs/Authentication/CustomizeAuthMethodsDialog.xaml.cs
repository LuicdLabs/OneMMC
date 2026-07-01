using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Authentication;

public sealed partial class CustomizeAuthMethodsDialog : UserControl
{
    private readonly ObservableCollection<AuthMethodListItem> _firstMethods = [];
    private readonly ObservableCollection<AuthMethodListItem> _secondMethods = [];
    private readonly Action<ElementTheme> _themeChangedHandler;

    public bool IsFirstAuthOptional => FirstAuthOptionalCheckBox.IsChecked == true;
    public bool IsSecondAuthOptional => SecondAuthOptionalCheckBox.IsChecked == true;
    public IReadOnlyList<AuthMethodDialogResult> FirstMethods => BuildEffectiveMethods(_firstMethods, IsFirstAuthOptional);
    public IReadOnlyList<AuthMethodDialogResult> SecondMethods => BuildEffectiveMethods(_secondMethods, IsSecondAuthOptional);
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public CustomizeAuthMethodsDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += CustomizeAuthMethodsDialog_Loaded;
        Unloaded += CustomizeAuthMethodsDialog_Unloaded;

        FirstAuthListView.ItemsSource = _firstMethods;
        SecondAuthListView.ItemsSource = _secondMethods;
        _firstMethods.CollectionChanged += AuthMethods_CollectionChanged;
        _secondMethods.CollectionChanged += AuthMethods_CollectionChanged;
        FirstAuthListView.SelectionChanged += AuthListView_SelectionChanged;
        SecondAuthListView.SelectionChanged += AuthListView_SelectionChanged;
        FirstAuthOptionalCheckBox.Checked += OptionalCheckBox_Changed;
        FirstAuthOptionalCheckBox.Unchecked += OptionalCheckBox_Changed;
        SecondAuthOptionalCheckBox.Checked += OptionalCheckBox_Changed;
        SecondAuthOptionalCheckBox.Unchecked += OptionalCheckBox_Changed;
        UpdateControlStates();
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_CustomizeAdvancedAuthenticationMethods_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 980,
            Height = 700,
            OnPrimaryButtonClick = TryCommitSelections
        });

        return modalWindow.ShowDialogAsync();
    }

    public void ApplySelections(
        IReadOnlyList<AuthMethodDialogResult> firstMethods,
        IReadOnlyList<AuthMethodDialogResult> secondMethods,
        bool isFirstAuthOptional,
        bool isSecondAuthOptional)
    {
        _firstMethods.Clear();
        bool inferredFirstOptional = isFirstAuthOptional;
        foreach (AuthMethodDialogResult firstMethod in firstMethods)
        {
            if (IsAnonymousMethod(firstMethod))
            {
                inferredFirstOptional = true;
                continue;
            }

            _firstMethods.Add(ToListItem(firstMethod));
        }

        _secondMethods.Clear();
        bool inferredSecondOptional = isSecondAuthOptional;
        foreach (AuthMethodDialogResult secondMethod in secondMethods)
        {
            if (IsAnonymousMethod(secondMethod))
            {
                inferredSecondOptional = true;
                continue;
            }

            _secondMethods.Add(ToListItem(secondMethod));
        }

        FirstAuthOptionalCheckBox.IsChecked = inferredFirstOptional;
        SecondAuthOptionalCheckBox.IsChecked = inferredSecondOptional;
        ResetValidationMessage();
        UpdateControlStates();
    }

    public void ApplySelections(
        IReadOnlyList<AuthMethodListItem> firstMethods,
        IReadOnlyList<AuthMethodListItem> secondMethods,
        bool isFirstAuthOptional,
        bool isSecondAuthOptional)
    {
        ApplySelections(
            firstMethods.Select(item => item.Result).ToList(),
            secondMethods.Select(item => item.Result).ToList(),
            isFirstAuthOptional,
            isSecondAuthOptional);
    }

    private void CustomizeAuthMethodsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void CustomizeAuthMethodsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private static AuthMethodListItem ToListItem(AuthMethodDialogResult result)
    {
        return new AuthMethodListItem
        {
            Method = result.Method,
            Details = result.Details,
            Result = result
        };
    }

    private Task<WindowDialogResult> OpenSubDialogAsync(
        string title,
        UserControl subDialog,
        int width,
        int height,
        Func<bool>? onPrimaryButtonClick)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = subDialog,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = width,
            Height = height,
            OnPrimaryButtonClick = onPrimaryButtonClick
        });

        return modalWindow.ShowDialogAsync();
    }

    private async void FirstAddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddFirstAuthMethodDialog();
        WindowDialogResult result = await OpenSubDialogAsync(
            LocalizedStrings.WF_AddFirstAuthenticationMethod_Title,
            dialog,
            700,
            620,
            dialog.TryCommitResult);

        if (result == WindowDialogResult.Primary && dialog.Result is not null)
        {
            if (!TryApplyFirstMethodResult(dialog.Result))
            {
                return;
            }

            AuthMethodListItem item = ToListItem(dialog.Result);
            _firstMethods.Add(item);
            FirstAuthListView.SelectedItem = item;
        }
    }

    private async void FirstEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (FirstAuthListView.SelectedItem is not AuthMethodListItem selected)
        {
            return;
        }

        var dialog = new AddFirstAuthMethodDialog();
        dialog.ApplyResult(selected.Result);

        WindowDialogResult result = await OpenSubDialogAsync(
            LocalizedStrings.WF_EditFirstAuthenticationMethod_Title,
            dialog,
            700,
            620,
            dialog.TryCommitResult);

        if (result == WindowDialogResult.Primary && dialog.Result is not null)
        {
            if (!TryApplyFirstMethodResult(dialog.Result, selected.Result))
            {
                return;
            }

            int selectedIndex = _firstMethods.IndexOf(selected);
            if (selectedIndex >= 0)
            {
                AuthMethodListItem item = ToListItem(dialog.Result);
                _firstMethods[selectedIndex] = item;
                FirstAuthListView.SelectedItem = item;
            }
        }
    }

    private void FirstDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FirstAuthListView.SelectedItem is AuthMethodListItem selected)
        {
            _firstMethods.Remove(selected);
        }
    }

    private void FirstMoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (FirstAuthListView.SelectedItem is not AuthMethodListItem selected)
        {
            return;
        }

        int index = _firstMethods.IndexOf(selected);
        if (index <= 0)
        {
            return;
        }

        _firstMethods.Move(index, index - 1);
        FirstAuthListView.SelectedItem = selected;
    }

    private void FirstMoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (FirstAuthListView.SelectedItem is not AuthMethodListItem selected)
        {
            return;
        }

        int index = _firstMethods.IndexOf(selected);
        if (index < 0 || index >= _firstMethods.Count - 1)
        {
            return;
        }

        _firstMethods.Move(index, index + 1);
        FirstAuthListView.SelectedItem = selected;
    }

    private async void SecondAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContainsPresharedKey())
        {
            return;
        }

        var dialog = new AddSecondAuthMethodDialog();
        dialog.ApplyExistingMethods(_secondMethods.Select(item => item.Result));
        WindowDialogResult result = await OpenSubDialogAsync(
            LocalizedStrings.WF_AddSecondAuthenticationMethod_Title,
            dialog,
            720,
            620,
            dialog.TryCommitResult);

        if (result == WindowDialogResult.Primary && dialog.Result is not null)
        {
            AuthMethodListItem item = ToListItem(dialog.Result);
            _secondMethods.Add(item);
            SecondAuthListView.SelectedItem = item;
        }
    }

    private async void SecondEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContainsPresharedKey() ||
            SecondAuthListView.SelectedItem is not AuthMethodListItem selected)
        {
            return;
        }

        var dialog = new AddSecondAuthMethodDialog();
        dialog.ApplyExistingMethods(_secondMethods.Where(item => item != selected).Select(item => item.Result));
        dialog.ApplyResult(selected.Result);

        WindowDialogResult result = await OpenSubDialogAsync(
            LocalizedStrings.WF_EditSecondAuthenticationMethod_Title,
            dialog,
            720,
            620,
            dialog.TryCommitResult);

        if (result == WindowDialogResult.Primary && dialog.Result is not null)
        {
            int selectedIndex = _secondMethods.IndexOf(selected);
            if (selectedIndex >= 0)
            {
                AuthMethodListItem item = ToListItem(dialog.Result);
                _secondMethods[selectedIndex] = item;
                SecondAuthListView.SelectedItem = item;
            }
        }
    }

    private void SecondDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondAuthListView.SelectedItem is AuthMethodListItem selected)
        {
            _secondMethods.Remove(selected);
        }
    }

    private void SecondMoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondAuthListView.SelectedItem is not AuthMethodListItem selected)
        {
            return;
        }

        int index = _secondMethods.IndexOf(selected);
        if (index <= 0)
        {
            return;
        }

        _secondMethods.Move(index, index - 1);
        SecondAuthListView.SelectedItem = selected;
    }

    private void SecondMoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondAuthListView.SelectedItem is not AuthMethodListItem selected)
        {
            return;
        }

        int index = _secondMethods.IndexOf(selected);
        if (index < 0 || index >= _secondMethods.Count - 1)
        {
            return;
        }

        _secondMethods.Move(index, index + 1);
        SecondAuthListView.SelectedItem = selected;
    }

    private IReadOnlyList<AuthMethodDialogResult> BuildEffectiveMethods(
        IEnumerable<AuthMethodListItem> visibleMethods,
        bool includeAnonymousOptionalMethod)
    {
        List<AuthMethodDialogResult> methods = visibleMethods
            .Select(item => item.Result)
            .ToList();

        if (includeAnonymousOptionalMethod)
        {
            methods.Add(CreateAnonymousMethod());
        }

        return methods;
    }

    private bool TryCommitSelections()
    {
        string? validationError = GetValidationErrorMessage();
        if (validationError is not null)
        {
            ShowValidationMessage(validationError);
            return false;
        }

        ResetValidationMessage();
        return true;
    }

    private string? GetValidationErrorMessage()
    {
        if (ContainsPresharedKey() && (_secondMethods.Count > 0 || IsSecondAuthOptional))
        {
            return LocalizedStrings.WF_Validation_PresharedKeyCannotUseSecondAuthentication;
        }

        bool hasUserSecondAuthentication = _secondMethods.Any(item => IsUserSecondAuthMethod(item.Result));
        bool hasComputerHealthSecondAuthentication = _secondMethods.Any(item => IsComputerHealthSecondAuthMethod(item.Result));
        if (hasUserSecondAuthentication && hasComputerHealthSecondAuthentication)
        {
            return LocalizedStrings.WF_Validation_SecondAuthenticationCategoryMismatch;
        }

        return null;
    }

    private bool TryApplyFirstMethodResult(AuthMethodDialogResult result, AuthMethodDialogResult? originalResult = null)
    {
        if (!IsPresharedKey(result))
        {
            return true;
        }

        if (_secondMethods.Count == 0 && !IsSecondAuthOptional)
        {
            return true;
        }

        if (originalResult is not null && IsPresharedKey(originalResult))
        {
            return true;
        }

        ShowValidationMessage(LocalizedStrings.WF_Validation_PresharedKeyCannotUseSecondAuthentication);
        UpdateControlStates();
        return false;
    }

    private void UpdateControlStates()
    {
        AuthMethodListItem? firstSelected = FirstAuthListView.SelectedItem as AuthMethodListItem;
        bool hasFirstSelection = firstSelected is not null;
        int firstSelectedIndex = firstSelected is not null ? _firstMethods.IndexOf(firstSelected) : -1;
        FirstEditButton.IsEnabled = hasFirstSelection;
        FirstDeleteButton.IsEnabled = hasFirstSelection;
        FirstMoveUpButton.IsEnabled = hasFirstSelection && firstSelectedIndex > 0;
        FirstMoveDownButton.IsEnabled = hasFirstSelection && firstSelectedIndex >= 0 && firstSelectedIndex < _firstMethods.Count - 1;

        bool firstHasPresharedKey = ContainsPresharedKey();
        AuthMethodListItem? secondSelected = SecondAuthListView.SelectedItem as AuthMethodListItem;
        bool hasSecondSelection = secondSelected is not null;
        int secondSelectedIndex = secondSelected is not null ? _secondMethods.IndexOf(secondSelected) : -1;
        bool hasSecondAuthenticationToClear = _secondMethods.Count > 0 || IsSecondAuthOptional;

        SecondAddButton.IsEnabled = !firstHasPresharedKey;
        SecondEditButton.IsEnabled = !firstHasPresharedKey && hasSecondSelection;
        SecondDeleteButton.IsEnabled = hasSecondSelection;
        SecondMoveUpButton.IsEnabled = !firstHasPresharedKey && hasSecondSelection && secondSelectedIndex > 0;
        SecondMoveDownButton.IsEnabled = !firstHasPresharedKey && hasSecondSelection && secondSelectedIndex >= 0 && secondSelectedIndex < _secondMethods.Count - 1;
        SecondAuthOptionalCheckBox.IsEnabled = !firstHasPresharedKey || hasSecondAuthenticationToClear;
        SecondAuthListView.IsEnabled = !firstHasPresharedKey || _secondMethods.Count > 0;
    }

    private void AuthMethods_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetValidationMessage();
        UpdateControlStates();
    }

    private void AuthListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateControlStates();
    }

    private void OptionalCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ResetValidationMessage();
        UpdateControlStates();
    }

    private void ShowValidationMessage(string message)
    {
        ValidationMessageTextBlock.Text = message;
        ValidationMessageTextBlock.Visibility = Visibility.Visible;
    }

    private void ResetValidationMessage()
    {
        ValidationMessageTextBlock.Text = string.Empty;
        ValidationMessageTextBlock.Visibility = Visibility.Collapsed;
    }

    private bool ContainsPresharedKey()
        => _firstMethods.Any(item => string.Equals(item.Result.Kind, "PresharedKey", StringComparison.Ordinal));

    private static bool IsPresharedKey(AuthMethodDialogResult method)
        => string.Equals(method.Kind, "PresharedKey", StringComparison.Ordinal);

    private static bool IsAnonymousMethod(AuthMethodDialogResult method)
        => string.Equals(method.Kind, "Anonymous", StringComparison.Ordinal);

    private static bool IsUserSecondAuthMethod(AuthMethodDialogResult method)
        => method.Kind is "UserKerberos" or "UserNtlm" or "UserCertificate";

    private static bool IsComputerHealthSecondAuthMethod(AuthMethodDialogResult method)
        => string.Equals(method.Kind, "ComputerHealthCertificate", StringComparison.Ordinal);

    private AuthMethodDialogResult CreateAnonymousMethod()
    {
        return new AuthMethodDialogResult
        {
            Kind = "Anonymous",
            Method = LocalizedStrings.WF_AuthMethod_Anonymous,
            Details = LocalizedStrings.WF_AuthDetails_AnonymousAuthentication
        };
    }
}
