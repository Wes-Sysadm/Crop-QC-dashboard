param([int]$HostPort = 55448, [switch]$KeepContainer)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-room-treatment-schema-$PID"
$password = 'cropqc-disposable-room-treatment-only'
$previousMigration = '20260817075807_AddEndOfDayFillWarehouseScope'
$expectedMigration = '20260818181556_AddRoomTreatmentTracking'
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
        if ($LASTEXITCODE -ne 0) { throw "476-object gate failed for $Database." }
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
    foreach ($name in @('preflight-room-treatment-tracking.sql','apply-room-treatment-tracking-schema.sql','verify-room-treatment-tracking.sql')) {
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
    Invoke-Gate $upgrade
    Invoke-Script $upgrade 'apply-room-treatment-tracking-schema.sql'
    Invoke-Gate $upgrade
    $historyAfter=Invoke-SqlScalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyAfter -ne $historyBefore) { throw "Migration history changed: $historyBefore -> $historyAfter" }
    $freshSignature=Get-ObjectSignature $fresh
    $compatibilitySignature=Get-ObjectSignature $upgrade
    if ($freshSignature -ne $compatibilitySignature) { throw "EF/compatibility catalog parity failed.`nEF: $freshSignature`nCompatibility: $compatibilitySignature" }

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

    Write-Output 'Fresh PostgreSQL 18 EF migration and 476-object gate: PASS'
    Write-Output 'Compatibility State A/apply/verify/repeat State B and gate: PASS'
    Write-Output "EF/compatibility catalog parity: PASS ($freshSignature)"
    Write-Output "Migration history unchanged: PASS ($historyBefore)"
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
