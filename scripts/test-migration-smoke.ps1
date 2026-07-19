param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("SqlServer", "PostgreSql")]
    [string]$Provider,

    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [switch]$ValidateFieldSamples
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

if (-not $ValidateFieldSamples) {
    return
}

if ($Provider -ne "PostgreSql") {
    Write-Warning "Field Sample smoke validation currently runs direct assertions for PostgreSQL only. SQL Server migration apply was completed above."
    return
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) {
    $candidate = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
    if (Test-Path $candidate) {
        $psqlPath = $candidate
    } else {
        throw "psql was not found on PATH or at $candidate."
    }
} else {
    $psqlPath = $psql.Source
}

function Get-ConnectionValue([string]$Name) {
    if ($ConnectionString -match "(?i)(^|;)$Name=([^;]*)") {
        return $Matches[2]
    }

    return $null
}

$hostName = Get-ConnectionValue "Host"
$port = Get-ConnectionValue "Port"
$userName = Get-ConnectionValue "Username"
$password = Get-ConnectionValue "Password"

if ([string]::IsNullOrWhiteSpace($hostName)) { $hostName = "127.0.0.1" }
if ([string]::IsNullOrWhiteSpace($port)) { $port = "5432" }
if ([string]::IsNullOrWhiteSpace($userName)) { $userName = "postgres" }

$sql = @'
do $$
declare
    receipt_id bigint;
    sample_id bigint := 9900000001;
    block_id int;
    other_block_id int;
    duplicate_blocked boolean := false;
begin
    if not exists (select 1 from "SampleTypes" where "Id" = 5 and "Name" = 'Field Sample') then
        raise exception 'Field Sample sample type seed is missing';
    end if;

    if not exists (
        select 1
        from information_schema.columns
        where table_name = 'QcSamples'
          and column_name = 'ReceiptId'
          and is_nullable = 'YES'
    ) then
        raise exception 'QcSamples.ReceiptId is not nullable';
    end if;

    if not exists (select 1 from information_schema.tables where table_name = 'CanonicalOrchardBlocks') then
        raise exception 'CanonicalOrchardBlocks table is missing';
    end if;

    if not exists (select 1 from information_schema.tables where table_name = 'OrchardBlockAliases') then
        raise exception 'OrchardBlockAliases table is missing';
    end if;

    if not exists (
        select 1
        from pg_indexes
        where tablename = 'QcSamples'
          and indexname = 'IX_QcSamples_ReceiptId_SampleSequenceNumber'
          and indexdef like '%WHERE ("ReceiptId" IS NOT NULL)%'
    ) then
        raise exception 'Receipt-backed filtered unique index is missing or unfiltered';
    end if;

    insert into "Warehouses" ("Id", "Code", "Name", "IsActive")
    values (990001, 'SMOKE', 'Smoke Warehouse', true)
    on conflict ("Id") do nothing;

    insert into "Rooms" ("Id", "WarehouseId", "Code", "Name", "CapacityBins", "IsActive")
    values (990001, 990001, 'SMOKE-ROOM', 'Smoke Room', 1000, true)
    on conflict ("Id") do nothing;

    insert into "FruitProfiles" ("Id", "Name", "Description", "VarietyCode", "FruitType", "ProductionType", "IsOrganic", "IsActive")
    values (990001, 'Smoke Gala', 'Smoke test fruit profile', 'SMOKEGALA', 'Apple', 'Conventional', false, true)
    on conflict ("Id") do nothing;

    receipt_id := 9900000001;

    insert into "Receipts" (
        "Id", "CropYear", "ReceivedAt", "CompuTechReceiptId", "WarehouseId", "RoomId", "FruitProfileId",
        "GrowerName", "LotCode", "BinCount", "CreatedAt", "UpdatedAt"
    )
    values (
        receipt_id, 2026, now(), 'FIELD-SMOKE-001', 990001, 990001, 990001,
        'Smoke Grower', 'SMOKELOT', 42, now(), now()
    );

    insert into "QcSamples" (
        "Id", "ReceiptId", "SampleTypeId", "SampleSequenceNumber", "Status", "StarchStatus", "PhotoStatus", "EmailStatus",
        "ActualSampleSize", "SampleTakenAt", "CreatedAt", "UpdatedAt"
    )
    values
        (sample_id, receipt_id, 1, 1, 'Draft', 'Not Started', 'Not Required', 'Not Sent', 25, now(), now(), now()),
        (sample_id + 1, receipt_id, 2, 2, 'Draft', 'Not Started', 'Not Required', 'Not Applicable', 25, now(), now(), now()),
        (sample_id + 2, receipt_id, 3, 3, 'Draft', 'Not Started', 'Not Required', 'Not Applicable', 25, now(), now(), now());

    begin
        insert into "QcSamples" (
            "Id", "ReceiptId", "SampleTypeId", "SampleSequenceNumber", "Status", "StarchStatus", "PhotoStatus", "EmailStatus",
            "ActualSampleSize", "SampleTakenAt", "CreatedAt", "UpdatedAt"
        )
        values (sample_id + 3, receipt_id, 1, 1, 'Draft', 'Not Started', 'Not Required', 'Not Sent', 25, now(), now(), now());
    exception when unique_violation then
        duplicate_blocked := true;
    end;

    if not duplicate_blocked then
        raise exception 'Receipt-backed duplicate sample sequence was not blocked';
    end if;

    block_id := 990001;
    other_block_id := 990002;

    insert into "CanonicalOrchardBlocks" (
        "Id", "OrchardName", "CanonicalBlockName", "NormalizedOrchardKey", "NormalizedBlockKey",
        "IsActive", "CreatedAt", "UpdatedAt"
    )
    values (block_id, 'Smoke Orchard', 'North 1', 'SMOKE_ORCHARD', 'NORTH_1', true, now(), now());

    insert into "CanonicalOrchardBlocks" (
        "Id", "OrchardName", "CanonicalBlockName", "NormalizedOrchardKey", "NormalizedBlockKey",
        "IsActive", "CreatedAt", "UpdatedAt"
    )
    values (other_block_id, 'Other Smoke Orchard', 'North 1', 'OTHER_SMOKE_ORCHARD', 'NORTH_1', true, now(), now());

    insert into "OrchardBlockAliases" (
        "Id", "CanonicalOrchardBlockId", "AliasName", "NormalizedAliasKey", "IsActive", "CreatedAt", "UpdatedAt"
    )
    values (990001, block_id, 'N 1', 'N_1', true, now(), now());

    insert into "QcSamples" (
        "Id", "ReceiptId", "SampleTypeId", "SampleSequenceNumber", "Status", "StarchStatus", "PhotoStatus", "EmailStatus",
        "ActualSampleSize", "SampleTakenAt", "CreatedAt", "UpdatedAt",
        "CanonicalOrchardBlockId", "FieldSampleFruitProfileId", "FieldSampleGrowerName", "FieldSampleGrowerNumber",
        "FieldSampleOriginalBlockName", "FieldSampleBlockResolution"
    )
    values
        (sample_id + 4, null, 5, 1, 'Draft', 'Not Started', 'Not Required', 'Not Applicable', 10, now(), now(), now(), block_id, 990001, 'Smoke Grower', '9001', 'North 1', 'Exact'),
        (sample_id + 5, null, 5, 1, 'Draft', 'Not Started', 'Not Required', 'Not Applicable', 10, now(), now(), now(), block_id, 990001, 'Smoke Grower', '9001', 'N 1', 'Alias'),
        (sample_id + 6, null, 5, 1, 'Draft', 'Not Started', 'Not Required', 'Not Applicable', 10, now(), now(), now(), other_block_id, 990001, 'Other Grower', '9002', 'North 1', 'Exact');

    if (select count(*) from "QcSamples" where "SampleTypeId" = 5 and "ReceiptId" is null) < 3 then
        raise exception 'Receiptless Field Sample insert validation failed';
    end if;
end $$;
'@

$tempSql = Join-Path $env:TEMP ("cropqc-field-sample-smoke-" + [Guid]::NewGuid().ToString("N") + ".sql")
Set-Content -LiteralPath $tempSql -Value $sql
try {
    if ($null -ne $password) {
        $env:PGPASSWORD = $password
    }

    & $psqlPath -h $hostName -p $port -U $userName -d $databaseName -v ON_ERROR_STOP=1 -f $tempSql
    if ($LASTEXITCODE -ne 0) {
        throw "Field Sample PostgreSQL smoke validation failed with exit code $LASTEXITCODE."
    }
} finally {
    Remove-Item -LiteralPath $tempSql -ErrorAction SilentlyContinue
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
