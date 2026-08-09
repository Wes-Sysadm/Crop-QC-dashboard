using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public interface IInventoryDiagnosticAcknowledgmentService
{
    Task<InventoryDiagnosticOverviewViewModel> GetOverviewAsync(
        RoomInventoryReconciliationFilter filter,
        CancellationToken cancellationToken);

    Task<InventoryDiagnosticMutationResult> DismissAsync(
        string diagnosticKey,
        string reason,
        string changedByEmail,
        CancellationToken cancellationToken);

    Task<InventoryDiagnosticMutationResult> RestoreAsync(
        string diagnosticKey,
        string changedByEmail,
        CancellationToken cancellationToken);
}

public sealed record InventoryDiagnosticMutationResult(bool Succeeded, string? Error, string? DiagnosticLabel = null);

public sealed class InventoryDiagnosticAcknowledgmentService(
    CropQcDbContext dbContext,
    IInventoryDeductionInvariantService inventoryInvariant,
    IBusinessTimeService businessTime) : IInventoryDiagnosticAcknowledgmentService
{
    public const string DiagnosticType = "InventoryDeductionInvariant";
    public const int MinimumReasonLength = 10;
    private const int MaximumReasonLength = 500;

    public async Task<InventoryDiagnosticOverviewViewModel> GetOverviewAsync(
        RoomInventoryReconciliationFilter filter,
        CancellationToken cancellationToken)
    {
        var current = await GetCurrentDiagnosticsAsync(filter, cancellationToken);
        var currentByKey = current.ToDictionary(x => x.DiagnosticKey, StringComparer.Ordinal);
        var acknowledgments = (await AcknowledgmentQuery(filter)
                .OrderByDescending(x => x.Id)
                .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.DismissedAt)
            .ThenByDescending(x => x.Id)
            .ToList();
        var activeAcknowledgmentKeys = acknowledgments
            .Select(x => x.DiagnosticKey)
            .ToHashSet(StringComparer.Ordinal);

        return new InventoryDiagnosticOverviewViewModel
        {
            ActiveDiagnostics = current
                .Where(x => x.BlocksDeployment || !activeAcknowledgmentKeys.Contains(x.DiagnosticKey))
                .OrderByDescending(x => x.BlocksDeployment)
                .ThenBy(x => x.AdjustmentId)
                .ThenBy(x => x.Code)
                .ToList(),
            DismissedDiagnostics = acknowledgments
                .Select(x => ToDismissedViewModel(x, currentByKey.GetValueOrDefault(x.DiagnosticKey)))
                .ToList()
        };
    }

    public async Task<InventoryDiagnosticMutationResult> DismissAsync(
        string diagnosticKey,
        string reason,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeKey(diagnosticKey);
        var normalizedReason = reason?.Trim() ?? "";
        if (normalizedKey is null)
        {
            return Failed("The selected inventory diagnostic is invalid. Refresh the page and try again.");
        }
        if (normalizedReason.Length < MinimumReasonLength || normalizedReason.Length > MaximumReasonLength)
        {
            return Failed($"Dismissal reason must be between {MinimumReasonLength} and {MaximumReasonLength} characters.");
        }

        var current = (await GetCurrentDiagnosticsAsync(new RoomInventoryReconciliationFilter(), cancellationToken))
            .SingleOrDefault(x => x.DiagnosticKey == normalizedKey);
        if (current is null)
        {
            return Failed("That diagnostic no longer matches the current ledger evidence. Refresh before dismissing it.");
        }
        if (current.BlocksDeployment)
        {
            return Failed("Blocking or new-format inventory diagnostics cannot be dismissed.");
        }

        var changedBy = await FindUserAsync(changedByEmail, cancellationToken);
        if (changedBy is null)
        {
            return Failed("The signed-in administrator could not be resolved.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var existing = await dbContext.InventoryDiagnosticAcknowledgments
            .SingleOrDefaultAsync(x => x.DiagnosticKey == normalizedKey, cancellationToken);
        var before = existing is null ? null : AcknowledgmentSnapshot(existing);
        if (existing?.IsActive == true)
        {
            return Failed("That diagnostic is already dismissed.");
        }

        var now = businessTime.UtcNow;
        if (existing is null)
        {
            existing = new InventoryDiagnosticAcknowledgment
            {
                DiagnosticKey = current.DiagnosticKey,
                DiagnosticType = current.DiagnosticType,
                DiagnosticCode = current.Code,
                DiagnosticMessage = current.Message,
                RoomInventoryAdjustmentId = current.AdjustmentId,
                InvariantVersion = current.InvariantVersion,
                Reason = normalizedReason,
                DiagnosticSnapshotJson = current.DiagnosticSnapshotJson,
                DismissedByUserId = changedBy.Id,
                DismissedByEmail = changedBy.Email,
                DismissedAt = now,
                IsActive = true
            };
            dbContext.InventoryDiagnosticAcknowledgments.Add(existing);
        }
        else
        {
            existing.DiagnosticType = current.DiagnosticType;
            existing.DiagnosticCode = current.Code;
            existing.DiagnosticMessage = current.Message;
            existing.RoomInventoryAdjustmentId = current.AdjustmentId;
            existing.InvariantVersion = current.InvariantVersion;
            existing.Reason = normalizedReason;
            existing.DiagnosticSnapshotJson = current.DiagnosticSnapshotJson;
            existing.DismissedByUserId = changedBy.Id;
            existing.DismissedByEmail = changedBy.Email;
            existing.DismissedAt = now;
            existing.IsActive = true;
            existing.RestoredByUserId = null;
            existing.RestoredByEmail = null;
            existing.RestoredAt = null;
        }

        dbContext.AuditLogs.Add(Audit(
            changedBy.Id,
            "dismiss",
            existing.DiagnosticKey,
            before,
            AcknowledgmentSnapshot(existing),
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new InventoryDiagnosticMutationResult(true, null, $"{current.Code} for adjustment #{current.AdjustmentId}");
    }

    public async Task<InventoryDiagnosticMutationResult> RestoreAsync(
        string diagnosticKey,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeKey(diagnosticKey);
        if (normalizedKey is null)
        {
            return Failed("The selected inventory diagnostic is invalid. Refresh the page and try again.");
        }

        var changedBy = await FindUserAsync(changedByEmail, cancellationToken);
        if (changedBy is null)
        {
            return Failed("The signed-in administrator could not be resolved.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var acknowledgment = await dbContext.InventoryDiagnosticAcknowledgments
            .SingleOrDefaultAsync(x => x.DiagnosticKey == normalizedKey && x.IsActive, cancellationToken);
        if (acknowledgment is null)
        {
            return Failed("That dismissed diagnostic was not found or has already been restored.");
        }

        var before = AcknowledgmentSnapshot(acknowledgment);
        var now = businessTime.UtcNow;
        acknowledgment.IsActive = false;
        acknowledgment.RestoredByUserId = changedBy.Id;
        acknowledgment.RestoredByEmail = changedBy.Email;
        acknowledgment.RestoredAt = now;
        dbContext.AuditLogs.Add(Audit(
            changedBy.Id,
            "restore",
            acknowledgment.DiagnosticKey,
            before,
            AcknowledgmentSnapshot(acknowledgment),
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new InventoryDiagnosticMutationResult(
            true,
            null,
            $"{acknowledgment.DiagnosticCode} for adjustment #{acknowledgment.RoomInventoryAdjustmentId}");
    }

    private async Task<IReadOnlyList<InventoryDiagnosticViewModel>> GetCurrentDiagnosticsAsync(
        RoomInventoryReconciliationFilter filter,
        CancellationToken cancellationToken)
    {
        var readiness = await inventoryInvariant.VerifyReadinessAsync(cancellationToken);
        var issues = readiness.Issues;
        if (issues.Count == 0) return [];

        var adjustmentIds = issues.Select(x => x.AdjustmentId).Distinct().ToList();
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .Where(x => adjustmentIds.Contains(x.Id))
            .Where(x => filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomId == filter.RoomId)
            .Where(x => string.IsNullOrWhiteSpace(filter.Lot) || x.LotNumber.Contains(filter.Lot))
            .Where(x => string.IsNullOrWhiteSpace(filter.Variety)
                || (x.VarietyCode != null && x.VarietyCode.Contains(filter.Variety))
                || (x.FruitProfile != null && x.FruitProfile.VarietyCode.Contains(filter.Variety)))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return issues
            .Where(x => adjustments.ContainsKey(x.AdjustmentId))
            .Select(x => BuildDiagnostic(x, adjustments[x.AdjustmentId]))
            .ToList();
    }

    private IQueryable<InventoryDiagnosticAcknowledgment> AcknowledgmentQuery(RoomInventoryReconciliationFilter filter) =>
        dbContext.InventoryDiagnosticAcknowledgments.AsNoTracking()
            .Include(x => x.RoomInventoryAdjustment).ThenInclude(x => x.Warehouse)
            .Include(x => x.RoomInventoryAdjustment).ThenInclude(x => x.Room)
            .Include(x => x.RoomInventoryAdjustment).ThenInclude(x => x.FruitProfile)
            .Where(x => x.IsActive)
            .Where(x => filter.WarehouseId == null || x.RoomInventoryAdjustment.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomInventoryAdjustment.RoomId == filter.RoomId)
            .Where(x => string.IsNullOrWhiteSpace(filter.Lot) || x.RoomInventoryAdjustment.LotNumber.Contains(filter.Lot))
            .Where(x => string.IsNullOrWhiteSpace(filter.Variety)
                || (x.RoomInventoryAdjustment.VarietyCode != null && x.RoomInventoryAdjustment.VarietyCode.Contains(filter.Variety))
                || (x.RoomInventoryAdjustment.FruitProfile != null
                    && x.RoomInventoryAdjustment.FruitProfile.VarietyCode.Contains(filter.Variety)));

    private static InventoryDiagnosticViewModel BuildDiagnostic(
        InventoryDeductionIssue issue,
        RoomInventoryAdjustment adjustment)
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            fingerprintVersion = 1,
            diagnosticType = DiagnosticType,
            code = issue.Code,
            message = issue.Message,
            blocksDeployment = issue.BlocksDeployment,
            adjustmentId = adjustment.Id,
            invariantVersion = adjustment.InventoryInvariantVersion,
            adjustment.ReceiptId,
            adjustment.WarehouseId,
            adjustment.RoomId,
            adjustment.GrowerLotId,
            adjustment.FruitProfileId,
            adjustment.CropYear,
            lotNumber = NormalizeIdentity(adjustment.LotNumber),
            varietyCode = NormalizeIdentity(adjustment.VarietyCode),
            inventoryStatus = NormalizeIdentity(adjustment.InventoryStatus),
            adjustment.OldBinCount,
            adjustment.ChangeAmount,
            adjustment.NewBinCount,
            adjustmentType = NormalizeIdentity(adjustment.AdjustmentType),
            source = NormalizeIdentity(adjustment.Source),
            adjustment.ActualRunId,
            adjustment.RoomTransferId,
            adjustment.ReceiptInventoryOverrideId,
            inventoryOperationKey = NormalizeIdentity(adjustment.InventoryOperationKey)
        });
        return new InventoryDiagnosticViewModel
        {
            DiagnosticKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))),
            DiagnosticType = DiagnosticType,
            Code = issue.Code,
            Message = issue.Message,
            AdjustmentId = adjustment.Id,
            InvariantVersion = issue.InvariantVersion,
            BlocksDeployment = issue.BlocksDeployment,
            Facility = adjustment.Warehouse.Code,
            WarehouseId = adjustment.WarehouseId,
            Room = adjustment.Room.CropQcRoomName ?? adjustment.Room.DisplayName ?? adjustment.Room.Code,
            RoomId = adjustment.RoomId,
            Lot = adjustment.LotNumber,
            Variety = adjustment.FruitProfile?.VarietyCode ?? adjustment.VarietyCode ?? "",
            AdjustmentAt = adjustment.AdjustmentAt,
            DiagnosticSnapshotJson = snapshot
        };
    }

    private static DismissedInventoryDiagnosticViewModel ToDismissedViewModel(
        InventoryDiagnosticAcknowledgment acknowledgment,
        InventoryDiagnosticViewModel? current) => new()
        {
            DiagnosticKey = acknowledgment.DiagnosticKey,
            DiagnosticType = acknowledgment.DiagnosticType,
            Code = acknowledgment.DiagnosticCode,
            Message = acknowledgment.DiagnosticMessage,
            AdjustmentId = acknowledgment.RoomInventoryAdjustmentId,
            InvariantVersion = acknowledgment.InvariantVersion,
            BlocksDeployment = false,
            Facility = acknowledgment.RoomInventoryAdjustment.Warehouse.Code,
            WarehouseId = acknowledgment.RoomInventoryAdjustment.WarehouseId,
            Room = acknowledgment.RoomInventoryAdjustment.Room.CropQcRoomName
                ?? acknowledgment.RoomInventoryAdjustment.Room.DisplayName
                ?? acknowledgment.RoomInventoryAdjustment.Room.Code,
            RoomId = acknowledgment.RoomInventoryAdjustment.RoomId,
            Lot = acknowledgment.RoomInventoryAdjustment.LotNumber,
            Variety = acknowledgment.RoomInventoryAdjustment.FruitProfile?.VarietyCode
                ?? acknowledgment.RoomInventoryAdjustment.VarietyCode
                ?? "",
            AdjustmentAt = acknowledgment.RoomInventoryAdjustment.AdjustmentAt,
            DiagnosticSnapshotJson = acknowledgment.DiagnosticSnapshotJson,
            Reason = acknowledgment.Reason,
            DismissedByEmail = acknowledgment.DismissedByEmail,
            DismissedAt = acknowledgment.DismissedAt,
            StillMatchesCurrentDiagnostic = current is not null
        };

    private async Task<User?> FindUserAsync(string changedByEmail, CancellationToken cancellationToken)
    {
        var normalized = changedByEmail.Trim().ToUpperInvariant();
        return await dbContext.Users.SingleOrDefaultAsync(
            x => x.Email.ToUpper() == normalized,
            cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static InventoryDiagnosticMutationResult Failed(string error) => new(false, error);

    private static string? NormalizeKey(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is { Length: 64 } && normalized.All(Uri.IsHexDigit) ? normalized : null;
    }

    private static string NormalizeIdentity(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

    private static string AcknowledgmentSnapshot(InventoryDiagnosticAcknowledgment value) => JsonSerializer.Serialize(new
    {
        value.Id,
        value.DiagnosticKey,
        value.DiagnosticType,
        value.DiagnosticCode,
        value.DiagnosticMessage,
        value.RoomInventoryAdjustmentId,
        value.InvariantVersion,
        value.Reason,
        value.DismissedByUserId,
        value.DismissedByEmail,
        value.DismissedAt,
        value.IsActive,
        value.RestoredByUserId,
        value.RestoredByEmail,
        value.RestoredAt,
        value.DiagnosticSnapshotJson
    });

    private static AuditLog Audit(
        int userId,
        string action,
        string diagnosticKey,
        string? before,
        string after,
        DateTimeOffset createdAt) => new()
        {
            UserId = userId,
            Action = action,
            EntityName = "inventory-diagnostic-acknowledgment",
            EntityKey = diagnosticKey,
            BeforeValuesJson = before,
            AfterValuesJson = after,
            SourceApplication = "CropQc.Web",
            CreatedAt = createdAt
        };
}
