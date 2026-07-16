namespace HistoryClipboard.Core.Capture;

public sealed class ClipboardContent
{
    private ClipboardContent(
        ClipboardContentType type,
        string? text,
        ReadOnlyMemory<byte> pngBytes)
    {
        Type = type;
        Text = text;
        PngBytes = pngBytes;
    }

    public ClipboardContentType Type { get; }

    public string? Text { get; }

    public ReadOnlyMemory<byte> PngBytes { get; }

    public static ClipboardContent FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ClipboardContent(ClipboardContentType.Text, text, ReadOnlyMemory<byte>.Empty);
    }

    public static ClipboardContent FromPngImage(ReadOnlyMemory<byte> pngBytes)
    {
        if (pngBytes.IsEmpty)
        {
            throw new ArgumentException("PNG content cannot be empty.", nameof(pngBytes));
        }

        return new ClipboardContent(ClipboardContentType.PngImage, null, pngBytes);
    }
}
