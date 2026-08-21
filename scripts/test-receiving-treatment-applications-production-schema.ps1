param([int]$HostPort = 55453, [switch]$KeepContainer)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-receiving-treatment-schema-$PID"
$password = 'cropqc-disposable-receiving-treatment-only'
$previousMigration = '20260819142656_AddTreatmentReportAttachments'
$expectedMigration = '20260820194148_AddReceivingTreatmentApplications'
$containerScriptRoot = '/tmp/cropqc-receiving-treatment'

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function New-Database([string]$Name) {
    if ($Name -notmatch '^cropqc_receiving_treatment_') { throw "Refusing non-disposable database '$Name'." }
    Invoke-Docker -Arguments @('exec', $containerName, 'createdb', '-U', 'postgres', $Name)
}

function Invoke-Sql([string]$Database, [string]$Sql) {
    $Sql | & docker exec -i $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database
    if ($LASTEXITCODE -ne 0) { throw "SQL failed for $Database." }
}

function Invoke-Scalar([string]$Database, [string]$Sql) {
    $result = $Sql | & docker exec -i $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database -At
    if ($LASTEXITCODE -ne 0) { throw "Scalar SQL failed for $Database." }
    return ($result | Select-Object -Last 1).Trim()
}

function Invoke-Script([string]$Database, [string]$Name) {
    & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database -f "$containerScriptRoot/$Name"
    if ($LASTEXITCODE -ne 0) { throw "$Name failed for $Database." }
}

function Assert-ScriptFails([string]$Database, [string]$Name) {
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $Database -f "$containerScriptRoot/$Name" *> $null
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
    if ($exitCode -eq 0) { throw "$Name unexpectedly passed for incompatible database $Database." }
}

function Invoke-EfUpdate([string]$Database, [string]$Target) {
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
    finally { $env:DATABASE_PROVIDER = $priorProvider; $env:ConnectionStrings__CropQc = $priorConnection }
}

function Invoke-Gate([string]$Database) {
    $priorEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $env:DATABASE_PROVIDER = 'PostgreSql'
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$Database;Username=postgres;Password=$password"
        & dotnet 'src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll' "--verify-schema=$expectedMigration"
        if ($LASTEXITCODE -ne 0) { throw "517-object gate failed for $Database." }
    }
    finally { $env:ASPNETCORE_ENVIRONMENT = $priorEnvironment; $env:DATABASE_PROVIDER = $priorProvider; $env:ConnectionStrings__CropQc = $priorConnection }
}

function Get-Signature([string]$Database) {
    return Invoke-Scalar $Database @'
WITH target_tables(name) AS (VALUES
 ('TreatmentChemicals'),('RoomTreatmentApplications'),('RoomTreatmentApplicationSources'),('TreatmentLineageSegments'),('TreatmentLineageMovements')),
signatures AS (
  SELECT 'columns='||md5(string_agg(concat_ws('|',table_name,ordinal_position,column_name,data_type,coalesce(character_maximum_length::text,''),is_nullable,column_default),';' ORDER BY table_name,ordinal_position)) value
  FROM information_schema.columns WHERE table_schema=current_schema() AND table_name IN (SELECT name FROM target_tables)
  UNION ALL
  SELECT 'indexes='||md5(string_agg(t.relname||'|'||pg_get_indexdef(i.indexrelid),';' ORDER BY t.relname,c.relname))
  FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
  WHERE n.nspname=current_schema() AND t.relname IN (SELECT name FROM target_tables)
  UNION ALL
  SELECT 'constraints='||md5(string_agg(t.relname||'|'||c.conname||'|'||c.contype::text||'|'||pg_get_constraintdef(c.oid),';' ORDER BY t.relname,c.conname))
  FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
  WHERE n.nspname=current_schema() AND t.relname IN (SELECT name FROM target_tables))
SELECT string_agg(value,';' ORDER BY value) FROM signatures;
'@
}

function Get-ProtectedChemicalFingerprint([string]$Database) {
    return Invoke-Scalar $Database @'
SELECT count(*)||'|'||md5(coalesce(string_agg(concat_ws('|',"Id","ProductName",coalesce("CommonName",''),"Crop","Volume","Unit","UnitPrice","Currency","IsActive","CreatedAt",coalesce("CreatedByUserId"::text,''),coalesce("UpdatedByUserId"::text,'')),';' ORDER BY "Id"),''))
FROM "TreatmentChemicals";
'@
}

$productionTreatmentChemicalExtension = @'
INSERT INTO "TreatmentChemicals" ("Id","ProductName","CommonName","Crop","Volume","Unit","UnitPrice","Currency","IsActive","CreatedAt","CreatedByUserId","UpdatedAt","UpdatedByUserId") VALUES
(11,'SMARTFRESH INBOX FLEX/250X5G/1.25KG','MCP','Apples',1.00,'BIN',2.84,'USD',TRUE,'2026-08-20T02:02:13.864872Z',NULL,'2026-08-20T02:02:13.864931Z',NULL),
(12,'SMARTFRESH INBOX FLEX/250X5G/1.25KG Pear','MCP','Pears',1.00,'BIN',2.84,'USD',TRUE,'2026-08-20T02:03:27.286117Z',NULL,'2026-08-20T02:09:46.543253Z',NULL);
SELECT setval(pg_get_serial_sequence('"TreatmentChemicals"','Id'),12,true);
'@

$scriptNames = @(
    'preflight-room-treatment-tracking.sql','verify-room-treatment-tracking.sql',
    'preflight-treatment-report-attachments.sql','verify-treatment-report-attachments.sql',
    'preflight-receiving-treatment-applications.sql','apply-receiving-treatment-applications-schema.sql','verify-receiving-treatment-applications.sql',
    'preflight-receiving-treatment-chemical-levels.sql','apply-receiving-treatment-chemical-levels.sql','verify-receiving-treatment-chemical-levels.sql')

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is required.' }

Push-Location $repositoryRoot
try {
    Invoke-Docker -Arguments @('run','--rm','-d','--name',$containerName,'-e',"POSTGRES_PASSWORD=$password",'-p',"${HostPort}:5432",'postgres:18')
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'PostgreSQL 18 did not become ready.' }
    Invoke-Docker -Arguments @('exec',$containerName,'mkdir','-p',$containerScriptRoot)
    foreach ($name in $scriptNames) {
        Invoke-Docker -Arguments @('cp',(Join-Path $repositoryRoot "scripts\postgresql\$name"),"${containerName}:${containerScriptRoot}/$name")
    }

    $fresh = 'cropqc_receiving_treatment_fresh'
    New-Database $fresh
    Invoke-EfUpdate $fresh $null
    Invoke-Script $fresh 'verify-receiving-treatment-applications.sql'
    Invoke-Gate $fresh

    $upgrade = 'cropqc_receiving_treatment_upgrade'
    New-Database $upgrade
    Invoke-EfUpdate $upgrade $previousMigration
    Invoke-Sql $upgrade $productionTreatmentChemicalExtension
    $historyBefore = Invoke-Scalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    $chemicalsBefore = Get-ProtectedChemicalFingerprint $upgrade
    if (-not $chemicalsBefore.StartsWith('12|')) { throw "Expected 12 Treatment Chemicals, got $chemicalsBefore." }
    Invoke-Script $upgrade 'preflight-receiving-treatment-applications.sql'
    Invoke-Script $upgrade 'apply-receiving-treatment-applications-schema.sql'
    Invoke-Script $upgrade 'apply-receiving-treatment-applications-schema.sql'
    Invoke-Script $upgrade 'preflight-receiving-treatment-chemical-levels.sql'
    Invoke-Script $upgrade 'apply-receiving-treatment-chemical-levels.sql'
    $alignedFingerprint = Invoke-Scalar $upgrade 'select md5(string_agg(to_jsonb(t)::text,'';'' order by "Id")) from "TreatmentChemicals" t;'
    Invoke-Script $upgrade 'apply-receiving-treatment-chemical-levels.sql'
    $rerunFingerprint = Invoke-Scalar $upgrade 'select md5(string_agg(to_jsonb(t)::text,'';'' order by "Id")) from "TreatmentChemicals" t;'
    if ($rerunFingerprint -ne $alignedFingerprint) { throw "MCP config rerun wrote data: $alignedFingerprint -> $rerunFingerprint" }
    Invoke-Gate $upgrade
    $historyAfter = Invoke-Scalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyAfter -ne $historyBefore) { throw "Migration history changed: $historyBefore -> $historyAfter" }
    $chemicalsAfter = Get-ProtectedChemicalFingerprint $upgrade
    if ($chemicalsAfter -ne $chemicalsBefore) { throw "Protected Treatment Chemical fields changed: $chemicalsBefore -> $chemicalsAfter" }
    $freshSignature = Get-Signature $fresh
    $compatibilitySignature = Get-Signature $upgrade
    if ($freshSignature -ne $compatibilitySignature) { throw "EF/compatibility catalog parity failed.`nEF: $freshSignature`nCompatibility: $compatibilitySignature" }

    $partial = 'cropqc_receiving_treatment_partial'
    New-Database $partial
    Invoke-EfUpdate $partial $previousMigration
    Invoke-Sql $partial 'alter table "TreatmentChemicals" add column "ApplicationLevel" character varying(25);'
    Assert-ScriptFails $partial 'preflight-receiving-treatment-applications.sql'

    $wrong = 'cropqc_receiving_treatment_wrong'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$wrong)
    Invoke-Sql $wrong 'alter table "TreatmentChemicals" alter column "ApplicationLevel" type character varying(24);'
    Assert-ScriptFails $wrong 'preflight-receiving-treatment-applications.sql'

    $rollback = 'cropqc_receiving_treatment_rollback'
    New-Database $rollback
    Invoke-EfUpdate $rollback $previousMigration
    Invoke-Sql $rollback $productionTreatmentChemicalExtension
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $rollback -c "set cropqc.test_force_receiving_treatment_failure='on'" -f "$containerScriptRoot/apply-receiving-treatment-applications-schema.sql" *> $null
        $forcedSchemaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
    if ($forcedSchemaExit -eq 0) { throw 'Forced schema apply failure unexpectedly passed.' }
    if ((Invoke-Scalar $rollback 'select count(*) from information_schema.columns where table_schema=current_schema() and column_name in (''ApplicationLevel'',''ReceiptId'') and table_name in (''TreatmentChemicals'',''RoomTreatmentApplications'',''RoomTreatmentApplicationSources'',''TreatmentLineageSegments'',''TreatmentLineageMovements'');') -ne '0') { throw 'Forced schema failure left partial columns.' }

    Invoke-Script $rollback 'apply-receiving-treatment-applications-schema.sql'
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $rollback -c "set cropqc.test_force_receiving_treatment_config_failure='on'" -f "$containerScriptRoot/apply-receiving-treatment-chemical-levels.sql" *> $null
        $forcedConfigExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
    if ($forcedConfigExit -eq 0) { throw 'Forced config apply failure unexpectedly passed.' }
    if ((Invoke-Scalar $rollback 'select count(*) from "TreatmentChemicals" where "Id" in (11,12) and "ApplicationLevel"=''Room'';') -ne '2') { throw 'Forced config failure did not roll back MCP classifications.' }

    Write-Output 'Fresh PostgreSQL 18 EF migration and 517-object gate: PASS'
    Write-Output 'Compatibility State A/apply/verify/repeat State B and gate: PASS'
    Write-Output "EF/compatibility catalog parity: PASS ($freshSignature)"
    Write-Output "Migration history unchanged: PASS ($historyBefore)"
    Write-Output "Production-shape 12-row Treatment Chemical preservation: PASS ($chemicalsBefore)"
    Write-Output 'MCP config dry run/apply/rerun exact rows 11 and 12: PASS'
    Write-Output 'Partial/wrong State C and forced schema/config rollback: PASS'
}
finally {
    Pop-Location
    if (-not $KeepContainer) {
        $prior = $ErrorActionPreference
        try { $ErrorActionPreference = 'Continue'; & docker rm -f $containerName *> $null }
        finally { $ErrorActionPreference = $prior }
    }
}
