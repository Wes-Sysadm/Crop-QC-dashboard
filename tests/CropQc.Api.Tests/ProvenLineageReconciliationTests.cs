using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class ProvenLineageReconciliationTests
{
    private long auditCutoff;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-25T05:00:00Z");

    [Theory]
    [InlineData("EBS", false, 19, 29)]
    [InlineData("WP", false, 27, 28)]
    [InlineData("WP_DH", true, 19, 29)]
    [InlineData("McDougall", true, 27, 28)]
    public async Task Proven_shared_untreated_overcount_is_readonly_eligible_and_audited_on_room_move(
        string site, bool organic, int bins, int explicitBins)
    {
        await using var db = await SeedAsync(site, organic, bins, explicitBins);
        var service = Services(db).Treatment;
        var snapshot = Assert.Single(await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(null, [9002], default));
        var before = await HistoryAsync(db);
        var choice = Assert.Single(await service.GetSelectionsAsync(snapshot, default));
        Assert.True(choice.IsAvailable);
        Assert.Equal(bins, choice.CurrentBins);
        Assert.Equal(explicitBins, (await db.TreatmentLineageSegments.SingleAsync()).CurrentBins);
        Assert.Empty(await db.AuditLogs.ToListAsync());
        var parent = new RoomTransfer
        {
            OperationKey = "test-parent",
            SourceWarehouseId = 9001,
            SourceRoomId = 9002,
            DestinationWarehouseId = 9001,
            DestinationRoomId = 9003,
            CropYear = 2026,
            GrowerLotId = 9005,
            FruitProfileId = 9004,
            GrowerName = "Test grower",
            LotNumber = "LOT",
            VarietyCode = organic ? "ORGA" : "GALA",
            InventoryStatus = organic ? "Organic" : "Conventional",
            BinCount = bins,
            Reason = "Reconciliation regression",
            TransferredAt = Now,
            CreatedAt = Now
        };
        db.RoomTransfers.Add(parent);
        await db.SaveChangesAsync();
        var result = await service.MoveAsync(snapshot, "u", bins, 9001, 9003, "proven-transfer",
            TreatmentLineageMovementTypes.Transfer, parent.Id, null, null, Now, null, default);
        Assert.True(result.Success, result.Error);
        Assert.Equal(0, await db.TreatmentLineageSegments.Where(x => x.RoomId == 9002).SumAsync(x => x.CurrentBins));
        Assert.Equal(bins, await db.TreatmentLineageSegments.Where(x => x.RoomId == 9003).SumAsync(x => x.CurrentBins));
        Assert.Equal(before, await HistoryAsync(db));
        var audit = Assert.Single(await db.AuditLogs.Where(x => x.Action == "NormalizeHistoricalTreatmentLineage").ToListAsync());
        Assert.Contains("ledgerIds", audit.AfterValuesJson);
        Assert.All(await db.TreatmentLineageSegments.ToListAsync(), x => Assert.Equal(organic, x.IsOrganicSnapshot));
        Assert.True((await service.MoveAsync(snapshot, "u", bins, 9001, 9003, "proven-transfer",
            TreatmentLineageMovementTypes.Transfer, parent.Id, null, null, Now, null, default)).Success);
        Assert.Single(await db.TreatmentLineageMovements.ToListAsync());
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("no-ledger")]
    [InlineData("unknown")]
    [InlineData("receipt-specific")]
    [InlineData("malformed-key")]
    [InlineData("hold")]
    [InlineData("identity-mismatch")]
    [InlineData("unproven-return")]
    [InlineData("unproven-transfer")]
    [InlineData("negative")]
    [InlineData("transfer-receipt")]
    [InlineData("mixed-treatment")]
    [InlineData("treatment-link")]
    [InlineData("unprojected-application")]
    [InlineData("conflicting-movement")]
    public async Task Count_alone_or_ambiguous_provenance_never_normalizes(string ambiguity)
    {
        await using var db = await SeedAsync("EBS", false, 19, 29);
        var snapshot = Assert.Single(await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(null, [9002], default));
        var segment = await db.TreatmentLineageSegments.SingleAsync();
        var row = await db.RoomInventoryAdjustments.SingleAsync();
        switch (ambiguity)
        {
            case "conflicting-movement":
                db.Add(new TreatmentLineageMovement
                {
                    OperationKey = "contradictory-history",
                    MovementType = TreatmentLineageMovementTypes.Transfer,
                    SourceSegmentId = segment.Id,
                    SourceRoomId = 9002,
                    DestinationRoomId = 9003,
                    IdentityKey = segment.IdentityKey,
                    TreatmentStateSnapshot = "Confirmed",
                    TreatmentSignatureSnapshot = "u|a:1",
                    BinCount = 10,
                    OccurredAt = Now
                });
                break;
            case "no-ledger": db.RoomInventoryAdjustments.Remove(row); break;
            case "negative": segment.CurrentBins = -1; break;
            case "transfer-receipt": (await db.Receipts.SingleAsync()).IsTransferReceipt = true; break;
            case "mixed-treatment": segment.TreatmentState = "Confirmed"; segment.TreatmentSignature = "u|a:1"; break;
            case "treatment-link":
            case "unprojected-application":
                var application = new RoomTreatmentApplication
                {
                    OperationKey = "test-application",
                    WarehouseId = 9001,
                    RoomId = 9002,
                    AppliedAt = Now,
                    ProductNameSnapshot = "Known treatment",
                    CropSnapshot = "Apple",
                    UnitSnapshot = "BIN",
                    CurrencySnapshot = "USD"
                };
                db.Add(application);
                if (ambiguity == "treatment-link") segment.Applications.Add(new TreatmentLineageSegmentApplication { RoomTreatmentApplication = application });
                break;
            case "unknown": segment.TreatmentState = "Unknown"; segment.TreatmentSignature = "x"; break;
            case "receipt-specific": segment.ReceiptId = 9006; break;
            case "malformed-key": segment.IdentityKey += "|EXTRA"; break;
            case "hold": segment.IdentityKey += "HOLD"; segment.InventoryStatusSnapshot = "HOLD"; break;
            case "identity-mismatch": segment.IsOrganicSnapshot = true; break;
            case "unproven-return": row.AdjustmentType = "TransferReversal"; break;
            case "unproven-transfer": row.AdjustmentType = "TransferIn"; row.RoomTransferId = 99; break;
        }
        await db.SaveChangesAsync();
        var choice = Assert.Single(await Services(db).Treatment.GetSelectionsAsync(snapshot, default));
        Assert.False(choice.IsAvailable);
        Assert.Equal(ambiguity == "negative" ? -1 : 29, segment.CurrentBins);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Balanced_lineage_and_unrelated_identity_are_unchanged()
    {
        await using var db = await SeedAsync("EBS", false, 19, 19);
        var snapshot = Assert.Single(await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(null, [9002], default));
        var original = await db.TreatmentLineageSegments.SingleAsync();
        var other = (TreatmentLineageSegment)db.Entry(original).CurrentValues.ToObject();
        other.Id = 9010; other.GrowerLotId = 9011; other.LotNumberSnapshot = "OTHER";
        other.IdentityKey = other.IdentityKey.Replace("9005", "9011").Replace("LOT", "OTHER");
        other.CurrentBins = 100;
        db.Add(other); await db.SaveChangesAsync();
        var choices = await Services(db).Treatment.GetSelectionsAsync(snapshot, default);
        Assert.Equal(19, Assert.Single(choices).CurrentBins);
        Assert.Equal(100, other.CurrentBins);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Restored_examples_processor_sale_audit_rollback_and_history_when_configured()
    {
        var connection = Environment.GetEnvironmentVariable("LINEAGE_RECONCILIATION_RESTORE_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection);
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
        auditCutoff = await db.AuditLogs.MaxAsync(x => x.Id);
        var adopted = await db.InterCrewTransfers.Where(x => x.Id >= 1 && x.Id <= 15).ToListAsync();
        Assert.Equal(15, adopted.Count);
        Assert.All(adopted, x => { Assert.True(x.RequiresTruckReceipt); Assert.Equal("InTransit", x.Status); });
        Assert.Equal(630, adopted.Sum(x => x.BinsLoaded));
        var before = await ProtectedAsync(db);
        var services = Services(db);
        var page = await services.Processor.GetPageAsync(null, false, null, null, null, null, default);
        var choices = new[] { (13, "9722", 19), (7, "9682", 27) }.Select(expected =>
        {
            var choice = Assert.Single(page.Inventory, x => x.RoomId == expected.Item1 && x.LotNumber == expected.Item2);
            Assert.Equal(expected.Item3, choice.AvailableBins);
            return choice;
        }).ToList();
        Assert.Equal(before, await ProtectedAsync(db)); // GET has no reconciliation writes.
        Assert.Equal(29, await db.TreatmentLineageSegments.Where(x => x.Id == 200 || x.Id == 590).SumAsync(x => x.CurrentBins));
        Assert.Equal(28, await db.TreatmentLineageSegments.Where(x => x.Id == 401).SumAsync(x => x.CurrentBins));

        var processor = new Processor { Name = "Tree Top disposable processor-sale rehearsal", Code = "TEST-TT", IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        db.Add(processor); await db.SaveChangesAsync();
        var fullBefore = await WholeAsync(db);
        ProcessorShipmentForm Form(string key) => new()
        {
            OperationKey = key,
            ProcessorId = processor.Id,
            SaleRate = 1m,
            PricingBasis = ProcessorPricingBases.PerBin,
            Currency = "USD",
            ShippedAt = DateTime.Parse("2026-09-24T21:00:00"),
            ConfirmedReview = true,
            Lines = choices.Select(x => new ProcessorShipmentLineForm { SourceKey = x.SourceKey, ExpectedAvailableBins = x.AvailableBins, BinsSent = x.AvailableBins }).ToList()
        };
        // Inject failure specifically at audit persistence, after normalization has
        // been staged, to prove the production service's entire transaction rolls back.
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION lineage_test_audit_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\" = 'NormalizeHistoricalTreatmentLineage' THEN RAISE EXCEPTION 'injected normalization audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER lineage_test_audit_failure BEFORE INSERT ON \"AuditLogs\" FOR EACH ROW EXECUTE FUNCTION lineage_test_audit_failure();");
        try
        {
            var failed = await services.Processor.CreateAsync(Form("lineage-audit-failure"), default);
            Assert.False(failed.Success);
            db.ChangeTracker.Clear();
            Assert.Equal(before, await ProtectedAsync(db));
            Assert.Equal(fullBefore, await WholeAsync(db));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER lineage_test_audit_failure ON \"AuditLogs\"; DROP FUNCTION lineage_test_audit_failure();");
        }
        // Tree Top is configured as an Outside Warehouse in the restored production
        // data. Exercise that actual destination path as well as processor sales.
        var outside = services.Outside;
        var treeTop = await db.OutsideWarehouses.SingleAsync(x => x.Name == "Tree Top");
        foreach (var expected in new[] { (13, "9722", 19), (7, "9682", 27) })
        {
            var option = Assert.Single(await outside.GetInventoryAsync(default), x => x.RoomId == expected.Item1 && x.LotNumber == expected.Item2);
            Assert.True(option.IsAvailable);
            var sent = await outside.CreateAsync(new OutsideWarehouseTransferForm
            {
                OperationKey = "tree-top-" + expected.Item2,
                OutsideWarehouseId = treeTop.Id,
                SourceKey = option.SourceKey,
                ExpectedAvailableBins = expected.Item3,
                BinCount = expected.Item3,
                TransferredAt = DateTime.Parse("2026-09-24T21:00:00"),
                ConfirmedReview = true
            }, default);
            Assert.True(sent.Success, sent.Error);
            Assert.Null(await outside.ReverseAsync(new OutsideWarehouseTransferReversalForm
            { TransferId = sent.TransferId!.Value, OperationKey = "return-" + expected.Item2, Reason = "Disposable rehearsal return" }, default));
        }
        choices = (await services.Processor.GetPageAsync(null, false, null, null, null, null, default)).Inventory
            .Where(x => (x.RoomId == 13 && x.LotNumber == "9722") || (x.RoomId == 7 && x.LotNumber == "9682")).ToList();
        Assert.Equal(46, choices.Sum(x => x.AvailableBins));
        var result = await services.Processor.CreateAsync(Form("lineage-treetop-proof"), default);
        Assert.True(result.Success, result.Error);
        Assert.True((await services.Processor.CreateAsync(Form("lineage-treetop-proof"), default)).AlreadyApplied);
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.TreatmentLineageSegments.Where(x => x.Id == 200 || x.Id == 590 || x.Id == 401).SumAsync(x => x.CurrentBins));
        var ledger = new RoomInventoryLedgerQueryService(db);
        var snapshots = await ledger.GetSnapshotsAsync(null, [7, 13], default);
        Assert.Equal(0, snapshots.Where(x => x.RoomId == 13 && x.Lot == "9722").Sum(x => x.CurrentBins));
        Assert.Equal(0, snapshots.Where(x => x.RoomId == 7 && x.Lot == "9682").Sum(x => x.CurrentBins));
        var audits = await db.AuditLogs.Where(x => x.Action == "NormalizeHistoricalTreatmentLineage").ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, x => x.AfterValuesJson!.Contains("\"currentBins\":19"));
        Assert.Contains(audits, x => x.AfterValuesJson!.Contains("\"currentBins\":27"));
        Assert.Equal(before, await ProtectedAsync(db));
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "NormalizeHistoricalTreatmentLineage"));
    }

    private async Task<string> ProtectedAsync(CropQcDbContext db)
    {
        // Fixed historical cutoffs from verified Backup 167; new shipment rows
        // are excluded only in the successful-post test, never in the failure test.
        var parts = new List<string>();
        foreach (var (table, predicate) in new[] {
            ("Receipts", "true"), ("InterCrewTransfers", "true"), ("RoomTransfers", "true"),
            ("RoomTreatmentApplications", "true"), ("TreatmentLineageSegmentApplications", "true"),
            ("RoomInventoryAdjustments", "\"Id\" <= 3688"), ("TreatmentLineageMovements", "\"Id\" <= 726"),
            ("TreatmentLineageSegments", "\"Id\" NOT IN (200,590,401)"), ("AuditLogs", "\"Id\" <= " + auditCutoff) })
#pragma warning disable EF1002 // Fixed tables/predicates and numeric fixture cutoff only.
            parts.Add(await db.Database.SqlQueryRaw<string>($"SELECT COALESCE(md5(string_agg(v, '' ORDER BY v)), '') AS \"Value\" FROM (SELECT row_to_json(t)::text v FROM \"{table}\" t WHERE {predicate}) s").SingleAsync());
#pragma warning restore EF1002
        return string.Join('|', parts);
    }

    private static Task<string> HistoryAsync(CropQcDbContext db) => Task.FromResult(JsonSerializer.Serialize(
        db.Receipts.AsNoTracking().ToList().Select(x => new { x.Id, x.BinCount, x.GrowerLotId, x.FruitProfileId })));

    private static async Task<string> WholeAsync(CropQcDbContext db)
    {
        var hashes = new List<string>();
        foreach (var table in new[] { "RoomInventoryAdjustments", "TreatmentLineageSegments", "TreatmentLineageMovements", "AuditLogs", "ProcessorShipments", "ProcessorShipmentLines" })
#pragma warning disable EF1002 // Fixed test-only table names above.
            hashes.Add(await db.Database.SqlQueryRaw<string>($"SELECT COALESCE(md5(string_agg(v, '' ORDER BY v)), '') AS \"Value\" FROM (SELECT row_to_json(t)::text v FROM \"{table}\" t) s").SingleAsync());
#pragma warning restore EF1002
        return string.Join('|', hashes);
    }

    private static (RoomTreatmentService Treatment, ProcessorShipmentService Processor, OutsideWarehouseTransferService Outside) Services(CropQcDbContext db)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test")) } };
        var access = new UserAccessService(db, new ConfigurationBuilder().Build());
        var ledger = new RoomInventoryLedgerQueryService(db);
        var time = new PacificBusinessTimeService(new Clock());
        var treatment = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
        return (treatment, new ProcessorShipmentService(db, ledger, treatment, treatment,
            new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), access, accessor, time), new OutsideWarehouseTransferService(db, ledger, treatment, treatment,
            new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), access, accessor, time));
    }

    private static async Task<CropQcDbContext> SeedAsync(string site, bool organic, int bins, int explicitBins)
    {
        var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var production = organic ? "Organic" : "Conventional";
        var variety = organic ? "ORGA" : "GALA";
        db.AddRange(new Warehouse { Id = 9001, Code = site, Name = site },
            new Room { Id = 9002, WarehouseId = 9001, Code = "A", Name = "A" },
            new Room { Id = 9003, WarehouseId = 9001, Code = "B", Name = "B" },
            new FruitProfile { Id = 9004, Name = "Gala", VarietyCode = variety, FruitType = "Apple", ProductionType = production, IsOrganic = organic },
            new GrowerLot { Id = 9005, Grower = "Test grower", LotNumber = "LOT" },
            new Receipt { Id = 9006, CropYear = 2026, CompuTechReceiptId = "TEST", WarehouseId = 9001, RoomId = 9002, FruitProfileId = 9004, GrowerLotId = 9005, GrowerNumber = "LOT", GrowerName = "Test grower", LotCode = "LOT", BinCount = bins, ReceivedAt = Now.AddDays(-1) },
            new RoomInventoryAdjustment { Id = 9007, CropYear = 2026, ReceiptId = 9006, WarehouseId = 9001, RoomId = 9002, FruitProfileId = 9004, GrowerLotId = 9005, GrowerName = "Test grower", LotNumber = "LOT", ChangeAmount = bins, NewBinCount = bins, AdjustmentType = "ReceiptAdd", AdjustmentAt = Now.AddDays(-1) });
        await db.SaveChangesAsync();
        var snapshot = Assert.Single(await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(null, [9002], default));
        db.Add(new TreatmentLineageSegment
        {
            Id = 9008,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 9005,
            FruitProfileId = 9004,
            IdentityKey = RoomTreatmentService.IdentityKey(snapshot),
            GrowerNumberSnapshot = "LOT",
            GrowerNameSnapshot = "Test grower",
            LotNumberSnapshot = "LOT",
            VarietyCodeSnapshot = variety,
            ProductionTypeSnapshot = production,
            IsOrganicSnapshot = organic,
            TreatmentState = "Untreated",
            TreatmentSignature = "u",
            CurrentBins = explicitBins,
            CreatedAt = Now.AddDays(-2),
            UpdatedAt = Now.AddDays(-2)
        });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
}
