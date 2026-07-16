$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolver = Join-Path $PSScriptRoot "resolve-dotnet.ps1"
$originalHostPath = $env:DOTNET_HOST_PATH
$originalRoot = $env:DOTNET_ROOT
$originalPath = $env:PATH
$originalUserProfile = $env:USERPROFILE
$originalMultilevelLookup = $env:DOTNET_MULTILEVEL_LOOKUP
$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "HistoryClipboard-dotnet-resolution-" + [Guid]::NewGuid().ToString("N"))
$userDotnetJunction = $null

try {
    $activeDotnet = & $resolver
    $activeDotnet = [IO.Path]::GetFullPath($activeDotnet)
    $activeDotnetRoot = Split-Path -Parent $activeDotnet

    $env:DOTNET_HOST_PATH = $null
    $env:DOTNET_ROOT = $null
    $env:USERPROFILE = Join-Path $verificationRoot "no-user-sdk"
    $env:PATH = $activeDotnetRoot

    $pathResult = & $resolver
    if ([IO.Path]::GetFullPath($pathResult) -ne $activeDotnet) {
        throw "Resolver did not accept the PATH dotnet that satisfies global.json."
    }
    Write-Output "PATH_REQUIRED_SDK=passed"

    $hostOnlyRoot = Join-Path $verificationRoot "host-only"
    $hostOnlyHostDirectory = Join-Path $hostOnlyRoot "host"
    New-Item -ItemType Directory -Path $hostOnlyHostDirectory -Force | Out-Null
    Copy-Item -LiteralPath $activeDotnet `
        -Destination (Join-Path $hostOnlyRoot "dotnet.exe")
    Copy-Item -Path (Join-Path $activeDotnetRoot "host\*") `
        -Destination $hostOnlyHostDirectory `
        -Recurse `
        -Force

    $hostOnlyDotnet = Join-Path $hostOnlyRoot "dotnet.exe"
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    Push-Location $projectRoot
    try {
        $hostOnlyVersion = @(& $hostOnlyDotnet --version 2>$null)
        $hostOnlyExitCode = $LASTEXITCODE
    }
    catch {
        $hostOnlyVersion = @()
        $hostOnlyExitCode = 1
    }
    finally {
        Pop-Location
    }
    if ($hostOnlyExitCode -eq 0 -and $hostOnlyVersion.Count -ne 0) {
        throw "The host-only fixture unexpectedly satisfies global.json."
    }

    $fallbackProfile = Join-Path $verificationRoot "fallback-profile"
    New-Item -ItemType Directory -Path $fallbackProfile -Force | Out-Null
    $userDotnetJunction = Join-Path $fallbackProfile ".dotnet"
    New-Item -ItemType Junction `
        -Path $userDotnetJunction `
        -Target $activeDotnetRoot | Out-Null

    $env:DOTNET_HOST_PATH = $null
    $env:DOTNET_ROOT = $null
    $env:DOTNET_MULTILEVEL_LOOKUP = "0"
    $env:PATH = $hostOnlyRoot
    $env:USERPROFILE = $fallbackProfile

    $fallbackResult = & $resolver
    $expectedFallback = Join-Path $userDotnetJunction "dotnet.exe"
    if ([IO.Path]::GetFullPath($fallbackResult) -ne
        [IO.Path]::GetFullPath($expectedFallback)) {
        throw "Resolver did not skip the host-only PATH entry and use the fallback SDK."
    }
    Write-Output "HOST_ONLY_PATH_FALLBACK=passed"

    $resolverText = Get-Content -LiteralPath $resolver -Raw
    if ($resolverText -match '(?i)C:\\Users\\[^\\]+') {
        throw "Resolver contains a user-specific absolute path."
    }
    Write-Output "PERSONAL_PATH_DEPENDENCY=none"
}
finally {
    $env:DOTNET_HOST_PATH = $originalHostPath
    $env:DOTNET_ROOT = $originalRoot
    $env:PATH = $originalPath
    $env:USERPROFILE = $originalUserProfile
    $env:DOTNET_MULTILEVEL_LOOKUP = $originalMultilevelLookup

    if ($null -ne $userDotnetJunction -and
        (Test-Path -LiteralPath $userDotnetJunction)) {
        [IO.Directory]::Delete($userDotnetJunction)
    }
    if (Test-Path -LiteralPath $verificationRoot) {
        Remove-Item -LiteralPath $verificationRoot -Recurse -Force
    }
}
