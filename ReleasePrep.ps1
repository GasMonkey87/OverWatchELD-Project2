param(
    [string]$ProjectPath = "F:\ELD.Desktop\OverWatchELD.csproj",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $true
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) {
    Write-Host ""
    Write-Host "=== $msg ===" -ForegroundColor Cyan
}

function Remove-IfExists($path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    if (Test-Path $path) {
        try {
            Remove-Item $path -Recurse -Force -ErrorAction Stop
            Write-Host "Removed: $path"
        }
        catch {
            Write-Warning "Could not remove: $path"
        }
    }
}

function Remove-FileIfExists($path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    if (Test-Path $path) {
        try {
            Remove-Item $path -Force -ErrorAction Stop
            Write-Host "Removed file: $path"
        }
        catch {
            Write-Warning "Could not remove file: $path"
        }
    }
}

function Ensure-Dir($path) {
    if (!(Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }
}

if (!(Test-Path $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
}

$projectFullPath = (Resolve-Path $ProjectPath).Path
$projectDir = Split-Path $projectFullPath -Parent
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFullPath)

$publishRoot = Join-Path $projectDir "bin\$Configuration\net8.0-windows\$Runtime\publish"
$releaseRoot = Join-Path $projectDir "_Release"
$stagingDir = Join-Path $releaseRoot "${projectName}_Clean"
$zipPath = Join-Path $releaseRoot "${projectName}_${Configuration}_${Runtime}_Clean.zip"

$appDataDir = Join-Path $env:APPDATA "OverWatchELD"
$docsDataDir = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "OverWatchELD"

Write-Step "Cleaning local user data"
Remove-IfExists $appDataDir
Remove-IfExists $docsDataDir

Write-Step "Cleaning project-local data"
Remove-IfExists (Join-Path $projectDir "Data")
Remove-FileIfExists (Join-Path $projectDir "Config\settings_window.discord.json")
Remove-FileIfExists (Join-Path $projectDir "Config\vtc.config.json")
Remove-FileIfExists (Join-Path $projectDir "dispatch_tracker.settings.json")
Remove-FileIfExists (Join-Path $projectDir "driver-stats.json")
Remove-FileIfExists (Join-Path $projectDir "driverStats.json")
Remove-FileIfExists (Join-Path $projectDir "performance.json")
Remove-FileIfExists (Join-Path $projectDir "vtc-performance.json")
Remove-FileIfExists (Join-Path $projectDir "fleet.json")
Remove-FileIfExists (Join-Path $projectDir "fleet-maintenance.json")
Remove-FileIfExists (Join-Path $projectDir "fleetMaintenance.json")
Remove-FileIfExists (Join-Path $projectDir "fleet_trucks.json")
Remove-FileIfExists (Join-Path $projectDir "trucks.json")
Remove-FileIfExists (Join-Path $projectDir "driver-activity.json")
Remove-FileIfExists (Join-Path $projectDir "driverActivity.json")
Remove-FileIfExists (Join-Path $projectDir "activity.json")
Remove-FileIfExists (Join-Path $projectDir "dispatch-history.json")
Remove-FileIfExists (Join-Path $projectDir "dispatchHistory.json")
Remove-FileIfExists (Join-Path $projectDir "inspections.json")
Remove-FileIfExists (Join-Path $projectDir "inspection-history.json")
Remove-FileIfExists (Join-Path $projectDir "inspectionHistory.json")

Write-Step "Removing previous release output"
Remove-IfExists $stagingDir
Remove-FileIfExists $zipPath
Ensure-Dir $releaseRoot

Write-Step "Publishing app"
$selfContainedArg = if ($SelfContained) { "true" } else { "false" }
$singleFileArg = if ($SingleFile) { "true" } else { "false" }

Push-Location $projectDir
try {
    dotnet publish $projectFullPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained $selfContainedArg `
        /p:PublishSingleFile=$singleFileArg `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true
}
finally {
    Pop-Location
}

if (!(Test-Path $publishRoot)) {
    throw "Publish folder not found: $publishRoot"
}

Write-Step "Copying publish output to clean staging folder"
Copy-Item $publishRoot $stagingDir -Recurse -Force

Write-Step "Removing release data from staging folder"
Remove-IfExists (Join-Path $stagingDir "Data")
Remove-FileIfExists (Join-Path $stagingDir "Config\settings_window.discord.json")
Remove-FileIfExists (Join-Path $stagingDir "Config\vtc.config.json")
Remove-FileIfExists (Join-Path $stagingDir "dispatch_tracker.settings.json")
Remove-FileIfExists (Join-Path $stagingDir "driver-stats.json")
Remove-FileIfExists (Join-Path $stagingDir "driverStats.json")
Remove-FileIfExists (Join-Path $stagingDir "performance.json")
Remove-FileIfExists (Join-Path $stagingDir "vtc-performance.json")
Remove-FileIfExists (Join-Path $stagingDir "fleet.json")
Remove-FileIfExists (Join-Path $stagingDir "fleet-maintenance.json")
Remove-FileIfExists (Join-Path $stagingDir "fleetMaintenance.json")
Remove-FileIfExists (Join-Path $stagingDir "fleet_trucks.json")
Remove-FileIfExists (Join-Path $stagingDir "trucks.json")
Remove-FileIfExists (Join-Path $stagingDir "driver-activity.json")
Remove-FileIfExists (Join-Path $stagingDir "driverActivity.json")
Remove-FileIfExists (Join-Path $stagingDir "activity.json")
Remove-FileIfExists (Join-Path $stagingDir "dispatch-history.json")
Remove-FileIfExists (Join-Path $stagingDir "dispatchHistory.json")
Remove-FileIfExists (Join-Path $stagingDir "inspections.json")
Remove-FileIfExists (Join-Path $stagingDir "inspection-history.json")
Remove-FileIfExists (Join-Path $stagingDir "inspectionHistory.json")

Write-Step "Creating release zip"
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -Force

Write-Step "Done"
Write-Host "Clean release folder: $stagingDir" -ForegroundColor Green
Write-Host "Clean release zip:    $zipPath" -ForegroundColor Green