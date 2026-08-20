param([int]$HostPort = 55448, [switch]$KeepContainer)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-room-treatment-schema-$PID"
$password = 'cropqc-disposable-room-treatment-only'
$previousMigration = '20260817075807_AddEndOfDayFillWarehouseScope'
$expectedMigration = '20260819142656_AddTreatmentReportAttachments'
$containerScriptRoot = '/tmp/cropqc-room-treatment'

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function New-Database {
    param([string]$Name)
    if ($Name -notmatch '^cropqc_room_treatment_') { throw "Refusing non-disposable database '$Name'." }
    Invoke-Docker -Arguments @('exec', $containerName, 'createdb', '-U', 'postgres', $Name)
}

function Invoke-Sql {
    param([string]$Database, [string]$Sql)
    $Sql | & docker exec -i $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database
    if ($LASTEXITCODE -ne 0) { throw "SQL failed for $Database." }
}

function Invoke-SqlScalar {
    param([string]$Database, [string]$Sql)
    $result = $Sql | & docker exec -i $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database -At
    if ($LASTEXITCODE -ne 0) { throw "Scalar SQL failed for $Database." }
    return ($result | Select-Object -Last 1).Trim()
}

function Invoke-Script {
    param([string]$Database, [string]$Name)
    & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database -f "$containerScriptRoot/$Name"
    if ($LASTEXITCODE -ne 0) { throw "$Name failed for $Database." }
}

function Assert-ScriptFails {
    param([string]$Database, [string]$Name)
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database -f "$containerScriptRoot/$Name" *> $null
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
    if ($exitCode -eq 0) { throw "$Name unexpectedly passed for incompatible database $Database." }
}

function Invoke-EfUpdate {
    param([string]$Database, [string]$Target)
    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:DATABASE_PROVIDER = 'PostgreSql'
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        $arguments = @('ef', 'database', 'update')
        if ($Target) { $arguments += $Target }
        $arguments += @('--project', 'src\CropQc.Data\CropQc.Data.csproj', '--startup-project', 'src\CropQc.Data\CropQc.Data.csproj', '--no-build')
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "EF migration failed for $Database." }
    }
    finally {
        $env:DATABASE_PROVIDER = $priorProvider
        $env:ConnectionStrings__CropQc = $priorConnection
    }
}

function Invoke-Gate {
    param([string]$Database)
    $priorEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $env:DATABASE_PROVIDER = 'PostgreSql'
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        & dotnet 'src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll' "--verify-schema=$expectedMigration"
        if ($LASTEXITCODE -ne 0) { throw "502-object gate failed for $Database." }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $priorEnvironment
        $env:DATABASE_PROVIDER = $priorProvider
        $env:ConnectionStrings__CropQc = $priorConnection
    }
}

function Get-ObjectSignature {
    param([string]$Database)
    return Invoke-SqlScalar $Database @'
WITH target_tables AS (SELECT unnest(ARRAY['TreatmentChemicals','RoomTreatmentApplications','RoomTreatmentApplicationSources','TreatmentLineageSegments','TreatmentLineageSegmentApplications','TreatmentLineageMovements']) name), signatures AS (
  SELECT 'columns='||md5(string_agg(concat_ws('|',table_name,ordinal_position,column_name,data_type,coalesce(character_maximum_length::text,''),coalesce(numeric_precision::text,''),coalesce(numeric_scale::text,''),is_nullable,is_identity),';' ORDER BY table_name,ordinal_position)) value
  FROM information_schema.columns WHERE table_schema=current_schema() AND (table_name IN (SELECT name FROM target_tables) OR (table_name='BinsRunEntries' AND column_name LIKE 'Treatment%Snapshot') OR (table_name='ActualRunOverrideRequestLines' AND column_name='TreatmentSignature'))
  UNION ALL
  SELECT 'indexes='||md5(string_agg(pg_get_indexdef(i.indexrelid),';' ORDER BY t.relname,c.relname))
  FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=current_schema() AND t.relname IN (SELECT name FROM target_tables)
  UNION ALL
  SELECT 'constraints='||md5(string_agg(c.conname||'|'||c.contype::text||'|'||pg_get_constraintdef(c.oid),';' ORDER BY t.relname,c.conname))
  FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=current_schema() AND t.relname IN (SELECT name FROM target_tables) AND c.contype IN ('p','f','u','c','x')
  UNION ALL
  SELECT 'seed='||md5(string_agg(concat_ws('|',"Id","ProductName",coalesce("CommonName",''),"Crop","Volume","Unit","UnitPrice","Currency","IsActive"),';' ORDER BY "Id")) FROM "TreatmentChemicals")
SELECT string_agg(value,';' ORDER BY value) FROM signatures;
'@
}

function Get-TreatmentChemicalFingerprint {
    param([string]$Database)
    return Invoke-SqlScalar $Database @'
SELECT count(*)||'|'||md5(coalesce(string_agg(md5(to_jsonb(t)::text),',' ORDER BY md5(to_jsonb(t)::text)),''))
FROM "TreatmentChemicals" t;
'@
}

function Assert-InitialReviewedSeed {
    param([string]$Database)
    $state = Invoke-SqlScalar $Database @'
WITH expected(id,product,crop,price) AS (VALUES
  (1,'eFOG-160 PYR FOGGING','Apples',5.25::numeric),(2,'FOGGING EF 170,SB TBZ 99, EF80','Apples',5.67),(3,'FOGGING EF 180, TBZ 99, EF 80','Pears',9.58),(4,'eFOG-80 FDL FOGGING','Pears',5.25),(5,'FOGGING EF 170, EF 160','Apples',5.67),(6,'eFOG-180 FOGGING','Pears',4.95),(7,'FOGGING EF 170, EF 80','Apples',5.67),(8,'FOGGING EF 180, EF 160','Pears',9.27),(9,'FOGGING EF 170, SB TBZ 99','Apples',5.25),(10,'eFOG-170 DPA FOGGING','Apples',2.80)
), mismatches AS (
  SELECT 1 FROM expected e LEFT JOIN "TreatmentChemicals" c ON c."Id"=e.id
  WHERE (c."ProductName",c."CommonName",c."Crop",c."Volume",c."Unit",c."UnitPrice",c."Currency",c."IsActive",c."CreatedAt",c."CreatedByUserId",c."UpdatedAt",c."UpdatedByUserId")
    IS DISTINCT FROM (e.product,NULL::varchar,e.crop,1.00::numeric,'BIN'::varchar,e.price,'USD'::varchar,true,'2026-05-21T00:00:00Z'::timestamptz,NULL::integer,'2026-05-21T00:00:00Z'::timestamptz,NULL::integer)
)
SELECT CASE WHEN (SELECT count(*) FROM "TreatmentChemicals")=10 AND NOT EXISTS (SELECT 1 FROM mismatches) THEN 'exact' ELSE 'mismatch' END;
'@
    if ($state -ne 'exact') { throw "Initial reviewed Treatment Chemical seed is not exact in $Database." }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is required.' }

Push-Location $repositoryRoot
try {
    Invoke-Docker -Arguments @('run', '--rm', '-d', '--name', $containerName, '-e', "POSTGRES_PASSWORD=$password", '-p', "${HostPort}:5432", 'postgres:18')
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready=$true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'PostgreSQL 18 did not become ready.' }

    Invoke-Docker -Arguments @('exec', $containerName, 'mkdir', '-p', $containerScriptRoot)
    foreach ($name in @('preflight-room-treatment-tracking.sql','apply-room-treatment-tracking-schema.sql','verify-room-treatment-tracking.sql','preflight-treatment-report-attachments.sql','apply-treatment-report-attachments-schema.sql','verify-treatment-report-attachments.sql')) {
        Invoke-Docker -Arguments @('cp', (Join-Path $repositoryRoot "scripts\postgresql\$name"), "${containerName}:${containerScriptRoot}/$name")
    }

    $fresh='cropqc_room_treatment_fresh'
    New-Database $fresh
    Invoke-EfUpdate $fresh $null
    Invoke-Script $fresh 'verify-room-treatment-tracking.sql'
    Invoke-Gate $fresh

    $upgrade='cropqc_room_treatment_upgrade'
    New-Database $upgrade
    Invoke-EfUpdate $upgrade $previousMigration
    $historyBefore=Invoke-SqlScalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    Invoke-Script $upgrade 'preflight-room-treatment-tracking.sql'
    Invoke-Script $upgrade 'apply-room-treatment-tracking-schema.sql'
    Assert-InitialReviewedSeed $upgrade
    Invoke-Script $upgrade 'apply-room-treatment-tracking-schema.sql'
    $historyAfter=Invoke-SqlScalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyAfter -ne $historyBefore) { throw "Migration history changed: $historyBefore -> $historyAfter" }
    $freshSignature=Get-ObjectSignature $fresh
    $compatibilitySignature=Get-ObjectSignature $upgrade
    if ($freshSignature -ne $compatibilitySignature) { throw "EF/compatibility catalog parity failed.`nEF: $freshSignature`nCompatibility: $compatibilitySignature" }
    Invoke-Script $upgrade 'apply-treatment-report-attachments-schema.sql'
    Invoke-Script $upgrade 'apply-treatment-report-attachments-schema.sql'
    Invoke-Gate $upgrade

    $extra='cropqc_room_treatment_extra_master_data'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$extra)
    Invoke-Sql $extra @'
INSERT INTO "TreatmentChemicals" ("Id","ProductName","CommonName","Crop","Volume","Unit","UnitPrice","Currency","IsActive","CreatedAt","CreatedByUserId","UpdatedAt","UpdatedByUserId") VALUES
(11,'SMARTFRESH INBOX FLEX/250X5G/1.25KG','MCP','Apples',1.00,'BIN',2.84,'USD',TRUE,'2026-08-20T02:02:13.864872Z',NULL,'2026-08-20T02:02:13.864931Z',NULL),
(12,'SMARTFRESH INBOX FLEX/250X5G/1.25KG Pear','MCP','Pears',1.00,'BIN',2.84,'USD',TRUE,'2026-08-20T02:03:27.286117Z',NULL,'2026-08-20T02:09:46.543253Z',NULL);
SELECT setval(pg_get_serial_sequence('"TreatmentChemicals"','Id'),12,true);
'@
    $extraBefore=Get-TreatmentChemicalFingerprint $extra
    if (-not $extraBefore.StartsWith('12|')) { throw "Expected 12 Treatment Chemicals in production-shape regression, got $extraBefore." }
    Invoke-Script $extra 'preflight-room-treatment-tracking.sql'
    Invoke-Script $extra 'apply-room-treatment-tracking-schema.sql'
    $extraAfter=Get-TreatmentChemicalFingerprint $extra
    if ($extraAfter -ne $extraBefore) { throw "Additional Treatment Chemical master data changed: $extraBefore -> $extraAfter" }

    $many='cropqc_room_treatment_many_extra'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$extra,$many)
    Invoke-Sql $many @'
INSERT INTO "TreatmentChemicals" ("Id","ProductName","CommonName","Crop","Volume","Unit","UnitPrice","Currency","IsActive","CreatedAt","CreatedByUserId","UpdatedAt","UpdatedByUserId")
SELECT id,'ADDITIONAL REVIEWED PRODUCT '||id,NULL,CASE WHEN id%2=0 THEN 'Apples' ELSE 'Pears' END,1.00,'BIN',id::numeric,'USD',TRUE,'2026-08-20T03:00:00Z',NULL,'2026-08-20T03:00:00Z',NULL
FROM generate_series(13,20) id;
'@
    $manyBefore=Get-TreatmentChemicalFingerprint $many
    Invoke-Script $many 'preflight-room-treatment-tracking.sql'
    Invoke-Script $many 'apply-room-treatment-tracking-schema.sql'
    if ((Get-TreatmentChemicalFingerprint $many) -ne $manyBefore) { throw 'Many-extra-row compatibility no-op changed Treatment Chemical master data.' }

    $mutable='cropqc_room_treatment_mutable_seed_fields'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$mutable)
    Invoke-Sql $mutable @'
UPDATE "TreatmentChemicals" SET
  "ProductName"='MAINTAINED OFFICIAL PRODUCT NAME', "CommonName"='Maintained common name', "Crop"='Pears',
  "Volume"=2.50, "Unit"='PALLET', "UnitPrice"=12.34, "Currency"='CAD', "IsActive"=FALSE,
  "UpdatedAt"='2026-08-20T04:00:00Z'
WHERE "Id"=1;
'@
    $mutableBefore=Get-TreatmentChemicalFingerprint $mutable
    Invoke-Script $mutable 'preflight-room-treatment-tracking.sql'
    Invoke-Script $mutable 'apply-room-treatment-tracking-schema.sql'
    if ((Get-TreatmentChemicalFingerprint $mutable) -ne $mutableBefore) { throw 'Compatibility no-op changed legitimately maintained seed master data.' }

    $missingSeed='cropqc_room_treatment_missing_seed_identity'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$missingSeed)
    Invoke-Sql $missingSeed 'delete from "TreatmentChemicals" where "Id"=1;'
    Assert-ScriptFails $missingSeed 'preflight-room-treatment-tracking.sql'

    $replacedSeed='cropqc_room_treatment_replaced_seed_identity'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$replacedSeed)
    Invoke-Sql $replacedSeed @'
DELETE FROM "TreatmentChemicals" WHERE "Id"=1;
INSERT INTO "TreatmentChemicals" ("Id","ProductName","CommonName","Crop","Volume","Unit","UnitPrice","Currency","IsActive","CreatedAt","CreatedByUserId","UpdatedAt","UpdatedByUserId")
VALUES (1,'UNRELATED REPLACEMENT',NULL,'Apples',1.00,'BIN',1.00,'USD',TRUE,'2026-08-20T05:00:00Z',NULL,'2026-08-20T05:00:00Z',NULL);
'@
    Assert-ScriptFails $replacedSeed 'preflight-room-treatment-tracking.sql'

    $wrongColumn='cropqc_room_treatment_wrong_column'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$wrongColumn)
    Invoke-Sql $wrongColumn 'alter table "TreatmentLineageSegments" alter column "IdentityKey" type character varying(499);'
    Assert-ScriptFails $wrongColumn 'preflight-room-treatment-tracking.sql'

    $missingIndex='cropqc_room_treatment_missing_index'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$missingIndex)
    Invoke-Sql $missingIndex 'drop index "IX_TreatmentLineageSegments_RoomId_CurrentBins";'
    Assert-ScriptFails $missingIndex 'preflight-room-treatment-tracking.sql'

    $partial='cropqc_room_treatment_partial'
    New-Database $partial
    Invoke-EfUpdate $partial $previousMigration
    Invoke-Sql $partial 'alter table "BinsRunEntries" add column "TreatmentStateSnapshot" character varying(25);'
    Assert-ScriptFails $partial 'preflight-room-treatment-tracking.sql'

    $rollback='cropqc_room_treatment_rollback'
    New-Database $rollback
    Invoke-EfUpdate $rollback $previousMigration
    $prior=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $rollback -c "set cropqc.test_force_room_treatment_failure='on'" -f "$containerScriptRoot/apply-room-treatment-tracking-schema.sql" *> $null
        $forcedExit=$LASTEXITCODE
    }
    finally { $ErrorActionPreference=$prior }
    if ($forcedExit -eq 0) { throw 'Forced apply failure unexpectedly passed.' }
    if ((Invoke-SqlScalar $rollback 'select case when to_regclass(''"TreatmentChemicals"'') is null and not exists (select 1 from information_schema.columns where table_schema=current_schema() and table_name=''BinsRunEntries'' and column_name like ''Treatment%Snapshot'') then ''absent'' else ''partial'' end;') -ne 'absent') { throw 'Forced failure left partial room treatment objects.' }

    Write-Output 'Fresh PostgreSQL 18 EF migration and 502-object gate: PASS'
    Write-Output 'Room Treatment compatibility State A/apply/verify/repeat State B, attachment prerequisite, and final gate: PASS'
    Write-Output "EF/compatibility catalog parity: PASS ($freshSignature)"
    Write-Output "Migration history unchanged: PASS ($historyBefore)"
    Write-Output "Exact ten, 12-row production shape, many extras, and mutable Master Data State B: PASS ($extraBefore)"
    Write-Output 'Missing/replaced durable reviewed seed identity State C: PASS'
    Write-Output 'Wrong column, missing index, and partial State C fail closed: PASS'
    Write-Output 'Forced apply failure rollback: PASS'
}
finally {
    Pop-Location
    if (-not $KeepContainer) {
        $prior=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            & docker container inspect $containerName *> $null
            if ($LASTEXITCODE -eq 0) { & docker rm -f $containerName *> $null }
        }
        finally { $ErrorActionPreference=$prior }
    }
}
