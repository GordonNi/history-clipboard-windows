using HistoryClipboard.Core.History;

namespace HistoryClipboard.Core.Capture;

public interface IClipboardHistoryWriter
{
    void Write(ClipboardHistoryItem item);
}
