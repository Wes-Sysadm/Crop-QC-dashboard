using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

/// <summary>Proves treatment carried by an immutable departure. Source room
/// arrivals are not arrivals into transit, and another allocation's applications
/// cannot change this allocation's membership.</summary>
public static class InventoryCustodyTreatment
{
    public static InventoryEffectiveTreatment? Read(InventoryProjectionEvidence source,
        InventoryMovementEvidence movement, ImmutableArray<InventoryApplicationEvidence> applications)
    {
        if (!source.ExactIdentity || !movement.ExactIdentity || movement.Quantity <= 0
            || movement.SourceProjectionId != source.Id || !movement.Outgoing || movement.Incoming
            || movement.DestinationProjectionId != null || movement.ReversesId != null
            || movement.Parent == null
            || movement.Kind is not ("InterCrewDispatch" or "OutsideWarehouseTransfer" or "ProcessorShipment")
            || movement.ReceiptId != source.ReceiptId || movement.Signature != source.Signature
            || movement.State != source.State || source.ApplicationIds.IsDefault
            || source.ApplicationIds.Distinct().Count() != source.ApplicationIds.Length) return null;
        if (movement.State == "Untreated")
            return movement.Signature == "u" && source.ApplicationIds.IsEmpty ? new("u", "Untreated", []) : null;
        if (movement.State != "Confirmed" || source.ApplicationIds.IsEmpty
            || movement.Signature != "u|a:" + string.Join(',', source.ApplicationIds.Order())) return null;
        // The source's exact application links must exist before departure in
        // both business time and committed chronology. Later audited reversals
        // change effective membership without rewriting the dispatch snapshot.
        if (source.ApplicationIds.Any(id => applications.Count(a => a.Id == id) != 1
            || !applications.Any(a => a.Id == id && a.AppliedAt <= movement.At
                && (a.RecordedAt == null || a.RecordedAt <= movement.CreatedAt)))) return null;
        return InventoryEffectiveTreatment.Read(movement.Signature, movement.State, source.ApplicationIds, applications);
    }

    public static bool Proves(InventoryProjectionEvidence projection, InventoryPositionEvidence evidence)
    {
        if (evidence.Location.Custody == InventoryCustody.Room || !evidence.CustodyVerified
            || !evidence.IdentityVerified || projection.CustodyTreatment is not { } proof) return false;
        var movement = proof.Movement;
        if (projection.Quantity <= 0 || evidence.Projections.Where(p => p.CustodyTreatment?.Movement.Id == movement.Id)
                .Sum(p => p.Quantity) > movement.Quantity) return false;
        var original = projection with { Signature = proof.SourceSignature, State = proof.SourceState,
            ReceiptId = proof.SourceReceiptId, ApplicationIds = proof.ApplicationIds };
        var treatment = Read(original, movement, evidence.Applications);
        return treatment != null && projection.ReceiptId == movement.ReceiptId
            && projection.Signature == treatment.Signature && projection.State == treatment.State
            && projection.ApplicationIds.Order().SequenceEqual(treatment.ApplicationIds.Order());
    }
}
