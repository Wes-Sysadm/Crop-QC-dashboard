# Decisions

## ADR-0001: Use .NET/C# Solution

The Crop QC Dashboard will use .NET/C# across the web dashboard, API, shared contracts, data boundary, and Windows QC Station.

## ADR-0002: Target Render Web Service

The web dashboard and API will target Render Web Service hosting.

## ADR-0003: Use Render Postgres for Production Structured Data

Render Postgres is the target production database for structured records, metadata, workflow state, permissions, and audit logs. SQL Server LocalDB / SQL Server remains supported for local development until the PostgreSQL migration path is complete.

## ADR-0004: Store Files in Google Shared Drive

Photos and attachments will be stored in a Google Shared Drive. The database will store file metadata and references only.

## ADR-0005: Reserve Gmail Integration

Gmail API or Google Workspace SMTP relay will be added later for QC Summary email. The reserved email sender is `HL@fruitandland.com`, and the reserved QC Summary recipient is `QC@fruitandland.com`.

## ADR-0006: Design for Offline QC Station Sync

The Windows QC Station must support offline capture later. SQLite is reserved as the local cache, and project boundaries should keep sync responsibilities explicit.
