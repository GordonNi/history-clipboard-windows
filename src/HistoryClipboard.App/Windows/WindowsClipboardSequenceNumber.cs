using System.Runtime.InteropServices;
using HistoryClipboard.Core.Capture;

namespace HistoryClipboard.App.Windows;

public sealed partial class WindowsClipboardSequenceNumber : IClipboardSequenceNumber
{
    public uint GetCurrent()
    {
        return NativeMethods.GetClipboardSequenceNumber();
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial uint GetClipboardSequenceNumber();
    }
}
