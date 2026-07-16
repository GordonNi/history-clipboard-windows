param(
    [string]$Version = "0.1.0-preview.2",
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
$definitionPath = Join-Path $projectRoot "installer\HistoryClipboard.iss"
$packageName = "HistoryClipboard-$Version-unsigned-$RuntimeIdentifier"
$publishDirectory = Join-Path $projectRoot "artifacts\portable\$packageName"

foreach ($path in @($definitionPath, $publishDirectory)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required installer input is missing: $path"
    }
}

$definition = Get-Content -LiteralPath $definitionPath -Raw
$requiredPatterns = [ordered]@{
    "lowest-privilege mode" = '(?m)^PrivilegesRequired=lowest\s*$'
    "per-user install directory" =
        '(?m)^DefaultDirName=\{localappdata\}\\Programs\\HistoryClipboard\s*$'
    "x64-compatible package" = '(?m)^ArchitecturesAllowed=x64compatible\s*$'
    "64-bit install mode" =
        '(?m)^ArchitecturesInstallIn64BitMode=x64compatible\s*$'
    "unsigned preview name" =
        '(?m)^AppVerName=.*\{#AppVersion\}.*Preview.*Unsigned.*$'
    "running application mutex" =
        '(?m)^AppMutex=Local\\HistoryClipboard\.SingleInstance\s*$'
    "non-forced application handling" = '(?m)^CloseApplications=yes\s*$'
    "no automatic application restart" = '(?m)^RestartApplications=no\s*$'
    "Start menu application entry" =
        '(?m)^Name: "\{group\}\\HistoryClipboard";.*$'
    "Start menu uninstall entry" =
        '(?m)^Name: "\{group\}\\Uninstall HistoryClipboard";.*$'
    "silent-uninstall detection" =
        "SameText\(ParamStr\(Index\), '/VERYSILENT'\)"
    "data deletion defaults off" =
        '(?m)^\s*DeleteUserDataAfterUninstall := False;\s*$'
    "default-no data prompt" =
        '(?s)SuppressibleMsgBox\(.*?Choose No to keep this data.*?IDNO\) = IDYES;'
    "owned Run comparison" =
        '(?s)RegQueryStringValue\(.*?HKEY_CURRENT_USER.*?SameText\(Trim\(CurrentCommand\), InstalledRunCommand\)'
    "owned Run deletion" =
        '(?s)SameText\(Trim\(CurrentCommand\), InstalledRunCommand\).*?RegDeleteValue\('
    "post-uninstall data deletion" =
        '(?s)CurUninstallStep = usPostUninstall.*?DeleteUserDataAfterUninstall.*?DelTree\(UserDataDirectory, True, True, True\)'
}
foreach ($requirement in $requiredPatterns.GetEnumerator()) {
    if ($definition -notmatch $requirement.Value) {
        throw "Installer definition is missing $($requirement.Key)."
    }
}

$forbiddenDefinitionPatterns = [ordered]@{
    "administrative privilege request" = '(?m)^PrivilegesRequired=admin\s*$'
    "privilege override" = '(?m)^PrivilegesRequiredOverridesAllowed=\S+'
    "machine registry mutation" = '(?i)HKEY_LOCAL_MACHINE|\bHKLM\b'
    "unconditional user-data deletion section" = '(?m)^\[UninstallDelete\]\s*$'
    "code-signing command" = '(?m)^SignTool='
    "signed release claim" = '(?m)^SignedUninstaller=yes\s*$'
}
foreach ($forbidden in $forbiddenDefinitionPatterns.GetEnumerator()) {
    if ($definition -match $forbidden.Value) {
        throw "Installer definition contains a forbidden $($forbidden.Key)."
    }
}

$requiredFiles = @(
    "HistoryClipboard.App.exe",
    "HistoryClipboard.App.dll",
    "HistoryClipboard.App.deps.json",
    "HistoryClipboard.App.runtimeconfig.json",
    "coreclr.dll",
    "hostfxr.dll",
    "hostpolicy.dll",
    "PRIVACY.md",
    "RELEASE-CHECKLIST.md"
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $relativePath) -PathType Leaf)) {
        throw "Installer publish input is missing a required file: $relativePath"
    }
}

$forbiddenFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force -File |
    Where-Object {
        $_.Extension -in @(".pdb", ".user", ".suo", ".tmp", ".temp", ".db", ".log") -or
        $_.Name -in @("settings.json", "history.db") -or
        $_.Name -like "history.db-*"
    })
$forbiddenDirectories = @(
    Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force -Directory |
        Where-Object { $_.Name -in @("Images", "TestResults", ".git", "obj", "bin") }
)
if ($forbiddenFiles.Count -ne 0 -or $forbiddenDirectories.Count -ne 0) {
    $paths = @($forbiddenFiles.FullName) + @($forbiddenDirectories.FullName)
    throw "Installer publish input contains forbidden content: $($paths -join ', ')"
}

$projectPath = Join-Path $projectRoot "src\HistoryClipboard.App\HistoryClipboard.App.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) {
    throw "Installer version $Version does not match project version $projectVersion."
}

$productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $publishDirectory "HistoryClipboard.App.exe")).ProductVersion
if ($productVersion -ne "$Version+unsigned") {
    throw "Publish input has unexpected product version: $productVersion"
}

Write-Output "INSTALLER_DEFINITION=valid"
Write-Output "INSTALL_SCOPE=per-user-lowest"
Write-Output "INSTALL_ARCHITECTURE=x64"
Write-Output "PUBLISH_INPUT=self-contained-valid"
Write-Output "USER_DATA_INCLUDED=none"
Write-Output "SILENT_UNINSTALL_DATA_POLICY=preserve"
Write-Output "RUN_VALUE_DELETE_POLICY=owned-command-only"
Write-Output "CODE_SIGNING=unsigned"

try {
    $resolvedIscc = & (Join-Path $PSScriptRoot "resolve-iscc.ps1") -IsccPath $IsccPath
    Write-Output "ISCC=$resolvedIscc"
    Write-Output "INSTALLER_COMPILE=available-not-run"
}
catch {
    Write-Output "ISCC=missing"
    Write-Output "INSTALLER_COMPILE=blocked"
    Write-Output "INSTALLER_COMPILE_REASON=$($_.Exception.Message)"
}
