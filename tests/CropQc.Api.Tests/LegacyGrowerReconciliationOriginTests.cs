using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class LegacyGrowerReconciliationOriginTests
{
    [InventoryPostgresFact]
    public async Task Complete_reconciliation_validates_its_source_debit_and_credit_when_corrected_room_is_active()
    {
        await using var f = await LegacyReceiptReclassificationOriginTests.Seed();
        await Seed(f);
        await using var db = f.CreateDbContext();
        var history = await LegacyReceiptReclassificationOriginTests.HistoricalRows(db);
        var correction = await db.InventoryIdentityCorrections.SingleAsync();
        var prior = await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9003 && x.CreatedAt < correction.CreatedAt).ToArrayAsync();
        Assert.Equal(158, prior.Where(x => x.GrowerLotId == null).Sum(x => x.ChangeAmount));
        Assert.Equal(-158, prior.Where(x => x.GrowerLotId == 100000).Sum(x => x.ChangeAmount));
        Assert.Equal(0, prior.Sum(x => x.ChangeAmount));
        Assert.Equal(-280, Assert.Single(prior, x => x.Id == 400010).ChangeAmount);
        Assert.Null(await LegacyReceiptReclassificationOriginTests.Origin(db));

        // A normal movement makes the formerly empty corrected room an active
        // source. Its credit must recursively validate the original account debit.
        var arrival = await Move(9002, 9003, 10);
        Assert.True(arrival.Status == InventoryCommandStatus.Committed, arrival.Detail);
        Assert.Equal(400, await f.Physical());
        Assert.Equal(10, await f.Physical(9003));
        var evidence = await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9003]), DateTimeOffset.UtcNow, default);
        var active = Assert.Single(evidence.Positions, x => x.AuthoritativeQuantity > 0);
        Assert.Contains(active.Ledger, x => x.Id == 400013 && x.Quantity == 158);
        var beforeValidation = await f.Snapshot();
        Assert.Null(await LegacyReceiptReclassificationOriginTests.Origin(db, 9003));
        Assert.Equal(beforeValidation, await f.Snapshot());

        var source = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9003]), new(), DateTimeOffset.UtcNow)).Positions, x => x.AuthoritativeQuantity > 0);
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.RoomMove, 8000,
            DateTimeOffset.UtcNow, "Synthetic movement after reconciliation",
            [new(new(source.Identity, source.Location, source.Watermark.Fingerprint, source.Watermark.Versions), 7, "u", new(9001, 9002))]);
        var moved = await f.Execute(command);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        Assert.Equal(407, await f.Physical());
        Assert.Equal(3, await f.Physical(9003));
        Assert.Null(await LegacyReceiptReclassificationOriginTests.Origin(db, 9003));
        Assert.Null(await LegacyReceiptReclassificationOriginTests.Origin(db));
        var beforeRejected = await f.Snapshot();
        Assert.NotEqual(InventoryCommandStatus.Committed, (await Move(9003, 9002, 4)).Status);
        Assert.Equal(beforeRejected, await f.Snapshot());
        Assert.Equal(history, await LegacyReceiptReclassificationOriginTests.HistoricalRows(db));
        Assert.Equal(410, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        Assert.Equal(410, await db.TreatmentLineageSegments.SumAsync(x => x.CurrentBins));
        Assert.All(await db.TreatmentLineageMovements.ToArrayAsync(), x => Assert.Equal("u", x.TreatmentSignatureSnapshot));

        async Task<InventoryCommandResult> Move(int from, int to, int bins)
        {
            var position = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
                .ResolveAsync(new(9001, [from]), new(), DateTimeOffset.UtcNow)).Positions,
                x => x.Identity.GrowerLotId == 100000 && x.Identity.FruitProfileId == 9004 && x.AuthoritativeQuantity > 0);
            return await f.Execute(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.RoomMove, 8000,
                DateTimeOffset.UtcNow, "Synthetic corrected-room arrival or withdrawal",
                [new(new(position.Identity, position.Location, position.Watermark.Fingerprint, position.Watermark.Versions), bins, "u", new(9001, to))]));
        }
    }

    [InventoryPostgresFact]
    public async Task Recorded_mapping_proves_earlier_shared_pool_from_original_arrivals_without_backdating()
    {
        await using var f = await LegacyReceiptReclassificationOriginTests.Seed();
        await Seed(f);
        await using var db = f.CreateDbContext();
        var before = await f.Snapshot();
        Assert.Null(await LegacyReceiptReclassificationOriginTests.Origin(db));
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("audit-missing")]
    [InlineData("audit-duplicate")]
    [InlineData("audit-contradictory")]
    [InlineData("actor")]
    [InlineData("incomplete")]
    [InlineData("inactive")]
    [InlineData("duplicate-parent")]
    [InlineData("missing-debit")]
    [InlineData("excess-credit")]
    [InlineData("wrong-position")]
    [InlineData("wrong-profile")]
    [InlineData("wrong-lot")]
    [InlineData("wrong-key")]
    [InlineData("missing-positive-id")]
    [InlineData("missing-origin")]
    [InlineData("future-arrival")]
    [InlineData("unreviewed-later-arrival")]
    [InlineData("snapshot")]
    [InlineData("unsupported-treatment-movements")]
    [InlineData("unexpected-treatment-movement")]
    public async Task Incomplete_mapping_or_truly_unfunded_history_stays_blocked(string corruption)
    {
        await using var f = await LegacyReceiptReclassificationOriginTests.Seed();
        await Seed(f);
        await using var db = f.CreateDbContext();
        var parent = await db.InventoryIdentityCorrections.SingleAsync();
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityName == nameof(InventoryIdentityCorrection));
        var debit = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 400012);
        var credit = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 400013);
        switch (corruption)
        {
            case "audit-missing": db.AuditLogs.Remove(audit); break;
            case "audit-duplicate": var extraAudit = (AuditLog)db.Entry(audit).CurrentValues.ToObject(); extraAudit.Id = 0; db.AuditLogs.Add(extraAudit); break;
            case "audit-contradictory": audit.AfterValuesJson = "{}"; break;
            case "actor": audit.UserId = null; break;
            case "incomplete": parent.IsComplete = false; break;
            case "inactive": parent.IsActive = false; break;
            case "duplicate-parent": var extraParent = (InventoryIdentityCorrection)db.Entry(parent).CurrentValues.ToObject(); extraParent.Id = Guid.NewGuid(); extraParent.OperationKey += "duplicate"; db.InventoryIdentityCorrections.Add(extraParent); break;
            case "missing-debit": db.RoomInventoryAdjustments.Remove(debit); break;
            case "excess-credit": credit.ChangeAmount++; break;
            case "wrong-position": debit.RoomId = 9002; break;
            case "wrong-profile": parent.TargetFruitProfileId = 9005; break;
            case "wrong-lot": parent.SourceIdentitySnapshotJson = parent.SourceIdentitySnapshotJson.Replace("1372", "2350"); break;
            case "wrong-key": debit.InventoryOperationKey += "wrong"; break;
            case "missing-positive-id": parent.SourceIdentitySnapshotJson = parent.SourceIdentitySnapshotJson.Replace("400001,", ""); audit.BeforeValuesJson = parent.SourceIdentitySnapshotJson; break;
            case "missing-origin": db.RoomInventoryAdjustments.Remove(await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 400001)); break;
            case "future-arrival":
                // Current balances still reconcile, but the real arrival is later
                // than the withdrawal. A later mapping must never cure that.
                (await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 400001)).CreatedAt = InventoryEvidenceCorpus.Start.AddMinutes(4); break;
            case "unreviewed-later-arrival":
                var lateReceipt = (Receipt)db.Entry(await db.Receipts.SingleAsync(x => x.Id == 300001)).CurrentValues.ToObject();
                lateReceipt.Id = 300003; lateReceipt.CompuTechReceiptId = "Synthetic later missing key"; lateReceipt.BinCount = 10;
                lateReceipt.ReceivedAt = InventoryEvidenceCorpus.Start.AddMinutes(7); db.Receipts.Add(lateReceipt);
                var lateOrigin = (RoomInventoryAdjustment)db.Entry(await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 400001)).CurrentValues.ToObject();
                lateOrigin.Id = 400020; lateOrigin.ReceiptId = lateReceipt.Id; lateOrigin.ChangeAmount = 10; lateOrigin.NewBinCount = 10;
                lateOrigin.CreatedAt = lateOrigin.AdjustmentAt = lateReceipt.ReceivedAt; db.RoomInventoryAdjustments.Add(lateOrigin);
                var lateTransfer = (RoomTransfer)db.Entry(await db.RoomTransfers.SingleAsync()).CurrentValues.ToObject();
                lateTransfer.Id = 300001; lateTransfer.OperationKey = Guid.NewGuid().ToString("N"); lateTransfer.BinCount = 10;
                lateTransfer.CreatedAt = lateTransfer.TransferredAt = InventoryEvidenceCorpus.Start.AddMinutes(8); db.RoomTransfers.Add(lateTransfer);
                foreach (var originalId in new[] { 400010, 400011 })
                {
                    var lateLeg = (RoomInventoryAdjustment)db.Entry(await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == originalId)).CurrentValues.ToObject();
                    lateLeg.Id = originalId + 11; lateLeg.RoomTransferId = lateTransfer.Id; lateLeg.ChangeAmount = originalId == 400010 ? -10 : 10;
                    lateLeg.CreatedAt = lateLeg.AdjustmentAt = lateTransfer.CreatedAt; db.RoomInventoryAdjustments.Add(lateLeg);
                }
                break;
            case "snapshot": parent.SourceIdentitySnapshotJson = "{"; break;
            case "unsupported-treatment-movements": parent.ExpectedTreatmentMovementCount = 1; break;
            case "unexpected-treatment-movement":
                db.TreatmentLineageMovements.Add(new()
                {
                    OperationKey = "synthetic-unexpected",
                    MovementType = "InventoryIdentityCorrection",
                    IdentityKey = "synthetic",
                    TreatmentStateSnapshot = "Untreated",
                    TreatmentSignatureSnapshot = "u",
                    InventoryIdentityCorrectionId = parent.Id,
                    BinCount = 1,
                    CreatedAt = parent.CreatedAt,
                    OccurredAt = parent.CreatedAt
                }); break;
        }
        await db.SaveChangesAsync();
        var before = await f.Snapshot();
        Assert.NotNull(await LegacyReceiptReclassificationOriginTests.Origin(db));
        Assert.Equal(before, await f.Snapshot());
    }

    internal static async Task Seed(Fixture f)
    {
        await using var db = f.CreateDbContext();
        var at = InventoryEvidenceCorpus.Start;
        var modelReceipt = await db.Receipts.SingleAsync(x => x.Id == 100000);
        var modelOrigin = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
        foreach (var (id, bins, grower) in new[] { (300000, 122, (int?)100000), (300001, 158, (int?)null), (300002, 66, (int?)null) })
        {
            var receipt = (Receipt)db.Entry(modelReceipt).CurrentValues.ToObject();
            receipt.Id = id; receipt.CompuTechReceiptId = $"Synthetic key {id}"; receipt.RoomId = 9003;
            receipt.GrowerLotId = grower; receipt.BinCount = bins; receipt.ConcurrencyVersion = id == 300002 ? 1 : 0;
            receipt.IsDeleted = id == 300002; db.Receipts.Add(receipt);
            var origin = (RoomInventoryAdjustment)db.Entry(modelOrigin).CurrentValues.ToObject();
            origin.Id = id + 100000; origin.ReceiptId = id; origin.RoomId = 9003; origin.GrowerLotId = grower;
            origin.FruitProfileId = 9004; origin.VarietyCode = "BART"; origin.InventoryStatus = "Conventional";
            origin.ChangeAmount = bins; origin.NewBinCount = bins; db.RoomInventoryAdjustments.Add(origin);
        }
        var voidParent = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            ReceiptId = 300002,
            AdministratorUserId = 8000,
            ActionType = ReceiptInventoryOverrideActionTypes.VoidReceipt,
            OldReceiptBinCount = 66,
            NewReceiptBinCount = 0,
            InventoryDelta = -66,
            CurrentInventoryBefore = 66,
            CurrentInventoryAfter = 0,
            Reason = "Synthetic duplicate",
            OperationKey = Guid.NewGuid().ToString(),
            CreatedAt = at.AddMinutes(1),
            IsComplete = true,
            ExpectedAdjustmentCount = 1,
            BeforeReceiptSnapshotJson = "{\"id\":300002,\"cropYear\":2026,\"growerLotId\":null,\"fruitProfileId\":9004,\"binCount\":66}",
            AfterReceiptSnapshotJson = "{\"isDeleted\":true}",
            AffectedInventorySnapshotJson = "[]"
        };
        db.ReceiptInventoryOverrides.Add(voidParent);
        var voidLeg = Leg(400003, -66, null, at.AddMinutes(1));
        voidLeg.AdjustmentType = "ReceiptAdminOverride"; voidLeg.ReceiptId = 300002; voidLeg.ReceiptInventoryOverrideId = voidParent.Id;
        db.RoomInventoryAdjustments.Add(voidLeg);
        var transfer = new RoomTransfer
        {
            Id = 300000,
            OperationKey = Guid.NewGuid().ToString("N"),
            SourceWarehouseId = 9001,
            SourceRoomId = 9003,
            DestinationWarehouseId = 9001,
            DestinationRoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Synthetic Top Pear",
            LotNumber = "1372",
            BinCount = 280,
            Reason = "Synthetic custody",
            CreatedAt = at.AddMinutes(3),
            TransferredAt = at.AddMinutes(3),
            CreatedByUserId = 8000
        };
        db.RoomTransfers.Add(transfer);
        var outgoing = Leg(400010, -280, 100000, transfer.CreatedAt); outgoing.AdjustmentType = "TransferOut"; outgoing.RoomTransferId = transfer.Id;
        var incoming = Leg(400011, 280, 100000, transfer.CreatedAt); incoming.AdjustmentType = "TransferIn"; incoming.RoomTransferId = transfer.Id; incoming.RoomId = 9002;
        db.RoomInventoryAdjustments.AddRange(outgoing, incoming);
        var parent = new InventoryIdentityCorrection
        {
            Id = Guid.NewGuid(),
            OperationKey = Guid.NewGuid().ToString("N"),
            SourceCropYear = 2026,
            TargetCropYear = 2026,
            SourceFruitProfileId = 9004,
            TargetFruitProfileId = 9004,
            SourceGrowerLotId = null,
            TargetGrowerLotId = 100000,
            CreatedAt = at.AddMinutes(5),
            CreatedByUserId = 8000,
            IsActive = true,
            IsComplete = true,
            ExpectedAdjustmentCount = 2,
            Reason = "Synthetic reviewed missing key",
            SourceIdentitySnapshotJson = JsonSerializer.Serialize(new
            { WarehouseId = 9001, RoomId = 9003, CropYear = 2026, GrowerLotId = (int?)null, GrowerNumber = "1372", Lot = "1372", FruitProfileId = 9004, IsOrganic = false, CurrentBins = 158, StateToken = "synthetic", PositiveSourceAdjustmentIds = new long[] { 400001, 400002 } }),
            TargetIdentitySnapshotJson = JsonSerializer.Serialize(new
            { WarehouseId = 9001, RoomId = 9003, CropYear = 2026, GrowerLotId = 100000, GrowerNumber = "1372", FruitProfileId = 9004, IsOrganic = false, ExistingTargetBins = -158, CurrentBins = 0 })
        };
        db.InventoryIdentityCorrections.Add(parent);
        var debit = Leg(400012, -158, null, parent.CreatedAt); debit.OldBinCount = 158; debit.NewBinCount = 0;
        var credit = Leg(400013, 158, 100000, parent.CreatedAt); credit.OldBinCount = -158; credit.NewBinCount = 0;
        var index = 0;
        foreach (var leg in new[] { debit, credit })
        {
            leg.InventoryIdentityCorrectionId = parent.Id; leg.AdjustmentType = "InventoryIdentityCorrection";
            leg.InventoryOperationKey = $"identity-correction:{parent.OperationKey}:{++index}"; leg.Reason = parent.Reason;
            db.RoomInventoryAdjustments.Add(leg);
        }
        db.AuditLogs.Add(new()
        {
            EntityName = nameof(InventoryIdentityCorrection),
            EntityKey = parent.Id.ToString(),
            Action = "LegacyGrowerLotReconciliation",
            SourceApplication = "CropQc.Web reviewed legacy Grower Lot reconciliation",
            CreatedAt = parent.CreatedAt,
            UserId = 8000,
            BeforeValuesJson = parent.SourceIdentitySnapshotJson,
            AfterValuesJson = parent.TargetIdentitySnapshotJson
        });
        (await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000)).CurrentBins += 280;
        await db.SaveChangesAsync();

        RoomInventoryAdjustment Leg(long id, int amount, int? grower, DateTimeOffset created) => new()
        {
            Id = id,
            WarehouseId = 9001,
            RoomId = 9003,
            CropYear = 2026,
            GrowerLotId = grower,
            FruitProfileId = 9004,
            AdjustmentType = "Synthetic",
            GrowerName = "Synthetic Top Pear",
            LotNumber = "1372",
            VarietyCode = "BART",
            InventoryStatus = "Conventional",
            ChangeAmount = amount,
            CreatedAt = created,
            AdjustmentAt = created,
            CreatedByUserId = 8000,
            Reason = "Synthetic"
        };
    }
}
