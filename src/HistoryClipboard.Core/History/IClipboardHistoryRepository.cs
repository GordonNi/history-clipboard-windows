namespace HistoryClipboard.Core.History;

public interface IClipboardHistoryRepository
{
    IReadOnlyList<ClipboardHistoryItem> Load();

    void InsertText(ClipboardHistoryItem item);

    ClipboardHistoryItem InsertImage(
        Guid itemId,
        DateTimeOffset createdAtUtc,
        ReadOnlyMemory<byte> pngBytes);

    void UpdatePin(Guid itemId, bool isPinned);

    void Delete(IReadOnlyCollection<Guid> itemIds);
}
