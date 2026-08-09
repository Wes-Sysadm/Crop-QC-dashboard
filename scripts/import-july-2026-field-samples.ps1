param(
    [switch]$Apply,
    [switch]$ConfirmProduction,
    [string]$Provider = $(if ($env:DATABASE_PROVIDER) { $env:DATABASE_PROVIDER } elseif ($env:Database__Provider) { $env:Database__Provider } else { "SqlServer" }),
    [string]$ConnectionString = $env:ConnectionStrings__CropQc,
    [string]$EnvironmentName = $(if ($env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT } elseif ($env:DOTNET_ENVIRONMENT) { $env:DOTNET_ENVIRONMENT } else { "Development" })
)

$ErrorActionPreference = "Stop"

function Convert-DatabaseUrlToConnectionString {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch "^postgres(ql)?://") {
        return $Value
    }

    $dbUrl = [Uri]$Value
    $userInfo = $dbUrl.UserInfo.Split(':', 2)
    $user = [Uri]::UnescapeDataString($userInfo[0])
    $pass = if ($userInfo.Length -gt 1) { [Uri]::UnescapeDataString($userInfo[1]) } else { "" }
    $dbName = $dbUrl.AbsolutePath.TrimStart('/')
    $port = if ($dbUrl.Port -gt 0) { $dbUrl.Port } else { 5432 }
    return "Host=$($dbUrl.Host);Port=$port;Database=$dbName;Username=$user;Password=$pass;SSL Mode=Require;Trust Server Certificate=true"
}

function Get-MaskedTarget {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "(default connection string)"
    }

    return ($Value `
        -replace '(?i)(Password|Pwd)\s*=\s*[^;]+', '$1=***' `
        -replace '(?i)(User\s*Id|Username|UID)\s*=\s*[^;]+', '$1=***' `
        -replace '://([^:/@]+):([^@]+)@', '://***:***@')
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$dataProject = Join-Path $repoRoot "src\CropQc.Data\CropQc.Data.csproj"
$webProject = Join-Path $repoRoot "src\CropQc.Web\CropQc.Web.csproj"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "cropqc-july-2026-field-sample-importer"
$projectPath = Join-Path $tempRoot "CropQc.July2026FieldSampleImporter.csproj"
$programPath = Join-Path $tempRoot "Program.cs"
$resolvedConnectionString = Convert-DatabaseUrlToConnectionString $ConnectionString

Write-Host "July 2026 Field Sample importer"
Write-Host "Environment: $EnvironmentName"
Write-Host "Provider: $Provider"
Write-Host "Target: $(Get-MaskedTarget $resolvedConnectionString)"
Write-Host "Mode: $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })"

if ($EnvironmentName -eq "Production" -and -not $ConfirmProduction) {
    throw "Refusing to run against Production without -ConfirmProduction. Re-run with -ConfirmProduction after verifying the target database."
}

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$dataProject" />
    <ProjectReference Include="$webProject" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $projectPath -Encoding UTF8

@'
using System.Text.Json;
using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

const string SourceLabel = "Historical field sample image import - July 2026";
const string OrchardName = "WP ORCHARD";
const string WarehouseCode = "WP";
const string CropYear = "2026";
const string GrowerNumber = "1080";
const string FieldSampleTypeName = "Field Sample";

var provider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER") ?? "SqlServer";
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CropQc");
var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
var apply = string.Equals(Environment.GetEnvironmentVariable("CROPQC_IMPORT_APPLY"), "true", StringComparison.OrdinalIgnoreCase);

if (OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(OrchardName))
{
    throw new InvalidOperationException("The configured orchard is a four-digit grower number. Keep orchard and grower number separate.");
}

var samples = BuildSamples();
Console.WriteLine();
Console.WriteLine("Dry-run summary");
Console.WriteLine($"  Source label: {SourceLabel}");
Console.WriteLine($"  Unique samples: {samples.Count}");
Console.WriteLine($"  Fruit rows: {samples.Sum(sample => sample.Rows.Count)}");
Console.WriteLine($"  Blocks: {string.Join(", ", samples.Select(sample => sample.Block).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))}");
Console.WriteLine();

var optionsBuilder = new DbContextOptionsBuilder<CropQcDbContext>();
CropQcDatabase.Configure(optionsBuilder, provider, connectionString);
await using var db = new CropQcDbContext(optionsBuilder.Options);

var connection = db.Database.GetDbConnection();
Console.WriteLine($"Target database");
Console.WriteLine($"  Environment: {environmentName}");
Console.WriteLine($"  Provider: {provider}");
Console.WriteLine($"  Data source: {connection.DataSource}");
Console.WriteLine($"  Database: {connection.Database}");
Console.WriteLine();

var fieldSampleType = await db.SampleTypes.SingleOrDefaultAsync(x => x.Name == FieldSampleTypeName && x.IsActive)
    ?? throw new InvalidOperationException("Active Field Sample sample type was not found.");
var fruitProfiles = await db.FruitProfiles.AsNoTracking().ToListAsync();
var grades = await db.Grades.Where(x => x.IsActive).ToListAsync();
var gradeByCode = grades.ToDictionary(x => x.Code.Trim(), x => x, StringComparer.OrdinalIgnoreCase);
var fieldSampleService = new FieldSampleService(db, new ImportAccessService(), new ConfigurationBuilder().Build());
var importUser = new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim(ClaimTypes.Email, "historical-field-sample-import@cropqc.local"),
    new Claim(ClaimTypes.Name, "Historical Field Sample Import")
], "HistoricalImport"));

var missingVarieties = samples
    .Select(x => x.Variety)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Where(variety => ResolveFruitProfile(fruitProfiles, variety) is null)
    .ToList();
if (missingVarieties.Count > 0)
{
    throw new InvalidOperationException($"Missing fruit profiles for varieties: {string.Join(", ", missingVarieties)}.");
}

var missingGrades = samples
    .SelectMany(x => x.Rows)
    .Select(x => x.Grade)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Where(grade => !gradeByCode.ContainsKey(grade))
    .ToList();
if (missingGrades.Count > 0)
{
    if (!apply)
    {
        Console.WriteLine($"  Missing active grades that would be created on apply: {string.Join(", ", missingGrades)}");
    }
    else
    {
        foreach (var grade in missingGrades)
        {
            var existingGrade = await db.Grades.SingleOrDefaultAsync(x => x.Code == grade);
            if (existingGrade is null)
            {
                existingGrade = new Grade { Code = grade, Name = grade, IsActive = true };
                db.Grades.Add(existingGrade);
            }
            else if (!existingGrade.IsActive)
            {
                existingGrade.IsActive = true;
            }
        }

        await db.SaveChangesAsync();
        grades = await db.Grades.Where(x => x.IsActive).ToListAsync();
        gradeByCode = grades.ToDictionary(x => x.Code.Trim(), x => x, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"  Created or reactivated missing grades: {string.Join(", ", missingGrades)}");
    }
}

var beforeCounts = new SideEffectCounts(
    await db.Receipts.CountAsync(),
    await db.RoomInventoryAdjustments.CountAsync(),
    await db.BinsRunEntries.CountAsync(),
    await db.QcSummaryEmailLogs.CountAsync());

var plan = new List<PlannedSample>();
foreach (var sample in samples)
{
    var fruitProfile = ResolveFruitProfile(fruitProfiles, sample.Variety)!;
    var orchardKey = OrchardBlockMatcher.Normalize(OrchardName);
    var blockKey = OrchardBlockMatcher.Normalize(sample.Block);
    var block = await db.CanonicalOrchardBlocks.AsNoTracking()
        .SingleOrDefaultAsync(x => x.NormalizedOrchardKey == orchardKey && x.NormalizedBlockKey == blockKey);
    var existing = block is null
        ? null
        : await FindExistingSampleAsync(db, fieldSampleType.Id, block.Id, fruitProfile.Id, sample);

    plan.Add(new PlannedSample(sample, fruitProfile, block, existing));
}

Console.WriteLine("Plan");
foreach (var item in plan)
{
    var status = item.Existing is null ? "create" : $"skip existing sample {item.Existing.Id}";
    var blockStatus = item.Block is null ? "create block" : $"reuse block {item.Block.Id}";
    Console.WriteLine($"  {item.Sample.SampleDate:yyyy-MM-dd} | {item.Sample.Block} | {item.Sample.Variety} | rows {item.Sample.Rows.Count} | {blockStatus} | {status}");
}
Console.WriteLine();

if (!apply)
{
    Console.WriteLine("Dry run complete. Re-run with -Apply to import. Production also requires -ConfirmProduction.");
    return;
}

var created = 0;
var skipped = 0;
var failed = 0;
var createdIds = new List<long>();

foreach (var item in plan)
{
    if (item.Existing is not null)
    {
        skipped++;
        continue;
    }

    await using var transaction = await db.Database.BeginTransactionAsync();
    try
    {
        var block = await db.CanonicalOrchardBlocks
            .SingleOrDefaultAsync(x => x.NormalizedOrchardKey == OrchardBlockMatcher.Normalize(OrchardName)
                && x.NormalizedBlockKey == OrchardBlockMatcher.Normalize(item.Sample.Block));
        var existing = block is null
            ? null
            : await FindExistingSampleAsync(db, fieldSampleType.Id, block.Id, item.FruitProfile.Id, item.Sample);
        if (existing is not null)
        {
            await transaction.CommitAsync();
            skipped++;
            continue;
        }

        var now = DateTimeOffset.UtcNow;
        var sampleTakenAt = new DateTimeOffset(item.Sample.SampleDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var (sampleId, createError) = await fieldSampleService.CreateAsync(new FieldSampleCreateForm
        {
            OrchardName = OrchardName,
            GrowerNumber = GrowerNumber,
            BlockName = item.Sample.Block,
            FruitProfileId = item.FruitProfile.Id,
            SampleTakenAt = sampleTakenAt,
            ConfirmCreateNewBlock = true
        }, importUser, CancellationToken.None);
        if (createError is not null || sampleId is null)
        {
            throw new InvalidOperationException(createError ?? "Field Sample service did not return a sample ID.");
        }

        var sample = await db.QcSamples
            .Include(x => x.FruitReadings)
            .SingleAsync(x => x.Id == sampleId.Value);
        db.QcFruitReadings.RemoveRange(sample.FruitReadings);
        await db.SaveChangesAsync();

        sample.Status = "Complete";
        sample.StarchStatus = "Starch Pending";
        sample.PhotoStatus = "Not Required";
        sample.EmailStatus = "Not Applicable";
        sample.ActualSampleSize = item.Sample.Rows.Count;
        sample.FieldSampleBlockResolution = "HistoricalImport";
        sample.Notes = BuildNotes(item.Sample);
        sample.UpdatedAt = now;

        foreach (var row in item.Sample.Rows)
        {
            db.QcFruitReadings.Add(new QcFruitReading
            {
                QcSampleId = sample.Id,
                RowNumber = row.Row,
                Pressure1Lbs = row.PressureA,
                Pressure1Source = SourceLabel,
                Pressure2Lbs = row.PressureB,
                Pressure2Source = SourceLabel,
                WeightGrams = row.Weight,
                SizeCategory = row.Size,
                SizeStatus = "Imported",
                GradeId = gradeByCode[row.Grade].Id,
                IsCompleted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog
        {
            Action = "import",
            EntityName = nameof(QcSample),
            EntityKey = sample.Id.ToString(),
            BeforeValuesJson = null,
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                SourceLabel,
                WarehouseCode,
                CropYear,
                GrowerNumber,
                item.Sample.DoneBy,
                OrchardName,
                item.Sample.Block,
                item.Sample.Variety,
                item.Sample.SampleDate,
                RowCount = item.Sample.Rows.Count,
                Rows = item.Sample.Rows
            }),
            SourceApplication = SourceLabel,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        created++;
        createdIds.Add(sample.Id);
    }
    catch (Exception ex)
    {
        failed++;
        await transaction.RollbackAsync();
        Console.Error.WriteLine($"FAILED: {item.Sample.SampleDate:yyyy-MM-dd} {item.Sample.Block} {item.Sample.Variety}: {ex.Message}");
    }
}

await VerifyImportAsync(db, beforeCounts, fieldSampleType.Id, samples);

Console.WriteLine();
Console.WriteLine("Import result");
Console.WriteLine($"  Created: {created}");
Console.WriteLine($"  Skipped: {skipped}");
Console.WriteLine($"  Failed: {failed}");
Console.WriteLine($"  Created IDs: {(createdIds.Count == 0 ? "(none)" : string.Join(", ", createdIds))}");

if (failed > 0)
{
    Environment.ExitCode = 1;
}

static FruitProfile? ResolveFruitProfile(IReadOnlyList<FruitProfile> fruitProfiles, string variety)
{
    var matches = fruitProfiles
        .Where(x => string.Equals(x.VarietyCode, variety, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (matches.Count == 0)
    {
        matches = fruitProfiles
            .Where(x => string.Equals(x.Name, variety, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    return matches.Count == 1 ? matches[0] : null;
}

static async Task<QcSample?> FindExistingSampleAsync(CropQcDbContext db, int fieldSampleTypeId, int blockId, int fruitProfileId, ImportSample sample)
{
    var start = new DateTimeOffset(sample.SampleDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    var end = start.AddDays(1);
    return await db.QcSamples.AsNoTracking()
        .Where(x => !x.IsDeleted
            && x.ReceiptId == null
            && x.SampleTypeId == fieldSampleTypeId
            && x.CanonicalOrchardBlockId == blockId
            && x.FieldSampleFruitProfileId == fruitProfileId
            && x.FieldSampleGrowerName == OrchardName
            && x.FieldSampleOriginalBlockName == sample.Block
            && x.SampleTakenAt >= start
            && x.SampleTakenAt < end
            && x.Notes != null
            && x.Notes.Contains(SourceLabel))
        .FirstOrDefaultAsync();
}

static string BuildNotes(ImportSample sample) =>
    string.Join(Environment.NewLine, new[]
    {
        SourceLabel,
        $"Warehouse/facility: {WarehouseCode}",
        $"Crop year: {CropYear}",
        $"Grower number/source reference: {GrowerNumber}",
        $"Done by: {sample.DoneBy}",
        sample.Note
    }.Where(x => !string.IsNullOrWhiteSpace(x)));

static async Task VerifyImportAsync(CropQcDbContext db, SideEffectCounts beforeCounts, int fieldSampleTypeId, IReadOnlyList<ImportSample> sourceSamples)
{
    var sourceSamplesInDb = await db.QcSamples.AsNoTracking()
        .Include(x => x.CanonicalOrchardBlock)
        .Include(x => x.FieldSampleFruitProfile)
        .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
        .Where(x => x.SampleTypeId == fieldSampleTypeId && x.Notes != null && x.Notes.Contains(SourceLabel))
        .ToListAsync();

    if (sourceSamplesInDb.Count != 8)
    {
        throw new InvalidOperationException($"Verification failed: expected 8 imported Field Samples, found {sourceSamplesInDb.Count}.");
    }

    var tennisCourtCount = sourceSamplesInDb.Count(x => x.FieldSampleOriginalBlockName == "TENNIS COURT BLOCK");
    if (tennisCourtCount != 1)
    {
        throw new InvalidOperationException($"Verification failed: expected one Tennis Court sample, found {tennisCourtCount}.");
    }

    var claustrophobia = sourceSamplesInDb
        .Where(x => x.CanonicalOrchardBlock?.CanonicalBlockName == "CLAUSTROPHOBIA")
        .OrderBy(x => x.SampleTakenAt)
        .ToList();
    if (claustrophobia.Count != 2
        || claustrophobia.Select(x => x.CanonicalOrchardBlockId).Distinct().Count() != 1
        || claustrophobia.Select(x => x.SampleTakenAt.Date).ToArray() is not [var first, var second]
        || first != new DateTime(2026, 7, 14)
        || second != new DateTime(2026, 7, 20))
    {
        throw new InvalidOperationException("Verification failed: Claustrophobia samples were not imported under one canonical block on 2026-07-14 and 2026-07-20.");
    }

    foreach (var expected in sourceSamples)
    {
        var actual = sourceSamplesInDb.Single(x =>
            x.FieldSampleOriginalBlockName == expected.Block
            && x.FieldSampleFruitProfile?.VarietyCode == expected.Variety
            && x.SampleTakenAt.Date == expected.SampleDate.ToDateTime(TimeOnly.MinValue).Date);
        if (actual.ReceiptId is not null || actual.EmailStatus != "Not Applicable" || actual.PhotoStatus != "Not Required")
        {
            throw new InvalidOperationException($"Verification failed: sample {expected.Block} {expected.SampleDate:yyyy-MM-dd} has receipt/email/photo state.");
        }

        if (actual.FruitReadings.Count != expected.Rows.Count)
        {
            throw new InvalidOperationException($"Verification failed: sample {expected.Block} expected {expected.Rows.Count} rows, found {actual.FruitReadings.Count}.");
        }

        foreach (var expectedRow in expected.Rows)
        {
            var row = actual.FruitReadings.Single(x => x.RowNumber == expectedRow.Row);
            if (row.Pressure1Lbs != expectedRow.PressureA
                || row.Pressure2Lbs != expectedRow.PressureB
                || row.WeightGrams != expectedRow.Weight
                || row.SizeCategory != expectedRow.Size
                || !string.Equals(row.Grade?.Code, expectedRow.Grade, StringComparison.OrdinalIgnoreCase)
                || row.StarchScaleValueId is not null)
            {
                throw new InvalidOperationException($"Verification failed: {expected.Block} row {expectedRow.Row} does not match source data.");
            }
        }
    }

    var afterCounts = new SideEffectCounts(
        await db.Receipts.CountAsync(),
        await db.RoomInventoryAdjustments.CountAsync(),
        await db.BinsRunEntries.CountAsync(),
        await db.QcSummaryEmailLogs.CountAsync());
    if (!beforeCounts.Equals(afterCounts))
    {
        throw new InvalidOperationException("Verification failed: receipt, inventory, Bins Run, or email-log counts changed.");
    }

    var importedIds = sourceSamplesInDb.Select(x => x.Id).ToList();
    var photoCount = await db.QcPhotos.CountAsync(x => x.QcSampleId != null && importedIds.Contains(x.QcSampleId.Value));
    if (photoCount != 0)
    {
        throw new InvalidOperationException("Verification failed: imported Field Samples have photo rows.");
    }

    Console.WriteLine("Verification passed");
    Console.WriteLine("  Eight imported Field Samples exist.");
    Console.WriteLine("  Claustrophobia has 2026-07-14 and 2026-07-20 samples under one canonical block.");
    Console.WriteLine("  Tennis Court has only one imported sample.");
    Console.WriteLine("  Row counts and pressure/weight/size/grade values match the source data.");
    Console.WriteLine("  Receipt, inventory, Bins Run, photo, and Receiving email side-effect counts are unchanged.");
}

static IReadOnlyList<ImportSample> BuildSamples() =>
[
    new("2026-07-16", "TENNIS COURT BLOCK", "BART", "MARIA L.", [
        new(1,18.83m,18.17m,154m,120,"US1"), new(2,21.54m,18.18m,114m,165,"US1"), new(3,17.04m,16.89m,108m,180,"US1"),
        new(4,17.60m,17.57m,164m,120,"US1"), new(5,16.52m,16.58m,132m,150,"US1"), new(6,16.14m,16.24m,114m,165,"US1"),
        new(7,18.99m,20.93m,142m,135,"US1"), new(8,17.51m,18.24m,122m,150,"US1"), new(9,18.05m,18.14m,150m,135,"US1"),
        new(10,17.72m,16.92m,108m,180,"US1"), new(11,19.06m,18.73m,150m,135,"US1"), new(12,17.32m,17.83m,128m,150,"US1"),
        new(13,19.91m,19.97m,132m,150,"US1"), new(14,16.99m,17.63m,104m,180,"US1"), new(15,20.56m,18.14m,122m,150,"US1")]),
    new("2026-07-14", "ORG CHIL BLOCK L", "ORBA", "Ada", [
        new(1,19.44m,21.94m,162m,120,"US1"), new(2,17.61m,18.86m,118m,165,"US1"), new(3,21.03m,22.32m,130m,150,"US1"),
        new(4,19.36m,19.23m,132m,150,"US1"), new(5,20.32m,18.92m,118m,165,"US1"), new(6,19.19m,20.56m,120m,165,"US1"),
        new(7,20.57m,19.14m,194m,100,"US1"), new(8,19.17m,17.27m,148m,135,"US1"), new(9,17.34m,18.78m,148m,135,"US1"),
        new(10,19.62m,19.75m,178m,110,"US1B"), new(11,19.09m,18.60m,104m,180,"US1B"), new(12,19.14m,20.36m,140m,135,"US1B"),
        new(13,18.13m,17.53m,152m,120,"FCY"), new(14,20.10m,20.12m,116m,165,"FCY"), new(15,20.32m,20.76m,138m,135,"FCY")]),
    new("2026-07-14", "CLAUSTROPHOBIA", "BART", "Ada", [
        new(1,22.85m,21.23m,168m,110,"US1"), new(2,21.08m,18.72m,146m,135,"US1"), new(3,19.12m,18.01m,180m,110,"US1"),
        new(4,17.81m,16.55m,172m,110,"US1"), new(5,16.75m,18.58m,202m,100,"US1"), new(6,18.86m,19.28m,180m,110,"US1"),
        new(7,18.03m,16.40m,142m,135,"US1"), new(8,17.49m,17.81m,120m,165,"US1"), new(9,19.13m,17.20m,130m,150,"US1"),
        new(10,17.26m,18.78m,134m,150,"US1"), new(11,18.50m,18.01m,114m,165,"US1"), new(12,17.21m,15.80m,128m,150,"US1"),
        new(13,19.31m,18.40m,122m,150,"US1"), new(14,17.89m,19.29m,106m,180,"US1B"), new(15,17.96m,18.65m,94m,193,"FCY")]),
    new("2026-07-14", "CHILEAN POINT BLOCK", "ORBA", "Ada", [
        new(1,17.63m,18.40m,132m,150,"US1"), new(2,17.45m,19.42m,152m,120,"US1"), new(3,17.55m,17.36m,156m,120,"US1"),
        new(4,18.33m,19.01m,106m,180,"US1"), new(5,20.15m,20.88m,158m,120,"US1"), new(6,16.89m,16.88m,150m,135,"US1"),
        new(7,17.60m,17.87m,138m,135,"US1"), new(8,19.74m,19.24m,166m,110,"US1"), new(9,16.94m,16.55m,108m,180,"US1"),
        new(10,17.15m,16.86m,190m,100,"US1"), new(11,17.46m,17.80m,142m,135,"US1"), new(12,15.71m,16.10m,142m,135,"US1"),
        new(13,18.68m,19.28m,112m,165,"US1"), new(14,20.03m,18.93m,136m,135,"US1B"), new(15,18.45m,17.19m,146m,135,"US1B")]),
    new("2026-07-14", "CHILEAN TERRACE", "ORBA", "Ada", [
        new(1,19.15m,19.41m,138m,135,"US1"), new(2,21.08m,17.03m,228m,80,"US1"), new(3,18.11m,19.01m,192m,100,"US1"),
        new(4,19.31m,18.34m,176m,110,"US1"), new(5,19.74m,20.64m,212m,90,"US1"), new(6,18.06m,16.68m,170m,110,"US1"),
        new(7,19.07m,20.05m,164m,120,"US1"), new(8,17.73m,20.14m,140m,135,"US1"), new(9,19.11m,18.84m,138m,135,"US1"),
        new(10,17.62m,20.40m,162m,120,"US1B"), new(11,22.38m,23.15m,100m,193,"US1B"), new(12,18.60m,18.28m,128m,150,"US1B"),
        new(13,17.98m,20.61m,138m,135,"FCY"), new(14,23.64m,21.62m,168m,110,"FCY"), new(15,19.44m,19.41m,136m,135,"FCY")]),
    new("2026-07-14", "CHILEAN BLOCK L.P EXTENSION", "ORBA", "Ada", [
        new(1,18.24m,17.75m,120m,165,"US1"), new(2,19.23m,17.18m,156m,120,"US1"), new(3,17.93m,22.51m,138m,135,"US1"),
        new(4,18.10m,16.82m,144m,135,"US1"), new(5,18.29m,19.86m,174m,110,"US1"), new(6,20.21m,19.80m,156m,120,"US1"),
        new(7,18.31m,19.42m,160m,120,"US1"), new(8,20.91m,19.54m,132m,150,"US1"), new(9,18.17m,18.58m,172m,110,"US1"),
        new(10,17.32m,19.52m,144m,135,"US1"), new(11,19.16m,21.18m,114m,165,"US1"), new(12,19.72m,15.86m,176m,110,"US1B"),
        new(13,16.67m,17.97m,176m,110,"US1B"), new(14,19.86m,21.84m,210m,90,"FCY"), new(15,21.07m,21.85m,130m,150,"FCY")]),
    new("2026-07-14", "YOUNG BLOCK", "BART", "Ada", [
        new(1,19.25m,18.87m,198m,100,"US1"), new(2,16.55m,15.84m,114m,165,"US1"), new(3,18.96m,17.22m,180m,110,"US1"),
        new(4,16.94m,15.88m,146m,135,"US1"), new(5,18.58m,18.67m,122m,150,"US1"), new(6,16.77m,16.44m,154m,120,"US1"),
        new(7,18.37m,16.77m,106m,180,"US1"), new(8,17.42m,18.34m,130m,150,"US1"), new(9,20.25m,20.91m,116m,165,"US1"),
        new(10,18.93m,17.68m,104m,180,"US1"), new(11,18.96m,19.20m,122m,150,"US1B"), new(12,22.02m,22.89m,112m,165,"US1B"),
        new(13,20.11m,19.80m,106m,180,"US1B"), new(14,20.84m,20.65m,108m,180,"US1B"), new(15,20.76m,21.39m,114m,165,"US1B")]),
    new("2026-07-20", "CLAUSTROPHOBIA", "BART", "Ada", [
        new(1,17.33m,17.71m,152m,120,"US1"), new(2,16.97m,17.10m,146m,135,"US1"), new(3,18.42m,19.86m,156m,120,"US1"),
        new(4,18.74m,20.76m,150m,135,"US1"), new(5,17.74m,20.26m,136m,135,"US1"), new(6,17.21m,18.88m,212m,90,"US1"),
        new(7,16.96m,18.20m,130m,150,"US1"), new(8,17.44m,20.08m,144m,135,"US1"), new(9,17.43m,15.89m,92m,210,"US1"),
        new(10,16.58m,16.34m,286m,70,"US1"), new(11,17.90m,19.08m,102m,180,"US1"), new(12,17.31m,16.96m,134m,150,"US1"),
        new(13,19.72m,18.07m,180m,100,"US1B"), new(14,18.77m,16.79m,136m,135,"US1B")],
        "Second sample for the same canonical block; should trend against the 2026-07-14 Claustrophobia sample.")
];

sealed record ImportSample(DateOnly SampleDate, string Block, string Variety, string DoneBy, IReadOnlyList<ImportRow> Rows, string? Note = null)
{
    public ImportSample(string sampleDate, string block, string variety, string doneBy, IReadOnlyList<ImportRow> rows, string? note = null)
        : this(DateOnly.Parse(sampleDate), block, variety, doneBy, rows, note)
    {
    }
}

sealed record ImportRow(int Row, decimal PressureA, decimal PressureB, decimal Weight, int Size, string Grade);
sealed record PlannedSample(ImportSample Sample, FruitProfile FruitProfile, CanonicalOrchardBlock? Block, QcSample? Existing);
sealed record SideEffectCounts(int Receipts, int RoomInventoryAdjustments, int BinsRunEntries, int QcSummaryEmailLogs);

sealed class ImportAccessService : IUserAccessService
{
    public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) =>
        Task.FromResult(areaKey == ApplicationAreas.FieldSamples);

    public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) =>
        Task.FromResult(areaKey == ApplicationAreas.FieldSamples ? PageAccessLevel.Admin : PageAccessLevel.None);

    public void InvalidateAll() { }
}
'@ | Set-Content -Path $programPath -Encoding UTF8

$oldProvider = $env:DATABASE_PROVIDER
$oldConnectionString = $env:ConnectionStrings__CropQc
$oldAspNetCoreEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldApply = $env:CROPQC_IMPORT_APPLY

try {
    $env:DATABASE_PROVIDER = $Provider
    $env:ConnectionStrings__CropQc = $resolvedConnectionString
    $env:ASPNETCORE_ENVIRONMENT = $EnvironmentName
    $env:CROPQC_IMPORT_APPLY = if ($Apply) { "true" } else { "false" }

    dotnet run --project $projectPath
    if ($LASTEXITCODE -ne 0) {
        throw "Importer failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:DATABASE_PROVIDER = $oldProvider
    $env:ConnectionStrings__CropQc = $oldConnectionString
    $env:ASPNETCORE_ENVIRONMENT = $oldAspNetCoreEnvironment
    $env:CROPQC_IMPORT_APPLY = $oldApply
}
