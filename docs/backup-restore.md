# Backup And Restore

Production data must be backed up before production deploys and before any migration that could affect live data. The primary backup target is the configured Google Drive backup folder.

## Backup Contents

The backup workflow creates:

- `cropqc-prod-db-YYYYMMDD-HHMMSS.sql.gz`: PostgreSQL logical database dump when `pg_dump` is available in the runtime.
- `cropqc-prod-config-YYYYMMDD-HHMMSS.json`: non-secret configuration snapshot with environment name, provider choices, folder IDs, and feature flags. It must not include OAuth tokens, service account JSON, client secrets, API keys, or station keys.
- `cropqc-prod-photo-manifest-YYYYMMDD-HHMMSS.json`: manifest of photo/file metadata already stored in Google Drive, including receipt/sample references, photo type, Drive file ID, file name, uploaded date, and web URL when available.

Photo binaries are not duplicated by default because the production photo store is already Google Drive. The manifest is the index used to verify and locate files during a restore.

## Restore To A New Database

1. Open the Google Drive backup folder configured by `Backups__GoogleDriveFolderId`.
2. Download the newest `cropqc-prod-db-*.sql.gz`.
3. Create a new empty PostgreSQL database for restore testing or recovery.
4. Decompress the backup:
   ```bash
   gunzip -c cropqc-prod-db-YYYYMMDD-HHMMSS.sql.gz > cropqc-prod-db.sql
   ```
5. Restore into the new database:
   ```bash
   psql "$RESTORE_DATABASE_URL" < cropqc-prod-db.sql
   ```
6. Point a staging Render service at the restored database. Never point staging at the production database.
7. Configure staging Google Drive folders and test-only email recipients.
8. Verify `/health`, `/health/db`, login, Dashboard, Receipts, Daily QC, sample detail, QC email preview, and photo links.

## Photo Manifest Use

Use the latest `cropqc-prod-photo-manifest-*.json` to confirm that each photo record has a Drive file ID and expected receipt/sample reference. If a restored database points to the same production Google Drive files, do not move or delete files during restore testing.

## Safety Rules

- Never restore staging over production.
- Never restore a test database into production without explicit approval.
- Run a fresh production backup before production migrations.
- Verify the restored app in staging before using restored data for recovery.
- Keep Google Drive backup folder access restricted to approved admins.

## If pg_dump Is Unavailable

The Admin Backups page will show: `pg_dump is not available in this runtime. Configure external Render/Postgres backup or run the backup job from a worker with PostgreSQL tools.`

In that case, use Render/Postgres native backups or run the backup job from a worker image that includes PostgreSQL client tools. The app can still produce the non-secret config snapshot and photo manifest.
