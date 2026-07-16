using System.IO;
using System.Security;
using Microsoft.Win32;

namespace HistoryClipboard.App.Windows;

public sealed class LaunchAtLoginController
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "HistoryClipboard";

    private readonly Action deleteValue;
    private readonly string runCommand;
    private readonly Func<string?> readValue;
    private readonly Action<string> writeValue;

    public LaunchAtLoginController(string executablePath)
        : this(executablePath, ReadValue, WriteValue, DeleteValue)
    {
    }

    internal LaunchAtLoginController(
        string executablePath,
        Func<string?> readValue,
        Action<string> writeValue,
        Action deleteValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(readValue);
        ArgumentNullException.ThrowIfNull(writeValue);
        ArgumentNullException.ThrowIfNull(deleteValue);

        var fullPath = Path.GetFullPath(executablePath);
        if (fullPath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The executable path cannot contain a quotation mark.",
                nameof(executablePath));
        }

        runCommand = $"\"{fullPath}\"";
        this.readValue = readValue;
        this.writeValue = writeValue;
        this.deleteValue = deleteValue;
    }

    public LaunchAtLoginResult GetStatus()
    {
        try
        {
            var savedCommand = readValue();
            var isEnabled = string.Equals(
                savedCommand?.Trim(),
                runCommand,
                StringComparison.OrdinalIgnoreCase);
            return LaunchAtLoginResult.Success(isEnabled);
        }
        catch (Exception exception) when (IsRegistryAccessFailure(exception))
        {
            return LaunchAtLoginResult.Failure(exception);
        }
    }

    public LaunchAtLoginResult SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                writeValue(runCommand);
            }
            else
            {
                deleteValue();
            }

            return LaunchAtLoginResult.Success(enabled);
        }
        catch (Exception exception) when (IsRegistryAccessFailure(exception))
        {
            return LaunchAtLoginResult.Failure(exception);
        }
    }

    private static void DeleteValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private static bool IsRegistryAccessFailure(Exception exception)
    {
        return exception is IOException
            or SecurityException
            or UnauthorizedAccessException;
    }

    private static string? ReadValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(
            RunValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static void WriteValue(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(RunValueName, command, RegistryValueKind.String);
    }
}

public readonly record struct LaunchAtLoginResult(
    bool Succeeded,
    bool IsEnabled,
    string? ErrorType)
{
    public static LaunchAtLoginResult Failure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new LaunchAtLoginResult(false, false, exception.GetType().Name);
    }

    public static LaunchAtLoginResult Success(bool isEnabled)
    {
        return new LaunchAtLoginResult(true, isEnabled, null);
    }
}
