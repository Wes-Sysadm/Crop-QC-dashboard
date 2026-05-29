# Render Deployment

This document describes the target Render deployment direction for the MVP 1 Receiving/QC web dashboard and API. It does not implement Google Drive upload, Gmail sending, storage inventory, Mexico qualification, packout imports, pool closing imports, or analytics.

## Target Services

- Render Web Service for `CropQc.Web` or `CropQc.Api`.
- Render Postgres for structured data.
- Google Shared Drive later for photos and attachments.
- Gmail API or Google Workspace SMTP relay later for QC Summary email.
- WinForms QC Station remains local for FTA, camera, and future offline capture.

## Web Service Setup

Create a Render Web Service connected to this repository.

Recommended build command:

```bash
dotnet publish src/CropQc.Web/CropQc.Web.csproj -c Release -o out
```

Recommended start command:

```bash
dotnet out/CropQc.Web.dll
```

If deploying the API separately, publish and start `src/CropQc.Api/CropQc.Api.csproj` instead.

## Environment Variables

Set these variables in Render:

- `ASPNETCORE_ENVIRONMENT=Production`
- `DATABASE_PROVIDER=PostgreSql`
- `ConnectionStrings__CropQc=[Render Postgres external or internal connection string]`
- `FileStorage__Provider=Local`
- `FileStorage__LocalRootPath=/var/data/cropqc-files`
- `FileStorage__BasePath=Crop QC Photos`
- `Email__Provider=Gmail`
- `Email__FromAddress=HL@fruitandland.com`
- `Email__ToAddress=QC@fruitandland.com`

`FileStorage__Provider=Local` is a placeholder for now. Render ephemeral disk should not be treated as durable photo storage unless a persistent disk is explicitly configured. The intended durable file provider is Google Shared Drive in a later PR.

## Render Postgres

Create a Render Postgres instance and copy its connection string into `ConnectionStrings__CropQc`.

The application chooses the provider with:

```json
"Database": {
  "Provider": "PostgreSql",
  "ConnectionStringName": "CropQc"
}
```

Environment variable `DATABASE_PROVIDER=PostgreSql` overrides the appsettings value.

## Migrations

The existing EF Core migrations are SQL Server-oriented and must remain intact for current local development. PostgreSQL should use a separate migration path before production cutover.

Recommended next step:

1. Add a provider-specific PostgreSQL migrations assembly or folder.
2. Generate a fresh PostgreSQL initial migration against the current model.
3. Validate the migration against a Render Postgres test database.
4. Run the migration during deployment with either `dotnet ef database update` or a migration bundle.

Temporary manual migration command for testing:

```powershell
$env:DATABASE_PROVIDER="PostgreSql"
$env:ConnectionStrings__CropQc="[Render Postgres connection string]"
dotnet ef database update --project .\src\CropQc.Data\CropQc.Data.csproj --startup-project .\src\CropQc.Web\CropQc.Web.csproj
```

Do not run this against production until the PostgreSQL migration path has been generated and reviewed.

## Health And Smoke Checks

Suggested URLs after deployment:

- `/` for the web dashboard home.
- `/Receipts` for receipt list/search.
- `/DailyQc` for the Daily QC dashboard.
- API root `/` if deploying `CropQc.Api`.
- API OpenAPI endpoint in Development only unless explicitly enabled for Production later.
