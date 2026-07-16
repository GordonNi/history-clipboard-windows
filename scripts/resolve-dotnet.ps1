$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$candidates = New-Object System.Collections.Generic.List[string]

if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) {
    $candidates.Add($env:DOTNET_HOST_PATH)
}

if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
    $candidates.Add((Join-Path $env:DOTNET_ROOT "dotnet.exe"))
}

$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $dotnetCommand) {
    $commandPath = $dotnetCommand.Path
    if ([string]::IsNullOrWhiteSpace($commandPath)) {
        $commandPath = $dotnetCommand.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($commandPath)) {
        $candidates.Add($commandPath)
    }
}

$candidates.Add((Join-Path $projectRoot ".dotnet\dotnet.exe"))
if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
    $candidates.Add((Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"))
}

foreach ($candidate in $candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        continue
    }

    try {
        $fullPath = [IO.Path]::GetFullPath($candidate)
    }
    catch {
        continue
    }

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    $sdkList = @(& $fullPath --list-sdks 2>$null)
    if ($LASTEXITCODE -eq 0 -and $sdkList.Count -ne 0) {
        Write-Output $fullPath
        exit 0
    }
}

throw (
    "A dotnet.exe with an installed SDK was not found. Set DOTNET_HOST_PATH or DOTNET_ROOT, " +
    "add dotnet to PATH, or install the SDK in .dotnet under the project or user profile.")
