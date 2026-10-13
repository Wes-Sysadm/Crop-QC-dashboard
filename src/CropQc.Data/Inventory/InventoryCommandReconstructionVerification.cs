using System.Text.Json;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private sealed record ReconstructionBoundary(string Table, decimal? MaximumId, string[] Keys, string Hash);
    private sealed record ReconstructionCommitEvidence(InventoryPositionEvidence Position,
        ReconstructionBoundary[] Boundaries, string ProtectedHash, string CurrentRowsJson);

    // Explicit membership also handles identity sequences below imported IDs.
    // Neither effective dates nor ID high-water marks establish append order.
    private static async Task<ReconstructionBoundary[]> CaptureReconstructionBoundariesAsync(CropQcDbContext db, CancellationToken ct)
    {
        var result = new List<ReconstructionBoundary>();
        foreach (var table in ReconstructionProtectedTables)
        {
            var filter = table == "AuditLogs" ? " WHERE NOT (\"EntityName\"='ProjectionReconstruction' AND \"SourceApplication\"='CanonicalProjectionReconstruction/v1')" : "";
            var keys = await db.Database.SqlQueryRaw<string>(
                "SELECT coalesce(to_jsonb(t)->>'Id',to_jsonb(t)->>'OperationKey',to_jsonb(t)::text) AS \"Value\" FROM \"" + table + "\" t" + filter).ToArrayAsync(ct);
            var boundary = new ReconstructionBoundary(table, null, keys.Order(StringComparer.Ordinal).ToArray(), "");
            result.Add(boundary with { Hash = await BoundaryHashAsync(db, boundary, ct) });
        }
        return result.ToArray();
    }

    private static Task<string> BoundaryHashAsync(CropQcDbContext db, ReconstructionBoundary boundary, CancellationToken ct)
    {
        var filter = boundary.MaximumId != null ? "WHERE (to_jsonb(t)->>'Id')::numeric <= {0}"
            : "WHERE coalesce(to_jsonb(t)->>'Id',to_jsonb(t)->>'OperationKey',to_jsonb(t)::text) = ANY({0})";
        if (boundary.Table == "AuditLogs") filter += " AND NOT (\"EntityName\"='ProjectionReconstruction' AND \"SourceApplication\"='CanonicalProjectionReconstruction/v1')";
        return TableFingerprintAsync(db, boundary.Table, filter, ct, boundary.MaximumId is decimal maximum ? maximum : boundary.Keys);
    }

    private static void VerifyCommittedReconstruction(ProjectionReconstructionRequest request,
        ProjectionReconstructionResult result, ReconstructionCommitEvidence committed)
    {
        Require(result.Status == "Committed" && result.OperationKey == request.OperationKey && result.AuthoritativeQuantityDelta == 0
            && request.ExplicitlyExecute && request.Preview.Eligible && request.Preview.Snapshot != null,
            "Committed reconstruction contract is incomplete.");
        Require(result.VerificationSeal == RepairHash(JsonSerializer.Serialize(committed, ReconstructionJson)),
            "Immutable committed verification evidence changed or is unavailable; legacy repairs require independent review.");
        var before = request.Preview;
        var e = committed.Position;
        Require(e.Identity.Key == before.Target.Identity.Key && e.IdentityVerified && e.Location.RoomId == before.Target.RoomId
            && e.Location.WarehouseId == before.Target.WarehouseId && e.Location.Custody == InventoryCustody.Room
            && e.AuthoritativeQuantity == before.AuthoritativeQuantity && committed.ProtectedHash == before.ProtectedFingerprint,
            "Committed identity, custody, quantity or conservation evidence is inconsistent.");
        Require(committed.Boundaries.Select(x => x.Table).Order().SequenceEqual(ReconstructionProtectedTables.Order()),
            "Committed protected-history manifest is incomplete.");
        var replacement = e.Projections.SingleOrDefault(x => x.Id == result.ReplacementSegmentId);
        Require(replacement != null && replacement.Disposition == "Current" && replacement.RawKey == e.Identity.Key
            && replacement.ExactIdentity && replacement.Quantity == before.Plan!.ReplacementQuantity
            && replacement.ReceiptId == before.Plan.ReplacementReceiptId && replacement.State == "Untreated"
            && replacement.Signature == "u" && replacement.ApplicationIds.IsEmpty,
            "Committed replacement violates identity, quantity, provenance, disposition or treatment contract.");
        var changed = before.Plan!.Changes.Select(x => x.Id).ToHashSet();
        var preserved = before.Snapshot!.Projections.Where(x => x.Disposition == "Current" && !changed.Contains(x.Id)).ToArray();
        foreach (var p in preserved)
            Require(e.Projections.Any(x => JsonSerializer.Serialize(x, ReconstructionJson) == JsonSerializer.Serialize(p, ReconstructionJson)),
                $"Committed repair changed preserved projection {p.Id}.");
        var expected = preserved.Select(x => x.Id).Append(replacement!.Id).Distinct().Order().ToArray();
        Require(e.Projections.Where(x => x.Disposition == "Current").Select(x => x.Id).Order().SequenceEqual(expected),
            "Committed repair contains an unexpected or missing current projection.");
        Require(e.Projections.Where(x => x.Disposition == "Current" && x.Quantity > 0)
            .All(x => x.State == "Untreated" && x.Signature == "u" && x.ApplicationIds.IsEmpty),
            "Committed treatment state is not proven untreated.");
        var resolved = InventoryAvailabilityResolver.Resolve(e, new());
        Require(resolved.IsOperable && resolved.TreatmentConfidence == InventoryConfidence.Proven
            && resolved.RawProjectionQuantity == resolved.AuthoritativeQuantity,
            "Normal canonical resolver rejects committed reconstruction custody or treatment.");
    }

    private static async Task<ProjectionReconstructionResult> VerifyCurrentReconstructionAsync(CropQcDbContext db,
        ProjectionReconstructionRequest request, ProjectionReconstructionResult result,
        ReconstructionCommitEvidence committed, CancellationToken ct)
    {
        var target = request.Preview.Target;
        var batch = await new InventoryEvidenceLoader(db).LoadAsync(new(target.WarehouseId, [target.RoomId]), DateTimeOffset.UtcNow, ct);
        var current = batch.Positions.SingleOrDefault(x => x.Identity.Key == target.Identity.Key);
        Require(current != null, "Current reconstructed identity is missing.");
        var actualReplacement = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications)
            .SingleOrDefaultAsync(x => x.Id == result.ReplacementSegmentId, ct);
        Require(actualReplacement != null && actualReplacement.RoomId == target.RoomId && actualReplacement.WarehouseId == target.WarehouseId
            && actualReplacement.IdentityKey == target.Identity.Key && actualReplacement.CropYear == target.Identity.CropYear
            && actualReplacement.GrowerLotId == target.Identity.GrowerLotId && actualReplacement.FruitProfileId == target.Identity.FruitProfileId
            && actualReplacement.LotNumberSnapshot == target.Identity.Lot && actualReplacement.VarietyCodeSnapshot == target.Identity.Variety
            && actualReplacement.ProductionTypeSnapshot == target.Identity.ProductionType && actualReplacement.IsOrganicSnapshot == target.Identity.IsOrganic
            && actualReplacement.GrowerNumberSnapshot == target.Identity.GrowerNumber
            && InventoryStatusIdentity.Normalize(actualReplacement.InventoryStatusSnapshot, actualReplacement.ProductionTypeSnapshot)
                == InventoryStatusIdentity.Normalize(target.Identity.Status, target.Identity.ProductionType)
            && actualReplacement.TreatmentState == "Untreated" && actualReplacement.TreatmentSignature == "u"
            && actualReplacement.Applications.Count == 0 && actualReplacement.ReceiptId == request.Preview.Plan!.ReplacementReceiptId,
            "Current replacement identity, provenance, treatment state or application links are invalid.");
        // Projection evidence intentionally contains only resolver fields. Compare
        // the sealed persisted rows as well so display/origin metadata cannot be
        // silently rewritten while quantity and treatment still reconcile.
        using var committedRows = JsonDocument.Parse(committed.CurrentRowsJson);
        var committedIds = committedRows.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
        var persistedRows = await db.TreatmentLineageSegments.AsNoTracking().Where(x => committedIds.Contains(x.Id)).ToArrayAsync(ct);
        string[] mutableLifecycle = ["currentBins", "disposition", "retiredAt", "retiredQuantity", "retiredByCommandKey", "concurrencyVersion", "updatedAt"];
        Require(persistedRows.Length == committedIds.Length, "A committed current projection was deleted.");
        foreach (var row in persistedRows)
        {
            var original = committedRows.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetInt64() == row.Id);
            var actual = JsonSerializer.SerializeToElement(row, ReconstructionJson);
            Require(original.EnumerateObject().Where(x => !mutableLifecycle.Contains(x.Name))
                .All(x => x.Value.GetRawText() == actual.GetProperty(x.Name).GetRawText()),
                $"Committed projection {row.Id} identity or original metadata changed.");
        }
        // Check historical commit validity before considering any later change.
        // Edits to protected rows are never excused by unrelated subsequent activity.
        var changedTables = new List<string>();
        foreach (var boundary in committed.Boundaries)
            if (boundary.Hash != await BoundaryHashAsync(db, boundary, ct)) changedTables.Add(boundary.Table);
        string[] immutableTables = ["RoomInventoryAdjustments", "TreatmentLineageMovements", "AuditLogs", "InventoryCommands"];
        Require(!changedTables.Intersect(immutableTables).Any(),
            "Previously committed immutable ledger, movement, audit or command history changed: "
                + string.Join(", ", changedTables.Intersect(immutableTables)));
        var resolved = InventoryAvailabilityResolver.Resolve(current!, new());
        Require(resolved.IsOperable && resolved.TreatmentConfidence == InventoryConfidence.Proven
            && resolved.RawProjectionQuantity == resolved.AuthoritativeQuantity
            && current!.Projections.Where(x => x.Disposition == "Current" && x.Quantity > 0)
                .All(x => x.State is "Untreated" or "Confirmed"), "Fresh canonical inventory, custody or treatment is invalid.");
        foreach (var kind in new[] { InventoryCustody.InTransit, InventoryCustody.OutsideWarehouse, InventoryCustody.Processor })
        {
            var custody = await new InventoryEvidenceLoader(db).LoadAsync(new(target.WarehouseId, [target.RoomId], kind), DateTimeOffset.UtcNow, ct);
            Require(custody.Positions.Where(x => x.Identity.Key == target.Identity.Key)
                .All(x => InventoryAvailabilityResolver.Resolve(x, new(AllowedCustody: kind)).IsOperable),
                $"Fresh {kind} custody does not reconcile with recorded dispatch allocations.");
        }
        if (await ReconstructionReceiptCustodyAcknowledgmentAsync(db, ct) is long acknowledgmentId)
            return result with
            {
                Status = "CommittedEvidenceVerifiedCurrentReviewRequired",
                Detail = $"Original sealed repair verifies. ReceiptCustodyAcknowledgment {acknowledgmentId} requires the compensation-aware held/placement/reversal proof, which this maintenance verifier does not implement. Current custody allocation is not certified."
            };
        var unchanged = JsonSerializer.Serialize(current!.Projections, ReconstructionJson)
            == JsonSerializer.Serialize(committed.Position.Projections, ReconstructionJson);
        var sameContributions = JsonSerializer.Serialize(new { current.Ledger, current.Movements, current.Receipts, current.Applications }, ReconstructionJson)
            == JsonSerializer.Serialize(new { committed.Position.Ledger, committed.Position.Movements, committed.Position.Receipts, committed.Position.Applications }, ReconstructionJson);
        if (unchanged && sameContributions && current.AuthoritativeQuantity == committed.Position.AuthoritativeQuantity && changedTables.Count == 0)
            return result with { Status = "Verified", Detail = "Committed repair and fresh canonical quantity, custody, treatment, identity and preserved projections verify independently." };

        // Recognize append-only ordinary receiving/movement using recorded command
        // membership and exact per-projection movement deltas. Other mutations need
        // explicit review rather than being called either corruption or verified.
        var oldMoves = committed.Position.Movements.Select(x => x.Id).ToHashSet();
        var later = current.Movements.Where(x => !oldMoves.Contains(x.Id)).ToArray();
        var repairTime = await db.InventoryCommands.Where(x => x.OperationKey == request.OperationKey).Select(x => x.CommittedAt).SingleAsync(ct);
        var laterCommands = await db.InventoryCommands.AsNoTracking().Where(x => x.CommittedAt > repairTime).ToArrayAsync(ct);
        foreach (var operation in laterCommands)
        {
            if (operation.IntentHash != RepairHash(operation.IntentJson)) continue;
            using var payload = JsonDocument.Parse(operation.IntentJson);
            if (!payload.RootElement.TryGetProperty("kind", out _)) continue;
            var intent = JsonSerializer.Deserialize<InventoryCommand>(operation.IntentJson, ReconstructionJson)!;
            if (!InventoryCommandPolicy.IsTreatment(intent.Kind) || !intent.Lines.Any(x => x.Source.Identity.Key == target.Identity.Key
                && x.Source.Location.RoomId == target.RoomId && x.Source.Location.WarehouseId == target.WarehouseId)) continue;
            var effect = JsonSerializer.Deserialize<InventoryCommandResult>(operation.ResultJson, ReconstructionJson)!;
            if (effect.Status == InventoryCommandStatus.Committed && effect.OperationKey == operation.OperationKey
                && await db.RoomTreatmentApplications.AnyAsync(x => x.OperationKey.StartsWith(operation.OperationKey + ":")
                    && x.CreatedAt == operation.CommittedAt && x.RoomId == target.RoomId, ct))
                return result with
                {
                    Status = "CommittedEvidenceVerifiedCurrentReviewRequired",
                    Detail = "Original sealed repair verifies. A later recorded point-in-time treatment legitimately superseded its current projections; fresh treatment is operable but requires treatment-specific allocation verification."
                };
        }
        Require(unchanged || later.Length > 0, "Current projection quantity, disposition or metadata changed without recorded movement evidence.");
        var coverage = await ReconstructionCommandEvidence.ValidateAsync(db, current, ct);
        var oldLedger = committed.Position.Ledger.Select(x => x.Id).ToHashSet();
        var ledgerCovered = current.Ledger.Where(x => !oldLedger.Contains(x.Id)).All(x => coverage.LedgerIds.Contains(x.Id));
        var projectionsAgree = current.Projections.Where(x => x.Disposition == "Current").All(p =>
        {
            var old = committed.Position.Projections.SingleOrDefault(x => x.Id == p.Id);
            var delta = later.Where(x => x.DestinationProjectionId == p.Id).Sum(x => x.Quantity)
                - later.Where(x => x.SourceProjectionId == p.Id).Sum(x => x.Quantity);
            return p.ExactIdentity && p.RawKey == current.Identity.Key && p.Quantity == (old?.Quantity ?? 0) + delta
                && (old != null ? p.State == old.State && p.Signature == old.Signature && p.ReceiptId == old.ReceiptId
                    && p.ApplicationIds.SequenceEqual(old.ApplicationIds) : later.Any(x => x.DestinationProjectionId == p.Id)
                    && later.Where(x => x.DestinationProjectionId == p.Id).All(x => x.State == p.State && x.Signature == p.Signature && x.ReceiptId == p.ReceiptId));
        });
        var oldCurrentRemain = committed.Position.Projections.Where(x => x.Disposition == "Current")
            .All(p => current.Projections.Any(x => x.Id == p.Id && x.Disposition == "Current"));
        if (changedTables.Count == 0 && ledgerCovered && later.Length > 0 && later.All(x => x.ExactIdentity && coverage.MovementIds.Contains(x.Id))
            && projectionsAgree && oldCurrentRemain && actualReplacement!.Disposition == "Current")
            return result with { Status = "VerifiedWithLaterActivity", Detail = "Original sealed repair verifies; later canonical recorded allocations reconcile fresh inventory without changing committed history." };
        Require(actualReplacement!.Disposition == "Current" || later.Length > 0,
            "Replacement disposition changed without subsequent movement evidence.");
        return result with
        {
            Status = "CommittedEvidenceVerifiedCurrentReviewRequired",
            Detail = "Original sealed repair verifies. Later current state is operable but cannot be independently certified by the append-only allocation proof. Review changed protected tables: " + string.Join(", ", changedTables)
        };
    }
}
