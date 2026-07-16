using HistoryClipboard.Core.History;
using HistoryClipboard.Infrastructure.Settings;

namespace HistoryClipboard.Tests;

public sealed class JsonHistorySettingsStoreTests
{
    [Fact]
    public void MissingSettingsUseThreeDayDefault()
    {
        using var database = new TemporaryDatabase();
        var settingsPath = Path.Combine(
            Path.GetDirectoryName(database.Path)!,
            "settings.json");

        var retention = new JsonHistorySettingsStore(settingsPath).LoadRetention();

        Assert.Equal(RetentionPeriod.ThreeDays, retention);
    }

    [Fact]
    public void MissingSettingsUseControlShiftVHotkeyDefault()
    {
        using var database = new TemporaryDatabase();
        var settingsPath = Path.Combine(
            Path.GetDirectoryName(database.Path)!,
            "settings.json");

        var hotkey = new JsonHistorySettingsStore(settingsPath).LoadHotkey();

        Assert.Equal(GlobalHotkeyOption.ControlShiftV, hotkey);
    }

    [Fact]
    public void SavedRetentionReloadsFromJson()
    {
        using var database = new TemporaryDatabase();
        var settingsPath = Path.Combine(
            Path.GetDirectoryName(database.Path)!,
            "settings.json");
        var settings = new JsonHistorySettingsStore(settingsPath);

        settings.SaveRetention(RetentionPeriod.OneDay);

        var reloaded = new JsonHistorySettingsStore(settingsPath).LoadRetention();
        Assert.Equal(RetentionPeriod.OneDay, reloaded);
    }

    [Fact]
    public void SavedHotkeyReloadsWithoutChangingRetention()
    {
        using var database = new TemporaryDatabase();
        var settingsPath = Path.Combine(
            Path.GetDirectoryName(database.Path)!,
            "settings.json");
        var settings = new JsonHistorySettingsStore(settingsPath);

        settings.SaveRetention(RetentionPeriod.FiveDays);
        settings.SaveHotkey(GlobalHotkeyOption.ControlAltV);

        var reloaded = new JsonHistorySettingsStore(settingsPath);
        Assert.Equal(RetentionPeriod.FiveDays, reloaded.LoadRetention());
        Assert.Equal(GlobalHotkeyOption.ControlAltV, reloaded.LoadHotkey());
    }

    [Fact]
    public void SavingRetentionPreservesDisabledHotkey()
    {
        using var database = new TemporaryDatabase();
        var settingsPath = Path.Combine(
            Path.GetDirectoryName(database.Path)!,
            "settings.json");
        var settings = new JsonHistorySettingsStore(settingsPath);

        settings.SaveHotkey(GlobalHotkeyOption.Disabled);
        settings.SaveRetention(RetentionPeriod.OneDay);

        var reloaded = new JsonHistorySettingsStore(settingsPath);
        Assert.Equal(GlobalHotkeyOption.Disabled, reloaded.LoadHotkey());
        Assert.Equal(RetentionPeriod.OneDay, reloaded.LoadRetention());
    }

    [Theory]
    [InlineData("{\"retention_days\":99}")]
    [InlineData("{not-json")]
    public void InvalidSettingsUseThreeDayDefault(string json)
    {
        using var database = new TemporaryDatabase();
        var settingsPath = Path.Combine(
            Path.GetDirectoryName(database.Path)!,
            "settings.json");
        File.WriteAllText(settingsPath, json);

        var retention = new JsonHistorySettingsStore(settingsPath).LoadRetention();

        Assert.Equal(RetentionPeriod.ThreeDays, retention);
    }

    [Theory]
    [InlineData("{\"retention_days\":3,\"hotkey\":\"NotAHotkey\"}")]
    [InlineData("{not-json")]
    public void InvalidHotkeyUsesControlShiftVDefault(string json)
    {
        using var database = new TemporaryDatabase();
        var settingsPath = Path.Combine(
            Path.GetDirectoryName(database.Path)!,
            "settings.json");
        File.WriteAllText(settingsPath, json);

        var hotkey = new JsonHistorySettingsStore(settingsPath).LoadHotkey();

        Assert.Equal(GlobalHotkeyOption.ControlShiftV, hotkey);
    }
}
