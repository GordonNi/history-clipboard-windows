using HistoryClipboard.Core.Capture;
using HistoryClipboard.Core.History;
using HistoryClipboard.Infrastructure.Persistence;

namespace HistoryClipboard.Tests;

public sealed class ClipboardCaptureServiceTests
{
    private static readonly byte[] SamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task TemporarilyUnavailableClipboardIsRetriedAndCaptured()
    {
        var history = new ClipboardHistory();
        var reader = new ScriptedClipboardContentReader(
            static () => throw new ClipboardUnavailableException(),
            static () => throw new ClipboardUnavailableException(),
            static () => ClipboardContent.FromText("captured after retry"));
        var capture = new ClipboardCaptureService(
            history,
            reader,
            new ClipboardCaptureOptions
            {
                MaxReadAttempts = 3,
                RetryDelay = TimeSpan.Zero,
            });

        var result = await capture.CaptureAsync();

        Assert.Equal(ClipboardCaptureResult.CapturedText, result);
        Assert.Collection(
            history.Items,
            item => Assert.Equal("captured after retry", item.TextContent));
    }

    [Fact]
    public async Task ClipboardWithoutTextIsIgnored()
    {
        var history = new ClipboardHistory();
        var capture = new ClipboardCaptureService(
            history,
            new ScriptedClipboardContentReader(static () => null));

        var result = await capture.CaptureAsync();

        Assert.Equal(ClipboardCaptureResult.NoSupportedContent, result);
        Assert.Empty(history.Items);
    }

    [Fact]
    public async Task ConsecutiveDuplicateTextIsReportedWithoutAddingHistory()
    {
        var history = new ClipboardHistory();
        history.AddText("already captured");
        var capture = new ClipboardCaptureService(
            history,
            new ScriptedClipboardContentReader(
                static () => ClipboardContent.FromText("already captured")));

        var result = await capture.CaptureAsync();

        Assert.Equal(ClipboardCaptureResult.Duplicate, result);
        Assert.Single(history.Items);
    }

    [Fact]
    public async Task ExhaustedRetriesLeaveHistoryUnchanged()
    {
        var history = new ClipboardHistory();
        var capture = new ClipboardCaptureService(
            history,
            new ScriptedClipboardContentReader(
                static () => throw new ClipboardUnavailableException(),
                static () => throw new ClipboardUnavailableException()),
            new ClipboardCaptureOptions
            {
                MaxReadAttempts = 2,
                RetryDelay = TimeSpan.Zero,
            });

        var result = await capture.CaptureAsync();

        Assert.Equal(ClipboardCaptureResult.Unavailable, result);
        Assert.Empty(history.Items);
    }

    [Fact]
    public async Task PngClipboardContentIsCapturedAsImageHistory()
    {
        using var database = new TemporaryDatabase();
        var history = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        var capture = new ClipboardCaptureService(
            history,
            new ScriptedClipboardContentReader(
                () => ClipboardContent.FromPngImage(SamplePng)));

        var result = await capture.CaptureAsync();

        Assert.Equal(ClipboardCaptureResult.CapturedImage, result);
        var item = Assert.Single(history.Items);
        Assert.Equal(ClipboardItemType.Image, item.Type);
        Assert.Equal(SamplePng, File.ReadAllBytes(Assert.IsType<string>(item.ImagePath)));
    }

    [Fact]
    public async Task PausedCaptureDoesNotReadOrChangeClipboardHistory()
    {
        var history = new ClipboardHistory();
        var capture = new ClipboardCaptureService(
            history,
            new ScriptedClipboardContentReader(
                static () => throw new InvalidOperationException("The reader must not run.")));

        var isPaused = capture.TogglePaused();
        var result = await capture.CaptureAsync();

        Assert.True(isPaused);
        Assert.True(capture.IsPaused);
        Assert.Equal(ClipboardCaptureResult.Paused, result);
        Assert.Empty(history.Items);
    }

    [Fact]
    public async Task PausingDuringRetryStopsBeforeAnotherClipboardRead()
    {
        var history = new ClipboardHistory();
        ClipboardCaptureService? capture = null;
        var reader = new ScriptedClipboardContentReader(
            () =>
            {
                capture!.TogglePaused();
                throw new ClipboardUnavailableException();
            },
            static () => ClipboardContent.FromText("must not be captured"));
        capture = new ClipboardCaptureService(
            history,
            reader,
            new ClipboardCaptureOptions
            {
                MaxReadAttempts = 2,
                RetryDelay = TimeSpan.Zero,
            });

        var result = await capture.CaptureAsync();

        Assert.Equal(ClipboardCaptureResult.Paused, result);
        Assert.Empty(history.Items);
    }

    [Fact]
    public async Task CaptureResumesAfterPauseIsToggledOff()
    {
        var history = new ClipboardHistory();
        var capture = new ClipboardCaptureService(
            history,
            new ScriptedClipboardContentReader(
                static () => ClipboardContent.FromText("captured after resume")));
        capture.TogglePaused();

        var isPaused = capture.TogglePaused();
        var result = await capture.CaptureAsync();

        Assert.False(isPaused);
        Assert.False(capture.IsPaused);
        Assert.Equal(ClipboardCaptureResult.CapturedText, result);
        Assert.Collection(
            history.Items,
            item => Assert.Equal("captured after resume", item.TextContent));
    }
}

internal sealed class ScriptedClipboardContentReader(params Func<ClipboardContent?>[] readSteps)
    : IClipboardContentReader
{
    private readonly Queue<Func<ClipboardContent?>> readSteps = new(readSteps);

    public ClipboardContent? Read()
    {
        return readSteps.Dequeue().Invoke();
    }
}
