using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CropQc.Data.Inventory;

public sealed record OrasDefinitionFieldChange(string Entity, string Id, string Field, object? Before, object? After);
public sealed record OrasDefinitionPreview(int FruitProfileId, string Fingerprint, IReadOnlyList<OrasDefinitionFieldChange> Changes,
    int Receipts, int LedgerRows, int ReceivedBins, int LedgerNetBins);

/// <summary>
/// Read-only plan for correcting an erroneous product definition, including depleted history.
/// No physical transaction, new profile, normalization or receipt reassignment is involved.
/// Only the canonical executor may apply the plan, under its transaction and audit journal.
/// </summary>
public static class OrasDefinitionCorrection
{
    internal sealed record Edit(EntityEntry Entry, string Field, object? After);
    internal sealed record Plan(OrasDefinitionPreview Preview, IReadOnlyList<Edit> Edits);

    public static async Task<OrasDefinitionPreview> PreviewAsync(CropQcDbContext db, int id, CancellationToken ct = default)
        => (await BuildAsync(db, id, ct)).Preview;

    internal static async Task<Plan> BuildAsync(CropQcDbContext db, int id, CancellationToken ct)
    {
        var profile = await db.FruitProfiles.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Fruit profile was not found.");
        if (!OrasProductDefinition.Applies(profile.VarietyCode) || profile.FruitType != "Pear"
            || profile.ProductionType != "Conventional" || profile.IsOrganic)
            throw new InvalidOperationException("This correction requires the reviewed Conventional ORAS pear definition. Refresh and review its current state.");
        if (await db.FruitProfiles.AnyAsync(x => x.Id != id && x.VarietyCode.Trim().ToUpper() == "ORAS", ct))
            throw new InvalidOperationException("Multiple ORAS definitions require individual review.");
        if (await db.InventoryIdentityCorrections.AnyAsync(x => x.SourceFruitProfileId == id || x.TargetFruitProfileId == id, ct))
            throw new InvalidOperationException("ORAS has a prior identity correction requiring individual review.");

        var entries = new List<EntityEntry> { db.Entry(profile) };
        async Task<List<T>> Read<T>(IQueryable<T> query) where T : class
        {
            var rows = await query.IgnoreQueryFilters().ToListAsync(ct);
            entries.AddRange(rows.Select(x => db.Entry((object)x)));
            return rows;
        }
        var receipts = await Read(db.Receipts.Where(x => x.FruitProfileId == id));
        await Read(db.ReceiptVarietyLines.Where(x => x.FruitProfileId == id));
        var ledger = await Read(db.RoomInventoryAdjustments.Where(x => x.FruitProfileId == id));
        await Read(db.RoomDepletions.Where(x => x.FruitProfileId == id));
        await Read(db.RoomInventoryLosses.Where(x => x.FruitProfileId == id));
        await Read(db.RoomTransfers.Where(x => x.FruitProfileId == id));
        var runs = await Read(db.BinsRunEntries.Where(x => x.FruitProfileId == id || x.ReportingFruitProfileIdSnapshot == id));
        if (runs.Any(x => x.FruitProfileId != id || x.ReportingFruitProfileIdSnapshot != null && x.ReportingFruitProfileIdSnapshot != id))
            throw new InvalidOperationException("Conflicting run reporting identities require individual review.");
        var segments = await Read(db.TreatmentLineageSegments.Where(x => x.FruitProfileId == id));
        await Read(db.RoomTreatmentApplicationSources.Where(x => x.FruitProfileId == id));
        await Read(db.InterCrewTransfers.Where(x => x.FruitProfileId == id));
        await Read(db.OutsideWarehouseTransfers.Where(x => x.FruitProfileId == id));
        await Read(db.ProcessorShipmentLines.Where(x => x.FruitProfileId == id));
        var expectations = await Read(db.RunExpectationSources.Where(x => x.FruitProfileId == id));
        var projections = await Read(db.RunProjectionSources.Where(x => x.FruitProfileId == id));
        var segmentIds = segments.Select(x => x.Id).ToArray();
        var receiptIds = receipts.Select(x => x.Id).ToArray();
        var runIds = runs.Select(x => x.Id).ToArray();
        // Include links as well as keys: never silently omit a malformed historical key.
        var keyMarker = "|" + id + "|";
        var movements = await Read(db.TreatmentLineageMovements.Where(x => x.IdentityKey.Contains(keyMarker)
            || segmentIds.Contains(x.SourceSegmentId ?? 0) || segmentIds.Contains(x.DestinationSegmentId ?? 0)
            || receiptIds.Contains(x.ReceiptId ?? 0) || runIds.Contains(x.BinsRunEntryId ?? 0)));
        var unrelatedMovements = movements.Where(x => !KeyBelongsTo(x.IdentityKey, id)
            && !segmentIds.Contains(x.SourceSegmentId ?? 0) && !segmentIds.Contains(x.DestinationSegmentId ?? 0)
            && !receiptIds.Contains(x.ReceiptId ?? 0) && !runIds.Contains(x.BinsRunEntryId ?? 0)).ToArray();
        entries.RemoveAll(e => unrelatedMovements.Contains(e.Entity));
        var actualIds = runs.Where(x => x.ActualRunId != null).Select(x => x.ActualRunId!.Value).ToArray();
        var projectionIds = projections.Select(x => x.RunProjectionId).ToArray();
        var expectationIds = expectations.Select(x => x.RunExpectationId).ToArray();
        // A finalized packout contains signed/exported report snapshots. Do not silently
        // change its meaning without a reviewed report supersession workflow.
        if (await db.PackoutRuns.AnyAsync(x => runIds.Contains(x.BinsRunEntryId ?? 0)
            || actualIds.Contains(x.ActualRunId ?? 0) || projectionIds.Contains(x.RunProjectionId ?? 0)
            || expectationIds.Contains(x.RunExpectationId ?? 0), ct))
            throw new InvalidOperationException("ORAS packout reports require an individually reviewed historical report correction.");
        if (projections.Any(x => !string.IsNullOrEmpty(x.InventoryKey)))
            throw new InvalidOperationException("Saved ORAS run projections require individual review before correcting their selection keys.");

        var edits = new List<Edit>();
        void Change(EntityEntry entry, string field, object value)
        {
            var property = entry.Metadata.FindProperty(field);
            if (property != null && !Equals(entry.Property(field).CurrentValue, value)) edits.Add(new(entry, field, value));
        }
        foreach (var entry in entries)
        {
            if (entry.Entity is FruitProfile)
            {
                Change(entry, "ProductionType", "Organic");
                Change(entry, "IsOrganic", true);
                continue;
            }
            foreach (var code in new[] { "VarietyCode", "VarietyCodeSnapshot", "ReportingVarietyCodeSnapshot" })
                if (entry.Metadata.FindProperty(code) != null && entry.Property(code).CurrentValue is string value
                    && !string.IsNullOrWhiteSpace(value) && !OrasProductDefinition.Applies(value))
                    throw new InvalidOperationException("Conflicting ORAS historical variety references require individual review.");
            Change(entry, "ProductionTypeSnapshot", "Organic");
            Change(entry, "IsOrganicSnapshot", true);
            if (entry.Metadata.FindProperty("IdentityKey") != null)
                Change(entry, "IdentityKey", CorrectKey((string)entry.Property("IdentityKey").CurrentValue!, id));
            // Legacy redundant status aliases must not become a separate inventory bucket.
            foreach (var status in new[] { "InventoryStatus", "InventoryStatusSnapshot" })
                if (entry.Metadata.FindProperty(status) != null
                    && string.Equals(entry.Property(status).CurrentValue as string, "Conventional", StringComparison.OrdinalIgnoreCase))
                    Change(entry, status, "Organic");
        }
        foreach (var entry in edits.Select(x => x.Entry).Distinct().ToArray())
            if (entry.Metadata.FindProperty("ConcurrencyVersion") != null)
                Change(entry, "ConcurrencyVersion", checked((long)entry.Property("ConcurrencyVersion").CurrentValue! + 1));

        var ordered = entries.OrderBy(x => x.Metadata.GetTableName(), StringComparer.Ordinal).ThenBy(Id, StringComparer.Ordinal).ToArray();
        var snapshot = JsonSerializer.Serialize(ordered.Select(x => new
        {
            Table = x.Metadata.GetTableName(),
            Id = Id(x),
            Values = x.Properties.OrderBy(p => p.Metadata.Name, StringComparer.Ordinal).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)
        }));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        var changes = edits.Select(e => new OrasDefinitionFieldChange(e.Entry.Metadata.GetTableName()!, Id(e.Entry),
            e.Field, e.Entry.Property(e.Field).CurrentValue, e.After)).ToArray();
        return new(new(id, fingerprint, changes, receipts.Count, ledger.Count,
            receipts.Where(x => !x.IsDeleted && !x.IsTransferReceipt).Sum(x => x.BinCount), ledger.Sum(x => x.ChangeAmount)), edits);
    }

    private static string Id(EntityEntry e) => string.Join("/", e.Metadata.FindPrimaryKey()!.Properties.Select(p => e.Property(p.Name).CurrentValue));
    private static bool KeyBelongsTo(string key, int id) => key.Split('|') is var p && p.Length == 9 && p[2] == id.ToString();
    private static string CorrectKey(string key, int id)
    {
        var parts = key.Split('|');
        if (parts.Length != 9 || parts[2] != id.ToString() || !OrasProductDefinition.Applies(parts[5])
            || !new[] { "CONVENTIONAL", "ORGANIC" }.Contains(parts[6].ToUpperInvariant()))
            throw new InvalidOperationException("An ORAS historical identity key is inconsistent. Individual review is required.");
        if (parts[8].Equals("Conventional", StringComparison.OrdinalIgnoreCase)) parts[8] = "";
        parts[6] = "ORGANIC";
        parts[7] = "True";
        return string.Join('|', parts);
    }
}
