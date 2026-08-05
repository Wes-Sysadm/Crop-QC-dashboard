param(
    [Parameter(Mandatory = $true)][string[]]$DeployedReports,
    [Parameter(Mandatory = $true)][string[]]$MainReports,
    [Parameter(Mandatory = $true)][string[]]$CandidateReports,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [double]$MinimumDeployedImprovementPercent = 10
)

$ErrorActionPreference = "Stop"

function Read-Reports([string[]]$paths, [string]$label) {
    if ($paths.Count -lt 3) { throw "$label requires at least three repeated reports." }
    $reports = foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path)) { throw "$label report does not exist: $path" }
        Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    }
    $datasets = @($reports | Select-Object -ExpandProperty Database -Unique)
    if ($datasets.Count -ne 1) { throw "$label reports do not use one matched dataset." }
    $profiles = @($reports | Select-Object -ExpandProperty Profile -Unique)
    if ($profiles.Count -ne 1 -or $profiles[0] -ne "core") { throw "$label reports must use the core route profile." }
    return @($reports)
}

function Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { throw "Cannot calculate a median for an empty set." }
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}

function Stats([double[]]$values) {
    [pscustomobject]@{
        Samples = $values.Count
        Min = ($values | Measure-Object -Minimum).Minimum
        Median = Median $values
        Max = ($values | Measure-Object -Maximum).Maximum
        Range = (($values | Measure-Object -Maximum).Maximum - ($values | Measure-Object -Minimum).Minimum)
    }
}

function Phase-Values($reports, [string]$phaseName, [string]$metric) {
    [double[]]@($reports | ForEach-Object {
        $phase = @($_.Phases | Where-Object Name -eq $phaseName)
        if ($phase.Count -ne 1) { throw "Expected one $phaseName phase in report for commit $($_.Commit)." }
        switch ($metric) {
            "AllocatedPerRequest" { [double]$phase[0].TotalAllocatedBytes / [double]$phase[0].RequestCount }
            default { [double]$phase[0].$metric }
        }
    })
}

function Assert-NoiseBounded($mainStats, $candidateStats, [string]$description, [string]$metric) {
    $absoluteNoiseFloor = switch ($metric) {
        "PeakWorkingSetBytes" { 8MB }
        "PostIdleWorkingSetBytes" { 8MB }
        "PostIdleHeapBytes" { 2MB }
        "PostIdleLohBytes" { 1MB }
        default { 0 }
    }
    $allowedNoise = [math]::Max(
        [double]$absoluteNoiseFloor,
        [math]::Max([double]$mainStats.Range, [double]$mainStats.Median * 0.03))
    if ([double]$candidateStats.Median -gt [double]$mainStats.Median + $allowedNoise) {
        throw "$description materially regressed: candidate median $($candidateStats.Median), main median $($mainStats.Median), allowed noise $allowedNoise."
    }
}

$deployed = Read-Reports $DeployedReports "deployed"
$main = Read-Reports $MainReports "main"
$candidate = Read-Reports $CandidateReports "candidate"
$datasets = @($deployed.Database + $main.Database + $candidate.Database | Select-Object -Unique)
if ($datasets.Count -ne 1) { throw "All builds must use the exact same restored-production dataset." }

$phaseNames = @(
    "rooms-sequential-100",
    "current-inventory-sequential-100",
    "mixed-concurrency-2",
    "mixed-concurrency-4",
    "mixed-concurrency-8"
)
$metricNames = @(
    "AllocatedPerRequest",
    "PeakWorkingSetBytes",
    "PostIdleWorkingSetBytes",
    "PostIdleHeapBytes",
    "PostIdleLohBytes",
    "ResponseBytes"
)

$comparisons = foreach ($phaseName in $phaseNames) {
    $metrics = [ordered]@{}
    foreach ($metricName in $metricNames) {
        $deployedStats = Stats (Phase-Values $deployed $phaseName $metricName)
        $mainStats = Stats (Phase-Values $main $phaseName $metricName)
        $candidateStats = Stats (Phase-Values $candidate $phaseName $metricName)
        if ($metricName -notin @("ResponseBytes")) {
            Assert-NoiseBounded $mainStats $candidateStats "$phaseName $metricName" $metricName
        }
        $metrics[$metricName] = [pscustomobject]@{
            Deployed = $deployedStats
            Main = $mainStats
            Candidate = $candidateStats
        }
    }
    [pscustomobject]@{ Phase = $phaseName; Metrics = [pscustomobject]$metrics }
}

foreach ($phaseName in @("rooms-sequential-100", "current-inventory-sequential-100")) {
    $deployedAlloc = Stats (Phase-Values $deployed $phaseName "AllocatedPerRequest")
    $candidateAlloc = Stats (Phase-Values $candidate $phaseName "AllocatedPerRequest")
    $requiredMaximum = [double]$deployedAlloc.Median * (1 - ($MinimumDeployedImprovementPercent / 100))
    if ([double]$candidateAlloc.Median -gt $requiredMaximum) {
        throw "$phaseName is not at least $MinimumDeployedImprovementPercent percent better than the deployed build."
    }

    $sizes = @($deployed + $main + $candidate | ForEach-Object {
        $phase = $_.Phases | Where-Object Name -eq $phaseName
        [long]$phase.ResponseBytes
    } | Select-Object -Unique)
    if ($sizes.Count -ne 1) { throw "$phaseName response sizes differ across matched builds." }
}

$candidateConcurrencyPeak = (Phase-Values $candidate "mixed-concurrency-8" "PeakWorkingSetBytes" | Measure-Object -Maximum).Maximum
if ($candidateConcurrencyPeak -gt 384MB) {
    throw "Candidate concurrency-8 peak $candidateConcurrencyPeak exceeds the 384 MiB warning threshold."
}

$plateau = foreach ($label in @("Deployed", "Main", "Candidate")) {
    $reports = switch ($label) { "Deployed" { $deployed } "Main" { $main } default { $candidate } }
    $reportResults = foreach ($report in $reports) {
        $batches = @($report.RetainedMemoryPlateau)
        if ($batches.Count -ne 5) { throw "$label report for $($report.Commit) does not contain five plateau batches." }
        $first = [double]$batches[0].PostIdleWorkingSetBytes
        $last = [double]$batches[-1].PostIdleWorkingSetBytes
        $max = [double](($batches.PostIdleWorkingSetBytes | Measure-Object -Maximum).Maximum)
        [pscustomobject]@{ Commit=$report.Commit; First=$first; Last=$last; Max=$max; Growth=$last-$first }
    }
    [pscustomobject]@{ Build=$label; Runs=@($reportResults) }
}

foreach ($run in @($plateau | Where-Object Build -eq "Candidate" | Select-Object -ExpandProperty Runs)) {
    $allowedGrowth = [math]::Max(32MB, [double]$run.First * 0.10)
    if ($run.Growth -gt $allowedGrowth) { throw "Candidate retained memory did not plateau for $($run.Commit)." }
    if ($run.Max -gt 384MB) { throw "Candidate retained-memory workload exceeded 384 MiB." }
}

$result = [pscustomobject]@{
    Dataset = $datasets[0]
    CapturedAtUtc = [DateTimeOffset]::UtcNow
    SampleCountPerBuild = $candidate.Count
    MinimumDeployedImprovementPercent = $MinimumDeployedImprovementPercent
    Comparisons = @($comparisons)
    Plateau = @($plateau)
    ResultEquivalence = "Rooms and Current Inventory response bytes are identical across all matched runs."
    Status = "Passed"
}

$parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath
$result
