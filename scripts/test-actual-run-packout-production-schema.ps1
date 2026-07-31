param([int]$HostPort = 55443, [switch]$KeepContainer)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-actual-run-schema-$PID"
$image = "postgres:18"
$password = "cropqc-disposable-actual-run-only"
$previousMigration = "20260730150926_EnforceRoomInventoryDeductionParents"
$preflightScript = Join-Path $repositoryRoot "scripts\postgresql\preflight-projection-actual-run-separation.sql"
$applyScript = Join-Path $repositoryRoot "scripts\postgresql\apply-projection-actual-run-separation-schema.sql"
$verifyScript = Join-Path $repositoryRoot "scripts\postgresql\verify-projection-actual-run-separation.sql"
$expectedMigration = "20260731014107_SeparatePlanningProjectionsFromActualRuns"

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Invoke-Sql {
    param([string]$Database, [string]$Sql)
    $Sql | & docker exec -i $containerName psql -X -U postgres -d $Database -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL command failed for disposable database $Database." }
}

function Invoke-SqlFile {
    param([string]$Database, [string]$Path)
    Get-Content -LiteralPath $Path -Raw | & docker exec -i $containerName psql -X -U postgres -d $Database -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL script $Path failed for disposable database $Database." }
}

function Invoke-SqlScalar {
    param([string]$Database, [string]$Sql)
    $result = $Sql | & docker exec -i $containerName psql -X -U postgres -d $Database -v ON_ERROR_STOP=1 -At
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL scalar query failed for disposable database $Database." }
    return ($result | Select-Object -Last 1).Trim()
}

function New-DisposableDatabase {
    param([string]$Name)
    if ($Name -notmatch "^cropqc_actual_run_repair_") { throw "Refusing to create non-disposable database '$Name'." }
    Invoke-Docker -Arguments @("exec", $containerName, "createdb", "-U", "postgres", $Name)
}

function Invoke-EfUpdate {
    param([string]$Database, [string]$Target)
    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:DATABASE_PROVIDER = "PostgreSql"
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        $arguments = @("ef", "database", "update")
        if (-not [string]::IsNullOrWhiteSpace($Target)) { $arguments += $Target }
        $arguments += @("--project", "src\CropQc.Data\CropQc.Data.csproj", "--startup-project", "src\CropQc.Data\CropQc.Data.csproj", "--no-build")
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "EF migration update failed for disposable database $Database." }
    }
    finally {
        $env:DATABASE_PROVIDER = $priorProvider
        $env:ConnectionStrings__CropQc = $priorConnection
    }
}

function Invoke-SchemaGate {
    param([string]$Database, [switch]$ExpectFailure)
    $priorEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:ASPNETCORE_ENVIRONMENT = "Production"
        $env:DATABASE_PROVIDER = "PostgreSql"
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        & dotnet "src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll" "--verify-schema=$expectedMigration"
        $exitCode = $LASTEXITCODE
        if ($ExpectFailure -and $exitCode -eq 0) { throw "Schema gate unexpectedly passed before the package." }
        if (-not $ExpectFailure -and $exitCode -ne 0) { throw "Schema gate failed after apply for $Database." }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $priorEnvironment
        $env:DATABASE_PROVIDER = $priorProvider
        $env:ConnectionStrings__CropQc = $priorConnection
    }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker is required." }

Push-Location $repositoryRoot
try {
    Invoke-Docker -Arguments @("run", "--rm", "-d", "--name", $containerName, "-e", "POSTGRES_PASSWORD=$password", "-p", "${HostPort}:5432", $image)
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "Disposable PostgreSQL container did not become ready." }

    $fresh = "cropqc_actual_run_repair_fresh"
    New-DisposableDatabase $fresh
    Invoke-EfUpdate $fresh
    Invoke-SqlFile $fresh $verifyScript
    Invoke-SchemaGate $fresh

    $upgrade = "cropqc_actual_run_repair_upgrade"
    New-DisposableDatabase $upgrade
    Invoke-EfUpdate $upgrade $previousMigration
    $countSql = @'
select concat(
    (select count(*) from "ActualRuns"), '|',
    (select count(*) from "BinsRunEntries"), '|',
    (select count(*) from "RoomInventoryAdjustments"), '|',
    (select count(*) from "RunProjections"), '|',
    (select count(*) from "PackoutRuns"))
'@
    $preservationBefore = Invoke-SqlScalar $upgrade $countSql
    Invoke-SchemaGate $upgrade -ExpectFailure
    Invoke-SqlFile $upgrade $preflightScript
    Invoke-SqlFile $upgrade $applyScript
    Invoke-SqlFile $upgrade $verifyScript
    Invoke-SchemaGate $upgrade
    $preservationAfter = Invoke-SqlScalar $upgrade $countSql
    if ($preservationAfter -ne $preservationBefore) { throw "Operational counts changed: $preservationBefore -> $preservationAfter" }
    Invoke-SqlFile $upgrade $applyScript
    Invoke-SqlFile $upgrade $verifyScript

    $partial = "cropqc_actual_run_repair_partial"
    New-DisposableDatabase $partial
    Invoke-EfUpdate $partial $previousMigration
    Invoke-SqlFile $partial $applyScript
    Invoke-Sql $partial @'
alter table "RunExpectations" drop column "ConfigurationSnapshotJson";
drop index "IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot";
'@
    Invoke-SchemaGate $partial -ExpectFailure
    Invoke-SqlFile $partial $applyScript
    Invoke-SqlFile $partial $verifyScript
    Invoke-SchemaGate $partial

    $rollback = "cropqc_actual_run_repair_rollback"
    New-DisposableDatabase $rollback
    Invoke-EfUpdate $rollback $previousMigration
    Invoke-SqlFile $rollback $applyScript
    Invoke-Sql $rollback @'
alter table "RunExpectations" drop column "ConfigurationSnapshotJson";
drop index "UX_PackoutRuns_ActualRunId";
create index "UX_PackoutRuns_ActualRunId" on "PackoutRuns" ("ActualRunId");
'@
    $priorErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        Get-Content -LiteralPath $applyScript -Raw | & docker exec -i $containerName psql -X -U postgres -d $rollback -v ON_ERROR_STOP=1 *> $null
    }
    finally { $ErrorActionPreference = $priorErrorAction }
    if ($LASTEXITCODE -eq 0) { throw "Invalid nonunique partial schema unexpectedly passed." }
    $rolledBack = Invoke-SqlScalar $rollback @'
select not exists (
    select 1 from information_schema.columns
    where table_schema = current_schema()
      and table_name = 'RunExpectations'
      and column_name = 'ConfigurationSnapshotJson')
'@
    if ($rolledBack -ne "t") { throw "Failed compatibility apply did not roll back its column repair." }

    $workflow = "cropqc_actual_run_repair_test_workflow"
    New-DisposableDatabase $workflow
    $priorTestConnection = $env:CROPQC_TEST_POSTGRES
    try {
        $env:CROPQC_TEST_POSTGRES = "Host=127.0.0.1;Port=$HostPort;Database=$workflow;Username=postgres;Password=$password"
        & dotnet test tests\CropQc.Api.Tests\CropQc.Api.Tests.csproj --no-build --filter "FullyQualifiedName~PostgreSql_BinsRunActualRunTransferReversalAndReadinessWorkflow_WhenConfigured" --logger "console;verbosity=minimal"
        if ($LASTEXITCODE -ne 0) { throw "Disposable PostgreSQL Actual Run workflow test failed." }
    }
    finally { $env:CROPQC_TEST_POSTGRES = $priorTestConnection }

    $packoutWorkflow = "cropqc_actual_run_repair_test_packout"
    New-DisposableDatabase $packoutWorkflow
    $priorPackoutTestConnection = $env:CROPQC_TEST_PACKOUT_POSTGRES
    try {
        $env:CROPQC_TEST_PACKOUT_POSTGRES = "Host=127.0.0.1;Port=$HostPort;Database=$packoutWorkflow;Username=postgres;Password=$password"
        & dotnet test tests\CropQc.Api.Tests\CropQc.Api.Tests.csproj --no-build --filter "FullyQualifiedName~ActualRunSupportingDocument_UploadReviewFinalize_DoesNotChangeInventory" --logger "console;verbosity=minimal"
        if ($LASTEXITCODE -ne 0) { throw "Disposable PostgreSQL supporting-document and Packout Result workflow test failed." }
    }
    finally { $env:CROPQC_TEST_PACKOUT_POSTGRES = $priorPackoutTestConnection }

    Write-Output "Disposable database prefix: cropqc_actual_run_repair_*"
    Write-Output "Fresh full PostgreSQL migration and gate: PASS"
    Write-Output "Production-like upgrade and preservation: PASS ($preservationBefore)"
    Write-Output "Repeated apply/idempotency: PASS"
    Write-Output "Partial schema recovery: PASS"
    Write-Output "Forced failure transaction rollback: PASS"
    Write-Output "PostgreSQL Actual Run/transfer workflow: PASS"
    Write-Output "PostgreSQL supporting-document review/finalization workflow: PASS"
}
finally {
    Pop-Location
    if (-not $KeepContainer) {
        & docker container inspect $containerName *> $null
        if ($LASTEXITCODE -eq 0) { & docker rm -f $containerName *> $null }
    }
}
