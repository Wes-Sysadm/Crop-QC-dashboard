using CropQc.Shared.Inventory;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private static void AdmitDestination(CanonicalProjectionFactory factory, InventoryPositionEvidence evidence)
    {
        var assessment = InventoryDestinationAdmission.Assess(evidence);
        Require(assessment.Allowed, string.Join(" ", assessment.Failures));
        factory.Destinations.TryAdd((evidence.Location.RoomId!.Value, evidence.Identity.Key), evidence);
    }

    private static bool IsAdmittedDestination(CanonicalProjectionFactory factory, int room, string identity) =>
        factory.Destinations.ContainsKey((room, identity));

    private async Task PrepareDestinationAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand command, InventoryPositionEvidence evidence, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        AdmitDestination(factory, evidence);
        var key = (evidence.Location.RoomId!.Value, evidence.Identity.Key);
        if (!factory.PreparedDestinations.Add(key)) return;
        var originFailure = await InventoryOriginGuard.ValidateAsync(db, [evidence], now, ct);
        Require(originFailure == null, originFailure ?? "Destination recorded origin/custody validation failed.");
        var result = InventoryAvailabilityResolver.Resolve(evidence, new());
        // Retain the existing audited, independently proven untreated supersession
        // where it applies. A failed/unavailable treatment proof is not a veto:
        // the incoming command still creates its own isolated cohort.
        if (result.IsOperable && !result.UsesRecordedCohorts && result.TreatmentConfidence == InventoryConfidence.Proven
            && result.TreatmentSlices.All(x => x.Signature == "u")
            && evidence.Projections.Where(x => x.Disposition == "Current").All(x => x.ExactIdentity
                && x.Quantity >= 0 && x.Signature == "u" && x.ApplicationIds.IsEmpty))
        {
            await NormalizePositionAsync(db, factory, command, evidence, result, now, attempt, ct);
            evidence = (await new InventoryEvidenceLoader(db).LoadAsync(new(evidence.Location.WarehouseId,
                [evidence.Location.RoomId.Value]), now, ct)).Positions.Single(x => x.Identity.Key == evidence.Identity.Key);
        }
        factory.Destinations[key] = evidence;
    }

    private static async Task ValidateDestinationsAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand command, DateTimeOffset now, CancellationToken ct)
    {
        var loader = new InventoryEvidenceLoader(db);
        foreach (var before in factory.Destinations.Values)
        {
            var batch = await loader.LoadAsync(new(before.Location.WarehouseId, [before.Location.RoomId!.Value]), now, ct);
            var after = batch.Positions.Single(x => x.Identity.Key == before.Identity.Key);
            var beforeProjection = before.Projections.Where(x => x.Disposition == "Current").Sum(x => x.Quantity);
            var afterProjection = after.Projections.Where(x => x.Disposition == "Current").Sum(x => x.Quantity);
            Require(after.AuthoritativeQuantity >= 0
                && afterProjection - beforeProjection == after.AuthoritativeQuantity - before.AuthoritativeQuantity,
                "Destination command ledger and projection deltas do not conserve the incoming quantity.");
            foreach (var prior in before.Projections)
            {
                var retained = after.Projections.SingleOrDefault(x => x.Id == prior.Id);
                var consumed = after.Movements.Where(x => x.SourceProjectionId == prior.Id && x.Outgoing
                    && !before.Movements.Any(old => old.Id == x.Id)).Sum(x => x.Quantity);
                var unchanged = retained != null && retained.ApplicationIds.SequenceEqual(prior.ApplicationIds)
                    && (retained with { ApplicationIds = prior.ApplicationIds }) == prior;
                var exactConsumption = retained != null && consumed > 0 && retained.Quantity == prior.Quantity - consumed
                    && retained.Quantity >= 0 && retained.CohortKey == prior.CohortKey && retained.RawKey == prior.RawKey
                    && retained.ReceiptId == prior.ReceiptId && retained.State == prior.State && retained.Signature == prior.Signature
                    && retained.ApplicationIds.SequenceEqual(prior.ApplicationIds) && retained.CreatedAt == prior.CreatedAt
                    && retained.UpdatedAt == now && retained.Version > prior.Version
                    && (retained.Disposition == "Current" || retained.Quantity == 0 && retained.Disposition == "Historical");
                Require(unchanged || exactConsumption,
                    "Destination cohort changed without exact recorded consumption or preserved incoming allocation evidence.");
            }
            if (InventoryDestinationAdmission.Assess(before).RequiresReconciliation)
                AddAudit(db, command, "CanonicalDestinationReconciliation", $"Room:{before.Location.RoomId}:{before.Identity.Key}",
                    new { before.AuthoritativeQuantity, Projections = before.Projections, BeforeDiagnostic = InventoryAvailabilityResolver.Resolve(before, new()) },
                    new
                    {
                        after.AuthoritativeQuantity,
                        NewCohorts = after.Projections.Where(x => x.CohortKey == command.OperationKey),
                        Movements = after.Movements.Where(x => !before.Movements.Any(prior => prior.Id == x.Id)),
                        ReconciliationRequired = true,
                        Message = "Incoming inventory was recorded in separate event-backed cohorts. Existing historical projection discrepancies remain identified; no treatment or receipt ancestry was invented."
                    }, now);
        }
    }
}
