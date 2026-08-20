param([int]$HostPort = 55452, [switch]$KeepContainer)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-treatment-reports-schema-$PID"
$password = 'cropqc-disposable-treatment-reports-only'
$previousMigration = '20260818181556_AddRoomTreatmentTracking'
$expectedMigration = '20260819142656_AddTreatmentReportAttachments'
$containerScriptRoot = '/tmp/cropqc-treatment-reports'

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function New-Database([string]$Name) {
    if ($Name -notmatch '^cropqc_treatment_reports_') { throw "Refusing non-disposable database '$Name'." }
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
        if ($LASTEXITCODE -ne 0) { throw "502-object gate failed for $Database." }
    }
    finally { $env:ASPNETCORE_ENVIRONMENT = $priorEnvironment; $env:DATABASE_PROVIDER = $priorProvider; $env:ConnectionStrings__CropQc = $priorConnection }
}

function Get-Signature([string]$Database) {
    return Invoke-Scalar $Database @'
WITH signatures AS (
  SELECT 'columns='||md5(string_agg(concat_ws('|',ordinal_position,column_name,data_type,coalesce(character_maximum_length::text,''),is_nullable,is_identity),';' ORDER BY ordinal_position)) value
  FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomTreatmentApplicationAttachments'
  UNION ALL
  SELECT 'indexes='||md5(string_agg(pg_get_indexdef(i.indexrelid),';' ORDER BY c.relname))
  FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
  WHERE n.nspname=current_schema() AND t.relname='RoomTreatmentApplicationAttachments'
  UNION ALL
  SELECT 'constraints='||md5(string_agg(c.conname||'|'||c.contype::text||'|'||pg_get_constraintdef(c.oid),';' ORDER BY c.conname))
  FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
  WHERE n.nspname=current_schema() AND t.relname='RoomTreatmentApplicationAttachments')
SELECT string_agg(value,';' ORDER BY value) FROM signatures;
'@
}

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
    foreach ($name in @('preflight-room-treatment-tracking.sql','verify-room-treatment-tracking.sql','preflight-treatment-report-attachments.sql','apply-treatment-report-attachments-schema.sql','verify-treatment-report-attachments.sql')) {
        Invoke-Docker -Arguments @('cp',(Join-Path $repositoryRoot "scripts\postgresql\$name"),"${containerName}:${containerScriptRoot}/$name")
    }

    $fresh = 'cropqc_treatment_reports_fresh'
    New-Database $fresh
    Invoke-EfUpdate $fresh $null
    Invoke-Script $fresh 'verify-treatment-report-attachments.sql'
    Invoke-Gate $fresh

    $upgrade = 'cropqc_treatment_reports_upgrade'
    New-Database $upgrade
    Invoke-EfUpdate $upgrade $previousMigration
    $historyBefore = Invoke-Scalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    Invoke-Script $upgrade 'preflight-treatment-report-attachments.sql'
    Invoke-Script $upgrade 'apply-treatment-report-attachments-schema.sql'
    Invoke-Script $upgrade 'apply-treatment-report-attachments-schema.sql'
    Invoke-Gate $upgrade
    $historyAfter = Invoke-Scalar $upgrade 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyAfter -ne $historyBefore) { throw "Migration history changed: $historyBefore -> $historyAfter" }
    $freshSignature = Get-Signature $fresh
    $compatibilitySignature = Get-Signature $upgrade
    if ($freshSignature -ne $compatibilitySignature) { throw "EF/compatibility catalog parity failed.`nEF: $freshSignature`nCompatibility: $compatibilitySignature" }

    $partial = 'cropqc_treatment_reports_partial'
    New-Database $partial
    Invoke-EfUpdate $partial $previousMigration
    Invoke-Sql $partial 'create table "RoomTreatmentApplicationAttachments" ("Id" bigint);'
    Assert-ScriptFails $partial 'preflight-treatment-report-attachments.sql'

    $wrong = 'cropqc_treatment_reports_wrong'
    Invoke-Docker -Arguments @('exec',$containerName,'createdb','-U','postgres','-T',$upgrade,$wrong)
    Invoke-Sql $wrong 'alter table "RoomTreatmentApplicationAttachments" alter column "FileName" type character varying(254);'
    Assert-ScriptFails $wrong 'preflight-treatment-report-attachments.sql'

    $rollback = 'cropqc_treatment_reports_rollback'
    New-Database $rollback
    Invoke-EfUpdate $rollback $previousMigration
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $rollback -c "set cropqc.test_force_treatment_report_failure='on'" -f "$containerScriptRoot/apply-treatment-report-attachments-schema.sql" *> $null
        $forcedExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
    if ($forcedExit -eq 0) { throw 'Forced apply failure unexpectedly passed.' }
    if ((Invoke-Scalar $rollback 'select case when to_regclass(''"RoomTreatmentApplicationAttachments"'') is null then ''absent'' else ''partial'' end;') -ne 'absent') { throw 'Forced apply failure left partial objects.' }

    Write-Output 'Fresh PostgreSQL 18 EF migration and 502-object gate: PASS'
    Write-Output 'Compatibility State A/apply/verify/repeat State B and gate: PASS'
    Write-Output "EF/compatibility catalog parity: PASS ($freshSignature)"
    Write-Output "Migration history unchanged: PASS ($historyBefore)"
    Write-Output 'Partial/wrong State C fail closed and forced rollback: PASS'
}
finally {
    Pop-Location
    if (-not $KeepContainer) {
        $prior = $ErrorActionPreference
        try { $ErrorActionPreference = 'Continue'; & docker rm -f $containerName *> $null }
        finally { $ErrorActionPreference = $prior }
    }
}
