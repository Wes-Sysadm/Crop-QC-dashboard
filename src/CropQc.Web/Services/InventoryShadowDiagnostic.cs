using System.Collections.Immutable;
using System.Data;
using CropQc.Data;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record InventoryShadowReport(DateTimeOffset AsOf, string Mode, int EvidenceRowsLoaded,
    ImmutableArray<InventoryShadowPosition> Positions);

/// <summary>Compares existing treatment gates, not write authorization, destination rules or reservations.</summary>
public sealed class InventoryShadowDiagnostic(CropQcDbContext db, IInventoryAvailability canonical,
    IRoomInventoryLedgerQueryService ledger, IRoomTreatmentService treatment)
{
    public async Task<InventoryShadowReport> ResolveAsync(InventoryScope scope, InventoryOperationRequirements requirements,
        DateTimeOffset asOf, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
                ? IsolationLevel.Serializable : IsolationLevel.RepeatableRead, ct) : null;
        if (transaction != null && db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var result = await canonical.ResolveAsync(scope, requirements, asOf, ct);
        if (scope.Custody != InventoryCustody.Room)
            return new(asOf, "Read-only custody evidence; legacy room gates do not apply", result.EvidenceRowsLoaded,
                result.Positions.Select(x => InventoryShadowComparison.Compare(x, [])).ToImmutableArray());
        var snapshots = await ledger.GetSnapshotsAsOfAsync(scope.WarehouseId, scope.RoomIds.IsDefaultOrEmpty ? null : scope.RoomIds, asOf, ct);
        var selections = await treatment.GetSelectionsAsync(snapshots, ct);
        var dump = await treatment.GetActualRunCorrectionSelectionsAsync(snapshots, [], ct);
        return new(asOf, "Shadow only: existing shared movement gate and new-run dump treatment gate; excludes seals, permissions and destination-specific rules",
            result.EvidenceRowsLoaded, result.Positions.Select(x =>
            {
                var key = $"{x.Location.RoomId}:{x.Identity.Key}";
                return InventoryShadowComparison.Compare(x,
                [Observe("Existing shared movement treatment gate", selections.GetValueOrDefault(key) ?? []),
                 Observe("Existing new-run dump treatment gate", dump.GetValueOrDefault(key) ?? [])]);
            }).ToImmutableArray());
    }

    private static InventoryWorkflowObservation Observe(string name, IReadOnlyList<TreatmentSegmentSelection> rows) => new(name,
        rows.Where(x => x.IsAvailable).Sum(x => Math.Max(0, x.CurrentBins)),
        rows.Select(x => x.TreatmentSignature).Distinct().Order().ToImmutableArray(),
        rows.Select(x => x.ReceiptId is long id ? $"Selection receipt {id}; exact allocation not independently asserted by this gate" : "Shared/unattributed selection").Distinct().ToImmutableArray(),
        rows.FirstOrDefault(x => !x.IsAvailable)?.UnavailableReason);
}
