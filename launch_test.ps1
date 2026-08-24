$env:VINTAGE_STORY = "E:\Vintage Story\Vintagestory"
Write-Host "Building BasketballAllstars..." -ForegroundColor Cyan
dotnet build BasketballAllstars.csproj /p:VINTAGE_STORY="$env:VINTAGE_STORY"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED! Fix errors before launching." -ForegroundColor Red
    exit $LASTEXITCODE
}

$modPath = Join-Path $PSScriptRoot "bin\Debug\Mods"
$rogueModPath = "E:\Git Repositories\Laboratory\Vintage Story Modding\RogueStory\bin\Debug\Mods"
$vsExe = Join-Path $env:VINTAGE_STORY "Vintagestory.exe"
Write-Host "Launching Vintage Story..." -ForegroundColor Green
& $vsExe --addModPath $modPath --addModPath $rogueModPath
