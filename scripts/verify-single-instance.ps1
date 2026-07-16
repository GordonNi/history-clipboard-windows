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

public static class HistoryClipboardSingleInstanceProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);
}
'@

function Find-ProcessWindow {
    param(
        [int]$TargetProcessId,
        [bool]$VisibleOnly
    )

    $script:singleInstanceTargetProcessId = [uint32]$TargetProcessId
    $script:singleInstanceVisibleOnly = $VisibleOnly
    $script:singleInstanceWindowHandle = [IntPtr]::Zero
    $callback = [HistoryClipboardSingleInstanceProbe+EnumWindowsProc] {
        param($candidateHandle, $parameter)

        $candidateProcessId = [uint32]0
        [void][HistoryClipboardSingleInstanceProbe]::GetWindowThreadProcessId(
            $candidateHandle,
            [ref]$candidateProcessId)
        $visible = [HistoryClipboardSingleInstanceProbe]::IsWindowVisible($candidateHandle)
        if ($candidateProcessId -eq $script:singleInstanceTargetProcessId -and
            (-not $script:singleInstanceVisibleOnly -or $visible)) {
            $script:singleInstanceWindowHandle = $candidateHandle
            return $false
        }

        return $true
    }

    [void][HistoryClipboardSingleInstanceProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:singleInstanceWindowHandle
}

$primary = $null
$secondary = $null
try {
    $primary = Start-Process -FilePath $executable -WindowStyle Normal -PassThru
    $windowHandle = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        $primary.Refresh()
        if ($primary.HasExited) {
            throw "Primary application exited during startup with code $($primary.ExitCode)."
        }

        $windowHandle = Find-ProcessWindow -TargetProcessId $primary.Id -VisibleOnly $true
        if ($windowHandle -ne [IntPtr]::Zero) {
            break
        }
    }

    if ($windowHandle -eq [IntPtr]::Zero) {
        throw "No visible primary application window was found."
    }

    [void][HistoryClipboardSingleInstanceProbe]::SendMessage(
        $windowHandle,
        0x0006,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    if ([HistoryClipboardSingleInstanceProbe]::IsWindowVisible($windowHandle)) {
        throw "The primary window could not be hidden before the activation test."
    }

    $secondary = Start-Process -FilePath $executable -WindowStyle Normal -PassThru
    if (-not $secondary.WaitForExit(5000)) {
        throw "The secondary launch did not exit within five seconds."
    }

    if ($secondary.ExitCode -ne 0) {
        throw "The secondary launch exited with code $($secondary.ExitCode)."
    }

    $wasReactivated = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        Start-Sleep -Milliseconds 40
        if ([HistoryClipboardSingleInstanceProbe]::IsWindowVisible($windowHandle)) {
            $wasReactivated = $true
            break
        }
    }

    if (-not $wasReactivated) {
        throw "The existing primary window was not shown after the secondary launch."
    }

    $applicationProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
    if ($applicationProcesses.Count -ne 1 -or $applicationProcesses[0].Id -ne $primary.Id) {
        throw "Expected exactly one primary process after the secondary launch."
    }

    Write-Output "SECONDARY_LAUNCH=exited"
    Write-Output "PRIMARY_ACTIVATION=window-visible"
    Write-Output "APP_PROCESS_COUNT=1"
}
finally {
    if ($null -ne $secondary) {
        $secondary.Refresh()
        if (-not $secondary.HasExited) {
            Stop-Process -Id $secondary.Id
            $secondary.WaitForExit()
        }
    }

    if ($null -ne $primary) {
        $primary.Refresh()
        if (-not $primary.HasExited) {
            Stop-Process -Id $primary.Id
            $primary.WaitForExit()
        }
    }
}

Write-Output "APP_PROCESSES_RUNNING=none"
