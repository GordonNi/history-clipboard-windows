namespace HistoryClipboard.Core.History;

public sealed class ClipboardHistoryQueryResult
{
    internal ClipboardHistoryQueryResult(
        IReadOnlyList<ClipboardHistoryItem> items,
        int totalTextCount,
        int totalImageCount)
    {
        Items = items;
        TotalTextCount = totalTextCount;
        TotalImageCount = totalImageCount;
    }

    public IReadOnlyList<ClipboardHistoryItem> Items { get; }

    public int TotalTextCount { get; }

    public int TotalImageCount { get; }
}
