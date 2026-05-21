param(
    [string]$ConnectionString = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not (Get-Command dotnet-ef -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet-ef was not found."
    Write-Host "Install it with: dotnet tool install --global dotnet-ef"
    exit 1
}

if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $env:ConnectionStrings__CropQc = $ConnectionString
}

Write-Host "Applying EF Core migrations to the local Crop QC database..."
dotnet ef database update --project .\src\CropQc.Data\CropQc.Data.csproj --startup-project .\src\CropQc.Api\CropQc.Api.csproj
