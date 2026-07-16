using HistoryClipboard.Core.History;
using HistoryClipboard.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace HistoryClipboard.Tests;

public sealed class SqliteClipboardHistoryTests
{
    private static readonly byte[] SamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void AddedTextIsRestoredFromSameDatabase()
    {
        using var database = new TemporaryDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var originalHistory = new ClipboardHistory(
            time,
            new SqliteClipboardHistoryRepository(database.Path));
        var added = Assert.IsType<ClipboardHistoryItem>(originalHistory.AddText("persist me"));

        var restoredHistory = new ClipboardHistory(
            time,
            new SqliteClipboardHistoryRepository(database.Path));

        Assert.Collection(
            restoredHistory.Items,
            restored =>
            {
                Assert.Equal(added.Id, restored.Id);
                Assert.Equal(added.CreatedAtUtc, restored.CreatedAtUtc);
                Assert.Equal("persist me", restored.TextContent);
            });
    }

    [Fact]
    public void PinnedStateIsRestoredFromSameDatabase()
    {
        using var database = new TemporaryDatabase();
        var originalHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        var added = Assert.IsType<ClipboardHistoryItem>(originalHistory.AddText("keep pinned"));
        originalHistory.TogglePin(added.Id);

        var restoredHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));

        Assert.True(Assert.Single(restoredHistory.Items).IsPinned);
    }

    [Fact]
    public void DeletedTextDoesNotReturnAfterReload()
    {
        using var database = new TemporaryDatabase();
        var originalHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        var removed = Assert.IsType<ClipboardHistoryItem>(originalHistory.AddText("remove me"));
        originalHistory.AddText("keep me");
        originalHistory.Delete(removed.Id);

        var restoredHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));

        Assert.Collection(
            restoredHistory.Items,
            item => Assert.Equal("keep me", item.TextContent));
    }

    [Fact]
    public void ClearedTextDoesNotReturnAfterReload()
    {
        using var database = new TemporaryDatabase();
        var originalHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        originalHistory.AddText("first");
        originalHistory.AddText("second");
        originalHistory.Clear();

        var restoredHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));

        Assert.Empty(restoredHistory.Items);
    }

    [Fact]
    public void ExpiredTextDoesNotReturnAfterReload()
    {
        using var database = new TemporaryDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var originalHistory = new ClipboardHistory(
            time,
            new SqliteClipboardHistoryRepository(database.Path));
        originalHistory.AddText("expired");
        time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1));
        originalHistory.AddText("current");
        originalHistory.RemoveExpired(RetentionPeriod.OneDay);

        var restoredHistory = new ClipboardHistory(
            time,
            new SqliteClipboardHistoryRepository(database.Path));

        Assert.Collection(
            restoredHistory.Items,
            item => Assert.Equal("current", item.TextContent));
    }

    [Fact]
    public void UnsupportedSchemaVersionIsRejectedWithClearError()
    {
        using var database = new TemporaryDatabase();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Path,
            Pooling = false,
        }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE schema_version (version INTEGER NOT NULL);
                INSERT INTO schema_version (version) VALUES (2);
                """;
            command.ExecuteNonQuery();
        }

        var originalDatabaseBytes = File.ReadAllBytes(database.Path);

        var exception = Assert.Throws<UnsupportedSchemaVersionException>(
            () => new SqliteClipboardHistoryRepository(database.Path));

        Assert.Equal(2, exception.StoredVersion);
        Assert.Equal(1, exception.SupportedVersion);
        Assert.Contains("schema version 2", exception.Message, StringComparison.Ordinal);
        Assert.Equal(originalDatabaseBytes, File.ReadAllBytes(database.Path));
    }

    [Fact]
    public void AddedImageIsStoredAndRestoredFromSameDatabase()
    {
        using var database = new TemporaryDatabase();
        var originalHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));

        var added = Assert.IsType<ClipboardHistoryItem>(originalHistory.AddImage(SamplePng));

        Assert.Equal(ClipboardItemType.Image, added.Type);
        Assert.NotNull(added.ImagePath);
        Assert.Equal(SamplePng, File.ReadAllBytes(added.ImagePath));

        var restoredHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        var restored = Assert.Single(restoredHistory.Items);

        Assert.Equal(added.Id, restored.Id);
        Assert.Equal(ClipboardItemType.Image, restored.Type);
        Assert.Equal(added.ImagePath, restored.ImagePath);
        Assert.Equal(SamplePng, File.ReadAllBytes(Assert.IsType<string>(restored.ImagePath)));
    }

    [Fact]
    public void DeletedImageRemovesItsStoredFileAndDoesNotReturnAfterReload()
    {
        using var database = new TemporaryDatabase();
        var originalHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        var added = Assert.IsType<ClipboardHistoryItem>(originalHistory.AddImage(SamplePng));
        var imagePath = Assert.IsType<string>(added.ImagePath);

        originalHistory.Delete(added.Id);

        Assert.False(File.Exists(imagePath));
        var restoredHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        Assert.Empty(restoredHistory.Items);
    }

    [Fact]
    public void ClearRemovesAllStoredImagesFromMixedHistory()
    {
        using var database = new TemporaryDatabase();
        var history = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        var firstImage = Assert.IsType<ClipboardHistoryItem>(history.AddImage(SamplePng));
        history.AddText("text between images");
        var secondImage = Assert.IsType<ClipboardHistoryItem>(history.AddImage(SamplePng));
        var imagePaths = new[]
        {
            Assert.IsType<string>(firstImage.ImagePath),
            Assert.IsType<string>(secondImage.ImagePath),
        };

        history.Clear();

        Assert.All(imagePaths, imagePath => Assert.False(File.Exists(imagePath)));
        var restoredHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        Assert.Empty(restoredHistory.Items);
    }

    [Fact]
    public void ExpirationDeletesUnpinnedImageButKeepsPinnedImage()
    {
        using var database = new TemporaryDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var history = new ClipboardHistory(
            time,
            new SqliteClipboardHistoryRepository(database.Path));
        var expired = Assert.IsType<ClipboardHistoryItem>(history.AddImage(SamplePng));
        var pinned = Assert.IsType<ClipboardHistoryItem>(history.AddImage(SamplePng));
        history.TogglePin(pinned.Id);
        var expiredPath = Assert.IsType<string>(expired.ImagePath);
        var pinnedPath = Assert.IsType<string>(pinned.ImagePath);
        time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1));

        history.RemoveExpired(RetentionPeriod.OneDay);

        Assert.False(File.Exists(expiredPath));
        Assert.True(File.Exists(pinnedPath));
        var restoredHistory = new ClipboardHistory(
            time,
            new SqliteClipboardHistoryRepository(database.Path));
        var restored = Assert.Single(restoredHistory.Items);
        Assert.Equal(pinned.Id, restored.Id);
        Assert.True(restored.IsPinned);
    }

    [Fact]
    public void NonPngContentIsRejectedWithoutCreatingHistory()
    {
        using var database = new TemporaryDatabase();
        var history = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));

        Assert.Throws<ArgumentException>(
            () => history.AddImage(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

        Assert.Empty(history.Items);
        var restoredHistory = new ClipboardHistory(
            repository: new SqliteClipboardHistoryRepository(database.Path));
        Assert.Empty(restoredHistory.Items);
    }

    [Fact]
    public void RepositoryStartupRemovesAbandonedImageDeletionTombstones()
    {
        using var database = new TemporaryDatabase();
        Directory.CreateDirectory(database.ImagesDirectory);
        var tombstonePath = System.IO.Path.Combine(
            database.ImagesDirectory,
            ".orphan.png.00000000000000000000000000000000.deleting");
        File.WriteAllBytes(tombstonePath, SamplePng);

        _ = new SqliteClipboardHistoryRepository(database.Path);

        Assert.False(File.Exists(tombstonePath));
    }
}

internal sealed class TemporaryDatabase : IDisposable
{
    private readonly string directoryPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "HistoryClipboard.Tests",
        Guid.NewGuid().ToString("N"));

    public TemporaryDatabase()
    {
        Directory.CreateDirectory(directoryPath);
        Path = System.IO.Path.Combine(directoryPath, "history.db");
    }

    public string Path { get; }

    public string ImagesDirectory => System.IO.Path.Combine(directoryPath, "Images");

    public void Dispose()
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}
