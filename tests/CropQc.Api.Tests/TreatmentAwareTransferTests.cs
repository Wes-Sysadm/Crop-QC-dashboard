using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class TreatmentAwareTransferTests
{
    private static readonly DateTimeOffset RehearsalNow = DateTimeOffset.Parse("2026-08-21T18:00:00Z");

    [Fact]
    public void Physical_movement_forms_post_the_exact_treatment_segment_without_schema_changes()
    {
        var binsRun = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var room = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml");
        var treatmentService = ReadRepositoryFile("src", "CropQc.Web", "Services", "RoomTreatmentService.cs");

        Assert.Contains("name=\"TreatmentSegmentId\" id=\"transfer-treatment-segment-id\"", binsRun);
        Assert.Contains("name=\"Lines[@i].TreatmentSegmentId\"", binsRun);
        Assert.Contains("data-treatment-segment-id=\"@option.TreatmentSegmentId\"", binsRun);
        Assert.Contains("name=\"TreatmentSegmentId\" id=\"dropped-treatment-segment-id\"", room);
        Assert.Contains("MoveSelectedAsync", treatmentService);
        Assert.Contains("MoveSelectedToProcessorAsync", treatmentService);
        Assert.DoesNotContain("MigrationBuilder", treatmentService);
    }

    [Fact]
    public async Task Restored_production_MCP_segment_can_transfer_when_requested()
    {
        var connectionString = Environment.GetEnvironmentVariable("TREATMENT_AWARE_TRANSFER_RESTORE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);

        await using var db = new CropQcDbContext(
            new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options);
        var actor = await db.Users.AsNoTracking().SingleAsync(x => x.Email == ApplicationAreas.OwnerEmail && x.IsActive);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, actor.Email)], "RestoredTreatmentTransfer"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var configuration = new ConfigurationBuilder().Build();
        var access = new UserAccessService(db, configuration);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var businessTime = new PacificBusinessTimeService(new FixedClock(RehearsalNow));
        var treatments = new RoomTreatmentService(
            db, ledger, access, accessor, businessTime, NullLogger<RoomTreatmentService>.Instance);
        var dashboard = CreateDashboardService(db, principal, ledger, treatments, businessTime);
        var runKey = Guid.NewGuid().ToString("N");
        var untreatedMoveKey = $"restored-treatment-aware-transfer-untreated-{runKey}";
        var mcpMoveKey = $"restored-treatment-aware-transfer-mcp-{runKey}";

        const long receiptId = 205;
        var receipt = await db.Receipts.AsNoTracking()
            .Include(x => x.Room)
            .ThenInclude(x => x.Warehouse)
            .Include(x => x.FruitProfile)
            .SingleAsync(x => x.Id == receiptId && x.CompuTechReceiptId == "TR508248");
        var mcpChemicalId = await db.TreatmentChemicals.AsNoTracking()
            .Where(x => x.IsActive
                && x.ApplicationLevel == TreatmentApplicationLevels.Receiving
                && x.CommonName == "MCP"
                && x.Crop == "Apples")
            .Select(x => x.Id)
            .SingleAsync();

        var apply = await treatments.ApplyReceiptAsync(new ReceiptTreatmentApplyForm
        {
            ReceiptId = receipt.Id,
            TreatmentChemicalId = mcpChemicalId,
            AppliedAt = RehearsalNow,
            OperationKey = "restored-treatment-aware-transfer-mcp-apply-second-receipt",
            Notes = "Disposable run-91 transfer reproduction",
            ConfirmedReview = true
        }, default);
        Assert.Null(apply.Error);

        var roomSnapshots = await ledger.GetSnapshotsAsync(receipt.WarehouseId, [receipt.RoomId], default);
        var snapshot = Assert.Single(roomSnapshots, x => x.CurrentBins > 0
            && x.CropYear == receipt.CropYear
            && x.GrowerLotId == receipt.GrowerLotId
            && x.FruitProfileId == receipt.FruitProfileId
            && string.Equals(x.GrowerNumber ?? x.Lot, receipt.GrowerNumber ?? receipt.LotCode, StringComparison.OrdinalIgnoreCase));
        var segments = await treatments.GetSelectionsAsync(snapshot, default);
        var mcp = Assert.Single(segments, x => x.ReceiptId == receipt.Id && x.TreatmentState == TreatmentLineageStates.Confirmed);
        Assert.Equal(64, mcp.CurrentBins);
        Assert.Equal(snapshot.CurrentBins, segments.Sum(x => x.CurrentBins));
        Assert.True(segments.Where(x => x.TreatmentState == TreatmentLineageStates.Untreated).Sum(x => x.CurrentBins) > 0);

        var binsRun = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance,
            ledger, configuration: configuration, roomTreatmentService: treatments);
        var actualRunPage = await binsRun.GetPageAsync(new BinsRunFilterForm
        {
            Section = "Actual",
            WarehouseId = receipt.WarehouseId,
            RoomIds = [receipt.RoomId]
        }, principal, default);
        var actualRunSources = actualRunPage.AvailableInventory.Where(x => x.RoomId == receipt.RoomId
            && x.GrowerLotId == receipt.GrowerLotId && x.FruitProfileId == receipt.FruitProfileId).ToList();
        Assert.Equal(658, actualRunSources.Sum(x => x.CurrentBins));
        Assert.Contains(actualRunSources, x => x.TreatmentSegmentId == mcp.SegmentId && x.CurrentBins == 64);
        Assert.Contains(actualRunSources, x => x.TreatmentSignature == "u" && x.CurrentBins == 594);

        var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
        var processor = new ProcessorShipmentService(db, ledger, treatments, treatments, invariant, access, accessor, businessTime);
        var processorPage = await processor.GetPageAsync(null, false, null, null, null, receipt.WarehouseId, default);
        var processorSources = processorPage.Inventory.Where(x => x.RoomId == receipt.RoomId
            && x.GrowerLotId == receipt.GrowerLotId && x.FruitProfileId == receipt.FruitProfileId).ToList();
        Assert.Equal(658, processorSources.Sum(x => x.AvailableBins));
        Assert.Contains(processorSources, x => x.TreatmentSegmentId == mcp.SegmentId && x.AvailableBins == 64);
        Assert.Contains(processorSources, x => x.TreatmentSignature == "u" && x.AvailableBins == 594);

        var variety = new InventoryByVarietyService(db, ledger, treatments, new NoWriteVarietyColors(), new FacilityContextService(db));
        var varietyBefore = Assert.Single((await variety.GetSummaryAsync("All", default)).Varieties,
            x => string.Equals(x.VarietyKey, "GALA", StringComparison.OrdinalIgnoreCase)).CurrentBins;

        var page = await dashboard.GetRoomDetailAsync(receipt.RoomId, default);
        Assert.True(page.TransferInventoryReconciles, page.TransferInventoryError ?? page.DataWarning);
        Assert.Equal(roomSnapshots.Where(x => x.CurrentBins > 0).Sum(x => x.CurrentBins), page.TransferLotOptions.Sum(x => x.CurrentBins));
        var mcpSource = Assert.Single(page.TransferLotOptions, x => x.TreatmentReceiptId == receipt.Id
            && x.TreatmentSignature == mcp.TreatmentSignature);
        var untreatedSource = Assert.Single(page.TransferLotOptions, x => x.LotKey == mcpSource.LotKey
            && x.TreatmentSignature == "u");
        Assert.Equal(receipt.Id, mcpSource.TreatmentReceiptId);
        Assert.Contains(receipt.CompuTechReceiptId, mcpSource.Label);
        Assert.Equal(658, mcpSource.CurrentBins + untreatedSource.CurrentBins);

        var destination = new Room
        {
            WarehouseId = receipt.WarehouseId,
            Code = $"CODEX-TREAT-{runKey[..8]}",
            Name = $"Disposable Treatment Destination {runKey[..8]}",
            CropQcRoomName = $"CODEX Treatment Destination {runKey[..8]}",
            DisplayName = $"CODEX Treatment Destination {runKey[..8]}",
            SortOrder = 9998,
            CapacityBins = 1000,
            IsActive = true
        };
        db.Rooms.Add(destination);
        await db.SaveChangesAsync();
        var beforeTransfers = await db.RoomTransfers.CountAsync();
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();

        var untreatedError = await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = untreatedMoveKey,
            FromRoomId = receipt.RoomId,
            DestinationWarehouseId = destination.WarehouseId,
            DestinationRoomId = destination.Id,
            SourceLotKey = untreatedSource.LotKey,
            TreatmentSignature = untreatedSource.TreatmentSignature,
            TreatmentSegmentId = untreatedSource.TreatmentSegmentId,
            BinCount = 20,
            TransferAt = RehearsalNow,
            Reason = "Disposable untreated coexistence proof"
        }, default);
        Assert.Null(untreatedError);

        page = await dashboard.GetRoomDetailAsync(receipt.RoomId, default);
        mcpSource = Assert.Single(page.TransferLotOptions, x => x.TreatmentReceiptId == receipt.Id
            && x.TreatmentSignature == mcp.TreatmentSignature);
        var mcpError = await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = mcpMoveKey,
            FromRoomId = receipt.RoomId,
            DestinationWarehouseId = destination.WarehouseId,
            DestinationRoomId = destination.Id,
            SourceLotKey = mcpSource.LotKey,
            TreatmentSignature = mcpSource.TreatmentSignature,
            TreatmentSegmentId = mcpSource.TreatmentSegmentId,
            BinCount = 25,
            TransferAt = RehearsalNow.AddMinutes(1),
            Reason = "Disposable MCP coexistence proof"
        }, default);

        Assert.Null(mcpError);
        Assert.Equal(beforeTransfers + 2, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments + 4, await db.RoomInventoryAdjustments.CountAsync());

        var duplicateMcpError = await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = mcpMoveKey,
            FromRoomId = receipt.RoomId,
            DestinationWarehouseId = destination.WarehouseId,
            DestinationRoomId = destination.Id,
            SourceLotKey = mcpSource.LotKey,
            TreatmentSignature = mcpSource.TreatmentSignature,
            TreatmentSegmentId = mcpSource.TreatmentSegmentId,
            BinCount = 25,
            TransferAt = RehearsalNow.AddMinutes(1),
            Reason = "Disposable MCP coexistence proof"
        }, default);
        Assert.Null(duplicateMcpError);
        Assert.Equal(beforeTransfers + 2, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments + 4, await db.RoomInventoryAdjustments.CountAsync());

        var mismatchedSegmentError = await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = mcpMoveKey,
            FromRoomId = receipt.RoomId,
            DestinationWarehouseId = destination.WarehouseId,
            DestinationRoomId = destination.Id,
            SourceLotKey = mcpSource.LotKey,
            TreatmentSignature = untreatedSource.TreatmentSignature,
            TreatmentSegmentId = untreatedSource.TreatmentSegmentId,
            BinCount = 25,
            TransferAt = RehearsalNow.AddMinutes(1),
            Reason = "Disposable MCP coexistence proof"
        }, default);
        Assert.Equal("The operation key already belongs to a transfer of a different treatment segment.", mismatchedSegmentError);
        Assert.Equal(beforeTransfers + 2, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments + 4, await db.RoomInventoryAdjustments.CountAsync());

        var sourceAfterSnapshot = Assert.Single(await ledger.GetSnapshotsAsync(receipt.WarehouseId, [receipt.RoomId], default),
            x => SameIdentity(x, snapshot));
        var destinationSnapshot = Assert.Single(await ledger.GetSnapshotsAsync(receipt.WarehouseId, [destination.Id], default),
            x => SameIdentity(x, snapshot));
        Assert.Equal(613, sourceAfterSnapshot.CurrentBins);
        Assert.Equal(45, destinationSnapshot.CurrentBins);
        var sourceAfter = await treatments.GetSelectionsAsync(sourceAfterSnapshot, default);
        var destinationAfter = await treatments.GetSelectionsAsync(destinationSnapshot, default);
        Assert.Equal(39, sourceAfter.Single(x => x.TreatmentSignature == mcp.TreatmentSignature).CurrentBins);
        Assert.Equal(574, sourceAfter.Single(x => x.TreatmentState == TreatmentLineageStates.Untreated).CurrentBins);
        Assert.Equal(25, destinationAfter.Single(x => x.TreatmentSignature == mcp.TreatmentSignature).CurrentBins);
        Assert.Equal(receipt.Id, destinationAfter.Single(x => x.TreatmentSignature == mcp.TreatmentSignature).ReceiptId);
        Assert.Equal(20, destinationAfter.Single(x => x.TreatmentState == TreatmentLineageStates.Untreated).CurrentBins);
        Assert.Equal(45, destinationAfter.Sum(x => x.CurrentBins));
        var globalIdentityBins = (await ledger.GetSnapshotsAsync(null, null, default))
            .Where(x => SameIdentity(x, snapshot))
            .Sum(x => x.CurrentBins);
        Assert.Equal(658, globalIdentityBins);
        var varietyAfter = Assert.Single((await variety.GetSummaryAsync("All", default)).Varieties,
            x => string.Equals(x.VarietyKey, "GALA", StringComparison.OrdinalIgnoreCase)).CurrentBins;
        Assert.Equal(varietyBefore, varietyAfter);

        var mcpTransfer = await db.RoomTransfers.SingleAsync(x => x.OperationKey == mcpMoveKey);
        var untreatedTransfer = await db.RoomTransfers.SingleAsync(x => x.OperationKey == untreatedMoveKey);
        Assert.Null(await dashboard.ReverseRoomTransferAsync(new ReverseRoomTransferForm
        {
            Id = mcpTransfer.Id,
            OperationKey = $"restored-treatment-aware-transfer-mcp-reverse-{runKey}",
            Reason = "Disposable MCP restoration proof"
        }, default));
        Assert.Null(await dashboard.ReverseRoomTransferAsync(new ReverseRoomTransferForm
        {
            Id = untreatedTransfer.Id,
            OperationKey = $"restored-treatment-aware-transfer-untreated-reverse-{runKey}",
            Reason = "Disposable untreated restoration proof"
        }, default));
        var restoredSnapshot = Assert.Single(await ledger.GetSnapshotsAsync(receipt.WarehouseId, [receipt.RoomId], default),
            x => SameIdentity(x, snapshot));
        var restoredSegments = await treatments.GetSelectionsAsync(restoredSnapshot, default);
        Assert.Equal(658, restoredSnapshot.CurrentBins);
        Assert.Equal(64, restoredSegments.Single(x => x.TreatmentSignature == mcp.TreatmentSignature).CurrentBins);
        Assert.Equal(594, restoredSegments.Single(x => x.TreatmentState == TreatmentLineageStates.Untreated).CurrentBins);
        Assert.Equal(2, await db.TreatmentLineageMovements.CountAsync(x => x.ReversesTreatmentLineageMovementId != null
            && (x.RoomTransferId == mcpTransfer.Id || x.RoomTransferId == untreatedTransfer.Id)));

        static bool SameIdentity(RoomInventoryLedgerSnapshot x, RoomInventoryLedgerSnapshot expected) =>
            x.CropYear == expected.CropYear
            && x.GrowerLotId == expected.GrowerLotId
            && x.FruitProfileId == expected.FruitProfileId
            && string.Equals(x.GrowerNumber ?? x.Lot, expected.GrowerNumber ?? expected.Lot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Variety, expected.Variety, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ProductionType, expected.ProductionType, StringComparison.OrdinalIgnoreCase)
            && x.IsOrganic == expected.IsOrganic;
    }

    private static DashboardDataService CreateDashboardService(
        CropQcDbContext db,
        ClaimsPrincipal principal,
        IRoomInventoryLedgerQueryService ledger,
        IRoomTreatmentService treatments,
        IBusinessTimeService businessTime)
    {
        var configuration = new ConfigurationBuilder().Build();
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
            new CropYearService(db, configuration),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            configuration,
            NullLogger<DashboardDataService>.Instance,
            new UserAccessService(db, configuration),
            businessTime: businessTime,
            roomInventoryLedgerQueryService: ledger,
            roomTreatmentService: treatments);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class NoWriteVarietyColors : IVarietyColorService
    {
        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsReadOnlyAsync(
            IEnumerable<string> varietyKeys, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, VarietyColorResolved>>(varietyKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x, x => new VarietyColorResolved(
                    x,
                    VarietyColorService.NormalizeIdentity(x, x).Name,
                    VarietyColorService.FallbackColor(x),
                    false), StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsAsync(
            IEnumerable<string> varietyKeys, CancellationToken cancellationToken) =>
            GetResolvedColorsReadOnlyAsync(varietyKeys, cancellationToken);
        public Task<VarietyColorsAdminViewModel> GetAdminPageAsync(bool canManage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsForMasterDataAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> SaveAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ResetAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
