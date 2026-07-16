param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$dotnet = & (Join-Path $PSScriptRoot "resolve-dotnet.ps1")
$dotnetRoot = Split-Path -Parent $dotnet
$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"

$executable = (Resolve-Path (
    "src\HistoryClipboard.App\bin\{0}\net10.0-windows\HistoryClipboard.App.exe" -f $Configuration)).Path
$existingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($existingProcesses.Count -ne 0) {
    throw "Close the running HistoryClipboard.App process before using this probe."
}

$runKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "HistoryClipboard"
$expectedCommand = '"{0}"' -f $executable

Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HistoryClipboardLaunchProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr windowHandle);
}
'@

function Get-RunValueSnapshot {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $false)
    try {
        if ($null -eq $key -or $runValueName -notin $key.GetValueNames()) {
            return [pscustomobject]@{
                Exists = $false
                Kind = $null
                Value = $null
            }
        }

        return [pscustomobject]@{
            Exists = $true
            Kind = $key.GetValueKind($runValueName)
            Value = $key.GetValue(
                $runValueName,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    finally {
        if ($null -ne $key) {
            $key.Dispose()
        }
    }
}

function Remove-RunValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $true)
    try {
        if ($null -ne $key) {
            $key.DeleteValue($runValueName, $false)
        }
    }
    finally {
        if ($null -ne $key) {
            $key.Dispose()
        }
    }
}

function Restore-RunValue {
    param($Snapshot)

    if (-not $Snapshot.Exists) {
        Remove-RunValue
        return
    }

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKeyPath, $true)
    try {
        $key.SetValue($runValueName, $Snapshot.Value, $Snapshot.Kind)
    }
    finally {
        $key.Dispose()
    }
}

function Test-SnapshotEqual {
    param($Expected, $Actual)

    if ($Expected.Exists -ne $Actual.Exists) {
        return $false
    }

    if (-not $Expected.Exists) {
        return $true
    }

    if ($Expected.Kind -ne $Actual.Kind) {
        return $false
    }

    if ($Expected.Value -is [Array] -or $Actual.Value -is [Array]) {
        if ($Expected.Value.Count -ne $Actual.Value.Count) {
            return $false
        }

        for ($index = 0; $index -lt $Expected.Value.Count; $index++) {
            if (-not [object]::Equals($Expected.Value[$index], $Actual.Value[$index])) {
                return $false
            }
        }

        return $true
    }

    return [object]::Equals($Expected.Value, $Actual.Value)
}

function Find-ProcessWindow {
    param([int]$TargetProcessId)

    $script:launchTargetProcessId = [uint32]$TargetProcessId
    $script:launchWindowHandle = [IntPtr]::Zero
    $callback = [HistoryClipboardLaunchProbe+EnumWindowsProc] {
        param($candidateHandle, $parameter)

        $candidateProcessId = [uint32]0
        [void][HistoryClipboardLaunchProbe]::GetWindowThreadProcessId(
            $candidateHandle,
            [ref]$candidateProcessId)
        if ($candidateProcessId -eq $script:launchTargetProcessId -and
            [HistoryClipboardLaunchProbe]::IsWindowVisible($candidateHandle)) {
            $script:launchWindowHandle = $candidateHandle
            return $false
        }

        return $true
    }

    [void][HistoryClipboardLaunchProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:launchWindowHandle
}

function Wait-ForRunValue {
    param(
        [bool]$ExpectedToExist,
        [string]$ExpectedValue = ""
    )

    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 50
        $snapshot = Get-RunValueSnapshot
        if ($snapshot.Exists -eq $ExpectedToExist -and
            (-not $ExpectedToExist -or $snapshot.Value -ceq $ExpectedValue)) {
            return
        }
    }

    throw "The HKCU Run value did not reach the expected state."
}

$originalSnapshot = Get-RunValueSnapshot
$application = $null
$restoreSucceeded = $false
try {
    Remove-RunValue
    $application = Start-Process -FilePath $executable -WindowStyle Normal -PassThru

    $windowHandle = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 100
        $application.Refresh()
        if ($application.HasExited) {
            throw "Application exited during startup with code $($application.ExitCode)."
        }

        $windowHandle = Find-ProcessWindow -TargetProcessId $application.Id
        if ($windowHandle -ne [IntPtr]::Zero) {
            break
        }
    }

    if ($windowHandle -eq [IntPtr]::Zero) {
        throw "No application window was found."
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)
    $settingsCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "HistorySettingsExpander")
    $settings = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $settingsCondition)
    if ($null -eq $settings) {
        throw "The history settings expander was not exposed to UI Automation."
    }

    $expandPattern = $settings.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expandPattern.Expand()

    $checkboxCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "LaunchAtLoginCheckBox")
    $checkbox = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 50
        $checkbox = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $checkboxCondition)
        if ($null -ne $checkbox) {
            break
        }
    }

    if ($null -eq $checkbox) {
        throw "The launch-at-login checkbox was not exposed to UI Automation."
    }

    if ([string]::IsNullOrWhiteSpace($checkbox.Current.Name)) {
        throw "The launch-at-login checkbox does not expose an accessible name."
    }

    $togglePattern = $checkbox.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)
    if ($togglePattern.Current.ToggleState -ne
        [System.Windows.Automation.ToggleState]::Off) {
        throw "The launch-at-login checkbox did not reflect the missing Run value."
    }

    $togglePattern.Toggle()
    Wait-ForRunValue -ExpectedToExist $true -ExpectedValue $expectedCommand
    Write-Output "LAUNCH_AT_LOGIN=enabled"
    Write-Output "ACCESSIBLE_CONTROL=available"

    $togglePattern.Toggle()
    Wait-ForRunValue -ExpectedToExist $false
    Write-Output "LAUNCH_AT_LOGIN=disabled"
}
finally {
    if ($null -ne $application) {
        $application.Refresh()
        if (-not $application.HasExited) {
            Stop-Process -Id $application.Id
            $application.WaitForExit()
        }
    }

    Restore-RunValue -Snapshot $originalSnapshot
    $restoredSnapshot = Get-RunValueSnapshot
    $restoreSucceeded = Test-SnapshotEqual -Expected $originalSnapshot -Actual $restoredSnapshot
    if (-not $restoreSucceeded) {
        throw "The original HKCU Run value was not restored exactly."
    }
}

Write-Output "ORIGINAL_RUN_VALUE=restored"
Write-Output "APP_PROCESSES_RUNNING=none"
