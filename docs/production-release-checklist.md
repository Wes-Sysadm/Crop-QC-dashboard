# Production Release Checklist

Use this checklist before every production deploy. Production data is real company data and must be retained through future revisions.

- PR targets `main`.
- Build passes.
- Tests pass.
- No destructive migration is included, or the destructive migration has an explicit reviewed migration and backup plan.
- The Render pre-deploy schema gate passes for the application version being activated.
- When the packout reconciliation schema is required, follow `docs/production-packout-schema-update.md`; do not run the historical EF migration chain against an EnsureCreated/compatibility production database.
- `dotnet CropQc.Web.dll --run-backup=predeployment` completed with exit code 0 immediately before the first production-changing action.
- Admin Backups shows a verified package with filename, durable Google Drive location, timestamp, size, SHA-256, deployed commit, and backup-run ID.
- The package was read back and passed size, checksum, archive, component-manifest, and database-dump validation; issuing an export/upload command alone does not satisfy this check.
- Render production service deploys from `main`.
- Production service uses the production Postgres database.
- Production service uses production Google Drive photo and backup folders.
- Production service has `AppEnvironment__Kind=Production`.
- Production service has `Backups__Enabled=true` and `Backups__GoogleDriveFolderId` configured.
- Health endpoint passes: `/health`.
- Database health endpoint passes: `/health/db`.
- Login works.
- Dashboard loads.
- Dashboard Room Summary shows all rooms, including empty rooms.
- Room drill-down opens and depletion history/current lot breakout is visible.
- Receipts page loads.
- Daily QC loads.
- Sample detail loads.
- QC email preview works.
- QC Station config download works from Admin -> QC Stations.
- Google Drive photo upload works.
- Admin Downloads opens the Google Drive hosted-files folder.
- No test data seed/reset setting is enabled in production.
