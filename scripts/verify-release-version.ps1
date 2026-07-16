param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Tag
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+-preview\.\d+$') {
    throw "Release version must match 0.1.0-preview.2 style syntax."
}

if (-not [string]::IsNullOrWhiteSpace($Tag) -and $Tag -ne "v$Version") {
    throw "Tag $Tag does not match release version v$Version."
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $projectRoot (
    "src\HistoryClipboard.App\HistoryClipboard.App.csproj")
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
$informationalVersion = [string](
    $projectXml.Project.PropertyGroup.InformationalVersion)

if ($projectVersion -ne $Version) {
    throw "Project version $projectVersion does not match requested version $Version."
}

if ($informationalVersion -ne "$Version+unsigned") {
    throw (
        "Project informational version $informationalVersion does not match " +
        "$Version+unsigned.")
}

$versionedScripts = @(
    "build-portable-release.ps1",
    "verify-portable-package.ps1",
    "build-installer-release.ps1",
    "verify-installer-definition.ps1",
    "verify-installer-artifact.ps1",
    "verify-installer-roundtrip.ps1"
)

foreach ($scriptName in $versionedScripts) {
    $scriptPath = Join-Path $PSScriptRoot $scriptName
    $scriptText = Get-Content -LiteralPath $scriptPath -Raw
    $match = [regex]::Match(
        $scriptText,
        '\[string\]\$Version\s*=\s*"(?<version>[^"]+)"')
    if (-not $match.Success) {
        throw "Could not find the default release version in $scriptName."
    }

    $scriptVersion = $match.Groups["version"].Value
    if ($scriptVersion -ne $Version) {
        throw (
            "Script $scriptName defaults to $scriptVersion instead of " +
            "$Version.")
    }
}

$installerPath = Join-Path $projectRoot "installer\HistoryClipboard.iss"
$installerText = Get-Content -LiteralPath $installerPath -Raw
$installerMatch = [regex]::Match(
    $installerText,
    '(?m)^\s*#define AppVersion "(?<version>[^"]+)"\s*$')
if (-not $installerMatch.Success -or
    $installerMatch.Groups["version"].Value -ne $Version) {
    throw "The Inno Setup default version does not match $Version."
}

$releaseNotesPath = Join-Path $projectRoot (
    "docs\release-notes\v$Version.md")
if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
    throw "Release notes are missing: $releaseNotesPath"
}

Write-Output "RELEASE_VERSION=$Version"
Write-Output "INFORMATIONAL_VERSION=$Version+unsigned"
Write-Output "TAG_VERSION=valid"
Write-Output "SCRIPT_DEFAULTS=valid"
Write-Output "RELEASE_NOTES=present"
