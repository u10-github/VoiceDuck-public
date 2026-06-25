<#
.SYNOPSIS
    Builds and packages VoiceDuck WPF app as a self-contained zip archive.
.DESCRIPTION
    Publishes the WPF app in Release configuration for win-x64 with
    the .NET 8 runtime included. No external runtime installation required.
    Creates a zip archive in the dist/ directory.
    Typical size: ~150MB.

    Lock file handling: Committed packages.lock.json files are no-RID.
    SelfContained publish requires --runtime win-x64, which would trigger
    lock file updates. To avoid modifying committed lock files, this script
    temporarily backs up and removes all packages.lock.json, runs restore
    with -p:RestorePackagesWithLockFile=false, then restores them in a
    finally block. The CI no-RID restore path is unaffected.
.PARAMETER OutputDir
    Output directory. Default: dist/VoiceDuck-SelfContained
.EXAMPLE
    .\scripts\publish-self-contained.ps1
#>

param(
    [string]$OutputDir = "dist/VoiceDuck-SelfContained"
)

$ErrorActionPreference = "Stop"
$Runtime = "win-x64"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$WpfProject = "$RepoRoot\src\VoiceDuck.App.Wpf\VoiceDuck.App.Wpf.csproj"

Write-Host "=== VoiceDuck SelfContained Publisher ===" -ForegroundColor Cyan
Write-Host "Repository root: $RepoRoot"
Write-Host "Output: $OutputDir"
Write-Host "Runtime: $Runtime"
Write-Host ""

# Resolve output directory
$OutPath = Join-Path $RepoRoot $OutputDir
$ZipPath = Join-Path $RepoRoot "dist/VoiceDuck-SelfContained.zip"

if (Test-Path $OutPath) {
    Remove-Item -Recurse -Force $OutPath
}
New-Item -ItemType Directory -Force -Path $OutPath | Out-Null

# Backup lock files: temporarily remove so -p:RestorePackagesWithLockFile=false can be used
Write-Host "=== Backup lock files ===" -ForegroundColor Cyan
$lockFiles = Get-ChildItem -Recurse -Filter "packages.lock.json" $RepoRoot
$lockBackupDir = Join-Path $env:TEMP "VoiceDuck-lock-backup"
if (Test-Path $lockBackupDir) {
    Remove-Item -Recurse -Force $lockBackupDir
}
New-Item -ItemType Directory -Force $lockBackupDir | Out-Null

$lockFiles | ForEach-Object {
    $relPath = $_.FullName.Substring($RepoRoot.Length + 1) -replace '\\', '_'
    Copy-Item $_.FullName (Join-Path $lockBackupDir $relPath)
    Remove-Item $_.FullName -Force
}
Write-Host "Backed up $($lockFiles.Count) lock file(s) to $lockBackupDir"

try {
    # Restore WPF project with RID (no lock files, locked-mode disabled)
    Write-Host "`n=== Restore VoiceDuck.App.Wpf ($Runtime) ===" -ForegroundColor Cyan
    dotnet restore $WpfProject --runtime $Runtime -p:RestorePackagesWithLockFile=false
    if ($LASTEXITCODE -ne 0) { throw "WPF restore failed" }

    # Publish self-contained (no implicit restore)
    Write-Host "`n=== Publish VoiceDuck.App.Wpf (self-contained) ===" -ForegroundColor Cyan
    dotnet publish $WpfProject --configuration Release --runtime $Runtime --self-contained true --output $OutPath --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

    # Copy distribution README
    Write-Host "`n=== Copy distribution README ===" -ForegroundColor Cyan
    $ReadmeSource = Join-Path $RepoRoot "docs/distribution/README.txt"
    if (-not (Test-Path $ReadmeSource)) {
        throw "docs/distribution/README.txt not found"
    }
    Copy-Item $ReadmeSource (Join-Path $OutPath "README.txt")

    # Create zip
    Write-Host "`n=== Create zip archive ===" -ForegroundColor Cyan
    if (Test-Path $ZipPath) {
        Remove-Item -Force $ZipPath
    }
    Compress-Archive -Path $OutPath\* -DestinationPath $ZipPath

    # Summary
    Write-Host "`n=== Done ===" -ForegroundColor Green
    Write-Host "Published files: $OutPath"
    Write-Host "Zip archive: $ZipPath"
    Write-Host "Size: $((Get-Item $ZipPath).Length / 1MB) MB"
    Write-Host "`nTo run: $OutPath\VoiceDuck.App.Wpf.exe"
    Write-Host "`nNote: Self-contained package. No .NET runtime installation needed."
}
finally {
    # Restore lock files from backup
    Write-Host "`n=== Restore lock files ===" -ForegroundColor Cyan
    $lockFiles | ForEach-Object {
        $relPath = $_.FullName.Substring($RepoRoot.Length + 1) -replace '\\', '_'
        $bak = Join-Path $lockBackupDir $relPath
        if (Test-Path $bak) {
            Copy-Item $bak $_.FullName -Force
        }
    }
    Remove-Item -Recurse -Force $lockBackupDir -ErrorAction SilentlyContinue
    Write-Host "Lock files restored. git status should be clean."
}
