using HistoryClipboard.Core.History;
using HistoryClipboard.Infrastructure.Persistence;

namespace HistoryClipboard.Tests;

public sealed class ClipboardHistoryTests
{
    private static readonly byte[] SamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void NewerTextAppearsBeforeOlderText()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);

        history.AddText("first");
        time.Advance(TimeSpan.FromSeconds(1));
        history.AddText("second");

        Assert.Collection(
            history.Items,
            item => Assert.Equal("second", item.TextContent),
            item => Assert.Equal("first", item.TextContent));
    }

    [Fact]
    public void ConsecutiveDuplicateTextIsIgnored()
    {
        var history = new ClipboardHistory();

        var original = history.AddText("same text");
        var duplicate = history.AddText("same text");

        Assert.NotNull(original);
        Assert.Null(duplicate);
        Assert.Single(history.Items);
    }

    [Fact]
    public void ConsecutiveDuplicateTextIsIgnoredWhenOlderItemIsPinnedFirst()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);
        var older = Assert.IsType<ClipboardHistoryItem>(history.AddText("older pinned"));
        time.Advance(TimeSpan.FromSeconds(1));
        history.AddText("latest text");
        history.TogglePin(older.Id);

        var duplicate = history.AddText("latest text");

        Assert.Null(duplicate);
        Assert.Equal(2, history.Items.Count);
    }

    [Fact]
    public void EmptyTextIsIgnored()
    {
        var history = new ClipboardHistory();

        var result = history.AddText(string.Empty);

        Assert.Null(result);
        Assert.Empty(history.Items);
    }

    [Fact]
    public void PinnedTextAppearsBeforeNewerUnpinnedText()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);
        var older = Assert.IsType<ClipboardHistoryItem>(history.AddText("older"));
        time.Advance(TimeSpan.FromMinutes(1));
        history.AddText("newer");

        var updated = Assert.IsType<ClipboardHistoryItem>(history.TogglePin(older.Id));

        Assert.Same(older, updated);
        Assert.Collection(
            history.Items,
            item =>
            {
                Assert.Equal("older", item.TextContent);
                Assert.True(item.IsPinned);
            },
            item => Assert.Equal("newer", item.TextContent));
    }

    [Fact]
    public void TogglePinReturnsTheUpdatedHistoryItem()
    {
        var history = new ClipboardHistory();
        var item = Assert.IsType<ClipboardHistoryItem>(history.AddText("pin me"));

        var updated = Assert.IsType<ClipboardHistoryItem>(history.TogglePin(item.Id));

        Assert.Same(item, updated);
        Assert.True(updated.IsPinned);
    }

    [Fact]
    public void UnpinnedTextOlderThanRetentionIsRemoved()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);
        history.AddText("expired");
        time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1));
        history.AddText("current");

        var removed = history.RemoveExpired(RetentionPeriod.OneDay);

        Assert.Collection(removed, item => Assert.Equal("expired", item.TextContent));
        Assert.Collection(history.Items, item => Assert.Equal("current", item.TextContent));
    }

    [Fact]
    public void PinnedTextDoesNotExpire()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);
        var pinned = Assert.IsType<ClipboardHistoryItem>(history.AddText("keep me"));
        history.TogglePin(pinned.Id);
        time.Advance(TimeSpan.FromDays(6));

        var removed = history.RemoveExpired(RetentionPeriod.FiveDays);

        Assert.Empty(removed);
        Assert.Collection(history.Items, item => Assert.Equal("keep me", item.TextContent));
    }

    [Theory]
    [InlineData(RetentionPeriod.OneDay, 1)]
    [InlineData(RetentionPeriod.ThreeDays, 3)]
    [InlineData(RetentionPeriod.FiveDays, 5)]
    public void TextExactlyAtRetentionBoundaryIsKept(RetentionPeriod period, int days)
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);
        history.AddText("boundary");
        time.Advance(TimeSpan.FromDays(days));

        var removed = history.RemoveExpired(period);

        Assert.Empty(removed);
        Assert.Single(history.Items);
    }

    [Fact]
    public void DeleteRemovesOnlyTheSelectedItemAndReturnsIt()
    {
        var history = new ClipboardHistory();
        var selected = Assert.IsType<ClipboardHistoryItem>(history.AddText("delete me"));
        history.AddText("keep me");

        var removed = history.Delete(selected.Id);

        Assert.Same(selected, removed);
        Assert.Collection(history.Items, item => Assert.Equal("keep me", item.TextContent));
    }

    [Fact]
    public void ClearReturnsEveryRemovedItemAndLeavesHistoryEmpty()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);
        history.AddText("older");
        time.Advance(TimeSpan.FromSeconds(1));
        history.AddText("newer");

        var removed = history.Clear();

        Assert.Collection(
            removed,
            item => Assert.Equal("newer", item.TextContent),
            item => Assert.Equal("older", item.TextContent));
        Assert.Empty(history.Items);
    }

    [Fact]
    public void QueryReturnsTextMatchesAndCompleteHistoryTypeCounts()
    {
        using var database = new TemporaryDatabase();
        var history = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        history.AddText("needle text");
        history.AddText("unrelated text");
        history.AddImage(SamplePng);

        var result = history.Query("needle");

        Assert.Collection(
            result.Items,
            item => Assert.Equal("needle text", item.TextContent));
        Assert.Equal(2, result.TotalTextCount);
        Assert.Equal(1, result.TotalImageCount);
    }

    [Fact]
    public void SearchIgnoresCaseAndPreservesHistoryOrder()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(time);
        history.AddText("Alpha older");
        time.Advance(TimeSpan.FromSeconds(1));
        history.AddText("unrelated");
        time.Advance(TimeSpan.FromSeconds(1));
        history.AddText("ALPHA newer");

        var matches = history.Query("alpha").Items;

        Assert.Collection(
            matches,
            item => Assert.Equal("ALPHA newer", item.TextContent),
            item => Assert.Equal("Alpha older", item.TextContent));
    }

    [Fact]
    public void SearchTrimsSurroundingWhitespace()
    {
        var history = new ClipboardHistory();
        history.AddText("matching text");

        var matches = history.Query("  matching\r\n").Items;

        Assert.Single(matches);
    }

    [Fact]
    public void BlankSearchReturnsCompleteHistory()
    {
        var history = new ClipboardHistory();
        history.AddText("first");
        history.AddText("second");

        var matches = history.Query(" \t\r\n").Items;

        Assert.Equal(history.Items, matches);
    }

    [Fact]
    public void SearchWithoutAMatchReturnsEmptyResult()
    {
        var history = new ClipboardHistory();
        history.AddText("available text");

        var matches = history.Query("missing").Items;

        Assert.Empty(matches);
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan duration)
    {
        utcNow += duration;
    }
}
