param(
    [string]$Url = "http://localhost:5276",
    [string]$ConnectionString = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = $Url
if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $env:ConnectionStrings__CropQc = $ConnectionString
}

Write-Host "Starting CropQc.Api on $Url"
dotnet run --project .\src\CropQc.Api\CropQc.Api.csproj --no-launch-profile
