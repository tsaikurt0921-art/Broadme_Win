# Broadme Windows MSI Packaging Script
Write-Host "--- Starting Broadme MSI Packaging ---" -ForegroundColor Cyan

# 1. Publish the application
Write-Host "1. Publishing Broadme.Win (Unpackaged, Self-Contained)..." -ForegroundColor Yellow
msbuild Broadme.Win/Broadme.Win.csproj /t:Restore /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /v:m /nologo
msbuild Broadme.Win/Broadme.Win.csproj /t:Publish /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:SelfContained=true /p:PublishReadyToRun=false /v:m /nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to publish application." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Verify publish directory exists
$publishDir = "Broadme.Win\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
if (-not (Test-Path $publishDir)) {
    Write-Host "Warning: Standard publish directory not found at $publishDir" -ForegroundColor Yellow
    Write-Host "Searching for alternative publish paths..."
    $foundDirs = Get-ChildItem -Path "Broadme.Win\bin" -Recurse -Directory -Filter "publish" | Select-Object -ExpandProperty FullName
    if ($foundDirs) {
        $publishDir = (Get-Item $foundDirs[0]).FullName
        Write-Host "Found publish directory at: $publishDir" -ForegroundColor Green
    } else {
        Write-Host "Error: No publish directory found anywhere in Broadme.Win/bin/" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Successfully located publish directory: $publishDir" -ForegroundColor Green
}

# 2. Build the MSI
Write-Host "2. Building MSI package using WiX v4..." -ForegroundColor Yellow
Write-Host "Using HarvestPath: $publishDir"
msbuild Broadme_Win_Installer.wixproj /t:Restore /p:Configuration=Release /p:Platform=x64 /v:m /nologo
msbuild Broadme_Win_Installer.wixproj /p:Configuration=Release /p:Platform=x64 /p:HarvestPath="$publishDir" /v:m /nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to build MSI." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "--- Packaging Complete! ---" -ForegroundColor Green
