using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using HistoryClipboard.Core.Capture;
using HistoryClipboard.Core.History;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace HistoryClipboard.App.Windows;

public sealed class WpfClipboardHistoryWriter : IClipboardHistoryWriter
{
    public void Write(ClipboardHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            switch (item.Type)
            {
                case ClipboardItemType.Text:
                    WpfClipboard.SetText(item.TextContent, WpfTextDataFormat.UnicodeText);
                    break;
                case ClipboardItemType.Image:
                    WpfClipboard.SetImage(LoadImage(item));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported clipboard history item type '{item.Type}'.");
            }
        }
        catch (COMException exception)
        {
            throw new ClipboardUnavailableException(
                "Windows did not grant write access to the clipboard.",
                exception);
        }
    }

    private static BitmapFrame LoadImage(ClipboardHistoryItem item)
    {
        if (string.IsNullOrEmpty(item.ImagePath))
        {
            throw new InvalidOperationException("Image history is missing its PNG path.");
        }

        using var stream = File.OpenRead(item.ImagePath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames[0];
        bitmap.Freeze();
        return bitmap;
    }
}
