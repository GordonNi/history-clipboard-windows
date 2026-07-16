namespace HistoryClipboard.Core.Capture;

public enum ClipboardCaptureResult
{
    CapturedText,
    CapturedImage,
    NoSupportedContent,
    Duplicate,
    Unavailable,
    Paused,
}
