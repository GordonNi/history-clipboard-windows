namespace HistoryClipboard.Core.Capture;

public sealed class ClipboardCaptureOptions
{
    public int MaxReadAttempts { get; init; } = 5;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(50);
}
