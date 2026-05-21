# Crop QC Dashboard

Crop QC Dashboard is the starting point for a .NET/C# system that will support receiving quality control workflows for Fruit and Land.

MVP 1 is Receiving/QC only. It will focus on receipt entry, receiving sample capture, QC fruit measurements, required photos, QC Summary preview/send workflow, role-based access, auditing, and offline-capable Windows QC Station capture.

Later phases will add storage inventory, room controller imports, Mexico qualification, packout imports, pool closing imports, and long-term performance analytics.

## Technology Direction

- .NET/C#
- Azure App Service for the web dashboard and API
- Azure SQL for structured data
- SharePoint/OneDrive document library for photos and attachments
- Windows desktop QC Station app
- SQLite local cache for later offline QC Station support
- Microsoft Graph later for SharePoint file storage and Microsoft 365 email

## Solution Layout

- `src/CropQc.Api` - API placeholder for future dashboard and QC Station services.
- `src/CropQc.Web` - Web dashboard placeholder.
- `src/CropQc.QcStation` - Windows QC Station placeholder.
- `src/CropQc.Shared` - Shared contracts and cross-project constants.
- `src/CropQc.Data` - Data access boundary placeholder.
- `docs` - Requirements, architecture, MVP scope, decisions, data model, and future phase notes.
- `scripts` - Operational and developer scripts.
- `tests` - Test projects will be added as implementation begins.

## Current Status

This repository is intentionally a skeleton. Business logic for storage, Mexico qualification, packout, pool closing, and analytics has not been implemented.
