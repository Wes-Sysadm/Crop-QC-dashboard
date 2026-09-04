param([int]$HostPort = 55471, [switch]$KeepContainer)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$container = "cropqc-photo-orientation-$PID"
$password = 'cropqc-disposable-photo-orientation'
$previous = '20260902201338_AddActualRunDetailCorrections'
$expected = '20260904030132_ReintroduceQcPhotoOrientation'
$scriptRoot = '/tmp/cropqc-photo-orientation'

function Invoke-Docker { param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments) & docker @Arguments; if ($LASTEXITCODE -ne 0) { throw "docker failed: $($Arguments -join ' ')" } }
function New-Db([string]$name) { Invoke-Docker -Arguments @('exec', $container, 'createdb', '-U', 'postgres', $name) }
function Copy-Db([string]$source, [string]$name) { Invoke-Docker -Arguments @('exec', $container, 'createdb', '-U', 'postgres', '-T', $source, $name) }
function Invoke-Sql([string]$db, [string]$sql) { $sql | docker exec -i $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $db; if ($LASTEXITCODE -ne 0) { throw "SQL failed: $db" } }
function Get-Scalar([string]$db, [string]$sql) { $result = $sql | docker exec -i $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $db -At; if ($LASTEXITCODE -ne 0) { throw "SQL scalar failed: $db" }; return ($result | Select-Object -Last 1).Trim() }
function Invoke-Script([string]$db, [string]$name) { docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $db -f "$scriptRoot/$name"; if ($LASTEXITCODE -ne 0) { throw "$name failed: $db" } }
function Invoke-Ef([string]$db, [string]$target) { $oldProvider = $env:DATABASE_PROVIDER; $oldConnection = $env:ConnectionStrings__CropQc; try { $env:DATABASE_PROVIDER = 'PostgreSql'; $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$db;Username=postgres;Password=$password"; dotnet ef database update $target --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build; if ($LASTEXITCODE -ne 0) { throw "EF failed: $db" } } finally { $env:DATABASE_PROVIDER = $oldProvider; $env:ConnectionStrings__CropQc = $oldConnection } }
function New-MigrationScript([string]$path) { $oldProvider = $env:DATABASE_PROVIDER; try { $env:DATABASE_PROVIDER = 'PostgreSql'; dotnet ef migrations script $previous $expected --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build --output $path; if ($LASTEXITCODE -ne 0) { throw 'EF migration script generation failed.' } } finally { $env:DATABASE_PROVIDER = $oldProvider } }
function New-FreshModel([string]$db) { $oldConnection = $env:CROPQC_PHOTO_SCHEMA_POSTGRES; try { $env:CROPQC_PHOTO_SCHEMA_POSTGRES = "Host=127.0.0.1;Port=$HostPort;Database=$db;Username=postgres;Password=$password"; dotnet test tests\CropQc.Api.Tests\CropQc.Api.Tests.csproj --no-build --filter FullyQualifiedName~PostgreSql_fresh_model_contains_exact_orientation_schema_WhenConfigured; if ($LASTEXITCODE -ne 0) { throw "Fresh model creation failed: $db" } } finally { $env:CROPQC_PHOTO_SCHEMA_POSTGRES = $oldConnection } }
function Invoke-Concurrency([string]$db) { $oldConnection = $env:CROPQC_PHOTO_ORIENTATION_CONCURRENCY_POSTGRES; try { $env:CROPQC_PHOTO_ORIENTATION_CONCURRENCY_POSTGRES = "Host=127.0.0.1;Port=$HostPort;Database=$db;Username=postgres;Password=$password"; dotnet test tests\CropQc.Api.Tests\CropQc.Api.Tests.csproj --no-build --filter FullyQualifiedName~AuthenticatedPostgreSql_ConcurrentSameRevisionRotation_CommitsOnceAndReturnsCurrentRevision_WhenConfigured; if ($LASTEXITCODE -ne 0) { throw "PostgreSQL rotation concurrency failed: $db" } } finally { $env:CROPQC_PHOTO_ORIENTATION_CONCURRENCY_POSTGRES = $oldConnection } }
function Invoke-Gate([string]$db) { $oldEnvironment = $env:ASPNETCORE_ENVIRONMENT; $oldProvider = $env:DATABASE_PROVIDER; $oldConnection = $env:ConnectionStrings__CropQc; $oldError = $ErrorActionPreference; try { $env:ASPNETCORE_ENVIRONMENT = 'Production'; $env:DATABASE_PROVIDER = 'PostgreSql'; $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$db;Username=postgres;Password=$password"; $ErrorActionPreference = 'Continue'; $output = dotnet 'src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll' "--verify-schema=$expected" 2>&1; $gateText = $output -join "`n"; if ($gateText -notmatch 'Database deployment gate passed' -or $gateText -notmatch 'checked object count 909') { throw "909-object schema gate failed: $db" }; 'Application 909-object schema gate: PASS' } finally { $ErrorActionPreference = $oldError; $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment; $env:DATABASE_PROVIDER = $oldProvider; $env:ConnectionStrings__CropQc = $oldConnection } }
function Get-Signature([string]$db) { Get-Scalar $db @'
WITH parts AS (
 SELECT 'columns=' || md5(string_agg(column_name || '|' || data_type || '|' || coalesce(character_maximum_length::text, '') || '|' || is_nullable || '|' || coalesce(column_default, ''), ';' ORDER BY ordinal_position)) AS value
 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'QcPhotos' AND column_name IN ('OriginalExifOrientation','ManualRotationQuarterTurns','PresentationRevision','PresentationStorageKey','PresentationFileName','PresentationContentType','PresentationFileSizeBytes','PresentationUpdatedAt')
 UNION ALL
 SELECT 'checks=' || md5(string_agg(c.conname || '|' || pg_get_constraintdef(c.oid), ';' ORDER BY c.conname))
 FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace
 WHERE n.nspname = current_schema() AND t.relname = 'QcPhotos' AND c.conname IN ('CK_QcPhotos_OrientationState','CK_QcPhotos_PresentationMetadata'))
SELECT string_agg(value, ';' ORDER BY value) FROM parts;
'@ }
function Assert-Fails([string]$db, [string]$name) { $old = $ErrorActionPreference; try { $ErrorActionPreference = 'Continue'; docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $db -f "$scriptRoot/$name" *> $null; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $old }; if ($code -eq 0) { throw "$name unexpectedly passed: $db" } }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is required.' }
Push-Location $root
try {
    Invoke-Docker -Arguments @('run', '--rm', '-d', '--name', $container, '-e', "POSTGRES_PASSWORD=$password", '-p', "${HostPort}:5432", 'postgres:18')
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) { docker exec $container pg_isready -U postgres *> $null; if ($LASTEXITCODE -eq 0) { $ready = $true; break }; Start-Sleep -Milliseconds 500 }
    if (-not $ready) { throw 'PostgreSQL 18 did not become ready.' }
    Invoke-Docker -Arguments @('exec', $container, 'mkdir', '-p', $scriptRoot)
    foreach ($file in Get-ChildItem (Join-Path $root 'scripts\postgresql\*.sql')) { Invoke-Docker -Arguments @('cp', $file.FullName, "${container}:${scriptRoot}/$($file.Name)") }

    New-Db cropqc_photo_test_fresh
    New-FreshModel cropqc_photo_test_fresh
    Invoke-Concurrency cropqc_photo_test_fresh

    New-Db cropqc_photo_baseline
    Invoke-Ef cropqc_photo_baseline '20260828033737_AddTransferCustodyWorkflow'
    Invoke-Sql cropqc_photo_baseline 'create table "GrowerLots" ("Id" integer not null primary key);'
    Invoke-Script cropqc_photo_baseline preflight-inventory-identity-corrections.sql
    Invoke-Script cropqc_photo_baseline apply-inventory-identity-corrections-schema.sql
    Invoke-Script cropqc_photo_baseline verify-inventory-identity-corrections.sql
    Invoke-Script cropqc_photo_baseline preflight-actual-run-detail-corrections.sql
    Invoke-Script cropqc_photo_baseline apply-actual-run-detail-corrections-schema.sql
    Invoke-Script cropqc_photo_baseline verify-actual-run-detail-corrections.sql

    Copy-Db cropqc_photo_baseline cropqc_photo_ef
    $migrationSql = Join-Path $env:TEMP "cropqc-photo-orientation-$PID.sql"
    New-MigrationScript $migrationSql
    Invoke-Docker -Arguments @('cp', $migrationSql, "${container}:${scriptRoot}/ef-photo-migration.sql")
    Invoke-Script cropqc_photo_ef ef-photo-migration.sql
    Invoke-Script cropqc_photo_ef verify-qc-photo-orientation.sql
    Invoke-Gate cropqc_photo_ef
    $efSignature = Get-Signature cropqc_photo_ef

    Copy-Db cropqc_photo_baseline cropqc_photo_compat
    $historyBefore = Get-Scalar cropqc_photo_compat 'select count(*) || ''|'' || md5(string_agg("MigrationId" || ''|'' || "ProductVersion", '';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    Invoke-Script cropqc_photo_compat preflight-qc-photo-orientation.sql
    Invoke-Script cropqc_photo_compat apply-qc-photo-orientation-schema.sql
    Invoke-Script cropqc_photo_compat apply-qc-photo-orientation-schema.sql
    Invoke-Gate cropqc_photo_compat
    $historyAfter = Get-Scalar cropqc_photo_compat 'select count(*) || ''|'' || md5(string_agg("MigrationId" || ''|'' || "ProductVersion", '';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyBefore -ne $historyAfter) { throw 'Compatibility path changed migration history.' }
    $compatSignature = Get-Signature cropqc_photo_compat
    if ($efSignature -ne $compatSignature) { throw "EF/compatibility catalog parity failed.`nEF: $efSignature`nCompatibility: $compatSignature" }

    Copy-Db cropqc_photo_baseline cropqc_photo_partial
    Invoke-Sql cropqc_photo_partial 'alter table "QcPhotos" add column "PresentationRevision" integer not null default 0;'
    Assert-Fails cropqc_photo_partial preflight-qc-photo-orientation.sql

    Copy-Db cropqc_photo_baseline cropqc_photo_rollback
    $old = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d cropqc_photo_rollback -c "set cropqc.test_force_qc_photo_orientation_failure='on'" -f "$scriptRoot/apply-qc-photo-orientation-schema.sql" *> $null; $forced = $LASTEXITCODE } finally { $ErrorActionPreference = $old }
    if ($forced -eq 0) { throw 'Forced rollback unexpectedly passed.' }
    $remaining = Get-Scalar cropqc_photo_rollback 'select count(*) from information_schema.columns where table_schema=current_schema() and table_name=''QcPhotos'' and column_name in (''OriginalExifOrientation'',''ManualRotationQuarterTurns'',''PresentationRevision'',''PresentationStorageKey'',''PresentationFileName'',''PresentationContentType'',''PresentationFileSizeBytes'',''PresentationUpdatedAt'');'
    if ($remaining -ne '0') { throw "Forced rollback left $remaining target columns." }

    'Fresh PostgreSQL 18 current-baseline plus EF migration and 909-object gate: PASS'
    'Compatibility State A/apply/verify/repeat State B: PASS'
    "EF/compatibility catalog parity: PASS ($efSignature)"
    "Migration history unchanged: PASS ($historyBefore)"
    'Partial State C and forced rollback: PASS'
} finally {
    Pop-Location
    if (-not $KeepContainer) { $old = $ErrorActionPreference; try { $ErrorActionPreference = 'Continue'; docker rm -f $container *> $null } finally { $ErrorActionPreference = $old } }
}
