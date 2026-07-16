using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HistoryClipboard.App.Windows;

internal readonly record struct PhysicalRectangle(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

internal readonly record struct PhysicalWindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

internal readonly record struct WindowPlacementTarget(
    PhysicalRectangle WorkArea,
    uint DpiX,
    uint DpiY);

internal static class WindowPlacementGeometry
{
    private const double DefaultDpi = 96d;

    public static PhysicalWindowBounds PlaceBottomRight(
        PhysicalRectangle workArea,
        uint dpiX,
        uint dpiY,
        double widthDip,
        double heightDip,
        double marginDip)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        ArgumentOutOfRangeException.ThrowIfZero(dpiX);
        ArgumentOutOfRangeException.ThrowIfZero(dpiY);

        ValidateDip(widthDip, nameof(widthDip), allowZero: false);
        ValidateDip(heightDip, nameof(heightDip), allowZero: false);
        ValidateDip(marginDip, nameof(marginDip), allowZero: true);

        var requestedWidth = DipToPixels(widthDip, dpiX);
        var requestedHeight = DipToPixels(heightDip, dpiY);
        var horizontalMargin = DipToPixels(marginDip, dpiX);
        var verticalMargin = DipToPixels(marginDip, dpiY);
        var width = Math.Min(requestedWidth, workArea.Width);
        var height = Math.Min(requestedHeight, workArea.Height);
        var left = Math.Clamp(
            workArea.Right - width - horizontalMargin,
            workArea.Left,
            workArea.Right - width);
        var top = Math.Clamp(
            workArea.Bottom - height - verticalMargin,
            workArea.Top,
            workArea.Bottom - height);

        return new PhysicalWindowBounds(left, top, width, height);
    }

    private static int DipToPixels(double dip, uint dpi)
    {
        return checked((int)Math.Round(
            dip * dpi / DefaultDpi,
            MidpointRounding.AwayFromZero));
    }

    private static void ValidateDip(double value, string parameterName, bool allowZero)
    {
        if (!double.IsFinite(value) || value < 0 || (!allowZero && value == 0))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

internal static partial class WindowPlacementController
{
    private const uint DefaultToNearestMonitor = 0x00000002;
    private const uint DefaultToPrimaryMonitor = 0x00000001;
    private const uint EffectiveDpi = 0;
    private const uint NoActivate = 0x0010;
    private const uint NoOwnerZOrder = 0x0200;
    private const uint NoZOrder = 0x0004;

    public static WindowPlacementTarget SelectTarget(IntPtr applicationWindow)
    {
        var monitor = SelectMonitor(applicationWindow);
        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var dpiX = 96u;
        var dpiY = 96u;
        if (GetDpiForMonitor(monitor, EffectiveDpi, out var targetDpiX, out var targetDpiY) == 0
            && targetDpiX > 0
            && targetDpiY > 0)
        {
            dpiX = targetDpiX;
            dpiY = targetDpiY;
        }

        return new WindowPlacementTarget(
            new PhysicalRectangle(
                monitorInfo.WorkArea.Left,
                monitorInfo.WorkArea.Top,
                monitorInfo.WorkArea.Right,
                monitorInfo.WorkArea.Bottom),
            dpiX,
            dpiY);
    }

    public static void PlaceWindow(
        IntPtr applicationWindow,
        WindowPlacementTarget target,
        double widthDip,
        double heightDip,
        double marginDip)
    {
        if (applicationWindow == IntPtr.Zero)
        {
            throw new ArgumentException("A valid application window handle is required.",
                nameof(applicationWindow));
        }

        var placement = WindowPlacementGeometry.PlaceBottomRight(
            target.WorkArea,
            target.DpiX,
            target.DpiY,
            widthDip,
            heightDip,
            marginDip);
        if (!SetWindowPos(
            applicationWindow,
            IntPtr.Zero,
            placement.Left,
            placement.Top,
            placement.Width,
            placement.Height,
            NoActivate | NoOwnerZOrder | NoZOrder))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static IntPtr SelectMonitor(IntPtr applicationWindow)
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero)
        {
            var foregroundMonitor = MonitorFromWindow(
                foregroundWindow,
                DefaultToNearestMonitor);
            if (foregroundMonitor != IntPtr.Zero)
            {
                return foregroundMonitor;
            }
        }

        if (GetCursorPos(out var cursorPosition))
        {
            var cursorMonitor = MonitorFromPoint(cursorPosition, DefaultToNearestMonitor);
            if (cursorMonitor != IntPtr.Zero)
            {
                return cursorMonitor;
            }
        }

        if (applicationWindow != IntPtr.Zero)
        {
            var nearestApplicationMonitor = MonitorFromWindow(
                applicationWindow,
                DefaultToNearestMonitor);
            if (nearestApplicationMonitor != IntPtr.Zero)
            {
                return nearestApplicationMonitor;
            }
        }

        var primaryMonitor = MonitorFromPoint(default, DefaultToPrimaryMonitor);
        return primaryMonitor != IntPtr.Zero
            ? primaryMonitor
            : throw new InvalidOperationException("Windows did not provide a target monitor.");
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(
        IntPtr monitor,
        uint dpiType,
        out uint dpiX,
        out uint dpiY);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }
}
