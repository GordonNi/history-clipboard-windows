using HistoryClipboard.App.Windows;

namespace HistoryClipboard.Tests;

public sealed class LaunchAtLoginControllerTests
{
    [Fact]
    public void MatchingQuotedExecutablePathIsEnabled()
    {
        var executablePath = Path.Combine(
            Path.GetTempPath(),
            "History Clipboard",
            "HistoryClipboard.App.exe");
        var controller = CreateController(
            executablePath,
            readValue: () => $"  \"{Path.GetFullPath(executablePath).ToUpperInvariant()}\"  ");

        var result = controller.GetStatus();

        Assert.True(result.Succeeded);
        Assert.True(result.IsEnabled);
        Assert.Null(result.ErrorType);
    }

    [Fact]
    public void MissingOrDifferentRunValueIsDisabled()
    {
        var controller = CreateController(
            @"C:\Apps\HistoryClipboard.App.exe",
            readValue: () => "\"C:\\Other\\Application.exe\"");

        var result = controller.GetStatus();

        Assert.True(result.Succeeded);
        Assert.False(result.IsEnabled);
    }

    [Fact]
    public void EnablingWritesQuotedFullExecutablePath()
    {
        string? writtenValue = null;
        var executablePath = Path.Combine("relative", "HistoryClipboard.App.exe");
        var controller = CreateController(
            executablePath,
            writeValue: value => writtenValue = value);

        var result = controller.SetEnabled(true);

        Assert.True(result.Succeeded);
        Assert.True(result.IsEnabled);
        Assert.Equal($"\"{Path.GetFullPath(executablePath)}\"", writtenValue);
    }

    [Fact]
    public void DisablingDeletesTheOwnedRunValue()
    {
        var deleteCount = 0;
        var controller = CreateController(
            @"C:\Apps\HistoryClipboard.App.exe",
            deleteValue: () => deleteCount++);

        var result = controller.SetEnabled(false);

        Assert.True(result.Succeeded);
        Assert.False(result.IsEnabled);
        Assert.Equal(1, deleteCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegistryWriteFailuresAreReturnedWithoutThrowing(bool enabled)
    {
        var controller = CreateController(
            @"C:\Apps\HistoryClipboard.App.exe",
            writeValue: _ => throw new UnauthorizedAccessException(),
            deleteValue: () => throw new UnauthorizedAccessException());

        var result = controller.SetEnabled(enabled);

        Assert.False(result.Succeeded);
        Assert.False(result.IsEnabled);
        Assert.Equal(nameof(UnauthorizedAccessException), result.ErrorType);
    }

    [Fact]
    public void RegistryReadFailureIsReturnedWithoutThrowing()
    {
        var controller = CreateController(
            @"C:\Apps\HistoryClipboard.App.exe",
            readValue: () => throw new UnauthorizedAccessException());

        var result = controller.GetStatus();

        Assert.False(result.Succeeded);
        Assert.False(result.IsEnabled);
        Assert.Equal(nameof(UnauthorizedAccessException), result.ErrorType);
    }

    private static LaunchAtLoginController CreateController(
        string executablePath,
        Func<string?>? readValue = null,
        Action<string>? writeValue = null,
        Action? deleteValue = null)
    {
        return new LaunchAtLoginController(
            executablePath,
            readValue ?? (() => null),
            writeValue ?? (_ => { }),
            deleteValue ?? (() => { }));
    }
}
