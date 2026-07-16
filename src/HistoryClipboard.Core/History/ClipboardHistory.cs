using System.Collections.ObjectModel;

namespace HistoryClipboard.Core.History;

public sealed class ClipboardHistory
{
    private readonly List<ClipboardHistoryItem> items = [];
    private readonly ReadOnlyCollection<ClipboardHistoryItem> readOnlyItems;
    private readonly IClipboardHistoryRepository? repository;
    private readonly TimeProvider timeProvider;

    public ClipboardHistory(
        TimeProvider? timeProvider = null,
        IClipboardHistoryRepository? repository = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.repository = repository;
        items.AddRange(repository?.Load() ?? []);
        SortItems();
        readOnlyItems = items.AsReadOnly();
    }

    public IReadOnlyList<ClipboardHistoryItem> Items => readOnlyItems;

    public ClipboardHistoryItem? AddText(string text)
    {
        var latestItem = items.MaxBy(static item => item.CreatedAtUtc);
        if (text.Length == 0 ||
            (latestItem?.Type == ClipboardItemType.Text && latestItem.TextContent == text))
        {
            return null;
        }

        var item = new ClipboardHistoryItem(
            Guid.NewGuid(),
            ClipboardItemType.Text,
            timeProvider.GetUtcNow(),
            false,
            text,
            null);

        repository?.InsertText(item);
        items.Add(item);
        SortItems();
        return item;
    }

    public ClipboardHistoryItem? AddImage(ReadOnlyMemory<byte> pngBytes)
    {
        if (pngBytes.IsEmpty)
        {
            return null;
        }

        if (repository is null)
        {
            throw new InvalidOperationException(
                "Image history requires a configured persistence repository.");
        }

        var item = repository.InsertImage(
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            pngBytes);
        items.Add(item);
        SortItems();
        return item;
    }

    public ClipboardHistoryItem? TogglePin(Guid itemId)
    {
        var item = FindItem(itemId);
        if (item is null)
        {
            return null;
        }

        var isPinned = !item.IsPinned;
        repository?.UpdatePin(item.Id, isPinned);
        item.IsPinned = isPinned;
        SortItems();
        return item;
    }

    public ClipboardHistoryItem? Delete(Guid itemId)
    {
        var item = FindItem(itemId);
        if (item is null)
        {
            return null;
        }

        repository?.Delete([item.Id]);
        items.Remove(item);
        return item;
    }

    public IReadOnlyList<ClipboardHistoryItem> Clear()
    {
        var removed = items.ToArray();
        repository?.Delete(removed.Select(static item => item.Id).ToArray());
        items.Clear();
        return removed;
    }

    public ClipboardHistoryQueryResult Query(string query)
    {
        var normalizedQuery = query.Trim();
        IReadOnlyList<ClipboardHistoryItem> matches = normalizedQuery.Length == 0
            ? readOnlyItems
            : items
                .Where(item =>
                    item.Type == ClipboardItemType.Text &&
                    item.TextContent.Contains(
                        normalizedQuery,
                        StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        return new ClipboardHistoryQueryResult(
            matches,
            items.Count(static item => item.Type == ClipboardItemType.Text),
            items.Count(static item => item.Type == ClipboardItemType.Image));
    }

    public IReadOnlyList<ClipboardHistoryItem> RemoveExpired(RetentionPeriod retentionPeriod)
    {
        var cutoff = timeProvider.GetUtcNow() - TimeSpan.FromDays((int)retentionPeriod);
        var removed = items
            .Where(item => !item.IsPinned && item.CreatedAtUtc < cutoff)
            .ToArray();

        repository?.Delete(removed.Select(static item => item.Id).ToArray());
        foreach (var item in removed)
        {
            items.Remove(item);
        }

        return removed;
    }

    private ClipboardHistoryItem? FindItem(Guid itemId)
    {
        return items.FirstOrDefault(candidate => candidate.Id == itemId);
    }

    private void SortItems()
    {
        items.Sort(static (left, right) =>
        {
            if (left.IsPinned != right.IsPinned)
            {
                return left.IsPinned ? -1 : 1;
            }

            return right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
        });
    }
}
