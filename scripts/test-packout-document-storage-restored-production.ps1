param(
    [Parameter(Mandatory = $true)][string]$BackupDump,
    [int]$HostPort = 55468,
    [switch]$KeepContainer
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "cropqc-packout-doc-restore-$PID"
$database = 'cropqc_packout_docs_restored_production'
$password = 'cropqc-disposable-packout-restore-only'
$expectedMigration = '20260824233548_AddPackoutDocumentStorageMetadata'
$containerScriptRoot = '/tmp/cropqc-packout-documents'

function D {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed." }
}

function Scalar([string]$Sql) {
    $value = $Sql | & docker exec -i $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -At
    if ($LASTEXITCODE -ne 0) { throw 'Restored-production scalar query failed.' }
    return ($value | Select-Object -Last 1).Trim()
}

function Script([string]$Name) {
    & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$containerScriptRoot/$Name"
    if ($LASTEXITCODE -ne 0) { throw "$Name failed against the restored production copy." }
}

function Gate {
    $priorEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $priorProvider = $env:DATABASE_PROVIDER
    $priorConnection = $env:ConnectionStrings__CropQc
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $env:DATABASE_PROVIDER = 'PostgreSql'
        $env:ConnectionStrings__CropQc = "Host=127.0.0.1;Port=$HostPort;Database=$database;Username=postgres;Password=$password"
        & dotnet 'src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll' "--verify-schema=$expectedMigration"
        if ($LASTEXITCODE -ne 0) { throw '698-object gate failed against the restored production copy.' }
    }
    finally { $env:ASPNETCORE_ENVIRONMENT = $priorEnvironment; $env:DATABASE_PROVIDER = $priorProvider; $env:ConnectionStrings__CropQc = $priorConnection }
}

function Fingerprint([string]$Table) {
    if ($Table -notmatch '^[A-Za-z0-9_]+$') { throw "Unsafe table name $Table." }
    return Scalar "select count(*)||'|'||md5(coalesce(string_agg(md5(to_jsonb(t)::text),',' order by md5(to_jsonb(t)::text)),'')) from `"$Table`" t;"
}

if (-not (Test-Path -LiteralPath $BackupDump -PathType Leaf)) { throw "Backup dump was not found: $BackupDump" }
if ((Get-Item -LiteralPath $BackupDump).Length -le 0) { throw 'Backup dump is empty.' }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is required.' }

Push-Location $repositoryRoot
try {
    D -Arguments @('run','--rm','-d','--name',$containerName,'-e',"POSTGRES_PASSWORD=$password",'-p',"${HostPort}:5432",'postgres:18')
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'PostgreSQL 18 did not become ready.' }
    D -Arguments @('exec',$containerName,'createdb','-U','postgres',$database)
    D -Arguments @('cp',(Resolve-Path -LiteralPath $BackupDump).Path,"${containerName}:/tmp/restore.sql.gz")
    & docker exec $containerName sh -c "gunzip -c /tmp/restore.sql.gz | psql -X -v ON_ERROR_STOP=1 -U postgres -d $database" *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Production backup restore failed.' }

    D -Arguments @('exec',$containerName,'mkdir','-p',$containerScriptRoot)
    foreach ($name in @('verify-actual-run-sales-desk-attribution.sql','preflight-packout-document-storage.sql','apply-packout-document-storage-schema.sql','verify-packout-document-storage.sql')) {
        D -Arguments @('cp',(Join-Path $repositoryRoot "scripts\postgresql\$name"),"${containerName}:${containerScriptRoot}/$name")
    }

    $historyBefore = Scalar 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    $actualRunsBefore = Fingerprint 'ActualRuns'
    $adjustmentsBefore = Fingerprint 'RoomInventoryAdjustments'
    $receiptsBefore = Fingerprint 'Receipts'
    $sourcesBefore = Scalar @'
select count(*)||'|'||md5(coalesce(string_agg(md5(concat_ws('|',"Id","PackoutRunId","OriginalFileName","ContentType","FileSizeBytes","Sha256","ParserName",coalesce("ParserVersion",''),coalesce("Confidence"::text,''),coalesce("SafeDiagnostic",''),"ParsedAt"::text)),',' order by "Id"),'')) from "PackoutReportSources";
'@
    $run2 = Scalar @'
select "RunAt"::text||'|expectations='||(select count(*) from "RunExpectations" where "ActualRunId"=2)||'|packouts='||(select count(*) from "PackoutRuns" where "ActualRunId"=2) from "ActualRuns" where "Id"=2;
'@
    if (-not $run2) { throw 'Restored production copy does not contain Actual Run #2.' }

    Script 'preflight-packout-document-storage.sql'
    Script 'apply-packout-document-storage-schema.sql'
    Script 'apply-packout-document-storage-schema.sql'
    Gate

    $historyAfter = Scalar 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
    if ($historyAfter -ne $historyBefore) { throw "Migration history changed: $historyBefore -> $historyAfter" }
    if ((Fingerprint 'ActualRuns') -ne $actualRunsBefore) { throw 'ActualRuns changed during compatibility apply.' }
    if ((Fingerprint 'RoomInventoryAdjustments') -ne $adjustmentsBefore) { throw 'RoomInventoryAdjustments changed during compatibility apply.' }
    if ((Fingerprint 'Receipts') -ne $receiptsBefore) { throw 'Receipts changed during compatibility apply.' }
    if ((Scalar @'
select count(*)||'|'||md5(coalesce(string_agg(md5(concat_ws('|',"Id","PackoutRunId","OriginalFileName","ContentType","FileSizeBytes","Sha256","ParserName",coalesce("ParserVersion",''),coalesce("Confidence"::text,''),coalesce("SafeDiagnostic",''),"ParsedAt"::text)),',' order by "Id"),'')) from "PackoutReportSources";
'@) -ne $sourcesBefore) { throw 'Existing Packout report source metadata changed during compatibility apply.' }

    Write-Output "Restored production Actual Run #2: $run2"
    Write-Output "Restored production migration history unchanged: PASS ($historyBefore)"
    Write-Output "Operational fingerprints unchanged: PASS (ActualRuns $actualRunsBefore; Adjustments $adjustmentsBefore; Receipts $receiptsBefore; Packout sources $sourcesBefore)"
    Write-Output 'Restored production State A/apply/repeat State B/verifier/698-object gate/readiness: PASS'
}
finally {
    Pop-Location
    if (-not $KeepContainer) {
        $prior = $ErrorActionPreference
        try { $ErrorActionPreference = 'Continue'; & docker rm -f $containerName *> $null }
        finally { $ErrorActionPreference = $prior }
    }
}
