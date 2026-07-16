using System.IO;
using System.Security;
using HistoryClipboard.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace HistoryClipboard.App.Startup;

internal static class StartupFailureClassifier
{
    internal const string UnsupportedSchemaCode = "HC-START-001";
    internal const string StorageUnavailableCode = "HC-START-002";
    internal const string UnknownFailureCode = "HC-START-999";

    public static StartupFailureNotice Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var causes = EnumerateCauses(exception).ToArray();
        if (causes.Any(cause => cause is UnsupportedSchemaVersionException))
        {
            return new StartupFailureNotice(
                UnsupportedSchemaCode,
                "本地历史数据库由不兼容的应用版本创建。请不要删除或修改数据；" +
                "请安装与该数据库兼容的版本，或联系支持人员。");
        }

        if (causes.Any(IsStorageUnavailable))
        {
            return new StartupFailureNotice(
                StorageUnavailableCode,
                "无法访问本地历史存储。请确认当前用户对本地应用数据目录有读写权限、" +
                "磁盘空间充足，并关闭可能锁定文件的程序后重试。");
        }

        return new StartupFailureNotice(
            UnknownFailureCode,
            "应用启动失败。请重新启动 Windows 后重试；如果问题持续，请联系支持人员并提供错误代码。");
    }

    private static IEnumerable<Exception> EnumerateCauses(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions)
            {
                foreach (var cause in EnumerateCauses(innerException))
                {
                    yield return cause;
                }
            }

            yield break;
        }

        if (exception.InnerException is not null)
        {
            foreach (var cause in EnumerateCauses(exception.InnerException))
            {
                yield return cause;
            }
        }
    }

    private static bool IsStorageUnavailable(Exception exception)
    {
        return exception is SqliteException
            or IOException
            or UnauthorizedAccessException
            or SecurityException;
    }
}

internal sealed record StartupFailureNotice(string Code, string UserMessage);
