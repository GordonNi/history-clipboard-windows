$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolver = Join-Path $PSScriptRoot "resolve-dotnet.ps1"
$originalHostPath = $env:DOTNET_HOST_PATH
$originalRoot = $env:DOTNET_ROOT
$originalPath = $env:PATH
$originalUserProfile = $env:USERPROFILE

try {
    $env:DOTNET_HOST_PATH = $null
    $env:DOTNET_ROOT = $null
    $env:PATH = [Environment]::GetEnvironmentVariable("WINDIR")

    $userDotnet = Join-Path $originalUserProfile ".dotnet\dotnet.exe"
    if (-not (Test-Path -LiteralPath $userDotnet -PathType Leaf)) {
        throw "The user-profile fallback cannot be verified because its dotnet.exe is absent."
    }

    $fallbackResult = & $resolver
    if ([IO.Path]::GetFullPath($fallbackResult) -ne [IO.Path]::GetFullPath($userDotnet)) {
        throw "Resolver did not use the expected user-profile fallback."
    }
    Write-Output "USER_PROFILE_FALLBACK=passed"

    $dotnetDirectory = Split-Path -Parent $fallbackResult
    $env:USERPROFILE = Join-Path ([IO.Path]::GetTempPath()) (
        "HistoryClipboard-no-user-sdk-" + [Guid]::NewGuid().ToString("N"))
    $env:PATH = $dotnetDirectory

    $pathResult = & $resolver
    if ([IO.Path]::GetFullPath($pathResult) -ne [IO.Path]::GetFullPath($fallbackResult)) {
        throw "Resolver did not discover dotnet.exe from a CI-style PATH."
    }
    Write-Output "PATH_DISCOVERY=passed"

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
}
