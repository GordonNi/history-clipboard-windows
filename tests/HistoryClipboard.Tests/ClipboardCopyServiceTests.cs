using HistoryClipboard.Core.Capture;
using HistoryClipboard.Core.History;

namespace HistoryClipboard.Tests;

public sealed class ClipboardCopyServiceTests
{
    [Fact]
    public async Task CopyingHistoryItemSuppressesItsClipboardUpdate()
    {
        var history = new ClipboardHistory();
        var item = Assert.IsType<ClipboardHistoryItem>(history.AddText("copy me"));
        var sequence = new MutableClipboardSequenceNumber(40);
        var writer = new RecordingClipboardHistoryWriter(sequence);
        var copy = new ClipboardCopyService(writer, sequence);

        var result = await copy.CopyAsync(item);

        Assert.Equal(ClipboardCopyResult.Copied, result);
        Assert.Same(item, writer.WrittenItem);
        Assert.True(copy.ShouldSuppressCurrentUpdate());
    }

    [Fact]
    public async Task ExternalUpdateAfterCopyIsNotSuppressed()
    {
        var history = new ClipboardHistory();
        var item = Assert.IsType<ClipboardHistoryItem>(history.AddText("copy me"));
        var sequence = new MutableClipboardSequenceNumber(90);
        var copy = new ClipboardCopyService(
            new RecordingClipboardHistoryWriter(sequence),
            sequence);
        await copy.CopyAsync(item);

        sequence.Advance();

        Assert.False(copy.ShouldSuppressCurrentUpdate());
    }

    [Fact]
    public async Task TemporarilyUnavailableClipboardWriteIsRetried()
    {
        var history = new ClipboardHistory();
        var item = Assert.IsType<ClipboardHistoryItem>(history.AddText("copy after retry"));
        var sequence = new MutableClipboardSequenceNumber(120);
        var writer = new TemporarilyUnavailableClipboardHistoryWriter(sequence);
        var copy = new ClipboardCopyService(
            writer,
            sequence,
            new ClipboardCopyOptions
            {
                MaxWriteAttempts = 2,
                RetryDelay = TimeSpan.Zero,
            });

        var result = await copy.CopyAsync(item);

        Assert.Equal(ClipboardCopyResult.Copied, result);
        Assert.Equal(2, writer.Attempts);
        Assert.True(copy.ShouldSuppressCurrentUpdate());
    }
}

internal sealed class RecordingClipboardHistoryWriter(MutableClipboardSequenceNumber sequence)
    : IClipboardHistoryWriter
{
    public ClipboardHistoryItem? WrittenItem { get; private set; }

    public void Write(ClipboardHistoryItem item)
    {
        WrittenItem = item;
        sequence.Advance();
    }
}

internal sealed class MutableClipboardSequenceNumber(uint current) : IClipboardSequenceNumber
{
    public uint GetCurrent()
    {
        return current;
    }

    public void Advance()
    {
        current++;
    }
}

internal sealed class TemporarilyUnavailableClipboardHistoryWriter(
    MutableClipboardSequenceNumber sequence) : IClipboardHistoryWriter
{
    public int Attempts { get; private set; }

    public void Write(ClipboardHistoryItem item)
    {
        Attempts++;
        if (Attempts == 1)
        {
            throw new ClipboardUnavailableException();
        }

        sequence.Advance();
    }
}
