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

$hotkeyName = "ControlShiftV"
$settingsPath = Join-Path $env:LOCALAPPDATA "HistoryClipboard\settings.json"
if (Test-Path -LiteralPath $settingsPath) {
    try {
        $savedHotkey = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).hotkey
        if (-not [string]::IsNullOrWhiteSpace($savedHotkey)) {
            $hotkeyName = $savedHotkey
        }
    }
    catch {
        $hotkeyName = "ControlShiftV"
    }
}

$modifierNoRepeat = 0x4000
switch ($hotkeyName) {
    "ControlShiftV" {
        $hotkeyModifiers = 0x0002 -bor 0x0004 -bor $modifierNoRepeat
        $hotkeyVirtualKey = 0x56
    }
    "ControlAltV" {
        $hotkeyModifiers = 0x0002 -bor 0x0001 -bor $modifierNoRepeat
        $hotkeyVirtualKey = 0x56
    }
    "ControlShiftH" {
        $hotkeyModifiers = 0x0002 -bor 0x0004 -bor $modifierNoRepeat
        $hotkeyVirtualKey = 0x48
    }
    "Disabled" {
        throw "Enable a global hotkey before using this probe."
    }
    default {
        $hotkeyModifiers = 0x0002 -bor 0x0004 -bor $modifierNoRepeat
        $hotkeyVirtualKey = 0x56
    }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HistoryClipboardHotkeyProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int hotkeyId,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr windowHandle, int hotkeyId);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

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

    $script:hotkeyTargetProcessId = [uint32]$TargetProcessId
    $script:hotkeyVisibleOnly = $VisibleOnly
    $script:hotkeyWindowHandle = [IntPtr]::Zero
    $callback = [HistoryClipboardHotkeyProbe+EnumWindowsProc] {
        param($candidateHandle, $parameter)

        $candidateProcessId = [uint32]0
        [void][HistoryClipboardHotkeyProbe]::GetWindowThreadProcessId(
            $candidateHandle,
            [ref]$candidateProcessId)
        $visible = [HistoryClipboardHotkeyProbe]::IsWindowVisible($candidateHandle)
        if ($candidateProcessId -eq $script:hotkeyTargetProcessId -and
            (-not $script:hotkeyVisibleOnly -or $visible)) {
            $script:hotkeyWindowHandle = $candidateHandle
            return $false
        }

        return $true
    }

    [void][HistoryClipboardHotkeyProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:hotkeyWindowHandle
}

function Start-ProbeApplication {
    $process = Start-Process -FilePath $executable -WindowStyle Normal -PassThru
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($process.HasExited) {
            throw "Application exited during startup with code $($process.ExitCode)."
        }

        $windowHandle = Find-ProcessWindow -TargetProcessId $process.Id -VisibleOnly $true
        if ($windowHandle -ne [IntPtr]::Zero) {
            return @($process, $windowHandle)
        }
    }

    throw "No visible application window was found."
}

$application = $null
$conflictApplication = $null
$probeHotkeyId = 0x4844
try {
    $started = Start-ProbeApplication
    $application = $started[0]
    $windowHandle = $started[1]

    $unexpectedRegistration = [HistoryClipboardHotkeyProbe]::RegisterHotKey(
        [IntPtr]::Zero,
        $probeHotkeyId,
        $hotkeyModifiers,
        $hotkeyVirtualKey)
    if ($unexpectedRegistration) {
        [void][HistoryClipboardHotkeyProbe]::UnregisterHotKey([IntPtr]::Zero, $probeHotkeyId)
        throw "The application did not reserve the configured global hotkey."
    }

    [void][HistoryClipboardHotkeyProbe]::SendMessage(
        $windowHandle,
        0x0006,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    if ([HistoryClipboardHotkeyProbe]::IsWindowVisible($windowHandle)) {
        throw "The history window could not be hidden before the hotkey activation check."
    }

    [void][HistoryClipboardHotkeyProbe]::PostMessage(
        $windowHandle,
        0x0312,
        [IntPtr]0x4843,
        [IntPtr]::Zero)
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 50
        if ([HistoryClipboardHotkeyProbe]::IsWindowVisible($windowHandle)) {
            break
        }
    }

    if (-not [HistoryClipboardHotkeyProbe]::IsWindowVisible($windowHandle)) {
        throw "The configured hotkey message did not show the hidden history window."
    }

    Write-Output "GLOBAL_HOTKEY=registered"
    Write-Output "HOTKEY_ACTIVATION=window-visible"

    Stop-Process -Id $application.Id
    $application.WaitForExit()
    $application = $null

    if (-not [HistoryClipboardHotkeyProbe]::RegisterHotKey(
            [IntPtr]::Zero,
            $probeHotkeyId,
            $hotkeyModifiers,
            $hotkeyVirtualKey)) {
        throw "The probe could not reserve the configured hotkey for the conflict check."
    }

    $conflictStarted = Start-ProbeApplication
    $conflictApplication = $conflictStarted[0]
    Start-Sleep -Milliseconds 300
    $conflictApplication.Refresh()
    if ($conflictApplication.HasExited) {
        throw "The application exited when the configured global hotkey was unavailable."
    }

    Write-Output "HOTKEY_CONFLICT=process-still-running"
}
finally {
    [void][HistoryClipboardHotkeyProbe]::UnregisterHotKey([IntPtr]::Zero, $probeHotkeyId)

    foreach ($process in @($application, $conflictApplication)) {
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id
                $process.WaitForExit()
            }
        }
    }
}

Write-Output "APP_PROCESSES_RUNNING=none"
