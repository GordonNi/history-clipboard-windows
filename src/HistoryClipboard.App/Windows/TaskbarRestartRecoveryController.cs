using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HistoryClipboard.App.Windows;

public sealed partial class TaskbarRestartRecoveryController : IDisposable
{
    private const string TaskbarCreatedMessageName = "TaskbarCreated";

    private readonly Action<HwndSourceHook> removeHook;
    private readonly Action republishTrayIcon;
    private readonly int taskbarCreatedMessage;
    private readonly HwndSourceHook windowMessageHook;
    private bool isDisposed;

    public TaskbarRestartRecoveryController(IntPtr windowHandle, Action republishTrayIcon)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A created window handle is required.", nameof(windowHandle));
        }

        ArgumentNullException.ThrowIfNull(republishTrayIcon);

        var windowSource = HwndSource.FromHwnd(windowHandle) ??
            throw new InvalidOperationException("The WPF window source is unavailable.");
        var registeredMessage = NativeMethods.RegisterWindowMessage(TaskbarCreatedMessageName);
        if (registeredMessage == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        taskbarCreatedMessage = unchecked((int)registeredMessage);
        this.republishTrayIcon = republishTrayIcon;
        removeHook = windowSource.RemoveHook;
        windowMessageHook = WindowMessageHook;
        windowSource.AddHook(windowMessageHook);
    }

    internal TaskbarRestartRecoveryController(
        int taskbarCreatedMessage,
        Action<HwndSourceHook> addHook,
        Action<HwndSourceHook> removeHook,
        Action republishTrayIcon)
    {
        ArgumentOutOfRangeException.ThrowIfZero(taskbarCreatedMessage);
        ArgumentNullException.ThrowIfNull(addHook);
        ArgumentNullException.ThrowIfNull(removeHook);
        ArgumentNullException.ThrowIfNull(republishTrayIcon);

        this.taskbarCreatedMessage = taskbarCreatedMessage;
        this.removeHook = removeHook;
        this.republishTrayIcon = republishTrayIcon;
        windowMessageHook = WindowMessageHook;
        addHook(windowMessageHook);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        removeHook(windowMessageHook);
        GC.SuppressFinalize(this);
    }

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (!isDisposed && message == taskbarCreatedMessage)
        {
            republishTrayIcon();
        }

        return IntPtr.Zero;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint RegisterWindowMessage(string messageName);
    }
}
