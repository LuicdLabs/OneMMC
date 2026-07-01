using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32.Foundation;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.WindowsCapabilities;

/// <summary>
/// Holds serialized icon payloads for the common Windows policy icon format.
/// </summary>
public sealed class IconPickerPayload
{
    /// <summary>
    /// Gets or sets the serialized 16x16 icon payload.
    /// </summary>
    public string Icon16Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized 24x24 icon payload.
    /// </summary>
    public string Icon24Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized 32x32 icon payload.
    /// </summary>
    public string Icon32Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized 48x48 icon payload.
    /// </summary>
    public string Icon48Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets whether all icon payload strings are present.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Icon16Hex)
        && !string.IsNullOrWhiteSpace(Icon24Hex)
        && !string.IsNullOrWhiteSpace(Icon32Hex)
        && !string.IsNullOrWhiteSpace(Icon48Hex);
}

/// <summary>
/// Represents the icon selection result returned from the native icon picker.
/// </summary>
public sealed class IconPickerResult
{
    /// <summary>
    /// Gets or sets the source file that contains the selected icon.
    /// </summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected icon index.
    /// </summary>
    public int IconIndex { get; set; }

    /// <summary>
    /// Gets or sets the serialized icon payload.
    /// </summary>
    public IconPickerPayload Payload { get; set; } = new();
}

/// <summary>
/// Carries PNG preview bytes for a selected icon without exposing UI types from Core.
/// </summary>
public sealed class IconPickerPreview
{
    /// <summary>
    /// Gets or sets the PNG-encoded preview bytes.
    /// </summary>
    public byte[] PngBytes { get; set; } = [];

    /// <summary>
    /// Gets or sets the pixel size of the preview.
    /// </summary>
    public int PixelSize { get; set; }
}

/// <summary>
/// Shows the native icon picker and serializes selected icons for policy storage.
/// </summary>
public sealed class IconPickerService
{
    private const int BitmapHeaderSize = 56;
    private static readonly int[] IconSizes = [16, 24, 32, 48];
    private readonly ILogger<IconPickerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IconPickerService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public IconPickerService(ILogger<IconPickerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Shows the native icon picker and serializes the selected icon.
    /// </summary>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <param name="initialPath">The initial icon resource path.</param>
    /// <param name="initialIndex">The initial icon index.</param>
    /// <returns>The selected icon result, or <see langword="null"/> when cancelled or extraction fails.</returns>
    public unsafe IconPickerResult? PickIcon(nint ownerWindowHandle, string? initialPath, int initialIndex)
    {
        try
        {
            string resolvedPath = ResolveInitialPath(initialPath);
            char[] pathBuffer = new char[Math.Max(260, resolvedPath.Length + 1)];
            resolvedPath.CopyTo(0, pathBuffer, 0, resolvedPath.Length);

            int iconIndex = initialIndex;
            fixed (char* pathChars = pathBuffer)
            {
                int dialogResult = Win32PInvoke.PickIconDlg(new HWND(ownerWindowHandle), pathChars, (uint)pathBuffer.Length, &iconIndex);
                if (dialogResult != 1)
                {
                    return null;
                }

                string selectedPath = new(pathChars);
                if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
                {
                    _logger.LogWarning("[IconPickerService] PickIconDlg returned an invalid path.");
                    return null;
                }

                IconPickerPayload payload = CreatePayload(selectedPath, iconIndex);
                return new IconPickerResult
                {
                    IconPath = selectedPath,
                    IconIndex = iconIndex,
                    Payload = payload
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[IconPickerService] Failed to pick or serialize an icon.");
            return null;
        }
    }

    /// <summary>
    /// Creates PNG preview bytes from a serialized icon payload.
    /// </summary>
    /// <param name="payload">The serialized payload.</param>
    /// <param name="preferredSize">The preferred icon size.</param>
    /// <returns>The preview payload, or <see langword="null"/> when the payload is incomplete.</returns>
    public IconPickerPreview? CreatePreview(IconPickerPayload? payload, int preferredSize = 32)
    {
        if (payload?.IsConfigured != true)
        {
            return null;
        }

        try
        {
            using Bitmap? bitmap = CreateBitmapFromPayload(payload, preferredSize);
            if (bitmap is null)
            {
                return null;
            }

            using MemoryStream memoryStream = new();
            bitmap.Save(memoryStream, ImageFormat.Png);
            return new IconPickerPreview
            {
                PngBytes = memoryStream.ToArray(),
                PixelSize = preferredSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[IconPickerService] Failed to create icon preview bytes.");
            return null;
        }
    }

    private static string ResolveInitialPath(string? initialPath)
    {
        string candidatePath = string.IsNullOrWhiteSpace(initialPath)
            ? Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\shell32.dll")
            : Environment.ExpandEnvironmentVariables(initialPath);

        if (!File.Exists(candidatePath))
        {
            candidatePath = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\shell32.dll");
        }

        return candidatePath;
    }

    private IconPickerPayload CreatePayload(string iconPath, int iconIndex)
    {
        Dictionary<int, Bitmap> extractedIcons = [];

        try
        {
            foreach (int size in IconSizes)
            {
                extractedIcons[size] = ExtractBitmap(iconPath, iconIndex, size);
            }

            return new IconPickerPayload
            {
                Icon16Hex = EncodeBitmap(extractedIcons[16], 16),
                Icon24Hex = EncodeBitmap(extractedIcons[24], 24),
                Icon32Hex = EncodeBitmap(extractedIcons[32], 32),
                Icon48Hex = EncodeBitmap(extractedIcons[48], 48)
            };
        }
        finally
        {
            foreach (Bitmap bitmap in extractedIcons.Values)
            {
                bitmap.Dispose();
            }
        }
    }

    private unsafe Bitmap ExtractBitmap(string iconPath, int iconIndex, int size)
    {
        IntPtr largeIcon = IntPtr.Zero;
        IntPtr smallIcon = IntPtr.Zero;

        try
        {
            uint iconSize = MakeIconSize((ushort)size, (ushort)size);
            int hr = IconPickerNativeMethods.SHDefExtractIconW(iconPath, iconIndex, 0, &largeIcon, &smallIcon, iconSize);
            if (hr < 0)
            {
                throw new InvalidOperationException($"SHDefExtractIconW failed with HRESULT 0x{hr:X8}.");
            }

            IntPtr selectedIcon = largeIcon != IntPtr.Zero ? largeIcon : smallIcon;
            if (selectedIcon == IntPtr.Zero)
            {
                throw new InvalidOperationException("SHDefExtractIconW did not return an icon handle.");
            }

            using Icon icon = Icon.FromHandle(selectedIcon);
            using Bitmap sourceBitmap = icon.ToBitmap();
            Bitmap resizedBitmap = new(size, size, PixelFormat.Format32bppArgb);

            using Graphics graphics = Graphics.FromImage(resizedBitmap);
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(sourceBitmap, new Rectangle(0, 0, size, size));
            return resizedBitmap;
        }
        finally
        {
            if (largeIcon != IntPtr.Zero)
            {
                _ = IconPickerNativeMethods.DestroyIcon(largeIcon);
            }

            if (smallIcon != IntPtr.Zero && smallIcon != largeIcon)
            {
                _ = IconPickerNativeMethods.DestroyIcon(smallIcon);
            }
        }
    }

    private static Bitmap? CreateBitmapFromPayload(IconPickerPayload payload, int preferredSize)
    {
        (string HexPayload, int Size)? selectedPayload = SelectBestPayload(payload, preferredSize);
        if (selectedPayload is null)
        {
            return null;
        }

        byte[] bytes = Convert.FromHexString(selectedPayload.Value.HexPayload);
        int size = selectedPayload.Value.Size;
        int pixelByteCount = size * size * 4;
        if (bytes.Length < BitmapHeaderSize + pixelByteCount)
        {
            return null;
        }

        Bitmap bitmap = new(size, size, PixelFormat.Format32bppArgb);
        BitmapData bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            for (int row = 0; row < size; row++)
            {
                IntPtr rowTarget = bitmapData.Stride >= 0
                    ? IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride)
                    : IntPtr.Add(bitmapData.Scan0, (size - 1 - row) * -bitmapData.Stride);
                int payloadRow = size - 1 - row;

                Marshal.Copy(bytes, BitmapHeaderSize + (payloadRow * size * 4), rowTarget, size * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    private static (string HexPayload, int Size)? SelectBestPayload(IconPickerPayload payload, int preferredSize)
    {
        List<(int Distance, int Size, string HexPayload)> candidates = [];

        AddCandidate(payload.Icon16Hex, 16);
        AddCandidate(payload.Icon24Hex, 24);
        AddCandidate(payload.Icon32Hex, 32);
        AddCandidate(payload.Icon48Hex, 48);

        return candidates
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Size)
            .Select(candidate => (candidate.HexPayload, candidate.Size))
            .FirstOrDefault();

        void AddCandidate(string hexPayload, int size)
        {
            if (!string.IsNullOrWhiteSpace(hexPayload))
            {
                candidates.Add((Math.Abs(size - preferredSize), size, hexPayload));
            }
        }
    }

    private static string EncodeBitmap(Bitmap bitmap, int size)
    {
        byte[] pixelBytes = ReadBottomUpPixelBytes(bitmap, size);
        byte[] payloadBytes = new byte[BitmapHeaderSize + pixelBytes.Length + (CalculateMaskStride(size) * size)];

        WriteHeader(payloadBytes, size);
        System.Buffer.BlockCopy(pixelBytes, 0, payloadBytes, BitmapHeaderSize, pixelBytes.Length);
        return Convert.ToHexString(payloadBytes);
    }

    private static byte[] ReadBottomUpPixelBytes(Bitmap bitmap, int size)
    {
        BitmapData bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            byte[] pixels = new byte[size * size * 4];

            for (int row = 0; row < size; row++)
            {
                int visualRow = size - 1 - row;
                IntPtr rowSource = bitmapData.Stride >= 0
                    ? IntPtr.Add(bitmapData.Scan0, visualRow * bitmapData.Stride)
                    : IntPtr.Add(bitmapData.Scan0, (size - 1 - visualRow) * -bitmapData.Stride);

                Marshal.Copy(rowSource, pixels, row * size * 4, size * 4);
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static void WriteHeader(byte[] target, int size)
    {
        using MemoryStream stream = new(target);
        using BinaryWriter writer = new(stream);

        int maskStride = CalculateMaskStride(size);

        writer.Write(1U);
        writer.Write((uint)(size / 2));
        writer.Write((uint)(size / 2));
        writer.Write(0U);
        writer.Write((uint)size);
        writer.Write((uint)size);
        writer.Write((uint)(size * 4));
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0U);
        writer.Write((uint)size);
        writer.Write((uint)size);
        writer.Write((uint)maskStride);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(0U);
    }

    private static int CalculateMaskStride(int size) => ((size + 15) / 16) * 2;

    private static uint MakeIconSize(ushort largeSize, ushort smallSize) => (uint)(largeSize | (smallSize << 16));
}

/// <summary>
/// Centralizes handwritten icon-related interop that is not projected cleanly by CsWin32.
/// </summary>
internal static partial class IconPickerNativeMethods
{
    /// <summary>
    /// Extracts icon handles from a resource file.
    /// </summary>
    /// <remarks>
    /// CsWin32 currently does not emit a reliable callable projection for this workflow,
    /// so this wrapper keeps the exception localized to the common icon picker service.
    /// </remarks>
    [LibraryImport("Shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial int SHDefExtractIconW(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        IntPtr* phiconLarge,
        IntPtr* phiconSmall,
        uint nIconSize);

    /// <summary>
    /// Releases an icon handle returned by native shell APIs.
    /// </summary>
    [LibraryImport("User32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr hIcon);
}
