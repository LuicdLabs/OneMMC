using System;
using System.Collections.Generic;
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
    private const ushort VT_DISPATCH = 9;
    private const ushort VT_ERROR = 10;
    private const ushort VT_BOOL = 11;
    private const ushort VT_UNKNOWN = 13;
    private const ushort VT_I1 = 16;
    private const ushort VT_UI1 = 17;
    private const ushort VT_UI2 = 18;
    private const ushort VT_UI4 = 19;
    private const ushort VT_I8 = 20;
    private const ushort VT_UI8 = 21;
    private const ushort VT_INT = 22;
    private const ushort VT_UINT = 23;
    private const ushort VT_VARIANT = 12;
    private const ushort VT_ARRAY = 0x2000;
    private const uint DISP_E_PARAMNOTFOUND = 0x80020004;

    /// <summary>An explicitly empty variant (<c>VT_EMPTY</c>).</summary>
    internal static Variant Empty => default;

    /// <summary>A SQL/WMI NULL variant (<c>VT_NULL</c>) — used to clear a WMI property to NULL on Put.</summary>
    internal static Variant Null => new() { _vt = VT_NULL };

    /// <summary>The "truly omitted optional parameter" sentinel (<c>VT_ERROR</c> / <c>DISP_E_PARAMNOTFOUND</c>).</summary>
    internal static Variant Missing => new() { _vt = VT_ERROR, _value = unchecked((nint)DISP_E_PARAMNOTFOUND) };

    /// <summary>A <c>VT_I4</c> variant carrying <paramref name="value"/>.</summary>
    internal static Variant FromInt32(int value) => new() { _vt = VT_I4, _value = (nint)(uint)value };

    /// <summary>A <c>VT_BOOL</c> variant (<c>VARIANT_TRUE</c> = -1 / <c>VARIANT_FALSE</c> = 0).</summary>
    internal static Variant FromBool(bool value) => new() { _vt = VT_BOOL, _value = value ? -1 : 0 };

    /// <summary>
    /// A <c>VT_ARRAY | VT_UNKNOWN</c> variant wrapping a one-dimensional SAFEARRAY of the given COM
    /// interface pointers (each <c>AddRef</c>ed into the array by <c>SafeArrayPutElement</c>, so the
    /// caller keeps its own references) — the shape WMI wants for an embedded-instance array property
    /// (<c>CIM_OBJECT | CIM_FLAG_ARRAY</c>, e.g. IKE <c>Proposals</c>). Dispose to free the array and
    /// release its references. Returns an empty (<c>VT_EMPTY</c>) variant when there are no elements.
    /// </summary>
    internal static Variant FromComInterfaceArray(ReadOnlySpan<nint> interfaces)
    {
        if (interfaces.Length == 0)
        {
            return Empty;
        }

        nint psa = SafeArrayCreateVector(VT_UNKNOWN, 0, (uint)interfaces.Length);
        if (psa == 0)
        {
            return Empty;
        }

        // Populate the VT_UNKNOWN SAFEARRAY by writing the interface pointers straight into its data buffer
        // and taking a matching AddRef, rather than via SafeArrayPutElement. SafeArrayPutElement's
        // VT_UNKNOWN path (which walks the element's vtable to AddRef it) hard-faults here under the
        // marshal-free/source-generated interop model, whereas the direct write is stable and does the same
        // work. The array carries FADF_UNKNOWN from SafeArrayCreateVector, so SafeArrayDestroy/VariantClear
        // Releases each element on teardown — keeping the ref count balanced against these AddRefs.
        if (SafeArrayAccessData(psa, out nint pData) != 0 || pData == 0)
        {
            SafeArrayDestroy(psa);
            return Empty;
        }

        try
        {
            for (int i = 0; i < interfaces.Length; i++)
            {
                nint punk = interfaces[i];
                Marshal.WriteIntPtr(pData, i * nint.Size, punk);
                if (punk != 0)
                {
                    Marshal.AddRef(punk);
                }
            }
        }
        finally
        {
            SafeArrayUnaccessData(psa);
        }

        return new Variant { _vt = VT_ARRAY | VT_UNKNOWN, _value = psa };
    }

    /// <summary>A <c>VT_BSTR</c> variant; the returned variant owns the BSTR and must be disposed.</summary>
    internal static Variant FromString(string value) => new() { _vt = VT_BSTR, _value = Marshal.StringToBSTR(value) };

    /// <summary>
    /// A <c>VT_BSTR</c> variant when <paramref name="value"/> is non-empty (dispose to free it), or the
    /// <see cref="Missing"/> sentinel otherwise.
    /// </summary>
    internal static Variant OptionalString(string? value) =>
        string.IsNullOrEmpty(value) ? Missing : FromString(value);

    /// <summary>
    /// A <c>VT_ARRAY | VT_BSTR</c> variant wrapping a one-dimensional SAFEARRAY of the given strings
    /// (dispose to free the array + its BSTRs), or an empty (<c>VT_EMPTY</c>) variant when
    /// <paramref name="values"/> is null/empty — some OLE-automation setters (e.g. the firewall
    /// <c>INetFwRule::Interfaces</c>) treat an omitted value as "all" and reject an empty SAFEARRAY.
    /// </summary>
    internal static Variant FromStringArray(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return Empty;
        }

        nint psa = SafeArrayCreateVector(VT_BSTR, 0, (uint)values.Length);
        if (psa == 0)
        {
            return Empty;
        }

        for (int i = 0; i < values.Length; i++)
        {
            nint bstr = Marshal.StringToBSTR(values[i] ?? string.Empty);
            // SafeArrayPutElement copies the BSTR (SysAllocString), so free our local copy after.
            int hr = SafeArrayPutElement(psa, in i, in bstr);
            Marshal.FreeBSTR(bstr);
            if (hr != 0)
            {
                SafeArrayDestroy(psa);
                return Empty;
            }
        }

        return new Variant { _vt = VT_ARRAY | VT_BSTR, _value = psa };
    }

    /// <summary>The variant's raw <c>VARTYPE</c> (including any array/byref flags).</summary>
    internal readonly ushort VarType => _vt;

    /// <summary>
    /// The variant's raw 8-byte value slot (offset 8): the inline scalar bits for value kinds, or the
    /// pointer for <c>VT_BSTR</c> (BSTR), <c>VT_UNKNOWN</c>/<c>VT_DISPATCH</c> (the interface pointer),
    /// and <c>VT_ARRAY</c> (the SAFEARRAY). Exposed for the classic-WMI (<c>IWbemClassObject</c>) layer,
    /// which boxes a returned VARIANT to the CLR type dictated by the property's CIMTYPE (WMI stores both
    /// <c>CIM_UINT16</c> and <c>CIM_UINT32</c> as <c>VT_I4</c>, so VARTYPE alone is insufficient).
    /// </summary>
    internal readonly nint RawValue => _value;

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
    /// Wraps the object reference carried by a <c>VT_DISPATCH</c>/<c>VT_UNKNOWN</c> variant as the
    /// source-generated COM interface <typeparamref name="TInterface"/> (a new <c>UniqueInstance</c>
    /// wrapper owning its own reference — release it via <see cref="ComActivator.Release"/>), or
    /// <see langword="null"/> for a null reference or any other variant kind. The variant keeps its own
    /// reference; the caller still must <see cref="Clear"/>/<see cref="Dispose"/> it.
    /// </summary>
    internal readonly TInterface? ToComInterface<TInterface>() where TInterface : class
    {
        if ((_vt & VT_TYPEMASK) is not (VT_DISPATCH or VT_UNKNOWN) || _value == 0)
        {
            return null;
        }

        object wrapper = ComActivator.ComWrappers.GetOrCreateObjectForComInstance(_value, CreateObjectFlags.UniqueInstance);
        return (TInterface)wrapper;
    }

    /// <summary>
    /// Reads a <c>VT_ARRAY</c> variant holding a one-dimensional SAFEARRAY of VARIANTs or BSTRs (the
    /// shape OLE Automation APIs use for "array of strings" properties) into a string list, rendering
    /// each element via <see cref="ToInvariantString"/> semantics and skipping empty/null elements.
    /// Returns an empty list for non-array variants. Does not free the array — the caller still owns
    /// the variant and must <see cref="Clear"/>/<see cref="Dispose"/> it.
    /// </summary>
    internal readonly List<string> ToStringList()
    {
        var result = new List<string>();
        if ((_vt & VT_ARRAY) == 0 || _value == 0)
        {
            return result;
        }

        if (SafeArrayGetLBound(_value, 1, out int lower) != 0 ||
            SafeArrayGetUBound(_value, 1, out int upper) != 0)
        {
            return result;
        }

        ushort elementType = (ushort)(_vt & VT_TYPEMASK);
        for (int i = lower; i <= upper; i++)
        {
            if (elementType == VT_VARIANT)
            {
                // SafeArrayGetElement copies the element into the caller's (VT_EMPTY-initialized)
                // variant; the copy is ours to clear.
                Variant element = default;
                if (SafeArrayGetElement(_value, in i, ref element) == 0)
                {
                    string? text = element.ToInvariantString();
                    element.Clear();
                    if (!string.IsNullOrEmpty(text))
                    {
                        result.Add(text);
                    }
                }
            }
            else if (elementType == VT_BSTR)
            {
                // For BSTR arrays the function hands back a freshly allocated copy the caller frees.
                nint bstr = 0;
                if (SafeArrayGetElement(_value, in i, ref bstr) == 0 && bstr != 0)
                {
                    string text = Marshal.PtrToStringBSTR(bstr);
                    Marshal.FreeBSTR(bstr);
                    if (!string.IsNullOrEmpty(text))
                    {
                        result.Add(text);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a <c>VT_ARRAY | VT_UI1</c> variant (a SAFEARRAY of bytes — the shape a fetched binary
    /// attribute such as <c>objectSid</c> comes back as) into a byte array, or <see langword="null"/>
    /// for any other kind. Does not free the variant — the caller keeps ownership.
    /// </summary>
    internal readonly byte[]? ToByteArray()
    {
        if ((_vt & VT_ARRAY) == 0 || (_vt & VT_TYPEMASK) != VT_UI1 || _value == 0)
        {
            return null;
        }

        if (SafeArrayGetLBound(_value, 1, out int lower) != 0 ||
            SafeArrayGetUBound(_value, 1, out int upper) != 0)
        {
            return null;
        }

        int count = upper - lower + 1;
        if (count <= 0)
        {
            return null;
        }

        if (SafeArrayAccessData(_value, out nint pData) != 0 || pData == 0)
        {
            return null;
        }

        try
        {
            var result = new byte[count];
            Marshal.Copy(pData, result, 0, count);
            return result;
        }
        finally
        {
            SafeArrayUnaccessData(_value);
        }
    }

    /// <summary>
    /// Releases any resource the variant owns (BSTR string, object reference, or SAFEARRAY) and resets
    /// it to <c>VT_EMPTY</c>, via the OLE Automation <c>VariantClear</c>. A no-op for non-owning kinds.
    /// </summary>
    internal void Clear()
    {
        // VariantClear tolerates every VARTYPE (frees BSTR/SAFEARRAY / Releases IDispatch|IUnknown /
        // ignores scalars), so it is correct for both caller-built [in] variants and returned [out] variants.
        VariantClear(ref this);
    }

    /// <summary>Releases any owned resource (see <see cref="Clear"/>).</summary>
    public void Dispose() => Clear();

    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(ref Variant pvarg);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetLBound(nint psa, uint nDim, out int plLbound);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetUBound(nint psa, uint nDim, out int plUbound);

    [LibraryImport("oleaut32.dll", EntryPoint = "SafeArrayGetElement")]
    private static partial int SafeArrayGetElement(nint psa, in int rgIndices, ref Variant pv);

    [LibraryImport("oleaut32.dll", EntryPoint = "SafeArrayGetElement")]
    private static partial int SafeArrayGetElement(nint psa, in int rgIndices, ref nint pv);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayAccessData(nint psa, out nint ppvData);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayUnaccessData(nint psa);

    [LibraryImport("oleaut32.dll")]
    private static partial nint SafeArrayCreateVector(ushort vt, int lLbound, uint cElements);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayPutElement(nint psa, in int rgIndices, in nint pv);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayDestroy(nint psa);
}
