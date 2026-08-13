using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PrintManagement.ViewModels;
using OneMMC.Localization;

namespace OneMMC.Views;

/// <summary>
/// ContentDialog for displaying and managing printer forms.
/// </summary>
public sealed partial class FormsDialog : ContentDialog
{
    public PrintManagementViewModel ViewModel { get; }
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public string SubtitleText => string.Format(
        LocalizedStrings.PrintMgmt_FormsCountFormat, 
        ViewModel.Forms.Count);

    public FormsDialog(PrintManagementViewModel viewModel, XamlRoot xamlRoot)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        InitializeComponent();
        
        XamlRoot = xamlRoot;
        RequestedTheme = App.CurrentTheme;
    }
}
