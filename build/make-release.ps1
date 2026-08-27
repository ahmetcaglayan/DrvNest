<#
.SYNOPSIS
    Builds a complete DrvNest release locally: both architectures, the optional
    installer, and checksums.txt.

.DESCRIPTION
    Produces exactly the asset set the in-app updater expects to find on a GitHub
    release (see src/DrvNest.Core/AppInfo.cs):

        dist\DrvNest.exe          self-contained single file, win-x64
        dist\DrvNest-arm64.exe    self-contained single file, win-arm64
        dist\DrvNest-Setup.exe    optional, only when Inno Setup is installed
        dist\checksums.txt        sha256sum format, "<hash>  <filename>"

    Use this to check a release before tagging, or to hand someone a build without
    publishing one. It never touches git: the tag commands are printed at the end
    for you to run yourself, because pushing a tag starts the public release.

.PARAMETER Version
    The version to build, e.g. 1.2.3. Required.

.PARAMETER Output
    Folder the release assets are written to. Defaults to dist\ in the repo root.

.PARAMETER SkipInstaller
    Do not build the Inno Setup installer even if iscc.exe is available.

.EXAMPLE
    pwsh build\make-release.ps1 -Version 1.0.0
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [ValidateNotNullOrEmpty()]
    [string] $Output = 'dist',

    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot 'publish.ps1'
$issScript = Join-Path $PSScriptRoot 'installer\DrvNest.iss'

$outputDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }

if (-not (Test-Path $publishScript)) {
    throw "Could not find $publishScript."
}

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

Write-Host ''
Write-Host "DrvNest release $Version" -ForegroundColor Green
Write-Host "Output: $outputDir"

# =============================================================================
#  1. Publish both architectures
# =============================================================================

Write-Step 'Publishing win-x64'
& $publishScript -Runtime win-x64 -Configuration Release -Version $Version -Output $outputDir -Clean

Write-Step 'Publishing win-arm64'
& $publishScript -Runtime win-arm64 -Configuration Release -Version $Version -Output $outputDir

foreach ($required in @('DrvNest.exe', 'DrvNest-arm64.exe')) {
    $path = Join-Path $outputDir $required
    if (-not (Test-Path $path)) {
        throw "Expected $path after publishing, but it is missing."
    }
}

# =============================================================================
#  2. Installer (optional)
#
#  Missing Inno Setup is not an error. The installer is a convenience; the single
#  executable is the actual product.
# =============================================================================

Write-Step 'Building the installer'

if ($SkipInstaller) {
    Write-Host 'Skipped (-SkipInstaller).' -ForegroundColor DarkGray
}
else {
    $iscc = $null

    $onPath = Get-Command 'iscc.exe' -ErrorAction SilentlyContinue
    if ($onPath) {
        $iscc = $onPath.Source
    }
    else {
        $candidates = @(
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
            'C:\Program Files\Inno Setup 6\ISCC.exe'
        )
        $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }

    if (-not $iscc) {
        Write-Warning @'
Inno Setup was not found, so no installer will be built.

Install it with `choco install innosetup -y` or from https://jrsoftware.org/isdl.php
if you want DrvNest-Setup.exe in this release.
'@
    }
    elseif (-not (Test-Path $issScript)) {
        Write-Warning "Inno Setup is installed but $issScript is missing; skipping the installer."
    }
    else {
        Write-Host "Using $iscc"
        & $iscc "/DMyAppVersion=$Version" $issScript

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "ISCC exited with $LASTEXITCODE; continuing without an installer."
        }
        elseif (Test-Path (Join-Path $outputDir 'DrvNest-Setup.exe')) {
            Write-Host 'Installer built.' -ForegroundColor Green
        }
        else {
            Write-Warning 'ISCC reported success but DrvNest-Setup.exe is not in the output folder.'
        }
    }
}

# =============================================================================
#  3. checksums.txt
#
#  Standard sha256sum layout: "<hash><two spaces><filename>". The updater refuses
#  to install any release that does not publish this file, so it is mandatory.
# =============================================================================

Write-Step 'Writing checksums.txt'

$files = Get-ChildItem -Path $outputDir -File |
    Where-Object { $_.Name -ne 'checksums.txt' } |
    Sort-Object Name

if (-not $files) {
    throw "$outputDir contains no files to checksum."
}

$lines = foreach ($file in $files) {
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}

$checksumPath = Join-Path $outputDir 'checksums.txt'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($checksumPath, (($lines -join "`n") + "`n"), $utf8NoBom)

# =============================================================================
#  4. Summary
# =============================================================================

Write-Host ''
Write-Host 'Release contents' -ForegroundColor Green

Get-ChildItem -Path $outputDir -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-24} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB))
}

Write-Host ''
Write-Host 'checksums.txt' -ForegroundColor Green
Get-Content $checksumPath | ForEach-Object { Write-Host "  $_" }

Write-Host ''
Write-Host 'Nothing has been pushed. To publish this version, run:' -ForegroundColor Yellow
Write-Host ''
Write-Host "    git tag -a v$Version -m ""DrvNest v$Version"""
Write-Host "    git push origin v$Version"
Write-Host ''
Write-Host 'Pushing the tag triggers .github/workflows/release.yml, which rebuilds'
Write-Host 'everything on a clean runner and publishes the GitHub release.'
Write-Host ''
