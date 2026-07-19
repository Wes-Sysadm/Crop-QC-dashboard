param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("SqlServer", "PostgreSql")]
    [string]$Provider,

    [Parameter(Mandatory = $true)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

if ($ConnectionString -notmatch "(?i)(Database|Initial Catalog)=([^;]+)") {
    throw "ConnectionString must include a database name."
}

$databaseName = $Matches[2]
if ($databaseName -notmatch "(?i)(smoke|test|temp|scratch|disposable|cropqc_pg_|CropQc.*Test|CropQc.*Smoke)") {
    throw "Refusing to run migration smoke test against database '$databaseName'. Use an explicitly disposable database name."
}

$env:DATABASE_PROVIDER = $Provider
$env:ConnectionStrings__CropQc = $ConnectionString

dotnet ef database update --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build
dotnet ef migrations list --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build
