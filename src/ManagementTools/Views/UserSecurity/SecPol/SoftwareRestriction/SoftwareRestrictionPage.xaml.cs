using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ManagementTools.Core.Abstractions.Services;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.SoftwareRestriction;
using ManagementTools.Core.Localization;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;

namespace ManagementTools.Views;

public sealed partial class SoftwareRestrictionPage : Page
{
    private const string AllFilesFilterPattern = "*.*";
    private const string PathRuleFilePickerSettingsIdentifier = "SoftwareRestrictionPathRuleFilePicker";
    private const string PathRuleFolderPickerSettingsIdentifier = "SoftwareRestrictionPathRuleFolderPicker";
    private const string HashFilePickerSettingsIdentifier = "SoftwareRestrictionHashRuleFilePicker";
    private const string CertificateRuleFilePickerSettingsIdentifier = "SoftwareRestrictionCertificateRuleFilePicker";

    private readonly IFileDialogService _fileDialogService;
    private readonly ILogger<SoftwareRestrictionPage> _logger;
    private bool _hasLoaded;

    internal readonly LocalizedStrings LocalizedStrings = LocalizedStrings.Instance;

    public SoftwareRestrictionViewModel ViewModel { get; }

    public SoftwareRestrictionPage()
    {
        _logger = App.GetRequiredService<ILogger<SoftwareRestrictionPage>>();
        _fileDialogService = App.GetRequiredService<IFileDialogService>();
        ViewModel = App.GetRequiredService<SoftwareRestrictionViewModel>();

        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        await ViewModel.LoadPolicyAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        DataContext = null;
        Loaded -= OnPageLoaded;
        Unloaded -= OnPageUnloaded;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private async void CreatePolicyButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CreateDefaultPolicyAsync();
    }

    private async void DeletePolicyButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ShowPrimaryModalAsync(
            LocalizedStrings.SRP_Dialog_DeletePolicy_Title,
            CreateMessageContent(LocalizedStrings.SRP_Dialog_DeletePolicy_Message),
            LocalizedStrings.Common_DeleteButton,
            height: 240) == WindowDialogResult.Primary)
        {
            await ViewModel.DeletePolicyAsync();
        }
    }

    private async void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem?.Rule is not SoftwareRestrictionRule rule)
        {
            return;
        }

        if (await ShowPrimaryModalAsync(
            LocalizedStrings.SRP_Dialog_DeleteRule_Title,
            CreateMessageContent(LocalizedStrings.SRP_Dialog_DeleteRule_Message),
            LocalizedStrings.Common_DeleteButton,
            height: 240) == WindowDialogResult.Primary)
        {
            await ViewModel.DeleteRuleAsync(rule);
        }
    }

    private async void SetDefaultSecurityLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem?.SecurityLevel is SoftwareRestrictionSecurityLevel securityLevel)
        {
            await ShowSetDefaultSecurityLevelDialogAsync(securityLevel);
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        await EditCurrentSelectionAsync();
    }

    private async void PolicyListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await EditCurrentSelectionAsync();
    }

    private void PolicyTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is SoftwareRestrictionSectionItem section)
        {
            SelectSection(section);
        }
    }

    private void PolicyTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is SoftwareRestrictionSectionItem section)
        {
            SelectSection(section);
        }
    }

    private void SelectSection(SoftwareRestrictionSectionItem section)
    {
        if (ReferenceEquals(ViewModel.SelectedSection, section))
        {
            return;
        }

        ViewModel.SelectedSection = section;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.FilterText = sender.Text;
        }
    }

    private async void NewPathRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.Path, null);
    }

    private async void NewHashRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.Hash, null);
    }

    private async void NewCertificateRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.Certificate, null);
    }

    private async void NewNetworkZoneRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.NetworkZone, null);
    }

    private async Task EditCurrentSelectionAsync()
    {
        if (ViewModel.SelectedSection is null || !ViewModel.IsConfigured)
        {
            return;
        }

        if (ViewModel.SelectedSection.Kind == SoftwareRestrictionSectionKind.AdditionalRules)
        {
            if (ViewModel.SelectedItem?.Rule is not SoftwareRestrictionRule rule)
            {
                return;
            }

            if (rule.Kind is SoftwareRestrictionRuleKind.Path or SoftwareRestrictionRuleKind.Hash or SoftwareRestrictionRuleKind.NetworkZone)
            {
                await ShowRuleDialogAsync(rule.Kind, rule);
                return;
            }

            if (rule.CanViewDetails)
            {
                await ShowRuleDetailsAsync(rule);
                return;
            }

            await ShowMessageAsync(SoftwareRestrictionKeys.RuleUnsupportedForEdit);
            return;
        }

        switch (ViewModel.SelectedSection.Kind)
        {
            case SoftwareRestrictionSectionKind.SecurityLevels:
                if (ViewModel.SelectedItem?.SecurityLevel is SoftwareRestrictionSecurityLevel securityLevel)
                {
                    await ShowSetDefaultSecurityLevelDialogAsync(securityLevel);
                }

                break;
            case SoftwareRestrictionSectionKind.Enforcement:
                await ShowEnforcementDialogAsync();
                break;
            case SoftwareRestrictionSectionKind.DesignatedFileTypes:
                await ShowDesignatedFileTypesDialogAsync();
                break;
            case SoftwareRestrictionSectionKind.TrustedPublishers:
                await ShowTrustedPublishersDialogAsync();
                break;
        }
    }

    private async Task ShowSetDefaultSecurityLevelDialogAsync(SoftwareRestrictionSecurityLevel securityLevel)
    {
        if (securityLevel == ViewModel.PolicyState.Enforcement.DefaultSecurityLevel)
        {
            return;
        }

        string securityLevelName = SoftwareRestrictionRule.FormatSecurityLevel(securityLevel);
        if (await ShowPrimaryModalAsync(
            GetString(SoftwareRestrictionKeys.DialogSetDefaultSecurityLevelTitle),
            CreateMessageContent(string.Format(
                CultureInfo.CurrentCulture,
                GetString(SoftwareRestrictionKeys.DialogSetDefaultSecurityLevelMessageFormat),
                securityLevelName)),
            LocalizedStrings.SRP_Command_SetAsDefault,
            height: 260) == WindowDialogResult.Primary)
        {
            await ViewModel.SaveDefaultSecurityLevelAsync(securityLevel);
        }
    }

    private async Task ShowEnforcementDialogAsync()
    {
        SoftwareRestrictionEnforcementSettings settings = ViewModel.PolicyState.Enforcement;
        ComboBox defaultLevelBox = CreateComboBox(
            CreateSecurityLevelOptions(),
            settings.DefaultSecurityLevel);
        ComboBox userScopeBox = CreateComboBox(
            CreateUserScopeOptions(),
            settings.UserScope);
        ComboBox fileScopeBox = CreateComboBox(
            CreateFileScopeOptions(),
            settings.FileScope);
        CheckBox certificateRulesBox = new()
        {
            Content = GetString(SoftwareRestrictionKeys.EnforcementCertificateRules),
            IsChecked = settings.CertificateRulesEnabled
        };

        StackPanel panel = CreateDialogPanel();
        AddLabeledControl(panel, LocalizedStrings.SRP_Field_SecurityLevel, defaultLevelBox);
        AddLabeledControl(panel, GetString(SoftwareRestrictionKeys.EnforcementUserScope), userScopeBox);
        AddLabeledControl(panel, GetString(SoftwareRestrictionKeys.EnforcementFileScope), fileScopeBox);
        panel.Children.Add(certificateRulesBox);

        if (await ShowPrimaryModalAsync(
            LocalizedStrings.SRP_Dialog_EditEnforcement_Title,
            panel,
            LocalizedStrings.Common_SaveButton,
            height: 440) == WindowDialogResult.Primary)
        {
            await ViewModel.SaveEnforcementAsync(new SoftwareRestrictionEnforcementSettings
            {
                DefaultSecurityLevel = GetSelectedValue(defaultLevelBox, settings.DefaultSecurityLevel),
                UserScope = GetSelectedValue(userScopeBox, settings.UserScope),
                FileScope = GetSelectedValue(fileScopeBox, settings.FileScope),
                CertificateRulesEnabled = certificateRulesBox.IsChecked == true
            });
        }
    }

    private async Task ShowDesignatedFileTypesDialogAsync()
    {
        ObservableCollection<DesignatedFileTypeItem> fileTypes = new(
            ViewModel.PolicyState.DesignatedFileTypes.Select(CreateDesignatedFileTypeItem));
        ListView fileTypesList = new()
        {
            Height = 240,
            ItemTemplate = (DataTemplate)Resources["DesignatedFileTypeItemTemplate"],
            ItemsSource = fileTypes,
            SelectionMode = ListViewSelectionMode.Single
        };
        TextBox extensionBox = new()
        {
            MinWidth = 120,
            MaxLength = 32,
            TextWrapping = TextWrapping.NoWrap
        };
        Button addButton = new()
        {
            Content = LocalizedStrings.Common_AddButton,
            IsEnabled = false
        };
        Button removeButton = new()
        {
            Content = LocalizedStrings.Common_RemoveButton,
            IsEnabled = false
        };

        extensionBox.TextChanged += (_, _) =>
        {
            string extension = NormalizeFileExtension(extensionBox.Text);
            addButton.IsEnabled = !string.IsNullOrWhiteSpace(extension)
                && !ContainsFileExtension(fileTypes, extension);
        };
        fileTypesList.SelectionChanged += (_, _) =>
        {
            removeButton.IsEnabled = fileTypesList.SelectedItem is DesignatedFileTypeItem;
        };
        addButton.Click += (_, _) =>
        {
            string extension = NormalizeFileExtension(extensionBox.Text);
            if (string.IsNullOrWhiteSpace(extension) || ContainsFileExtension(fileTypes, extension))
            {
                return;
            }

            AddFileType(fileTypes, CreateDesignatedFileTypeItem(extension));
            extensionBox.Text = string.Empty;
            extensionBox.Focus(FocusState.Programmatic);
        };
        removeButton.Click += (_, _) =>
        {
            if (fileTypesList.SelectedItem is DesignatedFileTypeItem selected)
            {
                fileTypes.Remove(selected);
                string extension = NormalizeFileExtension(extensionBox.Text);
                addButton.IsEnabled = !string.IsNullOrWhiteSpace(extension)
                    && !ContainsFileExtension(fileTypes, extension);
            }
        };

        Grid headers = new()
        {
            ColumnSpacing = 16
        };
        headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headers.Children.Add(new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = LocalizedStrings.SRP_ListHeader_Extension
        });
        TextBlock fileTypeHeader = new()
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = LocalizedStrings.SRP_ListHeader_FileType
        };
        Grid.SetColumn(fileTypeHeader, 1);
        headers.Children.Add(fileTypeHeader);

        Grid fileTypesArea = new()
        {
            ColumnSpacing = 12,
            MinWidth = 520
        };
        fileTypesArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fileTypesArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel listPanel = new()
        {
            Spacing = 4
        };
        listPanel.Children.Add(headers);
        listPanel.Children.Add(fileTypesList);
        fileTypesArea.Children.Add(listPanel);

        removeButton.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(removeButton, 1);
        fileTypesArea.Children.Add(removeButton);

        StackPanel inputPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        inputPanel.Children.Add(extensionBox);
        inputPanel.Children.Add(addButton);

        StackPanel panel = CreateDialogPanel();
        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.SRP_Help_DesignatedFileTypes_AddRemove,
            TextWrapping = TextWrapping.Wrap
        });
        AddLabeledControl(panel, LocalizedStrings.SRP_Field_DesignatedFileTypes, fileTypesArea);
        AddLabeledControl(panel, LocalizedStrings.SRP_Field_FileExtension, inputPanel);

        if (await ShowPrimaryModalAsync(
            LocalizedStrings.SRP_Dialog_EditFileTypes_Title,
            panel,
            LocalizedStrings.Common_SaveButton,
            width: 720,
            height: 620) == WindowDialogResult.Primary)
        {
            await ViewModel.SaveDesignatedFileTypesAsync(fileTypes.Select(static item => item.Extension));
        }
    }

    private async Task ShowTrustedPublishersDialogAsync()
    {
        SoftwareRestrictionTrustedPublisherSettings settings = ViewModel.PolicyState.TrustedPublishers;
        CheckBox definePolicySettingsBox = new()
        {
            Content = LocalizedStrings.SRP_TrustedPublishers_DefineSettings,
            IsChecked = settings.IsDefined
        };
        RadioButton endUsersRadio = CreatePublisherScopeRadioButton(
            LocalizedStrings.SRP_PublisherScope_EndUsers_Option,
            settings.PublisherScope == SoftwareRestrictionPublisherScope.EndUsers);
        RadioButton localAdministratorsRadio = CreatePublisherScopeRadioButton(
            LocalizedStrings.SRP_PublisherScope_LocalAdministrators_Option,
            settings.PublisherScope == SoftwareRestrictionPublisherScope.LocalAdministrators);
        RadioButton enterpriseAdministratorsRadio = CreatePublisherScopeRadioButton(
            LocalizedStrings.SRP_PublisherScope_EnterpriseAdministrators_Option,
            settings.PublisherScope == SoftwareRestrictionPublisherScope.EnterpriseAdministrators);
        enterpriseAdministratorsRadio.IsEnabled = false;
        CheckBox publisherRevocationBox = new()
        {
            Content = LocalizedStrings.SRP_TrustedPublishers_PublisherRevocation_Option,
            IsChecked = settings.CheckPublisherRevocation
        };
        CheckBox timestampRevocationBox = new()
        {
            Content = LocalizedStrings.SRP_TrustedPublishers_TimestampRevocation_Option,
            IsChecked = settings.CheckTimestampRevocation
        };

        void SetPolicyControlsEnabled()
        {
            bool isDefined = definePolicySettingsBox.IsChecked == true;
            endUsersRadio.IsEnabled = isDefined;
            localAdministratorsRadio.IsEnabled = isDefined;
            enterpriseAdministratorsRadio.IsEnabled = false;
            publisherRevocationBox.IsEnabled = isDefined;
            timestampRevocationBox.IsEnabled = isDefined;
        }

        definePolicySettingsBox.Checked += (_, _) => SetPolicyControlsEnabled();
        definePolicySettingsBox.Unchecked += (_, _) => SetPolicyControlsEnabled();
        SetPolicyControlsEnabled();

        StackPanel panel = CreateDialogPanel();
        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.SRP_Help_TrustedPublishers,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(definePolicySettingsBox);
        panel.Children.Add(CreateDialogGroup(
            LocalizedStrings.SRP_TrustedPublishers_Management,
            endUsersRadio,
            localAdministratorsRadio,
            enterpriseAdministratorsRadio));
        panel.Children.Add(CreateDialogGroup(
            LocalizedStrings.SRP_TrustedPublishers_AdditionalChecks,
            publisherRevocationBox,
            timestampRevocationBox));

        if (await ShowPrimaryModalAsync(
            LocalizedStrings.SRP_Dialog_EditTrustedPublishers_Title,
            panel,
            LocalizedStrings.Common_SaveButton,
            height: 560) == WindowDialogResult.Primary)
        {
            await ViewModel.SaveTrustedPublishersAsync(new SoftwareRestrictionTrustedPublisherSettings
            {
                IsDefined = definePolicySettingsBox.IsChecked == true,
                PublisherScope = GetSelectedPublisherScope(
                    endUsersRadio,
                    localAdministratorsRadio,
                    enterpriseAdministratorsRadio),
                CheckPublisherRevocation = publisherRevocationBox.IsChecked == true,
                CheckTimestampRevocation = timestampRevocationBox.IsChecked == true
            });
        }
    }

    private async Task ShowRuleDetailsAsync(SoftwareRestrictionRule rule)
    {
        string title = string.Format(
            CultureInfo.CurrentCulture,
            GetString(SoftwareRestrictionKeys.DialogRuleDetailsTitleFormat),
            rule.DisplayValue);

        _ = await ShowCloseModalAsync(
            title,
            CreateRuleDetailsContent(rule),
            width: 720,
            height: 560);
    }

    private static StackPanel CreateRuleDetailsContent(SoftwareRestrictionRule rule)
    {
        StackPanel panel = CreateDialogPanel();

        panel.Children.Add(new TextBlock
        {
            Text = rule.DisplayValue,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        foreach (SoftwareRestrictionDetailItem detail in rule.Details)
        {
            AddReadOnlyDetail(panel, detail);
        }

        return panel;
    }

    private static void AddReadOnlyDetail(Panel panel, SoftwareRestrictionDetailItem detail)
    {
        TextBox valueBox = new()
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            MinWidth = 420,
            Text = detail.Value,
            TextWrapping = TextWrapping.Wrap
        };

        AddLabeledControl(panel, detail.Name, valueBox);
    }

    private async Task ShowRuleDialogAsync(SoftwareRestrictionRuleKind kind, SoftwareRestrictionRule? existingRule)
    {
        SoftwareRestrictionRule rule = existingRule is null
            ? new SoftwareRestrictionRule { Kind = kind }
            : new SoftwareRestrictionRule
            {
                Id = existingRule.Id,
                Kind = existingRule.Kind,
                SecurityLevel = existingRule.SecurityLevel,
                Value = existingRule.Value,
                Description = existingRule.Description,
                StoragePath = existingRule.StoragePath
            };

        ComboBox securityLevelBox = CreateComboBox(CreateSecurityLevelOptions(), rule.SecurityLevel);
        TextBox descriptionBox = new()
        {
            MinWidth = 420,
            Text = rule.Description,
            TextWrapping = TextWrapping.Wrap
        };

        FrameworkElement ruleValueControl;
        TextBox? ruleValueTextBox = null;
        ComboBox? zoneBox = null;
        if (kind == SoftwareRestrictionRuleKind.NetworkZone)
        {
            zoneBox = CreateComboBox(
                CreateNetworkZoneOptions(),
                string.IsNullOrWhiteSpace(rule.Value) ? "3" : rule.Value);
            TextBlock zoneDescriptionText = new()
            {
                Text = GetSelectedDescription<string>(zoneBox),
                TextWrapping = TextWrapping.Wrap
            };
            zoneBox.SelectionChanged += (_, _) => zoneDescriptionText.Text = GetSelectedDescription<string>(zoneBox);

            StackPanel zonePanel = new()
            {
                Spacing = 8
            };
            zonePanel.Children.Add(zoneBox);
            zonePanel.Children.Add(zoneDescriptionText);
            ruleValueControl = zonePanel;
        }
        else
        {
            ruleValueTextBox = new TextBox
            {
                MinWidth = 420,
                Text = existingRule is null ? string.Empty : rule.Value,
                TextWrapping = TextWrapping.NoWrap,
                IsReadOnly = existingRule is not null && kind == SoftwareRestrictionRuleKind.Hash
            };

            ruleValueControl = kind switch
            {
                SoftwareRestrictionRuleKind.Hash => CreateHashRuleFilePicker(ruleValueTextBox),
                SoftwareRestrictionRuleKind.Certificate => CreateCertificateRuleFilePicker(ruleValueTextBox),
                SoftwareRestrictionRuleKind.Path => CreatePathRulePicker(ruleValueTextBox),
                _ => ruleValueTextBox
            };
        }

        StackPanel panel = CreateDialogPanel();
        string valueLabel = kind switch
        {
            SoftwareRestrictionRuleKind.Hash => LocalizedStrings.SRP_Field_HashFile,
            SoftwareRestrictionRuleKind.Certificate => LocalizedStrings.SRP_Field_CertificateFile,
            SoftwareRestrictionRuleKind.NetworkZone => LocalizedStrings.SRP_Field_NetworkZone,
            _ => LocalizedStrings.SRP_Field_Path
        };
        AddLabeledControl(panel, valueLabel, ruleValueControl);
        AddLabeledControl(panel, LocalizedStrings.SRP_Field_SecurityLevel, securityLevelBox);
        AddLabeledControl(panel, LocalizedStrings.SRP_Field_Description, descriptionBox);

        if (await ShowPrimaryModalAsync(
            LocalizedStrings.SRP_Dialog_EditRule_Title,
            panel,
            LocalizedStrings.Common_SaveButton,
            width: 680,
            height: 520) == WindowDialogResult.Primary)
        {
            rule.SecurityLevel = GetSelectedValue(securityLevelBox, rule.SecurityLevel);
            rule.Description = descriptionBox.Text;
            rule.Value = zoneBox is not null
                ? GetSelectedValue(zoneBox, "3")
                : ruleValueTextBox?.Text ?? string.Empty;

            await ViewModel.SaveRuleAsync(rule);
        }
    }

    private StackPanel CreateHashRuleFilePicker(TextBox filePathBox)
    {
        Button browseButton = new()
        {
            Content = LocalizedStrings.Common_BrowseButton
        };
        browseButton.Click += async (_, _) => await BrowseForHashFileAsync(filePathBox);

        StackPanel panel = new()
        {
            Spacing = 8
        };
        panel.Children.Add(filePathBox);
        panel.Children.Add(browseButton);
        return panel;
    }

    private StackPanel CreateCertificateRuleFilePicker(TextBox certificatePathBox)
    {
        Button browseButton = new()
        {
            Content = LocalizedStrings.Common_BrowseButton
        };
        browseButton.Click += async (_, _) => await BrowseForCertificateRuleFileAsync(certificatePathBox);

        StackPanel panel = new()
        {
            Spacing = 8
        };
        panel.Children.Add(certificatePathBox);
        panel.Children.Add(browseButton);
        return panel;
    }

    private StackPanel CreatePathRulePicker(TextBox pathBox)
    {
        Button browseFileButton = new()
        {
            Content = LocalizedStrings.SRP_Browse_File
        };
        browseFileButton.Click += async (_, _) => await BrowseForPathRuleFileAsync(pathBox);

        Button browseFolderButton = new()
        {
            Content = LocalizedStrings.SRP_Browse_Folder
        };
        browseFolderButton.Click += async (_, _) => await BrowseForPathRuleFolderAsync(pathBox);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        buttons.Children.Add(browseFileButton);
        buttons.Children.Add(browseFolderButton);

        StackPanel panel = new()
        {
            Spacing = 8
        };
        panel.Children.Add(pathBox);
        panel.Children.Add(buttons);
        return panel;
    }

    private async Task BrowseForPathRuleFileAsync(TextBox pathBox)
    {
        nint ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? selectedPath = await _fileDialogService.OpenFileAsync(
            ownerWindowHandle,
            CreateAllFilesFilter(),
            LocalizedStrings.SRP_Dialog_SelectPathFile_Title,
            TryGetExistingDirectory(pathBox.Text),
            LocalizedStrings.Common_OpenButton,
            PathRuleFilePickerSettingsIdentifier);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            pathBox.Text = selectedPath;
        }
    }

    private async Task BrowseForPathRuleFolderAsync(TextBox pathBox)
    {
        nint ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? selectedPath = await _fileDialogService.PickFolderAsync(
            ownerWindowHandle,
            LocalizedStrings.SRP_Dialog_SelectPathFolder_Title,
            TryGetExistingDirectory(pathBox.Text),
            LocalizedStrings.Common_OpenButton,
            PathRuleFolderPickerSettingsIdentifier);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            pathBox.Text = selectedPath;
        }
    }

    private async Task BrowseForHashFileAsync(TextBox filePathBox)
    {
        nint ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? initialDirectory = TryGetExistingDirectory(filePathBox.Text);
        string? selectedPath = await _fileDialogService.OpenFileAsync(
            ownerWindowHandle,
            CreateAllFilesFilter(),
            LocalizedStrings.SRP_Dialog_SelectHashFile_Title,
            initialDirectory,
            LocalizedStrings.Common_OpenButton,
            HashFilePickerSettingsIdentifier);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            filePathBox.Text = selectedPath;
        }
    }

    private async Task BrowseForCertificateRuleFileAsync(TextBox filePathBox)
    {
        nint ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? selectedPath = await _fileDialogService.OpenFileAsync(
            ownerWindowHandle,
            CreateCertificateFilesFilter(),
            LocalizedStrings.SRP_Dialog_SelectCertificateFile_Title,
            TryGetExistingDirectory(filePathBox.Text),
            LocalizedStrings.Common_OpenButton,
            CertificateRuleFilePickerSettingsIdentifier);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            filePathBox.Text = selectedPath;
        }
    }

    private async Task ShowMessageAsync(string messageResourceKey)
    {
        _ = await ShowCloseModalAsync(
            LocalizedStrings.Common_ErrorTitle,
            CreateMessageContent(GetString(messageResourceKey)),
            height: 240);
    }

    private Task<WindowDialogResult> ShowPrimaryModalAsync(
        string title,
        object content,
        string primaryButtonText,
        int width = 620,
        int height = 520)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = content,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = width,
            Height = height
        });

        return modalWindow.ShowDialogAsync();
    }

    private Task<WindowDialogResult> ShowCloseModalAsync(
        string title,
        object content,
        int width = 620,
        int height = 520)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = content,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            DefaultButton = WindowDialogResult.None,
            Width = width,
            Height = height
        });

        return modalWindow.ShowDialogAsync();
    }

    private static TextBlock CreateMessageContent(string message)
    {
        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static StackPanel CreateDialogPanel()
    {
        return new StackPanel
        {
            MinWidth = 420,
            Spacing = 12
        };
    }

    private static void AddLabeledControl(Panel panel, string label, FrameworkElement control)
    {
        StackPanel group = new()
        {
            Spacing = 6
        };
        group.Children.Add(new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap
        });
        group.Children.Add(control);
        panel.Children.Add(group);
    }

    private static StackPanel CreateDialogGroup(string label, params UIElement[] children)
    {
        StackPanel group = new()
        {
            Spacing = 8
        };
        group.Children.Add(new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap
        });

        foreach (UIElement child in children)
        {
            group.Children.Add(child);
        }

        return group;
    }

    private static RadioButton CreatePublisherScopeRadioButton(string text, bool isChecked)
    {
        return new RadioButton
        {
            Content = text,
            GroupName = "TrustedPublisherScope",
            IsChecked = isChecked
        };
    }

    private static SoftwareRestrictionPublisherScope GetSelectedPublisherScope(
        RadioButton endUsersRadio,
        RadioButton localAdministratorsRadio,
        RadioButton enterpriseAdministratorsRadio)
    {
        if (localAdministratorsRadio.IsChecked == true)
        {
            return SoftwareRestrictionPublisherScope.LocalAdministrators;
        }

        if (enterpriseAdministratorsRadio.IsChecked == true)
        {
            return SoftwareRestrictionPublisherScope.EnterpriseAdministrators;
        }

        return SoftwareRestrictionPublisherScope.EndUsers;
    }

    private static ComboBox CreateComboBox<T>(IEnumerable<ComboOption<T>> options, T selectedValue)
        where T : notnull
    {
        ComboBox comboBox = new()
        {
            MinWidth = 280,
            DisplayMemberPath = nameof(ComboOption<T>.Name),
            ItemsSource = options.ToList()
        };

        foreach (ComboOption<T> option in comboBox.Items.Cast<ComboOption<T>>())
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, selectedValue))
            {
                comboBox.SelectedItem = option;
                break;
            }
        }

        comboBox.SelectedIndex = comboBox.SelectedIndex < 0 ? 0 : comboBox.SelectedIndex;
        return comboBox;
    }

    private static T GetSelectedValue<T>(ComboBox comboBox, T fallback)
        where T : notnull
    {
        return comboBox.SelectedItem is ComboOption<T> option ? option.Value : fallback;
    }

    private IReadOnlyList<ComboOption<SoftwareRestrictionSecurityLevel>> CreateSecurityLevelOptions()
    {
        return
        [
            new(LocalizedStrings.SRP_SecurityLevel_Disallowed, SoftwareRestrictionSecurityLevel.Disallowed),
            new(LocalizedStrings.SRP_SecurityLevel_BasicUser, SoftwareRestrictionSecurityLevel.BasicUser),
            new(LocalizedStrings.SRP_SecurityLevel_Unrestricted, SoftwareRestrictionSecurityLevel.Unrestricted)
        ];
    }

    private IReadOnlyList<ComboOption<SoftwareRestrictionUserScope>> CreateUserScopeOptions()
    {
        return
        [
            new(LocalizedStrings.SRP_UserScope_AllUsers, SoftwareRestrictionUserScope.AllUsers),
            new(LocalizedStrings.SRP_UserScope_AllExceptAdministrators, SoftwareRestrictionUserScope.AllUsersExceptLocalAdministrators)
        ];
    }

    private IReadOnlyList<ComboOption<SoftwareRestrictionFileScope>> CreateFileScopeOptions()
    {
        return
        [
            new(LocalizedStrings.SRP_FileScope_ExecutableFilesOnly, SoftwareRestrictionFileScope.ExecutableFilesOnly),
            new(LocalizedStrings.SRP_FileScope_AllSoftwareFiles, SoftwareRestrictionFileScope.AllSoftwareFiles)
        ];
    }

    private IReadOnlyList<ComboOption<string>> CreateNetworkZoneOptions()
    {
        return
        [
            new(LocalizedStrings.SRP_NetworkZone_Internet, "3", LocalizedStrings.SRP_NetworkZone_Internet_Description),
            new(LocalizedStrings.SRP_NetworkZone_LocalComputer, "0", LocalizedStrings.SRP_NetworkZone_LocalComputer_Description),
            new(LocalizedStrings.SRP_NetworkZone_LocalIntranet, "1", LocalizedStrings.SRP_NetworkZone_LocalIntranet_Description),
            new(LocalizedStrings.SRP_NetworkZone_RestrictedSites, "4", LocalizedStrings.SRP_NetworkZone_RestrictedSites_Description),
            new(LocalizedStrings.SRP_NetworkZone_TrustedSites, "2", LocalizedStrings.SRP_NetworkZone_TrustedSites_Description)
        ];
    }

    private string CreateAllFilesFilter()
    {
        return $"{LocalizedStrings.SRP_FileDialog_AllFiles}\0{AllFilesFilterPattern}\0";
    }

    private string CreateCertificateFilesFilter()
    {
        return $"{LocalizedStrings.SRP_FileDialog_CertificateFiles}\0*.cer;*.crt;*.der;*.pem\0"
            + $"{LocalizedStrings.SRP_FileDialog_SignedFiles}\0*.exe;*.dll;*.msi;*.msp;*.cab;*.cat;*.ocx;*.sys\0"
            + $"{LocalizedStrings.SRP_FileDialog_AllFiles}\0{AllFilesFilterPattern}\0";
    }

    private static string? TryGetExistingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (Directory.Exists(expandedPath))
            {
                return expandedPath;
            }

            string? directory = Path.GetDirectoryName(expandedPath);
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                ? directory
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DesignatedFileTypeItem CreateDesignatedFileTypeItem(string extension)
    {
        string normalizedExtension = NormalizeFileExtension(extension);
        return new DesignatedFileTypeItem(normalizedExtension, ResolveFileTypeName(normalizedExtension));
    }

    private static string NormalizeFileExtension(string extension)
    {
        return extension.Trim().TrimStart('.').ToUpperInvariant();
    }

    private static bool ContainsFileExtension(
        IEnumerable<DesignatedFileTypeItem> fileTypes,
        string extension)
    {
        return fileTypes.Any(item => string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddFileType(
        ObservableCollection<DesignatedFileTypeItem> fileTypes,
        DesignatedFileTypeItem item)
    {
        List<DesignatedFileTypeItem> ordered = fileTypes
            .Append(item)
            .OrderBy(static fileType => fileType.Extension, StringComparer.OrdinalIgnoreCase)
            .ToList();

        fileTypes.Clear();
        foreach (DesignatedFileTypeItem fileType in ordered)
        {
            fileTypes.Add(fileType);
        }
    }

    private static string ResolveFileTypeName(string extension)
    {
        try
        {
            using RegistryKey? extensionKey = Registry.ClassesRoot.OpenSubKey($".{extension}");
            string? progId = extensionKey?.GetValue(null)?.ToString();
            if (!string.IsNullOrWhiteSpace(progId))
            {
                using RegistryKey? progIdKey = Registry.ClassesRoot.OpenSubKey(progId);
                string? fileType = progIdKey?.GetValue(null)?.ToString();
                if (!string.IsNullOrWhiteSpace(fileType))
                {
                    return fileType;
                }
            }
        }
        catch (Exception)
        {
            // Best effort only; fall back to the MMC-style generic file type name.
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            GetString(SoftwareRestrictionKeys.FileTypeFallbackFormat),
            extension);
    }

    private static string GetSelectedDescription<T>(ComboBox comboBox)
        where T : notnull
    {
        return comboBox.SelectedItem is ComboOption<T> option ? option.Description : string.Empty;
    }

    private static string GetString(string key)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    private sealed record ComboOption<T>(string Name, T Value, string Description = "")
        where T : notnull;

    private sealed record DesignatedFileTypeItem(string Extension, string FileType);
}
