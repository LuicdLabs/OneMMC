using System;
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

namespace OneMMC.Views.Dialogs.WFProperties;

public sealed partial class DataProtectionSettingsDialog : UserControl
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ObservableCollection<DataIntegrityAlgorithmEntry> IntegrityAlgorithms { get; } =
    [
        new() { Protocol = "ESP", IntegrityAlgorithm = "SHA-256", MinutesLifetime = 60, KilobytesLifetime = 100000 },
        new() { Protocol = "AH", IntegrityAlgorithm = "SHA-256", MinutesLifetime = 60, KilobytesLifetime = 100000 },
        new() { Protocol = "ESP", IntegrityAlgorithm = "SHA-1", MinutesLifetime = 60, KilobytesLifetime = 100000 },
        new() { Protocol = "AH", IntegrityAlgorithm = "SHA-1", MinutesLifetime = 60, KilobytesLifetime = 100000 }
    ];

    public ObservableCollection<IntegrityEncryptionAlgorithmEntry> IntegrityEncryptionAlgorithms { get; } =
    [
        new()
        {
            Protocol = "ESP",
            IntegrityAlgorithm = "AES-GCM 256",
            EncryptionAlgorithm = "AES-GCM 256",
            MinutesLifetime = 60,
            KilobytesLifetime = 100000
        },
        new()
        {
            Protocol = "ESP",
            IntegrityAlgorithm = "SHA-256",
            EncryptionAlgorithm = "AES-CBC 256",
            MinutesLifetime = 60,
            KilobytesLifetime = 100000
        },
        new()
        {
            Protocol = "ESP",
            IntegrityAlgorithm = "SHA-1",
            EncryptionAlgorithm = "AES-CBC 128",
            MinutesLifetime = 60,
            KilobytesLifetime = 100000
        },
        new()
        {
            Protocol = "ESP",
            IntegrityAlgorithm = "SHA-1",
            EncryptionAlgorithm = "3DES",
            MinutesLifetime = 60,
            KilobytesLifetime = 100000
        }
    ];

    public DataProtectionSettingsDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += DataProtectionSettingsDialog_Unloaded;

        IntegrityAlgorithmsListView.ItemsSource = IntegrityAlgorithms;
        IntegrityEncryptionAlgorithmsListView.ItemsSource = IntegrityEncryptionAlgorithms;

        UpdateIntegritySelectionState();
        UpdateIntegrityEncryptionSelectionState();
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_CustomizeDataProtection_Title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 980,
            Height = 700
        });

        return modalWindow.ShowDialogAsync();
    }

    public bool RequireEncryption
    {
        get => RequireEncryptionCheckBox.IsChecked == true;
        set => RequireEncryptionCheckBox.IsChecked = value;
    }

    public void ApplyAlgorithms(
        IEnumerable<DataIntegrityAlgorithmEntry>? integrityAlgorithms,
        IEnumerable<IntegrityEncryptionAlgorithmEntry>? integrityEncryptionAlgorithms)
    {
        IntegrityAlgorithms.Clear();
        if (integrityAlgorithms is not null)
        {
            foreach (DataIntegrityAlgorithmEntry algorithm in integrityAlgorithms)
            {
                IntegrityAlgorithms.Add(new DataIntegrityAlgorithmEntry
                {
                    Protocol = algorithm.Protocol,
                    IntegrityAlgorithm = algorithm.IntegrityAlgorithm,
                    MinutesLifetime = algorithm.MinutesLifetime,
                    KilobytesLifetime = algorithm.KilobytesLifetime
                });
            }
        }

        IntegrityEncryptionAlgorithms.Clear();
        if (integrityEncryptionAlgorithms is not null)
        {
            foreach (IntegrityEncryptionAlgorithmEntry algorithm in integrityEncryptionAlgorithms)
            {
                IntegrityEncryptionAlgorithms.Add(new IntegrityEncryptionAlgorithmEntry
                {
                    Protocol = algorithm.Protocol,
                    IntegrityAlgorithm = algorithm.IntegrityAlgorithm,
                    EncryptionAlgorithm = algorithm.EncryptionAlgorithm,
                    MinutesLifetime = algorithm.MinutesLifetime,
                    KilobytesLifetime = algorithm.KilobytesLifetime
                });
            }
        }

        RequireEncryption = IntegrityAlgorithms.Count == 0;
    }

    public List<DataIntegrityAlgorithmEntry> GetIntegrityAlgorithms()
    {
        if (RequireEncryption)
        {
            return [];
        }

        return IntegrityAlgorithms
            .Select(algorithm => new DataIntegrityAlgorithmEntry
            {
                Protocol = algorithm.Protocol,
                IntegrityAlgorithm = algorithm.IntegrityAlgorithm,
                MinutesLifetime = algorithm.MinutesLifetime,
                KilobytesLifetime = algorithm.KilobytesLifetime
            })
            .ToList();
    }

    public List<IntegrityEncryptionAlgorithmEntry> GetIntegrityEncryptionAlgorithms()
    {
        return IntegrityEncryptionAlgorithms
            .Select(algorithm => new IntegrityEncryptionAlgorithmEntry
            {
                Protocol = algorithm.Protocol,
                IntegrityAlgorithm = algorithm.IntegrityAlgorithm,
                EncryptionAlgorithm = algorithm.EncryptionAlgorithm,
                MinutesLifetime = algorithm.MinutesLifetime,
                KilobytesLifetime = algorithm.KilobytesLifetime
            })
            .ToList();
    }

    private async void AddIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new IntegrityAlgorithmEditorDialog();
        WindowDialogResult result = await ShowIntegrityEditorAsync(LocalizedStrings.WF_DataProtection_AddIntegrityAlgorithmTitle, editor);
        if (result == WindowDialogResult.Primary && editor.Result is not null)
        {
            IntegrityAlgorithms.Add(editor.Result);
        }
    }

    private async void EditIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityAlgorithmsListView.SelectedItem is not DataIntegrityAlgorithmEntry selected)
        {
            return;
        }

        var editor = new IntegrityAlgorithmEditorDialog();
        editor.ApplyEntry(selected);

        WindowDialogResult result = await ShowIntegrityEditorAsync(LocalizedStrings.WF_DataProtection_EditIntegrityAlgorithmTitle, editor);
        if (result == WindowDialogResult.Primary && editor.Result is not null)
        {
            int index = IntegrityAlgorithms.IndexOf(selected);
            if (index >= 0)
            {
                IntegrityAlgorithms[index] = editor.Result;
            }
        }
    }

    private void RemoveIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityAlgorithmsListView.SelectedItem is DataIntegrityAlgorithmEntry selected)
        {
            IntegrityAlgorithms.Remove(selected);
        }
    }

    private async void AddIntegrityEncryptionButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new IntegrityEncryptionAlgorithmEditorDialog();
        WindowDialogResult result = await ShowIntegrityEncryptionEditorAsync(LocalizedStrings.WF_DataProtection_AddIntegrityEncryptionAlgorithmsTitle, editor);
        if (result == WindowDialogResult.Primary && editor.Result is not null)
        {
            IntegrityEncryptionAlgorithms.Add(editor.Result);
        }
    }

    private async void EditIntegrityEncryptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityEncryptionAlgorithmsListView.SelectedItem is not IntegrityEncryptionAlgorithmEntry selected)
        {
            return;
        }

        var editor = new IntegrityEncryptionAlgorithmEditorDialog();
        editor.ApplyEntry(selected);

        WindowDialogResult result = await ShowIntegrityEncryptionEditorAsync(LocalizedStrings.WF_DataProtection_EditIntegrityEncryptionAlgorithmsTitle, editor);
        if (result == WindowDialogResult.Primary && editor.Result is not null)
        {
            int index = IntegrityEncryptionAlgorithms.IndexOf(selected);
            if (index >= 0)
            {
                IntegrityEncryptionAlgorithms[index] = editor.Result;
            }
        }
    }

    private void RemoveIntegrityEncryptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityEncryptionAlgorithmsListView.SelectedItem is IntegrityEncryptionAlgorithmEntry selected)
        {
            IntegrityEncryptionAlgorithms.Remove(selected);
        }
    }

    private void MoveUpIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityAlgorithmsListView.SelectedItem is not DataIntegrityAlgorithmEntry selected)
        {
            return;
        }

        int index = IntegrityAlgorithms.IndexOf(selected);
        if (index > 0)
        {
            IntegrityAlgorithms.Move(index, index - 1);
            IntegrityAlgorithmsListView.SelectedItem = selected;
        }
    }

    private void MoveDownIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityAlgorithmsListView.SelectedItem is not DataIntegrityAlgorithmEntry selected)
        {
            return;
        }

        int index = IntegrityAlgorithms.IndexOf(selected);
        if (index >= 0 && index < IntegrityAlgorithms.Count - 1)
        {
            IntegrityAlgorithms.Move(index, index + 1);
            IntegrityAlgorithmsListView.SelectedItem = selected;
        }
    }

    private void MoveUpIntegrityEncryptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityEncryptionAlgorithmsListView.SelectedItem is not IntegrityEncryptionAlgorithmEntry selected)
        {
            return;
        }

        int index = IntegrityEncryptionAlgorithms.IndexOf(selected);
        if (index > 0)
        {
            IntegrityEncryptionAlgorithms.Move(index, index - 1);
            IntegrityEncryptionAlgorithmsListView.SelectedItem = selected;
        }
    }

    private void MoveDownIntegrityEncryptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IntegrityEncryptionAlgorithmsListView.SelectedItem is not IntegrityEncryptionAlgorithmEntry selected)
        {
            return;
        }

        int index = IntegrityEncryptionAlgorithms.IndexOf(selected);
        if (index >= 0 && index < IntegrityEncryptionAlgorithms.Count - 1)
        {
            IntegrityEncryptionAlgorithms.Move(index, index + 1);
            IntegrityEncryptionAlgorithmsListView.SelectedItem = selected;
        }
    }

    private void IntegrityAlgorithmsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateIntegritySelectionState();
    }

    private void IntegrityEncryptionAlgorithmsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateIntegrityEncryptionSelectionState();
    }

    private Task<WindowDialogResult> ShowIntegrityEditorAsync(string title, IntegrityAlgorithmEditorDialog editor)
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
            Width = 660,
            Height = 700,
            OnPrimaryButtonClick = () =>
            {
                editor.CommitResult();
                return editor.Result is not null;
            }
        });

        return modalWindow.ShowDialogAsync();
    }

    private Task<WindowDialogResult> ShowIntegrityEncryptionEditorAsync(string title, IntegrityEncryptionAlgorithmEditorDialog editor)
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
            Width = 720,
            Height = 700,
            OnPrimaryButtonClick = () =>
            {
                editor.CommitResult();
                return editor.Result is not null && editor.CanSave;
            }
        });

        return modalWindow.ShowDialogAsync();
    }

    private void UpdateIntegritySelectionState()
    {
        bool hasSelection = IntegrityAlgorithmsListView.SelectedItem is DataIntegrityAlgorithmEntry;
        EditIntegrityButton.IsEnabled = hasSelection;
        RemoveIntegrityButton.IsEnabled = hasSelection;
        MoveUpIntegrityButton.IsEnabled = hasSelection;
        MoveDownIntegrityButton.IsEnabled = hasSelection;
    }

    private void UpdateIntegrityEncryptionSelectionState()
    {
        bool hasSelection = IntegrityEncryptionAlgorithmsListView.SelectedItem is IntegrityEncryptionAlgorithmEntry;
        EditIntegrityEncryptionButton.IsEnabled = hasSelection;
        RemoveIntegrityEncryptionButton.IsEnabled = hasSelection;
        MoveUpIntegrityEncryptionButton.IsEnabled = hasSelection;
        MoveDownIntegrityEncryptionButton.IsEnabled = hasSelection;
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void DataProtectionSettingsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }
}
