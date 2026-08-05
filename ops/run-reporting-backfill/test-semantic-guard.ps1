[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9_-]+$')]
    [string] $ContainerName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9_]+$')]
    [string] $SourceDatabase
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$containerEnvironment = docker inspect --format '{{json .Config.Env}}' $ContainerName | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect disposable PostgreSQL container $ContainerName."
}

$postgresUserEntry = $containerEnvironment | Where-Object { $_ -like 'POSTGRES_USER=*' } | Select-Object -First 1
$postgresUser = if ([string]::IsNullOrWhiteSpace($postgresUserEntry)) {
    'postgres'
}
else {
    ($postgresUserEntry -split '=', 2)[1]
}

$containerPackagePath = '/tmp/run-reporting-semantic-guard-tests'
docker cp $PSScriptRoot ($ContainerName + ':' + $containerPackagePath) | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not copy the reporting package into the disposable container.'
}

function Invoke-Postgres {
    param(
        [Parameter(Mandatory)] [string] $Database,
        [Parameter(Mandatory)] [string] $Sql
    )

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = ($Sql + [Environment]::NewLine) |
            docker exec -i $ContainerName psql -X -v ON_ERROR_STOP=1 -P pager=off -U $postgresUser -d $Database -f - 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($exitCode -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }

    return $output
}

function Invoke-PackageFile {
    param(
        [Parameter(Mandatory)] [string] $Database,
        [Parameter(Mandatory)] [string] $File
    )

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = docker exec -w $containerPackagePath $ContainerName psql -X -v ON_ERROR_STOP=1 -P pager=off -U $postgresUser -d $Database -f $File 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function New-ScenarioDatabase {
    param([Parameter(Mandatory)] [string] $Scenario)

    $safeScenario = ($Scenario -replace '[^a-zA-Z0-9]', '').ToLowerInvariant()
    $database = ('cropqc_sg_' + $safeScenario)
    if ($database.Length -gt 55) {
        $database = $database.Substring(0, 55)
    }
    if ($database -eq $SourceDatabase -or $database -notmatch '^cropqc_sg_[a-z0-9]+$') {
        throw "Unsafe disposable database name: $database"
    }

    docker exec $ContainerName dropdb --if-exists --force -U $postgresUser $database | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clear disposable scenario database $database."
    }
    docker exec $ContainerName createdb -U $postgresUser -T $SourceDatabase $database | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone $SourceDatabase to $database."
    }

    return $database
}

function Remove-ScenarioDatabase {
    param([Parameter(Mandatory)] [string] $Database)

    if ($Database -eq $SourceDatabase -or $Database -notmatch '^cropqc_sg_[a-z0-9]+$') {
        throw "Refusing to drop unsafe database name: $Database"
    }
    docker exec $ContainerName dropdb --if-exists --force -U $postgresUser $Database | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not remove disposable scenario database $Database."
    }
}

function Test-Scenario {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Mutation,
        [Parameter(Mandatory)] [bool] $ShouldPass
    )

    $database = New-ScenarioDatabase -Scenario $Name
    try {
        Invoke-Postgres -Database $database -Sql $Mutation | Out-Null
        $preflight = Invoke-PackageFile -Database $database -File 'preflight.sql'
        $passed = $preflight.ExitCode -eq 0
        if ($passed -ne $ShouldPass) {
            throw "Scenario '$Name' expected pass=$ShouldPass but preflight exit code was $($preflight.ExitCode).`n$($preflight.Output -join [Environment]::NewLine)"
        }
        [pscustomobject]@{ Scenario = $Name; ExpectedPass = $ShouldPass; Result = 'Passed' }
    }
    finally {
        Remove-ScenarioDatabase -Database $database
    }
}

$unrelatedNewUserMutation = @'
INSERT INTO "Users" ("Id","Email","DisplayName","IsActive","CreatedAt","UpdatedAt","Domain","EmploymentFacility")
SELECT MAX("Id") + 1000, 'semantic-guard-test@example.invalid', 'Semantic Guard Test', true, now(), now(), '', 'Unassigned'
FROM "Users";
'@

$unrelatedLoginMutation = @'
UPDATE "Users"
SET "LastLoginAt"=COALESCE("LastLoginAt", now()) + interval '1 second',
    "UpdatedAt"=COALESCE("UpdatedAt", now()) + interval '1 second'
WHERE "Id"=(SELECT MIN("Id") FROM "Users" WHERE "Id" NOT IN (2,8));
'@

$newAuthoritativeLineMutation = @'
INSERT INTO "RoomInventoryAdjustments"
SELECT (jsonb_populate_record(NULL::"RoomInventoryAdjustments",
    to_jsonb(source) || jsonb_build_object(
        'Id', 9999001,
        'InventoryOperationKey', 'semantic-guard:new-authoritative-line'))).*
FROM "RoomInventoryAdjustments" AS source
WHERE source."Id"=(SELECT "InventoryAdjustmentId" FROM "BinsRunEntries" WHERE "Id"=39);

INSERT INTO "BinsRunEntries"
SELECT (jsonb_populate_record(NULL::"BinsRunEntries",
    to_jsonb(source) || jsonb_build_object(
        'Id', 9999001,
        'InventoryAdjustmentId', 9999001,
        'ReportingFacilityWarehouseId', NULL,
        'ReportingFacilityCodeSnapshot', NULL,
        'ReportingFacilityAssignmentSource', NULL,
        'ReportingFacilityAssignedAt', NULL,
        'ReportingFacilityAssignedByUserId', NULL,
        'ReportingCropYearSnapshot', NULL,
        'ReportingFruitProfileIdSnapshot', NULL,
        'ReportingVarietyCodeSnapshot', NULL,
        'ProductionTypeSnapshot', NULL,
        'IsOrganicSnapshot', NULL,
        'GrowerNumberSnapshot', NULL))).*
FROM "BinsRunEntries" AS source
WHERE source."Id"=39;
'@

$protectedRelationshipMutation = @'
UPDATE "BinsRunEntries" AS entry
SET "RoomId"=(SELECT MIN(room."Id") FROM "Rooms" AS room WHERE room."Id"<>entry."RoomId")
WHERE entry."Id"=28;
'@

$results = @()
$results += Test-Scenario 'target-last-login' 'UPDATE "Users" SET "LastLoginAt"=COALESCE("LastLoginAt", now()) + interval ''1 second'' WHERE "Id"=8;' $true
$results += Test-Scenario 'target-updated-at' 'UPDATE "Users" SET "UpdatedAt"=COALESCE("UpdatedAt", now()) + interval ''1 second'' WHERE "Id"=2;' $true
$results += Test-Scenario 'unrelated-new-user' $unrelatedNewUserMutation $true
$results += Test-Scenario 'unrelated-user-login' $unrelatedLoginMutation $true
$results += Test-Scenario 'alexis-email' 'UPDATE "Users" SET "Email"=''changed-alexis@example.invalid'' WHERE "Id"=8;' $false
$results += Test-Scenario 'robert-email' 'UPDATE "Users" SET "Email"=''changed-robert@example.invalid'' WHERE "Id"=2;' $false
$results += Test-Scenario 'alexis-inactive' 'UPDATE "Users" SET "IsActive"=false WHERE "Id"=8;' $false
$results += Test-Scenario 'robert-inactive' 'UPDATE "Users" SET "IsActive"=false WHERE "Id"=2;' $false
$results += Test-Scenario 'alexis-conflicting-facility' 'UPDATE "Users" SET "EmploymentFacility"=''EBS'' WHERE "Id"=8;' $false
$results += Test-Scenario 'robert-conflicting-facility' 'UPDATE "Users" SET "EmploymentFacility"=''WP'' WHERE "Id"=2;' $false
$results += Test-Scenario 'reviewed-run-quantity' 'UPDATE "BinsRunEntries" SET "BinsRun"="BinsRun"+1 WHERE "Id"=28;' $false
$results += Test-Scenario 'run-recording-user' 'UPDATE "ActualRuns" SET "CreatedByUserId"=2 WHERE "Id"=1;' $false
$results += Test-Scenario 'new-authoritative-line' $newAuthoritativeLineMutation $false
$results += Test-Scenario 'protected-relationship' $protectedRelationshipMutation $false

$idempotentDatabase = New-ScenarioDatabase -Scenario 'idempotent-apply'
try {
    $auditCountBefore = (Invoke-Postgres -Database $idempotentDatabase -Sql 'SELECT COUNT(*) FROM "AuditLogs";' | Select-String -Pattern '^\s*\d+\s*$').Line.Trim()
    $apply = Invoke-PackageFile -Database $idempotentDatabase -File 'apply.sql'
    if ($apply.ExitCode -ne 0) {
        throw ($apply.Output -join [Environment]::NewLine)
    }
    $verify = Invoke-PackageFile -Database $idempotentDatabase -File 'verify.sql'
    if ($verify.ExitCode -ne 0) {
        throw ($verify.Output -join [Environment]::NewLine)
    }
    $auditCountAfter = (Invoke-Postgres -Database $idempotentDatabase -Sql 'SELECT COUNT(*) FROM "AuditLogs";' | Select-String -Pattern '^\s*\d+\s*$').Line.Trim()
    if ($auditCountBefore -ne $auditCountAfter) {
        throw "Idempotent apply changed the audit count from $auditCountBefore to $auditCountAfter."
    }
    $results += [pscustomobject]@{ Scenario = 'idempotent-apply'; ExpectedPass = $true; Result = 'Passed' }
}
finally {
    Remove-ScenarioDatabase -Database $idempotentDatabase
}

$results | Format-Table -AutoSize
Write-Host "Semantic guard regression scenarios passed: $($results.Count)."
