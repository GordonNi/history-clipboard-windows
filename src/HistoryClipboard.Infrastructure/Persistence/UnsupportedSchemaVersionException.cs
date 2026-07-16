namespace HistoryClipboard.Infrastructure.Persistence;

public sealed class UnsupportedSchemaVersionException : NotSupportedException
{
    public UnsupportedSchemaVersionException(int storedVersion, int supportedVersion)
        : base(
            $"Clipboard database schema version {storedVersion} is not supported; " +
            $"expected {supportedVersion}.")
    {
        StoredVersion = storedVersion;
        SupportedVersion = supportedVersion;
    }

    public int StoredVersion { get; }

    public int SupportedVersion { get; }
}
