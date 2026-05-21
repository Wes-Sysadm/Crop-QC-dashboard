# Codex Guidance

This repository is for the Crop QC Dashboard.

## Scope Discipline

- Keep MVP 1 focused on Receiving/QC only.
- Do not implement future phases unless explicitly requested.
- Prefer small, reviewable PRs.
- Use clear names and comments.

## Data and File Storage

- Do not store photos directly in SQL.
- Use Azure SQL for structured data.
- Use SharePoint/OneDrive for photos, attachments, and other files.
- Microsoft Graph integration will be added later for SharePoint file storage and Microsoft 365 email.

## Audit and Sync

- Audit logging is required for all create, edit, delete, send, import, and export actions.
- Offline QC Station support is required later, so design with sync boundaries in mind.
- The Windows QC Station will use a SQLite local cache later for offline capture and sync.

## Deferred Work

- Do not build storage inventory yet.
- Do not build room controller imports yet.
- Do not build Mexico qualification yet.
- Do not build packout imports yet.
- Do not build pool closing imports yet.
- Do not build long-term performance analytics yet.
