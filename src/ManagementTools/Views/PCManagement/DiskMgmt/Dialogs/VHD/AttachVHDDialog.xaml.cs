using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;
using ManagementTools.Localization;
using WinRT.Interop;

namespace ManagementTools.Views.DiskMgmt;

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
        
        var selectedPath = await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            "Virtual Hard Disk\0*.vhdx;*.vhd\0All Files\0*.*\0",
            "Select Virtual Hard Disk");

        if (!string.IsNullOrEmpty(selectedPath))
        {
            VHDPathTextBox.Text = selectedPath;
        }
    }
}

