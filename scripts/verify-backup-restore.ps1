param(
    [Parameter(Mandatory = $true)][string]$BackupPackage,
    [Parameter(Mandatory = $true)][string]$DisposableDatabaseUrl,
    [string]$AppBaseUrl
)

$ErrorActionPreference = "Stop"
$resolvedPackage = (Resolve-Path -LiteralPath $BackupPackage).Path
if ($DisposableDatabaseUrl -notmatch '/([^/?]+)(?:\?|$)') { throw "The disposable database name could not be identified." }
$databaseName = $Matches[1]
if ($databaseName -match '(^|[-_])prod(uction)?($|[-_])' -or $env:ALLOW_PRODUCTION_RESTORE) { throw "Refusing to restore to a database that may be production." }

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cropqc-restore-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    Expand-Archive -LiteralPath $resolvedPackage -DestinationPath $tempRoot
    $manifestPath = Join-Path $tempRoot "backup-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "backup-manifest.json is missing." }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    foreach ($component in $manifest.components) {
        $path = Join-Path $tempRoot $component.name
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing component $($component.name)." }
        $item = Get-Item -LiteralPath $path
        if ($item.Length -ne $component.sizeBytes) { throw "Size mismatch for $($component.name)." }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $component.sha256) { throw "Checksum mismatch for $($component.name)." }
    }

    $dump = Get-ChildItem -LiteralPath $tempRoot -Filter "*.sql.gz" | Select-Object -Single
    $sqlPath = Join-Path $tempRoot "restore.sql"
    $source = [System.IO.File]::OpenRead($dump.FullName)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new($source, [System.IO.Compression.CompressionMode]::Decompress)
        try {
            $target = [System.IO.File]::Create($sqlPath)
            try { $gzip.CopyTo($target) } finally { $target.Dispose() }
        } finally { $gzip.Dispose() }
    } finally { $source.Dispose() }

    & psql $DisposableDatabaseUrl -v ON_ERROR_STOP=1 -f $sqlPath
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL restore failed." }
    & psql $DisposableDatabaseUrl -v ON_ERROR_STOP=1 -c 'SELECT count(*) AS migrations FROM "__EFMigrationsHistory";' -c 'SELECT count(*) AS receipts FROM "Receipts";' -c 'SELECT count(*) AS field_samples FROM "QcSamples" WHERE "ReceiptId" IS NULL;' -c 'SELECT count(*) AS receipt_samples FROM "QcSamples" WHERE "ReceiptId" IS NOT NULL;'
    if ($LASTEXITCODE -ne 0) { throw "Restore verification queries failed." }
    if ($AppBaseUrl) {
        Invoke-RestMethod "$($AppBaseUrl.TrimEnd('/'))/health" | Out-Null
        $dbHealth = Invoke-RestMethod "$($AppBaseUrl.TrimEnd('/'))/health/db"
        if ($dbHealth.status -ne "OK") { throw "Disposable application database health failed." }
    }
    Write-Host "Disposable restore verification passed for $databaseName."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
