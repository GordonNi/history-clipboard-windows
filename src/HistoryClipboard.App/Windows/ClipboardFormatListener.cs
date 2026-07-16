using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HistoryClipboard.App.Windows;

public sealed partial class ClipboardFormatListener : IDisposable
{
    private const int ClipboardUpdateMessage = 0x031d;
    private readonly HwndSource windowSource;
    private bool isRegistered;

    public ClipboardFormatListener(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A created window handle is required.", nameof(windowHandle));
        }

        windowSource = HwndSource.FromHwnd(windowHandle) ??
            throw new InvalidOperationException("The WPF window source is unavailable.");
        windowSource.AddHook(WindowMessageHook);

        if (!NativeMethods.AddClipboardFormatListener(windowHandle))
        {
            windowSource.RemoveHook(WindowMessageHook);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        isRegistered = true;
    }

    public event EventHandler? ClipboardChanged;

    public void Dispose()
    {
        if (!isRegistered)
        {
            return;
        }

        isRegistered = false;
        NativeMethods.RemoveClipboardFormatListener(windowSource.Handle);
        windowSource.RemoveHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == ClipboardUpdateMessage)
        {
            handled = true;
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AddClipboardFormatListener(IntPtr windowHandle);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RemoveClipboardFormatListener(IntPtr windowHandle);
    }
}
