param([int]$HostPort = 55471, [switch]$KeepContainer)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$container = "cropqc-photo-orientation-$PID"
$password = 'cropqc-disposable-photo-orientation'
$previous = '20260828033737_AddTransferCustodyWorkflow'
$expected = '20260830233943_NormalizeQcPhotoOrientation'
$scriptRoot = '/tmp/cropqc-photo-orientation'

function Invoke-Docker { param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments) & docker @Arguments; if ($LASTEXITCODE -ne 0) { throw "docker failed: $($Arguments -join ' ')" } }
function New-Db([string]$name) { Invoke-Docker -Arguments @('exec', $container, 'createdb', '-U', 'postgres', $name) }
function Invoke-Sql([string]$db, [string]$sql) { $sql | docker exec -i $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $db; if ($LASTEXITCODE -ne 0) { throw "SQL failed: $db" } }
function Get-Scalar([string]$db, [string]$sql) { $result = $sql | docker exec -i $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $db -At; if ($LASTEXITCODE -ne 0) { throw "SQL scalar failed: $db" }; return ($result | Select-Object -Last 1).Trim() }
function Invoke-Script([string]$db, [string]$name) { docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $db -f "$scriptRoot/$name"; if ($LASTEXITCODE -ne 0) { throw "$name failed: $db" } }
function Invoke-Ef([string]$db, [string]$target) { $oldProvider = $env:DATABASE_PROVIDER; $oldConnection = $env:ConnectionStrings__CropQc; try { $env:DATABASE_PROVIDER = 'PostgreSql'; $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$db;Username=postgres;Password=$password"; $arguments = @('ef', 'database', 'update'); if ($target) { $arguments += $target }; $arguments += @('--project', 'src\CropQc.Data\CropQc.Data.csproj', '--startup-project', 'src\CropQc.Data\CropQc.Data.csproj', '--no-build'); dotnet @arguments; if ($LASTEXITCODE -ne 0) { throw "EF failed: $db" } } finally { $env:DATABASE_PROVIDER = $oldProvider; $env:ConnectionStrings__CropQc = $oldConnection } }
function Invoke-Gate([string]$db) { $oldEnvironment = $env:ASPNETCORE_ENVIRONMENT; $oldProvider = $env:DATABASE_PROVIDER; $oldConnection = $env:ConnectionStrings__CropQc; try { $env:ASPNETCORE_ENVIRONMENT = 'Production'; $env:DATABASE_PROVIDER = 'PostgreSql'; $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$db;Username=postgres;Password=$password"; dotnet 'src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll' "--verify-schema=$expected"; if ($LASTEXITCODE -ne 0) { throw "844-object gate failed: $db" } } finally { $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment; $env:DATABASE_PROVIDER = $oldProvider; $env:ConnectionStrings__CropQc = $oldConnection } }
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
    foreach ($name in @('preflight-qc-photo-orientation.sql', 'apply-qc-photo-orientation-schema.sql', 'verify-qc-photo-orientation.sql')) { Invoke-Docker -Arguments @('cp', (Join-Path $root "scripts\postgresql\$name"), "${container}:${scriptRoot}/$name") }

    New-Db cropqc_photo_fresh
    Invoke-Ef cropqc_photo_fresh $null
    Invoke-Script cropqc_photo_fresh verify-qc-photo-orientation.sql
    Invoke-Gate cropqc_photo_fresh
    $freshSignature = Get-Signature cropqc_photo_fresh

    New-Db cropqc_photo_compat
    Invoke-Ef cropqc_photo_compat $previous
    $historyBefore = Get-Scalar cropqc_photo_compat 'select count(*) || ''|'' || md5(string_agg("MigrationId" || ''|'' || "ProductVersion", '';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    Invoke-Script cropqc_photo_compat preflight-qc-photo-orientation.sql
    Invoke-Script cropqc_photo_compat apply-qc-photo-orientation-schema.sql
    Invoke-Script cropqc_photo_compat apply-qc-photo-orientation-schema.sql
    Invoke-Gate cropqc_photo_compat
    $historyAfter = Get-Scalar cropqc_photo_compat 'select count(*) || ''|'' || md5(string_agg("MigrationId" || ''|'' || "ProductVersion", '';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyBefore -ne $historyAfter) { throw 'Compatibility path changed migration history.' }
    $compatSignature = Get-Signature cropqc_photo_compat
    if ($freshSignature -ne $compatSignature) { throw "Catalog parity failed.`nFresh: $freshSignature`nCompatibility: $compatSignature" }

    New-Db cropqc_photo_partial
    Invoke-Ef cropqc_photo_partial $previous
    Invoke-Sql cropqc_photo_partial 'alter table "QcPhotos" add column "PresentationRevision" integer not null default 0;'
    Assert-Fails cropqc_photo_partial preflight-qc-photo-orientation.sql

    New-Db cropqc_photo_rollback
    Invoke-Ef cropqc_photo_rollback $previous
    $old = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d cropqc_photo_rollback -c "set cropqc.test_force_qc_photo_orientation_failure='on'" -f "$scriptRoot/apply-qc-photo-orientation-schema.sql" *> $null; $forced = $LASTEXITCODE } finally { $ErrorActionPreference = $old }
    if ($forced -eq 0) { throw 'Forced rollback unexpectedly passed.' }
    $remaining = Get-Scalar cropqc_photo_rollback 'select count(*) from information_schema.columns where table_schema=current_schema() and table_name=''QcPhotos'' and column_name in (''OriginalExifOrientation'',''ManualRotationQuarterTurns'',''PresentationRevision'',''PresentationStorageKey'',''PresentationFileName'',''PresentationContentType'',''PresentationFileSizeBytes'',''PresentationUpdatedAt'');'
    if ($remaining -ne '0') { throw "Forced rollback left $remaining target columns." }

    'Fresh PostgreSQL 18 EF migration and 844-object gate: PASS'
    'Compatibility State A/apply/verify/repeat State B: PASS'
    "Catalog parity: PASS ($freshSignature)"
    "Migration history unchanged: PASS ($historyBefore)"
    'Partial State C and forced rollback: PASS'
} finally {
    Pop-Location
    if (-not $KeepContainer) { $old = $ErrorActionPreference; try { $ErrorActionPreference = 'Continue'; docker rm -f $container *> $null } finally { $ErrorActionPreference = $old } }
}
