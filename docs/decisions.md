# Decisions

## ADR-0001: Use .NET/C# Solution

The Crop QC Dashboard will use .NET/C# across the web dashboard, API, shared contracts, data boundary, and Windows QC Station.

## ADR-0002: Target Render Web Service

The web dashboard and API will target Render Web Service hosting.

## ADR-0003: Use Render Postgres for Production Structured Data

Render Postgres is the target production database for structured records, metadata, workflow state, permissions, and audit logs. SQL Server LocalDB / SQL Server remains supported for local development until the PostgreSQL migration path is complete.

## ADR-0004: Store Files in Google Shared Drive

Photos and attachments will be stored in a Google Shared Drive. The database will store file metadata and references only.

## ADR-0005: Use User-Delegated Gmail Sending

QC Summary email sends through the Gmail API with the logged-in user's delegated `gmail.send` permission. The sender is the logged-in Google Workspace user from an allowed company domain, currently `fruitandland.com`, `earlbrownandsons.com`, or `wp-packingllc.com`. QC Summary recipients come from `Email__QcDefaultRecipients`. During the current email test phase, that value is `rob@earlbrownandsons.com,wes@fruitandland.com`; change it before production rollout if the recipient list changes. Shared SMTP is not the normal sending identity.

## ADR-0006 - Admin Cleanup And Downloads Boundaries

Admin Data Cleanup is limited to Admin users whose email appears in `DataCleanup__AllowedEmails`; the default is `wes@fruitandland.com`. Admin role by itself is intentionally insufficient for destructive cleanup. Admin Downloads is only a curated Google Drive link surface for shared installer/support files, with `Downloads__MasterFolderUrl` as the preferred master folder link. Station-specific config JSON stays under Admin -> QC Stations.

## ADR-0007 - Dashboard And Layout Clarity

Dashboard metric cards must click through to filtered pages that explain or resolve the metric. Missing-data and needs-review lists must show the reason, not just a count. Normal dashboard pages should avoid page-level horizontal scrolling by using wrapping action bars, responsive cards, and text wrapping for long IDs/URLs.

## ADR-0008 - Production/Staging Separation And Backups

Production is the live system of record and must retain real receipts, samples, photos, emails, QC Station records, and audit logs through future revisions. Staging/test is isolated fake data only and must show a prominent `STAGING - Non-production data` banner. Production and staging use separate databases, Google Drive folders, OAuth redirect URIs, email recipients, and QC Station configs. Google Drive is the backup target for app-generated backup artifacts: PostgreSQL logical dumps when `pg_dump` is available, non-secret configuration snapshots, and photo/storage manifests.

## ADR-0006: Design for Offline QC Station Sync

The Windows QC Station must support offline capture later. SQLite is reserved as the local cache, and project boundaries should keep sync responsibilities explicit.
