using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

/// <summary>Interpret audited reversals for current custody/restoration without
/// rewriting the historical dispatch or treatment application links.</summary>
public sealed record InventoryEffectiveTreatment(string Signature, string State, ImmutableArray<long> ApplicationIds)
{
    public static InventoryEffectiveTreatment Read(string signature, string state, ImmutableArray<long> ids, IEnumerable<InventoryApplicationEvidence> applications)
    {
        var original = new InventoryEffectiveTreatment(signature, state, ids);
        var known = applications.ToDictionary(x => x.Id);
        if (state != "Confirmed" || ids.IsEmpty || signature != "u|a:" + string.Join(',', ids.Order()) || ids.Any(id => !known.ContainsKey(id))) return original;
        var active = ids.Where(id => known[id].ReversedAt == null).Order().ToImmutableArray();
        return new(active.IsEmpty ? "u" : "u|a:" + string.Join(',', active), active.IsEmpty ? "Untreated" : "Confirmed", active);
    }
}
