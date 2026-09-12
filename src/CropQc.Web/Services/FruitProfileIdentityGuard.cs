using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

/// <summary>Master Data protection only; never creates an operational correction.</summary>
internal static class FruitProfileIdentityGuard
{
    internal const string InUseMessage = "This Fruit Profile is already used by operational history. Variety code, commodity, production type, and Organic/Conventional classification cannot be changed through Master Data because doing so would reinterpret existing inventory or reporting. Create a new Fruit Profile or use the controlled identity-correction process.";
    internal const string RetryMessage = "The Fruit Profile or its operational references are being changed by another operation. Nothing was saved. Refresh and retry.";

    internal static bool ChangesIdentity(FruitProfile profile, string code, string commodity, string productionType, bool organic) =>
        profile.VarietyCode != code || profile.FruitType != commodity
        || profile.ProductionType != productionType || profile.IsOrganic != organic;

    internal static async Task<bool> IsInUseAsync(CropQcDbContext db, int id, CancellationToken ct) =>
        // Do not filter by positive balance, crop, deleted/test status, reversal or active revision.
        // Even depleted/deleted operational history still resolves live profile classification.
        await db.Receipts.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.RoomInventoryAdjustments.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.RoomDepletions.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.RoomInventoryLosses.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.RoomTransfers.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.BinsRunEntries.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id || x.ReportingFruitProfileIdSnapshot == id, ct)
        || await db.QcSamples.IgnoreQueryFilters().AnyAsync(x => x.FieldSampleFruitProfileId == id, ct)
        || await db.TreatmentLineageSegments.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.RoomTreatmentApplicationSources.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.OutsideWarehouseTransfers.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.InterCrewTransfers.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.ProcessorShipmentLines.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.InventoryIdentityCorrections.IgnoreQueryFilters().AnyAsync(x => x.SourceFruitProfileId == id || x.TargetFruitProfileId == id, ct)
        || await db.RunProjectionSources.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct)
        || await db.ActualRunOverrideRequestLines.IgnoreQueryFilters().AnyAsync(x => x.FruitProfileId == id, ct);

    internal static async Task LockReferencesAsync(CropQcDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        // Short, identity-edit-only lock. Includes snapshot IDs without a FK: a profile row
        // lock alone cannot exclude their concurrent first use. NOWAIT fails closed rather
        // than waiting behind operational writes. READ COMMITTED checks below see every
        // writer that committed before these locks. Locks live through the profile commit.
        await db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE "Receipts", "RoomInventoryAdjustments", "RoomDepletions",
                "RoomInventoryLosses", "RoomTransfers", "BinsRunEntries", "QcSamples",
                "TreatmentLineageSegments", "RoomTreatmentApplicationSources",
                "OutsideWarehouseTransfers", "InterCrewTransfers", "ProcessorShipmentLines",
                "InventoryIdentityCorrections", "RunProjectionSources", "ActualRunOverrideRequestLines"
            IN SHARE MODE NOWAIT
            """, ct);
    }
}
