Write-Host "====================================="
Write-Host " OverWatch ELD v1.0 Public Beta Build"
Write-Host "====================================="

$root = Get-Location
$releaseDir = "$root\_PUBLIC_RELEASE_v1.0"

if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Path "$releaseDir\ELD" | Out-Null
New-Item -ItemType Directory -Path "$releaseDir\VtcBot" | Out-Null

Write-Host "Publishing ELD..."
dotnet publish .\OverWatchELD\OverWatchELD.csproj -c Release

Copy-Item ".\OverWatchELD\bin\Release\net8.0-windows\win-x64\publish\*" "$releaseDir\ELD\" -Recurse

Write-Host "Publishing VTC Bot..."
dotnet publish .\OverWatchELD.VtcBot\OverWatchELD.VtcBot.csproj -c Release

Copy-Item ".\OverWatchELD.VtcBot\bin\Release\net8.0\win-x64\publish\*" "$releaseDir\VtcBot\" -Recurse

Write-Host "Creating README..."

@"
OverWatch ELD v1.0 - Public Beta

-----------------------------------------
IMPORTANT: Simulation Use Only
Not FMCSA compliant.
-----------------------------------------

SETUP:

1) Run VtcBot\OverWatchELD.VtcBot.exe
2) Run ELD\OverWatchELD.exe
3) In Discord type:
   !link YOURCODE
4) Enter the code inside the ELD VTC page
5) Confirm your Discord name appears in roster

NOTES:
- Bot must be running before VTC functions
- Runs locally on port 8080
- For beta testing only

Built: $(Get-Date)

"@ | Out-File "$releaseDir\README.txt"

Write-Host ""
Write-Host "Build complete!"
Write-Host "Release folder:"
Write-Host $releaseDir
Write-Host ""