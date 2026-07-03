using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Infrastructure.Interop;

/// <summary>
/// Minimal blittable <c>VARIANT</c> for passing scalar arguments to, and reading scalar results from,
/// source-generated (<see cref="System.Runtime.InteropServices.Marshalling.GeneratedComInterfaceAttribute"/>)
/// dual COM interfaces. Covers the value kinds OneMMC needs: <c>VT_EMPTY</c>/<c>VT_NULL</c>, the
/// <c>VT_ERROR</c> "omitted optional parameter" sentinel, the integer/boolean/floating scalars, and
/// <c>VT_BSTR</c>.
/// <para>
/// Deliberately <b>blittable</b> so it marshals by value with no <c>[assembly: DisableRuntimeMarshalling]</c>
/// (which <see cref="System.Runtime.InteropServices.Marshalling.ComVariant"/> would require, and which
/// would force every handwritten runtime-marshalled P/Invoke in this assembly to migrate first). Sized to
/// the native VARIANT on the supported 64-bit targets (x64/ARM64): <c>VARTYPE</c> + reserved + a 16-byte
/// union = 24 bytes, with the value at offset 8.
/// </para>
/// </summary>
/// <remarks>
/// A variant that owns a resource (a <c>VT_BSTR</c>'s string, or a <c>VT_DISPATCH</c>/<c>VT_UNKNOWN</c>
/// reference) must be released via <see cref="Dispose"/>/<see cref="Clear"/> (which call
/// <c>VariantClear</c>) after use. This applies both to an <c>[in]</c> variant the caller built (the COM
/// callee never frees it) and to an <c>[out, retval]</c> variant a property getter handed back (the
/// caller owns it). Clearing a non-owning kind (<c>VT_EMPTY</c>/<c>VT_ERROR</c>/<c>VT_I4</c>/…) is a no-op,
/// so shared sentinels are safe to copy and dispose.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal partial struct Variant : IDisposable
{
    [FieldOffset(0)] private ushort _vt;
    [FieldOffset(8)] private nint _value;

    // VARENUM subset. Masked with VT_TYPEMASK when reading so array/byref flags are ignored.
    private const ushort VT_TYPEMASK = 0x0FFF;
    private const ushort VT_EMPTY = 0;
    private const ushort VT_NULL = 1;
    private const ushort VT_I2 = 2;
    private const ushort VT_I4 = 3;
    private const ushort VT_R4 = 4;
    private const ushort VT_R8 = 5;
    private const ushort VT_BSTR = 8;
    private const ushort VT_ERROR = 10;
    private const ushort VT_BOOL = 11;
    private const ushort VT_I1 = 16;
    private const ushort VT_UI1 = 17;
    private const ushort VT_UI2 = 18;
    private const ushort VT_UI4 = 19;
    private const ushort VT_I8 = 20;
    private const ushort VT_UI8 = 21;
    private const ushort VT_INT = 22;
    private const ushort VT_UINT = 23;
    private const uint DISP_E_PARAMNOTFOUND = 0x80020004;

    /// <summary>An explicitly empty variant (<c>VT_EMPTY</c>).</summary>
    internal static Variant Empty => default;

    /// <summary>The "truly omitted optional parameter" sentinel (<c>VT_ERROR</c> / <c>DISP_E_PARAMNOTFOUND</c>).</summary>
    internal static Variant Missing => new() { _vt = VT_ERROR, _value = unchecked((nint)DISP_E_PARAMNOTFOUND) };

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

    /// <summary>The variant's raw <c>VARTYPE</c> (including any array/byref flags).</summary>
    internal readonly ushort VarType => _vt;

    /// <summary>
    /// Renders the variant's scalar value as an invariant-culture string, mirroring the previous
    /// reflection/RCW behaviour (<c>Convert.ToString(value, CultureInfo.InvariantCulture)</c>) so callers
    /// that string-compare COM property values keep matching. Returns <see langword="null"/> for
    /// <c>VT_EMPTY</c>/<c>VT_NULL</c>, a null BSTR, or any kind not modeled here (e.g. object references).
    /// Does not free the variant — the caller still owns it and must <see cref="Clear"/>/<see cref="Dispose"/> it.
    /// </summary>
    internal readonly string? ToInvariantString()
    {
        return (_vt & VT_TYPEMASK) switch
        {
            VT_EMPTY or VT_NULL => null,
            VT_BSTR => _value == 0 ? null : Marshal.PtrToStringBSTR(_value),
            // Booleans marshalled through the CLR surfaced as System.Boolean, whose Convert.ToString is
            // "True"/"False"; preserve that so existing equality checks (e.g. == "true") still work.
            VT_BOOL => (short)_value != 0 ? bool.TrueString : bool.FalseString,
            VT_I1 => ((sbyte)_value).ToString(CultureInfo.InvariantCulture),
            VT_UI1 => ((byte)_value).ToString(CultureInfo.InvariantCulture),
            VT_I2 => ((short)_value).ToString(CultureInfo.InvariantCulture),
            VT_UI2 => ((ushort)_value).ToString(CultureInfo.InvariantCulture),
            VT_I4 or VT_INT => ((int)_value).ToString(CultureInfo.InvariantCulture),
            VT_UI4 or VT_UINT => ((uint)_value).ToString(CultureInfo.InvariantCulture),
            VT_I8 => ((long)_value).ToString(CultureInfo.InvariantCulture),
            VT_UI8 => ((ulong)_value).ToString(CultureInfo.InvariantCulture),
            VT_R4 => BitConverter.Int32BitsToSingle((int)_value).ToString(CultureInfo.InvariantCulture),
            VT_R8 => BitConverter.Int64BitsToDouble(_value).ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    /// <summary>
    /// Releases any resource the variant owns (BSTR string or object reference) and resets it to
    /// <c>VT_EMPTY</c>, via the OLE Automation <c>VariantClear</c>. A no-op for non-owning kinds.
    /// </summary>
    internal void Clear()
    {
        // VariantClear tolerates every VARTYPE (frees BSTR / Releases IDispatch|IUnknown / ignores
        // scalars), so it is correct for both caller-built [in] variants and returned [out] variants.
        VariantClear(ref this);
    }

    /// <summary>Releases any owned resource (see <see cref="Clear"/>).</summary>
    public void Dispose() => Clear();

    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(ref Variant pvarg);
}
