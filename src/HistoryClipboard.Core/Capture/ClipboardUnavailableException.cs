namespace HistoryClipboard.Core.Capture;

public sealed class ClipboardUnavailableException : Exception
{
    public ClipboardUnavailableException()
        : base("The system clipboard is temporarily unavailable.")
    {
    }

    public ClipboardUnavailableException(string message)
        : base(message)
    {
    }

    public ClipboardUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
