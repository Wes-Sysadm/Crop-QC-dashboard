using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task ReconstructRecordedCohortsAsync(CropQcDbContext db, InventoryCommand command,
        InventoryPositionEvidence evidence, InventoryAvailabilityResult result, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        var replay = InventoryEventReplay.Replay(evidence);
        Require(replay.QuantityConserved && replay.UnresolvedEvents.IsEmpty
            && replay.Cohorts.All(x => x.State is "Untreated" or "Confirmed"),
            "Recorded custody or treatment allocation is incomplete; projection reconstruction is not authorized.");
        var origin = await InventoryOriginGuard.ValidateAsync(db, [evidence], now, ct);
        Require(origin == null, origin ?? "Origin validation failed.");
        var ids = evidence.Projections.Where(x => x.Disposition == "Current").Select(x => x.Id).ToArray();
        var rows = await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        Require(rows.Count == ids.Length, "Recorded reconstruction projection disappeared.", InventoryCommandStatus.Stale);
        foreach (var row in rows)
        {
            var before = evidence.Projections.Single(x => x.Id == row.Id);
            Require(row.Disposition == "Current" && row.ConcurrencyVersion == before.Version && row.CurrentBins == before.Quantity
                && row.UpdatedAt == before.UpdatedAt, "Recorded reconstruction projection changed.", InventoryCommandStatus.Stale);
            CanonicalProjectionFactory.Retire(row, command.OperationKey, now);
        }
        await db.SaveChangesAsync(ct);
        // A distinct namespace keeps rebuilt historical stock separate from any
        // incoming credit within the same command. This is not a physical bin ID.
        var namespaceKey = "r:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(command.OperationKey)))[..48];
        var replacementFactory = new CanonicalProjectionFactory(db, namespaceKey);
        foreach (var cohort in replay.Cohorts)
        {
            var segment = await replacementFactory.CurrentAsync(evidence.Identity, evidence.Location.WarehouseId,
                evidence.Location.RoomId!.Value, cohort.Signature, cohort.State, cohort.ReceiptId, cohort.ApplicationIds, now, ct);
            segment.CurrentBins = checked(segment.CurrentBins + cohort.Quantity);
        }
        AddAudit(db, command, "CanonicalRecordedCohortReconstruction", result.PositionKey, evidence.Projections,
            new { result.AuthoritativeQuantity, replay.Cohorts, result.Proof.ChronologyBasis }, now);
        await Stage("NormalizationAudit", db, attempt, ct);
        await db.SaveChangesAsync(ct);
        Require(await PhysicalAsync(db, evidence.Identity, evidence.Location.WarehouseId, evidence.Location.RoomId!.Value, ct)
            == evidence.AuthoritativeQuantity, "Recorded reconstruction changed physical inventory.");
    }
}
