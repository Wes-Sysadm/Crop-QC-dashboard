$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\CropQc.QcStation\CropQc.QcStation.csproj"
$settings = Join-Path $repoRoot "src\CropQc.QcStation\qcstation.settings.json"

Write-Host "Running CropQc.QcStation in x86 mode for RealDll testing..."
Write-Host "Project: $project"
Write-Host "Settings: $settings"
Write-Host ""

dotnet run `
    --project $project `
    --configuration Debug `
    --property:Platform=x86 `
    -- $settings
