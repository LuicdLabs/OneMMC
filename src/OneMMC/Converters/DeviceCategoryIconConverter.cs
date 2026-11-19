using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OneMMC.Converters;

/// <summary>
/// Creates a colored category header icon, leaving missing icons unset.
/// </summary>
public sealed partial class DeviceCategoryIconConverter : IValueConverter
{
    private const double IconSize = 24;
    private readonly ILogger<DeviceCategoryIconConverter> _logger =
        App.GetRequiredService<ILogger<DeviceCategoryIconConverter>>();
    private readonly Dictionary<byte[], BitmapImage?> _images = [];

    /// <summary>
    /// Converts an optional PNG payload to an icon element for a settings expander.
    /// </summary>
    /// <returns>A new icon element, or null so the card retains its original spacing.</returns>
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not byte[] { Length: > 0 } png)
        {
            return null;
        }

        if (!_images.TryGetValue(png, out BitmapImage? image))
        {
            try
            {
                using MemoryStream stream = new(png, writable: false);
                using var randomAccessStream = stream.AsRandomAccessStream();
                image = new BitmapImage();
                image.SetSource(randomAccessStream);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to decode device category icon.");
                image = null;
            }

            _images.Add(png, image);
        }

        // Share the bounded set of image sources, but never share an element between cards.
        return image is null ? null : new ImageIcon { Source = image, Width = IconSize, Height = IconSize };
    }

    /// <summary>
    /// Reverse conversion is unsupported because category icon bindings are one-way.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
