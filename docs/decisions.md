# Decisions

## ADR-0001: Use .NET/C# Solution

The Crop QC Dashboard will use .NET/C# across the web dashboard, API, shared contracts, data boundary, and Windows QC Station.

## ADR-0002: Target Azure App Service

The web dashboard and API will target Azure App Service.

## ADR-0003: Use Azure SQL for Structured Data

Azure SQL is the main database for structured records, metadata, workflow state, permissions, and audit logs.

## ADR-0004: Store Files in SharePoint/OneDrive

Photos and attachments will be stored in a SharePoint/OneDrive document library. SQL will store file metadata and references only.

## ADR-0005: Reserve Microsoft Graph Integration

Microsoft Graph will be added later for SharePoint file storage and Microsoft 365 email. The reserved email sender is `HL@fruitandland.com`, and the reserved QC Summary recipient is `QC@fruitandland.com`.

## ADR-0006: Design for Offline QC Station Sync

The Windows QC Station must support offline capture later. SQLite is reserved as the local cache, and project boundaries should keep sync responsibilities explicit.
