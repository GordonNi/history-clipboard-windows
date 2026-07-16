param(
    [string]$Version = "0.1.0-preview.2",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+-preview\.\d+$') {
    throw "Portable releases must use an explicit preview version such as 0.1.0-preview.2."
}

if ($RuntimeIdentifier -ne "win-x64") {
    throw "The first portable release supports only win-x64."
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $projectRoot "src\HistoryClipboard.App\HistoryClipboard.App.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) {
    throw "Requested version $Version does not match the project version $projectVersion."
}

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$packageName = "HistoryClipboard-$Version-unsigned-$RuntimeIdentifier"
$publishRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "portable"))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $publishRoot $packageName))
$zipPath = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "$packageName-portable.zip"))
$checksumPath = "$zipPath.sha256"
$artifactsPrefix = $artifactsRoot.TrimEnd('\') + '\'

foreach ($path in @($publishDirectory, $zipPath, $checksumPath)) {
    if (-not $path.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a release path outside the artifacts directory: $path"
    }
}

$dotnet = & (Join-Path $PSScriptRoot "resolve-dotnet.ps1")
$dotnetRoot = Split-Path -Parent $dotnet
$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
foreach ($path in @($publishDirectory, $zipPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

& $dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:InformationalVersion="$Version+unsigned" `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$documentation = @{
    (Join-Path $projectRoot "docs\portable-usage.md") = "PORTABLE-README.md"
    (Join-Path $projectRoot "docs\privacy.md") = "PRIVACY.md"
    (Join-Path $projectRoot "docs\release-checklist.md") = "RELEASE-CHECKLIST.md"
    (Join-Path $projectRoot "docs\installer-usage.md") = "INSTALLER-USAGE.md"
    (Join-Path $projectRoot "docs\inno-setup-toolchain.md") = "INNO-SETUP-TOOLCHAIN.md"
    (Join-Path $projectRoot "docs\technical-specification.md") = "TECHNICAL-SPECIFICATION.md"
}
foreach ($sourcePath in $documentation.Keys) {
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required release documentation is missing: $sourcePath"
    }

    Copy-Item -LiteralPath $sourcePath `
        -Destination (Join-Path $publishDirectory $documentation[$sourcePath])
}

$requiredFiles = @(
    "HistoryClipboard.App.exe",
    "HistoryClipboard.App.dll",
    "HistoryClipboard.App.deps.json",
    "HistoryClipboard.App.runtimeconfig.json",
    "coreclr.dll",
    "hostfxr.dll",
    "hostpolicy.dll",
    "PORTABLE-README.md",
    "PRIVACY.md",
    "RELEASE-CHECKLIST.md",
    "INSTALLER-USAGE.md",
    "INNO-SETUP-TOOLCHAIN.md",
    "TECHNICAL-SPECIFICATION.md"
)
foreach ($relativePath in $requiredFiles) {
    $candidate = Join-Path $publishDirectory $relativePath
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Portable publish is missing a required file: $relativePath"
    }
}

$forbiddenFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force -File |
    Where-Object {
        $_.Extension -in @(".pdb", ".user", ".suo", ".tmp", ".temp", ".db", ".log") -or
        $_.Name -in @("settings.json", "history.db") -or
        $_.Name -like "history.db-*"
    })
$forbiddenDirectories = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force -Directory |
    Where-Object { $_.Name -in @("Images", "TestResults", ".git", "obj", "bin") })
if ($forbiddenFiles.Count -ne 0 -or $forbiddenDirectories.Count -ne 0) {
    $paths = @($forbiddenFiles.FullName) + @($forbiddenDirectories.FullName)
    throw "Portable publish contains forbidden user, debug, or temporary content: $($paths -join ', ')"
}

Add-Type -AssemblyName System.IO.Compression
$zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $zipStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $fixedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $files = Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force -File |
            Sort-Object FullName
        $publishPrefix = $publishDirectory.TrimEnd('\') + '\'
        foreach ($file in $files) {
            if (-not $file.FullName.StartsWith(
                $publishPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "Publish file resolved outside the package directory: $($file.FullName)"
            }

            $relativePath = $file.FullName.Substring($publishPrefix.Length)
            $entryName = "$packageName/$($relativePath.Replace('\', '/'))"
            $entry = $archive.CreateEntry(
                $entryName,
                [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $entryStream = $entry.Open()
            try {
                $sourceStream = [IO.File]::OpenRead($file.FullName)
                try {
                    $sourceStream.CopyTo($entryStream)
                }
                finally {
                    $sourceStream.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $zipStream.Dispose()
}

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $checksumPath,
    "$zipHash  $([IO.Path]::GetFileName($zipPath))`n",
    [Text.UTF8Encoding]::new($false))

Write-Output "PORTABLE_DIRECTORY=$publishDirectory"
Write-Output "PORTABLE_ZIP=$zipPath"
Write-Output "PORTABLE_SHA256_FILE=$checksumPath"
Write-Output "PORTABLE_SHA256=$zipHash"
Write-Output "PORTABLE_SIZE_BYTES=$((Get-Item -LiteralPath $zipPath).Length)"
Write-Output "RELEASE_CHANNEL=preview"
Write-Output "CODE_SIGNING=unsigned"
