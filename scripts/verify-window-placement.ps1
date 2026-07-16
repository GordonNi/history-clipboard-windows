param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$dotnet = & (Join-Path $PSScriptRoot "resolve-dotnet.ps1")
$dotnetRoot = Split-Path -Parent $dotnet
$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"

$existingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($existingProcesses.Count -ne 0) {
    throw "Close the existing HistoryClipboard.App process before running this probe."
}

$executable = Resolve-Path (
    "src\HistoryClipboard.App\bin\{0}\net10.0-windows\HistoryClipboard.App.exe" -f $Configuration)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HistoryClipboardPlacementProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint Size;
        public Rectangle MonitorArea;
        public Rectangle WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr windowHandle, out Rectangle rectangle);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("shcore.dll")]
    public static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);
}
'@

function Find-ApplicationWindow {
    param(
        [int]$ProcessId,
        [bool]$VisibleOnly
    )

    $script:foundWindow = [IntPtr]::Zero
    $callback = [HistoryClipboardPlacementProbe+EnumWindowsProc] {
        param($candidateHandle, $parameter)

        $candidateProcessId = [uint32]0
        [void][HistoryClipboardPlacementProbe]::GetWindowThreadProcessId(
            $candidateHandle,
            [ref]$candidateProcessId)
        if ($candidateProcessId -eq [uint32]$ProcessId -and
            (-not $VisibleOnly -or
                [HistoryClipboardPlacementProbe]::IsWindowVisible($candidateHandle))) {
            $script:foundWindow = $candidateHandle
            return $false
        }

        return $true
    }

    [void][HistoryClipboardPlacementProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:foundWindow
}

function Wait-ForVisibleWindow {
    param(
        [System.Diagnostics.Process]$Process,
        [IntPtr]$ExpectedHandle = [IntPtr]::Zero
    )

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Application exited during placement verification with code $($Process.ExitCode)."
        }

        $windowHandle = Find-ApplicationWindow -ProcessId $Process.Id -VisibleOnly $true
        if ($windowHandle -ne [IntPtr]::Zero) {
            if ($ExpectedHandle -ne [IntPtr]::Zero -and $windowHandle -ne $ExpectedHandle) {
                throw "Activation created a different application window."
            }

            return $windowHandle
        }
    }

    throw "The application window did not become visible."
}

function Get-PlacementSnapshot {
    param([IntPtr]$WindowHandle)

    $rectangle = New-Object HistoryClipboardPlacementProbe+Rectangle
    if (-not [HistoryClipboardPlacementProbe]::GetWindowRect($WindowHandle, [ref]$rectangle)) {
        throw "GetWindowRect failed."
    }

    $monitor = [HistoryClipboardPlacementProbe]::MonitorFromWindow($WindowHandle, 2)
    if ($monitor -eq [IntPtr]::Zero) {
        throw "MonitorFromWindow did not select a monitor."
    }

    $monitorInfo = New-Object HistoryClipboardPlacementProbe+MonitorInfo
    $monitorInfo.Size = [uint32][Runtime.InteropServices.Marshal]::SizeOf($monitorInfo)
    if (-not [HistoryClipboardPlacementProbe]::GetMonitorInfo($monitor, [ref]$monitorInfo)) {
        throw "GetMonitorInfo failed."
    }

    if ($rectangle.Left -lt $monitorInfo.WorkArea.Left -or
        $rectangle.Top -lt $monitorInfo.WorkArea.Top -or
        $rectangle.Right -gt $monitorInfo.WorkArea.Right -or
        $rectangle.Bottom -gt $monitorInfo.WorkArea.Bottom) {
        throw "Application window is outside its selected monitor work area."
    }

    return [pscustomobject]@{
        Left = $rectangle.Left
        Top = $rectangle.Top
        Right = $rectangle.Right
        Bottom = $rectangle.Bottom
        WorkLeft = $monitorInfo.WorkArea.Left
        WorkTop = $monitorInfo.WorkArea.Top
        WorkRight = $monitorInfo.WorkArea.Right
        WorkBottom = $monitorInfo.WorkArea.Bottom
        Dpi = [HistoryClipboardPlacementProbe]::GetDpiForWindow($WindowHandle)
    }
}

function Hide-ApplicationWindow {
    param([IntPtr]$WindowHandle)

    [void][HistoryClipboardPlacementProbe]::SendMessage(
        $WindowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    if ([HistoryClipboardPlacementProbe]::IsWindowVisible($WindowHandle)) {
        throw "WM_CLOSE did not hide the application window."
    }
}

$primary = $null
$secondaryProcesses = @()
try {
    $primary = Start-Process -FilePath $executable -WindowStyle Normal -PassThru
    $windowHandle = Wait-ForVisibleWindow -Process $primary
    $initial = Get-PlacementSnapshot -WindowHandle $windowHandle

    $awareness = -1
    $awarenessResult = [HistoryClipboardPlacementProbe]::GetProcessDpiAwareness(
        $primary.Handle,
        [ref]$awareness)
    if ($awarenessResult -ne 0 -or $awareness -ne 2) {
        throw "Application is not per-monitor DPI aware (HRESULT=$awarenessResult, awareness=$awareness)."
    }

    $activationSnapshots = @()
    for ($activation = 0; $activation -lt 2; $activation++) {
        Hide-ApplicationWindow -WindowHandle $windowHandle
        $secondary = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
        $secondaryProcesses += $secondary
        if (-not $secondary.WaitForExit(5000)) {
            throw "Secondary activation process did not exit."
        }

        if ($secondary.ExitCode -ne 0) {
            throw "Secondary activation exited with code $($secondary.ExitCode)."
        }

        [void](Wait-ForVisibleWindow -Process $primary -ExpectedHandle $windowHandle)
        $activationSnapshots += Get-PlacementSnapshot -WindowHandle $windowHandle
    }

    $first = $activationSnapshots[0]
    $second = $activationSnapshots[1]
    if ($first.Left -ne $second.Left -or
        $first.Top -ne $second.Top -or
        $first.Right -ne $second.Right -or
        $first.Bottom -ne $second.Bottom -or
        $first.WorkLeft -ne $second.WorkLeft -or
        $first.WorkTop -ne $second.WorkTop -or
        $first.WorkRight -ne $second.WorkRight -or
        $first.WorkBottom -ne $second.WorkBottom) {
        throw "Repeated activation did not produce stable placement."
    }

    Write-Output "DPI_AWARENESS=per-monitor"
    Write-Output "WINDOW_DPI=$($second.Dpi)"
    Write-Output "WINDOW_PLACEMENT=inside-selected-work-area"
    Write-Output "REPEATED_SHOW=stable"
}
finally {
    foreach ($secondary in $secondaryProcesses) {
        if ($null -ne $secondary) {
            $secondary.Refresh()
            if (-not $secondary.HasExited) {
                Stop-Process -Id $secondary.Id
                $secondary.WaitForExit()
            }
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

$remainingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($remainingProcesses.Count -ne 0) {
    throw "Placement verification left an application process running."
}

Write-Output "APP_PROCESSES_RUNNING=none"
