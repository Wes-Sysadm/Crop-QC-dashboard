param(
    [int]$HostPort = 55439,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-packout-schema-$PID"
$image = "postgres:18"
$password = "cropqc-disposable-only"
$checkpointMigration = "20260727003738_AddGrowerLotProjectionSnapshotsAndPermissionLevels"
$preflightScript = Join-Path $repositoryRoot "scripts\postgresql\preflight-packout-projection-reconciliation.sql"
$applyScript = Join-Path $repositoryRoot "scripts\postgresql\apply-packout-projection-reconciliation-schema.sql"
$verifyScript = Join-Path $repositoryRoot "scripts\postgresql\verify-packout-projection-reconciliation.sql"

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Sql {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $Sql | & docker exec -i $containerName psql -X -U postgres -d $Database -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL command failed for disposable database $Database."
    }
}

function Invoke-SqlFile {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Path
    )

    Get-Content -LiteralPath $Path -Raw |
        & docker exec -i $containerName psql -X -U postgres -d $Database -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL script $Path failed for disposable database $Database."
    }
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $result = $Sql |
        & docker exec -i $containerName psql -X -U postgres -d $Database -v ON_ERROR_STOP=1 -At
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL scalar query failed for disposable database $Database."
    }

    return ($result | Select-Object -Last 1).Trim()
}

function New-DisposableDatabase {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ($Name -notmatch "^cropqc_packout_test_") {
        throw "Refusing to create non-disposable database '$Name'."
    }

    Invoke-Docker -Arguments @("exec", $containerName, "createdb", "-U", "postgres", $Name)
}

function Invoke-EfUpdate {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [string]$Target
    )

    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:DATABASE_PROVIDER = "PostgreSql"
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        $arguments = @(
            "ef", "database", "update"
        )
        if (-not [string]::IsNullOrWhiteSpace($Target)) {
            $arguments += $Target
        }
        $arguments += @(
            "--project", "src\CropQc.Data\CropQc.Data.csproj",
            "--startup-project", "src\CropQc.Data\CropQc.Data.csproj",
            "--no-build"
        )

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "EF migration update failed for disposable database $Database."
        }
    }
    finally {
        $env:DATABASE_PROVIDER = $priorProvider
        $env:ConnectionStrings__CropQc = $priorConnection
    }
}

function Invoke-SchemaGate {
    param([Parameter(Mandatory = $true)][string]$Database)

    $priorEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:ASPNETCORE_ENVIRONMENT = "Production"
        $env:DATABASE_PROVIDER = "PostgreSql"
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        & dotnet "src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll" `
            "--verify-schema=20260729165910_AddPackoutProjectionReconciliation"
        if ($LASTEXITCODE -ne 0) {
            throw "Application schema deployment gate failed for disposable database $Database."
        }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $priorEnvironment
        $env:DATABASE_PROVIDER = $priorProvider
        $env:ConnectionStrings__CropQc = $priorConnection
    }
}

function Add-RepresentativeSamples {
    param([Parameter(Mandatory = $true)][string]$Database)

    Invoke-Sql $Database @'
insert into "QcSamples" (
    "Id", "ReceiptId", "SampleTypeId", "SampleSequenceNumber", "Status",
    "StarchStatus", "PhotoStatus", "EmailStatus", "ActualSampleSize",
    "SampleTakenAt", "CreatedAt", "UpdatedAt",
    "FieldSampleAutosaveVersion")
values
    (9100000001, null, 5, 1, 'Draft', 'Not Started', 'Not Required', 'Not Applicable',
     1, now(), now(), now(), 0),
    (9100000002, null, 5, 1, 'Draft', 'Not Started', 'Not Required', 'Not Applicable',
     1, now(), now(), now(), 0);

insert into "QcFruitReadings" (
    "Id", "QcSampleId", "RowNumber", "SizeStatus", "DefectsInspected",
    "FieldVersion", "IsCompleted", "CreatedAt")
values
    (9100000001, 9100000001, 1, 'Not Entered', true, 0, false, now()),
    (9100000002, 9100000002, 1, 'Not Entered', true, 0, false, now());

insert into "QcFruitDefects" ("Id", "QcFruitReadingId", "DefectTypeId", "Notes")
values (9100000001, 9100000002, 1, 'Disposable migration test');
'@
}

function Add-ProductionCompatibilityTables {
    param([Parameter(Mandatory = $true)][string]$Database)

    # These tables predate the EF migration chain. Production already has them,
    # so checkpoint tests reproduce that real baseline before applying the
    # packout-only compatibility package.
    Invoke-Sql $Database @'
create table if not exists "RoomInventoryAdjustments" (
    "Id" bigint generated by default as identity,
    "ReceiptId" bigint null,
    "CropYear" integer null,
    "RoomDepletionId" bigint null,
    "WarehouseId" integer not null,
    "RoomId" integer not null,
    "GrowerLotId" integer null,
    "FruitProfileId" integer null,
    "GrowerName" character varying(200) not null,
    "LotNumber" character varying(100) not null,
    "PoolStart" character varying(20) null,
    "VarietyCode" character varying(50) null,
    "OldBinCount" integer null,
    "ChangeAmount" integer not null,
    "NewBinCount" integer not null,
    "AdjustmentType" character varying(50) not null,
    "Source" character varying(150) null,
    "SourceRoomCode" character varying(100) null,
    "SourceSubLocation" character varying(100) null,
    "InventoryStatus" character varying(100) null,
    "Reason" character varying(500) null,
    "Notes" character varying(1000) null,
    "AdjustmentAt" timestamp with time zone not null,
    "CreatedByUserId" integer null,
    "CreatedAt" timestamp with time zone not null,
    constraint "PK_RoomInventoryAdjustments" primary key ("Id"),
    constraint "FK_RoomInventoryAdjustments_Receipts_ReceiptId"
        foreign key ("ReceiptId") references "Receipts" ("Id") on delete set null,
    constraint "FK_RoomInventoryAdjustments_Warehouses_WarehouseId"
        foreign key ("WarehouseId") references "Warehouses" ("Id") on delete restrict,
    constraint "FK_RoomInventoryAdjustments_Rooms_RoomId"
        foreign key ("RoomId") references "Rooms" ("Id") on delete restrict,
    constraint "FK_RoomInventoryAdjustments_FruitProfiles_FruitProfileId"
        foreign key ("FruitProfileId") references "FruitProfiles" ("Id") on delete set null,
    constraint "FK_RoomInventoryAdjustments_Users_CreatedByUserId"
        foreign key ("CreatedByUserId") references "Users" ("Id") on delete set null
);
create index if not exists "IX_RoomInventoryAdjustments_RoomId_AdjustmentAt"
    on "RoomInventoryAdjustments" ("RoomId", "AdjustmentAt");
create index if not exists "IX_RoomInventoryAdjustments_ReceiptId_AdjustmentAt"
    on "RoomInventoryAdjustments" ("ReceiptId", "AdjustmentAt");

create table if not exists "BinsRunEntries" (
    "Id" bigint generated by default as identity,
    "ReceiptId" bigint null,
    "SourceInventoryAdjustmentId" bigint null,
    "InventoryAdjustmentId" bigint not null,
    "WarehouseId" integer not null,
    "RoomId" integer not null,
    "GrowerLotId" integer null,
    "FruitProfileId" integer null,
    "GrowerName" character varying(200) not null,
    "LotNumber" character varying(100) not null,
    "PoolStart" character varying(20) null,
    "VarietyCode" character varying(50) null,
    "InventoryStatus" character varying(100) null,
    "PreviousAvailableBins" integer not null,
    "BinsRun" integer not null,
    "NewAvailableBins" integer not null,
    "Notes" character varying(1000) null,
    "RunAt" timestamp with time zone not null,
    "CreatedByUserId" integer null,
    "CreatedAt" timestamp with time zone not null,
    "UpdatedAt" timestamp with time zone null,
    "IsReversed" boolean not null default false,
    "ReversedAt" timestamp with time zone null,
    "ReversedByUserId" integer null,
    "ReverseReason" character varying(1000) null,
    constraint "PK_BinsRunEntries" primary key ("Id"),
    constraint "FK_BinsRunEntries_Receipts_ReceiptId"
        foreign key ("ReceiptId") references "Receipts" ("Id") on delete set null,
    constraint "FK_BinsRunEntries_SourceInventoryAdjustmentId"
        foreign key ("SourceInventoryAdjustmentId") references "RoomInventoryAdjustments" ("Id") on delete set null,
    constraint "FK_BinsRunEntries_InventoryAdjustmentId"
        foreign key ("InventoryAdjustmentId") references "RoomInventoryAdjustments" ("Id") on delete restrict,
    constraint "FK_BinsRunEntries_Warehouses_WarehouseId"
        foreign key ("WarehouseId") references "Warehouses" ("Id") on delete restrict,
    constraint "FK_BinsRunEntries_Rooms_RoomId"
        foreign key ("RoomId") references "Rooms" ("Id") on delete restrict,
    constraint "FK_BinsRunEntries_FruitProfiles_FruitProfileId"
        foreign key ("FruitProfileId") references "FruitProfiles" ("Id") on delete set null,
    constraint "FK_BinsRunEntries_Users_CreatedByUserId"
        foreign key ("CreatedByUserId") references "Users" ("Id") on delete set null,
    constraint "FK_BinsRunEntries_Users_ReversedByUserId"
        foreign key ("ReversedByUserId") references "Users" ("Id") on delete set null
);
create index if not exists "IX_BinsRunEntries_RoomId_RunAt"
    on "BinsRunEntries" ("RoomId", "RunAt");
create index if not exists "IX_BinsRunEntries_ReceiptId_IsReversed"
    on "BinsRunEntries" ("ReceiptId", "IsReversed");
'@
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker is required for the disposable PostgreSQL compatibility tests."
}

Push-Location $repositoryRoot
try {
    Invoke-Docker -Arguments @(
        "run", "--rm", "-d",
        "--name", $containerName,
        "-e", "POSTGRES_PASSWORD=$password",
        "-p", "${HostPort}:5432",
        $image)

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) {
        throw "Disposable PostgreSQL container did not become ready."
    }

    # Fresh EF schema, followed by the compatibility package. This proves that
    # the package is safe when EF already created every schema object.
    $fresh = "cropqc_packout_test_fresh"
    New-DisposableDatabase $fresh
    Invoke-EfUpdate $fresh
    Invoke-SqlFile $fresh $applyScript
    Invoke-SqlFile $fresh $verifyScript

    # Checkpoint upgrade with representative historical samples and defects.
    $checkpoint = "cropqc_packout_test_checkpoint"
    New-DisposableDatabase $checkpoint
    Invoke-EfUpdate $checkpoint $checkpointMigration
    Add-ProductionCompatibilityTables $checkpoint
    Add-RepresentativeSamples $checkpoint
    $preservationBefore = Invoke-SqlScalar $checkpoint @'
select concat(
    (select count(*) from "QcSamples"), '|',
    (select count(*) from "QcFruitReadings"), '|',
    (select count(*) from "QcFruitDefects"))
'@
    Invoke-SqlFile $checkpoint $preflightScript
    Invoke-SqlFile $checkpoint $applyScript
    Invoke-SqlFile $checkpoint $verifyScript
    Invoke-SchemaGate $checkpoint
    $statusResult = Invoke-SqlScalar $checkpoint @'
select string_agg("Id"::text || ':' || "DefectInspectionStatus", ',' order by "Id")
from "QcSamples"
where "Id" in (9100000001, 9100000002)
'@
    if ($statusResult -ne "9100000001:No defects found,9100000002:Defects found") {
        throw "Defect-status backfill result was unexpected: $statusResult"
    }
    $preservationAfter = Invoke-SqlScalar $checkpoint @'
select concat(
    (select count(*) from "QcSamples"), '|',
    (select count(*) from "QcFruitReadings"), '|',
    (select count(*) from "QcFruitDefects"))
'@
    if ($preservationAfter -ne $preservationBefore) {
        throw "Historical sample/readings/defect counts changed: $preservationBefore -> $preservationAfter"
    }

    # Repeated execution must be a no-op for data and succeed.
    Invoke-SqlFile $checkpoint $applyScript
    Invoke-SqlFile $checkpoint $verifyScript
    $preservationRepeated = Invoke-SqlScalar $checkpoint @'
select concat(
    (select count(*) from "QcSamples"), '|',
    (select count(*) from "QcFruitReadings"), '|',
    (select count(*) from "QcFruitDefects"))
'@
    if ($preservationRepeated -ne $preservationBefore) {
        throw "Repeated execution changed historical counts."
    }

    # Common partial state: one table, one column, and one index are absent.
    $partial = "cropqc_packout_test_partial"
    New-DisposableDatabase $partial
    Invoke-EfUpdate $partial $checkpointMigration
    Add-ProductionCompatibilityTables $partial
    Invoke-SqlFile $partial $applyScript
    Invoke-Sql $partial @'
alter table "PackoutEmailAttempts" drop constraint if exists "FK_PackoutEmailAttempts_PackoutRuns_PackoutRunId";
drop table "PackoutEmailAttempts";
alter table "BinsRunEntries" drop column "ReconciledAt";
drop index "IX_PackoutRuns_RunProjectionId_Status";
'@
    Invoke-SqlFile $partial $applyScript
    Invoke-SqlFile $partial $verifyScript

    # EnsureCreated-style history gap: logical checkpoint objects exist but the
    # migration-history table is absent.
    $historyGap = "cropqc_packout_test_history_gap"
    New-DisposableDatabase $historyGap
    Invoke-EfUpdate $historyGap $checkpointMigration
    Add-ProductionCompatibilityTables $historyGap
    Invoke-Sql $historyGap 'drop table "__EFMigrationsHistory";'
    Invoke-SqlFile $historyGap $preflightScript
    Invoke-SqlFile $historyGap $applyScript
    Invoke-SqlFile $historyGap $verifyScript
    Invoke-SchemaGate $historyGap

    # Unsafe duplicate input must stop the transaction before any base column is
    # committed.
    $invalid = "cropqc_packout_test_invalid"
    New-DisposableDatabase $invalid
    Invoke-EfUpdate $invalid $checkpointMigration
    Add-ProductionCompatibilityTables $invalid
    Invoke-Sql $invalid @'
create table "PackCodeDefinitions" (
    "Id" integer generated by default as identity primary key,
    "Code" character varying(75) not null,
    "NormalizedCode" character varying(75) not null,
    "DisplayName" character varying(150) not null,
    "ProductCategory" character varying(50) not null,
    "IsActive" boolean not null,
    "CreatedAt" timestamp with time zone not null
);
insert into "PackCodeDefinitions" (
    "Code", "NormalizedCode", "DisplayName", "ProductCategory", "IsActive", "CreatedAt")
values
    ('DUP-A', 'DUP', 'Duplicate A', 'Packed product', true, now()),
    ('DUP-B', 'DUP', 'Duplicate B', 'Packed product', true, now());
'@
    $priorErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        Get-Content -LiteralPath $applyScript -Raw |
            & docker exec -i $containerName psql -X -U postgres -d $invalid -v ON_ERROR_STOP=1 *> $null
    }
    finally {
        $ErrorActionPreference = $priorErrorAction
    }
    if ($LASTEXITCODE -eq 0) {
        throw "Unsafe duplicate preflight data unexpectedly succeeded."
    }
    $rolledBack = Invoke-SqlScalar $invalid @'
select not exists (
    select 1
    from information_schema.columns
    where table_schema = current_schema()
      and table_name = 'QcSamples'
      and column_name = 'DefectInspectionStatus')
'@
    if ($rolledBack -ne "t") {
        throw "Invalid preflight data did not roll back the transaction."
    }

    Write-Output "Fresh PostgreSQL migration/package: PASS"
    Write-Output "Checkpoint upgrade/backfill/preservation: PASS ($statusResult)"
    Write-Output "Repeated execution/idempotency: PASS"
    Write-Output "Partial-update recovery: PASS"
    Write-Output "EnsureCreated-style history gap: PASS"
    Write-Output "Application pre-deploy schema gate: PASS"
    Write-Output "Unsafe-data rollback: PASS"
}
finally {
    Pop-Location
    if (-not $KeepContainer) {
        & docker container inspect $containerName *> $null
        if ($LASTEXITCODE -eq 0) {
            & docker rm -f $containerName *> $null
        }
    }
}
