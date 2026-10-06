using System.Collections.Immutable;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private static Task LockOrasDefinitionAsync(CropQcDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        LOCK TABLE "FruitProfiles", "Receipts", "ReceiptVarietyLines", "RoomInventoryAdjustments",
            "RoomDepletions", "RoomInventoryLosses", "RoomTransfers", "BinsRunEntries",
            "TreatmentLineageSegments", "TreatmentLineageMovements", "RoomTreatmentApplicationSources",
            "InterCrewTransfers", "OutsideWarehouseTransfers", "ProcessorShipmentLines", "RunExpectationSources",
            "RunProjectionSources", "PackoutRuns", "InventoryIdentityCorrections"
        IN SHARE ROW EXCLUSIVE MODE NOWAIT
        """, ct);

    private static async Task<ImmutableArray<InventoryCommandEffect>> CorrectOrasDefinitionAsync(
        CropQcDbContext db, InventoryCommand command, DateTimeOffset now, CancellationToken ct)
    {
        Require(db.CanonicalInventoryEnabled && command.Lines.IsEmpty && command.ProductDefinition is { FruitProfileId: > 0 },
            "ORAS definition correction requires canonical mode and a reviewed definition preview.");
        var actor = await db.Users.SingleAsync(x => x.Id == command.ActorId, ct);
        var roles = await db.UserRoles.Include(x => x.Role).ThenInclude(x => x.PageAccesses)
            .Where(x => x.UserId == actor.Id).ToListAsync(ct);
        Require(actor.Email.Equals("wes@fruitandland.com", StringComparison.OrdinalIgnoreCase)
            || roles.Count == 1 && roles[0].Role.IsActive && (roles[0].Role.Name == "Admin"
                || roles[0].Role.PageAccesses.Any(x => x.AreaKey == "varieties" && x.AccessLevel == "Admin")),
            "An active variety administrator must perform this historical definition correction.");
        var input = command.ProductDefinition!;
        var plan = await OrasDefinitionCorrection.BuildAsync(db, input.FruitProfileId, ct);
        Require(plan.Preview.Fingerprint == input.ExpectedFingerprint, "ORAS inventory or history changed. Refresh the correction preview.", InventoryCommandStatus.Stale);
        foreach (var edit in plan.Edits) edit.Entry.Property(edit.Field).CurrentValue = edit.After;
        foreach (var group in plan.Preview.Changes.GroupBy(x => (x.Entity, x.Id)))
            AddAudit(db, command, "OrasHistoricalDefinitionCorrection", group.Key.Entity + "/" + group.Key.Id,
                new { command.OperationKey, command.Reason, input.ExpectedFingerprint, Values = group.ToDictionary(x => x.Field, x => x.Before) },
                new { command.OperationKey, command.Reason, Values = group.ToDictionary(x => x.Field, x => x.After) }, now);
        // Existing ledger amounts, receipt IDs, run/movement IDs and treatment evidence
        // are retained. There is deliberately no physical command effect or normalization.
        return [];
    }
}
