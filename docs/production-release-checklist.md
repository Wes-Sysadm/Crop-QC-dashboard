# Production Release Checklist

Use this checklist before every production deploy. Production data is real company data and must be retained through future revisions.

- PR targets `main`.
- Build passes.
- Tests pass.
- No destructive migration is included, or the destructive migration has an explicit reviewed migration and backup plan.
- Production backup completed before deploy.
- Admin Backups shows recent database/config/photo-manifest success or an explicit documented backup alternative.
- Render production service deploys from `main`.
- Production service uses the production Postgres database.
- Production service uses production Google Drive photo and backup folders.
- Production service has `AppEnvironment__Kind=Production`.
- Production service has `Backups__Enabled=true` and `Backups__GoogleDriveFolderId` configured.
- Health endpoint passes: `/health`.
- Database health endpoint passes: `/health/db`.
- Login works.
- Dashboard loads.
- Receipts page loads.
- Daily QC loads.
- Sample detail loads.
- QC email preview works.
- QC Station config download works from Admin -> QC Stations.
- Google Drive photo upload works.
- Admin Downloads opens the Google Drive hosted-files folder.
- No test data seed/reset setting is enabled in production.
