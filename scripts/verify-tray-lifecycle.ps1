param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$dotnet = & (Join-Path $PSScriptRoot "resolve-dotnet.ps1")
$dotnetRoot = Split-Path -Parent $dotnet
$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"

$executable = Resolve-Path (
    "src\HistoryClipboard.App\bin\{0}\net10.0-windows\HistoryClipboard.App.exe" -f $Configuration)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HistoryClipboardTrayProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);
}
'@

$process = $null
try {
    $process = Start-Process -FilePath $executable -WindowStyle Normal -PassThru
    $script:windowHandle = [IntPtr]::Zero

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) {
            throw "Application exited during startup with code $($process.ExitCode)."
        }

        $callback = [HistoryClipboardTrayProbe+EnumWindowsProc] {
            param($candidateHandle, $parameter)

            $candidateProcessId = [uint32]0
            [void][HistoryClipboardTrayProbe]::GetWindowThreadProcessId(
                $candidateHandle,
                [ref]$candidateProcessId)

            if ($candidateProcessId -eq [uint32]$process.Id -and
                [HistoryClipboardTrayProbe]::IsWindowVisible($candidateHandle)) {
                $script:windowHandle = $candidateHandle
                return $false
            }

            return $true
        }

        [void][HistoryClipboardTrayProbe]::EnumWindows($callback, [IntPtr]::Zero)
        if ($script:windowHandle -ne [IntPtr]::Zero) {
            break
        }
    }

    if ($script:windowHandle -eq [IntPtr]::Zero) {
        throw "No visible application window was found."
    }

    [void][HistoryClipboardTrayProbe]::SendMessage(
        $script:windowHandle,
        0x0006,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    $process.Refresh()

    if ($process.HasExited) {
        throw "Deactivating the window exited the tray process with code $($process.ExitCode)."
    }

    if ([HistoryClipboardTrayProbe]::IsWindowVisible($script:windowHandle)) {
        throw "The history window remained visible after deactivation."
    }

    Write-Output "DEACTIVATION_AUTO_HIDE=window-hidden"

    [void][HistoryClipboardTrayProbe]::ShowWindow($script:windowHandle, 5)
    Start-Sleep -Milliseconds 200
    if (-not [HistoryClipboardTrayProbe]::IsWindowVisible($script:windowHandle)) {
        throw "The native test probe could not reshow the history window."
    }

    [void][HistoryClipboardTrayProbe]::SendMessage(
        $script:windowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    $process.Refresh()

    if ($process.HasExited) {
        throw "Closing the window exited the tray process with code $($process.ExitCode)."
    }

    Write-Output "CLOSE_TO_TRAY=process-still-running"
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
            $process.WaitForExit()
        }
    }
}

Write-Output "APP_PROCESSES_RUNNING=none"
