namespace HistoryClipboard.Core.History;

public sealed class ClipboardHistoryItem
{
    internal ClipboardHistoryItem(
        Guid id,
        ClipboardItemType type,
        DateTimeOffset createdAtUtc,
        bool isPinned,
        string textContent,
        string? imagePath)
    {
        Id = id;
        Type = type;
        CreatedAtUtc = createdAtUtc;
        IsPinned = isPinned;
        TextContent = textContent;
        ImagePath = imagePath;
    }

    public Guid Id { get; }

    public ClipboardItemType Type { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public bool IsPinned { get; internal set; }

    public string TextContent { get; }

    public string? ImagePath { get; }

    public static ClipboardHistoryItem Restore(
        Guid id,
        ClipboardItemType type,
        DateTimeOffset createdAtUtc,
        bool isPinned,
        string textContent,
        string? imagePath)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A persisted clipboard item must have an ID.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(textContent);

        return new ClipboardHistoryItem(
            id,
            type,
            createdAtUtc.ToUniversalTime(),
            isPinned,
            textContent,
            imagePath);
    }
}
