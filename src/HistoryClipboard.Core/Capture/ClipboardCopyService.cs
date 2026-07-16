using HistoryClipboard.Core.History;

namespace HistoryClipboard.Core.Capture;

public sealed class ClipboardCopyService
{
    private readonly IClipboardSequenceNumber sequenceNumber;
    private readonly IClipboardHistoryWriter writer;
    private readonly ClipboardCopyOptions options;
    private bool isWriting;
    private uint? lastWrittenSequence;

    public ClipboardCopyService(
        IClipboardHistoryWriter writer,
        IClipboardSequenceNumber sequenceNumber,
        ClipboardCopyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sequenceNumber);

        this.writer = writer;
        this.sequenceNumber = sequenceNumber;
        this.options = options ?? new ClipboardCopyOptions();

        ValidateOptions();
    }

    public async Task<ClipboardCopyResult> CopyAsync(
        ClipboardHistoryItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 1; attempt <= options.MaxWriteAttempts; attempt++)
        {
            isWriting = true;
            try
            {
                writer.Write(item);
                var currentSequence = sequenceNumber.GetCurrent();
                lastWrittenSequence = currentSequence == 0 ? null : currentSequence;
                return ClipboardCopyResult.Copied;
            }
            catch (ClipboardUnavailableException) when (attempt < options.MaxWriteAttempts)
            {
            }
            catch (ClipboardUnavailableException)
            {
                return ClipboardCopyResult.Unavailable;
            }
            finally
            {
                isWriting = false;
            }

            await Task.Delay(options.RetryDelay, cancellationToken);
        }

        return ClipboardCopyResult.Unavailable;
    }

    public bool ShouldSuppressCurrentUpdate()
    {
        if (isWriting)
        {
            return true;
        }

        if (lastWrittenSequence is null)
        {
            return false;
        }

        var currentSequence = sequenceNumber.GetCurrent();
        if (currentSequence == lastWrittenSequence)
        {
            return true;
        }

        lastWrittenSequence = null;
        return false;
    }

    private void ValidateOptions()
    {
        if (options.MaxWriteAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "At least one clipboard write attempt is required.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The clipboard retry delay cannot be negative.");
        }
    }
}
