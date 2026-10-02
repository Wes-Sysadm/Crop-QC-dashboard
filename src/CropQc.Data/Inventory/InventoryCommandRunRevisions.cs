using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> ReviseLegacyDumpAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.PhysicalParentId > 0 && c.Lines.Length == 1, "Legacy run correction needs one original entry and replacement allocation.");
        var line = c.Lines.Single();
        var loader = new InventoryEvidenceLoader(db);
        var batch = await new InventoryAvailabilityResolver(loader).ResolveAsync(new(line.Source.Location.WarehouseId, [line.Source.Location.RoomId!.Value]), new(), now, ct);
        var preview = await new InventoryRunCorrectionAvailability(db).ReadLegacyAsync(batch, c.PhysicalParentId!.Value, ct);
        var before = preview.Values.SingleOrDefault(x => x.Current.Identity.Key == line.Source.Identity.Key);
        Require(before != null && before.Current.Watermark.Fingerprint == line.Source.ExpectedFingerprint && before.Blocker == null,
            "Inventory changed; reload before correcting this run.", InventoryCommandStatus.Stale);
        var original = await db.BinsRunEntries.Include(x => x.InventoryAdjustment).SingleAsync(x => x.Id == c.PhysicalParentId, ct);
        Require(original.InventoryAdjustment.RoomDepletionId == null, "Use receipt depletion reversal before recording a replacement depletion.");
        var restored = await ReverseRunAsync(db, factory, c with { Kind = InventoryCommandKind.ReverseRunEntry, Lines = [] }, now, attempt, ct);
        var evidence = (await loader.LoadAsync(new(line.Source.Location.WarehouseId, [line.Source.Location.RoomId!.Value]), now, ct)).Positions
            .Single(x => x.Identity.Key == line.Source.Identity.Key);
        var refreshed = line with { Source = line.Source with { ExpectedFingerprint = evidence.Watermark.Fingerprint, ExpectedVersions = evidence.Watermark.Versions } };
        var resolved = InventoryAvailabilityResolver.Resolve(evidence, InventoryCommandPolicy.Requirements(InventoryCommandKind.LegacyDump, refreshed));
        Require(resolved.IsOperable && resolved.AvailableQuantity >= line.Quantity, "Exact restoration and current inventory cannot cover the replacement run.");
        await NormalizePositionAsync(db, factory, c, evidence, resolved, now, attempt, ct);
        var consumed = await ApplyAsync(db, factory, c with { Kind = InventoryCommandKind.LegacyDump }, [(refreshed, evidence, resolved)], now, attempt, ct);
        return restored.AddRange(consumed);
    }

    private async Task<ImmutableArray<InventoryCommandEffect>> ReviseRunAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.PhysicalParentId > 0 && c.Run != null && c.ExpectedParentVersion != null && c.Lines.Length > 0,
            "Run correction needs the current revision, facility and replacement allocations.");
        var loader = new InventoryEvidenceLoader(db);
        // Validate the user's pre-restoration view before changing anything. Restoration
        // credit is proved from this run's immutable consumption, never from the caller.
        var current = await new InventoryAvailabilityResolver(loader).ResolveAsync(new(null, c.Lines.Select(x => x.Source.Location.RoomId!.Value).Distinct().ToImmutableArray()), new(), now, ct);
        var preview = await new InventoryRunCorrectionAvailability(db).ReadAsync(current, c.PhysicalParentId!.Value, ct);
        foreach (var line in c.Lines)
        {
            var before = preview.Values.SingleOrDefault(x => x.Current.Identity.Key == line.Source.Identity.Key
                && x.Current.Location.RoomId == line.Source.Location.RoomId && x.Current.Location.WarehouseId == line.Source.Location.WarehouseId);
            Require(before != null && before.Current.Watermark.Fingerprint == line.Source.ExpectedFingerprint,
                "Inventory changed; reload the run correction.", InventoryCommandStatus.Stale);
            Require(before!.Blocker == null, "Current inventory is not proven for this run correction.");
        }
        var restored = await ReverseRunAsync(db, factory, c with { Lines = [] }, now, attempt, ct);
        var inputs = new List<(InventoryCommandLine Line, InventoryPositionEvidence Evidence, InventoryAvailabilityResult Result)>();
        foreach (var line in c.Lines)
        {
            var e = (await loader.LoadAsync(new(line.Source.Location.WarehouseId, [line.Source.Location.RoomId!.Value]), now, ct)).Positions
                .Single(x => x.Identity.Key == line.Source.Identity.Key);
            var refreshed = line with { Source = line.Source with { ExpectedFingerprint = e.Watermark.Fingerprint, ExpectedVersions = e.Watermark.Versions } };
            var r = InventoryAvailabilityResolver.Resolve(e, InventoryCommandPolicy.Requirements(InventoryCommandKind.Dump, refreshed));
            Require(r.IsOperable && r.AvailableQuantity >= line.Quantity, "Current stock plus exact same-run restoration cannot cover the correction.");
            Require(!inputs.Any(x => x.Result.PositionKey == r.PositionKey && x.Line.TreatmentSignature == line.TreatmentSignature), "Duplicate corrected run slice.");
            inputs.Add((refreshed, e, r));
            await NormalizePositionAsync(db, factory, c, e, r, now, attempt, ct);
        }
        var run = await db.ActualRuns.SingleAsync(x => x.Id == c.PhysicalParentId, ct);
        var revision = await db.ActualRunRevisions.SingleAsync(x => x.ActualRunId == run.Id && x.IsCurrent, ct);
        var consumed = await ApplyAsync(db, factory, c with { Kind = InventoryCommandKind.Dump }, inputs, now, attempt, ct, run, revision);
        return restored.AddRange(consumed);
    }

    private async Task<ImmutableArray<InventoryCommandEffect>> ReverseRunAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.PhysicalParentId > 0, "Run restoration requires an exact original parent.", InventoryCommandStatus.InvalidIntent);
        ActualRun? run = null;
        ActualRunRevision? revision = null;
        List<BinsRunEntry> entries;
        if (c.Kind is InventoryCommandKind.CancelRun or InventoryCommandKind.ReviseRun)
        {
            run = await db.ActualRuns.Include(x => x.Revisions).SingleOrDefaultAsync(x => x.Id == c.PhysicalParentId, ct);
            Require(run != null && run.Status == ActualRunStatuses.Active && run.ConcurrencyVersion == c.ExpectedParentVersion,
                "Actual Run is missing, canceled or changed; reload before canceling.", InventoryCommandStatus.Stale);
            entries = await db.BinsRunEntries.Include(x => x.InventoryAdjustment).Where(x => x.ActualRunId == run!.Id
                && x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed).OrderBy(x => x.Id).ToListAsync(ct);
            Require(entries.Count > 0 && entries.All(x => x.ActualRunRevisionId == run!.Revisions.Single(r => r.IsCurrent).Id),
                "Only the exact current run revision can be restored.");
            revision = new()
            {
                ActualRun = run!,
                RevisionNumber = run!.CurrentRevisionNumber + 1,
                OperationType = c.Kind == InventoryCommandKind.CancelRun ? ActualRunRevisionTypes.Cancel : ActualRunRevisionTypes.Edit,
                OperationKey = c.OperationKey,
                IsCurrent = true,
                Reason = c.Reason,
                CreatedByUserId = c.ActorId,
                CreatedAt = now
            };
            foreach (var old in run.Revisions) old.IsCurrent = false;
            db.ActualRunRevisions.Add(revision);
            if (c.Kind == InventoryCommandKind.CancelRun)
            { run.Status = ActualRunStatuses.Canceled; run.CancellationReason = c.Reason; run.CanceledByUserId = c.ActorId; run.CanceledAt = now; }
            run.UpdatedByUserId = c.ActorId; run.UpdatedAt = now; run.CurrentRevisionNumber = revision.RevisionNumber; run.ConcurrencyVersion++;
        }
        else
        {
            entries = await db.BinsRunEntries.Include(x => x.InventoryAdjustment).Where(x => c.Kind == InventoryCommandKind.ReverseDepletion
                ? x.InventoryAdjustment.RoomDepletionId == c.PhysicalParentId && x.InventoryAdjustment.ChangeAmount < 0 : x.Id == c.PhysicalParentId).ToListAsync(ct);
            Require(entries.Count == 1 && entries[0].ActualRunId == null && entries[0].TransactionType == ActualRunTransactionTypes.Legacy,
                "Use Actual Run cancellation for revisioned entries.");
        }
        Require(entries.All(x => !x.IsReversed && !x.IsReconciled && x.ReversesBinsRunEntryId == null),
            "A run entry is reversed or locked by finalized packout reconciliation.");
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        foreach (var entry in entries)
        {
            Require(!await db.BinsRunEntries.AnyAsync(x => x.ReversesBinsRunEntryId == entry.Id, ct), "Run entry was already restored.");
            var moves = await db.TreatmentLineageMovements.Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
                .Where(x => x.BinsRunEntryId == entry.Id).ToListAsync(ct);
            var restorations = await RestoreConsumptionAsync(db, factory, c, entry.InventoryAdjustment, moves, entry.BinsRun, now, attempt, ct);
            RoomDepletion? depletion = null;
            if (entry.InventoryAdjustment.RoomDepletionId is long depletionId)
            {
                depletion = await db.RoomDepletions.SingleAsync(x => x.Id == depletionId, ct);
                Require(!depletion.IsVoided && depletion.BinCountDepleted == entry.BinsRun, "Depletion is voided or differs from its original consumption.");
                depletion.IsVoided = true; depletion.VoidedAt = now; depletion.VoidedByUserId = c.ActorId; depletion.VoidReason = c.Reason;
            }
            foreach (var restored in restorations)
            {
                restored.Ledger.AdjustmentType = depletion == null ? "BinsRunReversal" : "DepletionVoid";
                restored.Ledger.RoomDepletion = depletion; restored.Ledger.ActualRun = run; restored.Ledger.ActualRunRevision = revision;
                var reversal = new BinsRunEntry
                {
                    ReceiptId = entry.ReceiptId,
                    SourceInventoryAdjustmentId = entry.SourceInventoryAdjustmentId,
                    InventoryAdjustment = restored.Ledger,
                    WarehouseId = entry.WarehouseId,
                    RoomId = entry.RoomId,
                    CropYear = restored.Identity.CropYear!.Value,
                    GrowerLotId = restored.Identity.GrowerLotId!.Value,
                    FruitProfileId = restored.Identity.FruitProfileId!.Value,
                    GrowerName = await db.GrowerLots.Where(x => x.Id == restored.Identity.GrowerLotId).Select(x => x.Grower).SingleAsync(ct),
                    LotNumber = restored.Identity.Lot,
                    PoolStart = entry.PoolStart,
                    VarietyCode = restored.Identity.Variety,
                    InventoryStatus = restored.Identity.Status,
                    PreviousAvailableBins = restored.Effect.Before,
                    BinsRun = restored.Effect.Quantity,
                    NewAvailableBins = restored.Effect.After,
                    Notes = c.Reason,
                    RunAt = now,
                    CreatedAt = now,
                    CreatedByUserId = c.ActorId,
                    ActualRun = run,
                    ActualRunRevision = revision,
                    TransactionType = ActualRunTransactionTypes.Reversal,
                    ReversesBinsRunEntry = entry,
                    ReportingFacilityWarehouseId = entry.ReportingFacilityWarehouseId,
                    ReportingFacilityCodeSnapshot = entry.ReportingFacilityCodeSnapshot,
                    ReportingFacilityAssignmentSource = entry.ReportingFacilityAssignmentSource,
                    ReportingFacilityAssignedByUserId = entry.ReportingFacilityAssignedByUserId,
                    ReportingFacilityAssignedAt = entry.ReportingFacilityAssignedAt,
                    ProductionTypeSnapshot = entry.ProductionTypeSnapshot,
                    IsOrganicSnapshot = entry.IsOrganicSnapshot,
                    GrowerNumberSnapshot = entry.GrowerNumberSnapshot,
                    ReportingCropYearSnapshot = entry.ReportingCropYearSnapshot,
                    ReportingFruitProfileIdSnapshot = entry.ReportingFruitProfileIdSnapshot,
                    ReportingVarietyCodeSnapshot = entry.ReportingVarietyCodeSnapshot,
                    TreatmentStateSnapshot = entry.TreatmentStateSnapshot,
                    TreatmentSignatureSnapshot = entry.TreatmentSignatureSnapshot,
                    TreatmentSummarySnapshot = entry.TreatmentSummarySnapshot
                };
                db.BinsRunEntries.Add(reversal);
                foreach (var move in restored.Movements) { move.BinsRunEntry = reversal; move.MovementType = "BinsRunReversal"; }
                entry.IsReversed = true; entry.ReversedAt = now; entry.ReversedByUserId = c.ActorId; entry.ReverseReason = c.Reason; entry.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(entry.WarehouseId, [entry.RoomId]), new(), now, ct))
                    .Positions.Single(x => x.PositionKey == restored.Effect.PositionKey);
                Require(after.IsOperable && after.AuthoritativeQuantity == restored.Effect.After && after.RawProjectionQuantity == after.AuthoritativeQuantity,
                    "Run restoration does not reconcile authoritative quantity and current treatment.");
                effects.Add(restored.Effect with { ParentId = run?.Id ?? entry.Id, LedgerIds = [restored.Ledger.Id], MovementIds = restored.Movements.Select(x => x.Id).ToImmutableArray() });
            }
        }
        return effects.ToImmutable();
    }
}
