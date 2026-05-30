# Render Deployment

This document describes the first Render staging deployment path for the MVP 1 Receiving/QC web dashboard. It includes Google Drive photo upload configuration. It does not implement Gmail sending, storage inventory, Mexico qualification, packout imports, pool closing imports, or analytics.

## Target Services

- Render Web Service for `CropQc.Web`.
- Render Postgres for structured data.
- Google Shared Drive later for photos and attachments.
- Gmail API or Google Workspace SMTP relay later for QC Summary email.
- WinForms QC Station remains local for FTA, camera, and future offline capture.

## Repository Deployment Files

The repository includes:

- `Dockerfile` at the repo root.
- `render.yaml` at the repo root.
- `src/CropQc.Web/appsettings.Production.json` with safe non-secret defaults.

The Docker image builds and publishes only `src/CropQc.Web/CropQc.Web.csproj`. It does not run the WinForms QC Station.

The Render web service should include a persistent disk mounted at `/var/data`. The app uses that disk for local placeholder file storage and for ASP.NET Core Data Protection keys at `/var/data/dataprotection-keys`.

## Render Blueprint Setup

Create a new Render Blueprint from the repository. The included `render.yaml` defines:

- Web service name: `crop-qc-dashboard`.
- Docker runtime using `./Dockerfile`.
- Health check path: `/health`.
- Render Postgres database name: `crop-qc-dashboard-db`.
- `ConnectionStrings__CropQc` wired from the Render Postgres connection string.
- `FileStorage__Provider=GoogleDrive` with the provided Google Shared Drive root folder ID.

The Render Blueprint syntax uses `fromDatabase` with `property: connectionString` for database connection string injection, matching Render's Blueprint environment variable reference pattern.

## Manual Web Service Setup

If not using `render.yaml`, create a Render Web Service manually:

- Environment: Docker.
- Dockerfile path: `./Dockerfile`.
- Health check path: `/health`.
- Region: same region as the Render Postgres database.
- Persistent disk: mount at `/var/data`.
- Auto-deploy: enabled for staging if desired.

The Dockerfile starts the app with:

```bash
dotnet CropQc.Web.dll --urls http://0.0.0.0:${PORT:-8080}
```

Render provides `PORT`; the fallback `8080` is for local Docker testing.

## Environment Variables

Set these variables in Render:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
- `DATABASE_PROVIDER=PostgreSql`
- `ConnectionStrings__CropQc=[Render internal Postgres connection string]`
- `Database__EnsureCreatedOnStartup=false`
- `Database__SeedMasterDataOnStartup=false`
- `Authentication__AllowedGoogleDomains=wp-packing.com,earlbrownandsons.com,fruitandland.com`
- `Authentication__BootstrapAdminEmails=wes@fruitandland.com`
- `Authentication__SessionDays=7`
- `Authentication__Google__ClientId=[Google OAuth web client ID]`
- `Authentication__Google__ClientSecret=[Google OAuth client secret]`
- `DataProtection__PersistKeysToFileSystem=true`
- `DataProtection__KeysPath=/var/data/dataprotection-keys`
- `DataProtection__ApplicationName=CropQcDashboard`
- `FileStorage__Provider=GoogleDrive`
- `FileStorage__LocalRootPath=/var/data/cropqc-files`
- `FileStorage__BasePath=Crop QC Photos`
- `GoogleDrive__UseSharedDrive=true`
- `GoogleDrive__RootFolderId=0ADHRTHdG9u98Uk9PVA`
- `GoogleDrive__SharedDriveId=0ADHRTHdG9u98Uk9PVA`
- `GoogleDrive__ServiceAccountJson=[Google service account JSON]`
- `GoogleDrive__ApplicationName=Crop QC Dashboard`
- `GoogleDrive__BaseFolderName=Photos`
- `Email__Provider=None`
- `QcStation__ApiBaseUrl=https://crop-qc-dashboard.onrender.com`

Do not commit database passwords, Google credentials, Gmail credentials, or API secrets.

WinForms QC Station access is managed from the database, not with one shared Render API key. Sign in as Admin, open `/Admin/QcStations`, create one station record per QC computer, and download that station's `qcstation.settings.json` immediately after creation or key rotation. The station sends `X-QC-STATION-CODE` and `X-QC-STATION-API-KEY`; the dashboard stores only the key hash. Deactivate a station to revoke access without breaking other QC computers.

Keep station config JSON private because it contains the raw station API key. If a config file is lost or exposed, rotate the station key from `/Admin/QcStations` and download a new config. The shared MSI installer contains no station secrets.

The Render Docker build publishes only the web dashboard. It does not build Windows desktop payloads or the QC Station installer. Build the signed MSI on a Windows build machine:

```powershell
.\scripts\build-qcstation-installer.ps1
```

The script publishes the WinForms x86 app, builds `artifacts\installers\CropQcStationSetup.msi`, and signs it when signing environment variables are configured. If signing is not configured, it builds an unsigned MSI and prints a SmartScreen/Defender warning.

To deploy the installer download, place the signed MSI at `src\CropQc.Web\App_Data\Downloads\CropQcStationSetup.msi` before web publish/deploy, or set `QcStation__InstallerPath` to a whitelisted deployed path containing `CropQcStationSetup.msi`. If the MSI is missing, `/Admin/Downloads` shows “QC Station installer has not been deployed yet” and the web app still starts normally.

Google login is required for dashboard pages. Only Google Workspace accounts from `wp-packing.com`, `earlbrownandsons.com`, and `fruitandland.com` are accepted. Other Google accounts are rejected and logged without logging secrets.

Successful Google login creates a persistent local dashboard session for `Authentication__SessionDays`, which defaults to 7 days. Users stay signed in for one week unless they click Logout, their account is deactivated, or authentication fails. Logout immediately clears the local auth cookie.

ASP.NET Core Data Protection keys must also be persisted for those one-week cookies to survive Render restarts and redeploys. Without persisted keys, Render may log a warning about `/root/.aspnet/DataProtection-Keys`, and existing login cookies become unreadable when the container is replaced. Configure a Render persistent disk mounted at `/var/data` and store keys at `/var/data/dataprotection-keys`. Do not commit key files and do not log key contents.

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and the app's forwarded-header middleware let ASP.NET Core honor Render's `X-Forwarded-Proto=https` header. This is required so Google OAuth generates `https://crop-qc-dashboard.onrender.com/signin-google` instead of an internal `http://` callback URL.

Initial Admin bootstrap is controlled by `Authentication__BootstrapAdminEmails`. The initial bootstrap admin is `wes@fruitandland.com`. After that account logs in, manage users and roles from `/Admin/Users`; the bootstrap setting remains a safety net so the first Admin is not locked out if database roles are empty.

Roles are managed inside the dashboard. `/Admin/Users` also shows a role permission matrix so Admin users can see exactly what each access level is intended to allow or block before changing a user role:

- Admin: Dashboard, Daily QC, Receipts, Master Data, Users, QC Stations, Downloads, Configuration, override/send, audit review, and exports.
- Manager: Dashboard, Daily QC, Receipts, Master Data, and QC Stations; no Users, Downloads, or Configuration access.
- QC User: Dashboard, Daily QC, and Receipts; no management/admin access.
- Viewer: Dashboard, Daily QC, and Receipts; no management/admin access.

Management dashboard pages:

- `/Admin/Users` manages user accounts, active status, and roles after Google login creates identity records.
- `/Admin/QcStations` manages station enrollment, per-station API keys, key rotation, deactivation, and raw config downloads. Admins and Managers can access this page.
- `/MasterData` shows edit/add/deactivate controls for Admins and Managers.
- `/Admin/Configuration` manages safe non-secret runtime configuration values. Do not store OAuth secrets, database connection strings, Gmail secrets, Google Drive secrets, or API keys there.
- `/Admin/Downloads` provides approved internal support-file links, such as the FTA DLL installer Google Drive file and the QC Station App Installer MSI when deployed, to Admin users only.

Master Data editing notes:

- Fruit profile organic/conventional status is controlled only by `ProductionType`. `ProductionType=Organic` derives `IsOrganic=true`; `ProductionType=Conventional` derives `IsOrganic=false`.
- Commodity is editable. Apple and Pear are seeded, and Admins can type additional commodities such as Cherry, Peach, Nectarine, or Apricot.
- Rooms are tied to warehouses. Room code uniqueness is per warehouse, and the Rooms page shows warehouse code/name with every room.

`FileStorage__Provider=GoogleDrive` enables photo uploads to the configured Google Drive root folder. Keep `FileStorage__Provider=Local` only for local development or temporary staging diagnostics. Render ephemeral disk should not be treated as durable photo storage unless a persistent disk is explicitly configured.

## Google Drive Photo Storage

The configured Google Shared Drive root folder is:

```text
https://drive.google.com/drive/folders/0ADHRTHdG9u98Uk9PVA?dmr=1&ec=wgc-drive-%5Bmodule%5D-goto
RootFolderId / SharedDriveId: 0ADHRTHdG9u98Uk9PVA
```

The app creates or reuses this folder structure below that root:

```text
Photos/{CropYear}/{Warehouse}/Receipt-{ReceiptId}/{PhotoType}/
```

Setup steps:

1. Enable the Google Drive API in the Google Cloud project.
2. Create a service account for the Crop QC Dashboard.
3. Add the service account email from the JSON to the Google Shared Drive.
4. Grant Content Manager or Manager access.
5. Set `FileStorage__Provider=GoogleDrive`.
6. Set `GoogleDrive__UseSharedDrive=true`.
7. Set `GoogleDrive__RootFolderId=0ADHRTHdG9u98Uk9PVA`.
8. Set `GoogleDrive__SharedDriveId=0ADHRTHdG9u98Uk9PVA`.
9. Set `GoogleDrive__ServiceAccountJson` to the full service account JSON. Do not commit this JSON.
10. Sign in as Admin and check `/health/storage`. The endpoint shows provider, Shared Drive mode, root-folder configuration, shared-drive configuration, credential configuration, and application-name configuration without exposing secrets.

If upload fails, check the service account folder access, Drive API status, root folder ID, shared drive ID, and service account JSON validity. A normal My Drive shared folder is not enough for service account uploads because service accounts do not have their own Drive storage quota. The target must be a Google Shared Drive folder.

## Retention And Backups

Database records are retained indefinitely by default. The app does not automatically purge receipts, samples, fruit readings, defects, audit logs, email logs, users, roles, rooms, varieties, or master data.

Photos and attachments must be retained for at least 3 crop years after the current crop year. The Admin Configuration value `PhotoRetentionCropYearsAfterCurrent` defaults to `3`, but it is a planning value only. No automatic photo deletion currently runs, and an Admin-reviewed archive/delete workflow is future work.

Render Postgres backups are operational backups, not a substitute for the Crop QC retention policy. Before production use, configure and document separate database backup/export procedures that meet company retention needs.

The eventual photo storage provider must support at least 3 crop years of retention after the current crop year. For staging, local placeholder storage is not durable unless backed by a Render persistent disk.

## Admin Downloads

`/Admin/Downloads` is protected by the Admin policy and links only to approved internal support files. It does not proxy downloads through the web app, expose arbitrary file paths, allow uploads, commit installer binaries, or store installer files in the Render container.

The current download entry is:

- Name: FTA DLL Installer
- File: `FTADLL.exe`
- Purpose: installer/runtime files needed for GUSS FTA DLL integration on QC Station computers.
- Link: `https://drive.google.com/file/d/1iYy1v1-D8T-S4SgfHJOeuwoeJfsbcvoS/view?usp=drive_link`
- Button text: `Open Google Drive Download`

The installer binary is not committed to the repository or deployed into Render. Google Drive sharing permissions are managed in Google Drive and should be limited to company users when possible.

## Render Postgres

Create the Render Postgres database in the same region as the web service. Use the internal database URL for the web service connection string.

The app chooses the provider from:

```json
"Database": {
  "Provider": "PostgreSql",
  "ConnectionStringName": "CropQc"
}
```

Environment variable `DATABASE_PROVIDER=PostgreSql` overrides the appsettings value.

## First Staging Schema Creation

The existing checked-in EF migrations are SQL Server-oriented and must remain intact for local SQL Server development. They are not the final PostgreSQL migration path.

For the first empty Render staging database only, use the opt-in schema creation switch:

1. Set `Database__EnsureCreatedOnStartup=true` in Render.
2. Set `Database__SeedMasterDataOnStartup=true` in Render.
3. Deploy the web service.
4. Open `/health/db` and confirm it returns success.
5. Open `/health/master-data` and confirm the master-data counts. The seeded room list currently has 68 rooms.
6. Sign in with an allowed Google account.
7. Open `/Receipts` and verify the warehouse, room, and variety dropdowns are populated.
8. Open `/MasterData/rooms` and `/MasterData/fruit-profiles` to verify the seeded lists.
9. Set `Database__EnsureCreatedOnStartup=false`.
10. Set `Database__SeedMasterDataOnStartup=false` after seed counts are confirmed in logs.
11. Redeploy the web service.

This uses EF Core `EnsureCreated` to create the current model in an empty PostgreSQL database. It does not create EF migration history and should not be used as the long-term production migration strategy.

The master-data seed is idempotent and only inserts missing required rows. It does not delete receipts, samples, photos, or user-entered data, and it does not reset existing master-data edits.

When `Database__SeedMasterDataOnStartup=true`, Render logs should include:

- warehouses count
- rooms before seed
- rooms added
- rooms after seed
- fruit profiles count
- grades count
- defects count

## PostgreSQL Migration Strategy

Before production cutover, add a provider-specific PostgreSQL migration path:

1. Add a PostgreSQL migrations assembly or provider-specific migrations folder.
2. Generate a fresh PostgreSQL initial migration against the current model.
3. Validate the migration against a disposable Render Postgres database.
4. Replace staging `EnsureCreated` usage with `dotnet ef database update` or a migration bundle.

Example migration command for the future migration path:

```powershell
$env:DATABASE_PROVIDER="PostgreSql"
$env:ConnectionStrings__CropQc="[Render Postgres connection string]"
dotnet ef database update --project .\src\CropQc.Data\CropQc.Data.csproj --startup-project .\src\CropQc.Web\CropQc.Web.csproj
```

Do not run SQL Server-oriented migrations against Render Postgres.

## Health And Smoke Checks

After deployment:

- `/health` should return `200 OK` with `Crop QC Dashboard OK`.
- `/health/db` should return `200 OK` when the app can connect to the configured database.
- `/health/master-data` should return counts for warehouses, rooms, fruit profiles, grades, defects, sample types, starch values, and size thresholds. For the current MVP 1 seed, `rooms` should be at least `68` after the room seed runs.
- `/` should redirect to Google login when not authenticated, then load the dashboard after sign-in.
- `/Receipts` should load receipt list/search after sign-in and show seeded warehouses, rooms, and fruit profiles.
- `/DailyQc` should load the Daily QC dashboard after sign-in.
- `/Receipts/Export` downloads the receiving-data Excel export after sign-in.

`/health` intentionally does not touch the database so Render can distinguish app startup issues from database connectivity or schema issues.

## Repair Missing Rooms

If warehouses and fruit profiles exist but `/MasterData/rooms` is empty, repair the live database with the idempotent master-data seed:

1. Set `Database__SeedMasterDataOnStartup=true` in Render.
2. Redeploy the web service.
3. Watch Render logs for `Room seed started`, `Rooms added`, `Rooms repaired`, `Rooms after seed`, and any missing warehouse-code warnings.
4. Open `/health/master-data` and verify `rooms` is at least `68`.
5. Open `/MasterData/rooms` and confirm rooms show warehouse code, warehouse name, room code, room name, capacity, and active status.
6. Open `/Receipts` and confirm selecting WP, EBS, DH, or McDougall filters the room dropdown to that warehouse.
7. Set `Database__SeedMasterDataOnStartup=false` after counts are confirmed, then redeploy.

The room seed matches warehouses by warehouse code, not fixed database IDs. It adds missing rooms only and does not overwrite user-edited names or capacities unless a name is blank or a capacity is invalid.

## Google OAuth Setup

Create a Google OAuth web client in the Google Cloud project used for the staging dashboard.

Configure the authorized redirect URI for the Render service:

```text
https://[render-host]/signin-google
```

Do not add an `http://` redirect URI in Google. If Google shows a `redirect_uri_mismatch` with `http://crop-qc-dashboard.onrender.com/signin-google`, confirm `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is set in Render and redeploy the service.

Set the OAuth client ID and secret in Render environment variables. Do not store them in appsettings files or source control.

## Receiving Data Export

The Receipts page includes `Export Receiving Data to Excel`. The export returns an `.xlsx` file with receipt, sample, readiness/status, and fruit-row data. It is a dashboard export only; no email is sent and no Google Drive upload is performed.

## Redeploy After GitHub Push

If auto-deploy is enabled, Render redeploys after a push to the configured branch. For manual deploys, use Render Dashboard -> service -> Manual Deploy -> Deploy latest commit.

Check the deploy logs for:

- Docker restore/publish success.
- The app binding to `0.0.0.0:$PORT`.
- `/health` passing.
- `/health/db` passing after the database is configured and schema exists.
