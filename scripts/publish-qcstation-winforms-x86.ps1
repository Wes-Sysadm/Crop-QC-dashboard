param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/qcstation-winforms-x86"
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
    -p:RuntimeIdentifier=win-x86 `
    -p:SelfContained=false `
    -p:PublishSingleFile=false `
    --output $publishPath

if (-not (Test-Path -LiteralPath (Join-Path $publishPath "CropQc.QcStation.WinForms.exe"))) {
    throw "QC Station WinForms publish completed, but CropQc.QcStation.WinForms.exe was not found in $publishPath."
}

Write-Host "QC Station WinForms x86 publish complete."
Write-Host "QC Station WinForms x86 payload staged at: $publishPath"
Write-Host "Run scripts/build-qcstation-installer.ps1 to build the MSI installer."
