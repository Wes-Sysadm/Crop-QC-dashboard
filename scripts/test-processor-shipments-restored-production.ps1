param(
 [Parameter(Mandatory=$true)][string]$RestoreSql,
 [int]$HostPort=55454,
 [switch]$KeepContainer)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$restore=(Resolve-Path -LiteralPath $RestoreSql).Path
$container="cropqc-processor-restore-$PID"
$database='cropqc_processor_restored_copy'
$password='cropqc-disposable-processor-restore-only'
$scriptRoot='/tmp/cropqc-processor-restore'
function D { param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments) & docker @Arguments;if($LASTEXITCODE-ne 0){throw "docker failed: $($Arguments -join ' ')"} }
function Scalar([string]$sql){$r=$sql|& docker exec -i $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -At;if($LASTEXITCODE-ne 0){throw 'Scalar SQL failed'};return ($r|Select-Object -Last 1).Trim()}
Push-Location $root
try{
 D -Arguments @('run','--rm','-d','--name',$container,'-e',"POSTGRES_PASSWORD=$password",'-p',"${HostPort}:5432",'postgres:18')
 $ready=$false;for($i=0;$i-lt 30;$i++){& docker exec $container pg_isready -U postgres *> $null;if($LASTEXITCODE-eq 0){$ready=$true;break};Start-Sleep -Milliseconds 500};if(-not$ready){throw 'PostgreSQL 18 not ready'}
 D -Arguments @('exec',$container,'createdb','-U','postgres',$database)
 D -Arguments @('exec',$container,'mkdir','-p',$scriptRoot)
 D -Arguments @('cp',$restore,"${container}:${scriptRoot}/restore.sql")
 foreach($n in @('preflight-room-treatment-tracking.sql','verify-room-treatment-tracking.sql','preflight-treatment-report-attachments.sql','apply-treatment-report-attachments-schema.sql','verify-treatment-report-attachments.sql','preflight-receiving-treatment-applications.sql','apply-receiving-treatment-applications-schema.sql','verify-receiving-treatment-applications.sql','preflight-receiving-treatment-chemical-levels.sql','apply-receiving-treatment-chemical-levels.sql','verify-receiving-treatment-chemical-levels.sql','preflight-processor-shipments.sql','apply-processor-shipments-schema.sql','verify-processor-shipments.sql')){D -Arguments @('cp',(Join-Path $root "scripts\postgresql\$n"),"${container}:${scriptRoot}/$n")}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/restore.sql" *> $null;if($LASTEXITCODE-ne 0){throw 'Run restore failed'}
 $historyBefore=Scalar 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";'
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/apply-treatment-report-attachments-schema.sql";if($LASTEXITCODE-ne 0){throw 'Restored prerequisite attachment compatibility apply failed'}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/preflight-receiving-treatment-applications.sql";if($LASTEXITCODE-ne 0){throw 'Restored Receiving treatment preflight failed'}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/apply-receiving-treatment-applications-schema.sql";if($LASTEXITCODE-ne 0){throw 'Restored Receiving treatment compatibility apply failed'}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/preflight-receiving-treatment-chemical-levels.sql";if($LASTEXITCODE-ne 0){throw 'Restored Receiving treatment chemical dry run failed'}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/apply-receiving-treatment-chemical-levels.sql";if($LASTEXITCODE-ne 0){throw 'Restored Receiving treatment chemical alignment failed'}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/preflight-processor-shipments.sql";if($LASTEXITCODE-ne 0){throw 'Restored preflight failed'}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/apply-processor-shipments-schema.sql";if($LASTEXITCODE-ne 0){throw 'Restored apply failed'}
 & docker exec $container psql -X -v ON_ERROR_STOP=1 -U postgres -d $database -f "$scriptRoot/verify-processor-shipments.sql";if($LASTEXITCODE-ne 0){throw 'Restored verify failed'}
 $historyAfter=Scalar 'select count(*)||''|''||md5(string_agg("MigrationId"||''|''||"ProductVersion",'';'' order by "MigrationId")) from "__EFMigrationsHistory";';if($historyBefore-ne$historyAfter){throw 'Migration history changed'}
 $connection="Host=127.0.0.1;Port=$HostPort;Database=$database;Username=postgres;Password=$password"
 $oldP=$env:DATABASE_PROVIDER;$oldC=$env:ConnectionStrings__CropQc;$oldE=$env:ASPNETCORE_ENVIRONMENT;$oldR=$env:PROCESSOR_SHIPMENT_RESTORE_CONNECTION_STRING
 try{
  $env:DATABASE_PROVIDER='PostgreSql';$env:ConnectionStrings__CropQc=$connection;$env:ASPNETCORE_ENVIRONMENT='Production'
  & dotnet 'src\CropQc.Web\bin\Debug\net9.0\CropQc.Web.dll' '--verify-schema=20260821031442_AddProcessorShipments';if($LASTEXITCODE-ne 0){throw 'Restored 619-object gate failed'}
  $env:PROCESSOR_SHIPMENT_RESTORE_CONNECTION_STRING=$connection
  & dotnet test 'tests\CropQc.Api.Tests\CropQc.Api.Tests.csproj' --no-build --filter 'FullyQualifiedName~Restored_production_postgresql_processor_workflow_when_requested' --logger 'console;verbosity=minimal';if($LASTEXITCODE-ne 0){throw 'Restored Processor Shipment workflow failed'}
 }finally{$env:DATABASE_PROVIDER=$oldP;$env:ConnectionStrings__CropQc=$oldC;$env:ASPNETCORE_ENVIRONMENT=$oldE;$env:PROCESSOR_SHIPMENT_RESTORE_CONNECTION_STRING=$oldR}
 "Run restore PostgreSQL 18 compatibility/gate/workflow: PASS";"Migration history unchanged: PASS ($historyBefore)"
}finally{Pop-Location;if(-not$KeepContainer){$old=$ErrorActionPreference;try{$ErrorActionPreference='Continue';& docker rm -f $container *> $null}finally{$ErrorActionPreference=$old}}}
