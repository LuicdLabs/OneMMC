namespace OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;

/// <summary>
/// The property value kinds the WF write path sets — the <c>Microsoft.Management.Infrastructure.CimType</c>
/// subset used by <c>CimProperty.Create</c> across the firewall services. Each maps to a VARIANT encoding
/// WMI accepts for that CIMTYPE on <c>IWbemClassObject::Put</c> (notably CIM uint16/uint32 → <c>VT_I4</c>
/// and uint64 → <c>VT_BSTR</c> — the same encoding lesson as the WmiLight method-parameter work).
/// </summary>
internal enum WbemType
{
    String,
    Boolean,
    UInt16,
    SInt16,
    UInt32,
    SInt32,
    UInt64,
    SInt64,
    StringArray,
    /// <summary>An array of embedded objects (<c>CIM_OBJECT | CIM_FLAG_ARRAY</c>), e.g. IKE Proposals.</summary>
    InstanceArray
}

/// <summary>
/// CIMTYPE_ENUMERATION values (wbemcli.h) and the WMI flag constants used by the marshal-free
/// <c>IWbemServices</c>/<c>IWbemClassObject</c> layer. These are not projected by CsWin32 (they live in
/// <c>wbemcli.h</c> as C macros, not a type library), so they are declared here.
/// <para>
/// A property's CIMTYPE is what <c>IWbemClassObject::Get</c> returns in its <c>pType</c> out-parameter and
/// is authoritative for boxing a returned VARIANT to the CLR type the old
/// <c>Microsoft.Management.Infrastructure</c> (<c>CimInstance</c>) layer produced — WMI stores both
/// <c>CIM_UINT16</c> and <c>CIM_UINT32</c> as <c>VT_I4</c>, so the VARTYPE alone cannot distinguish
/// <see cref="ushort"/> from <see cref="uint"/>.
/// </para>
/// </summary>
internal static class WbemCimType
{
    // CIMTYPE_ENUMERATION (wbemcli.h)
    internal const int CIM_ILLEGAL = 0xFFF;
    internal const int CIM_EMPTY = 0;
    internal const int CIM_SINT8 = 16;
    internal const int CIM_UINT8 = 17;
    internal const int CIM_SINT16 = 2;
    internal const int CIM_UINT16 = 18;
    internal const int CIM_SINT32 = 3;
    internal const int CIM_UINT32 = 19;
    internal const int CIM_SINT64 = 20;
    internal const int CIM_UINT64 = 21;
    internal const int CIM_REAL32 = 4;
    internal const int CIM_REAL64 = 5;
    internal const int CIM_BOOLEAN = 11;
    internal const int CIM_STRING = 8;
    internal const int CIM_DATETIME = 101;
    internal const int CIM_REFERENCE = 102;
    internal const int CIM_CHAR16 = 103;
    internal const int CIM_OBJECT = 13;

    /// <summary>OR-ed into a CIMTYPE to indicate an array property (<c>CIM_FLAG_ARRAY</c>).</summary>
    internal const int CIM_FLAG_ARRAY = 0x2000;

    /// <summary>The base CIMTYPE with the <see cref="CIM_FLAG_ARRAY"/> bit masked off.</summary>
    internal static int BaseType(int cimType) => cimType & ~CIM_FLAG_ARRAY;

    /// <summary>Whether the CIMTYPE has the array flag set.</summary>
    internal static bool IsArray(int cimType) => (cimType & CIM_FLAG_ARRAY) != 0;

    // Enumeration/query flags (WBEM_GENERIC_FLAG_TYPE) — forward-only, semi-synchronous.
    internal const int WBEM_FLAG_RETURN_IMMEDIATELY = 0x00000010;
    internal const int WBEM_FLAG_FORWARD_ONLY = 0x00000020;

    // Timeout sentinels for IEnumWbemClassObject::Next (WBEM_TIMEOUT_TYPE).
    internal const int WBEM_INFINITE = unchecked((int)0xFFFFFFFF);

    // IWbemClassObject::SpawnInstance / PutInstance change flags (WBEM_CHANGE_FLAG_TYPE).
    internal const int WBEM_FLAG_CREATE_OR_UPDATE = 0x00000000;
    internal const int WBEM_FLAG_UPDATE_ONLY = 0x00000001;
    internal const int WBEM_FLAG_CREATE_ONLY = 0x00000002;

    // Well-known status codes.
    internal const int WBEM_S_NO_ERROR = 0x00000000;
    internal const int WBEM_S_FALSE = 0x00040001;
    internal const int WBEM_E_NOT_FOUND = unchecked((int)0x80041002);
}
