using System.Collections.Immutable;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using LegacyLedger = CropQc.Web.Services.RoomInventoryLedgerQueryService;
using LegacyTreatment = CropQc.Web.Services.RoomTreatmentService;

namespace CropQc.Api.Tests;

public sealed class InventoryAvailabilityDatabaseTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Resolver_preserves_tracked_entities_timestamps_projections_and_audits()
    {
        var guard = new ReadGuard();
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(guard).Options);
        await SeedAsync(db, 1);
        var before = State(db);
        var count = db.ChangeTracker.Entries().Count();
        var persisted = await PersistedAsync(db);
        guard.Enabled = true;
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var first = await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        var second = await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(19, Assert.Single(first.Positions).AvailableQuantity);
        Assert.Equal(first.Positions[0].Watermark, second.Positions[0].Watermark with { Versions = first.Positions[0].Watermark.Versions });
        Assert.Equal(count, db.ChangeTracker.Entries().Count());
        Assert.Equal(before, State(db));
        Assert.Equal(persisted, await PersistedAsync(db));
        Assert.Equal(0, guard.SaveAttempts);
    }

    [Fact]
    public async Task Shared_module_is_resolvable_without_referencing_Web_or_API()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CropQcDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddCanonicalInventoryReads();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await SeedAsync(scope.ServiceProvider.GetRequiredService<CropQcDbContext>(), 1);
        var result = await scope.ServiceProvider.GetRequiredService<IInventoryAvailability>()
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(19, Assert.Single(result.Positions).AvailableQuantity);
        Assert.DoesNotContain(typeof(InventoryAvailabilityResolver).Assembly.GetReferencedAssemblies(), x => x.Name is "CropQc.Web" or "CropQc.Api");
        Assert.DoesNotContain(typeof(InventoryEvidenceLoader).Assembly.GetReferencedAssemblies(), x => x.Name is "CropQc.Web" or "CropQc.Api");
    }

    [Fact]
    public void Admin_diagnostic_has_explicit_admin_policy_and_only_get_actions()
    {
        var controller = typeof(InventoryShadowController);
        Assert.Equal(CropQc.Web.Services.AccessPolicyNames.HistoricalInventoryCleanupAdmin,
            Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>()).Policy);
        Assert.Empty(controller.GetMethods().SelectMany(x => x.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false)));
        Assert.Single(controller.GetMethods().SelectMany(x => x.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), false)));
    }

    [Fact]
    public async Task InTransit_adapter_uses_dispatch_custody_and_rejects_unbalanced_parent()
    {
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await SeedAsync(db, 1);
        var segment = await db.TreatmentLineageSegments.SingleAsync();
        var at = InventoryEvidenceCorpus.Start.AddDays(1);
        var transfer = new InterCrewTransfer
        {
            Id = 50,
            OperationKey = "corpus-dispatch",
            RequiresTruckReceipt = true,
            SourceWarehouseId = 9001,
            SourceRoomId = 9002,
            DestinationCustodyGroup = "EBS",
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerNumberSnapshot = segment.GrowerNumberSnapshot,
            GrowerNameSnapshot = "Corpus",
            LotNumberSnapshot = segment.LotNumberSnapshot,
            VarietyCodeSnapshot = "CGAL",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            TreatmentSummarySnapshot = "Untreated",
            BinsLoaded = 19,
            LoadedAt = at,
            CreatedAt = at,
            Status = "InTransit"
        };
        db.AddRange(transfer, new RoomInventoryAdjustment
        {
            Id = 200000,
            CropYear = 2026,
            WarehouseId = 9001,
            RoomId = 9002,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = segment.LotNumberSnapshot,
            VarietyCode = "CGAL",
            ChangeAmount = -19,
            AdjustmentType = "InterCrewTransferDispatch",
            AdjustmentAt = at,
            CreatedAt = at,
            InterCrewTransferId = 50
        }, new TreatmentLineageMovement
        {
            Id = 200000,
            OperationKey = "corpus-dispatch-movement",
            MovementType = "InterCrewDispatch",
            SourceSegmentId = segment.Id,
            SourceRoomId = 9002,
            IdentityKey = segment.IdentityKey,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            BinCount = 19,
            InterCrewTransferId = 50,
            OccurredAt = at,
            CreatedAt = at
        });
        await db.SaveChangesAsync();
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var transitScope = new InventoryScope(9001, [], InventoryCustody.InTransit, 50);
        var transit = Assert.Single((await resolver.ResolveAsync(transitScope, new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(19, transit.AuthoritativeQuantity);
        Assert.Equal(19, transit.AvailableQuantity);
        Assert.Equal(0, transit.CommittedQuantity); // Already deducted from the room, not a second reservation.
        var room = Assert.Single((await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(0, room.AuthoritativeQuantity);
        Assert.Equal(0, room.AvailableQuantity);
        transfer.BinsLoaded = 20;
        await db.SaveChangesAsync();
        var invalid = Assert.Single((await resolver.ResolveAsync(transitScope, new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(0, invalid.AvailableQuantity);
        Assert.Contains(invalid.Blockers, x => x.Code == InventoryBlockerCode.InvalidCustody);
        transfer.BinsLoaded = 19;
        (await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 200000)).ChangeAmount = -38;
        db.Add(new RoomInventoryAdjustment
        {
            Id = 200001,
            CropYear = 2026,
            WarehouseId = 9001,
            RoomId = 9999,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = segment.LotNumberSnapshot,
            VarietyCode = "CGAL",
            ChangeAmount = 19,
            AdjustmentType = "InterCrewTransferReceive",
            AdjustmentAt = at,
            CreatedAt = at,
            InterCrewTransferId = 50
        });
        await db.SaveChangesAsync();
        var wrongCustody = Assert.Single((await resolver.ResolveAsync(transitScope, new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow)).Positions);
        Assert.Contains(wrongCustody.Blockers, x => x.Code == InventoryBlockerCode.InvalidCustody);
    }

    [InventoryPostgresFact]
    public async Task Provider_batch_round_trips_are_bounded_and_legacy_baseline_is_measured()
    {
        var original = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_TEST_POSTGRES")!;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(original);
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(original)
        { Database = $"canonical_inventory_{Guid.NewGuid():N}_test" }.ConnectionString;
        var guard = new ReadGuard();
        var counter = new QueryCounter();
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection)
            .AddInterceptors(guard, counter).Options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var measurements = new List<object>();
            foreach (var count in new[] { 1, 10, 100, 1000 })
            {
                guard.Enabled = false;
                var room = 9002 + count;
                await SeedAsync(db, count, room, 100000 + count * 10000);
                db.ChangeTracker.Clear();
                guard.Enabled = true;
                var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
                counter.Reads = 0;
                var allocatedBefore = GC.GetTotalAllocatedBytes(true);
                var watch = Stopwatch.StartNew();
                var result = await resolver.ResolveAsync(new(9001, [room]), new(), DateTimeOffset.UtcNow);
                watch.Stop();
                var queries = counter.Reads;
                var bytes = GC.GetTotalAllocatedBytes(false) - allocatedBefore;
                Assert.Equal(count, result.Positions.Length);
                Assert.All(result.Positions, x => Assert.Equal(19, x.AvailableQuantity));
                Assert.Equal(10, queries);
                Assert.Empty(db.ChangeTracker.Entries());
                Assert.Equal(0, guard.SaveAttempts);
                var ledger = new LegacyLedger(db);
                var treatment = new LegacyTreatment(db, ledger, null!, null!, null!, NullLogger<LegacyTreatment>.Instance);
                counter.Reads = 0;
                var baselineWatch = Stopwatch.StartNew();
                var snapshots = await ledger.GetSnapshotsAsync(9001, [room], default);
                var legacy = await treatment.GetSelectionsAsync(snapshots, default);
                baselineWatch.Stop();
                Assert.Equal(count, legacy.Count);
                var baselineQueries = counter.Reads;
                Assert.True(baselineQueries > queries || count == 1);
                measurements.Add(new
                {
                    positions = count,
                    queries,
                    result.EvidenceRowsLoaded,
                    elapsedMs = watch.Elapsed.TotalMilliseconds,
                    allocatedBytes = bytes,
                    legacyQueries = baselineQueries,
                    legacyElapsedMs = baselineWatch.Elapsed.TotalMilliseconds
                });
            }
            output.WriteLine(JsonSerializer.Serialize(measurements));
            var report = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_PERFORMANCE_REPORT");
            if (!string.IsNullOrWhiteSpace(report)) await File.WriteAllTextAsync(report, JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            guard.Enabled = false;
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection);
            await db.Database.EnsureDeletedAsync();
        }
    }

    internal static async Task SeedAsync(CropQcDbContext db, int count, int room = 9002, int firstId = 100000)
    {
        if (!await db.Warehouses.AnyAsync(x => x.Id == 9001))
            db.AddRange(new Warehouse { Id = 9001, Code = "CORPUS", Name = "Corpus" },
                new FruitProfile { Id = 9004, Name = "Gala", VarietyCode = "CGAL", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false });
        db.Add(new Room { Id = room, WarehouseId = 9001, Code = $"R{room}", Name = $"Corpus room {room}" });
        for (var i = 0; i < count; i++)
        {
            var id = firstId + i;
            var lot = $"CORPUS-{id}";
            var identity = new InventoryIdentity(2026, id, 9004, lot, lot, "CGAL", "Conventional", false, "");
            db.AddRange(new GrowerLot { Id = id, Grower = "Corpus", LotNumber = lot },
                new Receipt
                {
                    Id = id,
                    CropYear = 2026,
                    CompuTechReceiptId = lot,
                    WarehouseId = 9001,
                    RoomId = room,
                    FruitProfileId = 9004,
                    GrowerLotId = id,
                    GrowerNumber = lot,
                    GrowerName = "Corpus",
                    LotCode = lot,
                    BinCount = 19,
                    ReceivedAt = InventoryEvidenceCorpus.Start,
                    CreatedAt = InventoryEvidenceCorpus.Start,
                    UpdatedAt = InventoryEvidenceCorpus.Start
                },
                new RoomInventoryAdjustment
                {
                    Id = id,
                    CropYear = 2026,
                    ReceiptId = id,
                    WarehouseId = 9001,
                    RoomId = room,
                    FruitProfileId = 9004,
                    GrowerLotId = id,
                    GrowerName = "Corpus",
                    LotNumber = lot,
                    VarietyCode = "CGAL",
                    ChangeAmount = 19,
                    NewBinCount = 19,
                    AdjustmentType = "ReceiptAdd",
                    AdjustmentAt = InventoryEvidenceCorpus.Start,
                    CreatedAt = InventoryEvidenceCorpus.Start
                },
                new TreatmentLineageSegment
                {
                    Id = id,
                    WarehouseId = 9001,
                    RoomId = room,
                    CropYear = 2026,
                    GrowerLotId = id,
                    FruitProfileId = 9004,
                    IdentityKey = identity.Key,
                    GrowerNumberSnapshot = lot,
                    GrowerNameSnapshot = "Corpus",
                    LotNumberSnapshot = lot,
                    VarietyCodeSnapshot = "CGAL",
                    ProductionTypeSnapshot = "Conventional",
                    IsOrganicSnapshot = false,
                    TreatmentState = "Untreated",
                    TreatmentSignature = "u",
                    CurrentBins = 29,
                    CreatedAt = InventoryEvidenceCorpus.Start,
                    UpdatedAt = InventoryEvidenceCorpus.Start
                });
        }
        await db.SaveChangesAsync();
    }

    private static string State(CropQcDbContext db) => JsonSerializer.Serialize(db.ChangeTracker.Entries()
        .Select(x => new { Type = x.Entity.GetType().Name, x.State, Values = x.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue) }));
    private static async Task<string> PersistedAsync(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Segments = await db.TreatmentLineageSegments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.CurrentBins, x.UpdatedAt, x.ConcurrencyVersion }).ToListAsync(),
        Receipts = await db.Receipts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.UpdatedAt, x.ConcurrencyVersion }).ToListAsync(),
        Ledger = await db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ChangeAmount, x.NewBinCount }).ToListAsync(),
        Audits = await db.AuditLogs.CountAsync()
    });

    private sealed class ReadGuard : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public int SaveAttempts { get; private set; }
        private void Check() { if (Enabled) { SaveAttempts++; throw new InvalidOperationException("Read attempted SaveChanges"); } }
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { Check(); return result; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Check(); return ValueTask.FromResult(result); }
    }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Reads++; return ValueTask.FromResult(result); }
    }
}

public sealed class InventoryPostgresFactAttribute : FactAttribute
{
    public InventoryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_TEST_POSTGRES")))
            Skip = "Requires an isolated local PostgreSQL test connection; never production.";
    }
}
