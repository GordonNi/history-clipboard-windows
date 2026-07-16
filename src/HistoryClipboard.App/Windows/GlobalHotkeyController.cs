using System.Runtime.InteropServices;
using System.Windows.Interop;
using HistoryClipboard.Infrastructure.Settings;

namespace HistoryClipboard.App.Windows;

public sealed partial class GlobalHotkeyController : IDisposable
{
    private const int HotkeyId = 0x4843;
    private const int HotkeyMessage = 0x0312;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeyH = 0x48;
    private const uint VirtualKeyV = 0x56;

    private readonly Action hotkeyPressed;
    private readonly HwndSource windowSource;
    private bool isDisposed;
    private bool isRegistered;

    public GlobalHotkeyController(IntPtr windowHandle, Action hotkeyPressed)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A created window handle is required.", nameof(windowHandle));
        }

        ArgumentNullException.ThrowIfNull(hotkeyPressed);

        windowSource = HwndSource.FromHwnd(windowHandle) ??
            throw new InvalidOperationException("The WPF window source is unavailable.");
        this.hotkeyPressed = hotkeyPressed;
        windowSource.AddHook(WindowMessageHook);
    }

    public GlobalHotkeyRegistrationResult SetHotkey(GlobalHotkeyOption option)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (!Enum.IsDefined(option))
        {
            throw new ArgumentOutOfRangeException(nameof(option));
        }

        UnregisterCurrent();
        if (option == GlobalHotkeyOption.Disabled)
        {
            return GlobalHotkeyRegistrationResult.Disabled;
        }

        var (modifiers, virtualKey) = Resolve(option);
        isRegistered = NativeMethods.RegisterHotKey(
            windowSource.Handle,
            HotkeyId,
            modifiers | ModifierNoRepeat,
            virtualKey);
        return isRegistered
            ? GlobalHotkeyRegistrationResult.Registered
            : GlobalHotkeyRegistrationResult.Unavailable;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        UnregisterCurrent();
        windowSource.RemoveHook(WindowMessageHook);
        GC.SuppressFinalize(this);
    }

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == HotkeyMessage && wParam == HotkeyId)
        {
            handled = true;
            hotkeyPressed();
        }

        return IntPtr.Zero;
    }

    private void UnregisterCurrent()
    {
        if (!isRegistered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(windowSource.Handle, HotkeyId);
        isRegistered = false;
    }

    private static (uint Modifiers, uint VirtualKey) Resolve(GlobalHotkeyOption option)
    {
        return option switch
        {
            GlobalHotkeyOption.ControlShiftV => (ModifierControl | ModifierShift, VirtualKeyV),
            GlobalHotkeyOption.ControlAltV => (ModifierControl | ModifierAlt, VirtualKeyV),
            GlobalHotkeyOption.ControlShiftH => (ModifierControl | ModifierShift, VirtualKeyH),
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(
            IntPtr windowHandle,
            int hotkeyId,
            uint modifiers,
            uint virtualKey);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(IntPtr windowHandle, int hotkeyId);
    }
}

public enum GlobalHotkeyRegistrationResult
{
    Registered,
    Disabled,
    Unavailable,
}
