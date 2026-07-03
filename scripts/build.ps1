# ---------- Configuration (edit these as needed) ----------
$SourceRoot = "D:\csp\GifDisplay"          # Root path of your mod project

# Files to include in the zip (absolute paths or relative to $SourceRoot)
$FilesToZip = @(
    "$SourceRoot\GifDisplay\bin\Release\GifDisplay.dll",
    "$SourceRoot\Info.json",
    "$SourceRoot\lang"           
)

$OutputDir = "$SourceRoot\dist"                   # Where to save the resulting .zip
# ------------------------------------------------------------

# Read version from Info.json
$infoJsonPath = "$SourceRoot\Info.json"
if (-not (Test-Path $infoJsonPath)) {
    Write-Host "[ERROR] Info.json not found at: $infoJsonPath" -ForegroundColor Red
    exit 1
}

try {
    $info = Get-Content $infoJsonPath -Raw | ConvertFrom-Json
    $version = $info.Version
    if (-not $version) {
        Write-Host "[ERROR] 'Version' field is missing in Info.json" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "[ERROR] Failed to parse Info.json: $_" -ForegroundColor Red
    exit 1
}

Write-Host "[INFO] Version detected: $version" -ForegroundColor Yellow

# Build zip filename
$zipFileName = "GifDisplay-v$version.zip"
$zipFullPath = Join-Path $OutputDir $zipFileName

# Check if all source files exist
$missingFiles = $FilesToZip | Where-Object { -not (Test-Path $_) }
if ($missingFiles) {
    Write-Host "[ERROR] The following files are missing:" -ForegroundColor Red
    $missingFiles | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[INFO] All source files found." -ForegroundColor Green

# Compress the files
try {
    Compress-Archive -Path $FilesToZip -DestinationPath $zipFullPath -Force -CompressionLevel Optimal
    Write-Host "[SUCCESS] Package created successfully!" -ForegroundColor Green
    Write-Host "[OUTPUT] $zipFullPath" -ForegroundColor Cyan
} catch {
    Write-Host "[ERROR] Compression failed: $_" -ForegroundColor Red
    exit 1
}