using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using HistoryClipboard.Core.Capture;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace HistoryClipboard.App.Windows;

public sealed class WpfClipboardContentReader : IClipboardContentReader
{
    public ClipboardContent? Read()
    {
        try
        {
            if (WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText))
            {
                return ClipboardContent.FromText(
                    WpfClipboard.GetText(WpfTextDataFormat.UnicodeText));
            }

            if (!WpfClipboard.ContainsImage())
            {
                return null;
            }

            var bitmap = WpfClipboard.GetImage();
            if (bitmap is null)
            {
                return null;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return ClipboardContent.FromPngImage(stream.ToArray());
        }
        catch (COMException exception)
        {
            throw new ClipboardUnavailableException(
                "Windows did not grant access to the clipboard.",
                exception);
        }
    }
}
