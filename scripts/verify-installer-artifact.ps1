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
$publishExecutable = Join-Path $artifactsRoot (
    "portable\$packageName\HistoryClipboard.App.exe")

foreach ($path in @($installerPath, $checksumPath, $publishExecutable)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required installer artifact is missing: $path"
    }
}

$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
if ($checksumLine -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
    throw "Installer checksum file does not use the expected SHA-256 format."
}

$expectedHash = $Matches[1].ToLowerInvariant()
if ($Matches[2] -ne [IO.Path]::GetFileName($installerPath)) {
    throw "Installer checksum file names a different artifact."
}

$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).
    Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "Installer SHA-256 mismatch."
}

$localApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
$fixedAppId = "{92B90231-45C7-4382-BD5A-9216D18FC0D1}"
$uninstallKeyPath = "Software\Microsoft\Windows\CurrentVersion\Uninstall"

function Get-HistoryClipboardInstallEvidence {
    $evidence = [Collections.Generic.List[string]]::new()
    $defaultInstallDirectory = Join-Path $localApplicationData (
        "Programs\HistoryClipboard")
    $defaultExecutable = Join-Path $defaultInstallDirectory (
        "HistoryClipboard.App.exe")
    if (Test-Path -LiteralPath $defaultExecutable) {
        $evidence.Add("default executable: $defaultExecutable")
    }
    elseif (Test-Path -LiteralPath $defaultInstallDirectory) {
        $evidence.Add(
            "default install directory (partial or damaged install): " +
            $defaultInstallDirectory)
    }

    foreach ($programsFolder in @(
            [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs),
            [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms))) {
        if ([string]::IsNullOrWhiteSpace($programsFolder)) {
            continue
        }

        $groupPath = Join-Path $programsFolder "HistoryClipboard"
        if (Test-Path -LiteralPath $groupPath -PathType Container) {
            $evidence.Add("Start Menu group: $groupPath")
        }
    }

    $registryLocations = @(
        [pscustomobject]@{
            Hive = [Microsoft.Win32.RegistryHive]::CurrentUser
            HiveName = "HKCU"
            View = [Microsoft.Win32.RegistryView]::Registry64
        },
        [pscustomobject]@{
            Hive = [Microsoft.Win32.RegistryHive]::CurrentUser
            HiveName = "HKCU"
            View = [Microsoft.Win32.RegistryView]::Registry32
        },
        [pscustomobject]@{
            Hive = [Microsoft.Win32.RegistryHive]::LocalMachine
            HiveName = "HKLM"
            View = [Microsoft.Win32.RegistryView]::Registry64
        },
        [pscustomobject]@{
            Hive = [Microsoft.Win32.RegistryHive]::LocalMachine
            HiveName = "HKLM"
            View = [Microsoft.Win32.RegistryView]::Registry32
        })

    foreach ($location in $registryLocations) {
        $baseKey = $null
        $uninstallKey = $null
        try {
            $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
                $location.Hive,
                $location.View)
            $uninstallKey = $baseKey.OpenSubKey($uninstallKeyPath, $false)
            if ($null -eq $uninstallKey) {
                continue
            }

            foreach ($subKeyName in $uninstallKey.GetSubKeyNames()) {
                $productKey = $null
                try {
                    $productKey = $uninstallKey.OpenSubKey($subKeyName, $false)
                    if ($null -eq $productKey) {
                        continue
                    }

                    $displayName = $productKey.GetValue(
                        "DisplayName",
                        $null,
                        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                    $matchesAppId = $subKeyName.Equals(
                        $fixedAppId,
                        [StringComparison]::OrdinalIgnoreCase) -or
                        $subKeyName.Equals(
                            ($fixedAppId + "_is1"),
                            [StringComparison]::OrdinalIgnoreCase)
                    $matchesDisplayName = $displayName -is [string] -and
                        $displayName.StartsWith(
                            "HistoryClipboard",
                            [StringComparison]::OrdinalIgnoreCase)
                    if ($matchesAppId -or $matchesDisplayName) {
                        $displayText = if ($displayName -is [string]) {
                            $displayName
                        }
                        else {
                            "<missing>"
                        }
                        $evidence.Add(
                            "uninstall entry: $($location.HiveName) " +
                            "$($location.View)\$subKeyName " +
                            "(DisplayName=$displayText)")
                    }
                }
                finally {
                    if ($null -ne $productKey) {
                        $productKey.Dispose()
                    }
                }
            }
        }
        catch {
            throw (
                "Could not safely inspect $($location.HiveName) " +
                "$($location.View) uninstall state. The installer artifact " +
                "probe refuses to continue: $($_.Exception.Message)")
        }
        finally {
            if ($null -ne $uninstallKey) {
                $uninstallKey.Dispose()
            }
            if ($null -ne $baseKey) {
                $baseKey.Dispose()
            }
        }
    }

    return @($evidence | Select-Object -Unique)
}

$installEvidence = @(Get-HistoryClipboardInstallEvidence)
if ($installEvidence.Count -ne 0) {
    throw (
        "Installer artifact probe refused: HistoryClipboard installation " +
        "evidence exists in this profile or machine state. This probe may run " +
        "only in a configuration with no installed product, such as a " +
        "disposable profile. User data in %LOCALAPPDATA%\HistoryClipboard may " +
        "exist and will still be fingerprint-protected. Evidence: " +
        ($installEvidence -join "; "))
}

$existingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($existingProcesses.Count -ne 0) {
    throw "Close HistoryClipboard.App before running the installer artifact probe."
}

$userDataDirectory = [IO.Path]::GetFullPath(
    (Join-Path $localApplicationData "HistoryClipboard"))
$runKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "HistoryClipboard"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "HistoryClipboard-installer-artifact-" + [Guid]::NewGuid().ToString("N"))
$installDirectory = Join-Path $tempRoot "app"
$runSnapshot = $null
$userDataSnapshot = $null
$installedExecutable = $null
$uninstaller = $null
$probeCompleted = $false

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
        $key.SetValue(
            $runValueName,
            ('"{0}"' -f $Executable),
            [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally {
        $key.Dispose()
    }
}

function Get-UserDataSnapshot {
    if (-not (Test-Path -LiteralPath $userDataDirectory -PathType Container)) {
        return [pscustomobject]@{ Exists = $false; Fingerprint = "absent" }
    }

    $prefix = $userDataDirectory.TrimEnd('\') + '\'
    $root = Get-Item -LiteralPath $userDataDirectory -Force
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($entry in @(Get-ChildItem -LiteralPath $userDataDirectory -Force -Recurse |
            Sort-Object FullName)) {
        if (-not $entry.FullName.StartsWith(
            $prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "User-data entry resolved outside its expected directory."
        }

        $relativePath = $entry.FullName.Substring($prefix.Length).Replace('\', '/')
        if ($entry.PSIsContainer) {
            $entries.Add([pscustomobject]@{
                Path = $relativePath
                Type = "directory"
                LastWriteTimeUtc = $entry.LastWriteTimeUtc.ToString("O")
            })
        }
        else {
            $entries.Add([pscustomobject]@{
                Path = $relativePath
                Type = "file"
                Length = $entry.Length
                LastWriteTimeUtc = $entry.LastWriteTimeUtc.ToString("O")
                Sha256 = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).
                    Hash.ToLowerInvariant()
            })
        }
    }

    $snapshotJson = [pscustomobject]@{
        RootLastWriteTimeUtc = $root.LastWriteTimeUtc.ToString("O")
        Entries = $entries
    } | ConvertTo-Json -Depth 8 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($snapshotJson)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $fingerprint = ([BitConverter]::ToString($sha256.ComputeHash($bytes))).
            Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }

    return [pscustomobject]@{ Exists = $true; Fingerprint = $fingerprint }
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
    $userDataSnapshot = Get-UserDataSnapshot
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    $installArguments = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/NOCLOSEAPPLICATIONS",
        "/NORESTARTAPPLICATIONS",
        "/NOICONS",
        "/DIR=$installDirectory"
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
    foreach ($relativePath in @(
            "HistoryClipboard.App.exe",
            "HistoryClipboard.App.dll",
            "HistoryClipboard.App.deps.json",
            "HistoryClipboard.App.runtimeconfig.json",
            "coreclr.dll",
            "hostfxr.dll",
            "hostpolicy.dll")) {
        if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $relativePath) -PathType Leaf)) {
            throw "Temporary install is missing required file: $relativePath"
        }
    }

    $installedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
        $installedExecutable).ProductVersion
    if ($installedVersion -ne "$Version+unsigned") {
        throw "Installed executable has unexpected product version: $installedVersion"
    }

    $publishExecutableHash = (Get-FileHash -LiteralPath $publishExecutable `
        -Algorithm SHA256).Hash
    $installedExecutableHash = (Get-FileHash -LiteralPath $installedExecutable `
        -Algorithm SHA256).Hash
    if ($installedExecutableHash -ne $publishExecutableHash) {
        throw "Installed executable does not match the validated portable publish input."
    }

    $installedExecutableText = [Text.Encoding]::UTF8.GetString(
        [IO.File]::ReadAllBytes($installedExecutable))
    if (-not $installedExecutableText.Contains("PerMonitorV2")) {
        throw "Installed executable is missing the PerMonitorV2 manifest declaration."
    }

    $installedAssembly = Join-Path $installDirectory "HistoryClipboard.App.dll"
    $installedAssemblyBytes = [IO.File]::ReadAllBytes($installedAssembly)
    $installedAssemblyUtf8 = [Text.Encoding]::UTF8.GetString($installedAssemblyBytes)
    $installedAssemblyUnicode = [Text.Encoding]::Unicode.GetString($installedAssemblyBytes)
    foreach ($startupGuardMarker in @(
            "StartupFailureCoordinator",
            "HC-START-001",
            "HC-START-002",
            "HC-START-999")) {
        if (-not $installedAssemblyUtf8.Contains($startupGuardMarker) -and
            -not $installedAssemblyUnicode.Contains($startupGuardMarker)) {
            throw "Installed application is missing startup-failure guard marker: $startupGuardMarker"
        }
    }

    $uninstaller = Get-ChildItem -LiteralPath $installDirectory -Filter "unins*.exe" -File |
        Select-Object -First 1
    if ($null -eq $uninstaller) {
        throw "Temporary install is missing its uninstaller."
    }

    $afterInstallData = Get-UserDataSnapshot
    if ($afterInstallData.Exists -ne $userDataSnapshot.Exists -or
        $afterInstallData.Fingerprint -ne $userDataSnapshot.Fingerprint) {
        throw "File-only install changed the existing user-data directory."
    }

    Set-OwnedRunValue -Executable $installedExecutable
    $uninstallProcess = Start-Process `
        -FilePath $uninstaller.FullName `
        -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Silent uninstaller exited with code $($uninstallProcess.ExitCode)."
    }

    for ($attempt = 0; $attempt -lt 300 -and
        (Test-Path -LiteralPath $installDirectory); $attempt++) {
        Start-Sleep -Milliseconds 100
    }

    if (Test-Path -LiteralPath $installDirectory) {
        $remainingEntries = @(
            Get-ChildItem -LiteralPath $installDirectory -Force -Recurse |
                ForEach-Object { $_.FullName.Substring($installDirectory.Length) })
        throw (
            "Uninstall left the temporary program directory behind. Remaining entries: " +
            "$($remainingEntries -join ', ')")
    }

    $afterUninstallRun = Get-RunValueSnapshot
    if ($afterUninstallRun.Exists) {
        throw "Uninstall did not remove the Run value owned by the temporary install."
    }

    $afterUninstallData = Get-UserDataSnapshot
    if ($afterUninstallData.Exists -ne $userDataSnapshot.Exists -or
        $afterUninstallData.Fingerprint -ne $userDataSnapshot.Fingerprint) {
        throw "Silent uninstall changed the existing user-data directory."
    }

    $probeCompleted = $true
}
finally {
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
    }
}

$restoredRunSnapshot = Get-RunValueSnapshot
if (-not (Test-SnapshotEqual -Expected $runSnapshot -Actual $restoredRunSnapshot)) {
    throw "The original HistoryClipboard Run value was not restored exactly."
}

$restoredDataSnapshot = Get-UserDataSnapshot
if ($restoredDataSnapshot.Exists -ne $userDataSnapshot.Exists -or
    $restoredDataSnapshot.Fingerprint -ne $userDataSnapshot.Fingerprint) {
    throw "The original user-data directory state was not preserved exactly."
}

if (-not $probeCompleted) {
    throw "The installer artifact probe did not complete all assertions."
}

$remainingProcesses = @(Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue)
if ($remainingProcesses.Count -ne 0) {
    throw "Installer verification left an application process running."
}

Write-Output "INSTALLER_SHA256=valid"
Write-Output "SILENT_INSTALL=file-only-passed"
Write-Output "APPLICATION_START=not-run"
Write-Output "INSTALLED_VERSION=$Version+unsigned"
Write-Output "INSTALLED_INPUT_MATCH=portable-publish"
Write-Output "INSTALLED_DPI_MANIFEST=PerMonitorV2"
Write-Output "INSTALLED_STARTUP_FAILURE_GUARD=present"
Write-Output "UNINSTALLER=present-and-executed"
Write-Output "SILENT_UNINSTALL=passed"
Write-Output "PROGRAM_FILES_REMOVED=yes"
Write-Output "OWNED_RUN_VALUE_REMOVED=yes"
Write-Output "ORIGINAL_RUN_VALUE=restored"
Write-Output "ORIGINAL_USER_DATA_STATE=preserved"
Write-Output "APP_PROCESSES_RUNNING=none"
