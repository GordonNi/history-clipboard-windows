param(
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$candidates = [Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates.Add($IsccPath)
}

if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH)) {
    $candidates.Add($env:ISCC_PATH)
}

if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_HOME)) {
    $candidates.Add((Join-Path $env:INNO_SETUP_HOME "ISCC.exe"))
}

$command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($null -ne $command) {
    $candidates.Add($command.Source)
}

$registryLocations = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1"
)
foreach ($registryLocation in $registryLocations) {
    $entry = Get-ItemProperty -LiteralPath $registryLocation -ErrorAction SilentlyContinue
    if ($null -ne $entry -and
        -not [string]::IsNullOrWhiteSpace([string]$entry.InstallLocation)) {
        $candidates.Add((Join-Path ([string]$entry.InstallLocation) "ISCC.exe"))
    }
}

$programFilesX86 = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::ProgramFilesX86)
$programFiles = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::ProgramFiles)
$localApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
foreach ($baseDirectory in @($programFilesX86, $programFiles, $localApplicationData)) {
    if ([string]::IsNullOrWhiteSpace($baseDirectory)) {
        continue
    }

    foreach ($relativePath in @(
            "Inno Setup 6\ISCC.exe",
            "Inno Setup 7\ISCC.exe",
            "Programs\Inno Setup 6\ISCC.exe",
            "Programs\Inno Setup 7\ISCC.exe")) {
        $candidates.Add((Join-Path $baseDirectory $relativePath))
    }
}

$resolved = $candidates |
    Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        (Test-Path -LiteralPath $_ -PathType Leaf)
    } |
    ForEach-Object { [IO.Path]::GetFullPath($_) } |
    Select-Object -Unique -First 1

if ($null -eq $resolved) {
    throw (
        "ISCC.exe was not found. No compiler was installed and PATH was not changed. " +
        "Install Inno Setup separately or pass -IsccPath with the full path to ISCC.exe."
    )
}

Write-Output $resolved
