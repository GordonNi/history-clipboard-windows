using System.Globalization;
using HistoryClipboard.Core.History;
using Microsoft.Data.Sqlite;

namespace HistoryClipboard.Infrastructure.Persistence;

public sealed class SqliteClipboardHistoryRepository : IClipboardHistoryRepository
{
    private const int CurrentSchemaVersion = 1;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private readonly string connectionString;
    private readonly string databaseDirectory;
    private readonly string imagesDirectory;

    public SqliteClipboardHistoryRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullDatabasePath = Path.GetFullPath(databasePath);
        var directoryPath = Path.GetDirectoryName(fullDatabasePath);
        databaseDirectory = directoryPath ??
            throw new ArgumentException("The database path must include a directory.", nameof(databasePath));
        imagesDirectory = Path.Combine(databaseDirectory, "Images");
        Directory.CreateDirectory(databaseDirectory);

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

        EnsureSchema();
        CleanupAbandonedDeletionTombstones();
    }

    public IReadOnlyList<ClipboardHistoryItem> Load()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, type, created_at_utc, is_pinned, text_content, image_path
            FROM clipboard_items;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<ClipboardHistoryItem>();
        while (reader.Read())
        {
            var type = (ClipboardItemType)reader.GetInt32(1);
            if (!Enum.IsDefined(type))
            {
                throw new InvalidDataException($"Unknown clipboard item type '{(int)type}'.");
            }

            var storedImagePath = reader.IsDBNull(5) ? null : reader.GetString(5);
            items.Add(ClipboardHistoryItem.Restore(
                Guid.ParseExact(reader.GetString(0), "D"),
                type,
                DateTimeOffset.ParseExact(
                    reader.GetString(2),
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.GetBoolean(3),
                reader.GetString(4),
                storedImagePath is null ? null : ResolveImagePath(storedImagePath)));
        }

        return items;
    }

    public void InsertText(ClipboardHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Type != ClipboardItemType.Text)
        {
            throw new ArgumentException("Only text items can use text persistence.", nameof(item));
        }

        InsertRecord(item, storedImagePath: null);
    }

    public ClipboardHistoryItem InsertImage(
        Guid itemId,
        DateTimeOffset createdAtUtc,
        ReadOnlyMemory<byte> pngBytes)
    {
        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("An image item must have an ID.", nameof(itemId));
        }

        if (pngBytes.IsEmpty)
        {
            throw new ArgumentException("PNG content cannot be empty.", nameof(pngBytes));
        }

        if (!pngBytes.Span.StartsWith(PngSignature))
        {
            throw new ArgumentException("Image content must use PNG encoding.", nameof(pngBytes));
        }

        Directory.CreateDirectory(imagesDirectory);
        var fileName = $"{itemId:D}.png";
        var storedImagePath = Path.Combine("Images", fileName);
        var finalImagePath = Path.Combine(imagesDirectory, fileName);
        var temporaryImagePath = Path.Combine(
            imagesDirectory,
            $".{itemId:N}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryImagePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(pngBytes.Span);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryImagePath, finalImagePath);
            var item = ClipboardHistoryItem.Restore(
                itemId,
                ClipboardItemType.Image,
                createdAtUtc,
                false,
                string.Empty,
                finalImagePath);

            try
            {
                InsertRecord(item, storedImagePath);
            }
            catch
            {
                File.Delete(finalImagePath);
                throw;
            }

            return item;
        }
        finally
        {
            File.Delete(temporaryImagePath);
        }
    }

    private void InsertRecord(ClipboardHistoryItem item, string? storedImagePath)
    {
        ArgumentNullException.ThrowIfNull(item);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO clipboard_items (
                id,
                type,
                created_at_utc,
                is_pinned,
                text_content,
                image_path)
            VALUES (
                $id,
                $type,
                $createdAtUtc,
                $isPinned,
                $textContent,
                $imagePath);
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$type", (int)item.Type);
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            item.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$isPinned", item.IsPinned);
        command.Parameters.AddWithValue("$textContent", item.TextContent);
        command.Parameters.AddWithValue("$imagePath", (object?)storedImagePath ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void UpdatePin(Guid itemId, bool isPinned)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE clipboard_items SET is_pinned = $isPinned WHERE id = $id;";
        command.Parameters.AddWithValue("$isPinned", isPinned);
        command.Parameters.AddWithValue("$id", itemId.ToString("D"));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                $"Persisted clipboard item '{itemId:D}' was not found.");
        }
    }

    public void Delete(IReadOnlyCollection<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var stagedImages = new List<StagedImage>();

        try
        {
            foreach (var itemId in itemIds)
            {
                var storedImagePath = ReadStoredImagePath(connection, transaction, itemId);
                if (storedImagePath is null)
                {
                    continue;
                }

                var imagePath = ResolveImagePath(storedImagePath);
                if (File.Exists(imagePath))
                {
                    var stagedPath = Path.Combine(
                        imagesDirectory,
                        $".{Path.GetFileName(imagePath)}.{Guid.NewGuid():N}.deleting");
                    File.Move(imagePath, stagedPath);
                    stagedImages.Add(new StagedImage(imagePath, stagedPath));
                }
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM clipboard_items WHERE id = $id;";
            var idParameter = command.Parameters.Add("$id", SqliteType.Text);

            foreach (var itemId in itemIds)
            {
                idParameter.Value = itemId.ToString("D");
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException(
                        $"Persisted clipboard item '{itemId:D}' was not found.");
                }
            }

            transaction.Commit();
        }
        catch
        {
            RestoreStagedImages(stagedImages);
            throw;
        }

        DeleteStagedImages(stagedImages);
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var versionTableCommand = connection.CreateCommand())
        {
            versionTableCommand.Transaction = transaction;
            versionTableCommand.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_version (
                    version INTEGER NOT NULL
                );
                """;
            versionTableCommand.ExecuteNonQuery();
        }

        var storedVersion = ReadSchemaVersion(connection, transaction);
        if (storedVersion is null)
        {
            using var insertVersionCommand = connection.CreateCommand();
            insertVersionCommand.Transaction = transaction;
            insertVersionCommand.CommandText =
                "INSERT INTO schema_version (version) VALUES ($version);";
            insertVersionCommand.Parameters.AddWithValue("$version", CurrentSchemaVersion);
            insertVersionCommand.ExecuteNonQuery();
        }
        else if (storedVersion != CurrentSchemaVersion)
        {
            throw new UnsupportedSchemaVersionException(
                storedVersion.Value,
                CurrentSchemaVersion);
        }

        using (var historyTableCommand = connection.CreateCommand())
        {
            historyTableCommand.Transaction = transaction;
            historyTableCommand.CommandText =
                """
                CREATE TABLE IF NOT EXISTS clipboard_items (
                    id TEXT PRIMARY KEY,
                    type INTEGER NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    is_pinned INTEGER NOT NULL,
                    text_content TEXT NOT NULL,
                    image_path TEXT NULL
                );
                """;
            historyTableCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private string ResolveImagePath(string storedImagePath)
    {
        var fullImagePath = Path.IsPathFullyQualified(storedImagePath)
            ? Path.GetFullPath(storedImagePath)
            : Path.GetFullPath(Path.Combine(databaseDirectory, storedImagePath));
        var relativePath = Path.GetRelativePath(imagesDirectory, fullImagePath);
        if (relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidDataException(
                $"Persisted image path '{storedImagePath}' is outside the Images directory.");
        }

        return fullImagePath;
    }

    private void CleanupAbandonedDeletionTombstones()
    {
        if (!Directory.Exists(imagesDirectory))
        {
            return;
        }

        foreach (var tombstonePath in Directory.EnumerateFiles(
            imagesDirectory,
            "*.deleting",
            SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(tombstonePath);
            }
            catch (IOException)
            {
                // A locked tombstone remains hidden and will be retried on the next repository startup.
            }
            catch (UnauthorizedAccessException)
            {
                // A transient Windows permission failure is retried on the next repository startup.
            }
        }
    }

    private static int? ReadSchemaVersion(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var value = command.ExecuteScalar();
        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string? ReadStoredImagePath(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid itemId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT image_path FROM clipboard_items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", itemId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException(
                $"Persisted clipboard item '{itemId:D}' was not found.");
        }

        return reader.IsDBNull(0) ? null : reader.GetString(0);
    }

    private static void RestoreStagedImages(IEnumerable<StagedImage> stagedImages)
    {
        foreach (var image in stagedImages.Reverse())
        {
            if (File.Exists(image.StagedPath) && !File.Exists(image.OriginalPath))
            {
                File.Move(image.StagedPath, image.OriginalPath);
            }
        }
    }

    private static void DeleteStagedImages(IEnumerable<StagedImage> stagedImages)
    {
        foreach (var image in stagedImages)
        {
            try
            {
                File.Delete(image.StagedPath);
            }
            catch (IOException)
            {
                // The canonical image and its metadata are already gone. A later cleanup can remove the tombstone.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep logical deletion successful even if Windows temporarily holds the tombstone file.
            }
        }
    }

    private readonly record struct StagedImage(string OriginalPath, string StagedPath);
}
