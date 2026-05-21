# Local Development Setup

This guide explains how to run the current Crop QC Dashboard MVP 1 Receiving/QC web dashboard and API on a Windows development machine.

The current local setup is for development only. It does not add authentication, FTA integration, USB camera capture, SharePoint upload, email sending, storage inventory, Mexico qualification, packout imports, pool closing imports, or analytics.

## 1. Check Prerequisites

Open Windows PowerShell.

```powershell
git --version
dotnet --info
sqllocaldb info
```

Install anything missing:

- Git for Windows: https://git-scm.com/download/win
- .NET SDK 9 or later: https://dotnet.microsoft.com/download
- SQL Server LocalDB or SQL Server Express:
  - LocalDB is included with Visual Studio workloads and SQL Server Express installers.
  - SQL Server Express also works if you provide a connection string.

Install the EF Core CLI if it is not already available:

```powershell
dotnet tool install --global dotnet-ef
dotnet-ef --version
```

If `dotnet-ef` was already installed, update it when needed:

```powershell
dotnet tool update --global dotnet-ef
```

## 2. Clone The Repository

```powershell
cd C:\Dev
git clone https://github.com/Wes-Sysadm/Crop-QC-dashboard.git
cd .\Crop-QC-dashboard
```

If you are working on an open PR branch, check it out after cloning:

```powershell
git fetch origin
git checkout <branch-name>
```

## 3. Restore Packages

```powershell
dotnet restore .\CropQc.sln
```

Or use the helper script:

```powershell
.\scripts\dev-build.ps1
```

The build script restores packages and then builds the solution.

## 4. Build The Solution

```powershell
dotnet build .\CropQc.sln
```

Helper script:

```powershell
.\scripts\dev-build.ps1
```

## 5. Create Or Update The Local Database

By default, the projects use SQL Server LocalDB:

```text
Server=(localdb)\mssqllocaldb;Database=CropQcDashboard;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
```

Apply EF Core migrations:

```powershell
dotnet ef database update --project .\src\CropQc.Data\CropQc.Data.csproj --startup-project .\src\CropQc.Api\CropQc.Api.csproj
```

Helper script:

```powershell
.\scripts\dev-update-db.ps1
```

To use SQL Server Express instead of LocalDB, pass a connection string:

```powershell
.\scripts\dev-update-db.ps1 -ConnectionString "Server=.\SQLEXPRESS;Database=CropQcDashboard;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
```

The run scripts accept the same `-ConnectionString` value.

## 6. Run The API

Start the API in one PowerShell window:

```powershell
.\scripts\dev-run-api.ps1
```

Default API URL:

```text
http://localhost:5276
```

Open the API root:

```powershell
Invoke-WebRequest http://localhost:5276 -UseBasicParsing
```

In Development, OpenAPI is mapped by the API project. Try:

```text
http://localhost:5276/openapi/v1.json
```

## 7. Run The Web Dashboard

Start the web dashboard in a second PowerShell window:

```powershell
.\scripts\dev-run-web.ps1
```

Default web URL:

```text
http://localhost:5275
```

Open the dashboard in a browser:

```powershell
Start-Process http://localhost:5275
```

## 8. Run Tests

```powershell
dotnet test .\CropQc.sln
```

Helper script:

```powershell
.\scripts\dev-test.ps1
```

## 9. Basic MVP 1 Smoke-Test Workflow

Use the web dashboard at `http://localhost:5275`.

1. Open the dashboard home page.
2. Go to `Receipts`.
3. Create a receipt with:
   - Crop year
   - Received date/time
   - Compu-Tech receipt ID
   - Warehouse
   - Room
   - Fruit profile
   - Grower
   - Lot
   - Bin count
4. Open the new receipt detail page.
5. Select `Create Receiving Sample`.
6. If the receipt already has a receiving sample, confirm the warning appears and the new sample displays with a sequence such as `12345(2)`.
7. On the QC sample detail page, enter one or more fruit rows in the editable 25-row grid:
   - Pressure 1 lbs
   - Pressure 2 lbs
   - Weight grams
   - Grade
   - Optional starch
   - Optional multiple defects
   - Optional notes when `Other` defect is selected
8. Save the fruit rows.
9. Confirm the row shows calculated average pressure, calculated size, size status, and completed status.
10. Add placeholder receipt-level photo metadata for at least one `BinTruck` photo from the receipt detail page.
11. Add placeholder sample-level photo metadata for:
    - `SampleBeforeCutting`
    - `CutFruit`
    - `FruitAfterStarch`
12. Return to the sample detail page and confirm Summary Readiness changes as rows, starch, and photos are completed.
13. Confirm the `Send QC Summary Placeholder` button is disabled while readiness is false and appears enabled only when readiness is true.

No actual email is sent. No image binary is uploaded or stored in SQL. Photo forms currently save metadata and placeholder SharePoint/OneDrive references only.

## Useful Direct Commands

Run API without helper script:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5276"
dotnet run --project .\src\CropQc.Api\CropQc.Api.csproj --no-launch-profile
```

Run web without helper script:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5275"
dotnet run --project .\src\CropQc.Web\CropQc.Web.csproj --no-launch-profile
```

Override the database connection string for a single PowerShell session:

```powershell
$env:ConnectionStrings__CropQc = "Server=.\SQLEXPRESS;Database=CropQcDashboard;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
```

## Troubleshooting

- If `dotnet ef` is not recognized, install or update `dotnet-ef` and restart PowerShell.
- If LocalDB is not available, install SQL Server Express LocalDB or use SQL Server Express with `-ConnectionString`.
- If ports `5275` or `5276` are already in use, pass another URL to the run script:

```powershell
.\scripts\dev-run-web.ps1 -Url "http://localhost:5285"
.\scripts\dev-run-api.ps1 -Url "http://localhost:5286"
```

- If the web dashboard starts but shows empty data, confirm the database migration ran against the same connection string used by the web app.
