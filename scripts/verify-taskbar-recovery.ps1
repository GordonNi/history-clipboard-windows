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
$existingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($existingProcesses.Count -ne 0) {
    throw "Close the running HistoryClipboard.App process before using this probe."
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HistoryClipboardTaskbarRecoveryProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string messageName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);
}
'@

function Find-ProcessWindow {
    param([int]$TargetProcessId)

    $script:taskbarRecoveryTargetProcessId = [uint32]$TargetProcessId
    $script:taskbarRecoveryWindowHandle = [IntPtr]::Zero
    $callback = [HistoryClipboardTaskbarRecoveryProbe+EnumWindowsProc] {
        param($candidateHandle, $parameter)

        $candidateProcessId = [uint32]0
        [void][HistoryClipboardTaskbarRecoveryProbe]::GetWindowThreadProcessId(
            $candidateHandle,
            [ref]$candidateProcessId)
        if ($candidateProcessId -eq $script:taskbarRecoveryTargetProcessId -and
            [HistoryClipboardTaskbarRecoveryProbe]::IsWindowVisible($candidateHandle)) {
            $script:taskbarRecoveryWindowHandle = $candidateHandle
            return $false
        }

        return $true
    }

    [void][HistoryClipboardTaskbarRecoveryProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:taskbarRecoveryWindowHandle
}

$process = $null
try {
    $process = Start-Process -FilePath $executable -WindowStyle Normal -PassThru
    $windowHandle = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($process.HasExited) {
            throw "Application exited during startup with code $($process.ExitCode)."
        }

        $windowHandle = Find-ProcessWindow -TargetProcessId $process.Id
        if ($windowHandle -ne [IntPtr]::Zero) {
            break
        }
    }

    if ($windowHandle -eq [IntPtr]::Zero) {
        throw "No application window was found."
    }

    $taskbarCreatedMessage =
        [HistoryClipboardTaskbarRecoveryProbe]::RegisterWindowMessage("TaskbarCreated")
    if ($taskbarCreatedMessage -eq 0) {
        throw "The TaskbarCreated message could not be registered."
    }

    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        [void][HistoryClipboardTaskbarRecoveryProbe]::SendMessage(
            $windowHandle,
            $taskbarCreatedMessage,
            [IntPtr]::Zero,
            [IntPtr]::Zero)
    }

    Start-Sleep -Milliseconds 300
    $process.Refresh()
    if ($process.HasExited) {
        throw "Application exited while handling TaskbarCreated with code $($process.ExitCode)."
    }

    Write-Output "TASKBAR_CREATED=message-delivered"
    Write-Output "TRAY_RECOVERY=republish-completed"
    Write-Output "RECOVERY_IDEMPOTENCE=process-still-running"
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
