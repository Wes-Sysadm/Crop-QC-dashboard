using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class ReceiptPositiveTrueUpPostgreSqlTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T18:00:00Z");

    [Fact]
    public async Task Tr508605_production_shape_positive_true_up_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_RECEIPT_TRUEUP_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options;
        await using var db = new CropQcDbContext(options);
        await db.Database.EnsureCreatedAsync();
        Assert.False(await db.Receipts.AnyAsync());

        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == 4);
        var room = new Room { Id = 3, Warehouse = warehouse, WarehouseId = 4, Code = "WP-6", Name = "WP-6" };
        var danj = await db.FruitProfiles.SingleAsync(x => x.Id == 18);
        var orda = await db.FruitProfiles.SingleAsync(x => x.Id == 21);
        var growerLot = new GrowerLot { Id = 394, Grower = "Grower 1080", LotNumber = "1080", IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        var admin = new User { Id = 1, Email = ApplicationAreas.OwnerEmail, DisplayName = "Wes", Domain = "fruitandland.com", CreatedAt = Now };
        var voided = Receipt(838, 28, danj, warehouse, room, growerLot, deleted: true);
        voided.DeleteReason = "recieved as wrong variety";
        voided.DeletedAt = Now.AddDays(-1);
        voided.DeletedByUserId = admin.Id;
        var active = Receipt(842, 27, orda, warehouse, room, growerLot, deleted: false);
        var other = Receipt(843, 391, orda, warehouse, room, growerLot, deleted: false);
        other.CompuTechReceiptId = "TR508605-OTHER-ORDA";
        db.AddRange(room, growerLot, admin, voided, active, other);

        db.RoomInventoryAdjustments.AddRange(
            Adjustment(2400, voided, 28, 0, 28, "ReceiptCreate"),
            Adjustment(2401, active, 27, 0, 27, "ReceiptCreate"),
            Adjustment(2402, other, 391, 27, 418, "ReceiptCreate"));

        var voidOverride = new ReceiptInventoryOverride
        {
            Id = Guid.Parse("1558f258-f19b-4cf1-86dc-4cf19bf2c9a7"),
            Receipt = voided,
            ReceiptId = voided.Id,
            ActionType = ReceiptInventoryOverrideActionTypes.VoidReceipt,
            OldReceiptBinCount = 28,
            NewReceiptBinCount = 0,
            InventoryDelta = -28,
            CurrentInventoryBefore = 28,
            CurrentInventoryAfter = 0,
            AdministratorUser = admin,
            AdministratorUserId = admin.Id,
            Reason = voided.DeleteReason,
            OperationKey = "protected-receipt-838-void",
            CreatedAt = Now.AddDays(-1),
            VoidConfirmationDetails = "Protected production-shaped void",
            BeforeReceiptSnapshotJson = ReceiptSnapshot(voided, false, 28),
            AfterReceiptSnapshotJson = ReceiptSnapshot(voided, true, 28),
            AffectedInventorySnapshotJson = Affected(voided, 28),
            ExpectedAdjustmentCount = 1,
            IsComplete = true
        };
        var voidAdjustment = Adjustment(2403, voided, -28, 28, 0, ReceiptInventoryOverrideService.AdjustmentType);
        voidAdjustment.ReceiptInventoryOverride = voidOverride;
        voidAdjustment.ReceiptInventoryOverrideId = voidOverride.Id;
        voidAdjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        voidAdjustment.InventoryOperationKey = "receipt-override:protected-receipt-838-void:1";
        voidAdjustment.CreatedByUser = admin;
        voidAdjustment.CreatedByUserId = admin.Id;
        voidOverride.InventoryAdjustments.Add(voidAdjustment);
        db.AddRange(voidOverride, voidAdjustment);

        var consumed = Adjustment(2435, active, -287, 418, 131, "ActualRun");
        consumed.Receipt = null;
        consumed.ReceiptId = null;
        var actualRun = new ActualRun { Id = 501, Status = "Completed", CurrentRevisionNumber = 1, RunAt = Now.AddHours(-2), CreatedAt = Now.AddHours(-2) };
        var runEntry = new BinsRunEntry
        {
            Id = 601,
            Receipt = active,
            ReceiptId = active.Id,
            InventoryAdjustment = consumed,
            Warehouse = warehouse,
            WarehouseId = warehouse.Id,
            Room = room,
            RoomId = room.Id,
            CropYear = 2026,
            GrowerLotId = growerLot.Id,
            FruitProfile = orda,
            FruitProfileId = orda.Id,
            GrowerName = growerLot.Grower,
            LotNumber = growerLot.LotNumber,
            VarietyCode = orda.VarietyCode,
            PreviousAvailableBins = 418,
            BinsRun = 287,
            NewAvailableBins = 131,
            RunAt = Now.AddHours(-2),
            CreatedAt = Now.AddHours(-2),
            ActualRun = actualRun,
            ActualRunId = actualRun.Id
        };
        db.AddRange(consumed, actualRun, runEntry);
        db.TreatmentLineageSegments.Add(new TreatmentLineageSegment
        {
            Id = 352,
            Warehouse = warehouse,
            WarehouseId = warehouse.Id,
            Room = room,
            RoomId = room.Id,
            CropYear = 2026,
            GrowerLotId = growerLot.Id,
            FruitProfile = orda,
            FruitProfileId = orda.Id,
            IdentityKey = "2026|394|21|1080|1080|ORDA|ORGANIC|True|ORGANIC",
            GrowerNumberSnapshot = "1080",
            GrowerNameSnapshot = growerLot.Grower,
            LotNumberSnapshot = "1080",
            VarietyCodeSnapshot = "ORDA",
            ProductionTypeSnapshot = "Organic",
            IsOrganicSnapshot = true,
            InventoryStatusSnapshot = "Organic",
            TreatmentState = TreatmentLineageStates.Untreated,
            TreatmentSignature = "u",
            CurrentBins = 131,
            CreatedAt = Now.AddHours(-3),
            UpdatedAt = Now.AddHours(-2),
            ConcurrencyVersion = 4
        });
        db.AuditLogs.Add(new AuditLog
        {
            Id = 23057,
            Action = ReceiptInventoryOverrideActionTypes.VoidReceipt,
            EntityName = nameof(ReceiptInventoryOverride),
            EntityKey = voidOverride.Id.ToString("D"),
            User = admin,
            UserId = admin.Id,
            BeforeValuesJson = voidOverride.BeforeReceiptSnapshotJson,
            AfterValuesJson = voidOverride.AfterReceiptSnapshotJson,
            SourceApplication = "CropQc.Web",
            CreatedAt = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var protectedBefore = await Protected838Async(db);
        var historicalAdjustmentIds = await db.RoomInventoryAdjustments.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
        var historicalRun = await db.BinsRunEntries.AsNoTracking().SingleAsync(x => x.Id == 601);
        var access = new UserAccessService(db, new ConfigurationBuilder().Build());
        var clock = new PacificBusinessTimeService(new FixedClock(Now));
        var ledger = new RoomInventoryLedgerQueryService(db);
        var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
        var treatments = new RoomTreatmentService(db, ledger, access,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal(admin.Email) } },
            clock, NullLogger<RoomTreatmentService>.Instance);
        var service = new ReceiptInventoryOverrideService(db, access, invariant, ledger,
            new InventoryIdentityService(db), treatments, clock, NullLogger<ReceiptInventoryOverrideService>.Instance);
        var preview = await service.GetPreviewAsync(active.Id, CancellationToken.None);
        Assert.Equal((131, 1), (preview!.CurrentCanonicalInventory, preview.TrueUpPositions.Count(x => x.IsEligible)));
        var form = Form(active, preview, "Received 28 bins; receipt was entered as 27.");

        var result = await service.ApplyEditAsync(form, Principal(admin.Email), CancellationToken.None);
        var rerun = await service.ApplyEditAsync(form, Principal(admin.Email), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(rerun.WasIdempotent);
        Assert.Equal(28, (await db.Receipts.FindAsync(842L))!.BinCount);
        Assert.Equal(132, await CurrentAsync(db, 394, 21));
        Assert.Equal(132, await db.TreatmentLineageSegments.Where(x => x.GrowerLotId == 394 && x.FruitProfileId == 21).SumAsync(x => x.CurrentBins));
        var addedSegment = await db.TreatmentLineageSegments.SingleAsync(x => x.ReceiptId == 842);
        Assert.Equal((1, "Untreated", "u"), (addedSegment.CurrentBins, addedSegment.TreatmentState, addedSegment.TreatmentSignature));
        var operation = await db.ReceiptInventoryOverrides.Include(x => x.InventoryAdjustments).SingleAsync(x => x.Id == result.OverrideId);
        Assert.Equal((27, 28, 1, 131, 132), (operation.OldReceiptBinCount, operation.NewReceiptBinCount,
            operation.InventoryDelta, operation.CurrentInventoryBefore, operation.CurrentInventoryAfter));
        Assert.Single(operation.InventoryAdjustments);
        Assert.Equal(3, operation.InventoryAdjustments.Single().RoomId);
        Assert.Contains("\"treatmentSignature\":\"u\"", operation.AffectedInventorySnapshotJson);
        Assert.Equal(protectedBefore, await Protected838Async(db));
        Assert.Equal(287, historicalRun.BinsRun);
        Assert.Equal(historicalAdjustmentIds, await db.RoomInventoryAdjustments.Where(x => x.ReceiptInventoryOverrideId != result.OverrideId)
            .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync());
        Assert.Equal(2, await db.ReceiptInventoryOverrides.CountAsync());
        Assert.Equal(1, await db.RoomInventoryAdjustments.CountAsync(x => x.ReceiptInventoryOverrideId == result.OverrideId));
        var readiness = await invariant.VerifyReadinessAsync(CancellationToken.None);
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Issues.Where(x => x.BlocksDeployment).Select(x => x.Code)));
    }

    private static Receipt Receipt(long id, int bins, FruitProfile fruit, Warehouse warehouse, Room room, GrowerLot lot, bool deleted) => new()
    {
        Id = id,
        CropYear = 2026,
        ReceivedAt = Now.AddDays(-2),
        CompuTechReceiptId = "TR508605",
        ReceiptType = "Truck receipt",
        Warehouse = warehouse,
        WarehouseId = warehouse.Id,
        Room = room,
        RoomId = room.Id,
        FruitProfile = fruit,
        FruitProfileId = fruit.Id,
        GrowerLot = lot,
        GrowerLotId = lot.Id,
        GrowerNumber = "1080",
        GrowerName = lot.Grower,
        LotCode = "1080",
        BinCount = bins,
        CreatedAt = Now.AddDays(-2),
        UpdatedAt = Now.AddDays(-1),
        ConcurrencyVersion = 1,
        IsDeleted = deleted
    };

    private static RoomInventoryAdjustment Adjustment(long id, Receipt receipt, int change, int oldBins, int newBins, string type) => new()
    {
        Id = id,
        Receipt = receipt,
        ReceiptId = receipt.Id,
        CropYear = receipt.CropYear,
        WarehouseId = receipt.WarehouseId,
        RoomId = receipt.RoomId,
        GrowerLotId = receipt.GrowerLotId,
        FruitProfileId = receipt.FruitProfileId,
        GrowerName = receipt.GrowerName,
        LotNumber = receipt.LotCode,
        VarietyCode = receipt.FruitProfile.VarietyCode,
        InventoryStatus = receipt.FruitProfile.ProductionType,
        OldBinCount = oldBins,
        ChangeAmount = change,
        NewBinCount = newBins,
        AdjustmentType = type,
        AdjustmentAt = Now.AddHours(-2),
        CreatedAt = Now.AddHours(-2)
    };

    private static AdminReceiptInventoryOverrideForm Form(Receipt receipt, ReceiptInventoryOverridePreviewViewModel preview, string reason) => new()
    {
        Id = receipt.Id,
        ExpectedConcurrencyVersion = receipt.ConcurrencyVersion,
        ExpectedPositiveTrueUpStateToken = preview.PositiveTrueUpStateToken,
        OperationKey = "50860500-0000-0000-0000-000000000001",
        Reason = reason,
        ConfirmInventoryChange = true,
        CropYear = receipt.CropYear,
        ConfirmCropYear = true,
        ReceivedAt = receipt.ReceivedAt,
        CompuTechReceiptId = receipt.CompuTechReceiptId,
        ReceiptType = receipt.ReceiptType,
        WarehouseId = receipt.WarehouseId,
        RoomId = receipt.RoomId,
        FruitProfileId = receipt.FruitProfileId,
        GrowerLotId = receipt.GrowerLotId,
        GrowerNumber = receipt.GrowerNumber!,
        GrowerName = receipt.GrowerName,
        LotCode = receipt.LotCode,
        BinCount = 28
    };

    private static string ReceiptSnapshot(Receipt receipt, bool deleted, int bins) => JsonSerializer.Serialize(new
    {
        receipt.Id,
        receipt.CompuTechReceiptId,
        receipt.CropYear,
        receipt.ReceivedAt,
        receipt.ReceiptType,
        receipt.WarehouseId,
        receipt.RoomId,
        receipt.FruitProfileId,
        receipt.GrowerLotId,
        receipt.GrowerNumber,
        receipt.GrowerName,
        receipt.LotCode,
        BinCount = bins,
        IsDeleted = deleted,
        receipt.ConcurrencyVersion
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string Affected(Receipt receipt, int bins) => JsonSerializer.Serialize(new[] { new
    {
        receipt.WarehouseId, Warehouse = receipt.Warehouse.Code, receipt.RoomId, Room = receipt.Room.Code,
        CropYear = (int?)receipt.CropYear, receipt.GrowerLotId, FruitProfileId = (int?)receipt.FruitProfileId,
        Grower = receipt.GrowerName, Lot = receipt.LotCode, Variety = receipt.FruitProfile.VarietyCode,
        InventoryStatus = receipt.FruitProfile.ProductionType, CurrentBins = bins
    } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<int> CurrentAsync(CropQcDbContext db, int growerLotId, int fruitProfileId) =>
        await db.RoomInventoryAdjustments.Where(x => x.GrowerLotId == growerLotId && x.FruitProfileId == fruitProfileId).SumAsync(x => x.ChangeAmount);

    private static async Task<string> Protected838Async(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Receipt = await db.Receipts.AsNoTracking().Where(x => x.Id == 838).Select(x => new { x.BinCount, x.IsDeleted, x.DeleteReason, x.ConcurrencyVersion }).SingleAsync(),
        Override = await db.ReceiptInventoryOverrides.AsNoTracking().Where(x => x.ReceiptId == 838).Select(x => new { x.Id, x.InventoryDelta, x.Reason, x.IsComplete }).SingleAsync(),
        Adjustment = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.ReceiptInventoryOverrideId != null && x.ReceiptId == 838).Select(x => new { x.Id, x.ChangeAmount, x.NewBinCount }).SingleAsync(),
        Audit = await db.AuditLogs.AsNoTracking().Where(x => x.Id == 23057).Select(x => new { x.Id, x.Action, x.EntityKey, x.BeforeValuesJson, x.AfterValuesJson }).SingleAsync()
    });

    private static ClaimsPrincipal Principal(string email) => new(new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "Test"));
    private sealed class FixedClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow => utcNow; }
}
