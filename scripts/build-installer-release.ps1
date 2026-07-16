param(
    [string]$Version = "0.1.0-preview.2",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+-preview\.\d+$') {
    throw "Installer releases must use an explicit preview version."
}

if ($RuntimeIdentifier -ne "win-x64") {
    throw "The first installer supports only win-x64."
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$packageName = "HistoryClipboard-$Version-unsigned-$RuntimeIdentifier"
$publishDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "portable\$packageName"))
$installerBaseName = "$packageName-setup"
$installerPath = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "$installerBaseName.exe"))
$checksumPath = "$installerPath.sha256"
$definitionPath = Join-Path $projectRoot "installer\HistoryClipboard.iss"
$artifactsPrefix = $artifactsRoot.TrimEnd('\') + '\'

foreach ($path in @($publishDirectory, $installerPath, $checksumPath)) {
    if (-not $path.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use an installer path outside artifacts: $path"
    }
}

& (Join-Path $PSScriptRoot "build-portable-release.ps1") `
    -Version $Version `
    -Configuration $Configuration `
    -RuntimeIdentifier $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "Portable publish preparation failed with exit code $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot "verify-installer-definition.ps1") `
    -Version $Version `
    -RuntimeIdentifier $RuntimeIdentifier `
    -IsccPath $IsccPath
if ($LASTEXITCODE -ne 0) {
    throw "Installer definition verification failed with exit code $LASTEXITCODE."
}

$iscc = & (Join-Path $PSScriptRoot "resolve-iscc.ps1") -IsccPath $IsccPath

foreach ($path in @($installerPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$compilerArguments = @(
    "/DAppVersion=$Version",
    "/DPublishDir=$publishDirectory",
    "/DInstallerOutputDir=$artifactsRoot",
    "/DInstallerBaseName=$installerBaseName",
    $definitionPath
)
& $iscc @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Inno Setup completed without producing the expected installer: $installerPath"
}

$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).
    Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $checksumPath,
    "$installerHash  $([IO.Path]::GetFileName($installerPath))`n",
    [Text.UTF8Encoding]::new($false))

Write-Output "INSTALLER=$installerPath"
Write-Output "INSTALLER_SHA256_FILE=$checksumPath"
Write-Output "INSTALLER_SHA256=$installerHash"
Write-Output "INSTALLER_SIZE_BYTES=$((Get-Item -LiteralPath $installerPath).Length)"
Write-Output "INSTALL_SCOPE=per-user-lowest"
Write-Output "RELEASE_CHANNEL=preview"
Write-Output "CODE_SIGNING=unsigned"
