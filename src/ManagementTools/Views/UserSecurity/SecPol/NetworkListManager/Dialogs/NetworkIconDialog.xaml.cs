using ManagementTools.Core.Features.UserSecurity.Models.NetworkListManager;
using ManagementTools.Core.Infrastructure.WindowsCapabilities;
using ManagementTools.Localization;
using ManagementTools.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ManagementTools.Views;

public sealed partial class NetworkIconDialog : ContentDialog
{
    private readonly IconPickerService _iconPickerService;
    private NetworkListIconPayload? _iconPayload;
    private string? _selectedIconPath;
    private int _selectedIconIndex;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public NetworkListIconPayload? IconPayload => IconRadioButton.IsChecked == true
        ? ClonePayload(_iconPayload)
        : null;

    public NetworkIconDialog(IconPickerService iconPickerService)
    {
        _iconPickerService = iconPickerService;
        InitializeComponent();
        UpdateState();
    }

    public async Task SetStateAsync(NetworkListIconPayload? payload)
    {
        _iconPayload = ClonePayload(payload);
        _selectedIconPath = null;
        _selectedIconIndex = 0;

        IconRadioButton.IsChecked = payload?.IsConfigured == true;
        NotConfiguredRadioButton.IsChecked = payload?.IsConfigured != true;

        await UpdatePreviewAsync();
        UpdateState();
    }

    private void SelectionRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateState();
    }

    private async void ChangeIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance is null)
        {
            return;
        }

        nint ownerHandle = WindowNative.GetWindowHandle(App.MainWindowInstance);
        IconPickerResult? result = _iconPickerService.PickIcon(ownerHandle, _selectedIconPath, _selectedIconIndex);
        if (result is null)
        {
            return;
        }

        _selectedIconPath = result.IconPath;
        _selectedIconIndex = result.IconIndex;
        _iconPayload = ToNetworkListPayload(result.Payload);

        await UpdatePreviewAsync();
        UpdateState();
    }

    private async Task UpdatePreviewAsync()
    {
        IconPreviewImage.Source = await CreateBitmapImageAsync(_iconPickerService.CreatePreview(ToIconPickerPayload(_iconPayload)));
        IconPreviewPlaceholderText.Visibility = IconPreviewImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateState()
    {
        bool hasCustomIcon = IconRadioButton.IsChecked == true;
        IconConfigurationPanel.Visibility = hasCustomIcon ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = !hasCustomIcon || _iconPayload?.IsConfigured == true;
    }

    private static NetworkListIconPayload? ClonePayload(NetworkListIconPayload? payload)
    {
        if (payload is null)
        {
            return null;
        }

        return new NetworkListIconPayload
        {
            Icon16Hex = payload.Icon16Hex,
            Icon24Hex = payload.Icon24Hex,
            Icon32Hex = payload.Icon32Hex,
            Icon48Hex = payload.Icon48Hex
        };
    }

    private static NetworkListIconPayload ToNetworkListPayload(IconPickerPayload payload)
    {
        return new NetworkListIconPayload
        {
            Icon16Hex = payload.Icon16Hex,
            Icon24Hex = payload.Icon24Hex,
            Icon32Hex = payload.Icon32Hex,
            Icon48Hex = payload.Icon48Hex
        };
    }

    private static IconPickerPayload? ToIconPickerPayload(NetworkListIconPayload? payload)
    {
        if (payload is null)
        {
            return null;
        }

        return new IconPickerPayload
        {
            Icon16Hex = payload.Icon16Hex,
            Icon24Hex = payload.Icon24Hex,
            Icon32Hex = payload.Icon32Hex,
            Icon48Hex = payload.Icon48Hex
        };
    }

    private static async Task<BitmapImage?> CreateBitmapImageAsync(IconPickerPreview? preview)
    {
        if (preview is null || preview.PngBytes.Length == 0)
        {
            return null;
        }

        using InMemoryRandomAccessStream randomAccessStream = new();
        await randomAccessStream.WriteAsync(preview.PngBytes.AsBuffer());
        randomAccessStream.Seek(0);

        BitmapImage image = new();
        await image.SetSourceAsync(randomAccessStream);
        return image;
    }
}
