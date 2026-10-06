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
        typeof(RoomTransfer), typeof(RoomDepletion), typeof(RoomInventoryLoss),
        typeof(ReceiptInventoryOverride), typeof(InventoryIdentityCorrection), typeof(InventoryCommandRecord), typeof(ActualRunRevision)
    ];
    private static readonly HashSet<string> ReceiptInventoryFields =
    [nameof(Receipt.BinCount), nameof(Receipt.CropYear), nameof(Receipt.WarehouseId), nameof(Receipt.RoomId),
     nameof(Receipt.GrowerLotId), nameof(Receipt.FruitProfileId), nameof(Receipt.LotCode), nameof(Receipt.GrowerNumber),
     nameof(Receipt.IsTransferReceipt), nameof(Receipt.TransferCompletedAt), nameof(Receipt.ReceivedAt), nameof(Receipt.IsDeleted), nameof(Receipt.ReceiptType)];

    internal static void Check(CropQcDbContext db)
    {
        if (db.CanonicalCommandTransaction) return;
        bool? requiresCanonical = null;
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (PhysicalEntities.Contains(entry.Metadata.ClrType)
                || entry.Entity is BinsRunEntry && (entry.State != EntityState.Modified || entry.Properties.Any(p => p.IsModified
                    && p.Metadata.Name is nameof(BinsRunEntry.BinsRun) or nameof(BinsRunEntry.WarehouseId) or nameof(BinsRunEntry.RoomId)
                    or nameof(BinsRunEntry.CropYear) or nameof(BinsRunEntry.GrowerLotId) or nameof(BinsRunEntry.FruitProfileId) or nameof(BinsRunEntry.LotNumber)
                    or nameof(BinsRunEntry.InventoryAdjustmentId) or nameof(BinsRunEntry.IsReversed) or nameof(BinsRunEntry.ReversesBinsRunEntryId)
                    or nameof(BinsRunEntry.ActualRunId) or nameof(BinsRunEntry.ActualRunRevisionId) or nameof(BinsRunEntry.TransactionType)))
                || entry.Entity is OutsideWarehouseTransfer or ProcessorShipmentLine && entry.State != EntityState.Unchanged
                || entry.Entity is ProcessorShipment && (entry.State != EntityState.Modified || entry.Property(nameof(ProcessorShipment.ReversedAt)).IsModified)
                || entry.Entity is ActualRun && (entry.State != EntityState.Modified || entry.Properties.Any(p => p.IsModified
                    && p.Metadata.Name is nameof(ActualRun.Status) or nameof(ActualRun.CurrentRevisionNumber)))
                || entry.Entity is InterCrewTransfer && (entry.State != EntityState.Modified || entry.Properties.Any(p => p.IsModified
                    && p.Metadata.Name is nameof(InterCrewTransfer.BinsLoaded) or nameof(InterCrewTransfer.BinsReceived)
                    or nameof(InterCrewTransfer.SourceWarehouseId) or nameof(InterCrewTransfer.SourceRoomId)
                    or nameof(InterCrewTransfer.DestinationWarehouseId) or nameof(InterCrewTransfer.DestinationRoomId)
                    or nameof(InterCrewTransfer.ReceivedAt) or nameof(InterCrewTransfer.ReversedAt)
                    or nameof(InterCrewTransfer.CropYear) or nameof(InterCrewTransfer.GrowerLotId) or nameof(InterCrewTransfer.FruitProfileId)
                    or nameof(InterCrewTransfer.LotNumberSnapshot) or nameof(InterCrewTransfer.InventoryStatusSnapshot))
                    || entry.Property(nameof(InterCrewTransfer.Status)).IsModified
                        && !(entry.Property(nameof(InterCrewTransfer.Status)).OriginalValue as string == InterCrewTransferStatuses.ReceivedNeedsReview
                            && entry.Property(nameof(InterCrewTransfer.Status)).CurrentValue as string == InterCrewTransferStatuses.Received))
                || entry.Entity is Receipt receipt
                    && (receipt.ReceiptType == "Truck receipt" || entry.Property(nameof(Receipt.ReceiptType)).OriginalValue as string == "Truck receipt")
                    && (!receipt.IsTransferReceipt || receipt.TransferCompletedAt != null
                        || entry.Property(nameof(Receipt.TransferCompletedAt)).OriginalValue != null
                        || entry.Property(nameof(Receipt.IsTransferReceipt)).IsModified)
                    && (entry.State != EntityState.Modified || entry.Properties.Any(p => p.IsModified && ReceiptInventoryFields.Contains(p.Metadata.Name))))
            {
                if (requiresCanonical ??= RequiresCanonicalWrites(db)) throw new InventoryWriterNotMigratedException(entry.Metadata.ClrType.Name);
            }
        }
    }

    // Once a canonical command is committed, disabling configuration must not
    // allow old writers to reinterpret its physical history. No cached false:
    // a long-lived scope must observe activation by another process.
    internal static bool RequiresCanonicalWrites(CropQcDbContext db) => db.CanonicalInventoryEnabled || db.InventoryCommands.AsNoTracking().Any();
}

public static class CanonicalInventoryServices
{
    public static IServiceCollection AddCanonicalInventoryCommands(this IServiceCollection services, bool enabled)
    {
        services.AddSingleton(new CanonicalInventoryMode(enabled));
        services.AddScoped<IDbContextFactory<CropQcDbContext>, CommandContextFactory>();
        services.AddScoped<InventoryCommandExecutor>();
        services.AddScoped<IInventoryCommandExecutor, ActivatedExecutor>();
        services.AddScoped<CanonicalReceivingService>();
        return services;
    }

    private sealed class ActivatedExecutor(CanonicalInventoryMode mode, InventoryCommandExecutor executor) : IInventoryCommandExecutor
    {
        public Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default) => mode.Enabled
            ? executor.ExecuteAsync(command, cancellationToken)
            : Task.FromResult(new InventoryCommandResult(InventoryCommandStatus.Blocked, command.OperationKey, "Canonical inventory commands are not activated.", []));
    }

    private sealed class CommandContextFactory(DbContextOptions<CropQcDbContext> options, CanonicalInventoryMode mode)
        : IDbContextFactory<CropQcDbContext>
    {
        public CropQcDbContext CreateDbContext() => new(options, mode);
    }
}
