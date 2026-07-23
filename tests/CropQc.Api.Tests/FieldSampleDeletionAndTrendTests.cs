using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class FieldSampleDeletionAndTrendTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-23T14:30:00Z");

    [Fact]
    public async Task DeleteAsync_SoftDeletesSampleAndPhotoWhilePreservingOperationalHistory()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, 101, "North", Now.AddDays(-1));
        var row = new QcFruitReading
        {
            QcSampleId = sample.Id,
            RowNumber = 1,
            WeightGrams = 250m,
            SizeCategory = 80,
            SizeStatus = "Calculated",
            DefectsInspected = true,
            CreatedAt = Now.AddDays(-1)
        };
        row.Defects.Add(new QcFruitDefect { DefectTypeId = 1 });
        db.QcFruitReadings.Add(row);
        db.QcPhotos.Add(new QcPhoto
        {
            QcSampleId = sample.Id,
            PhotoType = "CutFruit",
            PhotoSource = "BrowserUpload",
            FileName = "retained.jpg",
            ContentType = "image/jpeg",
            StorageProvider = "GoogleDrive",
            FileId = "drive-file-retained",
            SharePointDriveId = "",
            SharePointItemId = "",
            CapturedAt = Now.AddHours(-3)
        });
        db.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
        {
            QcSampleId = sample.Id,
            FromAddress = "sender@example.com",
            ToAddress = "qc@fruitandland.com",
            Subject = "Existing report",
            Status = "Sent",
            CreatedAt = Now.AddHours(-2),
            SentAt = Now.AddHours(-2)
        });
        db.BackupRunRecords.Add(VerifiedBackup());
        await db.SaveChangesAsync();

        var service = DeletionService(db);
        var confirmation = await service.GetConfirmationAsync(sample.Id, CancellationToken.None);
        Assert.NotNull(confirmation);
        Assert.Equal(1, confirmation.Dependencies.FruitRows);
        Assert.Equal(1, confirmation.Dependencies.Defects);
        Assert.Equal(1, confirmation.Dependencies.Photos);
        Assert.Equal(1, confirmation.Dependencies.EmailLogs);

        var error = await service.DeleteAsync(new DeleteFieldSampleForm
        {
            Id = sample.Id,
            ConfirmationValue = sample.Id.ToString(),
            Reason = "Duplicate training sample entered by mistake.",
            ConfirmDeletion = true,
            OperationToken = Guid.NewGuid().ToString("D"),
            VerifiedBackupRunId = 77
        }, Owner(), CancellationToken.None);

        Assert.Null(error);
        var deleted = await db.QcSamples.SingleAsync(x => x.Id == sample.Id);
        Assert.True(deleted.IsDeleted);
        Assert.Equal("Duplicate training sample entered by mistake.", deleted.DeleteReason);
        var photo = await db.QcPhotos.SingleAsync();
        Assert.True(photo.IsDeleted);
        Assert.Equal("drive-file-retained", photo.FileId);
        Assert.Single(await db.QcFruitReadings.ToListAsync());
        Assert.Single(await db.QcFruitDefects.ToListAsync());
        Assert.Single(await db.QcSummaryEmailLogs.ToListAsync());
        var deletionAudit = await db.FieldSampleDeletionAudits.SingleAsync();
        Assert.Equal(77, deletionAudit.BackupRunId);
        Assert.Contains("-07:00", deletionAudit.DeletedAtPacific);
        Assert.Contains("\"lifecycleStatus\"", deletionAudit.IdentifyingFieldsJson);
        Assert.Contains("\"photoBinariesRetained\": true", (await db.AuditLogs.SingleAsync(x => x.Action == "Delete")).AfterValuesJson);
    }

    [Fact]
    public async Task DeleteAsync_RequiresAuthorizationIdentifierReasonBackupAndSecondConfirmation()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, 102, "North", Now);
        db.BackupRunRecords.Add(VerifiedBackup());
        await db.SaveChangesAsync();
        var service = DeletionService(db);
        var form = new DeleteFieldSampleForm
        {
            Id = sample.Id,
            ConfirmationValue = "wrong",
            Reason = "",
            OperationToken = Guid.NewGuid().ToString("D"),
            VerifiedBackupRunId = 77
        };

        Assert.Equal("Field Samples Admin access is required.",
            await service.DeleteAsync(form, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None));
        Assert.Contains("detailed deletion reason",
            await service.DeleteAsync(form, Owner(), CancellationToken.None));
        form.Reason = "Known erroneous sample.";
        Assert.Equal("Select the second confirmation before deleting the Field Sample.",
            await service.DeleteAsync(form, Owner(), CancellationToken.None));
        form.ConfirmDeletion = true;
        Assert.Contains("exact Field Sample ID", await service.DeleteAsync(form, Owner(), CancellationToken.None));
        form.ConfirmationValue = sample.Id.ToString();
        Assert.Null(await service.DeleteAsync(form, Owner(), CancellationToken.None));
        Assert.Equal("This deletion request was already processed.",
            await service.DeleteAsync(form, Owner(), CancellationToken.None));
        Assert.Single(await db.FieldSampleDeletionAudits.ToListAsync());
        Assert.True((await db.QcSamples.SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_RejectsMissingCurrentVerifiedBackup()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, 109, "North", Now);
        var service = DeletionService(db);
        var form = new DeleteFieldSampleForm
        {
            Id = sample.Id,
            ConfirmationValue = sample.Id.ToString(),
            Reason = "Known erroneous sample.",
            ConfirmDeletion = true,
            OperationToken = Guid.NewGuid().ToString("D"),
            VerifiedBackupRunId = 999
        };

        Assert.Contains("verified backup gate", await service.DeleteAsync(form, Owner(), CancellationToken.None));
        Assert.False((await db.QcSamples.SingleAsync()).IsDeleted);
        Assert.Empty(await db.FieldSampleDeletionAudits.ToListAsync());
    }

    [Fact]
    public async Task Index_DefaultsToActiveAndAdminCanInspectDeletedRecords()
    {
        await using var db = CreateDbContext();
        await SeedSampleAsync(db, 103, "North", Now);
        var deleted = await SeedSampleAsync(db, 104, "South", Now.AddDays(-1));
        deleted.IsDeleted = true;
        deleted.DeletedAt = Now;
        await db.SaveChangesAsync();
        var service = FieldService(db);

        var active = await service.GetIndexAsync(new FieldSampleSearchForm(), Owner(), CancellationToken.None);
        Assert.Single(active.Samples);
        Assert.False(active.Samples[0].IsDeleted);
        Assert.True(active.CanAdminister);

        var deletedOnly = await service.GetIndexAsync(
            new FieldSampleSearchForm { DeletionStatus = "Deleted" },
            Owner(),
            CancellationToken.None);
        Assert.Single(deletedOnly.Samples);
        Assert.True(deletedOnly.Samples[0].IsDeleted);
        Assert.False(deletedOnly.Samples[0].CanEdit);
        Assert.Empty(deletedOnly.BlockTrends);
    }

    [Fact]
    public async Task TrendService_BuildsOneThirtyDayCardPerConfirmedBlockAndExcludesDeleted()
    {
        await using var db = CreateDbContext();
        var current = await SeedSampleAsync(db, 105, "North", Now);
        var prior = await SeedSampleAsync(db, 106, "North", Now.AddDays(-7), existingBlock: current.CanonicalOrchardBlock);
        var old = await SeedSampleAsync(db, 107, "North", Now.AddDays(-40), existingBlock: current.CanonicalOrchardBlock);
        var deleted = await SeedSampleAsync(db, 108, "North", Now.AddDays(-2), existingBlock: current.CanonicalOrchardBlock);
        deleted.IsDeleted = true;
        foreach (var pair in new[] { (current.Id, 15m), (prior.Id, 16m), (old.Id, 22m), (deleted.Id, 2m) })
        {
            db.QcFruitReadings.Add(new QcFruitReading
            {
                QcSampleId = pair.Id,
                RowNumber = 1,
                Pressure1Lbs = pair.Item2,
                Pressure2Lbs = pair.Item2,
                SizeStatus = "NotCalculated",
                CreatedAt = Now
            });
        }
        await db.SaveChangesAsync();

        var service = new FieldSampleTrendService(db);
        var reportTrend = await service.GetForSampleAsync(current.Id, CancellationToken.None);
        Assert.NotNull(reportTrend);
        Assert.Equal([prior.Id, current.Id], reportTrend.Points.Select(x => x.SampleId).ToArray());

        var cards = await service.GetCardsAsync([current.Id, prior.Id, old.Id, deleted.Id], CancellationToken.None);
        var card = Assert.Single(cards);
        Assert.Equal([prior.Id, current.Id], card.Points.Select(x => x.SampleId).ToArray());
        Assert.Equal(15m, card.Latest!.Summary.AveragePressureLbs);
    }

    [Fact]
    public async Task TrendService_SeparatesCanonicalBlocksAndIgnoresUnconfirmedText()
    {
        await using var db = CreateDbContext();
        var north = await SeedSampleAsync(db, 110, "Block 1", Now);
        var south = await SeedSampleAsync(db, 111, "Block 1", Now.AddHours(-1));
        var unconfirmed = await SeedSampleAsync(db, 112, "Block 1", Now.AddHours(-2));
        unconfirmed.CanonicalOrchardBlock = null;
        unconfirmed.CanonicalOrchardBlockId = null;
        unconfirmed.FieldSampleBlockResolution = "Suggested";
        await db.SaveChangesAsync();

        var service = new FieldSampleTrendService(db);
        var cards = await service.GetCardsAsync([north.Id, south.Id, unconfirmed.Id], CancellationToken.None);

        Assert.Equal(2, cards.Count);
        Assert.All(cards, x => Assert.Single(x.Points));
        Assert.DoesNotContain(cards.SelectMany(x => x.Points), x => x.SampleId == unconfirmed.Id);
        Assert.Equal(north.Id, cards[0].Latest!.SampleId);
    }

    [Fact]
    public void Views_KeepHistoricalTrendOffDetailAndPutSharedCardsOnIndex()
    {
        var detail = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "FieldSamples", "Details.cshtml"));
        var index = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "FieldSamples", "Index.cshtml"));
        var report = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "FieldSampleReportService.cs"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "FieldSamplesController.cs"));

        Assert.DoesNotContain("Weight Trend", detail);
        Assert.DoesNotContain("Pressure Trend", detail);
        Assert.DoesNotContain("Starch Trend", detail);
        Assert.DoesNotContain("Size Trend", detail);
        Assert.True(detail.IndexOf("Final Sample Summary", StringComparison.Ordinal) > detail.IndexOf("field-sample-rows-form", StringComparison.Ordinal));
        Assert.Contains("_BlockTrendCard", index);
        Assert.Contains("data-prevent-double-submit", File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "FieldSamples", "Delete.cshtml")));
        Assert.Contains("AppendTrend(html, detail)", report);
        Assert.Contains("AccessPolicyNames.FieldSamplesAdmin", controller);
        Assert.Contains("[ValidateAntiForgeryToken]", controller);
    }

    private static FieldSampleDeletionService DeletionService(CropQcDbContext db)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new FieldSampleDeletionService(
            db,
            new UserAccessService(db, configuration),
            new PacificBusinessTimeService(new FixedClock(Now)));
    }

    private static FieldSampleService FieldService(CropQcDbContext db)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new FieldSampleService(
            db,
            new UserAccessService(db, configuration),
            configuration,
            new PacificBusinessTimeService(new FixedClock(Now)),
            new FieldSampleTrendService(db));
    }

    private static CropQcDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<QcSample> SeedSampleAsync(
        CropQcDbContext db,
        long id,
        string blockName,
        DateTimeOffset takenAt,
        CanonicalOrchardBlock? existingBlock = null)
    {
        if (!await db.SampleTypes.AnyAsync())
        {
            db.SampleTypes.Add(new SampleType { Id = 5, Name = "Field Sample" });
            db.FruitProfiles.Add(new FruitProfile
            {
                Id = 1,
                Name = "Gala",
                VarietyCode = "GALA",
                FruitType = "Apple",
                ProductionType = "Conventional"
            });
            db.DefectTypes.Add(new DefectType { Id = 1, Name = "Bruise", IsActive = true });
        }

        var block = existingBlock;
        if (block is null)
        {
            var orchard = new CanonicalOrchard
            {
                OrchardName = "WP ORCHARD",
                NormalizedOrchardKey = "WP ORCHARD",
                CreatedAt = Now,
                UpdatedAt = Now
            };
            block = new CanonicalOrchardBlock
            {
                CanonicalOrchard = orchard,
                OrchardName = orchard.OrchardName,
                CanonicalBlockName = blockName,
                NormalizedOrchardKey = orchard.NormalizedOrchardKey,
                NormalizedBlockKey = blockName.ToUpperInvariant(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.CanonicalOrchardBlocks.Add(block);
        }

        var sample = new QcSample
        {
            Id = id,
            SampleTypeId = 5,
            FieldSampleFruitProfileId = 1,
            CanonicalOrchardBlock = block,
            FieldSampleGrowerName = "WP ORCHARD",
            FieldSampleGrowerNumber = "1080",
            FieldSampleOriginalBlockName = blockName,
            Status = "Data Entry In Progress",
            StarchStatus = "Starch Pending",
            PhotoStatus = "Not Required",
            EmailStatus = "Not Sent",
            ActualSampleSize = 10,
            SampleTakenAt = takenAt,
            CreatedAt = takenAt,
            UpdatedAt = takenAt
        };
        db.QcSamples.Add(sample);
        await db.SaveChangesAsync();
        return sample;
    }

    private static BackupRunRecord VerifiedBackup() =>
        new()
        {
            Id = 77,
            BackupType = BackupRunTypes.PreDeployment,
            Status = BackupRunStatuses.Succeeded,
            EnvironmentName = "Production",
            DatabaseProvider = "Npgsql",
            RetentionCategory = BackupRunTypes.PreDeployment,
            StartedAt = Now.AddMinutes(-20),
            CompletedAt = Now.AddMinutes(-10),
            VerifiedAt = Now.AddMinutes(-9),
            RetentionProcessedAt = Now.AddMinutes(-8),
            LeaseReleasedAt = Now.AddMinutes(-7),
            PackageFileName = "cropqc-production-predeployment.zip",
            PackageStorageKey = "restricted-backups/file",
            FileSizeBytes = 12345,
            Sha256 = new string('a', 64)
        };

    private static ClaimsPrincipal Owner() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new FileNotFoundException("Could not find repository file.", Path.Combine(parts));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
