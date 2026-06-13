# Requirements

## Product

Crop QC Dashboard supports receiving quality control workflows. MVP 1 is limited to Receiving/QC.

## MVP 1 Functional Scope

- Email/password login.
- Roles: Admin, Manager, QC User, Viewer.
- Admin-configurable roles and permissions.
- Password policy configurable from the dashboard:
  - Yearly reset.
  - Minimum 8 characters.
  - At least 1 uppercase letter.
  - At least 1 lowercase letter.
  - At least 1 number.
  - At least 1 symbol.
- Warehouses: EBS, DH, McDougall, WP.
- Admin-editable warehouses and rooms.
- Fruit profile and variety code table.
- Grade list: W1, W2, W3, W4, WF, US1, US2, USF.
- Admin-editable defect list.
- Starch scale list, admin-editable by fruit profile.
- Apple and pear size conversion tables.
- Receipt entry.
- Receiving sample entry.
- 25-row QC grid.
- Actual sample size can be fewer than 25.
- Completed fruit rows require Pressure 1, Pressure 2, weight in grams, and grade.
- Pressure is recorded in lbs.
- Weight is recorded in grams.
- Starch is per fruit and can be added later.
- Starch is required before QC Summary email can be sent.
- Multiple defects per fruit.
- Two USB cameras per QC station:
  - Bin/truck camera.
  - Sample/starch camera.
- Manual photo upload fallback.
- Required photos before sending QC Summary:
  - At least one bin/truck photo.
  - Sample before cutting photo.
  - Cut fruit photo.
  - Fruit after starch photo.
- QC Summary preview before send.
- One email per receipt.
- No batching.
- QC Summary email sends from the logged-in Google Workspace user through Gmail API when `Email__Provider=GmailUser` is configured. Allowed company domains are `fruitandland.com`, `earlbrownandsons.com`, and `wp-packingllc.com`. During testing, configured recipients are `rob@earlbrownandsons.com,wes@fruitandland.com`.
- Production defaults QC Summary sending to GmailUser unless `Email__Provider=None` is explicitly set. Admin Configuration shows safe email status/diagnostics without exposing Gmail tokens or secrets.
- Reply-To is the user who took the sample.
- Managers and Admins can resend with a reason.
- Daily QC dashboard showing received samples and sent/not-sent/ready/missing status.
- Dashboard cards must click through to filtered receipt/Daily QC lists. Today’s Receiving Samples opens today’s receiving receipts; Samples Ready to Email opens ready-to-send Daily QC; Samples Missing Data shows missing data/photo reasons; Samples Needing Review shows explicit review reasons from configured pressure/starch/defect/variance thresholds or manual Needs Review flags.
- Dashboard includes a Room Summary section backed by every room in master data. It defaults to rooms with fruit and can filter to Empty rooms or All rooms. Facilities filter as All, MCD, WP, EBS, and DH; EBS rooms are grouped/filterable as Evans, Lamb, and BM where room naming identifies those locations.
- Room Summary current bins and current lots are room-specific. A room shows only bins/lots currently assigned to that exact room, excluding bins in other rooms, depleted bins, and observational Door/Lot samples.
- Receiving receipts add inventory to the assigned room. Door Sample and Lot Sample entries are observational only: they do not add bins or lots, but they can contribute pressure, starch, defect, latest sample, and month-over-month quality trend data for the linked room/lot.
- Master Data includes Grower Lots so operators can manage the production grower/orchard list. User-facing labels are `Grower`, `Lot #`, and `Pool Start`; the stored Lot # may still map to older `GrowerNumber` fields internally for compatibility. Rows whose grower starts with `INACTIVE` are inactive by default and hidden from normal receipt/sample selectors.
- Receipt and sample selection uses the Grower Lots master data and displays/searches values as `Grower - Lot # - Pool Start`. `Pool Start` is stored for later final-results analysis only; do not build final-results analytics in MVP 1.
- Dashboard room rollups show current non-depleted fruit only, with pressure, pressure standard deviation, month-over-month pressure change, harvest starch, defects, latest sample date, lot/bin counts, and configured review flags when data exists.
- Room drill-down shows current lots, depleted/history lots, related receipt/sample links, depletion history, and a Manager/Admin action to record bins sent to line.
- Depletion is the production-safe way to remove fruit from active room summaries when bins are sent to line. Depletion records are additive and audited; they never delete receipts, QC samples, fruit rows, photos, email logs, or historical room/sample data.
- Managers and Admins can create and void depletion records. QC Users and Viewers cannot create or void depletion records. Voided depletion records remain in history and are ignored by current-room calculations.
- Admin Data Cleanup is restricted by both Admin role and `DataCleanup__AllowedEmails`; the default allowed email is `wes@fruitandland.com`.
- Offline capture in Windows QC Station app.
- Sync to the Render/Postgres backend and Google Drive storage when internet returns.
- Photos and attachments stored in Google Shared Drive.
- Database stores metadata and structured data.
- Everything is audit logged.
- Production and staging must be separate environments. Production uses real data and staging/test uses fake data only. Staging/test must show a visible `TEST SITE — DO NOT ENTER REAL QC DATA` banner.
- Production data must be retained through future revisions. No production reset, fake seed, table drop, column drop, destructive migration, or purge is allowed without a documented backup and migration/recovery plan.
- Production migrations should be additive whenever possible. Backfill data before enforcing required fields, preserve existing receipts/samples/fruit rows/photos/users/stations/emails/audit logs, run a backup before migration, and verify health/key pages after deploy.
- Admin Data Cleanup remains restricted by allowed email, requires typed confirmation, and must audit every delete/purge action. Test data seeding/reset workflows must never run in production.
- Production backups use Google Drive as the configured target. At minimum the app must support non-secret configuration snapshots, photo/storage manifests, and a PostgreSQL logical dump when `pg_dump` is available. If `pg_dump` is unavailable, the Admin Backups page must warn and docs must describe the external backup path.
- Admin -> Backups lets Admins configure the Google Drive backup folder by folder URL or folder ID, set retention/schedule options, run a backup now, and test write access without exposing secrets.
- Admin -> Downloads shows only the hosted Google Drive folder link for installers/support files. Individual MSI, FTADLL, and dependency file buttons are intentionally not shown there; the folder is the production source.
- Admin -> Backups accepts a full Google Drive folder URL or raw folder ID. For a URL such as `https://drive.google.com/drive/folders/0AJIU41AM__WNUk9PVA?...`, the saved folder ID is `0AJIU41AM__WNUk9PVA`.
- Master Data -> Grower Lots supports production-safe CSV import with preview. The user-facing columns are Grower, Lot #, and Pool Start. Uploaded rows insert missing lots or update matching Lot # records, mark Growers starting with `INACTIVE` inactive, and never delete production rows missing from the upload.
- Receipts and samples should use Grower Lots master data when available. Typing a known Lot # should autofill Grower and Pool Start and link the record to the Grower Lot; unknown Lot # values may be entered manually with a warning.
- Current lot/bin counts are adjusted through audited inventory adjustments. Receiving adds inventory, depletion subtracts inventory, manual true-up corrects current counts, and void/reversal restores counts without deleting receipts, samples, photos, or email history.
- Every bin count change must record old count when available, change amount, new count, adjustment type, reason/note, user, timestamp, and related receipt/depletion/true-up context.
- Dashboard room summaries should highlight the weakest lot when current lots have quality data. Weakest-lot reasons must be explainable, such as low average pressure, pressure drop month-over-month, defects, starch, or high variance. Empty rooms show Empty and rooms without trend data should not invent a weakest lot.
- Admin navigation is consolidated into one Admin dropdown. On desktop it opens on hover and still supports click/tap for touch devices.
- Layouts should be optimized for 1920x1080 and 4K while avoiding normal page-level horizontal scrolling. Data-heavy pages should use responsive cards, wrapped action bars, and wider content containers instead of crushing short labels like `Avg`, `P1`, `P2`, `Size`, `Grade`, or `Empty` into vertical text.

## Out of Scope for MVP 1

- Storage inventory.
- Room controller imports.
- Mexico qualification.
- Packout imports.
- Pool closing imports.
- Long-term performance analytics.
