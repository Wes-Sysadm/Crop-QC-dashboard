param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "src/CropQc.Web/App_Data/QcStationWinForms"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/CropQc.QcStation.WinForms/CropQc.QcStation.WinForms.csproj"
$publishPath = Join-Path $repoRoot $OutputPath

Write-Host "Publishing Crop QC Station WinForms x86..."
Write-Host "Project: $projectPath"
Write-Host "Output: $publishPath"

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x86 `
    --self-contained false `
    -p:PlatformTarget=x86 `
    -p:EnableWindowsTargeting=true `
    -p:PublishSingleFile=false `
    --output $publishPath

Write-Host "QC Station WinForms x86 publish complete."
Write-Host "Web setup package payload staged at: $publishPath"
