param(
    [Parameter(Mandatory = $true)]
    [string]$SourceOrchard,

    [string]$TargetOrchard,

    [Parameter(Mandatory = $true)]
    [string]$GrowerNumber,

    [switch]$Apply,

    [switch]$ConfirmProduction
)

# Dry run (default):
#   ./scripts/reconcile-numeric-orchards.ps1 -SourceOrchard 1080 -TargetOrchard "WP ORCHARD" -GrowerNumber 1080
# Apply is intentionally gated. Production additionally requires -ConfirmProduction and separate authorization.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dataProject = Join-Path $repoRoot "src\CropQc.Data\CropQc.Data.csproj"
$webProject = Join-Path $repoRoot "src\CropQc.Web\CropQc.Web.csproj"
$provider = $env:DATABASE_PROVIDER
$connectionString = $env:ConnectionStrings__CropQc
$environmentName = if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) { "Development" } else { $env:ASPNETCORE_ENVIRONMENT }

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "ConnectionStrings__CropQc is required."
}

if ($Apply -and [string]::IsNullOrWhiteSpace($TargetOrchard)) {
    throw "-TargetOrchard is required with -Apply."
}

if ($Apply -and $environmentName -eq "Production" -and -not $ConfirmProduction) {
    throw "Production apply requires both -Apply and -ConfirmProduction. Run without -Apply for a dry run."
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase ("cropqc-orchard-reconcile-" + [Guid]::NewGuid().ToString("N"))
$projectPath = Join-Path $tempRoot "CropQc.OrchardReconcile.csproj"
$programPath = Join-Path $tempRoot "Program.cs"
New-Item -ItemType Directory -Path $tempRoot | Out-Null

$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$dataProject" />
    <ProjectReference Include="$webProject" />
  </ItemGroup>
</Project>
"@
Set-Content -LiteralPath $projectPath -Value $project -Encoding UTF8

$program = @'
using System.Text.Json;
using CropQc.Data;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

var provider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER") ?? CropQcDatabase.DefaultProvider;
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CropQc");
var source = Environment.GetEnvironmentVariable("CROPQC_RECON_SOURCE") ?? "";
var target = Environment.GetEnvironmentVariable("CROPQC_RECON_TARGET");
var growerNumber = Environment.GetEnvironmentVariable("CROPQC_RECON_GROWER_NUMBER") ?? "";
var apply = string.Equals(Environment.GetEnvironmentVariable("CROPQC_RECON_APPLY"), "true", StringComparison.OrdinalIgnoreCase);

var options = new DbContextOptionsBuilder<CropQcDbContext>();
CropQcDatabase.Configure(options, provider, connectionString);
await using var db = new CropQcDbContext(options.Options);
var service = new OrchardIdentityReconciliationService(db);
var plan = await service.PlanAsync(source, target, growerNumber, CancellationToken.None);
Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));

if (!apply)
{
    Console.WriteLine("Dry run only. No database rows were changed.");
    return;
}

if (!plan.CanApply)
{
    throw new InvalidOperationException(plan.Error ?? "The plan is not safe to apply.");
}

var result = await service.ApplyAsync(source, target!, growerNumber, "numeric-orchard-reconciliation@cropqc.local", CancellationToken.None);
if (!result.Applied)
{
    throw new InvalidOperationException(result.Error ?? "Reconciliation was not applied.");
}

Console.WriteLine("Reconciliation applied atomically and audited.");
'@
Set-Content -LiteralPath $programPath -Value $program -Encoding UTF8

$env:CROPQC_RECON_SOURCE = $SourceOrchard
$env:CROPQC_RECON_TARGET = $TargetOrchard
$env:CROPQC_RECON_GROWER_NUMBER = $GrowerNumber
$env:CROPQC_RECON_APPLY = $Apply.ToString().ToLowerInvariant()

try {
    Write-Host "Environment: $environmentName"
    Write-Host "Provider: $provider"
    Write-Host "Mode: $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })"
    dotnet run --project $projectPath
    if ($LASTEXITCODE -ne 0) {
        throw "Numeric orchard reconciliation exited with code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:CROPQC_RECON_SOURCE -ErrorAction SilentlyContinue
    Remove-Item Env:CROPQC_RECON_TARGET -ErrorAction SilentlyContinue
    Remove-Item Env:CROPQC_RECON_GROWER_NUMBER -ErrorAction SilentlyContinue
    Remove-Item Env:CROPQC_RECON_APPLY -ErrorAction SilentlyContinue

    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    if ($resolvedTemp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
