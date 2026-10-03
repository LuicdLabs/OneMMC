using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;
using OneMMC.Localization;
using WinRT.Interop;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class AttachVHDDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public string VHDPath => VHDPathTextBox.Text;
    public bool IsReadOnly => ReadOnlyCheckBox.IsChecked ?? false;

    public AttachVHDDialog()
    {
        this.InitializeComponent();
        this.Closing += AttachVHDDialog_Closing;
    }

    private void AttachVHDDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(VHDPath))
            {
                args.Cancel = true;
                return;
            }
        }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        
        var selectedPath = await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            $"{LocalizedStrings.DiskMgmt_FileFilter_Vhd}\0*.vhdx;*.vhd\0{LocalizedStrings.Common_FileFilter_AllFiles}\0*.*\0",
            LocalizedStrings.DiskMgmt_SelectVhd);

        if (!string.IsNullOrEmpty(selectedPath))
        {
            VHDPathTextBox.Text = selectedPath;
        }
    }
}

