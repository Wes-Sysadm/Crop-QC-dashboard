param([int]$HostPort = 55446, [switch]$KeepContainer)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-room-inventory-loss-schema-$PID"
$password = "cropqc-disposable-room-inventory-loss-only"
$previousMigration = "20260809151943_AddInventoryDiagnosticAcknowledgments"
$expectedMigration = "20260812061125_AddRoomInventoryLosses"
$containerScriptRoot = "/tmp/cropqc-room-inventory-loss"

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function New-Database {
    param([string]$Name)
    if ($Name -notmatch '^cropqc_room_inventory_loss_') { throw "Refusing non-disposable database '$Name'." }
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
        $ErrorActionPreference = "Continue"
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
        $env:DATABASE_PROVIDER = "PostgreSql"
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
        $env:ASPNETCORE_ENVIRONMENT = "Production"
        $env:DATABASE_PROVIDER = "PostgreSql"
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        & dotnet 'src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll' "--verify-schema=$expectedMigration"
        if ($LASTEXITCODE -ne 0) { throw "311-object gate failed for $Database." }
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
WITH loss AS (SELECT '"RoomInventoryLosses"'::regclass::oid oid), adjustment AS (SELECT '"RoomInventoryAdjustments"'::regclass::oid oid), signatures AS (
  SELECT 'loss-columns='||md5(string_agg(concat_ws('|',ordinal_position,column_name,data_type,coalesce(character_maximum_length::text,''),is_nullable,is_identity,coalesce(identity_generation,'')),';' ORDER BY ordinal_position)) value
  FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomInventoryLosses'
  UNION ALL
  SELECT 'adjustment-column='||md5(string_agg(concat_ws('|',ordinal_position,column_name,data_type,is_nullable),';' ORDER BY ordinal_position))
  FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomInventoryAdjustments' AND column_name='RoomInventoryLossId'
  UNION ALL
  SELECT 'loss-indexes='||md5(string_agg(pg_get_indexdef(i.indexrelid),';' ORDER BY c.relname))
  FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid,loss l WHERE i.indrelid=l.oid
  UNION ALL
  SELECT 'adjustment-indexes='||md5(string_agg(pg_get_indexdef(i.indexrelid),';' ORDER BY c.relname))
  FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid,adjustment a WHERE i.indrelid=a.oid AND c.relname LIKE '%RoomInventoryLoss%'
  UNION ALL
  SELECT 'loss-constraints='||md5(string_agg(c.conname||'|'||c.contype::text||'|'||pg_get_constraintdef(c.oid),';' ORDER BY c.conname))
  FROM pg_constraint c,loss l WHERE c.conrelid=l.oid AND c.contype IN ('p','f','u','c','x')
  UNION ALL
  SELECT 'adjustment-fk='||md5(string_agg(c.conname||'|'||pg_get_constraintdef(c.oid),';' ORDER BY c.conname))
  FROM pg_constraint c,adjustment a WHERE c.conrelid=a.oid AND c.conname LIKE '%RoomInventoryLoss%')
SELECT string_agg(value,';' ORDER BY value) FROM signatures;
'@
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker is required." }

Push-Location $repositoryRoot
try {
    Invoke-Docker -Arguments @('run', '--rm', '-d', '--name', $containerName, '-e', "POSTGRES_PASSWORD=$password", '-p', "${HostPort}:5432", 'postgres:18')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "PostgreSQL 18 did not become ready." }

    Invoke-Docker -Arguments @('exec', $containerName, 'mkdir', '-p', $containerScriptRoot)
    foreach ($name in @('preflight-room-inventory-losses.sql', 'apply-room-inventory-losses-schema.sql', 'verify-room-inventory-losses.sql')) {
        Invoke-Docker -Arguments @('cp', (Join-Path $repositoryRoot "scripts\postgresql\$name"), "${containerName}:${containerScriptRoot}/$name")
    }

    $fresh = 'cropqc_room_inventory_loss_fresh'
    New-Database $fresh
    Invoke-EfUpdate $fresh $null
    Invoke-Script $fresh 'verify-room-inventory-losses.sql'
    Invoke-Gate $fresh

    $upgrade = 'cropqc_room_inventory_loss_upgrade'
    New-Database $upgrade
    Invoke-EfUpdate $upgrade $previousMigration
    $historyBefore = Invoke-SqlScalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    Invoke-Script $upgrade 'preflight-room-inventory-losses.sql'
    Invoke-Script $upgrade 'apply-room-inventory-losses-schema.sql'
    Invoke-Script $upgrade 'verify-room-inventory-losses.sql'
    Invoke-Gate $upgrade
    Invoke-Script $upgrade 'apply-room-inventory-losses-schema.sql'
    Invoke-Script $upgrade 'verify-room-inventory-losses.sql'
    Invoke-Gate $upgrade
    $historyAfter = Invoke-SqlScalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyAfter -ne $historyBefore) { throw "Migration history changed: $historyBefore -> $historyAfter" }
    $freshSignature = Get-ObjectSignature $fresh
    $compatibilitySignature = Get-ObjectSignature $upgrade
    if ($compatibilitySignature -ne $freshSignature) { throw "Compatibility and EF object signatures differ.`nEF: $freshSignature`nCompatibility: $compatibilitySignature" }

    $wrongColumn = 'cropqc_room_inventory_loss_wrong_column'
    Invoke-Docker -Arguments @('exec', $containerName, 'createdb', '-U', 'postgres', '-T', $upgrade, $wrongColumn)
    Invoke-Sql $wrongColumn 'alter table "RoomInventoryLosses" alter column "Reason" type character varying(499);'
    Assert-ScriptFails $wrongColumn 'preflight-room-inventory-losses.sql'
    Assert-ScriptFails $wrongColumn 'apply-room-inventory-losses-schema.sql'

    $missingIndex = 'cropqc_room_inventory_loss_missing_index'
    Invoke-Docker -Arguments @('exec', $containerName, 'createdb', '-U', 'postgres', '-T', $upgrade, $missingIndex)
    Invoke-Sql $missingIndex 'drop index "IX_RoomInventoryLosses_RoomId_CreatedAt";'
    Assert-ScriptFails $missingIndex 'preflight-room-inventory-losses.sql'

    $wrongFk = 'cropqc_room_inventory_loss_wrong_fk'
    Invoke-Docker -Arguments @('exec', $containerName, 'createdb', '-U', 'postgres', '-T', $upgrade, $wrongFk)
    Invoke-Sql $wrongFk 'alter table "RoomInventoryAdjustments" drop constraint "FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId"; alter table "RoomInventoryAdjustments" add constraint "FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId" foreign key ("RoomInventoryLossId") references "RoomInventoryLosses" ("Id") on delete cascade;'
    Assert-ScriptFails $wrongFk 'preflight-room-inventory-losses.sql'

    $rollback = 'cropqc_room_inventory_loss_rollback'
    New-Database $rollback
    Invoke-EfUpdate $rollback $previousMigration
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $rollback -c "set cropqc.test_force_room_inventory_loss_failure='on'" -f "$containerScriptRoot/apply-room-inventory-losses-schema.sql" *> $null
        $forcedExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
    if ($forcedExit -eq 0) { throw "Forced apply failure unexpectedly passed." }
    if ((Invoke-SqlScalar $rollback 'select case when to_regclass(''"RoomInventoryLosses"'') is null and not exists (select 1 from information_schema.columns where table_schema=current_schema() and table_name=''RoomInventoryAdjustments'' and column_name=''RoomInventoryLossId'') then ''absent'' else ''partial'' end;') -ne 'absent') {
        throw "Forced failure left partial Room Inventory Loss objects."
    }

    Write-Output "Fresh PostgreSQL 18 EF migration and 311-object gate: PASS"
    Write-Output "Compatibility preflight/apply/verify/repeat and gate: PASS"
    Write-Output "EF/compatibility catalog parity: PASS ($freshSignature)"
    Write-Output "Migration history unchanged: PASS ($historyBefore)"
    Write-Output "Wrong column, missing index, and wrong FK fail closed: PASS"
    Write-Output "Forced apply failure rollback: PASS"
}
finally {
    Pop-Location
    if (-not $KeepContainer) {
        $prior = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & docker container inspect $containerName *> $null
            if ($LASTEXITCODE -eq 0) { & docker rm -f $containerName *> $null }
        }
        finally { $ErrorActionPreference = $prior }
    }
}
