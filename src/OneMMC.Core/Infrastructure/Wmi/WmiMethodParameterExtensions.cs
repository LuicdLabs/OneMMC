using System.Globalization;
using WmiLight;

namespace OneMMC.Core.Infrastructure.Wmi;

/// <summary>
/// Sets WMI method in-parameters using the VARIANT types the WMI provider actually accepts.
/// <para>
/// <c>IWbemClassObject::Put</c> does not take the VARIANT type matching the CIM type for
/// unsigned/wide values — the documented mapping is CIM uint16/uint32 → <c>VT_I4</c>,
/// CIM uint64/sint64 → <c>VT_BSTR</c>, and CIM char16 → <c>VT_I2</c>. WmiLight's typed
/// <c>SetPropertyValue</c> overloads encode e.g. <see langword="ushort"/> as <c>VT_UI2</c>,
/// which strict providers (verified: the Storage Management provider) reject with
/// <c>WBEM_E_TYPE_MISMATCH</c> (0x80041005). Route method parameters through these helpers
/// instead of the raw overloads.
/// </para>
/// </summary>
internal static class WmiMethodParameterExtensions
{
    /// <summary>Sets a CIM <c>uint16</c> method parameter (marshalled as <c>VT_I4</c>).</summary>
    public static void SetUInt16Parameter(this WmiMethodParameters parameters, string name, ushort value)
        => parameters.SetPropertyValue(name, (int)value);

    /// <summary>Sets a CIM <c>uint32</c> method parameter (marshalled as <c>VT_I4</c>).</summary>
    public static void SetUInt32Parameter(this WmiMethodParameters parameters, string name, uint value)
        => parameters.SetPropertyValue(name, unchecked((int)value));

    /// <summary>Sets a CIM <c>uint64</c> method parameter (marshalled as <c>VT_BSTR</c>).</summary>
    public static void SetUInt64Parameter(this WmiMethodParameters parameters, string name, ulong value)
        => parameters.SetPropertyValue(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Sets a CIM <c>char16</c> method parameter (marshalled as <c>VT_I2</c>).</summary>
    public static void SetChar16Parameter(this WmiMethodParameters parameters, string name, char value)
        => parameters.SetPropertyValue(name, (short)value);
}
