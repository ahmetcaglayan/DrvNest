<#
.SYNOPSIS
    Publishes Hexnest as a self-contained, single-file executable.

.DESCRIPTION
    The local equivalent of what .github/workflows/release.yml does on CI, for one
    runtime at a time. The output file name matches what the in-app updater looks
    for on GitHub Releases: Hexnest.exe for x64, Hexnest-arm64.exe for arm64.

.PARAMETER Runtime
    win-x64 (default) or win-arm64.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER Version
    Semantic version baked into the executable, e.g. 1.2.3. Defaults to the value
    in Directory.Build.props.

.PARAMETER Output
    Folder the finished executable is copied into. Defaults to dist\ in the repo root.

.PARAMETER Clean
    Empties the whole output folder first. Without it only this runtime's own files
    are replaced, so publishing x64 and then arm64 into the same folder works.

.EXAMPLE
    pwsh build\publish.ps1
    pwsh build\publish.ps1 -Runtime win-arm64 -Version 1.2.3
#>

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [ValidateNotNullOrEmpty()]
    [string] $Output = 'dist',

    [switch] $Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# =============================================================================
#  Paths
# =============================================================================

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Hexnest.App\Hexnest.App.csproj'

if (-not (Test-Path $project)) {
    throw "Could not find $project. Run this script from inside the Hexnest repository."
}

$outputDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }

# Each runtime publishes into its own staging folder; only the finished file is
# copied into $outputDir. Otherwise the two runtimes would overwrite each other.
$stagingDir = Join-Path $repoRoot "publish\$Runtime"

$targetName = if ($Runtime -eq 'win-arm64') { 'Hexnest-arm64.exe' } else { 'Hexnest.exe' }
$targetPath = Join-Path $outputDir $targetName

# =============================================================================
#  Toolchain checks
# =============================================================================

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw @'
dotnet was not found on PATH.

Hexnest needs the .NET 8 SDK (or newer). Install it from:
    https://dotnet.microsoft.com/download/dotnet/8.0
and reopen your terminal so PATH picks it up.
'@
}

$sdks = @(& dotnet --list-sdks)
$majors = foreach ($line in $sdks) {
    if ($line -match '^(\d+)\.') { [int]$Matches[1] }
}

if (-not $majors -or (($majors | Measure-Object -Maximum).Maximum -lt 8)) {
    $installed = if ($sdks) { ($sdks -join "`n    ") } else { '(none)' }
    throw @"
No .NET SDK 8.0 or newer was found.

Installed SDKs:
    $installed

Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
"@
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Hexnest can only be built on Windows: the app is WPF and Hexnest.Core calls SetupAPI, CfgMgr32 and the Windows Update Agent.'
}

# =============================================================================
#  Clean
# =============================================================================

if ($Clean -and (Test-Path $outputDir)) {
    Write-Host "Cleaning $outputDir" -ForegroundColor DarkGray
    Remove-Item -Path $outputDir -Recurse -Force
}

if (Test-Path $stagingDir) {
    Remove-Item -Path $stagingDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
if (Test-Path $targetPath) { Remove-Item -Path $targetPath -Force }

# =============================================================================
#  Publish
# =============================================================================

Write-Host ''
Write-Host "Publishing Hexnest  ($Runtime, $Configuration)" -ForegroundColor Cyan
if ($Version) { Write-Host "Version             $Version" -ForegroundColor Cyan }
Write-Host ''

$arguments = @(
    'publish', $project
    '--configuration', $Configuration
    '--runtime', $Runtime
    '--self-contained', 'true'
    '-p:PublishSingleFile=true'
    '-p:EnableCompressionInSingleFile=true'
    '-p:PublishReadyToRun=true'
    '-p:PublishTrimmed=false'
    '--output', $stagingDir
)

if ($Version) { $arguments += "-p:Version=$Version" }

& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# =============================================================================
#  Collect
# =============================================================================

$published = Join-Path $stagingDir 'Hexnest.exe'
if (-not (Test-Path $published)) {
    throw "Publish succeeded but $published does not exist. Check the AssemblyName in Hexnest.App.csproj."
}

Copy-Item -Path $published -Destination $targetPath -Force

$item = Get-Item $targetPath
$hash = (Get-FileHash -Path $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host ''
Write-Host 'Published' -ForegroundColor Green
Write-Host ("  File     {0}" -f $item.FullName)
Write-Host ("  Size     {0:N1} MB ({1:N0} bytes)" -f ($item.Length / 1MB), $item.Length)
Write-Host ("  SHA-256  {0}" -f $hash)
Write-Host ''

if ($item.Length -lt 10MB) {
    Write-Warning 'The executable is suspiciously small; the .NET runtime may not be bundled.'
}
