using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace OneMMC.Views.Settings
{
    /// <summary>
    /// A modal <see cref="ContentDialog"/> that loads and displays a legal document
    /// from the app's legal documents folder.
    /// </summary>
    public sealed partial class LegalDocumentDialog : ContentDialog
    {
        private readonly string _resourcePath;

        public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

        public LegalDocumentDialog(string title, string resourcePath, XamlRoot xamlRoot, ElementTheme requestedTheme)
        {
            _resourcePath = resourcePath;

            InitializeComponent();

            Title = title;
            XamlRoot = xamlRoot;
            RequestedTheme = requestedTheme;

            Loaded += async (_, _) => await LoadContentAsync();
        }

        public new async Task ShowAsync()
        {
            await base.ShowAsync().AsTask();
        }

        private async Task LoadContentAsync()
        {
            try
            {
                var text = await ReadDocumentTextAsync();
                ContentTextBlock.Text = text;
            }
            catch (Exception)
            {
                ContentTextBlock.Text = LocalizedStrings.LegalDocument_LoadError;
            }
        }

        private async Task<string> ReadDocumentTextAsync()
        {
            if (IsPackaged())
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(_resourcePath));
                return await FileIO.ReadTextAsync(file);
            }

            var relativePath = _resourcePath
                .Replace("ms-appx:///", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace('/', Path.DirectorySeparatorChar);

            var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
            return await File.ReadAllTextAsync(fullPath);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetCurrentPackageId")]
        private static extern int GetCurrentPackageId(ref uint bufferLength, IntPtr buffer);

        private static bool IsPackaged()
        {
            try
            {
                uint len = 0;
                return GetCurrentPackageId(ref len, IntPtr.Zero) == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
