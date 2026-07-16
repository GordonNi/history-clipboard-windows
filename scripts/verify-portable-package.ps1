param(
    [string]$Version = "0.1.0-preview.2",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SkipStartupProbe
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$packageName = "HistoryClipboard-$Version-unsigned-$RuntimeIdentifier"
$zipPath = Join-Path $artifactsRoot "$packageName-portable.zip"
$checksumPath = "$zipPath.sha256"

foreach ($path in @($zipPath, $checksumPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required portable artifact is missing: $path"
    }
}

$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
if ($checksumLine -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
    throw "Checksum file does not use the expected SHA-256 format."
}

$expectedHash = $Matches[1].ToLowerInvariant()
$expectedFileName = $Matches[2]
if ($expectedFileName -ne [IO.Path]::GetFileName($zipPath)) {
    throw "Checksum file names a different artifact: $expectedFileName"
}

$actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "Portable ZIP SHA-256 mismatch."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $entryNames = @($entries.FullName)
    $requiredEntries = @(
        "$packageName/HistoryClipboard.App.exe",
        "$packageName/HistoryClipboard.App.dll",
        "$packageName/HistoryClipboard.App.deps.json",
        "$packageName/HistoryClipboard.App.runtimeconfig.json",
        "$packageName/coreclr.dll",
        "$packageName/hostfxr.dll",
        "$packageName/hostpolicy.dll",
        "$packageName/PORTABLE-README.md",
        "$packageName/PRIVACY.md",
        "$packageName/RELEASE-CHECKLIST.md",
        "$packageName/INSTALLER-USAGE.md",
        "$packageName/INNO-SETUP-TOOLCHAIN.md",
        "$packageName/TECHNICAL-SPECIFICATION.md"
    )
    foreach ($requiredEntry in $requiredEntries) {
        if ($requiredEntry -notin $entryNames) {
            throw "Portable ZIP is missing a required entry: $requiredEntry"
        }
    }

    $forbiddenEntries = @($entryNames | Where-Object {
        $_ -match '(?i)(^|/)(history\.db(?:-.+)?|settings\.json)$' -or
        $_ -match '(?i)(^|/)(Images|TestResults|\.git|bin|obj)/' -or
        $_ -match '(?i)\.(pdb|user|suo|tmp|temp|db|log)$'
    })
    if ($forbiddenEntries.Count -ne 0) {
        throw "Portable ZIP contains forbidden content: $($forbiddenEntries -join ', ')"
    }

    $boundaryDocuments = @(
        "$packageName/INSTALLER-USAGE.md",
        "$packageName/INNO-SETUP-TOOLCHAIN.md",
        "$packageName/RELEASE-CHECKLIST.md",
        "$packageName/TECHNICAL-SPECIFICATION.md"
    )
    foreach ($documentName in $boundaryDocuments) {
        $documentEntry = $entries | Where-Object { $_.FullName -eq $documentName }
        $reader = [IO.StreamReader]::new(
            $documentEntry.Open(),
            [Text.Encoding]::UTF8)
        try {
            $documentText = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $mentionsInstalledProductRefusal =
            $documentText -match '(?is)(refus|must run only|may run only).{0,500}(installed|partial|damaged)' -or
            $documentText -match '(?is)(installed|partial|damaged).{0,500}(refus|must run only|may run only)'
        $mentionsDataOnlyAllowance =
            $documentText -match '(?is)(user data|%LOCALAPPDATA%\\HistoryClipboard).{0,500}(allow|permitted|may exist)' -or
            $documentText -match '(?is)(allow|permitted|may exist).{0,500}(user data|%LOCALAPPDATA%\\HistoryClipboard)'
        if (-not $mentionsInstalledProductRefusal -or -not $mentionsDataOnlyAllowance) {
            throw (
                "Portable release document does not state the installer-probe " +
                "installed-product refusal and data-only allowance: $documentName")
        }
    }
}
finally {
    $archive.Dispose()
}

$existingProcessIds = @(
    Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Id } |
        Sort-Object)
if (-not $SkipStartupProbe -and $existingProcessIds.Count -ne 0) {
    throw "Close the running HistoryClipboard.App process before using this probe."
}

$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "HistoryClipboard-package-verification-" + [Guid]::NewGuid().ToString("N"))
$process = $null
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $verificationRoot)
    $executable = Join-Path $verificationRoot "$packageName\HistoryClipboard.App.exe"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Extracted portable executable was not found."
    }

    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
    if ($productVersion -ne "$Version+unsigned") {
        throw "Portable executable has unexpected product version: $productVersion"
    }

    $executableText = [Text.Encoding]::UTF8.GetString(
        [IO.File]::ReadAllBytes($executable))
    if (-not $executableText.Contains("PerMonitorV2")) {
        throw "Portable executable is missing the PerMonitorV2 manifest declaration."
    }

    $applicationAssembly = Join-Path (Split-Path -Parent $executable) "HistoryClipboard.App.dll"
    $applicationAssemblyBytes = [IO.File]::ReadAllBytes($applicationAssembly)
    $applicationAssemblyUtf8 = [Text.Encoding]::UTF8.GetString($applicationAssemblyBytes)
    $applicationAssemblyUnicode = [Text.Encoding]::Unicode.GetString($applicationAssemblyBytes)
    foreach ($startupGuardMarker in @(
            "StartupFailureCoordinator",
            "HC-START-001",
            "HC-START-002",
            "HC-START-999")) {
        if (-not $applicationAssemblyUtf8.Contains($startupGuardMarker) -and
            -not $applicationAssemblyUnicode.Contains($startupGuardMarker)) {
            throw "Portable application is missing startup-failure guard marker: $startupGuardMarker"
        }
    }

    if (-not $SkipStartupProbe) {
        $process = Start-Process -FilePath $executable -WindowStyle Normal -PassThru
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
            if ($process.HasExited) {
                throw (
                    "Portable application exited during startup with code " +
                    "$($process.ExitCode).")
            }

            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                break
            }
        }

        $process.Refresh()
        if ($process.HasExited) {
            throw "Portable application did not remain running."
        }
    }

    Write-Output "ZIP_STRUCTURE=valid"
    Write-Output "ZIP_SHA256=valid"
    Write-Output "SELF_CONTAINED_RUNTIME=present"
    Write-Output "USER_DATA_INCLUDED=none"
    Write-Output "RELEASE_IDENTITY=$productVersion"
    Write-Output "DPI_MANIFEST=PerMonitorV2"
    Write-Output "STARTUP_FAILURE_GUARD=present"
    if ($SkipStartupProbe) {
        Write-Output "PORTABLE_STARTUP=skipped-by-request"
    }
    else {
        Write-Output "PORTABLE_STARTUP=process-running"
    }
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
            $process.WaitForExit()
        }
    }

    if (Test-Path -LiteralPath $verificationRoot) {
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        $resolvedVerificationRoot = [IO.Path]::GetFullPath($verificationRoot)
        if (-not $resolvedVerificationRoot.StartsWith(
            $tempRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a verification path outside the temp directory."
        }

        Remove-Item -LiteralPath $resolvedVerificationRoot -Recurse -Force
    }
}

$remainingProcessIds = @(
    Get-Process -Name "HistoryClipboard.App" -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Id } |
        Sort-Object)
if ($SkipStartupProbe) {
    if (($remainingProcessIds -join ",") -ne ($existingProcessIds -join ",")) {
        throw "Headless portable verification changed the application process set."
    }

    Write-Output "APP_PROCESS_STATE=unchanged"
}
else {
    if ($remainingProcessIds.Count -ne 0) {
        throw "Portable verification left an application process running."
    }

    Write-Output "APP_PROCESSES_RUNNING=none"
}
