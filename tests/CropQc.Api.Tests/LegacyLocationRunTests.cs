using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class LegacyLocationRunTests
{
    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audited_location_movement_restores_full_run_selection_without_inventing_stock(bool treated)
    {
        await using var f = await Seed(treated);
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        var before = await f.Snapshot();
        var evidence = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9003]), DateTimeOffset.UtcNow, default)).Positions);
        var result = InventoryAvailabilityResolver.Resolve(evidence, new() { AllowIndependentCohorts = true });
        Assert.Equal(378, result.AuthoritativeQuantity);
        Assert.Equal(129, result.RawProjectionQuantity);
        Assert.True(result.IsOperable, string.Join("; ", InventoryEventReplay.Replay(evidence).UnresolvedEvents));
        Assert.Equal(378, result.AvailableQuantity);
        var broad = (await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9002, 9003]), DateTimeOffset.UtcNow, default))
            .Positions.Single(p => p.Location.RoomId == 9003);
        Assert.Equal(evidence.Watermark.Fingerprint, broad.Watermark.Fingerprint);
        Assert.Equal(before, await f.Snapshot());
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var service = new BinsRunService(db, new CanonicalOutsideWorkflowTests.Access(), NullLogger<BinsRunService>.Instance,
            canonicalCommands: new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection), runExpectations: new CanonicalRunExpectationWriter()));
        var options = (await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9003] }, principal, default)).AvailableInventory;
        Assert.All(options, option => Assert.True(option.IsAvailable, option.UnavailableReason));
        Assert.Equal(treated ? 2 : 1, options.Count);
        Assert.Equal(treated ? 346 : 378, options.Single(x => x.TreatmentLabel == "Untreated").CurrentBins);
        if (treated) Assert.Equal(32, options.Single(x => x.TreatmentLabel == "Confirmed treatment").CurrentBins);
        var form = new ActualRunForm
        {
            RunFacilityWarehouseId = 9001,
            RunAt = DateTimeOffset.UtcNow,
            SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
            Lines = options.Select(option => new ActualRunLineForm
            {
                InventoryKey = option.InventoryKey,
                TreatmentSignature = option.TreatmentSignature,
                CanonicalFingerprint = option.CanonicalFingerprint,
                ExpectedAvailableBins = option.CurrentBins,
                BinsRun = treated ? option.TreatmentSignature == "u" ? 300 : 10 : 310
            }).ToList()
        };
        var excessive = JsonSerializer.Deserialize<ActualRunForm>(JsonSerializer.Serialize(form))!;
        excessive.Lines[0].BinsRun = 379;
        Assert.NotNull(await service.CreateActualRunAsync(excessive, principal, default));
        Assert.Equal(before, await f.Snapshot());
        Assert.Null(await service.CreateActualRunAsync(form, principal, default));
        var after = await f.Snapshot();
        Assert.Null(await service.CreateActualRunAsync(form, principal, default));
        Assert.Equal(after, await f.Snapshot());
        var remaining = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9003]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(68, remaining.AuthoritativeQuantity);
        Assert.Equal(68, remaining.AvailableQuantity);
        if (treated) Assert.Equal(22, remaining.TreatmentSlices.Where(x => x.State == "Confirmed").Sum(x => x.Quantity));
        Assert.Equal(310, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
        var conservation = await new InventoryConservationReportService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), 2026).AnalyzeAsync(default);
        Assert.Equal(0, conservation.Global.Difference);
        Assert.Empty(conservation.InvalidLocationCorrections);
        var readiness = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.True(readiness.IsReady, JsonSerializer.Serialize(readiness.Issues));
        var run = await db.ActualRuns.AsNoTracking().SingleAsync();
        Assert.Null(await service.CancelActualRunAsync(new()
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            OperationKey = Guid.NewGuid().ToString("N"),
            Reason = "Synthetic reversal"
        }, principal, default));
        var restored = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9003]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(378, restored.AuthoritativeQuantity);
        Assert.Equal(378, restored.AvailableQuantity);
        if (treated) Assert.Equal(32, restored.TreatmentSlices.Where(x => x.State == "Confirmed").Sum(x => x.Quantity));
    }

    [InventoryPostgresFact]
    public async Task Prior_signed_quantity_corrections_preserve_authorized_origin_for_the_run()
    {
        await using var f = await Seed();
        await using (var db = f.CreateDbContext())
        {
            var location = await db.ReceiptInventoryOverrides.SingleAsync();
            var receipt = await db.Receipts.SingleAsync(x => x.Id == 100000);
            var origin = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
            origin.ChangeAmount = origin.NewBinCount = 42;
            var other = await db.Receipts.SingleAsync(x => x.Id == 100001);
            var otherOrigin = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100001);
            otherOrigin.ChangeAmount = otherOrigin.NewBinCount = 93;
            foreach (var (r, l, oldBins, newBins, id) in new[] { (receipt, origin, 42, 32, 100005L), (other, otherOrigin, 93, 97, 100006L) })
            {
                var time = location.CreatedAt.AddHours(-2);
                string Snapshot(int bins, int version) => JsonSerializer.Serialize(new
                {
                    id = r.Id,
                    cropYear = r.CropYear,
                    warehouseId = 9001,
                    roomId = 9002,
                    fruitProfileId = r.FruitProfileId,
                    growerLotId = r.GrowerLotId,
                    growerNumber = r.GrowerNumber,
                    lotCode = r.LotCode,
                    receiptType = r.ReceiptType,
                    receivedAt = r.ReceivedAt,
                    binCount = bins,
                    isDeleted = false,
                    concurrencyVersion = version
                });
                var change = new ReceiptInventoryOverride
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = r.Id,
                    ActionType = "QuantityCorrection",
                    OldReceiptBinCount = oldBins,
                    NewReceiptBinCount = newBins,
                    InventoryDelta = newBins - oldBins,
                    AdministratorUserId = 8000,
                    Reason = "Synthetic signed historical correction",
                    OperationKey = $"quantity-{id}",
                    CreatedAt = time,
                    BeforeReceiptSnapshotJson = Snapshot(oldBins, 0),
                    AfterReceiptSnapshotJson = Snapshot(newBins, 1),
                    AffectedInventorySnapshotJson = "[]",
                    ExpectedAdjustmentCount = 1,
                    IsComplete = true
                };
                db.ReceiptInventoryOverrides.Add(change);
                db.AuditLogs.Add(new()
                {
                    EntityName = "ReceiptInventoryOverride",
                    EntityKey = change.Id.ToString(),
                    Action = change.ActionType,
                    UserId = 8000,
                    CreatedAt = time,
                    BeforeValuesJson = change.BeforeReceiptSnapshotJson,
                    AfterValuesJson = JsonSerializer.Serialize(new { afterReceiptSnapshotJson = change.AfterReceiptSnapshotJson })
                });
                var delta = (RoomInventoryAdjustment)db.Entry(l).CurrentValues.Clone().ToObject();
                delta.Id = id; delta.ChangeAmount = newBins - oldBins; delta.NewBinCount = newBins;
                delta.AdjustmentType = "ReceiptAdminOverride"; delta.InventoryInvariantVersion = 1;
                delta.InventoryOperationKey = $"receipt-override:{change.OperationKey}:1";
                delta.ReceiptInventoryOverrideId = change.Id; delta.AdjustmentAt = delta.CreatedAt = time; delta.CreatedByUserId = 8000;
                db.RoomInventoryAdjustments.Add(delta);
                r.ConcurrencyVersion++;
            }
            location.BeforeReceiptSnapshotJson = location.BeforeReceiptSnapshotJson.Replace("\"concurrencyVersion\":0", "\"concurrencyVersion\":1");
            location.AfterReceiptSnapshotJson = location.AfterReceiptSnapshotJson.Replace("\"concurrencyVersion\":1", "\"concurrencyVersion\":2");
            var audit = await db.AuditLogs.SingleAsync(x => x.EntityKey == location.Id.ToString());
            audit.BeforeValuesJson = location.BeforeReceiptSnapshotJson;
            audit.AfterValuesJson = JsonSerializer.Serialize(new { afterReceiptSnapshotJson = location.AfterReceiptSnapshotJson });
            await db.SaveChangesAsync();
        }
        await using var read = f.CreateDbContext();
        var position = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(read))
            .ResolveAsync(new(9001, [9003]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(378, position.AvailableQuantity);
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.Dump, 8000, DateTimeOffset.UtcNow,
            "Synthetic authorized history", [new(new(position.Identity, position.Location, position.Watermark.Fingerprint, []), 310, "u")]);
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(68, await f.Physical(9003));
    }

    [InventoryPostgresTheory]
    [InlineData("audit")]
    [InlineData("snapshot")]
    [InlineData("debit")]
    [InlineData("quantity")]
    [InlineData("actor")]
    [InlineData("key")]
    [InlineData("identity")]
    [InlineData("receipt")]
    [InlineData("incomplete")]
    [InlineData("treatment")]
    public async Task Unproven_location_allocation_stays_blocked_and_read_only(string corruption)
    {
        await using var f = await Seed();
        await using var db = f.CreateDbContext();
        var original = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9003]), DateTimeOffset.UtcNow, default)).Positions);
        var revision = await db.ReceiptInventoryOverrides.SingleAsync();
        var move = await db.TreatmentLineageMovements.SingleAsync(x => x.Id == 100010);
        switch (corruption)
        {
            case "audit": db.AuditLogs.Remove(await db.AuditLogs.SingleAsync()); break;
            case "snapshot": revision.AfterReceiptSnapshotJson = "{}"; break;
            case "debit": db.RoomInventoryAdjustments.Remove(await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100010)); break;
            case "quantity": move.BinCount++; break;
            case "actor": move.CreatedByUserId = null; break;
            case "key": move.OperationKey += "-wrong"; break;
            case "identity": move.IdentityKey += "-wrong"; break;
            case "receipt": move.ReceiptId = 100001; break;
            case "incomplete": revision.IsComplete = false; break;
            case "treatment": move.TreatmentStateSnapshot = "Unknown"; move.TreatmentSignatureSnapshot = "x"; break;
        }
        await db.SaveChangesAsync();
        var before = await f.Snapshot();
        var evidence = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9003]), DateTimeOffset.UtcNow, default)).Positions);
        Assert.NotEqual(original.Watermark.Fingerprint, evidence.Watermark.Fingerprint);
        Assert.False(InventoryAvailabilityResolver.Resolve(evidence, new()).IsOperable);
        Assert.Equal(378, evidence.AuthoritativeQuantity);
        Assert.Equal(before, await f.Snapshot());
    }

    internal static async Task<Fixture> Seed(bool treated = false)
    {
        var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var at = InventoryEvidenceCorpus.Start.AddDays(1);
        var actor = await db.Users.SingleAsync(x => x.Id == 8000);
        actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = InventoryEvidenceCorpus.Start;
        var receipt = await db.Receipts.SingleAsync();
        receipt.BinCount = 32;
        var origin = await db.RoomInventoryAdjustments.SingleAsync();
        origin.ChangeAmount = origin.NewBinCount = 32;
        var segment = await db.TreatmentLineageSegments.SingleAsync();
        segment.CurrentBins = 0;
        string Snapshot(int room, int version) => JsonSerializer.Serialize(new
        {
            id = receipt.Id,
            cropYear = receipt.CropYear,
            warehouseId = 9001,
            roomId = room,
            fruitProfileId = receipt.FruitProfileId,
            growerLotId = receipt.GrowerLotId,
            growerNumber = receipt.GrowerNumber,
            lotCode = receipt.LotCode,
            receiptType = receipt.ReceiptType,
            receivedAt = receipt.ReceivedAt,
            binCount = 32,
            isDeleted = false,
            concurrencyVersion = version
        });
        var change = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            ActionType = "LocationCorrection",
            OldReceiptBinCount = 32,
            NewReceiptBinCount = 32,
            InventoryDelta = 0,
            CurrentInventoryBefore = 32,
            CurrentInventoryAfter = 32,
            AdministratorUserId = 8000,
            Reason = "Synthetic historical location correction",
            OperationKey = "legacy-location",
            CreatedAt = at,
            BeforeReceiptSnapshotJson = Snapshot(9002, 0),
            AfterReceiptSnapshotJson = Snapshot(9003, 1),
            AffectedInventorySnapshotJson = JsonSerializer.Serialize(new[] { new { warehouseId = 9001, roomId = 9002,
                cropYear = 2026, growerLotId = 100000, fruitProfileId = 9004, lot = receipt.LotCode,
                variety = "CGAL", inventoryStatus = "", currentBins = 32 } }),
            ExpectedAdjustmentCount = 2,
            IsComplete = true
        };
        db.ReceiptInventoryOverrides.Add(change);
        db.AuditLogs.Add(new()
        {
            EntityName = "ReceiptInventoryOverride",
            EntityKey = change.Id.ToString(),
            Action = change.ActionType,
            UserId = 8000,
            CreatedAt = at,
            BeforeValuesJson = change.BeforeReceiptSnapshotJson,
            AfterValuesJson = JsonSerializer.Serialize(new { afterReceiptSnapshotJson = change.AfterReceiptSnapshotJson })
        });
        receipt.RoomId = 9003; receipt.ConcurrencyVersion = 1; receipt.UpdatedAt = at;
        RoomInventoryAdjustment Row(long id, int room, int qty, string kind, DateTimeOffset time)
        {
            var r = (RoomInventoryAdjustment)db.Entry(origin).CurrentValues.Clone().ToObject();
            r.Id = id; r.RoomId = room; r.ChangeAmount = qty; r.NewBinCount = Math.Max(0, qty);
            r.AdjustmentType = kind; r.AdjustmentAt = r.CreatedAt = time; r.ReceiptId = null;
            r.CreatedByUserId = 8000;
            return r;
        }
        foreach (var (id, room, qty) in new[] { (100010L, 9002, -32), (100011L, 9003, 32) })
        {
            var r = Row(id, room, qty, "ReceiptAdminOverride", at);
            r.ReceiptId = receipt.Id; r.ReceiptInventoryOverrideId = change.Id; r.InventoryInvariantVersion = 1;
            r.InventoryOperationKey = $"receipt-override:{change.OperationKey}:{(qty < 0 ? 1 : 2)}";
            db.RoomInventoryAdjustments.Add(r);
        }
        TreatmentLineageSegment Slice(long id, int qty)
        {
            var s = (TreatmentLineageSegment)db.Entry(segment).CurrentValues.Clone().ToObject();
            s.Id = id; s.RoomId = 9003; s.CurrentBins = qty; s.CreatedAt = s.UpdatedAt = at.AddMilliseconds(56);
            return s;
        }
        var moved = Slice(100010, 32); moved.ReceiptId = receipt.Id; db.TreatmentLineageSegments.Add(moved);
        db.TreatmentLineageMovements.Add(new()
        {
            Id = 100010,
            OperationKey = $"receipt-location-correction:{change.OperationKey}:{segment.Id}",
            MovementType = "ReceiptLocationCorrection",
            SourceRoomId = 9002,
            DestinationRoomId = 9003,
            SourceSegmentId = segment.Id,
            DestinationSegmentId = moved.Id,
            ReceiptId = receipt.Id,
            IdentityKey = segment.IdentityKey,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            BinCount = 32,
            OccurredAt = at,
            CreatedAt = at.AddMilliseconds(56),
            CreatedByUserId = 8000
        });
        foreach (var (id, room, qty) in new[] { (100001L, 9002, 97), (100002L, 9003, 249) })
        {
            var r = (Receipt)db.Entry(receipt).CurrentValues.Clone().ToObject();
            r.Id = id; r.CompuTechReceiptId = $"SYNTHETIC-{id}"; r.RoomId = room; r.BinCount = qty;
            r.ConcurrencyVersion = 0; r.UpdatedAt = r.CreatedAt;
            db.Receipts.Add(r);
            var l = Row(id, room, qty, "ReceiptAdd", InventoryEvidenceCorpus.Start); l.ReceiptId = id;
            db.RoomInventoryAdjustments.Add(l);
        }
        var transferAt = at.AddDays(1);
        db.RoomTransfers.Add(new()
        {
            Id = 100020,
            OperationKey = "legacy-transfer",
            SourceWarehouseId = 9001,
            SourceRoomId = 9002,
            DestinationWarehouseId = 9001,
            DestinationRoomId = 9003,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = receipt.LotCode!,
            VarietyCode = "CGAL",
            BinCount = 97,
            Reason = "Synthetic transfer",
            TransferredAt = transferAt,
            CreatedAt = transferAt
        });
        foreach (var (id, room, qty, kind) in new[] { (100020L, 9002, -97, "TransferOut"), (100021L, 9003, 97, "TransferIn") })
        { var l = Row(id, room, qty, kind, transferAt); l.RoomTransferId = 100020; db.RoomInventoryAdjustments.Add(l); }
        var transferred = Slice(100020, 97); db.TreatmentLineageSegments.Add(transferred);
        db.TreatmentLineageMovements.Add(new()
        {
            Id = 100020,
            OperationKey = "legacy-transfer:treatment",
            MovementType = "Transfer",
            SourceRoomId = 9002,
            DestinationRoomId = 9003,
            SourceSegmentId = segment.Id,
            DestinationSegmentId = transferred.Id,
            IdentityKey = segment.IdentityKey,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            BinCount = 97,
            RoomTransferId = 100020,
            OccurredAt = transferAt,
            CreatedAt = transferAt
        });
        if (treated)
        {
            var chemical = await db.TreatmentChemicals.FirstAsync();
            db.RoomTreatmentApplications.Add(new()
            {
                Id = 800000,
                OperationKey = "synthetic-receiving-treatment",
                TreatmentChemicalId = chemical.Id,
                WarehouseId = 9001,
                RoomId = 9002,
                ReceiptId = receipt.Id,
                AppliedByUserId = 8000,
                CreatedByUserId = 8000,
                TotalBinsSnapshot = 32,
                ProductNameSnapshot = chemical.ProductName,
                CropSnapshot = chemical.Crop,
                UnitSnapshot = chemical.Unit,
                CurrencySnapshot = chemical.Currency,
                AppliedAt = at.AddHours(-1),
                CreatedAt = at.AddHours(-1)
            });
            moved.TreatmentState = "Confirmed"; moved.TreatmentSignature = "u|a:800000";
            db.TreatmentLineageSegmentApplications.Add(new() { TreatmentLineageSegmentId = moved.Id, RoomTreatmentApplicationId = 800000, Sequence = 1 });
            var movement = db.ChangeTracker.Entries<TreatmentLineageMovement>().Single(x => x.Entity.Id == 100010).Entity;
            movement.TreatmentStateSnapshot = "Confirmed"; movement.TreatmentSignatureSnapshot = "u|a:800000";
        }
        await db.SaveChangesAsync();
        return f;
    }
}
