namespace HistoryClipboard.Core.Capture;

public sealed class ClipboardCopyOptions
{
    public int MaxWriteAttempts { get; init; } = 5;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(50);
}
