# Codex Guidance

This repository is for the Crop QC Dashboard.

## Scope Discipline

- Keep MVP 1 focused on Receiving/QC only.
- Do not implement future phases unless explicitly requested.
- Prefer small, reviewable PRs.
- Use clear names and comments.

## Data and File Storage

- Do not store photos directly in SQL.
- Use provider-based database configuration. SQL Server remains supported for local development, and Render Postgres is the target production database.
- Use provider-based file storage. Local storage is for development, and Google Shared Drive is the target production store for photos, attachments, and other files.
- Gmail API or Google Workspace SMTP relay will be added later for QC Summary email.

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
