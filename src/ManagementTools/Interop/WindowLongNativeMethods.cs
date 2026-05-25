using System;
using System.Runtime.InteropServices;
namespace ManagementTools.Interop;

/// <summary>
/// Provides a small wrapper for window-long APIs that CsWin32 does not currently emit
/// for this project configuration. The wrapper binds the native export at runtime and
/// falls back to <c>SetWindowLongW</c> on 32-bit targets.
/// </summary>
internal static class WindowLongNativeMethods
{
    private const int GwlpHwndParent = -8;

    private static readonly nint User32Module = NativeLibrary.Load("user32.dll");
    private static readonly SetWindowLongPtrDelegate? SetWindowLongPtr = BindSetWindowLongPtr();
    private static readonly SetWindowLongDelegate? SetWindowLong = BindSetWindowLong();

    private delegate nint SetWindowLongPtrDelegate(nint hWnd, int nIndex, nint dwNewLong);
    private delegate int SetWindowLongDelegate(nint hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Sets the Win32 owner HWND of a top-level window.
    /// </summary>
    /// <param name="windowHandle">The dialog window handle.</param>
    /// <param name="ownerHandle">The owner window handle.</param>
    internal static void SetOwnerWindow(nint windowHandle, nint ownerHandle)
    {
        if (IntPtr.Size == 4)
        {
            if (SetWindowLong is null)
            {
                throw new InvalidOperationException("SetWindowLongW is unavailable on this platform.");
            }

            _ = SetWindowLong(windowHandle, GwlpHwndParent, unchecked((int)ownerHandle));
            return;
        }

        if (SetWindowLongPtr is null)
        {
            throw new InvalidOperationException("SetWindowLongPtrW is unavailable on this platform.");
        }

        _ = SetWindowLongPtr(windowHandle, GwlpHwndParent, ownerHandle);
    }

    private static SetWindowLongPtrDelegate? BindSetWindowLongPtr()
    {
        return NativeLibrary.TryGetExport(User32Module, "SetWindowLongPtrW", out IntPtr export)
            ? Marshal.GetDelegateForFunctionPointer<SetWindowLongPtrDelegate>(export)
            : null;
    }

    private static SetWindowLongDelegate? BindSetWindowLong()
    {
        return NativeLibrary.TryGetExport(User32Module, "SetWindowLongW", out IntPtr export)
            ? Marshal.GetDelegateForFunctionPointer<SetWindowLongDelegate>(export)
            : null;
    }
}
