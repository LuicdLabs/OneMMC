using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;
using OneMMC.Localization;
using WinRT.Interop;

namespace OneMMC.Views.DiskMgmt;

public sealed partial class CreateVHDDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    
    public string VHDPath => VHDPathTextBox.Text;
    public ulong VHDSizeInMB => (ulong)VHDSizeNumberBox.Value;
    public bool IsFixedSize => VHDTypeComboBox.SelectedIndex == 0;

    public CreateVHDDialog()
    {
        this.InitializeComponent();
        this.Closing += CreateVHDDialog_Closing;
    }

    private void CreateVHDDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(VHDPath))
            {
                args.Cancel = true;
                return;
            }

            if (VHDSizeInMB == 0)
            {
                args.Cancel = true;
                return;
            }
        }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);

        var selectedPath = await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().SaveFileAsync(
            hwnd,
            "Virtual Hard Disk (*.vhdx)\0*.vhdx\0Virtual Hard Disk (*.vhd)\0*.vhd\0All Files\0*.*\0",
            "Create Virtual Hard Disk",
            null,
            "vhdx",
            "NewVirtualDisk");

        if (!string.IsNullOrEmpty(selectedPath))
        {
            App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().CleanupPlaceholderFile(selectedPath);
            VHDPathTextBox.Text = selectedPath;
        }
    }
}

