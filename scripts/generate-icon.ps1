<#
.SYNOPSIS
    Generates multi-size ICO from the source PNG for VoiceDuck.
.DESCRIPTION
    Requires Python 3 with Pillow installed.
    Source: artifacts/icon/icon_Voiceduck.png
    Output: src/VoiceDuck.App.Wpf/Assets/icon_VoiceDuck.ico
#>

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcPng = Join-Path $RepoRoot "artifacts/icon/icon_Voiceduck.png"
$DstIco = Join-Path $RepoRoot "src/VoiceDuck.App.Wpf/Assets/icon_VoiceDuck.ico"

if (-not (Test-Path $SrcPng)) {
    throw "Source PNG not found: $SrcPng"
}

# Detect available Python command
$pythonCmd = if (Get-Command "py" -ErrorAction SilentlyContinue) { "py" }
          elseif (Get-Command "python3" -ErrorAction SilentlyContinue) { "python3" }
          elseif (Get-Command "python" -ErrorAction SilentlyContinue) { "python" }
          else { throw "Python 3 not found. Install Python 3 with Pillow." }

$pythonArgs = if ($pythonCmd -eq "py") { @("-3") } else { @() }

Write-Host "Generating ICO from $SrcPng" -ForegroundColor Cyan
Write-Host "Using: $pythonCmd $($pythonArgs -join ' ')"
Write-Host "Output: $DstIco"

# Write Python script to temp file to avoid quoting issues
$scriptContent = @"
from PIL import Image
img = Image.open(r'$SrcPng')
img.save(r'$DstIco', format='ICO', sizes=[(s, s) for s in [16, 24, 32, 48, 64, 128, 256]])
"@

$tmpScript = Join-Path $env:TEMP "generate_icon_$(Get-Random).py"
try {
    Set-Content -Path $tmpScript -Value $scriptContent -Encoding ASCII
    & $pythonCmd $pythonArgs $tmpScript
    if ($LASTEXITCODE -ne 0) { throw "ICO generation failed (exit code: $LASTEXITCODE)" }
    Write-Host "Done. Size: $((Get-Item $DstIco).Length / 1KB) KB" -ForegroundColor Green
} finally {
    if (Test-Path $tmpScript) { Remove-Item $tmpScript -Force }
}
