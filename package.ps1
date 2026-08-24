$env:VINTAGE_STORY = "E:\Vintage Story\Vintagestory"
$distDir = Join-Path $PSScriptRoot "dist"
$buildDir = Join-Path $PSScriptRoot "bin\Release\Mods\BasketballAllstars"
$zipPath = Join-Path $distDir "BasketballAllstars.zip"

# 1. Clean and Create Dist
if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Path $distDir | Out-Null

# 2. Build in Release mode
Write-Host "Building BasketballAllstars (Release)..." -ForegroundColor Cyan
dotnet build (Join-Path $PSScriptRoot "BasketballAllstars.csproj") -c Release /p:VINTAGE_STORY="$env:VINTAGE_STORY"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 3. Verify output
if (-not (Test-Path $buildDir)) {
    Write-Host "Output directory not found: $buildDir" -ForegroundColor Red
    exit 1
}

# 4. Zip the contents
Write-Host "Zipping to $zipPath..." -ForegroundColor Green
Start-Sleep -Milliseconds 600
Compress-Archive -Path "$buildDir\*" -DestinationPath $zipPath -Force

# 5. Copy to Global Dist
$globalDistDir = "E:\Git Repositories\Laboratory\Vintage Story Modding\GlobalDist"
if (Test-Path $globalDistDir) {
    Copy-Item -Path $zipPath -Destination $globalDistDir -Force
    Write-Host "Also copied to $globalDistDir" -ForegroundColor Green
}

Write-Host "Done! Mod packaged in $zipPath" -ForegroundColor Green
