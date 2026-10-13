param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repoRoot
try {
    $connection = $env:CANONICAL_INVENTORY_TEST_POSTGRES
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'Mandatory contracts require disposable localhost PostgreSQL 18; missing fixture is not PASS.' }
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($connection)
    if ($builder['Host'] -notin @('localhost', '127.0.0.1', '::1') -or $builder['Database'] -notmatch '(^|_)test($|_)') {
        throw 'Governance contracts require localhost and a test-marked database.'
    }
    $matrix = Get-Content docs/governance/traceability.json -Raw | ConvertFrom-Json
    $required = @($matrix.rules.tests | Where-Object required | ForEach-Object { "$($_.type).$($_.member)" } | Sort-Object -Unique)
    $selectors = @($required | ForEach-Object { "FullyQualifiedName~$_" }) + @($matrix.structuralSuites | ForEach-Object { "FullyQualifiedName~$_" })
    if (-not $NoBuild) {
        dotnet build tests/CropQc.Api.Tests/CropQc.Api.Tests.csproj --configuration Release
        if ($LASTEXITCODE -ne 0) { throw 'Contract build failed.' }
    }
    $results = Join-Path $repoRoot ("artifacts/governance/" + [Guid]::NewGuid().ToString('N'))
    dotnet test tests/CropQc.Api.Tests/CropQc.Api.Tests.csproj --configuration Release --no-build --settings tests/CropQc.Api.Tests/canonical-postgres.runsettings --filter ($selectors -join '|') --results-directory $results --logger 'trx;LogFileName=contracts.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Mandatory contracts failed.' }
    [xml]$trx = Get-Content (Join-Path $results 'contracts.trx') -Raw
    $tests = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($tests.Count -eq 0 -or @($tests | Where-Object { $_.outcome -ne 'Passed' }).Count -gt 0) { throw 'Missing, failed or skipped mandatory contract.' }
    $definitions = @($trx.SelectNodes("//*[local-name()='UnitTest']"))
    foreach ($suite in $matrix.structuralSuites) {
        if (@($definitions | Where-Object { $_.TestMethod.className -eq $suite }).Count -eq 0) {
            throw "Required architecture suite was not executed: $suite"
        }
    }
    foreach ($expected in $required) {
        $definition = @($definitions | Where-Object { "$($_.TestMethod.className).$($_.TestMethod.name)" -eq $expected })
        if ($definition.Count -eq 0) { throw "Required test was not discovered: $expected" }
        foreach ($entry in $definition) {
            if (@($tests | Where-Object { $_.testId -eq $entry.id -and $_.outcome -eq 'Passed' }).Count -eq 0) { throw "Required test not executed: $expected" }
        }
    }
    Write-Output "Mandatory governance contracts PASS: $($tests.Count), skipped 0. TRX: $results/contracts.trx"
} finally { Pop-Location }
