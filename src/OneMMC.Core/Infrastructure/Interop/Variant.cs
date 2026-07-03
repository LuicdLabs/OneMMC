using System;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Infrastructure.Interop;

/// <summary>
/// Minimal blittable <c>VARIANT</c> for passing scalar arguments to source-generated
/// (<see cref="System.Runtime.InteropServices.Marshalling.GeneratedComInterfaceAttribute"/>) dual COM
/// interfaces. Covers only the value kinds OneMMC needs: <c>VT_EMPTY</c>, the <c>VT_ERROR</c> "omitted
/// optional parameter" sentinel, <c>VT_I4</c>, and <c>VT_BSTR</c>.
/// <para>
/// Deliberately <b>blittable</b> so it marshals by value with no <c>[assembly: DisableRuntimeMarshalling]</c>
/// (which <see cref="System.Runtime.InteropServices.Marshalling.ComVariant"/> would require, and which
/// would force every handwritten runtime-marshalled P/Invoke in this assembly to migrate first). Sized to
/// the native VARIANT on the supported 64-bit targets (x64/ARM64): <c>VARTYPE</c> + reserved + a 16-byte
/// union = 24 bytes, with the value at offset 8.
/// </para>
/// </summary>
/// <remarks>
/// A <c>VT_BSTR</c> variant owns a BSTR; the caller must <see cref="Dispose"/> it (directly or via
/// <see langword="using"/>) after the COM call to free it. The COM callee never frees an <c>[in]</c>
/// VARIANT, so freeing the local copy exactly once is correct. Disposing the non-allocating kinds
/// (<c>VT_EMPTY</c>/<c>VT_ERROR</c>/<c>VT_I4</c>) is a no-op, so shared sentinels are safe to copy and dispose.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct Variant : IDisposable
{
    [FieldOffset(0)] private ushort _vt;
    [FieldOffset(8)] private nint _value;

    private const ushort VT_EMPTY = 0;
    private const ushort VT_I4 = 3;
    private const ushort VT_BSTR = 8;
    private const ushort VT_ERROR = 10;
    private const uint DISP_E_PARAMNOTFOUND = 0x80020004;

    /// <summary>An explicitly empty variant (<c>VT_EMPTY</c>).</summary>
    internal static Variant Empty => default;

    /// <summary>The "truly omitted optional parameter" sentinel (<c>VT_ERROR</c> / <c>DISP_E_PARAMNOTFOUND</c>).</summary>
    internal static Variant Missing => new() { _vt = VT_ERROR, _value = (nint)DISP_E_PARAMNOTFOUND };

    /// <summary>A <c>VT_I4</c> variant carrying <paramref name="value"/>.</summary>
    internal static Variant FromInt32(int value) => new() { _vt = VT_I4, _value = (nint)(uint)value };

    /// <summary>A <c>VT_BSTR</c> variant; the returned variant owns the BSTR and must be disposed.</summary>
    internal static Variant FromString(string value) => new() { _vt = VT_BSTR, _value = Marshal.StringToBSTR(value) };

    /// <summary>
    /// A <c>VT_BSTR</c> variant when <paramref name="value"/> is non-empty (dispose to free it), or the
    /// <see cref="Missing"/> sentinel otherwise.
    /// </summary>
    internal static Variant OptionalString(string? value) =>
        string.IsNullOrEmpty(value) ? Missing : FromString(value);

    /// <summary>Frees the BSTR if this is a <c>VT_BSTR</c> variant; a no-op for all other kinds.</summary>
    public void Dispose()
    {
        if (_vt == VT_BSTR && _value != 0)
        {
            Marshal.FreeBSTR(_value);
            _value = 0;
            _vt = VT_EMPTY;
        }
    }
}
