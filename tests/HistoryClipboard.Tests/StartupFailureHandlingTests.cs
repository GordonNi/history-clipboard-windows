using System.Security;
using HistoryClipboard.App.Startup;
using HistoryClipboard.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace HistoryClipboard.Tests;

public sealed class StartupFailureHandlingTests
{
    [Fact]
    public void UnsupportedSchemaHasStableSafeNotice()
    {
        var sensitiveMessage = @"secret path C:\Users\person\history.db clipboard words";

        var notice = Classify(
            new InvalidOperationException(
                sensitiveMessage,
                new UnsupportedSchemaVersionException(7, 1)));

        Assert.Equal(StartupFailureClassifier.UnsupportedSchemaCode, notice.Code);
        Assert.DoesNotContain("7", notice.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveMessage, notice.UserMessage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StorageFailures))]
    public void StorageFailuresHaveStableSafeNotice(Exception exception)
    {
        var notice = Classify(exception);

        Assert.Equal(StartupFailureClassifier.StorageUnavailableCode, notice.Code);
        Assert.DoesNotContain(exception.Message, notice.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFailureHasStableSafeNotice()
    {
        const string sensitiveMessage = "clipboard content and a private stack detail";

        var notice = Classify(new InvalidOperationException(sensitiveMessage));

        Assert.Equal(StartupFailureClassifier.UnknownFailureCode, notice.Code);
        Assert.DoesNotContain(sensitiveMessage, notice.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedSchemaFailureTakesPriorityOverStorageWrapper()
    {
        var exception = new IOException(
            "private path",
            new UnsupportedSchemaVersionException(2, 1));

        var notice = Classify(exception);

        Assert.Equal(StartupFailureClassifier.UnsupportedSchemaCode, notice.Code);
    }

    [Fact]
    public void CoordinatorCleansUpThenPresentsThenShutsDownWithNonzeroCode()
    {
        var events = new List<string>();
        StartupFailureCoordinator.Handle(
            new InvalidOperationException(),
            [
                () => events.Add("taskbar"),
                () => events.Add("tray"),
                () => events.Add("window"),
                () => events.Add("single-instance"),
            ],
            notice => events.Add($"present:{notice.Code}"),
            exitCode => events.Add($"shutdown:{exitCode}"));

        Assert.Equal(
            [
                "taskbar",
                "tray",
                "window",
                "single-instance",
                $"present:{StartupFailureClassifier.UnknownFailureCode}",
                "shutdown:1",
            ],
            events);
    }

    [Fact]
    public void CleanupAndPresentationFailuresDoNotPreventRemainingCleanupOrShutdown()
    {
        var events = new List<string>();
        var exitCode = 0;

        StartupFailureCoordinator.Handle(
            new InvalidOperationException(),
            [
                () =>
                {
                    events.Add("cleanup-failed");
                    throw new IOException();
                },
                () => events.Add("cleanup-continued"),
            ],
            _ =>
            {
                events.Add("present-failed");
                throw new InvalidOperationException();
            },
            value =>
            {
                events.Add("shutdown");
                exitCode = value;
            });

        Assert.Equal(
            ["cleanup-failed", "cleanup-continued", "present-failed", "shutdown"],
            events);
        Assert.NotEqual(0, exitCode);
    }

    public static TheoryData<Exception> StorageFailures()
    {
        const string sensitiveMessage = @"C:\private\history.db contains private clipboard text";
        return new TheoryData<Exception>
        {
            new SqliteException(sensitiveMessage, 14),
            new IOException(sensitiveMessage),
            new UnauthorizedAccessException(sensitiveMessage),
            new SecurityException(sensitiveMessage),
        };
    }

    private static StartupFailureNotice Classify(Exception exception)
    {
        return StartupFailureClassifier.Classify(exception);
    }
}
