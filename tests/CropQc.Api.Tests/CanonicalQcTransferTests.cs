using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class CanonicalQcTransferTests
{
    [Fact]
    public async Task ReceivingQc_FollowsPartialAndChainedTransfers_AndReversalWithoutDuplication()
    {
        await using var db = CreateDbContext();
        await SeedTransferFruitAsync(db);
        var manager = Principal("manager@fruitandland.com");
        var dashboard = Dashboard(db, manager);
        var binsRun = BinsRun(db);
        var originalCounts = await EvidenceCountsAsync(db);
        var seededReceipt = await db.Receipts.Include(x => x.FruitProfile).SingleAsync(x => x.Id == 990401);
        var seededIdentity = CanonicalQcFruitIdentity.FromReceipt(seededReceipt)!;
        Assert.Single(await CanonicalQcFruitIdentity.FilterReceiptSamples(db.QcSamples, [seededIdentity]).ToListAsync());

        var source = (await dashboard.GetRoomDetailAsync(99101, CancellationToken.None))
            .TransferLotOptions.Single(x => x.Label.Contains("9040", StringComparison.Ordinal));
        Assert.Null(await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "qc-a-to-b",
            FromRoomId = 99101,
            ToRoomId = 99102,
            SourceLotKey = source.LotKey,
            BinCount = 40,
            TransferAt = DateTimeOffset.Parse("2026-08-02T12:00:00Z"),
            Reason = "QC transfer regression A to B"
        }, CancellationToken.None));

        var partial = await binsRun.GetPageAsync(
            new BinsRunFilterForm { RoomIds = [99101, 99102] },
            manager,
            CancellationToken.None);
        Assert.Equal(60, partial.AvailableInventory.Single(x => x.RoomId == 99101).CurrentBins);
        Assert.Equal(40, partial.AvailableInventory.Single(x => x.RoomId == 99102).CurrentBins);
        Assert.All(partial.AvailableInventory, x => Assert.DoesNotContain("No grade data", x.GradeSummary));
        Assert.Equal(100, partial.AvailableInventory.Sum(x => x.CurrentBins));

        var destination = (await dashboard.GetRoomDetailAsync(99102, CancellationToken.None))
            .TransferLotOptions.Single(x => x.Label.Contains("9040", StringComparison.Ordinal));
        Assert.Null(await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "qc-b-to-c",
            FromRoomId = 99102,
            ToRoomId = 99103,
            SourceLotKey = destination.LotKey,
            BinCount = 40,
            TransferAt = DateTimeOffset.Parse("2026-08-03T12:00:00Z"),
            Reason = "QC transfer regression B to C"
        }, CancellationToken.None));

        var roomC = await dashboard.GetRoomDetailAsync(99103, CancellationToken.None);
        var movedLot = Assert.Single(roomC.CurrentLots);
        Assert.Equal(40, movedLot.CurrentBins);
        Assert.Equal(12m, movedLot.AveragePressureLbs);
        Assert.Equal(3m, movedLot.AverageStarch);
        Assert.Contains("Rot", movedLot.DefectSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Unavailable", movedLot.GradeSummary);
        Assert.NotEqual("Unavailable", movedLot.SizeSummary);
        Assert.Contains(movedLot.Samples, x => x.SampleId == 990501 && x.DisplayReceiptId == "TR-QC-9040");
        Assert.Contains(movedLot.ReceiptEvidence, x => x.ReceiptId == 990401 && x.DisplayReceiptId == "TR-QC-9040");

        var roomCPage = await binsRun.GetPageAsync(
            new BinsRunFilterForm { RoomId = 99103 },
            manager,
            CancellationToken.None);
        var runOption = Assert.Single(roomCPage.AvailableInventory);
        Assert.Equal(40, runOption.CurrentBins);
        Assert.DoesNotContain("No grade data", runOption.GradeSummary);
        Assert.NotEmpty(roomCPage.RoomSummary!.SizeDistribution);

        var home = await dashboard.GetHomeDashboardAsync(new RoomSummaryFilterForm { Facility = "All" }, CancellationToken.None);
        var roomCCard = home.RoomSummaries.Single(x => x.RoomId == 99103);
        Assert.Equal(40, roomCCard.CurrentBinsCount);
        Assert.Equal(40, roomCCard.ReceivingPressureRepresentedBins);

        var transfers = await db.RoomTransfers.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, transfers.Count);
        Assert.All(transfers, x => Assert.Equal(990077, x.GrowerLotId));
        Assert.All(await db.RoomInventoryAdjustments.Where(x => x.RoomTransferId != null).ToListAsync(), x =>
        {
            Assert.Equal(990077, x.GrowerLotId);
            Assert.Equal(990011, x.FruitProfileId);
            Assert.Equal(2026, x.CropYear);
        });

        var admin = Dashboard(db, Principal("admin@fruitandland.com"));
        Assert.Null(await admin.ReverseRoomTransferAsync(new ReverseRoomTransferForm
        {
            Id = transfers[1].Id,
            OperationKey = "qc-c-to-b-reversal",
            Reason = "QC transfer reversal regression"
        }, CancellationToken.None));
        var returned = await BinsRun(db).GetPageAsync(
            new BinsRunFilterForm { RoomId = 99102 },
            manager,
            CancellationToken.None);
        Assert.DoesNotContain("No grade data", Assert.Single(returned.AvailableInventory).GradeSummary);

        Assert.Equal(originalCounts, await EvidenceCountsAsync(db));
        var receipt = await db.Receipts.Include(x => x.Samples).SingleAsync(x => x.Id == 990401);
        Assert.Equal(99101, receipt.RoomId);
        Assert.Contains(receipt.Samples, x => x.Id == 990501);
    }

    [Fact]
    public async Task AuthoritativeGrowerName_FollowsChainedTransferWithoutChangingGrowerNumbersOrQcOwnership()
    {
        await using var db = CreateDbContext();
        await SeedTransferFruitAsync(db, "1080", "WP ORCHARD", "TR-QC-1080");
        AddMappedGrower(db, "1080", "WP ORCHARD ORG CHIL", "WINDY POINT", "WP ORCHARD");
        await db.SaveChangesAsync();

        var manager = Principal("manager@fruitandland.com");
        var dashboard = Dashboard(db, manager);
        var beforeEvidence = await EvidenceCountsAsync(db);

        var sourceRoom = await dashboard.GetRoomDetailAsync(99101, CancellationToken.None);
        var sourceLot = Assert.Single(sourceRoom.CurrentLots);
        Assert.Equal("WP ORCHARD ORG CHIL", sourceLot.GrowerName);
        Assert.Equal("1080", sourceLot.GrowerNumber);
        var sourceOption = sourceRoom.TransferLotOptions.Single(x => x.Label.Contains("1080", StringComparison.Ordinal));

        Assert.Null(await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "authoritative-name-a-to-b",
            FromRoomId = 99101,
            ToRoomId = 99102,
            SourceLotKey = sourceOption.LotKey,
            BinCount = 40,
            TransferAt = DateTimeOffset.Parse("2026-08-02T12:00:00Z"),
            Reason = "Authoritative-name and QC identity regression A to B"
        }, CancellationToken.None));

        var roomB = await dashboard.GetRoomDetailAsync(99102, CancellationToken.None);
        var roomBOption = roomB.TransferLotOptions.Single(x => x.Label.Contains("1080", StringComparison.Ordinal));
        Assert.Null(await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "authoritative-name-b-to-c",
            FromRoomId = 99102,
            ToRoomId = 99103,
            SourceLotKey = roomBOption.LotKey,
            BinCount = 40,
            TransferAt = DateTimeOffset.Parse("2026-08-03T12:00:00Z"),
            Reason = "Authoritative-name and QC identity regression B to C"
        }, CancellationToken.None));

        var roomC = await dashboard.GetRoomDetailAsync(99103, CancellationToken.None);
        var movedLot = Assert.Single(roomC.CurrentLots);
        Assert.Equal("WP ORCHARD ORG CHIL", movedLot.GrowerName);
        Assert.Equal("1080", movedLot.GrowerNumber);
        Assert.Equal(40, movedLot.CurrentBins);
        Assert.Equal(12m, movedLot.AveragePressureLbs);
        Assert.Equal(3m, movedLot.AverageStarch);
        Assert.Contains(movedLot.Samples, x => x.SampleId == 990501 && x.DisplayReceiptId == "TR-QC-1080");
        Assert.Contains(movedLot.ReceiptEvidence, x => x.ReceiptId == 990401 && x.DisplayReceiptId == "TR-QC-1080");

        var binsRun = await BinsRun(db).GetPageAsync(
            new BinsRunFilterForm { RoomId = 99103 },
            manager,
            CancellationToken.None);
        var runOption = Assert.Single(binsRun.AvailableInventory);
        Assert.Equal("WP ORCHARD ORG CHIL", runOption.Grower);
        Assert.Equal("1080", runOption.Lot);
        Assert.Equal(40, runOption.CurrentBins);
        Assert.DoesNotContain("No grade data", runOption.GradeSummary);

        Assert.Equal(beforeEvidence, await EvidenceCountsAsync(db));
        Assert.Equal(100, (await BinsRun(db).GetPageAsync(
            new BinsRunFilterForm { RoomIds = [99101, 99102, 99103] },
            manager,
            CancellationToken.None)).AvailableInventory.Sum(x => x.CurrentBins));

        var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 990401);
        Assert.Equal("WP ORCHARD", receipt.GrowerName);
        Assert.Equal("1080", receipt.GrowerNumber);
        Assert.Equal("1080", receipt.LotCode);
        Assert.Equal(99101, receipt.RoomId);
        var growerLot = await db.GrowerLots.AsNoTracking().SingleAsync(x => x.Id == 990077);
        Assert.Equal("WP ORCHARD", growerLot.Grower);
        Assert.Equal("1080", growerLot.LotNumber);
        var originalAdjustment = await db.RoomInventoryAdjustments.AsNoTracking().SingleAsync(x => x.Id == 990801);
        Assert.Equal("WP ORCHARD", originalAdjustment.GrowerName);
        Assert.Equal("1080", originalAdjustment.LotNumber);
        Assert.All(await db.RoomTransfers.AsNoTracking().ToListAsync(), x => Assert.Equal("1080", x.LotNumber));
        Assert.All(await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.RoomTransferId != null).ToListAsync(), x => Assert.Equal("1080", x.LotNumber));
        Assert.Equal(99101, await db.QcSamples.AsNoTracking().Where(x => x.Id == 990501).Select(x => x.Receipt!.RoomId).SingleAsync());
    }

    [Fact]
    public async Task PostgreSql_CanonicalQcQueriesFollowTransfer_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_CANONICAL_QC_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var options = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, connectionString);
        await using var db = new CropQcDbContext(options.Options);
        Assert.True(
            await db.Database.EnsureCreatedAsync(),
            "The configured canonical-QC PostgreSQL database must start empty.");
        await SeedTransferFruitAsync(db);

        var manager = Principal("manager@fruitandland.com");
        var dashboard = Dashboard(db, manager);
        var source = (await dashboard.GetRoomDetailAsync(99101, CancellationToken.None))
            .TransferLotOptions.Single(x => x.Label.Contains("9040", StringComparison.Ordinal));
        Assert.Null(await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "postgres-qc-a-to-b",
            FromRoomId = 99101,
            ToRoomId = 99102,
            SourceLotKey = source.LotKey,
            BinCount = 40,
            TransferAt = DateTimeOffset.Parse("2026-08-02T12:00:00Z"),
            Reason = "PostgreSQL canonical QC query regression"
        }, CancellationToken.None));

        var destinationDetail = await dashboard.GetRoomDetailAsync(99102, CancellationToken.None);
        var movedLot = Assert.Single(destinationDetail.CurrentLots);
        Assert.Contains(movedLot.Samples, x => x.SampleId == 990501);
        Assert.Contains(movedLot.ReceiptEvidence, x => x.ReceiptId == 990401);

        var binsRun = await BinsRun(db).GetPageAsync(
            new BinsRunFilterForm { RoomId = 99102 },
            manager,
            CancellationToken.None);
        Assert.DoesNotContain("No grade data", Assert.Single(binsRun.AvailableInventory).GradeSummary);

        var dashboardPage = await dashboard.GetHomeDashboardAsync(
            new RoomSummaryFilterForm { Facility = "All" },
            CancellationToken.None);
        Assert.Equal(40, dashboardPage.RoomSummaries.Single(x => x.RoomId == 99102).ReceivingPressureRepresentedBins);

        var transferIn = await db.RoomInventoryAdjustments
            .SingleAsync(x => x.RoomTransferId != null && x.RoomId == 99102 && x.ChangeAmount > 0);
        var run = new ActualRun
        {
            Id = 901,
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            RunAt = DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
            CreatedByUserId = 1,
            CreatedAt = DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
            RunFacilityWarehouseId = 9910,
            RunFacilityCodeSnapshot = "EBS"
        };
        var revision = new ActualRunRevision
        {
            Id = 902,
            ActualRun = run,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = "postgres-qc-expectation",
            IsCurrent = true,
            CreatedByUserId = 1,
            CreatedAt = run.RunAt
        };
        var entry = new BinsRunEntry
        {
            Id = 903,
            ActualRun = run,
            ActualRunRevision = revision,
            InventoryAdjustmentId = transferIn.Id,
            WarehouseId = 9910,
            RoomId = 99102,
            CropYear = 2026,
            GrowerLotId = 990077,
            FruitProfileId = 990011,
            GrowerName = "Grower 9040",
            GrowerNumberSnapshot = "9040",
            LotNumber = "9040",
            VarietyCode = "QC-GALA",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            PreviousAvailableBins = 40,
            BinsRun = 5,
            NewAvailableBins = 35,
            RunAt = run.RunAt,
            CreatedByUserId = 1,
            CreatedAt = run.RunAt,
            TransactionType = ActualRunTransactionTypes.Depletion,
            ReportingFacilityWarehouseId = 9910,
            ReportingFacilityCodeSnapshot = "EBS",
            ReportingCropYearSnapshot = 2026,
            ReportingFruitProfileIdSnapshot = 990011,
            ReportingVarietyCodeSnapshot = "QC-GALA"
        };
        db.AddRange(run, revision, entry);
        await db.SaveChangesAsync();

        var expectation = await new RunExpectationService(db).CreateFrozenAsync(
            run,
            revision,
            [entry],
            1,
            run.RunAt,
            CancellationToken.None);
        var expectationSource = Assert.Single(expectation.Sources);
        Assert.Equal(99102, expectationSource.RoomId);
        Assert.Equal(990501, expectationSource.QcSampleId);
        Assert.Equal("Destination Room B", expectationSource.RoomSnapshot);
    }

    [Fact]
    public async Task RestoredProduction_TransferOneSourceQcResolvesAtDestination_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RESTORED_CANONICAL_QC_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, connectionString);
        await using var db = new CropQcDbContext(options.Options);
        var before = await EvidenceAndInventoryCountsAsync(db);
        var transfer = await db.RoomTransfers.AsNoTracking()
            .Include(x => x.SourceRoom)
            .Include(x => x.DestinationRoom)
            .SingleAsync(x => x.Id == 1);
        var sourceSample = await db.QcSamples.AsNoTracking()
            .Include(x => x.Receipt)
            .Include(x => x.SampleType)
            .SingleAsync(x => x.Id == 263);

        Assert.Equal("TR108869", sourceSample.Receipt!.CompuTechReceiptId);
        Assert.Equal(transfer.SourceRoomId, sourceSample.Receipt.RoomId);
        Assert.NotEqual(transfer.DestinationRoomId, sourceSample.Receipt.RoomId);
        Assert.False(await db.QcSamples.AsNoTracking().AnyAsync(x =>
            x.Id == sourceSample.Id && x.Receipt!.RoomId == transfer.DestinationRoomId));

        var owner = Principal(ApplicationAreas.OwnerEmail);
        var destination = await Dashboard(db, owner)
            .GetRoomDetailAsync(transfer.DestinationRoomId, CancellationToken.None);
        Assert.Contains(destination.CurrentLots.SelectMany(x => x.Samples), x => x.SampleId == sourceSample.Id);
        Assert.Contains(destination.CurrentLots.SelectMany(x => x.ReceiptEvidence), x => x.ReceiptId == sourceSample.ReceiptId);

        var runsAndTransfers = await BinsRun(db).GetPageAsync(
            new BinsRunFilterForm { RoomId = transfer.DestinationRoomId },
            owner,
            CancellationToken.None);
        Assert.Contains(runsAndTransfers.AvailableInventory, x =>
            x.Lot == "9392" && !x.GradeSummary.Contains("No grade data", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, await EvidenceAndInventoryCountsAsync(db));
    }

    [Fact]
    public void CanonicalIdentity_IsExactAndLegacyFallbackFailsClosedWhenAmbiguous()
    {
        var canonical = Identity(2026, 77, "9040", "9040", 11, "GALA", "Conventional", false);
        Assert.True(canonical.Matches(Identity(2026, 77, "9040", "9040", 11, "GALA", "Conventional", false)));
        Assert.False(canonical.Matches(Identity(2025, 77, "9040", "9040", 11, "GALA", "Conventional", false)));
        Assert.False(canonical.Matches(Identity(2026, 78, "9040", "9040", 11, "GALA", "Conventional", false)));
        Assert.False(canonical.Matches(Identity(2026, 77, "9040", "9040", 12, "GALA", "Organic", true)));
        Assert.False(canonical.Matches(Identity(2026, 77, "9040", "9040", 13, "BART", "Conventional", false)));

        var legacy = Identity(2026, null, "9040", "9040", 11, "GALA", "Conventional", false);
        var candidates = new[]
        {
            Identity(2026, 77, "9040", "9040", 11, "GALA", "Conventional", false),
            Identity(2026, 78, "9040", "9040", 11, "GALA", "Conventional", false)
        };
        Assert.Empty(CanonicalQcFruitIdentity.ResolveUnambiguous(legacy, candidates, x => x));
        Assert.Single(CanonicalQcFruitIdentity.ResolveUnambiguous(legacy, candidates.Take(1), x => x));

        var profileFallback = CanonicalQcFruitIdentity.Create(2026, 77, "9040", "9040", null, "GALA", "Conventional", false)!;
        Assert.Empty(CanonicalQcFruitIdentity.ResolveUnambiguous(
            profileFallback,
            new[]
            {
                Identity(2026, 77, "9040", "9040", 11, "GALA", "Conventional", false),
                Identity(2026, 77, "9040", "9040", 14, "GALA", "Conventional", false)
            },
            x => x));
    }

    private static CanonicalQcFruitIdentity Identity(
        int year,
        int? growerLotId,
        string grower,
        string lot,
        int? profileId,
        string variety,
        string production,
        bool? organic) =>
        CanonicalQcFruitIdentity.Create(year, growerLotId, grower, lot, profileId, variety, production, organic)!;

    private static CropQcDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase($"canonical-qc-transfer-{Guid.NewGuid():N}")
            .Options);

    private static async Task SeedTransferFruitAsync(
        CropQcDbContext db,
        string growerNumber = "9040",
        string growerName = "Grower 9040",
        string displayReceiptId = "TR-QC-9040")
    {
        var warehouse = new Warehouse { Id = 9910, Code = "QC-EBS", Name = "QC Earl Brown", IsActive = true };
        var rooms = new[]
        {
            new Room { Id = 99101, Warehouse = warehouse, Code = "QC-A", Name = "Source Room A", CropQcRoomName = "Source Room A", CapacityBins = 1000 },
            new Room { Id = 99102, Warehouse = warehouse, Code = "QC-B", Name = "Destination Room B", CropQcRoomName = "Destination Room B", CapacityBins = 1000 },
            new Room { Id = 99103, Warehouse = warehouse, Code = "QC-C", Name = "Destination Room C", CropQcRoomName = "Destination Room C", CapacityBins = 1000 }
        };
        var growerLot = new GrowerLot
        {
            Id = 990077,
            Grower = growerName,
            LotNumber = growerNumber,
            IsActive = true,
            CreatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
        };
        var profile = new FruitProfile
        {
            Id = 990011,
            Name = "Gala",
            VarietyCode = "QC-GALA",
            FruitType = "Apple",
            ProductionType = "Conventional",
            IsOrganic = false,
            IsActive = true
        };
        var receipt = new Receipt
        {
            Id = 990401,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            CompuTechReceiptId = displayReceiptId,
            ReceiptType = "Truck receipt",
            Warehouse = warehouse,
            Room = rooms[0],
            FruitProfile = profile,
            GrowerLot = growerLot,
            GrowerName = growerName,
            GrowerNumber = growerNumber,
            LotCode = growerNumber,
            BinCount = 100,
            CreatedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z")
        };
        var sampleType = await db.SampleTypes.SingleOrDefaultAsync(x => x.Name == "Receiving Sample");
        if (sampleType is null)
        {
            sampleType = new SampleType { Id = 990021, Name = "Receiving Sample", IsActive = true };
            db.SampleTypes.Add(sampleType);
        }
        var grade = new Grade { Id = 990031, Code = "QC-US1", Name = "QC US 1", IsActive = true };
        var defect = new DefectType { Id = 990041, Name = "QC Transfer Rot", IsActive = true };
        var scale = new StarchScale { Id = 990051, Name = "QC Transfer Apple", FruitType = "Apple", IsActive = true };
        var starch = new StarchScaleValue { Id = 990052, StarchScale = scale, Value = 3m, SortOrder = 1, IsActive = true };
        var sample = new QcSample
        {
            Id = 990501,
            Receipt = receipt,
            SampleType = sampleType,
            Status = "Complete",
            StarchStatus = "Complete",
            PhotoStatus = "Complete",
            EmailStatus = "Not sent",
            ActualSampleSize = 1,
            SampleTakenAt = DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-08-01T09:00:00Z")
        };
        var reading = new QcFruitReading
        {
            Id = 990601,
            QcSample = sample,
            RowNumber = 1,
            Pressure1Lbs = 11m,
            Pressure2Lbs = 13m,
            WeightGrams = 180m,
            Grade = grade,
            StarchScaleValue = starch,
            SizeCategory = 80,
            SizeStatus = "Sized",
            IsCompleted = true,
            CreatedAt = DateTimeOffset.Parse("2026-08-01T09:00:00Z")
        };
        reading.Defects.Add(new QcFruitDefect { Id = 990701, QcFruitReading = reading, DefectType = defect });
        var receiptAdd = new RoomInventoryAdjustment
        {
            Id = 990801,
            Receipt = receipt,
            CropYear = 2026,
            Warehouse = warehouse,
            Room = rooms[0],
            GrowerLot = growerLot,
            FruitProfile = profile,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.GrowerNumber,
            VarietyCode = profile.VarietyCode,
            OldBinCount = 0,
            ChangeAmount = 100,
            NewBinCount = 100,
            AdjustmentType = "ReceiptAdd",
            Source = "Receiving inventory added",
            AdjustmentAt = receipt.ReceivedAt,
            CreatedAt = receipt.ReceivedAt
        };
        db.AddRange(
            warehouse,
            rooms[0], rooms[1], rooms[2],
            growerLot,
            profile,
            grade,
            defect,
            scale,
            starch,
            receipt,
            sample,
            reading,
            receiptAdd,
            User(1, "manager@fruitandland.com", PageAccessLevel.Edit),
            User(2, "admin@fruitandland.com", PageAccessLevel.Admin));
        await db.SaveChangesAsync();
    }

    private static void AddMappedGrower(
        CropQcDbContext db,
        string number,
        string name,
        params string[] aliases)
    {
        var now = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var grower = new CanonicalGrower
        {
            DisplayName = name,
            NormalizedKey = $"REVIEWED_GROWER_NUMBER_{number}",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        grower.GrowerNumbers.Add(new CanonicalGrowerNumber
        {
            GrowerNumber = number,
            NormalizedGrowerNumber = number,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        grower.Aliases.Add(new CanonicalGrowerAlias
        {
            AliasName = name,
            NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey(name),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        foreach (var alias in aliases)
        {
            grower.Aliases.Add(new CanonicalGrowerAlias
            {
                AliasName = alias,
                NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey(alias),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        db.CanonicalGrowers.Add(grower);
    }

    private static async Task<(int Samples, int Readings, int Photos)> EvidenceCountsAsync(CropQcDbContext db) =>
        (await db.QcSamples.CountAsync(), await db.QcFruitReadings.CountAsync(), await db.QcPhotos.CountAsync());

    private static async Task<(int Samples, int Readings, int Photos, int Receipts, int Adjustments, int AdjustmentQuantity)> EvidenceAndInventoryCountsAsync(CropQcDbContext db) =>
        (await db.QcSamples.CountAsync(),
            await db.QcFruitReadings.CountAsync(),
            await db.QcPhotos.CountAsync(),
            await db.Receipts.CountAsync(),
            await db.RoomInventoryAdjustments.CountAsync(),
            await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));

    private static BinsRunService BinsRun(CropQcDbContext db)
    {
        var config = new ConfigurationBuilder().Build();
        return new BinsRunService(db, new UserAccessService(db, config), NullLogger<BinsRunService>.Instance);
    }

    private static DashboardDataService Dashboard(CropQcDbContext db, ClaimsPrincipal principal)
    {
        var config = new ConfigurationBuilder().Build();
        return new DashboardDataService(
            db,
            null!,
            new FileStorageOptions(),
            new EmailOptions(),
            null!,
            new GoogleAuthenticationOptions(),
            null!,
            null!,
            new QcPhotoRequirementPolicy(),
            null!,
            new CropYearService(db, config),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            config,
            NullLogger<DashboardDataService>.Instance,
            new UserAccessService(db, config));
    }

    private static User User(int id, string email, PageAccessLevel level)
    {
        var role = new Role { Name = $"QC transfer role {id}", NormalizedName = $"QC TRANSFER ROLE {id}", IsActive = true };
        foreach (var area in ApplicationAreas.All)
        {
            role.PageAccesses.Add(new RolePageAccess { AreaKey = area.Key, AccessLevel = level.ToString(), UpdatedAt = DateTimeOffset.UtcNow });
        }
        var user = new User
        {
            Id = id,
            Email = email,
            DisplayName = email,
            Domain = "fruitandland.com",
            IsActive = true,
            EmploymentFacility = EmploymentFacilities.Ebs,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.UserRoles.Add(new UserRole { User = user, Role = role });
        return user;
    }

    private static ClaimsPrincipal Principal(string email) => new(
        new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "Test"));
}
