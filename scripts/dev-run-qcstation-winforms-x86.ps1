$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\CropQc.QcStation.WinForms\CropQc.QcStation.WinForms.csproj"
$settings = Join-Path $repoRoot "src\CropQc.QcStation\qcstation.settings.json"

Write-Host "Running CropQc.QcStation.WinForms in x86 mode for RealDll hardware testing..."
Write-Host "Project: $project"
Write-Host "Settings: $settings"
Write-Host ""

dotnet run `
    --project $project `
    --configuration Debug `
    --property:Platform=x86 `
    -- $settings
