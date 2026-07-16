param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$toolsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "tools"))
$lockPath = Join-Path $toolsRoot "inno-setup.lock.json"
if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
    throw "The pinned Inno Setup lock file is missing: $lockPath"
}

$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$toolDirectory = [IO.Path]::GetFullPath(
    (Join-Path $toolsRoot "inno-setup\$($lock.version)-$($lock.architecture)"))
$cacheDirectory = [IO.Path]::GetFullPath((Join-Path $toolsRoot ".cache\inno-setup"))
$installerPath = Join-Path $cacheDirectory ([string]$lock.installerFileName)
$signaturePath = "$installerPath.issig"
$isccPath = Join-Path $toolDirectory "ISCC.exe"
$toolsPrefix = $toolsRoot.TrimEnd('\') + '\'

foreach ($path in @($toolDirectory, $cacheDirectory, $installerPath, $signaturePath)) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($toolsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a private-tool path outside the project tools directory: $resolved"
    }
}

function Get-UninstallKeyInventory {
    $locations = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    )
    $entries = foreach ($location in $locations) {
        if (-not (Test-Path -LiteralPath $location)) {
            continue
        }

        Get-ChildItem -LiteralPath $location -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like "Inno Setup *_is1" } |
            ForEach-Object { "$location\$($_.PSChildName)" }
    }

    return @($entries | Sort-Object)
}

function Test-StringSequenceEqual {
    param([string[]]$Expected, [string[]]$Actual)

    if ($Expected.Count -ne $Actual.Count) {
        return $false
    }

    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [string]::Equals(
            $Expected[$index], $Actual[$index], [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    return $true
}

function Invoke-VerifiedDownload {
    param(
        [string]$Uri,
        [string]$Destination,
        [string]$ExpectedSha256
    )

    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        $existingHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).
            Hash.ToLowerInvariant()
        if ($existingHash -eq $ExpectedSha256.ToLowerInvariant()) {
            return
        }

        Remove-Item -LiteralPath $Destination -Force
    }

    $partialPath = "$Destination.$([Guid]::NewGuid().ToString('N')).part"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $partialPath
        $actualHash = (Get-FileHash -LiteralPath $partialPath -Algorithm SHA256).
            Hash.ToLowerInvariant()
        if ($actualHash -ne $ExpectedSha256.ToLowerInvariant()) {
            throw "Downloaded file SHA-256 mismatch for $Uri. Expected $ExpectedSha256, got $actualHash."
        }

        Move-Item -LiteralPath $partialPath -Destination $Destination
    }
    finally {
        if (Test-Path -LiteralPath $partialPath) {
            Remove-Item -LiteralPath $partialPath -Force
        }
    }
}

if ((Test-Path -LiteralPath $isccPath -PathType Leaf) -and -not $Force) {
    $existingInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($isccPath)
    $existingSignature = Get-AuthenticodeSignature -LiteralPath $isccPath
    if ($existingInfo.ProductName -ne "Inno Setup" -or
        $existingInfo.FileDescription -ne "Inno Setup Command-Line Compiler" -or
        $existingSignature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $null -eq $existingSignature.SignerCertificate -or
        $existingSignature.SignerCertificate.Subject -notlike
            "*$($lock.authenticodePublisher)*") {
        throw "The existing private compiler failed identity verification. Use -Force to rebuild it."
    }

    Write-Output "INNO_SETUP_VERSION=$($lock.version)"
    Write-Output "INNO_SETUP_SCOPE=project-private-portable"
    Write-Output "ISCC=$isccPath"
    Write-Output "INSTALLER_SHA256=$($lock.installerSha256)"
    Write-Output "BOOTSTRAP=already-valid"
    exit 0
}

New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
Invoke-VerifiedDownload `
    -Uri ([string]$lock.signatureFileUrl) `
    -Destination $signaturePath `
    -ExpectedSha256 ([string]$lock.signatureFileSha256)
Invoke-VerifiedDownload `
    -Uri ([string]$lock.installerUrl) `
    -Destination $installerPath `
    -ExpectedSha256 ([string]$lock.installerSha256)

$signatureText = Get-Content -LiteralPath $signaturePath -Raw
$escapedFileName = [regex]::Escape([string]$lock.installerFileName)
if ($signatureText -notmatch "(?m)^file-name `"$escapedFileName`"\s*$" -or
    $signatureText -notmatch "(?m)^file-size $($lock.installerSize)\s*$" -or
    $signatureText -notmatch "(?m)^file-hash $($lock.installerSha256)\s*$") {
    throw "The pinned official .issig descriptor does not match the installer lock."
}

$installerInfo = Get-Item -LiteralPath $installerPath
if ($installerInfo.Length -ne [long]$lock.installerSize) {
    throw "The installer size does not match the pinned official descriptor."
}

$authenticode = Get-AuthenticodeSignature -LiteralPath $installerPath
if ($authenticode.Status -ne [Management.Automation.SignatureStatus]::Valid) {
    throw "The Inno Setup installer Authenticode signature is not valid: $($authenticode.StatusMessage)"
}

if ($null -eq $authenticode.SignerCertificate -or
    $authenticode.SignerCertificate.Subject -notlike "*$($lock.authenticodePublisher)*") {
    throw "The Inno Setup installer is not signed by the pinned publisher $($lock.authenticodePublisher)."
}

$processPathBefore = $env:PATH
$userPathBefore = [Environment]::GetEnvironmentVariable("PATH", "User")
$machinePathBefore = [Environment]::GetEnvironmentVariable("PATH", "Machine")
$uninstallKeysBefore = @(Get-UninstallKeyInventory)

if (Test-Path -LiteralPath $toolDirectory) {
    if (-not $Force) {
        throw "The private tool directory exists but ISCC.exe is missing: $toolDirectory"
    }

    Remove-Item -LiteralPath $toolDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
$portableArguments = @(
    "/PORTABLE=1",
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/CURRENTUSER",
    "/NOICONS",
    ('/DIR="{0}"' -f $toolDirectory)
)
$portableProcess = Start-Process `
    -FilePath $installerPath `
    -ArgumentList $portableArguments `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($portableProcess.ExitCode -ne 0) {
    throw (
        "The official Inno Setup portable extraction failed with exit code " +
        "$($portableProcess.ExitCode).")
}

if (-not (Test-Path -LiteralPath $isccPath -PathType Leaf)) {
    throw "Portable extraction did not produce the expected compiler: $isccPath"
}

$isccInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($isccPath)
$isccSignature = Get-AuthenticodeSignature -LiteralPath $isccPath
if ($isccInfo.ProductName -ne "Inno Setup" -or
    $isccInfo.FileDescription -ne "Inno Setup Command-Line Compiler" -or
    $isccSignature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
    $null -eq $isccSignature.SignerCertificate -or
    $isccSignature.SignerCertificate.Subject -notlike
        "*$($lock.authenticodePublisher)*") {
    throw "The extracted compiler failed identity or publisher verification."
}

$privateUninstallers = @(Get-ChildItem -LiteralPath $toolDirectory -Filter "unins*.exe" -File)
if ($privateUninstallers.Count -ne 0) {
    throw "Portable extraction unexpectedly created an uninstaller."
}

$uninstallKeysAfter = @(Get-UninstallKeyInventory)
if (-not (Test-StringSequenceEqual -Expected $uninstallKeysBefore -Actual $uninstallKeysAfter)) {
    throw "Portable extraction changed the machine/user Inno Setup uninstall-key inventory."
}

if ($processPathBefore -ne $env:PATH -or
    $userPathBefore -ne [Environment]::GetEnvironmentVariable("PATH", "User") -or
    $machinePathBefore -ne [Environment]::GetEnvironmentVariable("PATH", "Machine")) {
    throw "Portable extraction changed a PATH value."
}

Write-Output "INNO_SETUP_VERSION=$($lock.version)"
Write-Output "INNO_SETUP_SCOPE=project-private-portable"
Write-Output "INNO_SETUP_PUBLISHER=$($lock.authenticodePublisher)"
Write-Output "AUTHENTICODE=valid"
Write-Output "OFFICIAL_SIGNATURE_DESCRIPTOR=matched"
Write-Output "INSTALLER_SHA256=$($lock.installerSha256)"
Write-Output "ISCC=$isccPath"
Write-Output "PATH_CHANGED=no"
Write-Output "UNINSTALL_REGISTRATION_CHANGED=no"
Write-Output "BOOTSTRAP=complete"
