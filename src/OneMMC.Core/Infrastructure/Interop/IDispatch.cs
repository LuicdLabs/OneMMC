using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OneMMC.Core.Infrastructure.Interop;

/// <summary>
/// Source-generated base for calling IDispatch <b>dual</b> COM interfaces through their vtable.
/// A dual interface's vtable is <c>IUnknown</c> (3 slots) + <c>IDispatch</c> (4 slots) + the
/// interface's own members. Deriving a <see cref="GeneratedComInterfaceAttribute"/> interface from
/// this base reproduces that layout so early-bound vtable calls land on the correct slots — the
/// AOT-safe replacement for CLR RCW dual-interface dispatch and for <c>dynamic</c>/IDispatch late
/// binding (doc/NativeAotMigration.md, M3).
/// </summary>
/// <remarks>
/// The four members below are vtable placeholders only; OneMMC calls the derived interfaces by
/// vtable and never invokes <c>IDispatch</c> itself, so the parameters are typed as opaque
/// <see cref="nint"/> to avoid pulling <c>ITypeInfo</c>/VARIANT marshalling into the generator.
/// </remarks>
[GeneratedComInterface]
[Guid("00020400-0000-0000-C000-000000000046")]
internal partial interface IDispatch
{
    /// <summary>Placeholder for <c>IDispatch::GetTypeInfoCount</c> (vtable slot 0).</summary>
    void GetTypeInfoCount(out uint pctinfo);

    /// <summary>Placeholder for <c>IDispatch::GetTypeInfo</c> (vtable slot 1).</summary>
    void GetTypeInfo(uint iTInfo, uint lcid, out nint ppTInfo);

    /// <summary>Placeholder for <c>IDispatch::GetIDsOfNames</c> (vtable slot 2).</summary>
    void GetIDsOfNames(in Guid riid, nint rgszNames, uint cNames, uint lcid, nint rgDispId);

    /// <summary>Placeholder for <c>IDispatch::Invoke</c> (vtable slot 3).</summary>
    void Invoke(int dispIdMember, in Guid riid, uint lcid, ushort wFlags, nint pDispParams, nint pVarResult, nint pExcepInfo, nint puArgErr);
}
