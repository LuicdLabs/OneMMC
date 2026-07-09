using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using OneMMC.Core.Infrastructure.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.System.Variant;
using Windows.Win32.System.Wmi;

namespace OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;

/// <summary>
/// Managed wrapper over a classic-WMI <c>IWbemClassObject*</c> — the marshal-free, Native-AOT-compatible
/// replacement for <c>Microsoft.Management.Infrastructure.CimInstance</c> in the Windows Firewall feature.
/// Owns exactly one reference on the underlying object and releases it on <see cref="Dispose"/>.
/// <para>
/// Property reads go through <see cref="GetValue"/>, which boxes the returned VARIANT to the same CLR type
/// the old <c>CimInstance</c> exposed (driven by the property's CIMTYPE — see <see cref="WbemCimType"/>),
/// so the many <c>value switch</c> / <c>is T[]</c> consumers keep matching. Embedded objects and embedded
/// object arrays (e.g. IKE <c>Proposals</c>) surface as further <see cref="WbemObject"/>s.
/// </para>
/// </summary>
internal sealed unsafe partial class WbemObject : IDisposable
{
    private IWbemClassObject* _object;

    internal WbemObject(IWbemClassObject* obj) => _object = obj;

    /// <summary>The raw pointer (never null for a live wrapper); for the session's write/associator calls.</summary>
    internal IWbemClassObject* Pointer => _object;

    /// <summary>The object's WMI class name (<c>__CLASS</c> system property).</summary>
    internal string? ClassName => GetValue("__CLASS") as string;

    /// <summary>The object's full WMI path (<c>__PATH</c> system property) — used for associator queries and delete.</summary>
    internal string? Path => GetValue("__PATH") as string;

    /// <summary>
    /// Reads the named property, returning the value boxed to the CLR type the old <c>CimInstance</c>
    /// produced, or <see langword="null"/> when the property is absent or NULL — matching the old
    /// <c>CimInstanceProperties[name]?.Value</c> indexer (an unknown name degrades to <see langword="null"/>
    /// via the returned HRESULT, with no first-chance <see cref="COMException"/>).
    /// </summary>
    internal object? GetValue(string name)
    {
        Variant variant = default;
        int cimType = 0;
        HRESULT hr;
        fixed (char* pName = name)
        {
            hr = RawGet(_object, (PCWSTR)pName, (VARIANT*)&variant, &cimType);
        }

        if (hr.Failed)
        {
            return null;
        }

        try
        {
            ushort vt = (ushort)(variant.VarType & 0x0FFF);
            if (vt is 0 or 1) // VT_EMPTY / VT_NULL
            {
                return null;
            }

            if (WbemCimType.IsArray(cimType))
            {
                return ConvertArray(variant, WbemCimType.BaseType(cimType));
            }

            int baseType = WbemCimType.BaseType(cimType);
            return baseType == WbemCimType.CIM_OBJECT
                ? WrapEmbedded(variant.RawValue)
                : ConvertScalar(variant, baseType);
        }
        finally
        {
            variant.Clear();
        }
    }

    /// <summary>
    /// Sets a property to <paramref name="value"/>, encoded per <paramref name="type"/> — the write
    /// equivalent of <c>CimProperty.Create(name, value, cimType, …)</c> / <c>CimInstanceProperties.Add</c>.
    /// For a spawned instance of an existing class every property (and its key qualifier) is already
    /// declared, so the VARIANT is Put with type 0 and WMI coerces it to the property's declared CIMTYPE.
    /// </summary>
    internal void SetProperty(string name, object? value, WbemType type)
    {
        // A null value clears the property to WMI NULL (VT_NULL) — matching CimProperty.Value = null (e.g.
        // clearing TunnelType on a non-tunnel connection-security rule); the encoded WbemType is irrelevant.
        Variant variant = value is null ? Variant.Null : BuildVariant(value, type);
        try
        {
            Put(name, variant);
        }
        finally
        {
            variant.Clear();
        }
    }

    /// <summary>
    /// Sets an existing property's value, inferring the VARIANT encoding from the CLR runtime type — the
    /// equivalent of <c>CimInstanceProperties[name].Value = value</c>. Used where the old code mutated an
    /// already-present property (e.g. re-assigning an embedded <c>Proposals</c> array).
    /// </summary>
    internal void SetProperty(string name, object? value)
        => SetProperty(name, value, InferType(value));

    /// <summary>
    /// Sets <paramref name="name"/> to <paramref name="value"/> only if the property exists on this object,
    /// returning whether it did — the equivalent of the old "<c>if (CimInstanceProperties[name] is CimProperty p) p.Value = …</c>"
    /// guard (used for optional properties like <c>TunnelType</c>/<c>OverrideBlockRules</c>).
    /// </summary>
    internal bool TrySetProperty(string name, object? value)
    {
        if (!HasProperty(name))
        {
            return false;
        }

        SetProperty(name, value);
        return true;
    }

    /// <summary>Whether the named property exists on this object (a non-throwing <c>Get</c> that succeeds even for a NULL value).</summary>
    internal bool HasProperty(string name)
    {
        Variant variant = default;
        int cimType = 0;
        HRESULT hr;
        fixed (char* pName = name)
        {
            hr = RawGet(_object, (PCWSTR)pName, (VARIANT*)&variant, &cimType);
        }

        variant.Clear();
        return hr.Succeeded;
    }

    private static WbemType InferType(object? value) => value switch
    {
        null or string => WbemType.String,
        bool => WbemType.Boolean,
        ushort => WbemType.UInt16,
        short => WbemType.SInt16,
        uint => WbemType.UInt32,
        int => WbemType.SInt32,
        ulong => WbemType.UInt64,
        long => WbemType.SInt64,
        string[] => WbemType.StringArray,
        WbemObject[] => WbemType.InstanceArray,
        _ => WbemType.String
    };

    private static Variant BuildVariant(object? value, WbemType type)
    {
        switch (type)
        {
            case WbemType.String:
                return value is null ? Variant.Empty : Variant.FromString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            case WbemType.Boolean:
                return Variant.FromBool(value is bool b && b);
            // CIM uint16/uint32/sint16/sint32 are Put as VT_I4 (WMI rejects VT_UI2/VT_UI4 with
            // WBEM_E_TYPE_MISMATCH — the same encoding rule as the WmiLight method-parameter work).
            case WbemType.UInt16:
            case WbemType.SInt16:
            case WbemType.UInt32:
            case WbemType.SInt32:
                return Variant.FromInt32(unchecked((int)Convert.ToInt64(value ?? 0, CultureInfo.InvariantCulture)));
            // 64-bit CIM integers are Put as decimal strings (VT_BSTR).
            case WbemType.UInt64:
            case WbemType.SInt64:
                return Variant.FromString(Convert.ToString(value ?? 0, CultureInfo.InvariantCulture) ?? "0");
            case WbemType.StringArray:
                return Variant.FromStringArray(value as string[]);
            case WbemType.InstanceArray:
                return BuildInstanceArray(value as WbemObject[]);
            default:
                return Variant.Empty;
        }
    }

    private static Variant BuildInstanceArray(WbemObject[]? items)
    {
        if (items is null || items.Length == 0)
        {
            return Variant.Empty;
        }

        Span<nint> pointers = items.Length <= 16 ? stackalloc nint[items.Length] : new nint[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            pointers[i] = (nint)items[i]._object;
        }
        return Variant.FromComInterfaceArray(pointers);
    }

    /// <summary>Non-throwing <c>IWbemClassObject::Put</c> (vtable slot 5); throws on a genuine failure HRESULT.</summary>
    private void Put(string name, in Variant value)
    {
        HRESULT hr;
        fixed (char* pName = name)
        fixed (Variant* pVal = &value)
        {
            void** vtbl = *(void***)_object;
            // Put(name, lFlags=0, VARIANT*, Type=0 -> use the class-declared CIMTYPE).
            hr = ((delegate* unmanaged[Stdcall]<IWbemClassObject*, PCWSTR, int, VARIANT*, int, HRESULT>)vtbl[5])(_object, (PCWSTR)pName, 0, (VARIANT*)pVal, 0);
        }
        hr.ThrowOnFailure();
    }

    /// <summary>Boxes a scalar VARIANT to the CLR type dictated by <paramref name="cimType"/>.</summary>
    private static object? ConvertScalar(in Variant variant, int cimType)
    {
        nint raw = variant.RawValue;
        return cimType switch
        {
            WbemCimType.CIM_BOOLEAN => (short)raw != 0,
            WbemCimType.CIM_UINT8 => (byte)raw,
            WbemCimType.CIM_SINT8 => (sbyte)raw,
            WbemCimType.CIM_UINT16 => (ushort)raw,
            WbemCimType.CIM_SINT16 => (short)raw,
            WbemCimType.CIM_UINT32 => (uint)raw,
            WbemCimType.CIM_SINT32 => (int)raw,
            WbemCimType.CIM_REAL32 => BitConverter.Int32BitsToSingle((int)raw),
            WbemCimType.CIM_REAL64 => BitConverter.Int64BitsToDouble(raw),
            WbemCimType.CIM_CHAR16 => (char)(ushort)raw,
            // WMI returns 64-bit integers as VT_BSTR; parse to the CimInstance CLR type.
            WbemCimType.CIM_UINT64 => ParseUInt64(raw),
            WbemCimType.CIM_SINT64 => ParseInt64(raw),
            // Strings, datetimes (DMTF string) and references all come back as BSTR.
            WbemCimType.CIM_STRING or WbemCimType.CIM_DATETIME or WbemCimType.CIM_REFERENCE
                => raw == 0 ? null : Marshal.PtrToStringBSTR(raw),
            // Unknown/illegal — fall back to the invariant scalar rendering.
            _ => variant.ToInvariantString()
        };
    }

    /// <summary>Boxes a raw integer array element to the CLR type its CIMTYPE base implies (matching CimInstance).</summary>
    private static object BoxNumeric(long raw, int cimBase) => cimBase switch
    {
        WbemCimType.CIM_BOOLEAN => raw != 0,
        WbemCimType.CIM_UINT8 => (byte)raw,
        WbemCimType.CIM_SINT8 => (sbyte)raw,
        WbemCimType.CIM_UINT16 => (ushort)raw,
        WbemCimType.CIM_SINT16 => (short)raw,
        WbemCimType.CIM_UINT32 => (uint)raw,
        WbemCimType.CIM_SINT32 => (int)raw,
        WbemCimType.CIM_UINT64 => (ulong)raw,
        WbemCimType.CIM_SINT64 => raw,
        WbemCimType.CIM_REAL32 => BitConverter.Int32BitsToSingle((int)raw),
        WbemCimType.CIM_REAL64 => BitConverter.Int64BitsToDouble(raw),
        WbemCimType.CIM_CHAR16 => (char)(ushort)raw,
        _ => raw
    };

    private static object ParseUInt64(nint bstr)
        => bstr != 0 && ulong.TryParse(Marshal.PtrToStringBSTR(bstr), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v) ? v : 0UL;

    private static object ParseInt64(nint bstr)
        => bstr != 0 && long.TryParse(Marshal.PtrToStringBSTR(bstr), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0L;

    /// <summary>
    /// Reads an array-typed VARIANT into the CLR array the old <c>CimInstance</c> produced: a
    /// <see cref="string"/>[] for string arrays, a <see cref="WbemObject"/>[] for embedded-object arrays
    /// (e.g. IKE <c>Proposals</c>), or an <see cref="object"/>[] of boxed scalars otherwise.
    /// </summary>
    private static object? ConvertArray(in Variant variant, int baseType)
    {
        nint psa = variant.RawValue;
        if (psa == 0 || SafeArrayGetLBound(psa, 1, out int lower) != 0 || SafeArrayGetUBound(psa, 1, out int upper) != 0)
        {
            return null;
        }

        int count = upper - lower + 1;
        if (count <= 0)
        {
            return baseType == WbemCimType.CIM_OBJECT ? Array.Empty<WbemObject>()
                 : baseType == WbemCimType.CIM_STRING ? Array.Empty<string>()
                 : Array.Empty<object>();
        }

        if (baseType == WbemCimType.CIM_OBJECT)
        {
            var result = new WbemObject[count];
            for (int i = lower, j = 0; i <= upper; i++, j++)
            {
                // SafeArrayGetElement AddRefs each IUnknown element into our copy; we own it.
                nint punk = 0;
                result[j] = SafeArrayGetElement(psa, in i, ref punk) == 0 && punk != 0
                    ? new WbemObject((IWbemClassObject*)punk)
                    : null!;
            }
            return result;
        }

        if (baseType == WbemCimType.CIM_STRING || baseType == WbemCimType.CIM_DATETIME || baseType == WbemCimType.CIM_REFERENCE)
        {
            var result = new string[count];
            for (int i = lower, j = 0; i <= upper; i++, j++)
            {
                nint bstr = 0;
                if (SafeArrayGetElement(psa, in i, ref bstr) == 0 && bstr != 0)
                {
                    result[j] = Marshal.PtrToStringBSTR(bstr);
                    Marshal.FreeBSTR(bstr);
                }
                else
                {
                    result[j] = string.Empty;
                }
            }
            return result;
        }

        // Numeric/boolean arrays: the SAFEARRAY holds fixed-size elements (e.g. VT_ARRAY|VT_I4 for a
        // CIM uint16[]), NOT 24-byte VARIANTs, so read the raw buffer by element width and box each value
        // to the CIMTYPE CLR type the old CimInstance produced.
        int elementVt = variant.VarType & 0x0FFF;
        int elementSize = elementVt switch
        {
            2 or 18 or 11 => 2,          // VT_I2 / VT_UI2 / VT_BOOL
            16 or 17 => 1,               // VT_I1 / VT_UI1
            20 or 21 or 5 => 8,          // VT_I8 / VT_UI8 / VT_R8
            _ => 4                        // VT_I4 / VT_UI4 / VT_R4 / VT_INT / VT_UINT
        };

        var objects = new object?[count];
        if (SafeArrayAccessData(psa, out nint pData) == 0 && pData != 0)
        {
            try
            {
                for (int j = 0; j < count; j++)
                {
                    long raw = elementSize switch
                    {
                        1 => Marshal.ReadByte(pData, j),
                        2 => Marshal.ReadInt16(pData, j * 2),
                        8 => Marshal.ReadInt64(pData, j * 8),
                        _ => Marshal.ReadInt32(pData, j * 4)
                    };
                    objects[j] = BoxNumeric(raw, baseType);
                }
            }
            finally
            {
                SafeArrayUnaccessData(psa);
            }
        }
        return objects;
    }

    /// <summary>Wraps the IUnknown carried by a scalar embedded-object VARIANT as a new owned <see cref="WbemObject"/>.</summary>
    private static WbemObject? WrapEmbedded(nint punk)
    {
        if (punk == 0)
        {
            return null;
        }

        // The VARIANT still owns its reference and will Release it on Clear(); take our own AddRef so the
        // returned wrapper's lifetime is independent.
        Marshal.AddRef(punk);
        return new WbemObject((IWbemClassObject*)punk);
    }

    public void Dispose()
    {
        // Suppress before releasing: while Dispose is executing the object is reachable, so the GC cannot be
        // finalizing it concurrently — this ordering makes the release race-free without locking.
        GC.SuppressFinalize(this);
        ReleaseOnce();
    }

    // Finalizer backstop: the WF services enumerate many WbemObjects through lazy LINQ pipelines and cannot
    // always dispose each one deterministically (mirroring the old CimInstance, which had a finalizer). The
    // underlying IWbemClassObject is a free-threaded client-side data object, so releasing it from the
    // finalizer thread is safe. Explicit Dispose remains the primary, prompt path.
    ~WbemObject() => ReleaseOnce();

    private void ReleaseOnce()
    {
        IWbemClassObject* obj = _object;
        if (obj is not null)
        {
            _object = null;
            Marshal.Release((nint)obj);
        }
    }

    /// <summary>
    /// Non-throwing <c>IWbemClassObject::Get</c> (vtable slot 4 — fixed by the COM contract: IUnknown 0–2,
    /// GetQualifierSet 3, Get 4). Called directly rather than via the throwing friendly wrapper so an
    /// absent property name degrades to <c>WBEM_E_NOT_FOUND</c> → <see langword="null"/> (matching the old
    /// <c>CimInstance</c> indexer) with no first-chance <see cref="COMException"/>.
    /// </summary>
    private static HRESULT RawGet(IWbemClassObject* obj, PCWSTR name, VARIANT* value, int* cimType)
    {
        void** vtbl = *(void***)obj;
        return ((delegate* unmanaged[Stdcall]<IWbemClassObject*, PCWSTR, int, VARIANT*, int*, int*, HRESULT>)vtbl[4])(obj, name, 0, value, cimType, null);
    }

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetLBound(nint psa, uint nDim, out int plLbound);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetUBound(nint psa, uint nDim, out int plUbound);

    [LibraryImport("oleaut32.dll", EntryPoint = "SafeArrayGetElement")]
    private static partial int SafeArrayGetElement(nint psa, in int rgIndices, ref nint pv);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayAccessData(nint psa, out nint ppvData);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayUnaccessData(nint psa);
}
