[CmdletBinding()]
param(
    [int]$HostPort = 55442,
    [string]$Image = 'postgres:18-alpine'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-ebs-season-opening-$([Guid]::NewGuid().ToString('N').Substring(0, 10))"
$databaseName = 'cropqc_ebs_season_opening_disposable'
$password = [Guid]::NewGuid().ToString('N')
$operatorEmail = 'disposable.operator@example.invalid'

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed: docker $($Arguments -join ' ')"
    }
}

function Invoke-Sql {
    param(
        [Parameter(Mandatory)][string]$Sql,
        [string[]]$Variables = @(),
        [switch]$Quiet
    )

    $arguments = @('exec', '-i', $containerName, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', $databaseName)
    foreach ($variable in $Variables) {
        $arguments += @('-v', $variable)
    }
    if ($Quiet) {
        $arguments += @('-q', '-t', '-A')
    }
    $Sql | & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Disposable PostgreSQL SQL execution failed.'
    }
}

function Invoke-SqlFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$Variables = @()
    )

    Invoke-Sql -Sql (Get-Content -LiteralPath $Path -Raw) -Variables $Variables
}

function Read-Scalar {
    param([Parameter(Mandatory)][string]$Sql)
    $result = Invoke-Sql -Sql $Sql -Quiet
    return (($result | Out-String).Trim())
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)][string]$Expected,
        [Parameter(Mandatory)][string]$Actual,
        [Parameter(Mandatory)][string]$Message
    )
    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected'; found '$Actual'."
    }
}

$schemaAndFixture = @'
create table "Warehouses" (
    "Id" integer primary key,
    "Code" text not null,
    "Name" text null
);
create table "Rooms" (
    "Id" integer primary key,
    "WarehouseId" integer not null,
    "Code" text null,
    "Name" text null,
    "CropQcRoomName" text null,
    "CompuTechRoomCode" text null,
    "DisplayName" text null
);
create table "FruitProfiles" (
    "Id" integer primary key,
    "VarietyCode" text not null
);
create table "GrowerLots" (
    "Id" bigint primary key,
    "LotNumber" text null
);
create table "Receipts" (
    "Id" bigint primary key,
    "WarehouseId" integer not null,
    "RoomId" integer null,
    "GrowerLotId" bigint null,
    "FruitProfileId" integer null,
    "CropYear" integer not null,
    "BinCount" integer not null,
    "ReceivedAt" timestamptz not null,
    "IsDeleted" boolean not null default false,
    "IsTestData" boolean not null default false,
    "ReceiptNumber" text null
);
create table "Users" (
    "Id" integer primary key,
    "Email" text not null,
    "IsActive" boolean not null
);
create table "RoomDepletions" (
    "Id" bigint primary key,
    "RoomId" integer not null
);
create table "RoomTransfers" (
    "Id" bigint primary key,
    "SourceRoomId" integer not null,
    "DestinationRoomId" integer not null
);
create table "RoomInventoryAdjustments" (
    "Id" bigint primary key,
    "WarehouseId" integer not null,
    "RoomId" integer not null,
    "ReceiptId" bigint null,
    "GrowerLotId" bigint null,
    "FruitProfileId" integer null,
    "CropYear" integer null,
    "LotNumber" text null,
    "VarietyCode" text null,
    "ChangeAmount" integer not null,
    "OldBinCount" integer null,
    "NewBinCount" integer not null,
    "AdjustmentType" text not null,
    "Source" text null,
    "Reason" text null,
    "Notes" text null,
    "RoomDepletionId" bigint null,
    "RoomTransferId" bigint null,
    "ActualRunId" bigint null,
    "CreatedAt" timestamptz not null,
    "AdjustmentAt" timestamptz not null,
    "CreatedByUserId" integer null,
    "InventoryInvariantVersion" integer not null default 0,
    "InventoryOperationKey" text null
);
create table "BinsRunEntries" (
    "Id" bigint primary key,
    "RoomId" integer not null,
    "GrowerLotId" bigint null,
    "InventoryAdjustmentId" bigint null,
    "SourceInventoryAdjustmentId" bigint null
);
create table "AuditLogs" (
    "Id" bigint primary key,
    "UserId" integer null,
    "Action" text not null,
    "EntityName" text not null,
    "EntityKey" text null,
    "BeforeValuesJson" text null,
    "AfterValuesJson" text null,
    "SourceApplication" text null,
    "CreatedAt" timestamptz not null
);

insert into "Warehouses" values (1, 'EBS', 'EBS'), (2, 'WP', 'Windy Point'), (3, 'OTHER', 'Other');
insert into "Rooms" values
    (17, 1, 'EVANCA07', 'Evans Street 7', 'Evans Street 7', 'EVANCA07', 'Evans Street 7'),
    (30, 1, 'BM-4', 'Bluemountain 4', 'Bluemountain 4', null, 'Bluemountain 4'),
    (27, 1, 'BM-1', 'BM-1', 'BM-1', null, 'BM-1'),
    (32, 1, 'BM-6', 'BM-6', 'BM-6', null, 'BM-6'),
    (11, 1, 'EVANS-01', 'Evans Street 1', 'Evans Street 1', null, 'Evans Street 1'),
    (22, 1, 'EVANS-12', 'Evans Street 12', 'Evans Street 12', null, 'Evans Street 12'),
    (15, 1, 'EVANS-5', 'Evans Street 5', 'Evans Street 5', null, 'Evans Street 5'),
    (10, 1, 'LAMB-17', 'Lamb Street 17', 'Lamb Street 17', null, 'Lamb Street 17'),
    (7, 1, 'LAMB-14', 'Lamb Street 14', 'Lamb Street 14', null, 'Lamb Street 14'),
    (100, 2, 'WP-1', 'WP 1', 'WP 1', null, 'WP 1'),
    (101, 3, 'OTHER-1', 'Other 1', 'Other 1', null, 'Other 1');
insert into "FruitProfiles" values (1, 'GALA'), (14, 'RED');
insert into "GrowerLots" values (104, '9290'), (105, '9291'), (251, '9660'), (87, 'LS020');
insert into "Users" values (1, 'disposable.operator@example.invalid', true);

insert into "Receipts" values
    (26, 1, 30, 251, 14, 2025, 34, '2026-06-15 19:51:00+00', false, false, 'LS018'),
    (28, 1, 11, 87, 14, 2025, 1039, '2026-06-18 16:00:00+00', true, false, 'LS020'),
    (99, 1, 17, 104, 1, 2026, 44, '2026-07-28 15:36:00+00', false, false, '108833');
insert into "Receipts"
select 99 + sequence_number, 1, 17,
       case when sequence_number % 2 = 0 then 104 else 105 end,
       1, 2026, 10, '2026-07-28 16:00:00+00'::timestamptz + sequence_number * interval '1 hour',
       false, false, 'E7-' || sequence_number::text
from generate_series(1, 11) sequence_number;

insert into "RoomInventoryAdjustments" (
    "Id", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "CropYear", "LotNumber", "VarietyCode",
    "ChangeAmount", "OldBinCount", "NewBinCount", "AdjustmentType", "Source", "Reason", "Notes",
    "CreatedAt", "AdjustmentAt", "CreatedByUserId")
select candidate_id, 1, 27, 251, 14, 2025, '9660', 'RED',
       0, null, 0, 'StartingInventoryImport', 'Fixture history', 'Fixture history', 'Fixture history',
       '2026-06-20 00:00:00+00', '2026-06-20 00:00:00+00', 1
from generate_series(1, 80) candidate_id
where candidate_id <> 52;

update "RoomInventoryAdjustments" set "RoomId" = 30, "ReceiptId" = 26, "GrowerLotId" = 251,
    "ChangeAmount" = 34, "OldBinCount" = null, "NewBinCount" = 34,
    "AdjustmentType" = 'ReceiptAdd', "Source" = 'Receiving inventory added'
where "Id" = 1;
update "RoomInventoryAdjustments" set "RoomId" = 11, "ReceiptId" = 28, "GrowerLotId" = 87,
    "ChangeAmount" = 1039, "OldBinCount" = null, "NewBinCount" = 1039,
    "AdjustmentType" = 'ReceiptAdd', "Source" = 'Receiving inventory added'
where "Id" = 8;
update "RoomInventoryAdjustments" set "RoomId" = 7, "ChangeAmount" = 0, "OldBinCount" = 144, "NewBinCount" = 144,
    "AdjustmentType" = 'ReceiptEdit', "Source" = 'Receipt source row' where "Id" in (22, 23);
update "RoomInventoryAdjustments" set "RoomId" = 7, "ChangeAmount" = 0, "OldBinCount" = 101, "NewBinCount" = 101,
    "AdjustmentType" = 'ReceiptEdit', "Source" = 'Receipt source row' where "Id" in (25, 26);
update "RoomInventoryAdjustments" set "RoomId" = 7, "ChangeAmount" = -144, "OldBinCount" = 144, "NewBinCount" = 0,
    "AdjustmentType" = 'BinsRun', "Source" = 'Bins Run deduction' where "Id" in (76, 77);
update "RoomInventoryAdjustments" set "RoomId" = 7, "ChangeAmount" = -101, "OldBinCount" = 101, "NewBinCount" = 0,
    "AdjustmentType" = 'BinsRun', "Source" = 'Bins Run deduction' where "Id" in (78, 79);

insert into "BinsRunEntries" values
    (23, 7, 251, 76, 22), (24, 7, 251, 77, 23),
    (25, 7, 251, 78, 25), (26, 7, 251, 79, 26);

insert into "RoomInventoryAdjustments" (
    "Id", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "CropYear", "LotNumber", "VarietyCode",
    "ChangeAmount", "OldBinCount", "NewBinCount", "AdjustmentType", "Source", "CreatedAt", "AdjustmentAt")
select 1000 + sequence_number, 1, 17,
       case when sequence_number % 2 = 0 then 104 else 105 end, 1, 2026, '9290', 'GALA',
       case when sequence_number = 1 then 388 else 0 end, 0,
       case when sequence_number = 1 then 388 else 0 end,
       'ReceiptAdd', 'Protected Evans 7 fixture', '2026-07-28 16:00:00+00', '2026-07-28 16:00:00+00'
from generate_series(1, 10) sequence_number;
insert into "RoomInventoryAdjustments" (
    "Id", "WarehouseId", "RoomId", "CropYear", "LotNumber", "VarietyCode",
    "ChangeAmount", "OldBinCount", "NewBinCount", "AdjustmentType", "Source", "CreatedAt", "AdjustmentAt")
values
    (2001, 2, 100, 2026, 'WPLOT', 'GALA', 777, 0, 777, 'ReceiptAdd', 'Protected WP', '2026-07-01', '2026-07-01'),
    (3001, 3, 101, 2026, 'OTHERLOT', 'GALA', 88, 0, 88, 'ReceiptAdd', 'Protected other', '2026-07-01', '2026-07-01');
'@

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is required for the disposable PostgreSQL correction test.'
}

$preflight = Join-Path $repositoryRoot 'scripts/postgresql/preflight-ebs-2026-season-opening-correction.sql'
$apply = Join-Path $repositoryRoot 'scripts/postgresql/apply-ebs-2026-season-opening-correction.sql'
$verify = Join-Path $repositoryRoot 'scripts/postgresql/verify-ebs-2026-season-opening-correction.sql'

Push-Location $repositoryRoot
try {
    Invoke-Docker -Arguments @('run', '--rm', '-d', '--name', $containerName,
        '-e', "POSTGRES_PASSWORD=$password", '-p', "${HostPort}:5432", $Image)

    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $priorErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & docker exec $containerName psql -X -U postgres -d postgres -c 'select 1' *> $null
        $readyExitCode = $LASTEXITCODE
        $ErrorActionPreference = $priorErrorPreference
        if ($readyExitCode -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) {
        throw 'Disposable PostgreSQL container did not become ready.'
    }
    Start-Sleep -Milliseconds 500

    Invoke-Docker -Arguments @('exec', $containerName, 'createdb', '-U', 'postgres', $databaseName)
    Invoke-Sql -Sql $schemaAndFixture

    $protectedBefore = Read-Scalar @'
select concat(
  md5(string_agg(to_jsonb(a)::text, '' order by a."Id")), '|',
  (select count(*) from "Receipts" where "RoomId" = 17), '|',
  (select count(distinct "GrowerLotId") from "Receipts" where "RoomId" = 17), '|',
  (select sum("ChangeAmount") from "RoomInventoryAdjustments" where "RoomId" = 17), '|',
  (select md5(string_agg(to_jsonb(w)::text, '' order by w."Id")) from "RoomInventoryAdjustments" w where w."WarehouseId" = 2))
from "RoomInventoryAdjustments" a where a."RoomId" = 17
'@

    Invoke-SqlFile -Path $preflight
    Invoke-SqlFile -Path $apply -Variables @(
        'correction_authorization=APPLY_EBS_2026_SEASON_OPENING_CORRECTION',
        'expected_boundary_receipt_id=99',
        "operator_email=$operatorEmail")
    Invoke-SqlFile -Path $verify

    $afterFirst = Read-Scalar @'
select concat(
  (select coalesce(sum("ChangeAmount"), 0) from "RoomInventoryAdjustments" where "WarehouseId" = 1 and "RoomId" <> 17), '|',
  (select count(*) from "AuditLogs" where "Action" = 'ApplyEbs2026SeasonOpeningCorrection'), '|',
  (select string_agg("Id"::text || ':' || "ChangeAmount"::text, ',' order by "Id") from "RoomInventoryAdjustments" where "Id" in (1,8,22,23,25,26)))
'@
    Assert-Equal -Expected '0|1|1:0,8:0,22:144,23:144,25:101,26:101' -Actual $afterFirst -Message 'First apply did not produce the reviewed state.'

    Invoke-SqlFile -Path $apply -Variables @(
        'correction_authorization=APPLY_EBS_2026_SEASON_OPENING_CORRECTION',
        'expected_boundary_receipt_id=99',
        "operator_email=$operatorEmail")
    Invoke-SqlFile -Path $verify

    $protectedAfter = Read-Scalar @'
select concat(
  md5(string_agg(to_jsonb(a)::text, '' order by a."Id")), '|',
  (select count(*) from "Receipts" where "RoomId" = 17), '|',
  (select count(distinct "GrowerLotId") from "Receipts" where "RoomId" = 17), '|',
  (select sum("ChangeAmount") from "RoomInventoryAdjustments" where "RoomId" = 17), '|',
  (select md5(string_agg(to_jsonb(w)::text, '' order by w."Id")) from "RoomInventoryAdjustments" w where w."WarehouseId" = 2))
from "RoomInventoryAdjustments" a where a."RoomId" = 17
'@
    Assert-Equal -Expected $protectedBefore -Actual $protectedAfter -Message 'Evans 7 or WP changed.'
    Assert-Equal -Expected '1' -Actual (Read-Scalar 'select count(*) from "AuditLogs" where "Action" = ''ApplyEbs2026SeasonOpeningCorrection'';') -Message 'Repeated apply was not idempotent.'

    Write-Host "Disposable PostgreSQL correction test passed. Database: $databaseName; container: $containerName"
    Write-Host 'Verified: dynamic boundary, 79-row fingerprint, six-row correction, Evans 7/WP preservation, zero final carry, and repeated-apply idempotency.'
}
finally {
    Pop-Location
    & docker rm -f $containerName *> $null
}
