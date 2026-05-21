# Architecture

## Technology Direction

- .NET/C# solution.
- Azure App Service target for the web dashboard and API.
- Azure SQL as the main database for structured data.
- SharePoint/OneDrive document library for photos and attachments.
- Windows desktop QC Station app.
- SQLite local cache for later offline QC Station data.
- Microsoft Graph later for SharePoint file storage and Microsoft 365 email.

## Project Boundaries

- `CropQc.Web` hosts the web dashboard user experience.
- `CropQc.Api` exposes API endpoints for the dashboard and QC Station.
- `CropQc.QcStation` is the Windows desktop capture app boundary.
- `CropQc.Shared` contains shared contracts, constants, and cross-project types.
- `CropQc.Data` contains the data access boundary for Azure SQL and, later, sync-related persistence abstractions.

## Storage Boundaries

Azure SQL stores structured data, including receipts, samples, fruit rows, measurements, defects, photo metadata, email status, permissions, and audit records.

Photos and attachments are stored in SharePoint/OneDrive. SQL should store metadata and stable references only, not binary photo content.

## Email Boundary

Microsoft Graph will be used later for Microsoft 365 email. MVP 1 documentation reserves `HL@fruitandland.com` as the sender and `QC@fruitandland.com` as the QC Summary recipient.

## Offline Boundary

The QC Station must be designed around offline capture and later sync. The station will use SQLite as a local cache, then sync structured data and file metadata to Azure when internet connectivity returns.

## Audit Boundary

All create, edit, delete, send, import, and export actions require audit logging. Audit records should identify the actor, action, timestamp, target entity, relevant before/after context, and source application.
