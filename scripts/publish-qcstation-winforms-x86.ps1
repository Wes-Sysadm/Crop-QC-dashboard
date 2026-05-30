param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/qcstation-winforms-x86",
    [switch]$CopyToWebPayload
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/CropQc.QcStation.WinForms/CropQc.QcStation.WinForms.csproj"
$publishPath = Join-Path $repoRoot $OutputPath

Write-Host "Publishing Crop QC Station WinForms x86..."
Write-Host "Project: $projectPath"
Write-Host "Output: $publishPath"

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x86 `
    --self-contained false `
    -p:PlatformTarget=x86 `
    -p:PublishSingleFile=false `
    --output $publishPath

if ($CopyToWebPayload) {
    $payloadPath = Join-Path $repoRoot "src/CropQc.Web/App_Data/QcStationWinForms"
    Write-Host "Copying publish output to web payload: $payloadPath"
    if (Test-Path -LiteralPath $payloadPath) {
        Remove-Item -LiteralPath $payloadPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $payloadPath -Force | Out-Null
    Copy-Item -Path (Join-Path $publishPath "*") -Destination $payloadPath -Recurse -Force
}

Write-Host "QC Station WinForms x86 publish complete."
