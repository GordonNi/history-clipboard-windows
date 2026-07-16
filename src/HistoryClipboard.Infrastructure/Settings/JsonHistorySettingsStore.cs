using System.Text.Json;
using System.Text.Json.Serialization;
using HistoryClipboard.Core.History;

namespace HistoryClipboard.Infrastructure.Settings;

public sealed class JsonHistorySettingsStore
{
    private const string DefaultHotkeyName = nameof(GlobalHotkeyOption.ControlShiftV);
    private readonly string settingsPath;

    public JsonHistorySettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = Path.GetFullPath(settingsPath);
    }

    public RetentionPeriod LoadRetention()
    {
        var settings = LoadDocument();
        return IsSupported(settings.RetentionPeriod)
            ? settings.RetentionPeriod
            : RetentionPeriod.ThreeDays;
    }

    public GlobalHotkeyOption LoadHotkey()
    {
        var settings = LoadDocument();
        return Enum.TryParse<GlobalHotkeyOption>(settings.Hotkey, out var hotkey)
            && Enum.IsDefined(hotkey)
            ? hotkey
            : GlobalHotkeyOption.ControlShiftV;
    }

    public void SaveRetention(RetentionPeriod retentionPeriod)
    {
        if (!IsSupported(retentionPeriod))
        {
            throw new ArgumentOutOfRangeException(nameof(retentionPeriod));
        }

        var current = LoadDocument();
        SaveDocument(current with { RetentionPeriod = retentionPeriod });
    }

    public void SaveHotkey(GlobalHotkeyOption hotkey)
    {
        if (!Enum.IsDefined(hotkey))
        {
            throw new ArgumentOutOfRangeException(nameof(hotkey));
        }

        var current = LoadDocument();
        SaveDocument(current with { Hotkey = hotkey.ToString() });
    }

    private HistorySettingsDocument LoadDocument()
    {
        if (!File.Exists(settingsPath))
        {
            return CreateDefaultDocument();
        }

        try
        {
            using var stream = File.OpenRead(settingsPath);
            return JsonSerializer.Deserialize<HistorySettingsDocument>(stream)
                ?? CreateDefaultDocument();
        }
        catch (JsonException)
        {
            return CreateDefaultDocument();
        }
    }

    private void SaveDocument(HistorySettingsDocument settings)
    {
        var directoryPath = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(
                    stream,
                    settings);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record HistorySettingsDocument(
        [property: JsonPropertyName("retention_days")]
        RetentionPeriod RetentionPeriod,
        [property: JsonPropertyName("hotkey")]
        string Hotkey = DefaultHotkeyName);

    private static HistorySettingsDocument CreateDefaultDocument()
    {
        return new HistorySettingsDocument(
            RetentionPeriod.ThreeDays,
            DefaultHotkeyName);
    }

    private static bool IsSupported(RetentionPeriod retentionPeriod)
    {
        return retentionPeriod is RetentionPeriod.OneDay
            or RetentionPeriod.ThreeDays
            or RetentionPeriod.FiveDays;
    }
}
