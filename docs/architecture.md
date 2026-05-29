# Architecture

## Technology Direction

- .NET/C# solution.
- Render Web Service target for the web dashboard and API.
- Render Postgres as the target production database for structured data.
- SQL Server LocalDB / SQL Server remains supported for local development while PostgreSQL migration work is completed.
- Google Shared Drive as the target store for photos and attachments.
- Windows desktop QC Station app.
- SQLite local cache for later offline QC Station data.
- Gmail API or Google Workspace SMTP relay later for QC Summary email.

## Project Boundaries

- `CropQc.Web` hosts the web dashboard user experience.
- `CropQc.Api` exposes API endpoints for the dashboard and QC Station.
- `CropQc.QcStation` is the Windows desktop capture app boundary.
- `CropQc.Shared` contains shared contracts, constants, and cross-project types.
- `CropQc.Data` contains the EF Core data access boundary. It can be configured for SQL Server or PostgreSQL.

## Storage Boundaries

The database stores structured data, including receipts, samples, fruit rows, measurements, defects, photo metadata, email status, permissions, and audit records. The target production database is Render Postgres. SQL Server remains the current local/dev-compatible provider until the PostgreSQL migration path is completed.

Photos and attachments will be stored in Google Shared Drive. The database should store metadata and stable references only, not binary photo content. The application has a file storage service boundary with a local provider for development and a planned Google Drive provider.

## Retention Boundary

Database records are retained indefinitely by default. There is no automatic deletion or purge of receipts, samples, fruit readings, fruit defects, photo metadata, audit logs, email logs, users, roles, rooms, varieties, or other master data.

Photos and attachments must be retained for at least 3 crop years after the current crop year. The current `PhotoRetentionCropYearsAfterCurrent` configuration value is a planning value only; no automatic photo deletion currently runs.

Admin-reviewed archive/delete workflows are future work. Until that workflow exists, retention actions must not run automatically.

## Email Boundary

Gmail API or Google Workspace SMTP relay will be used later for QC Summary email. MVP 1 documentation reserves `HL@fruitandland.com` as the sender and `QC@fruitandland.com` as the QC Summary recipient.

## Offline Boundary

The QC Station must be designed around offline capture and later sync. The station will use SQLite as a local cache, then sync structured data and file metadata to the Render/Postgres backend and Google Drive storage when internet connectivity returns.

## Audit Boundary

All create, edit, delete, send, import, and export actions require audit logging. Audit records should identify the actor, action, timestamp, target entity, relevant before/after context, and source application.
