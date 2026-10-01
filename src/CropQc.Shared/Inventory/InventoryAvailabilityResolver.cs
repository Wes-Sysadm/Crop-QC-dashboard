using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

/// <summary>Pure evidence projection. Candidates describe later work; they are not executable repair plans.</summary>
public sealed class InventoryAvailabilityResolver(IInventoryEvidenceLoader loader) : IInventoryAvailability
{
    public const string Algorithm = "canonical-inventory-shadow/v1";

    public async Task<InventoryAvailabilityBatch> ResolveAsync(InventoryScope scope,
        InventoryOperationRequirements requirements, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var evidence = await loader.LoadAsync(scope, asOf, cancellationToken);
        return new(asOf, Algorithm, evidence.RowsLoaded,
            evidence.Positions.Select(x => Resolve(x, requirements)).ToImmutableArray());
    }

    public static InventoryAvailabilityResult Resolve(InventoryPositionEvidence e, InventoryOperationRequirements requirements)
    {
        var retired = e.Projections.Where(x => x.Disposition == "Historical").ToArray();
        e = e with { Projections = e.Projections.Where(x => x.Disposition != "Historical").ToImmutableArray() };
        var blockers = new List<InventoryBlocker>();
        var slices = new List<InventoryTreatmentSlice>();
        var history = new List<HistoricalInventoryProjection>();
        var candidates = new List<InventoryNormalizationCandidate>();
        history.AddRange(retired.Select(x => new HistoricalInventoryProjection([x.Id], x.RetiredQuantity ?? 0,
            ProjectionExclusionReason.StaleHistoricalPool, true, [Ref("TreatmentLineageSegment", x.Id)])));
        var references = e.Ledger.Select(x => Ref("RoomInventoryAdjustment", x.Id))
            .Concat(e.Movements.Select(x => Ref("TreatmentLineageMovement", x.Id)))
            .Concat(e.Projections.Select(x => Ref("TreatmentLineageSegment", x.Id)))
            .Concat(e.Applications.Select(x => Ref("RoomTreatmentApplication", x.Id)))
            .Concat(e.Receipts.Select(x => Ref("Receipt", x.Id)))
            .Concat(e.IdentityCorrections.IsDefault ? [] : e.IdentityCorrections).Distinct().ToImmutableArray();
        if (e.AuthoritativeQuantity < 0) Block(InventoryBlockerCode.NegativeAuthoritativeBalance, "Legacy authoritative ledger is negative; it has not been clamped or repaired.");
        if (!e.IdentityVerified || !e.Identity.IsComplete) Block(InventoryBlockerCode.ConflictingIdentity, "Immutable inventory identity is incomplete or conflicting.");
        if (!e.CustodyVerified || e.Location.Custody != requirements.AllowedCustody
            || e.CommittedQuantity < 0 || e.CommittedQuantity > Math.Max(0, e.AuthoritativeQuantity))
            Block(InventoryBlockerCode.InvalidCustody, "Custody evidence or the requested custody does not match.");
        if (e.HistoricalSnapshotUnavailable) Block(InventoryBlockerCode.HistoricalSnapshotUnavailable, "Mutable evidence changed after the requested cutoff; historical state cannot be reconstructed from current projections.");
        if (requirements.ExpectedFingerprint is not null && requirements.ExpectedFingerprint != e.Watermark.Fingerprint)
            Block(InventoryBlockerCode.StaleRead, "Read evidence has changed; the fingerprint is not a lock.");

        var positive = e.Projections.Where(x => x.Quantity > 0).ToArray();
        var raw = checked(e.Projections.Sum(x => x.Quantity));
        var receiptIds = ImmutableArray<long>.Empty;
        var receiptConfidence = InventoryConfidence.Unknown;
        var treatmentConfidence = InventoryConfidence.Unknown;
        DateTimeOffset? occupied = null;
        var replay = Replay(e, out var epoch);
        if (replay && epoch.Length > 0) occupied = epoch[0].RecordedAt ?? epoch[0].At;
        var pool = replay && ProveUntreatedPool(e, epoch, out receiptIds);
        var projectionConflict = e.Projections.Any(x => !x.ExactIdentity || x.Quantity < 0);
        if (projectionConflict) Block(InventoryBlockerCode.ConflictingIdentity, "A projection contradicts the canonical identity or contains a negative quantity.");

        // Room authority is the existing ledger. Custody adapters supply verified
        // dispatch-minus-reversal slices and never credit the source room again.
        var balanced = raw == e.AuthoritativeQuantity && !projectionConflict
            && positive.All(x => ValidTreatment(x, e)) && e.AuthoritativeQuantity >= 0;
        if (pool && !projectionConflict && !(balanced && positive.Length > 0 && positive.All(x => x.ReceiptId != null)))
        {
            treatmentConfidence = InventoryConfidence.Proven;
            receiptConfidence = receiptIds.Length == 1 && e.Receipts.Any(x => x.Id == receiptIds[0] && x.ExactIdentity && !x.IsDeleted)
                && epoch.Where(x => x.Quantity > 0).All(x => x.ReceiptId == receiptIds[0]
                    || x.MovementParent != null && e.Movements.Where(m => m.Incoming && m.Parent == x.MovementParent).All(m => m.ReceiptId == receiptIds[0])) ? InventoryConfidence.Proven
                : receiptIds.Length > 1 ? InventoryConfidence.Ambiguous : InventoryConfidence.Unknown;
            if (e.AuthoritativeQuantity > 0)
                slices.Add(new("u", "Untreated", e.AuthoritativeQuantity, treatmentConfidence,
                    positive.Select(x => x.Id).ToImmutableArray(), [], receiptIds));
        }
        else if (balanced && positive.Length > 0)
        {
            // Exact balanced, disjoint persisted slices with validated application
            // links can be presented without inventing missing untreated bins.
            foreach (var group in positive.GroupBy(x => new { x.Signature, x.State, x.ReceiptId }))
                slices.Add(new(group.Key.Signature, group.Key.State, group.Sum(x => x.Quantity), InventoryConfidence.Proven,
                    group.Select(x => x.Id).ToImmutableArray(), group.SelectMany(x => x.ApplicationIds).Distinct().Order().ToImmutableArray(),
                    group.Key.ReceiptId is long id ? [id] : []));
            treatmentConfidence = InventoryConfidence.Proven;
            receiptIds = positive.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).Distinct().Order().ToImmutableArray();
            receiptConfidence = positive.All(x => x.ReceiptId != null) && receiptIds.All(id => e.Receipts.Any(x => x.Id == id && x.ExactIdentity && !x.IsDeleted))
                ? InventoryConfidence.Proven : InventoryConfidence.Unknown;
        }
        else if (e.AuthoritativeQuantity == 0 && raw == 0 && !projectionConflict)
        {
            // Empty physical stock is not an unknown-treatment bin pool.
            treatmentConfidence = InventoryConfidence.Proven;
        }
        else if (positive.Select(x => x.Signature).Distinct().Count() > 1)
        {
            treatmentConfidence = InventoryConfidence.Ambiguous;
            if (requirements.RequireKnownTreatment) Block(InventoryBlockerCode.MixedTreatmentAmbiguity, "Overlapping or incomplete projections do not prove the surviving quantities of each treatment.");
        }
        else if (requirements.RequireKnownTreatment)
        {
            Block(raw > e.AuthoritativeQuantity ? InventoryBlockerCode.UnsupportedHistoricalEvidence : InventoryBlockerCode.UnknownTreatment,
                "Arrival, treatment or consumption evidence does not prove a current treatment pool. A quantity gap is not proof of Untreated.");
        }

        foreach (var p in e.Projections.Where(x => x.Quantity == 0))
            history.Add(new([p.Id], 0, ProjectionExclusionReason.Depleted, true, [Ref("TreatmentLineageSegment", p.Id)]));
        var excess = Math.Max(0, raw - Math.Max(0, e.AuthoritativeQuantity));
        if (excess > 0)
        {
            var remaining = excess;
            if (pool && !projectionConflict)
            {
                foreach (var p in positive.Where(x => IsAliasDuplicate(x, e)))
                {
                    var excluded = Math.Min(remaining, p.Quantity);
                    if (excluded == 0) break;
                    history.Add(new([p.Id], excluded, ProjectionExclusionReason.DuplicateStatusAlias, true, references));
                    candidates.Add(new(NormalizationCandidateKind.RetireDuplicate, [p.Id], excluded, e.AuthoritativeQuantity, true, references));
                    remaining -= excluded;
                }
            }
            if (remaining > 0)
            {
                var proven = pool && !projectionConflict;
                var ids = positive.Select(x => x.Id).ToImmutableArray();
                var duplicateIds = candidates.Where(x => x.Kind == NormalizationCandidateKind.RetireDuplicate).SelectMany(x => x.ProjectionIds).ToHashSet();
                var consumedFromDuplicates = e.Movements.Where(x => x.Outgoing && duplicateIds.Contains(x.SourceProjectionId ?? -1)).Sum(x => x.Quantity);
                history.Add(new(ids, remaining, !proven ? ProjectionExclusionReason.UnprovenProjection
                    : duplicateIds.Count > 0 && consumedFromDuplicates == remaining ? ProjectionExclusionReason.ConsumedHistoricalRepresentation
                    : ProjectionExclusionReason.StaleHistoricalPool, proven, references));
                if (proven) candidates.Add(new(NormalizationCandidateKind.ReconcileHistoricalPool, ids, remaining,
                    e.AuthoritativeQuantity, false, references));
            }
        }

        if (requirements.RequireExactReceipt && (receiptConfidence != InventoryConfidence.Proven
            || requirements.ReceiptId is long requested && !receiptIds.Contains(requested)))
            Block(InventoryBlockerCode.MissingReceiptProvenance, "This operation requires exact surviving receipt attribution; a treatment pool alone is insufficient.");
        var eligibleSlices = slices.Where(x => requirements.TreatmentSignature is null || x.Signature == requirements.TreatmentSignature)
            .Where(x => !requirements.RequireExactReceipt || requirements.ReceiptId is null || x.ReceiptEvidenceIds.Contains(requirements.ReceiptId.Value)).ToArray();
        if (requirements.TreatmentSignature is not null && eligibleSlices.Length == 0 && e.AuthoritativeQuantity > 0)
            Block(InventoryBlockerCode.SelectedTreatmentUnavailable, "The selected treatment is not a proven current slice.");
        var available = blockers.Count == 0
            ? Math.Min(Math.Max(0, e.AuthoritativeQuantity - e.CommittedQuantity),
                requirements.RequireKnownTreatment || requirements.RequireExactReceipt || requirements.TreatmentSignature is not null
                    ? eligibleSlices.Sum(x => x.Quantity) : Math.Max(0, e.AuthoritativeQuantity - e.CommittedQuantity)) : 0;
        return new($"{e.Location.Custody}:{e.Location.WarehouseId}:{e.Location.RoomId}:{e.Location.CustodyRecordId}:{e.Identity.Key}",
            e.Identity, e.Location, occupied, e.AuthoritativeQuantity, e.CommittedQuantity, available, raw,
            e.IdentityVerified && e.CustodyVerified && e.AuthoritativeQuantity >= 0 ? InventoryConfidence.Proven : InventoryConfidence.Unknown,
            treatmentConfidence, slices.ToImmutableArray(), new(receiptConfidence, receiptIds,
                receiptConfidence == InventoryConfidence.Proven ? "Exact receipt evidence is retained." : "Receipt evidence is retained without inventing a surviving per-receipt allocation."),
            history.ToImmutableArray(), new(Algorithm, "ledger-movement-application/v1", references, candidates.ToImmutableArray(),
                "Recorded ledger sequence for current occupancy; effective dates retained, never rewritten", BackdatedRows(e.Ledger)),
            blockers.DistinctBy(x => x.Code).ToImmutableArray(), e.Watermark,
            blockers.Count == 0 && !e.CustodyAllocations.IsDefault ? e.CustodyAllocations : []);

        void Block(InventoryBlockerCode code, string detail) => blockers.Add(new(code, detail));
    }

    private static bool Replay(InventoryPositionEvidence e, out ImmutableArray<InventoryLedgerEvidence> epoch)
    {
        var rows = new List<InventoryLedgerEvidence>();
        long balance = 0;
        // Current custody follows recorded ledger events. A backdated run's
        // business date is not evidence that bins were consumed before arrival.
        // Retain both clocks and disclose every effective-date inversion.
        foreach (var row in e.Ledger.OrderBy(x => x.RecordedAt ?? x.At).ThenBy(x => x.Id))
        {
            if (!row.ExactIdentity || row.Kind == InventoryLedgerKinds.StartingInventoryImport) { epoch = []; return false; }
            if (balance == 0) rows.Clear();
            balance += row.Quantity;
            if (balance < 0) { epoch = []; return false; }
            rows.Add(row);
        }
        epoch = rows.ToImmutableArray();
        return e.Location.Custody == InventoryCustody.Room && e.Ledger.Length > 0 && balance == e.AuthoritativeQuantity;
    }

    private static bool ProveUntreatedPool(InventoryPositionEvidence e, ImmutableArray<InventoryLedgerEvidence> epoch,
        out ImmutableArray<long> receiptIds)
    {
        receiptIds = [];
        if (epoch.IsEmpty) return false;
        var start = epoch.Min(x => x.At);
        var ids = new HashSet<long>();
        var arrivals = epoch.Where(x => x.Quantity > 0).ToArray();
        if (arrivals.Length == 0) return false;
        foreach (var row in arrivals)
        {
            if (row.Kind == "ReceiptAdd" && row.ReceiptId is long receiptId)
            {
                var receipt = e.Receipts.SingleOrDefault(x => x.Id == receiptId);
                if (receipt is null || !receipt.ExactIdentity || receipt.IsDeleted || receipt.IsTransferReceipt
                    || receipt.Quantity != row.Quantity || arrivals.Count(x => x.ReceiptId == receiptId) != 1) return false;
                ids.Add(receiptId);
            }
            else
            {
                // Every transfer/return needs an immutable parent and exact movement
                // conservation. A positive correction/true-up is not an arrival proof.
                if (row.MovementParent is null || row.Kind is not ("TransferIn" or "InterCrewTransferReceive"
                    or "TransferReversalIn" or "InterCrewTransferReversalSource" or "BinsRunReversal"
                    or "OutsideWarehouseTransferReversal" or "ProcessorShipmentReversal" or "InterCrewTransferReturnToSource")) return false;
                var incoming = e.Movements.Where(x => x.Incoming && x.Parent == row.MovementParent).ToArray();
                if (incoming.Length == 0 || incoming.Sum(x => x.Quantity) != row.Quantity
                    || incoming.Any(x => !x.ExactIdentity || x.Quantity <= 0 || x.Signature != "u" || x.State != "Untreated")) return false;
                if (row.Kind == "TransferIn" && incoming.Any(x => x.Kind != "Transfer" || x.ReversesId != null)
                    || row.Kind == "InterCrewTransferReceive" && incoming.Any(x => x.Kind != "InterCrewReceive" || x.ReversesId != null)
                    || row.Kind is not ("TransferIn" or "InterCrewTransferReceive") && incoming.Any(x => x.ReversesId == null)) return false;
                if (incoming.Any(x => x.ReversesId is long originalId && !e.Movements.Any(original => original.Id == originalId
                    && original.Outgoing && original.ExactIdentity && original.Quantity >= x.Quantity
                    && original.Signature == x.Signature && original.State == x.State))) return false;
                foreach (var id in incoming.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value)) ids.Add(id);
            }
        }
        if (e.Movements.Any(x => x.At >= start && (!x.ExactIdentity || x.Signature != "u" || x.State != "Untreated"))) return false;
        if (e.Applications.Any(x => x.ReceiptId is long id ? ids.Contains(id) : AppliesToRoom(x, e) && x.AppliedAt >= start)) return false;
        if (e.Projections.Any(x => x.Quantity > 0 && (x.Signature != "u" || x.State != "Untreated" || !x.ApplicationIds.IsEmpty))) return false;
        receiptIds = ids.Order().ToImmutableArray();
        return true;
    }

    // A treatment carried in from another room belongs to that allocation. It
    // cannot make pre-existing untreated stock in this destination treated/ambiguous.
    // Unknown scope remains conservative for older serialized fixture evidence.
    private static bool AppliesToRoom(InventoryApplicationEvidence application, InventoryPositionEvidence evidence) =>
        application.RoomId == null || application.RoomId == evidence.Location.RoomId;

    private static bool ValidTreatment(InventoryProjectionEvidence p, InventoryPositionEvidence e)
    {
        if (p.State == "Untreated")
        {
            var earliestArrival = e.Ledger.Where(x => x.Quantity > 0).Select(x => x.At).DefaultIfEmpty(p.CreatedAt).Min();
            // An exact incoming allocation proves when THIS stock entered the room.
            // Later receipts do not inherit a room treatment applied before arrival.
            var arrivals = e.Movements.Where(x => x.Incoming && x.DestinationProjectionId == p.Id).ToArray();
            var exactArrival = p.ReceiptId is long receipt && e.Receipts.Any(x => x.Id == receipt && x.ExactIdentity && !x.IsDeleted)
                && arrivals.Length > 0 && arrivals.All(x => x.ExactIdentity && x.ReceiptId == receipt && x.Signature == "u" && x.State == "Untreated")
                && arrivals.Sum(x => x.Quantity) - e.Movements.Where(x => x.Outgoing && x.SourceProjectionId == p.Id).Sum(x => x.Quantity) == p.Quantity;
            if (exactArrival) earliestArrival = arrivals.Min(x => x.At);
            var receiptLedger = e.Ledger.Where(x => x.ReceiptId == p.ReceiptId).ToArray();
            var exactReceiptAllocation = exactArrival || p.ReceiptId is long receiptId
                && e.Receipts.Any(x => x.Id == receiptId && x.ExactIdentity && !x.IsDeleted)
                && receiptLedger.Length > 0 && receiptLedger.All(x => x.ExactIdentity) && receiptLedger.Sum(x => x.Quantity) == p.Quantity;
            return p.Signature == "u" && p.ApplicationIds.IsEmpty
                && !e.Applications.Any(x => x.ReversedAt is null && (x.ReceiptId is long id
                    ? !exactReceiptAllocation || id == p.ReceiptId : AppliesToRoom(x, e) && x.AppliedAt >= earliestArrival));
        }
        if (p.State != "Confirmed" || p.ApplicationIds.IsEmpty || !p.Signature.StartsWith("u|a:", StringComparison.Ordinal)) return false;
        var suffix = p.Signature[4..].Split(',');
        return suffix.Length == p.ApplicationIds.Length && suffix.All(x => long.TryParse(x, out var id) && p.ApplicationIds.Contains(id))
            && p.ApplicationIds.All(id => e.Applications.Any(x => x.Id == id && x.ReversedAt is null));
    }

    private static bool IsAliasDuplicate(InventoryProjectionEvidence p, InventoryPositionEvidence e)
    {
        if (p.ReceiptId != null || e.Movements.Any(x => x.DestinationProjectionId == p.Id)) return false;
        var outgoing = e.Movements.Where(x => x.SourceProjectionId == p.Id).ToArray();
        if (outgoing.Length != 1 || outgoing[0].CreatedAt < p.CreatedAt || outgoing[0].Parent is null) return false;
        var deduction = e.Ledger.SingleOrDefault(x => x.MovementParent == outgoing[0].Parent && x.Quantity == -outgoing[0].Quantity);
        if (deduction is null) return false;
        var original = p.Quantity + outgoing[0].Quantity;
        var before = e.Ledger.Where(x => x.Id < deduction.Id).Sum(x => x.Quantity);
        var olderIds = e.Projections.Where(x => x.CreatedAt < p.CreatedAt && x.RawKey != p.RawKey
            && InventoryStatusIdentity.NormalizeLineageKey(x.RawKey) == InventoryStatusIdentity.NormalizeLineageKey(p.RawKey)).Select(x => x.Id).ToHashSet();
        var olderArrivals = e.Movements.Where(x => x.DestinationProjectionId is long id && olderIds.Contains(id) && x.CreatedAt < p.CreatedAt).Sum(x => x.Quantity)
            - e.Movements.Where(x => x.SourceProjectionId is long id && olderIds.Contains(id) && x.CreatedAt < p.CreatedAt).Sum(x => x.Quantity);
        return before == original && olderArrivals == original;
    }

    private static ImmutableArray<long> BackdatedRows(ImmutableArray<InventoryLedgerEvidence> ledger)
    {
        var ids = ImmutableArray.CreateBuilder<long>();
        var latest = DateTimeOffset.MinValue;
        foreach (var row in ledger.OrderBy(x => x.RecordedAt ?? x.At).ThenBy(x => x.Id))
        {
            if (row.At < latest) ids.Add(row.Id);
            if (row.At > latest) latest = row.At;
        }
        return ids.ToImmutable();
    }

    private static InventoryEvidenceReference Ref(string entity, long id) => new(entity, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
