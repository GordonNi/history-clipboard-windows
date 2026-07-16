using HistoryClipboard.Core.History;

namespace HistoryClipboard.Core.Capture;

public sealed class ClipboardCaptureService
{
    private readonly ClipboardHistory history;
    private readonly IClipboardContentReader contentReader;
    private readonly ClipboardCaptureOptions options;

    public ClipboardCaptureService(
        ClipboardHistory history,
        IClipboardContentReader reader,
        ClipboardCaptureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(reader);

        this.history = history;
        contentReader = reader;
        this.options = options ?? new ClipboardCaptureOptions();

        ValidateOptions();
    }

    public bool IsPaused { get; private set; }

    public bool TogglePaused()
    {
        IsPaused = !IsPaused;
        return IsPaused;
    }

    public Task<ClipboardCaptureResult> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsPaused)
        {
            return Task.FromResult(ClipboardCaptureResult.Paused);
        }

        return CaptureWithRetryAsync(cancellationToken);
    }

    private void ValidateOptions()
    {

        if (options.MaxReadAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "At least one clipboard read attempt is required.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The clipboard retry delay cannot be negative.");
        }
    }

    private async Task<ClipboardCaptureResult> CaptureWithRetryAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.MaxReadAttempts; attempt++)
        {
            if (IsPaused)
            {
                return ClipboardCaptureResult.Paused;
            }

            try
            {
                var content = contentReader.Read();
                if (content is null ||
                    (content.Type == ClipboardContentType.Text && string.IsNullOrEmpty(content.Text)))
                {
                    return ClipboardCaptureResult.NoSupportedContent;
                }

                return content.Type switch
                {
                    ClipboardContentType.Text => history.AddText(content.Text!) is null
                        ? ClipboardCaptureResult.Duplicate
                        : ClipboardCaptureResult.CapturedText,
                    ClipboardContentType.PngImage => history.AddImage(content.PngBytes) is null
                        ? ClipboardCaptureResult.Duplicate
                        : ClipboardCaptureResult.CapturedImage,
                    _ => throw new InvalidOperationException(
                        $"Unsupported clipboard content type '{content.Type}'."),
                };
            }
            catch (ClipboardUnavailableException) when (attempt < options.MaxReadAttempts)
            {
                if (IsPaused)
                {
                    return ClipboardCaptureResult.Paused;
                }

                await Task.Delay(options.RetryDelay, cancellationToken);
            }
            catch (ClipboardUnavailableException)
            {
                return ClipboardCaptureResult.Unavailable;
            }
        }

        return ClipboardCaptureResult.Unavailable;
    }
}
