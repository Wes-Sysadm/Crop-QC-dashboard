using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

/// <summary>Final persistence guard, shared by Web, API and imported receipts.</summary>
internal static class OrasReceivingGuard
{
    private static int[] CheckProfiles(CropQcDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<FruitProfile>().Where(x => x.State is EntityState.Added or EntityState.Modified))
            if (!OrasProductDefinition.IsValid(entry.Entity)) throw new InvalidOperationException(OrasProductDefinition.Error);
        return db.ChangeTracker.Entries<Receipt>().Where(x => x.State == EntityState.Added
                || x.State == EntityState.Modified && x.Property(p => p.FruitProfileId).IsModified)
            .Select(x => x.Entity.FruitProfileId)
            .Concat(db.ChangeTracker.Entries<ReceiptVarietyLine>().Where(x => x.State is EntityState.Added or EntityState.Modified)
                .Select(x => x.Entity.FruitProfileId)).Distinct().ToArray();
    }
    private static void Validate(IEnumerable<FruitProfile> profiles)
    {
        if (profiles.Any(x => !OrasProductDefinition.IsValid(x))) throw new InvalidOperationException(OrasProductDefinition.Error);
    }
    internal static void Check(CropQcDbContext db)
    {
        var ids = CheckProfiles(db);
        if (ids.Length != 0) Validate(db.FruitProfiles.Where(x => ids.Contains(x.Id)).ToList());
    }
    internal static async Task CheckAsync(CropQcDbContext db, CancellationToken ct)
    {
        var ids = CheckProfiles(db);
        if (ids.Length != 0) Validate(await db.FruitProfiles.Where(x => ids.Contains(x.Id)).ToListAsync(ct));
    }
}
