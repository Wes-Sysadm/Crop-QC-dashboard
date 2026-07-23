# Production Backup And Restore

Crop QC production changes are blocked until a full backup has been created, uploaded, downloaded again, and verified. A successful command without a verified durable artifact is not a completed backup.

## Architecture

The configured Google Shared Drive backup root contains:

```text
Crop QC Backups/
  Production/
    Daily/
    Weekly/
    Manual/
    PreDeployment/
    Manifests/
  Failed/
```

Every run creates one ZIP package containing:

- a consistent `pg_dump` plain-SQL dump compressed with gzip;
- applied and pending EF migration state, provider, connection result, and key row counts;
- a redacted configuration manifest with secret values omitted;
- the production photo/file relationship inventory and Google Drive accessibility result;
- an internal manifest with the size and SHA-256 of every component.

The uploaded package is downloaded from Google Drive and its byte count, SHA-256, ZIP structure, component checksums, manifest, and database-dump header are revalidated. A small sidecar manifest records the verified package checksum. Only then is the run marked `Succeeded`.

## Standard Commands

Run from the published production image with its normal production environment variables:

```bash
dotnet CropQc.Web.dll --run-backup=predeployment
dotnet CropQc.Web.dll --run-backup=manual
dotnet CropQc.Web.dll --run-backup=scheduled
```

Each command returns nonzero when database connectivity, `pg_dump`, packaging, manifest generation, Google Drive upload, read-back, size, checksum, or structural verification fails. Deployment or mutation must stop on a nonzero result.

Render runs `--run-backup=scheduled` at `30 10 * * *` UTC. This is 02:30 PST and 03:30 PDT in `America/Los_Angeles`, deliberately within the low-activity overnight window. Sunday runs are retained as the ISO-week recovery point instead of creating a duplicate package.

## Retention

- Keep every verified daily, manual, and pre-deployment package for at least 30 calendar days.
- Keep one verified package for each of the most recent 52 ISO weeks.
- Use timestamps and ISO year/week keys, including across year boundaries.
- Prune only after a newer backup has been uploaded and verified.
- Never prune the last verified package.
- Pruning moves expired Google Drive artifacts to trash; it does not permanently delete them immediately.

## Disposable Restore Verification

Never restore over production. Download a selected verified ZIP and use `scripts/verify-backup-restore.ps1` with a newly created disposable PostgreSQL database. The script validates the package, restores the SQL dump, inspects migration history and representative row counts, and can check a separately started disposable application through its health endpoints.

```powershell
./scripts/verify-backup-restore.ps1 `
  -BackupPackage C:\Temp\cropqc-production-weekly-YYYYMMDD-HHMMSS.zip `
  -DisposableDatabaseUrl $env:CROPQC_DISPOSABLE_DATABASE_URL `
  -AppBaseUrl http://localhost:5080
```

The disposable database must not be production, and the script refuses a URL when `ALLOW_PRODUCTION_RESTORE` is set or the database name cannot be identified. Run a full disposable restore at least monthly and record its result in the operational log.

## Recovery

1. Select a verified package from Admin → Backups and download it from the restricted Drive folder.
2. Verify the package SHA-256 against the administration history/sidecar.
3. Restore to a new empty PostgreSQL database first.
4. Confirm `__EFMigrationsHistory`, key counts, receipt queries, Field Sample queries, dashboard startup, `/health`, and `/health/db`.
5. Point a staging application at the restored database and use nonproduction email/storage configuration.
6. Production cutover or data replacement requires a separate reviewed recovery plan and explicit authorization.
