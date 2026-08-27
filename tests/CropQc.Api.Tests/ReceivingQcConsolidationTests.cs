using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class ReceivingQcConsolidationTests
{
    [Theory]
    [InlineData("Truck receipt", "Receiving Sample")]
    [InlineData("Door sample", "Door Sample")]
    [InlineData("Lot sample", "Lot Sample")]
    public void ReceiptTypeMapping_IsExactAndDoesNotUseDatabaseIds(string receiptType, string expected)
    {
        Assert.Equal(expected, ReceiptQcSampleCoordinator.ExpectedSampleTypeName(receiptType));
    }

    [Theory]
    [InlineData("Truck receipt", "Receiving Sample")]
    [InlineData("Door sample", "Door Sample")]
    [InlineData("Lot sample", "Lot Sample")]
    public async Task OpenOrCreate_CreatesOneCorrectSampleThenAlwaysReopensIt(string receiptType, string expectedType)
    {
        await using var db = NewContext();
        var receipt = SeedReceipt(db, receiptType);
        await db.SaveChangesAsync();

        var created = await OpenAsync(db, receipt.Id);
        var reopened = await OpenAsync(db, receipt.Id);

        Assert.True(created.Created);
        Assert.False(reopened.Created);
        Assert.Null(created.Error);
        Assert.Equal(created.Sample!.Id, reopened.Sample!.Id);
        Assert.Equal(1, await db.QcSamples.CountAsync(x => x.ReceiptId == receipt.Id && !x.IsDeleted));
        Assert.Equal(expectedType, (await db.QcSamples.Include(x => x.SampleType).SingleAsync()).SampleType.Name);
        Assert.Equal(1, created.Sample.SampleSequenceNumber);
    }

    [Fact]
    public async Task ConcurrentOpen_ProducesExactlyOneSample()
    {
        var databaseName = $"receiving-qc-concurrency-{Guid.NewGuid():N}";
        long receiptId;
        await using (var seed = NewContext(databaseName))
        {
            var receipt = SeedReceipt(seed, "Truck receipt");
            await seed.SaveChangesAsync();
            receiptId = receipt.Id;
        }

        await using var first = NewContext(databaseName);
        await using var second = NewContext(databaseName);
        var results = await Task.WhenAll(OpenAsync(first, receiptId), OpenAsync(second, receiptId));

        await using var verify = NewContext(databaseName);
        Assert.Equal(1, await verify.QcSamples.CountAsync(x => x.ReceiptId == receiptId && !x.IsDeleted));
        Assert.Single(results, x => x.Created);
        Assert.Equal(results[0].Sample!.Id, results[1].Sample!.Id);
    }

    [Fact]
    public async Task PostgreSql_ConcurrentOpen_UsesSerializableReceiptLockAndCreatesExactlyOneSample()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RECEIVING_QC_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options;
        long receiptId;
        await using (var seed = new CropQcDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            if (!await seed.SampleTypes.AnyAsync(x => x.Name == "Receiving Sample"))
            {
                seed.SampleTypes.AddRange(
                    new SampleType { Name = "Receiving Sample", IsActive = true },
                    new SampleType { Name = "Door Sample", IsActive = true },
                    new SampleType { Name = "Lot Sample", IsActive = true });
            }
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var warehouse = new Warehouse { Code = $"Q{suffix[..3]}", Name = $"QC Test {suffix}", IsActive = true };
            var room = new Room { Warehouse = warehouse, Code = $"R{suffix}", Name = $"QC Room {suffix}", CapacityBins = 100, IsActive = true };
            var fruit = new FruitProfile
            {
                Name = $"QC Fruit {suffix}",
                VarietyCode = $"Q{suffix[..5]}",
                FruitType = "Apple",
                ProductionType = "Conventional",
                IsOrganic = false,
                IsActive = true
            };
            var now = DateTimeOffset.UtcNow;
            var receipt = new Receipt
            {
                CropYear = 2026,
                ReceivedAt = now,
                CompuTechReceiptId = $"PG-QC-{suffix}",
                ReceiptType = "Truck receipt",
                Warehouse = warehouse,
                Room = room,
                FruitProfile = fruit,
                GrowerName = "PostgreSQL Test Grower",
                LotCode = suffix,
                BinCount = 10,
                CreatedAt = now,
                UpdatedAt = now
            };
            seed.Receipts.Add(receipt);
            await seed.SaveChangesAsync();
            receiptId = receipt.Id;
        }

        await using var first = new CropQcDbContext(options);
        await using var second = new CropQcDbContext(options);
        var results = await Task.WhenAll(OpenAsync(first, receiptId), OpenAsync(second, receiptId));
        await using var verify = new CropQcDbContext(options);
        Assert.Single(results, x => x.Created);
        Assert.Equal(results[0].Sample!.Id, results[1].Sample!.Id);
        Assert.Equal(1, await verify.QcSamples.CountAsync(x => x.ReceiptId == receiptId && !x.IsDeleted));
    }

    [Fact]
    public async Task HistoricalDuplicate_FailsClosedWithoutChangingAnything()
    {
        await using var db = NewContext();
        var receipt = SeedReceipt(db, "Truck receipt");
        await db.SaveChangesAsync();
        var type = await db.SampleTypes.SingleAsync(x => x.Name == "Receiving Sample");
        db.QcSamples.AddRange(Sample(receipt.Id, type.Id, 1), Sample(receipt.Id, type.Id, 2));
        await db.SaveChangesAsync();

        var result = await OpenAsync(db, receipt.Id);

        Assert.True(result.HistoricalConflict);
        Assert.Equal(ReceiptQcSampleCoordinator.HistoricalConflictMessage, result.Error);
        Assert.Null(result.Sample);
        Assert.Equal(2, await db.QcSamples.CountAsync(x => x.ReceiptId == receipt.Id && !x.IsDeleted));
    }

    [Fact]
    public async Task MissingInactiveOrAmbiguousSampleType_FailsSafelyWithoutCreatingSample()
    {
        await using var db = NewContext(seedSampleTypes: false);
        var receipt = SeedReceipt(db, "Truck receipt");
        db.SampleTypes.Add(new SampleType { Name = "Receiving Sample", IsActive = false });
        await db.SaveChangesAsync();

        var missing = await OpenAsync(db, receipt.Id);
        Assert.Contains("must be configured exactly once", missing.Error);
        Assert.Empty(await db.QcSamples.ToListAsync());

        db.SampleTypes.AddRange(
            new SampleType { Name = "Receiving Sample", IsActive = true },
            new SampleType { Name = "receiving sample", IsActive = true });
        await db.SaveChangesAsync();
        var ambiguous = await OpenAsync(db, receipt.Id);
        Assert.Contains("must be configured exactly once", ambiguous.Error);
        Assert.Empty(await db.QcSamples.ToListAsync());
    }

    [Fact]
    public async Task RequestedWrongSampleType_FailsClosed()
    {
        await using var db = NewContext();
        var receipt = SeedReceipt(db, "Truck receipt");
        await db.SaveChangesAsync();
        var door = await db.SampleTypes.SingleAsync(x => x.Name == "Door Sample");

        var result = await ReceiptQcSampleCoordinator.OpenOrCreateAsync(
            db, receipt.Id, true, door.Id, null, null, 10, null, null, CancellationToken.None);

        Assert.Contains("Another Sample Type cannot be selected", result.Error);
        Assert.Empty(await db.QcSamples.ToListAsync());
    }

    [Fact]
    public void ViewsAndNavigation_PresentOneReceivingWorkflowAndKeepFieldSamplesDistinct()
    {
        var receiptIndex = Read("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml");
        var receiptDetail = Read("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml");
        var sample = Read("src", "CropQc.Web", "Views", "Samples", "Details.cshtml");
        var navigation = Read("src", "CropQc.Web", "Services", "SiteNavigationService.cs");
        var receiptsController = Read("src", "CropQc.Web", "Controllers", "ReceiptsController.cs");

        Assert.Contains("Open Receiving", receiptIndex);
        Assert.Contains("View Receipt Summary", receiptIndex);
        Assert.DoesNotContain("Add Sample", receiptDetail);
        Assert.DoesNotContain("_DeviceCapturePanel", receiptDetail);
        Assert.Contains("Receiving QC Workspace", sample);
        Assert.Contains("Receiving Treatments", sample);
        Assert.DoesNotContain("new(\"qc\"", navigation);
        Assert.Contains("new(\"field-samples\", \"receiving\"", navigation);
        Assert.Contains("new(\"receipt-qc\", \"receiving\"", navigation);
        Assert.Contains("allowCreate: true", receiptsController);
        Assert.Contains("ApplicationAreas.DailyQc", receiptsController);
        Assert.Contains("PageAccessLevel.View", receiptsController);
    }

    [Fact]
    public void HistoricalDuplicateAudit_IsExplicitlyReadOnlyAndAvailableAsACommand()
    {
        var coordinator = Read("src", "CropQc.Data", "ReceiptQcSampleCoordinator.cs");
        var program = Read("src", "CropQc.Web", "Program.cs");

        Assert.Contains("--audit-receipt-qc-samples", program);
        Assert.Contains("GetHistoricalDuplicateAuditAsync", program);
        Assert.Contains("AsNoTracking()", coordinator);
        Assert.Contains("EnteredFruitCount", coordinator);
        Assert.Contains("PhotoCount", coordinator);
        Assert.Contains("EmailStatus", coordinator);
        Assert.DoesNotContain("RemoveRange", coordinator);
        Assert.DoesNotContain("ExecuteDelete", coordinator);
    }

    [Fact]
    public void PhotoReclassification_IsAntiforgeryProtectedPolicyDrivenAndMetadataOnly()
    {
        var samples = Read("src", "CropQc.Web", "Controllers", "SamplesController.cs");
        var fields = Read("src", "CropQc.Web", "Controllers", "FieldSamplesController.cs");
        var service = Read("src", "CropQc.Web", "Services", "DashboardDataService.cs");
        var script = Read("src", "CropQc.Web", "wwwroot", "js", "photo-reclassification.js");

        Assert.Contains("[ValidateAntiForgeryToken]", samples);
        Assert.Contains("[Authorize(Policy = AccessPolicyNames.DailyQcEdit)]", samples);
        Assert.Contains("[ValidateAntiForgeryToken]", fields);
        Assert.Contains("[Authorize(Policy = AccessPolicyNames.FieldSamplesEdit)]", fields);
        Assert.Contains("photoRequirementPolicy.GetAvailablePhotoTypes", service);
        Assert.Contains("ReceiptQcSampleCoordinator.HistoricalConflictMessage", service);
        Assert.Contains("photo.PhotoType = normalizedTarget", service);
        Assert.Contains("photo.ReceiptId = targetReceiptId", service);
        Assert.Contains("photo.QcSampleId = targetSampleId", service);
        Assert.Contains("photo.FileId", service);
        Assert.Contains("card.dataset.photoType = targetPhotoType", script);
        Assert.Contains("throw new Error", script);
    }

    private static Task<ReceiptQcSampleOpenResult> OpenAsync(CropQcDbContext db, long receiptId) =>
        ReceiptQcSampleCoordinator.OpenOrCreateAsync(
            db, receiptId, true, null, null, null, 10, null, null, CancellationToken.None);

    private static CropQcDbContext NewContext(string? databaseName = null, bool seedSampleTypes = true)
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"receiving-qc-{Guid.NewGuid():N}")
            .Options;
        var db = new CropQcDbContext(options);
        if (seedSampleTypes)
        {
            db.SampleTypes.AddRange(
                new SampleType { Name = "Receiving Sample", IsActive = true },
                new SampleType { Name = "Door Sample", IsActive = true },
                new SampleType { Name = "Lot Sample", IsActive = true });
        }
        return db;
    }

    private static Receipt SeedReceipt(CropQcDbContext db, string receiptType)
    {
        var now = DateTimeOffset.UtcNow;
        var receipt = new Receipt
        {
            CropYear = 2026,
            ReceivedAt = now,
            CompuTechReceiptId = $"TEST-{Guid.NewGuid():N}",
            ReceiptType = receiptType,
            WarehouseId = 1,
            RoomId = 1,
            FruitProfileId = 1,
            GrowerName = "Test Grower",
            LotCode = "1000",
            BinCount = 20,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Receipts.Add(receipt);
        return receipt;
    }

    private static QcSample Sample(long receiptId, int sampleTypeId, int sequence)
    {
        var now = DateTimeOffset.UtcNow;
        return new QcSample
        {
            ReceiptId = receiptId,
            SampleTypeId = sampleTypeId,
            SampleSequenceNumber = sequence,
            Status = "Data Entry In Progress",
            StarchStatus = "Starch Pending",
            PhotoStatus = "Photo Pending",
            EmailStatus = "Not Sent",
            SampleTakenAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
