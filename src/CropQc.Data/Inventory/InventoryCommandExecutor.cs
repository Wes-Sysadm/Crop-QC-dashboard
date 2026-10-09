using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CropQc.Data.Inventory;

/// <summary>Optional test instrumentation. No implementation is registered in either application.</summary>
public interface IInventoryCommandObserver
{
    Task AtAsync(string stage, int attempt, CancellationToken ct);
}

/// <summary>Dormant engine: no Web/API operational caller is registered or migrated in Phase 2.</summary>
public sealed partial class InventoryCommandExecutor(IDbContextFactory<CropQcDbContext> contexts,
    IInventoryCommandObserver? observer = null, ICanonicalRunExpectationWriter? runExpectations = null, ILogger<InventoryCommandExecutor>? logger = null) : IInventoryCommandExecutor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class Rejection(InventoryCommandStatus status, string message) : Exception(message)
    { public InventoryCommandStatus Status { get; } = status; }
    private static void Require(bool condition, string message, InventoryCommandStatus status = InventoryCommandStatus.Blocked)
    { if (!condition) throw new Rejection(status, message); }

    public async Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default)
    {
        command = command with { EffectiveAt = command.EffectiveAt.ToUniversalTime() };
        var key = command.OperationKey;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 60 || command.ActorId <= 0 || !Enum.IsDefined(command.Kind)
            || string.IsNullOrWhiteSpace(command.Reason) || command.Lines.IsDefault
            || command.Lines.IsEmpty && command.Kind is not (InventoryCommandKind.ReceiveStock or InventoryCommandKind.ActivateReceiptInventory or InventoryCommandKind.ReverseLoss or InventoryCommandKind.CancelRun or InventoryCommandKind.ReverseRunEntry or InventoryCommandKind.ReverseDepletion
                or InventoryCommandKind.CorrectReceiptQuantity or InventoryCommandKind.VoidReceipt or InventoryCommandKind.UpdateReceiptMetadata or InventoryCommandKind.TreatmentReversal or InventoryCommandKind.ImportBaseline or InventoryCommandKind.CorrectReceiptLocation or InventoryCommandKind.CorrectReceiptIdentity or InventoryCommandKind.CorrectOrasDefinition) || command.Lines.Length > 100
            || command.EffectiveAt > DateTimeOffset.UtcNow || command.Lines.Any(x => x.Quantity <= 0
                || !x.Source.Identity.IsComplete || string.IsNullOrWhiteSpace(x.Source.ExpectedFingerprint)
                || string.IsNullOrWhiteSpace(x.TreatmentSignature)))
            return new(InventoryCommandStatus.InvalidIntent, key, "Invalid command intent or missing read watermark.", []);
        // Preserve submitted order: line order is part of complete business intent.
        var intent = JsonSerializer.Serialize(command, Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(intent)));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            Require(db.Database.IsNpgsql(), "Canonical writes require PostgreSQL Serializable transactions.");
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            db.CanonicalCommandTransaction = true;
            try
            {
                if (command.Kind == InventoryCommandKind.CorrectOrasDefinition) await LockOrasDefinitionAsync(db, cancellationToken);
                var prior = await db.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, cancellationToken);
                if (prior != null)
                {
                    Require(prior.IntentHash == hash && prior.IntentJson == intent, "Operation key was used with different intent.", InventoryCommandStatus.Conflict);
                    return JsonSerializer.Deserialize<InventoryCommandResult>(prior.ResultJson, Json)! with { Status = InventoryCommandStatus.Replayed, Attempts = attempt };
                }
                Require(await db.Users.AnyAsync(x => x.Id == command.ActorId && x.IsActive, cancellationToken), "Unknown actor.");
                ValidateShape(command);
                var loader = new InventoryEvidenceLoader(db);
                var resolved = new List<(InventoryCommandLine Line, InventoryPositionEvidence Evidence, InventoryAvailabilityResult Result)>();
                var readAt = DateTimeOffset.UtcNow;
                foreach (var line in command.Kind is InventoryCommandKind.ReviseRun or InventoryCommandKind.ReviseLegacyDump ? [] : command.Lines)
                {
                    var location = line.Source.Location;
                    if (location.Custody == InventoryCustody.Room)
                        Require(await db.Rooms.AnyAsync(x => x.Id == location.RoomId && x.WarehouseId == location.WarehouseId
                            && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, cancellationToken), "Source is unavailable, sealed or mismatched.");
                    var evidence = await loader.LoadAsync(new(location.WarehouseId, location.RoomId is int room ? [room] : [],
                        location.Custody, location.CustodyRecordId), readAt, cancellationToken);
                    var e = evidence.Positions.SingleOrDefault(x => x.Identity.Key == line.Source.Identity.Key
                        && x.Location.CustodyRecordId == location.CustodyRecordId);
                    Require(e != null, "Inventory position no longer exists.", InventoryCommandStatus.Stale);
                    var r = InventoryAvailabilityResolver.Resolve(e!, InventoryCommandPolicy.Requirements(command.Kind, line));
                    Require(!r.Blockers.Any(x => x.Code == InventoryBlockerCode.StaleRead), "Read fingerprint changed.", InventoryCommandStatus.Stale);
                    Require(line.Source.ExpectedVersions.IsDefaultOrEmpty || line.Source.ExpectedVersions.All(x => r.Watermark.Versions.Contains(x)),
                        "Expected entity version changed.", InventoryCommandStatus.Stale);
                    Require(r.IsOperable && (command.Kind == InventoryCommandKind.ManualStockAddition ? r.AuthoritativeQuantity >= 0
                        : line.AdjustmentDirection == InventoryAdjustmentDirection.Increase ? r.AvailableQuantity > 0 : r.AvailableQuantity >= line.Quantity),
                        $"Canonical evidence or available quantity blocks {command.Kind}: {string.Join(',', r.Blockers.Select(x => x.Code))}.");
                    Require(!resolved.Any(x => x.Result.PositionKey == r.PositionKey && x.Line.TreatmentSignature == line.TreatmentSignature),
                        "Duplicate treatment slice in a command.", InventoryCommandStatus.InvalidIntent);
                    resolved.Add((line, e!, r));
                }
                var destinations = new List<(InventoryCommandLine Line, InventoryPositionEvidence Evidence, InventoryAvailabilityResult Result)>();
                foreach (var source in resolved.Where(x => x.Line.Destination != null))
                {
                    var dest = source.Line.Destination!;
                    Require(!resolved.Any(x => x.Result.Location.Custody == InventoryCustody.Room && x.Result.Location.RoomId == dest.RoomId
                        && x.Result.Identity.Key == source.Result.Identity.Key), "A command cannot also consume its own destination.");
                    var batch = await loader.LoadAsync(new(dest.WarehouseId, [dest.RoomId]), readAt, cancellationToken);
                    var e = batch.Positions.SingleOrDefault(x => x.Identity.Key == source.Result.Identity.Key);
                    if (e == null || destinations.Any(x => x.Result.Identity.Key == e.Identity.Key && x.Result.Location.RoomId == dest.RoomId)) continue;
                    var r = InventoryAvailabilityResolver.Resolve(e, new());
                    Require(r.IsOperable, CanonicalInventoryMessages.PlacementBlocker(
                        $"{command.Kind} {command.PhysicalParentId?.ToString() ?? command.OperationKey}", e.Location.Name, r));
                    destinations.Add((source.Line, e, r));
                }
                if (command.Kind == InventoryCommandKind.TreatmentAssignment)
                    foreach (var room in resolved.Select(x => x.Result.Location.RoomId!.Value).Distinct())
                    {
                        var batch = await loader.LoadAsync(new(null, [room]), readAt, cancellationToken);
                        Require(batch.Positions.Where(x => x.AuthoritativeQuantity != 0).All(x => resolved.Any(r => r.Result.PositionKey == InventoryAvailabilityResolver.Resolve(x, new()).PositionKey)),
                            "Room treatment must include every occupied inventory position.");
                    }
                await Stage("Resolved", db, attempt, cancellationToken);
                var factory = new CanonicalProjectionFactory(db);
                foreach (var item in resolved.Concat(destinations).Where(x => x.Evidence.Location.Custody == InventoryCustody.Room).DistinctBy(x => x.Result.PositionKey))
                {
                    if (command.Kind == InventoryCommandKind.ManualStockAddition && item.Result.RawProjectionQuantity == item.Result.AuthoritativeQuantity) continue;
                    await NormalizePositionAsync(db, factory, command, item.Evidence, item.Result, readAt, attempt, cancellationToken);
                }
                await Stage("Normalized", db, attempt, cancellationToken);
                var effects = command.Kind switch
                {
                    InventoryCommandKind.ReceiveStock or InventoryCommandKind.ActivateReceiptInventory => await ReceiveStockAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.ReverseLoss => await ReverseLossAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.CancelRun or InventoryCommandKind.ReverseRunEntry or InventoryCommandKind.ReverseDepletion => await ReverseRunAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.ReviseRun => await ReviseRunAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.ReviseLegacyDump => await ReviseLegacyDumpAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.ReturnTransitAllocation => await ReturnTransitAllocationAsync(db, factory, command, resolved.Single().Result, readAt, attempt, cancellationToken),
                    InventoryCommandKind.ManualStockAddition => await AddManualStockAsync(db, factory, command, resolved.Single().Result, readAt, attempt, cancellationToken),
                    InventoryCommandKind.CorrectReceiptQuantity or InventoryCommandKind.VoidReceipt => await ChangeReceiptQuantityAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.UpdateReceiptMetadata => await UpdateReceiptMetadataAsync(db, command, readAt, cancellationToken),
                    InventoryCommandKind.ImportBaseline => await ImportBaselineAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.CorrectReceiptLocation => await CorrectReceiptLocationAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.CorrectOrasDefinition => await CorrectOrasDefinitionAsync(db, command, readAt, cancellationToken),
                    InventoryCommandKind.CorrectReceiptIdentity => await CorrectReceiptIdentityAsync(db, factory, command, readAt, attempt, cancellationToken),
                    InventoryCommandKind.TreatmentReversal => await ReverseCurrentTreatmentAsync(db, factory, command, resolved.Select(x => x.Result).ToArray(), readAt, cancellationToken),
                    _ => await ApplyAsync(db, factory, command, resolved, readAt, attempt, cancellationToken)
                };
                AddAudit(db, command, "CanonicalInventoryCommand", key, new { intent, hash }, effects, readAt);
                await Stage("OperationAudit", db, attempt, cancellationToken);
                var result = new InventoryCommandResult(InventoryCommandStatus.Committed, key, "Command committed atomically.", effects, attempt);
                db.InventoryCommands.Add(new()
                {
                    OperationKey = key,
                    IntentHash = hash,
                    IntentJson = intent,
                    ResultJson = JsonSerializer.Serialize(result, Json),
                    ActorId = command.ActorId,
                    CommittedAt = readAt,
                    ReversesOperationKey = command.Kind is InventoryCommandKind.Return or InventoryCommandKind.ReopenTransfer ? command.OriginalOperationKey : null
                });
                await db.SaveChangesAsync(cancellationToken);
                await Stage("BeforeCommit", db, attempt, cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                // A PostgreSQL serialization failure can abort COMMIT itself.
                // That transaction is already complete; another Rollback throws
                // and hides the retryable server error. Disposal rolls back only
                // pending work and also handles an already-completed transaction.
                await tx.DisposeAsync();
                db.ChangeTracker.Clear();
                PostgresException? pg = null;
                for (Exception? cause = ex; cause != null; cause = cause.InnerException)
                    if (cause is PostgresException serverError) { pg = serverError; break; }
                if (pg?.SqlState == "55P03") return new(InventoryCommandStatus.Conflict, key, "Inventory is being changed. Refresh the correction preview and retry.", [], attempt);
                if (pg?.SqlState is "40001" or "40P01" or "23505")
                {
                    if (attempt < 3) continue; // fresh DbContext and COMPLETE intent on retry
                    return new(InventoryCommandStatus.RetryRequired, key, "Concurrent command; refresh or retry the same intent.", [], attempt);
                }
                if (ex is Rejection rejection) return new(rejection.Status, key, rejection.Message, [], attempt);
                if (ex is DbUpdateConcurrencyException) return new(InventoryCommandStatus.Stale, key, "Entity version changed.", [], attempt);
                if (ex is InvalidOperationException)
                {
                    if (command.Kind != InventoryCommandKind.CorrectReceiptLocation)
                        return new(InventoryCommandStatus.Blocked, key, ex.Message, [], attempt);
                    logger?.LogError(ex, "Receipt location correction {OperationKey} failed for receipt {ReceiptId}; transaction rolled back.", key, command.ReceiptLocation?.ReceiptId);
                    return new(InventoryCommandStatus.Blocked, key, "Receipt inventory could not be verified. No changes were saved. Refresh and contact an administrator if this continues.", [], attempt);
                }
                throw;
            }
        }
        throw new InvalidOperationException("Unreachable retry state.");
    }

    private async Task NormalizePositionAsync(CropQcDbContext db, CanonicalProjectionFactory factory, InventoryCommand command,
        InventoryPositionEvidence evidence, InventoryAvailabilityResult result, DateTimeOffset readAt, int attempt, CancellationToken cancellationToken)
    {
        var plan = InventoryNormalizationPlanner.Plan(evidence, result);
        if (plan == null) return;
        var ids = plan.Changes.Select(x => x.Id).ToArray();
        var segments = await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        Require(segments.Count == plan.Changes.Length, "Normalization row disappeared.", InventoryCommandStatus.Stale);
        foreach (var change in plan.Changes)
        {
            var row = segments.Single(x => x.Id == change.Id);
            Require(row.CurrentBins == change.BeforeQuantity && row.ConcurrencyVersion == change.BeforeVersion
                && row.Disposition == change.BeforeDisposition && row.TreatmentSignature == change.Signature
                && row.IdentityKey == change.RawIdentityKey && row.TreatmentState == change.TreatmentState && row.UpdatedAt == change.BeforeUpdatedAt
                && row.ReceiptId == change.ReceiptId && row.Applications.Select(x => x.RoomTreatmentApplicationId).Order().SequenceEqual(change.ApplicationIds.Order()),
                "Normalization plan no longer matches exact rows.", InventoryCommandStatus.Stale);
            CanonicalProjectionFactory.Retire(row, command.OperationKey, readAt);
        }
        await db.SaveChangesAsync(cancellationToken); // release current-only uniqueness, still inside outer transaction
        var replacement = await factory.CurrentAsync(result.Identity, result.Location.WarehouseId,
            result.Location.RoomId!.Value, "u", "Untreated", plan.ReplacementReceiptId, [], readAt, cancellationToken);
        Require(replacement.CurrentBins == 0, "Replacement projection unexpectedly exists.");
        replacement.CurrentBins = plan.ReplacementQuantity;
        await db.SaveChangesAsync(cancellationToken);
        plan = plan with { ReplacementProjectionId = replacement.Id };
        AddAudit(db, command, "CanonicalInventoryNormalization", result.PositionKey, plan.Changes, plan, readAt);
        await Stage("NormalizationAudit", db, attempt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var balance = await PhysicalAsync(db, result.Identity, result.Location.WarehouseId, result.Location.RoomId.Value, cancellationToken);
        Require(balance == result.AuthoritativeQuantity, "Normalization changed physical inventory.");
    }

    private Task Stage(string name, CropQcDbContext db, int attempt, CancellationToken ct) => observer?.AtAsync(name, attempt, ct) ?? Task.CompletedTask;
    private static void AddAudit(CropQcDbContext db, InventoryCommand command, string action, string entityKey, object before, object after, DateTimeOffset now) =>
        db.AuditLogs.Add(new()
        {
            UserId = command.ActorId,
            Action = action,
            EntityName = "CanonicalInventory",
            EntityKey = entityKey,
            BeforeValuesJson = JsonSerializer.Serialize(before, Json),
            AfterValuesJson = JsonSerializer.Serialize(after, Json),
            SourceApplication = "CanonicalInventory/v1",
            CreatedAt = now
        });
    private static async Task<int> PhysicalAsync(CropQcDbContext db, InventoryIdentity identity, int warehouse, int room, CancellationToken ct) =>
        (await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(warehouse, [room], ct))
        .Where(x => x.CropYear == identity.CropYear && x.GrowerLotId == identity.GrowerLotId && x.FruitProfileId == identity.FruitProfileId
            && x.Lot == identity.Lot && InventoryStatusIdentity.Normalize(x.InventoryStatus, x.ProductionType) == InventoryStatusIdentity.Normalize(identity.Status, identity.ProductionType)).Sum(x => x.CurrentBins);
}
