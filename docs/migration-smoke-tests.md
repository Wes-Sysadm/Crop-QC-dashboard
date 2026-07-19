# Migration Smoke Tests

Crop QC supports SQL Server for local development and PostgreSQL for hosted production. Run these smoke tests before merging migration compatibility changes.

## PostgreSQL Fresh Database

Use a disposable database only. Do not run this against production.

Example with a local PostgreSQL instance:

```powershell
createdb -h 127.0.0.1 -p 55432 -U postgres cropqc_pg_fresh_main
$env:DATABASE_PROVIDER = 'PostgreSql'
$env:ConnectionStrings__CropQc = 'Host=127.0.0.1;Port=55432;Database=cropqc_pg_fresh_main;Username=postgres;Password='
dotnet ef database update --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build
dotnet ef migrations list --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build
```

Or use the opt-in smoke script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-migration-smoke.ps1 `
  -Provider PostgreSql `
  -ConnectionString 'Host=127.0.0.1;Port=55432;Database=cropqc_pg_fresh_main;Username=postgres;Password='
```

Verify required seed data:

```sql
select "Id", "Name", "IsActive"
from "DefectTypes"
order by "Id";
```

Expected `DefectTypes` rows are ids 1 through 11: Bruise, Sunburn, Bitter Pit, Scald, Decay, Puncture, Watercore, Limb Rub, Stem Bowl Crack, Internal Browning, and Other. All should be active.

## PostgreSQL Checkpoint Upgrade

To verify an upgrade path, apply an earlier migration first and then resume to latest:

```powershell
createdb -h 127.0.0.1 -p 55432 -U postgres cropqc_pg_checkpoint_main
$env:DATABASE_PROVIDER = 'PostgreSql'
$env:ConnectionStrings__CropQc = 'Host=127.0.0.1;Port=55432;Database=cropqc_pg_checkpoint_main;Username=postgres;Password='
dotnet ef database update 20260521173857_InitialMvp1ReceivingQcModel --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build
dotnet ef database update --project src\CropQc.Data\CropQc.Data.csproj --startup-project src\CropQc.Data\CropQc.Data.csproj --no-build
```

## SQL Server LocalDB Fresh Database

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-migration-smoke.ps1 `
  -Provider SqlServer `
  -ConnectionString "Server=(localdb)\mssqllocaldb;Database=CropQcMigrationSmoke;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Connect Timeout=5"
```

## Safety Notes

- Historical migration ids and ordering must not be changed.
- Existing databases with migrations recorded in `__EFMigrationsHistory` do not rerun corrected historical migration code.
- PostgreSQL compatibility fixes should preserve the same logical schema and seed data as SQL Server.
- Do not log production passwords, connection strings, OAuth tokens, private photo URLs, or business data during smoke tests.
- Future data-seed operations should avoid provider-specific store-type assumptions. If a migration must support multiple providers, use provider-aware store types or provider-specific SQL branches.
