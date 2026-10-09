using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

/// <summary>Verifies an isolated incoming allocation without using unrelated
/// destination projection quantities as inventory or treatment evidence.</summary>
public static class InventoryCohortEvidence
{
    public static bool ProvesReceipt(InventoryPositionEvidence evidence, long receiptId)
    {
        if (!evidence.ApplicationAllocationsLoaded || !evidence.Receipts.Any(r => r.Id == receiptId
            && r.ExactIdentity && !r.IsDeleted && !r.IsTransferReceipt)) return false;
        var replay = InventoryEventReplay.Replay(evidence);
        var owned = replay.Cohorts.Where(x => x.ReceiptId == receiptId).ToArray();
        if (!replay.QuantityConserved || owned.Length == 0 || owned.Any(x => x.State == "Unknown")) return false;
        // This fallback cannot prove that a shared remainder excludes this
        // receipt. Keep exact receipt operations conservative; ordinary movement
        // of separately established treatment cohorts does not need this proof.
        if (replay.Cohorts.Any(x => x.ReceiptId == null)) return false;
        var projections = evidence.Projections.Where(x => x.Disposition == "Current" && x.Quantity > 0 && x.ReceiptId == receiptId).ToArray();
        return projections.Length > 0 && projections.All(p => p.ExactIdentity)
            && projections.Sum(x => x.Quantity) == owned.Sum(x => x.Quantity)
            && owned.GroupBy(x => x.Signature).All(g => projections.Where(x => x.Signature == g.Key).Sum(x => x.Quantity) == g.Sum(x => x.Quantity));
    }

    public static ImmutableArray<InventoryProjectionEvidence> Proven(InventoryPositionEvidence evidence)
    {
        if (evidence.Location.Custody != InventoryCustody.Room || !evidence.CustodyVerified
            || !evidence.IdentityVerified || evidence.AuthoritativeQuantity < 0) return [];
        var replay = evidence.ApplicationAllocationsLoaded ? InventoryEventReplay.Replay(evidence) : null;
        return evidence.Projections.Where(p => p.Disposition == "Current" && p.Quantity > 0
            && p.ExactIdentity && p.RawKey == evidence.Identity.Key && !string.IsNullOrWhiteSpace(p.CohortKey)
            && (Prove(p, evidence) || replay is { QuantityConserved: true } && p.State is "Untreated" or "Confirmed"
                && replay.Cohorts.Where(c => c.ReceiptId == p.ReceiptId && c.Signature == p.Signature && c.State == p.State
                    && c.ApplicationIds.SequenceEqual(p.ApplicationIds)).Sum(c => c.Quantity)
                    == evidence.Projections.Where(other => other.Disposition == "Current" && other.Quantity > 0
                        && other.ReceiptId == p.ReceiptId && other.Signature == p.Signature).Sum(other => other.Quantity)))
            .ToImmutableArray();
    }

    private static bool Prove(InventoryProjectionEvidence p, InventoryPositionEvidence e)
    {
        var incoming = e.Movements.Where(m => m.Incoming && m.DestinationProjectionId == p.Id).ToArray();
        var outgoing = e.Movements.Where(m => m.Outgoing && m.SourceProjectionId == p.Id).ToArray();
        if (incoming.Length == 0 || incoming.Concat(outgoing).Any(m => !m.ExactIdentity || m.Quantity <= 0
                || m.ReceiptId != p.ReceiptId || m.Signature != p.Signature || m.State != p.State)
            || incoming.Sum(m => m.Quantity) - outgoing.Sum(m => m.Quantity) != p.Quantity) return false;
        foreach (var movement in incoming)
        {
            var supported = e.Ledger.Any(l => l.ExactIdentity && l.Quantity > 0 && Related(l, movement))
                || movement.Kind == "Receipt" && movement.ReceiptId is long receipt
                    && e.Receipts.Any(r => r.Id == receipt && r.ExactIdentity && !r.IsTransferReceipt)
                    && e.Ledger.Count(l => l.Kind == "ReceiptAdd" && l.ReceiptId == receipt && l.Quantity == movement.Quantity && l.ExactIdentity) == 1;
            if (!supported) return false;
        }
        var arrived = incoming.Min(m => m.At);
        var recorded = incoming.Min(m => m.CreatedAt);
        // Any later unexplained withdrawal could have consumed this allocation.
        // A projection claiming it survived is not sufficient evidence.
        foreach (var debit in e.Ledger.Where(l => l.Quantity < 0 && (l.RecordedAt ?? l.At) >= recorded))
        {
            var allocations = e.Movements.Where(m => m.Outgoing && m.ExactIdentity
                && Related(debit, m)).ToArray();
            if (allocations.Sum(m => m.Quantity) != -debit.Quantity) return false;
        }
        var applicable = e.Applications.Where(a => a.ReversedAt == null
            && (a.ReceiptId is long id ? id == p.ReceiptId && a.AppliedAt >= arrived
                : (a.RoomId == null || a.RoomId == e.Location.RoomId) && a.AppliedAt >= arrived)).ToArray();
        if (p.State == "Untreated") return p.Signature == "u" && p.ApplicationIds.IsEmpty && applicable.Length == 0;
        return p.State == "Confirmed" && !p.ApplicationIds.IsEmpty
            && p.Signature == "u|a:" + string.Join(',', p.ApplicationIds.Order())
            && p.ApplicationIds.All(id => e.Applications.Any(a => a.Id == id && a.ReversedAt == null))
            && applicable.All(a => p.ApplicationIds.Contains(a.Id));
    }

    public static bool Related(InventoryLedgerEvidence ledger, InventoryMovementEvidence movement) =>
        ledger.MovementParent != null && ledger.MovementParent == movement.Parent
        || ledger.MovementParent == null && movement.Parent == null
            && ledger.InvariantVersion >= InventoryLedgerKinds.CanonicalCommandInvariantVersion
            && ledger.ReceiptId != null && ledger.ReceiptId == movement.ReceiptId
            && ledger.RecordedAt == movement.CreatedAt
            && !string.IsNullOrWhiteSpace(ledger.OperationKey) && !string.IsNullOrWhiteSpace(movement.OperationKey)
            && (ledger.Kind is "CorrectOriginalRoomIn" or "CorrectOriginalRoomOut" && movement.Kind == "ReceiptLocationCorrection"
                || ledger.Kind is "ReceiptAdminOverride" or "ReceiptQuantityCorrection" && movement.Kind is "ReceiptQuantityCorrection" or "ReceiptVoid");
}
