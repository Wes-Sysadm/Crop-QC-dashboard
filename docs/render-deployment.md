# Render Deployment

This document describes the first Render staging deployment path for the MVP 1 Receiving/QC web dashboard. It does not implement Google Drive upload, Gmail sending, storage inventory, Mexico qualification, packout imports, pool closing imports, or analytics.

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

## Render Blueprint Setup

Create a new Render Blueprint from the repository. The included `render.yaml` defines:

- Web service name: `crop-qc-dashboard`.
- Docker runtime using `./Dockerfile`.
- Health check path: `/health`.
- Render Postgres database name: `crop-qc-dashboard-db`.
- `ConnectionStrings__CropQc` wired from the Render Postgres connection string.

The Render Blueprint syntax uses `fromDatabase` with `property: connectionString` for database connection string injection, matching Render's Blueprint environment variable reference pattern.

## Manual Web Service Setup

If not using `render.yaml`, create a Render Web Service manually:

- Environment: Docker.
- Dockerfile path: `./Dockerfile`.
- Health check path: `/health`.
- Region: same region as the Render Postgres database.
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
- `Authentication__Google__ClientId=[Google OAuth web client ID]`
- `Authentication__Google__ClientSecret=[Google OAuth client secret]`
- `FileStorage__Provider=Local`
- `FileStorage__LocalRootPath=/var/data/cropqc-files`
- `FileStorage__BasePath=Crop QC Photos`
- `Email__Provider=None`

Do not commit database passwords, Google credentials, Gmail credentials, or API secrets.

Google login is required for dashboard pages. Only Google Workspace accounts from `wp-packing.com`, `earlbrownandsons.com`, and `fruitandland.com` are accepted. Other Google accounts are rejected and logged without logging secrets.

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and the app's forwarded-header middleware let ASP.NET Core honor Render's `X-Forwarded-Proto=https` header. This is required so Google OAuth generates `https://crop-qc-dashboard.onrender.com/signin-google` instead of an internal `http://` callback URL.

Initial Admin bootstrap is controlled by `Authentication__BootstrapAdminEmails`. The initial bootstrap admin is `wes@fruitandland.com`. After that account logs in, manage users and roles from `/Admin/Users`; the bootstrap setting remains a safety net so the first Admin is not locked out if database roles are empty.

Roles are managed inside the dashboard:

- Admin: full access to user management, Master Data editing, and Configuration.
- Manager: future review/resend/override workflows.
- QC User: future same-day QC entry permissions.
- Viewer: read-only dashboard access.

Admin-only dashboard pages:

- `/Admin/Users` manages user accounts, active status, and roles after Google login creates identity records.
- `/MasterData` shows edit/add/deactivate controls only for Admin users.
- `/Admin/Configuration` manages safe non-secret runtime configuration values. Do not store OAuth secrets, database connection strings, Gmail secrets, Google Drive secrets, or API keys there.

`FileStorage__Provider=Local` is a placeholder for now. Render ephemeral disk should not be treated as durable photo storage unless a persistent disk is explicitly configured. The intended durable file provider is Google Shared Drive in a later PR.

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
