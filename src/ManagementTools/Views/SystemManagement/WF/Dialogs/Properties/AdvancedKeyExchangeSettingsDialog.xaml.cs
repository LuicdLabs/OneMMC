using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.Dialogs.WFProperties;

public sealed partial class AdvancedKeyExchangeSettingsDialog : UserControl
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ObservableCollection<SecurityMethodEntry> SecurityMethods { get; } =
    [
        new()
        {
            IntegrityAlgorithm = "SHA-256",
            EncryptionAlgorithm = "AES-CBC 256",
            KeyExchangeAlgorithm = "Elliptic Curve Diffie-Hellman P-256"
        },
        new()
        {
            IntegrityAlgorithm = "SHA-256",
            EncryptionAlgorithm = "AES-CBC 192",
            KeyExchangeAlgorithm = "Elliptic Curve Diffie-Hellman P-256"
        },
        new()
        {
            IntegrityAlgorithm = "SHA-1",
            EncryptionAlgorithm = "AES-CBC 128",
            KeyExchangeAlgorithm = "Diffie-Hellman Group 2"
        },
        new()
        {
            IntegrityAlgorithm = "SHA-1",
            EncryptionAlgorithm = "3DES",
            KeyExchangeAlgorithm = "Diffie-Hellman Group 2"
        }
    ];

    public AdvancedKeyExchangeSettingsDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += AdvancedKeyExchangeSettingsDialog_Unloaded;

        SecurityMethodsListView.ItemsSource = SecurityMethods;
        UpdateSelectionState();
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_CustomizeAdvancedKeyExchange_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 900,
            Height = 680
        });

        return modalWindow.ShowDialogAsync();
    }

    public void ApplySecurityMethods(IEnumerable<SecurityMethodEntry>? methods)
    {
        SecurityMethods.Clear();

        if (methods is null)
        {
            return;
        }

        foreach (SecurityMethodEntry method in methods)
        {
            SecurityMethods.Add(new SecurityMethodEntry
            {
                IntegrityAlgorithm = method.IntegrityAlgorithm,
                EncryptionAlgorithm = method.EncryptionAlgorithm,
                KeyExchangeAlgorithm = method.KeyExchangeAlgorithm
            });
        }
    }

    public void ApplyOptions(int lifetimeMinutes, int lifetimeSessions, bool forceDiffieHellman)
    {
        MinutesLifetimeNumberBox.Value = Math.Max(0, lifetimeMinutes);
        SessionsLifetimeNumberBox.Value = Math.Max(0, lifetimeSessions);
        UseDiffieHellmanCheckBox.IsChecked = forceDiffieHellman;
    }

    public int GetLifetimeMinutes()
        => (int)Math.Max(0, MinutesLifetimeNumberBox.Value);

    public int GetLifetimeSessions()
        => (int)Math.Max(0, SessionsLifetimeNumberBox.Value);

    public bool GetForceDiffieHellman()
        => UseDiffieHellmanCheckBox.IsChecked == true;

    public List<SecurityMethodEntry> GetSecurityMethods()
    {
        return SecurityMethods
            .Select(method => new SecurityMethodEntry
            {
                IntegrityAlgorithm = method.IntegrityAlgorithm,
                EncryptionAlgorithm = method.EncryptionAlgorithm,
                KeyExchangeAlgorithm = method.KeyExchangeAlgorithm
            })
            .ToList();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new SecurityMethodEditorDialog();

        WindowDialogResult result = await ShowSecurityMethodEditorAsync(LocalizedStrings.WF_AdvancedKeyExchange_AddSecurityMethodTitle, editor);
        if (result == WindowDialogResult.Primary && editor.Result is not null)
        {
            SecurityMethods.Add(editor.Result);
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecurityMethodsListView.SelectedItem is not SecurityMethodEntry selected)
        {
            return;
        }

        var editor = new SecurityMethodEditorDialog();
        editor.ApplyEntry(selected);

        WindowDialogResult result = await ShowSecurityMethodEditorAsync(LocalizedStrings.WF_AdvancedKeyExchange_EditSecurityMethodTitle, editor);
        if (result == WindowDialogResult.Primary && editor.Result is not null)
        {
            int index = SecurityMethods.IndexOf(selected);
            if (index >= 0)
            {
                SecurityMethods[index] = editor.Result;
            }
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecurityMethodsListView.SelectedItem is SecurityMethodEntry selected)
        {
            SecurityMethods.Remove(selected);
        }
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecurityMethodsListView.SelectedItem is not SecurityMethodEntry selected)
        {
            return;
        }

        int index = SecurityMethods.IndexOf(selected);
        if (index > 0)
        {
            SecurityMethods.Move(index, index - 1);
            SecurityMethodsListView.SelectedItem = selected;
        }
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecurityMethodsListView.SelectedItem is not SecurityMethodEntry selected)
        {
            return;
        }

        int index = SecurityMethods.IndexOf(selected);
        if (index >= 0 && index < SecurityMethods.Count - 1)
        {
            SecurityMethods.Move(index, index + 1);
            SecurityMethodsListView.SelectedItem = selected;
        }
    }

    private void SecurityMethodsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionState();
    }

    private Task<WindowDialogResult> ShowSecurityMethodEditorAsync(string title, SecurityMethodEditorDialog editor)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 560,
            Height = 620,
            OnPrimaryButtonClick = () =>
            {
                editor.CommitResult();
                return editor.Result is not null;
            }
        });

        return modalWindow.ShowDialogAsync();
    }

    private void UpdateSelectionState()
    {
        bool hasSelection = SecurityMethodsListView.SelectedItem is SecurityMethodEntry;
        EditButton.IsEnabled = hasSelection;
        RemoveButton.IsEnabled = hasSelection;
        MoveUpButton.IsEnabled = hasSelection;
        MoveDownButton.IsEnabled = hasSelection;
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void AdvancedKeyExchangeSettingsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }
}
