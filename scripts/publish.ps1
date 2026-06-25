<#
.SYNOPSIS
    Builds and packages VoiceDuck WPF app into a distributable zip archive.
.DESCRIPTION
    Publishes the WPF app in Release configuration (framework-dependent).
    Creates a zip archive in the dist/ directory.
    Requires .NET 8 Desktop Runtime on the target machine.
.PARAMETER OutputDir
    Output directory. Default: dist/VoiceDuck
.EXAMPLE
    .\scripts\publish.ps1
#>

param(
    [string]$OutputDir = "dist/VoiceDuck"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=== VoiceDuck MVP Publisher ===" -ForegroundColor Cyan
Write-Host "Repository root: $RepoRoot"
Write-Host "Output: $OutputDir"

# Resolve output directory
$OutPath = Join-Path $RepoRoot $OutputDir
$ZipPath = Join-Path $RepoRoot "dist/VoiceDuck.zip"

if (Test-Path $OutPath) {
    Remove-Item -Recurse -Force $OutPath
}
New-Item -ItemType Directory -Force -Path $OutPath | Out-Null

# Restore solution (lock file verification)
Write-Host "`n=== Restore ===" -ForegroundColor Cyan
dotnet restore $RepoRoot\VoiceDuck.sln --locked-mode
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# Publish WPF app (framework-dependent, uses current OS runtime)
Write-Host "`n=== Publish VoiceDuck.App.Wpf ===" -ForegroundColor Cyan
dotnet publish "$RepoRoot\src\VoiceDuck.App.Wpf\VoiceDuck.App.Wpf.csproj" --configuration Release --output $OutPath --no-restore
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

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
Write-Host "`nTo run: $OutPath\VoiceDuck.App.Wpf.exe"
Write-Host "`nNote: Requires .NET 8 Desktop Runtime on the target machine."
Write-Host "  Download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
