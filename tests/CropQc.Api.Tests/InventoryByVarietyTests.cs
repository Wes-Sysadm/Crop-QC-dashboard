using CropQc.Data;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class InventoryByVarietyTests
{
    [Fact]
    public async Task Summary_reconciles_dashboard_total_and_canonical_varieties_without_multiplying_treatments()
    {
        await using var fixture = new Fixture([
            Snapshot(1, "WP", 11, "WP-11", "Gala", "GALA", "9350", "Grower A", "Conventional", false, 60),
            Snapshot(1, "WP", 12, "WP-12", "Organic Gala", "GALA", "9351", "Grower B", "Organic", true, 40),
            Snapshot(2, "EBS", 21, "EVANS-9", "GSMT", "GSMT", "1084", "Grower C", "Conventional", false, 30),
            Snapshot(2, "EBS", 22, "LAMB-15", "Grannysmith", "GRNY", "1085", "Grower D", "Conventional", false, 20)
        ]);

        var page = await fixture.Service.GetSummaryAsync("All", default);

        Assert.Equal(150, page.TotalCurrentBins);
        Assert.Equal(2, page.Varieties.Count);
        var gala = Assert.Single(page.Varieties, x => x.VarietyKey == "GALA");
        Assert.Equal(100, gala.CurrentBins);
        Assert.Equal(2, gala.RoomCount);
        Assert.Equal(["9350", "9351"], gala.GrowerNumbers);
        Assert.Contains(gala.Breakdowns, x => x.OrganicStatus == "Organic" && x.CurrentBins == 40);
        Assert.Contains(gala.Breakdowns, x => x.OrganicStatus == "Conventional" && x.CurrentBins == 60);
        var granny = Assert.Single(page.Varieties, x => x.VarietyKey == "GRANNY_SMITH");
        Assert.Equal(50, granny.CurrentBins);
        Assert.Equal(1, fixture.Ledger.QueryCount);
        Assert.Equal(0, fixture.Treatments.BatchQueryCount);
    }

    [Fact]
    public async Task Facility_context_retains_exact_all_wp_and_ebs_totals()
    {
        await using var fixture = new Fixture([
            Snapshot(1, "WP", 11, "WP-11", "Gala", "GALA", "9350", "Grower A", "Conventional", false, 60),
            Snapshot(2, "EBS", 21, "EVANS-9", "Gala", "GALA", "9351", "Grower B", "Conventional", false, 40)
        ]);

        Assert.Equal(100, (await fixture.Service.GetSummaryAsync("All", default)).TotalCurrentBins);
        Assert.Equal(60, (await fixture.Service.GetSummaryAsync("WP", default)).TotalCurrentBins);
        Assert.Equal(40, (await fixture.Service.GetSummaryAsync("EBS", default)).TotalCurrentBins);
        Assert.Equal("WP", (await fixture.Service.GetSummaryAsync("wp", default)).Facility);
    }

    [Fact]
    public async Task Detail_total_equals_card_and_shows_exact_treatment_partition_without_quantity_duplication()
    {
        var snapshot = Snapshot(1, "WP", 11, "MCD-09", "Gala", "GALA", "9350", "Current Grower", "Conventional", false, 60);
        await using var fixture = new Fixture([snapshot]);
        fixture.Treatments.Selections[RoomTreatmentService.SelectionLookupKey(snapshot)] =
        [
            new("identity", "mcp", "Treated", 20, "MCP"),
            new("identity", "untreated", "Untreated", 40, "Untreated")
        ];

        var card = Assert.Single((await fixture.Service.GetSummaryAsync("WP", default)).Varieties);
        var detail = Assert.IsType<InventoryVarietyDetailPageViewModel>(
            await fixture.Service.GetDetailAsync(card.VarietyKey, "WP", default));

        Assert.Equal(card.CurrentBins, detail.TotalCurrentBins);
        var line = Assert.Single(detail.Lines);
        Assert.Equal(60, line.CurrentBins);
        Assert.Contains("MCP: 20 bins", line.TreatmentStatus);
        Assert.Contains("Untreated: 40 bins", line.TreatmentStatus);
        Assert.Equal("Current Grower", line.GrowerName);
        Assert.Equal(1, fixture.Treatments.BatchQueryCount);
    }

    [Fact]
    public async Task Destination_rooms_are_current_depleted_fruit_is_absent_and_room_regressions_reconcile()
    {
        await using var fixture = new Fixture([
            Snapshot(1, "MCD", 9, "MCD-09", "Gala", "GALA", "9350", "Grower A", "Conventional", false, 25),
            Snapshot(2, "EBS", 109, "EVANS-9", "Gala", "GALA", "9351", "Grower B", "Conventional", false, 20),
            Snapshot(1, "WP", 5, "SOURCE-5", "Gala", "GALA", "9352", "Grower C", "Conventional", false, 0)
        ]);

        var detail = Assert.IsType<InventoryVarietyDetailPageViewModel>(
            await fixture.Service.GetDetailAsync("GALA", "All", default));

        Assert.Equal(45, detail.TotalCurrentBins);
        Assert.Contains(detail.Lines, x => x.Room == "MCD-09" && x.CurrentBins == 25);
        Assert.Contains(detail.Lines, x => x.Room == "EVANS-9" && x.CurrentBins == 20);
        Assert.DoesNotContain(detail.Lines, x => x.Room == "SOURCE-5");
        Assert.Equal(detail.TotalCurrentBins, detail.Lines.Sum(x => x.CurrentBins));
    }

    [Fact]
    public async Task Summary_and_detail_are_zero_write_read_only_operations()
    {
        await using var fixture = new Fixture([
            Snapshot(1, "WP", 11, "WP-11", "Gala", "GALA", "9350", "Grower A", "Conventional", false, 60)
        ]);
        var before = fixture.Db.ChangeTracker.Entries().Count();

        var summary = await fixture.Service.GetSummaryAsync("All", default);
        _ = await fixture.Service.GetDetailAsync(summary.Varieties.Single().VarietyKey, "All", default);

        Assert.Equal(before, fixture.Db.ChangeTracker.Entries().Count());
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
        Assert.Equal(0, fixture.Colors.WriteCount);
    }

    [Fact]
    public async Task Empty_inventory_has_empty_state_and_unknown_variety_returns_not_found_model()
    {
        await using var fixture = new Fixture([]);

        var page = await fixture.Service.GetSummaryAsync("WP", default);

        Assert.Empty(page.Varieties);
        Assert.Equal(0, page.TotalCurrentBins);
        Assert.Null(await fixture.Service.GetDetailAsync("GALA", "WP", default));
    }

    [Fact]
    public void Navigation_authorization_provenance_and_mobile_presentation_are_wired()
    {
        var service = Read("src", "CropQc.Web", "Services", "DashboardDataService.cs");
        var controller = Read("src", "CropQc.Web", "Controllers", "HomeController.cs");
        var summary = Read("src", "CropQc.Web", "Views", "Home", "InventoryByVariety.cshtml");
        var detail = Read("src", "CropQc.Web", "Views", "Home", "InventoryVarietyDetail.cshtml");
        var css = Read("src", "CropQc.Web", "wwwroot", "css", "site.css");
        var implementation = Read("src", "CropQc.Web", "Services", "InventoryByVarietyService.cs");

        Assert.Contains("/Inventory/ByVariety?Facility={encodedFacility}", service);
        Assert.Contains("AccessPolicyNames.DashboardView", controller);
        Assert.Contains("Dashboard", summary);
        Assert.Contains("Inventory by Variety", detail);
        Assert.Contains("/Rooms/@line.RoomId?Facility=", detail);
        Assert.Contains("/Receipts/@line.ReceiptId", detail);
        Assert.Contains("inventory-variety-detail-table td::before", css);
        Assert.Contains("GetSnapshotsAsync(null, null", implementation);
        Assert.Contains("GetSelectionsAsync(snapshots", implementation);
        Assert.DoesNotContain("SaveChanges", implementation);
    }

    private static RoomInventoryLedgerSnapshot Snapshot(
        int warehouseId,
        string facility,
        int roomId,
        string room,
        string varietyName,
        string varietyCode,
        string growerNumber,
        string grower,
        string productionType,
        bool? isOrganic,
        int currentBins) => new(
            WarehouseId: warehouseId,
            Facility: facility,
            RoomId: roomId,
            Room: room,
            LocationGroup: facility,
            CropYear: 2026,
            GrowerLotId: roomId + 1000,
            FruitProfileId: roomId + 2000,
            Grower: grower,
            GrowerNumber: growerNumber,
            Lot: growerNumber,
            PoolStart: null,
            StoredVarietyCode: varietyCode,
            Variety: varietyCode,
            VarietyName: varietyName,
            FruitType: "Apple",
            ProductionType: productionType,
            IsOrganic: isOrganic,
            InventoryStatus: "Current",
            PositiveBins: currentBins,
            NegativeBins: 0,
            ActualRunDepletionBins: 0,
            ActualRunReversalBins: 0,
            LegacyBinsRunDepletionBins: 0,
            TransferInBins: 0,
            TransferOutBins: 0,
            TrueUpBins: 0,
            OtherAdjustmentBins: 0,
            CurrentBins: currentBins,
            TransactionCount: 1,
            FirstTransactionAt: DateTimeOffset.UtcNow.AddDays(-1),
            LastTransactionAt: DateTimeOffset.UtcNow,
            LatestAdjustmentId: roomId + 3000,
            SourceReference: $"Source {growerNumber}");

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln")))
        {
            directory = directory.Parent;
        }
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots)
        {
            var options = new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"inventory-by-variety-{Guid.NewGuid():N}")
                .Options;
            Db = new CropQcDbContext(options);
            Ledger = new FakeLedger(snapshots);
            Treatments = new FakeTreatments();
            Colors = new FakeColors();
            Service = new InventoryByVarietyService(
                Db,
                Ledger,
                Treatments,
                Colors,
                new FacilityContextService(Db));
        }

        public CropQcDbContext Db { get; }
        public FakeLedger Ledger { get; }
        public FakeTreatments Treatments { get; }
        public FakeColors Colors { get; }
        public InventoryByVarietyService Service { get; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeLedger(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots) : IRoomInventoryLedgerQueryService
    {
        public int QueryCount { get; private set; }
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, CancellationToken cancellationToken)
        {
            QueryCount++;
            return Task.FromResult(snapshots);
        }
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId, CancellationToken cancellationToken) => GetSnapshotsAsync(warehouseId, roomIds, cancellationToken);
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsOfAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, DateTimeOffset asOf, CancellationToken cancellationToken) => GetSnapshotsAsync(warehouseId, roomIds, cancellationToken);
    }

    private sealed class FakeTreatments : IRoomTreatmentService
    {
        public Dictionary<string, IReadOnlyList<TreatmentSegmentSelection>> Selections { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int BatchQueryCount { get; private set; }
        public Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots, CancellationToken cancellationToken)
        {
            BatchQueryCount++;
            IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>> result = snapshots.ToDictionary(
                RoomTreatmentService.SelectionLookupKey,
                x => Selections.GetValueOrDefault(RoomTreatmentService.SelectionLookupKey(x)) ?? [new("identity", "untreated", "Untreated", x.CurrentBins, "Untreated")],
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
        public Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken) => Task.FromResult(Selections.GetValueOrDefault(RoomTreatmentService.SelectionLookupKey(snapshot)) ?? (IReadOnlyList<TreatmentSegmentSelection>)[new("identity", "untreated", "Untreated", snapshot.CurrentBins, "Untreated")]);
        public Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> MoveAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeColors : IVarietyColorService
    {
        public int WriteCount { get; private set; }
        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsReadOnlyAsync(IEnumerable<string> varietyKeys, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, VarietyColorResolved>>(varietyKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(x => x, x => new VarietyColorResolved(x, VarietyColorService.NormalizeIdentity(x, x).Name, VarietyColorService.FallbackColor(x), false), StringComparer.OrdinalIgnoreCase));
        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsAsync(IEnumerable<string> varietyKeys, CancellationToken cancellationToken) => GetResolvedColorsReadOnlyAsync(varietyKeys, cancellationToken);
        public Task<VarietyColorsAdminViewModel> GetAdminPageAsync(bool canManage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsForMasterDataAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> SaveAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken) { WriteCount++; throw new NotSupportedException(); }
        public Task<string?> ResetAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken) { WriteCount++; throw new NotSupportedException(); }
    }
}
