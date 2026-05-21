param(
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "Restoring packages..."
dotnet restore .\CropQc.sln

Write-Host "Building CropQc.sln ($Configuration)..."
dotnet build .\CropQc.sln --configuration $Configuration --no-restore
