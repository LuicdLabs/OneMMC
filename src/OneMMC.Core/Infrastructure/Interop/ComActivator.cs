using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace OneMMC.Core.Infrastructure.Interop;

/// <summary>
/// AOT-safe activation of COM coclasses for source-generated (<see cref="GeneratedComInterfaceAttribute"/>)
/// interfaces. Replaces the reflection/built-in-COM idiom
/// <c>Type.GetTypeFromCLSID(clsid)</c> + <c>Activator.CreateInstance(type)</c>, which is unsupported
/// under Native AOT, with <c>CoCreateInstance</c> (CsWin32) + <see cref="ComWrappers"/>-based wrapping.
/// This is the repository's standard COM activation path; see <c>doc/NativeAot.md</c>,
/// "COM interop".
/// </summary>
internal static class ComActivator
{
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");

    /// <summary>
    /// Process-wide <see cref="ComWrappers"/> used to marshal source-generated COM interfaces. The
    /// strategy-based implementation resolves each interface's vtable from the interop source
    /// generator, so no runtime marshaller or reflection is required.
    /// </summary>
    internal static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>
    /// Creates the coclass identified by <paramref name="clsid"/> and returns its
    /// <typeparamref name="TInterface"/> view. The caller owns the returned reference and must release
    /// it (see <see cref="Release"/>) when done.
    /// </summary>
    /// <typeparam name="TInterface">A <see cref="GeneratedComInterfaceAttribute"/> interface implemented by the coclass.</typeparam>
    /// <param name="clsid">The class identifier of the coclass to activate.</param>
    /// <param name="context">The activation context (in-proc/local server); defaults to both.</param>
    /// <exception cref="COMException">Activation failed (e.g. the coclass is not registered).</exception>
    internal static unsafe TInterface CreateInstance<TInterface>(
        Guid clsid,
        CLSCTX context = CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_LOCAL_SERVER)
    {
        Guid iid = IID_IUnknown;
        void* pUnknown;
        PInvoke.CoCreateInstance(&clsid, null, context, &iid, &pUnknown).ThrowOnFailure();

        var unknownPtr = (nint)pUnknown;
        try
        {
            // UniqueInstance: each wrapper owns exactly one AddRef and is deterministically
            // releasable via IDisposable, matching the previous Marshal.ReleaseComObject discipline.
            object wrapper = ComWrappers.GetOrCreateObjectForComInstance(unknownPtr, CreateObjectFlags.UniqueInstance);
            return (TInterface)wrapper;
        }
        finally
        {
            Marshal.Release(unknownPtr);
        }
    }

    /// <summary>
    /// Releases a source-generated COM wrapper obtained from <see cref="CreateInstance{TInterface}"/> or
    /// returned from a COM call. Wrappers created by <see cref="ComWrappers"/> are
    /// <see cref="IDisposable"/>; managed objects and <see langword="null"/> are ignored. This is the
    /// ComWrappers-era replacement for <c>Marshal.ReleaseComObject</c> (which throws on such wrappers).
    /// </summary>
    internal static void Release(object? comWrapper)
    {
        if (comWrapper is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
