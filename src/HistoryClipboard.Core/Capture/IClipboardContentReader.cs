namespace HistoryClipboard.Core.Capture;

public interface IClipboardContentReader
{
    ClipboardContent? Read();
}
