# Architecture

## Technology Direction

- .NET/C# solution.
- Render Web Service target for the web dashboard and API.
- Render Postgres as the target production database for structured data.
- SQL Server LocalDB / SQL Server remains supported for local development while PostgreSQL migration work is completed.
- Google Shared Drive as the target store for photos and attachments.
- Windows desktop QC Station app.
- SQLite local cache for later offline QC Station data.
- Gmail API user-delegated sending for QC Summary email.

## Project Boundaries

- `CropQc.Web` hosts the web dashboard user experience.
- `CropQc.Api` exposes API endpoints for the dashboard and QC Station.
- `CropQc.QcStation` is the Windows desktop capture app boundary.
- `CropQc.Shared` contains shared contracts, constants, and cross-project types.
- `CropQc.Data` contains the EF Core data access boundary. It can be configured for SQL Server or PostgreSQL.

## Storage Boundaries

The database stores structured data, including receipts, samples, fruit rows, measurements, defects, photo metadata, email status, permissions, and audit records. The target production database is Render Postgres. SQL Server remains the current local/dev-compatible provider until the PostgreSQL migration path is completed.

Photos and attachments are stored through the file storage service boundary. Local development can use the local provider, and Render can use the Google Drive provider. The Google Drive provider writes files under the configured root folder and the database stores metadata and stable references only, not binary photo content.

## Retention Boundary

Database records are retained indefinitely by default. There is no automatic deletion or purge of receipts, samples, fruit readings, fruit defects, photo metadata, audit logs, email logs, users, roles, rooms, varieties, or other master data.

Photos and attachments must be retained for at least 3 crop years after the current crop year. The current `PhotoRetentionCropYearsAfterCurrent` configuration value is a planning value only; no automatic photo deletion currently runs.

Admin-reviewed archive/delete workflows are future work. Until that workflow exists, retention actions must not run automatically.

## Environment Boundary

Production and staging/test are separate operational environments. Production runs real users, receipts, samples, photos, emails, and QC Station connections. Staging/test runs fake data only and must be visibly labeled with `TEST SITE — DO NOT ENTER REAL QC DATA`. The app reads `AppEnvironment__Kind` (`Production`, `Staging`, or `Development`) and `AppEnvironment__DisplayName` to drive the visible label and production safety warnings.

Production and staging must use separate Postgres databases, Google Drive photo folders, Google Drive backup folders, OAuth redirect URIs, email recipients, and QC Station configs/API keys. No staging service should point at a production database or production Drive folder.

## Production Data Safety Boundary

Future revisions must carry production data forward. Production changes should prefer additive migrations, backfill data before enforcing required fields, and keep old records readable after schema changes. Do not run destructive production migrations, table drops, column drops, seed/reset jobs, or hard purge actions without an explicit documented plan, a current backup, and post-deploy verification. Admin Data Cleanup is allowed only for configured cleanup emails, requires typed confirmation, and must audit every cleanup action.

## Backup Boundary

Google Drive is the backup target. Admin -> Backups reports the configured provider/folder, lets Admins set the Google Drive backup folder by folder URL or ID, shows the parsed folder ID, shows last backup status, production safety warnings, and manual actions. For a URL such as `https://drive.google.com/drive/folders/0AJIU41AM__WNUk9PVA?...`, the saved folder ID is `0AJIU41AM__WNUk9PVA`. The backup workflow produces a PostgreSQL logical dump when `pg_dump` is available, a non-secret configuration snapshot, and a Google Drive photo/storage manifest. Configuration backups must not contain OAuth tokens, Google service account JSON, client secrets, API keys, station keys, or Gmail credentials. If `pg_dump` is missing from the runtime, the app warns and the deployment process must use Render/Postgres backups or a PostgreSQL-tools worker for database dumps.

## Room Inventory Boundary

Room Summary is a current-state view derived from receipts, samples, fruit readings, and additive room depletion records. Receipts and QC samples remain the historical source of what was received, sampled, photographed, emailed, and captured by QC Station. Depletion records represent bins sent from a room to the packing line and are used to remove those bins/lots from current room rollups without deleting or mutating historical QC data.

Dashboard Room Summary is grouped by facility/location. The default view shows rooms with fruit. Operators can filter by `All`, `MCD`, `WP`, `EBS`, and `DH`; EBS rooms are further grouped/filterable as `Evans`, `Lamb`, and `BM` where room naming identifies those locations. Empty rooms are still available through the Empty/All filter.

Current room inventory is receipt-driven. Receiving/receipt records add current bins and lots to the exact room on the receipt. Door and Lot samples do not create room inventory; they are observational quality checks against fruit already associated with a receipt/lot/room. Door and Lot samples can contribute pressure, starch, defect, and month-over-month quality trends, but they must not increase current bins or current lot counts.

Room bin and lot counts must be scoped to the exact room. Current bins are receipt bins for that room minus non-voided depletion records for the same receipt/room. Current lots are current receipt lots in that exact room. Bins/lots in other rooms, depleted bins/lots, and observational Door/Lot samples are excluded from current inventory counts.

Grower lots are managed under Master Data -> Grower Lots. In user-facing screens, `Grower` means the grower/orchard name, `Lot #` is the lot number formerly stored in some code paths as `GrowerNumber`, and `Pool Start` is the first pool characters retained for future final-results analysis. Inactive grower rows are hidden from normal receipt/sample selectors. Receipt inventory remains the current-room source; Grower Lots is master data for selecting and preserving grower/lot identity. The Grower Lots import workflow accepts CSV headers such as `Grower`, `GrowerName`, `#`, `Grower Number`, `Lot #`, `Lot Number`, `POOL Starts`, `Pool Starts`, `Pool Start`, and `PoolCode`; it previews adds/updates/unchanged/conflicts/invalid rows before applying, matches primarily by Lot #, marks Growers starting with `INACTIVE` inactive, and never deletes production rows missing from the upload.

Managers and Admins can create depletion records, true up current lot/bin counts, and void incorrect depletion records with a reason. Voiding is reversible for current-room calculations because voided records are retained but ignored by active inventory. Every receiving add, depletion, manual true-up, correction, and void/reversal creates a `RoomInventoryAdjustment` with old count when available, delta, new count, adjustment type, reason/note, user, timestamp, and related receipt/depletion context. Depletion and bin count history remain visible in room drill-downs and must be audited. Do not delete receipts or samples to remove fruit from room summaries.

EBS starting inventory can be imported from `src/CropQc.Web/Data/Seed/ebs-starting-room-inventory.csv` or an uploaded CSV under Admin -> Current Lots. The import is non-destructive and idempotent: it never deletes production rows, matches by facility + Crop QC room + Compu-Tech room code + Lot # + variety + source, and writes `RoomInventoryAdjustment` rows with `AdjustmentType=StartingInventoryImport`. Crop QC room names are the user-facing names (`Evans-5`, `Evans-12`, `Evans-01`, `BM-4`, `BM-1`, `Lamb-17`). Compu-Tech short room codes are preserved separately as external matching codes (`evanca05`, `evanca12`, `Evanca01`, `Blueca04`, `blueca01`, `Lambca17`) and are matched case-insensitively. EBS sub-location comes from the Crop QC room mapping: Evans, BM, or Lamb. Lot # values are matched to Grower Lots where possible so Grower and Pool Start flow into room inventory; unknown lots import with warnings instead of crashing. Dashboard room summaries include these adjustment-only current inventory rows, but they do not create historical QC sample data or fake receipts.

Room Summary and room drill-downs can highlight the weakest current lot. The reason must be explainable to the user, such as low pressure compared with other current lots, pressure drop month-over-month, defects, starch, or pressure variance. Empty rooms show `Empty`, and current lots with no QC trend data should show no-data language rather than fake quality values.

## Email Boundary

QC Summary email sends through the Gmail API using the logged-in Google Workspace user's delegated `gmail.send` permission when `Email__Provider=GmailUser` is configured. Allowed sending domains are configured with `Authentication__AllowedGoogleDomains`; current company domains are `fruitandland.com`, `earlbrownandsons.com`, and `wp-packingllc.com`. QC recipients are configured with `Email__QcDefaultRecipients`; the current test value is `rob@earlbrownandsons.com,wes@fruitandland.com`. The sender is the logged-in user, not a shared SMTP account. Refresh tokens are encrypted with ASP.NET Core Data Protection and are not logged.

Admin Downloads links only to the Google Drive-hosted master folder configured by `Downloads__MasterFolderUrl`; individual MSI, FTADLL, and dependency file links are kept inside that folder instead of rendered as separate buttons. Station-specific QC Station config JSON remains under Admin -> QC Stations. Admin Data Cleanup is additionally restricted by `DataCleanup__AllowedEmails` so Admin role alone is not enough. Normal web pages should use responsive layouts without page-level horizontal scrolling; action groups must wrap or stack instead of hiding controls off-screen. The Admin top-bar menu opens on hover for desktop users and remains clickable/tappable for touch devices.

## Offline Boundary

The QC Station must be designed around offline capture and later sync. The station will use SQLite or another durable local queue as a local cache, then sync structured data and file metadata to the Render/Postgres backend and Google Drive storage when internet connectivity returns. The web site alone should not be treated as the offline workflow; see `docs/offline-sync-design.md` for the planned station-side queue, idempotency, conflict, and sync-status model.

## Audit Boundary

All create, edit, delete, send, import, and export actions require audit logging. Audit records should identify the actor, action, timestamp, target entity, relevant before/after context, and source application.
