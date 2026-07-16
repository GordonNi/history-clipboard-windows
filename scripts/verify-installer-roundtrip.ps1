param(
    [string]$Version = "0.1.0-preview.2",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$packageName = "HistoryClipboard-$Version-unsigned-$RuntimeIdentifier"
$installerPath = Join-Path $artifactsRoot "$packageName-setup.exe"
$checksumPath = "$installerPath.sha256"

foreach ($path in @($installerPath, $checksumPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required installer artifact is missing: $path"
    }
}

$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
if ($checksumLine -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
    throw "Installer checksum file does not use the expected SHA-256 format."
}

if ($Matches[2] -ne [IO.Path]::GetFileName($installerPath)) {
    throw "Installer checksum file names a different artifact."
}

$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).
    Hash.ToLowerInvariant()
if ($actualHash -ne $Matches[1].ToLowerInvariant()) {
    throw "Installer SHA-256 mismatch."
}

$existingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($existingProcesses.Count -ne 0) {
    throw "Close HistoryClipboard.App before running the installer round-trip probe."
}

$localApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
$userDataDirectory = Join-Path $localApplicationData "HistoryClipboard"
if (Test-Path -LiteralPath $userDataDirectory) {
    throw (
        "The installer round-trip probe requires a disposable user profile with no existing " +
        "$userDataDirectory directory. It will not risk launching against real clipboard data."
    )
}

$runKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "HistoryClipboard"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "HistoryClipboard-installer-roundtrip-" + [Guid]::NewGuid().ToString("N"))
$installDirectory = Join-Path $tempRoot "app"
$sentinelPath = Join-Path $userDataDirectory "installer-probe-sentinel.txt"
$sentinelText = "HistoryClipboard installer probe " + [Guid]::NewGuid().ToString("N")
$application = $null
$dataDirectoryCreatedByProbe = $false
$runSnapshot = $null
$ownedRunWasRemoved = $false
$silentUninstallPreservedData = $false
$roundTripCompleted = $false

function Get-RunValueSnapshot {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $false)
    try {
        if ($null -eq $key -or $runValueName -notin $key.GetValueNames()) {
            return [pscustomobject]@{ Exists = $false; Kind = $null; Value = $null }
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

function Set-OwnedRunValue {
    param([string]$Executable)

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKeyPath, $true)
    try {
        $key.SetValue($runValueName, ('"{0}"' -f $Executable),
            [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally {
        $key.Dispose()
    }
}

function Assert-TempPath {
    param([string]$Path)

    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the temporary directory: $resolved"
    }
}

try {
    $runSnapshot = Get-RunValueSnapshot
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $userDataDirectory -Force | Out-Null
    $dataDirectoryCreatedByProbe = $true
    [IO.File]::WriteAllText(
        $sentinelPath,
        $sentinelText,
        [Text.UTF8Encoding]::new($false))

    $installArguments = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/NOCLOSEAPPLICATIONS",
        "/NORESTARTAPPLICATIONS",
        ('/DIR="{0}"' -f $installDirectory)
    )
    $installProcess = Start-Process `
        -FilePath $installerPath `
        -ArgumentList $installArguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($installProcess.ExitCode -ne 0) {
        throw "Silent installer exited with code $($installProcess.ExitCode)."
    }

    $installedExecutable = Join-Path $installDirectory "HistoryClipboard.App.exe"
    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
        throw "The temporary install is missing HistoryClipboard.App.exe."
    }

    $application = Start-Process -FilePath $installedExecutable -PassThru
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        $application.Refresh()
        if ($application.HasExited) {
            throw "The installed application exited during startup with code $($application.ExitCode)."
        }

        if ($application.MainWindowHandle -ne [IntPtr]::Zero) {
            break
        }
    }

    $application.Refresh()
    if ($application.HasExited) {
        throw "The installed application did not remain running."
    }

    Stop-Process -Id $application.Id
    $application.WaitForExit()
    $application = $null

    Set-OwnedRunValue -Executable $installedExecutable
    $uninstaller = Get-ChildItem -LiteralPath $installDirectory -Filter "unins*.exe" -File |
        Select-Object -First 1
    if ($null -eq $uninstaller) {
        throw "The temporary install is missing its uninstaller."
    }

    $uninstallProcess = Start-Process `
        -FilePath $uninstaller.FullName `
        -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Silent uninstaller exited with code $($uninstallProcess.ExitCode)."
    }

    for ($attempt = 0; $attempt -lt 30 -and
        (Test-Path -LiteralPath $installDirectory); $attempt++) {
        Start-Sleep -Milliseconds 100
    }

    if (Test-Path -LiteralPath $installDirectory) {
        throw "Uninstall left the temporary program directory behind."
    }

    $afterUninstallRun = Get-RunValueSnapshot
    $ownedRunWasRemoved = -not $afterUninstallRun.Exists
    if (-not $ownedRunWasRemoved) {
        throw "Uninstall did not remove the Run value owned by the temporary install."
    }

    $silentUninstallPreservedData =
        (Test-Path -LiteralPath $sentinelPath -PathType Leaf) -and
        ((Get-Content -LiteralPath $sentinelPath -Raw) -eq $sentinelText)
    if (-not $silentUninstallPreservedData) {
        throw "Silent uninstall removed or changed the user-data sentinel."
    }

    $roundTripCompleted = $true
}
finally {
    if ($null -ne $application) {
        $application.Refresh()
        if (-not $application.HasExited) {
            Stop-Process -Id $application.Id
            $application.WaitForExit()
        }
    }

    try {
        if ($null -ne $runSnapshot) {
            Restore-RunValue -Snapshot $runSnapshot
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Assert-TempPath -Path $tempRoot
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }

        if ($dataDirectoryCreatedByProbe -and
            (Test-Path -LiteralPath $userDataDirectory)) {
            Remove-Item -LiteralPath $userDataDirectory -Recurse -Force
        }
    }
}

$restoredRunSnapshot = Get-RunValueSnapshot
if (-not (Test-SnapshotEqual -Expected $runSnapshot -Actual $restoredRunSnapshot)) {
    throw "The original HistoryClipboard Run value was not restored exactly."
}

if (Test-Path -LiteralPath $userDataDirectory) {
    throw "The probe did not restore the original absent user-data-directory state."
}

if (-not $roundTripCompleted -or -not $ownedRunWasRemoved -or
    -not $silentUninstallPreservedData) {
    throw "The installer round trip did not complete all safety assertions."
}

$remainingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($remainingProcesses.Count -ne 0) {
    throw "Installer verification left an application process running."
}

Write-Output "INSTALLER_SHA256=valid"
Write-Output "SILENT_INSTALL=passed"
Write-Output "INSTALLED_STARTUP=passed"
Write-Output "SILENT_UNINSTALL=passed"
Write-Output "PROGRAM_FILES_REMOVED=yes"
Write-Output "OWNED_RUN_VALUE_REMOVED=yes"
Write-Output "SILENT_UNINSTALL_USER_DATA=preserved"
Write-Output "ORIGINAL_RUN_VALUE=restored"
Write-Output "ORIGINAL_USER_DATA_STATE=restored"
Write-Output "APP_PROCESSES_RUNNING=none"
