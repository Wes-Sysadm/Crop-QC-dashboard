param([int]$HostPort = 55445, [switch]$KeepContainer)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-inventory-diagnostic-schema-$PID"
$password = "cropqc-disposable-inventory-diagnostic-only"
$previousMigration = "20260807210820_AddRoleBasedUserAccess"
$expectedMigration = "20260809151943_AddInventoryDiagnosticAcknowledgments"
$containerScriptRoot = "/tmp/cropqc-inventory-diagnostic"

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function New-Database {
    param([string]$Name)
    if ($Name -notmatch '^cropqc_inventory_diagnostic_') { throw "Refusing non-disposable database '$Name'." }
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres',$Name)
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
        $arguments = @("ef", "database", "update")
        if ($Target) { $arguments += $Target }
        $arguments += @("--project", "src\CropQc.Data\CropQc.Data.csproj", "--startup-project", "src\CropQc.Data\CropQc.Data.csproj", "--no-build")
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
        & dotnet "src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll" "--verify-schema=$expectedMigration"
        if ($LASTEXITCODE -ne 0) { throw "267-object gate failed for $Database." }
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
WITH target AS (SELECT '"InventoryDiagnosticAcknowledgments"'::regclass::oid oid), signatures AS (
  SELECT 'columns='||md5(string_agg(concat_ws('|',ordinal_position,column_name,data_type,coalesce(character_maximum_length::text,''),is_nullable,is_identity,coalesce(identity_generation,'')),';' ORDER BY ordinal_position)) value
  FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='InventoryDiagnosticAcknowledgments'
  UNION ALL
  SELECT 'indexes='||md5(string_agg(pg_get_indexdef(i.indexrelid),';' ORDER BY c.relname))
  FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid,target t WHERE i.indrelid=t.oid
  UNION ALL
  SELECT 'constraints='||md5(string_agg(c.conname||'|'||c.contype::text||'|'||pg_get_constraintdef(c.oid),';' ORDER BY c.conname))
  FROM pg_constraint c,target t WHERE c.conrelid=t.oid AND c.contype IN ('p','f','u','c','x'))
SELECT string_agg(value,';' ORDER BY value) FROM signatures;
'@
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker is required." }

Push-Location $repositoryRoot
try {
    Invoke-Docker -Arguments @('run','--rm','-d','--name',$containerName,'-e',"POSTGRES_PASSWORD=$password",'-p',"${HostPort}:5432",'postgres:18')
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready=$true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "PostgreSQL 18 did not become ready." }

    Invoke-Docker -Arguments @('exec',$containerName,'mkdir','-p',$containerScriptRoot)
    foreach ($name in @('preflight-inventory-diagnostic-acknowledgments.sql','apply-inventory-diagnostic-acknowledgments-schema.sql','verify-inventory-diagnostic-acknowledgments.sql')) {
        Invoke-Docker -Arguments @('cp',(Join-Path $repositoryRoot "scripts\postgresql\$name"),"${containerName}:${containerScriptRoot}/$name")
    }

    $fresh='cropqc_inventory_diagnostic_fresh'
    New-Database $fresh
    Invoke-EfUpdate $fresh $null
    Invoke-Script $fresh 'verify-inventory-diagnostic-acknowledgments.sql'
    Invoke-Gate $fresh

    $upgrade='cropqc_inventory_diagnostic_upgrade'
    New-Database $upgrade
    Invoke-EfUpdate $upgrade $previousMigration
    $historyBefore=Invoke-SqlScalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    Invoke-Script $upgrade 'preflight-inventory-diagnostic-acknowledgments.sql'
    Invoke-Script $upgrade 'apply-inventory-diagnostic-acknowledgments-schema.sql'
    Invoke-Script $upgrade 'verify-inventory-diagnostic-acknowledgments.sql'
    Invoke-Gate $upgrade
    Invoke-Script $upgrade 'apply-inventory-diagnostic-acknowledgments-schema.sql'
    Invoke-Script $upgrade 'verify-inventory-diagnostic-acknowledgments.sql'
    Invoke-Gate $upgrade
    $historyAfter=Invoke-SqlScalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyAfter -ne $historyBefore) { throw "Migration history changed: $historyBefore -> $historyAfter" }
    if ((Get-ObjectSignature $upgrade) -ne (Get-ObjectSignature $fresh)) { throw "Compatibility and EF object signatures differ." }

    $wrongColumn='cropqc_inventory_diagnostic_wrong_column'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$wrongColumn)
    Invoke-Sql $wrongColumn 'alter table "InventoryDiagnosticAcknowledgments" alter column "DiagnosticMessage" type character varying(999);'
    Assert-ScriptFails $wrongColumn 'preflight-inventory-diagnostic-acknowledgments.sql'
    Assert-ScriptFails $wrongColumn 'apply-inventory-diagnostic-acknowledgments-schema.sql'

    $missingIndex='cropqc_inventory_diagnostic_missing_index'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$missingIndex)
    Invoke-Sql $missingIndex 'drop index "IX_InventoryDiagnosticAck_ActiveAdjustment";'
    Assert-ScriptFails $missingIndex 'preflight-inventory-diagnostic-acknowledgments.sql'

    $wrongFk='cropqc_inventory_diagnostic_wrong_fk'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$wrongFk)
    Invoke-Sql $wrongFk 'alter table "InventoryDiagnosticAcknowledgments" drop constraint "FK_InventoryDiagnosticAck_Adjustment"; alter table "InventoryDiagnosticAcknowledgments" add constraint "FK_InventoryDiagnosticAck_Adjustment" foreign key ("RoomInventoryAdjustmentId") references "RoomInventoryAdjustments" ("Id") on delete cascade;'
    Assert-ScriptFails $wrongFk 'preflight-inventory-diagnostic-acknowledgments.sql'

    $rollback='cropqc_inventory_diagnostic_rollback'
    New-Database $rollback
    Invoke-EfUpdate $rollback $previousMigration
    $prior=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $rollback -c "set cropqc.test_force_inventory_diagnostic_ack_failure='on'" -f "$containerScriptRoot/apply-inventory-diagnostic-acknowledgments-schema.sql" *> $null
        $forcedExit=$LASTEXITCODE
    }
    finally { $ErrorActionPreference=$prior }
    if ($forcedExit -eq 0) { throw "Forced apply failure unexpectedly passed." }
    if ((Invoke-SqlScalar $rollback 'select case when to_regclass(''"InventoryDiagnosticAcknowledgments"'') is null then ''absent'' else ''present'' end;') -ne 'absent') { throw "Forced failure left a partial table." }

    Write-Output "Fresh PostgreSQL 18 EF migration and 267-object gate: PASS"
    Write-Output "Compatibility preflight/apply/verify/repeat and gate: PASS"
    Write-Output "EF/compatibility catalog parity: PASS ($(Get-ObjectSignature $fresh))"
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
