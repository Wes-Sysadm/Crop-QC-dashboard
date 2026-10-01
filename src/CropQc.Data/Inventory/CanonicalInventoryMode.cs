using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CropQc.Data.Inventory;

/// <summary>One process-wide deployment boundary. Never controls persisted command identity.</summary>
public sealed record CanonicalInventoryMode(bool Enabled = false);

public sealed class InventoryWriterNotMigratedException(string entity) : InvalidOperationException(
    $"This inventory operation has not been migrated to the canonical inventory engine ({entity}).");

internal static class CanonicalInventoryWriteGuard
{
    private static readonly HashSet<Type> PhysicalEntities =
    [
        typeof(RoomInventoryAdjustment), typeof(TreatmentLineageSegment), typeof(TreatmentLineageMovement),
        typeof(TreatmentLineageSegmentApplication), typeof(RoomTreatmentApplication), typeof(RoomTreatmentApplicationSource),
        typeof(RoomTransfer), typeof(RoomDepletion), typeof(RoomInventoryLoss), typeof(BinsRunEntry),
        typeof(ReceiptInventoryOverride), typeof(InventoryIdentityCorrection), typeof(InventoryCommandRecord)
    ];
    private static readonly HashSet<string> ReceiptInventoryFields =
    [nameof(Receipt.BinCount), nameof(Receipt.CropYear), nameof(Receipt.WarehouseId), nameof(Receipt.RoomId),
     nameof(Receipt.GrowerLotId), nameof(Receipt.FruitProfileId), nameof(Receipt.LotCode), nameof(Receipt.GrowerNumber),
     nameof(Receipt.IsTransferReceipt), nameof(Receipt.TransferCompletedAt), nameof(Receipt.ReceivedAt), nameof(Receipt.IsDeleted), nameof(Receipt.ReceiptType)];

    internal static void Check(CropQcDbContext db)
    {
        if (!db.CanonicalInventoryEnabled || db.CanonicalCommandTransaction) return;
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (PhysicalEntities.Contains(entry.Metadata.ClrType)
                || entry.Entity is Receipt receipt
                    && (receipt.ReceiptType == "Truck receipt" || entry.Property(nameof(Receipt.ReceiptType)).OriginalValue as string == "Truck receipt")
                    && (!receipt.IsTransferReceipt || receipt.TransferCompletedAt != null
                        || entry.Property(nameof(Receipt.TransferCompletedAt)).OriginalValue != null
                        || entry.Property(nameof(Receipt.IsTransferReceipt)).IsModified)
                    && (entry.State != EntityState.Modified || entry.Properties.Any(p => p.IsModified && ReceiptInventoryFields.Contains(p.Metadata.Name))))
                throw new InventoryWriterNotMigratedException(entry.Metadata.ClrType.Name);
        }
    }
}

public static class CanonicalInventoryServices
{
    public static IServiceCollection AddCanonicalInventoryCommands(this IServiceCollection services, bool enabled)
    {
        services.AddSingleton(new CanonicalInventoryMode(enabled));
        services.AddScoped<IDbContextFactory<CropQcDbContext>, CommandContextFactory>();
        services.AddScoped<IInventoryCommandExecutor, InventoryCommandExecutor>();
        services.AddScoped<CanonicalReceivingService>();
        return services;
    }

    private sealed class CommandContextFactory(DbContextOptions<CropQcDbContext> options, CanonicalInventoryMode mode)
        : IDbContextFactory<CropQcDbContext>
    {
        public CropQcDbContext CreateDbContext() => new(options, mode);
    }
}
