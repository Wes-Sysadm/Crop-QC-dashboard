using System.Data;
using System.Globalization;
using System.Security.Claims;
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

public sealed record ProcessorShipmentWriteResult(bool Success, bool AlreadyApplied, long? ShipmentId, string? Error);

public interface IProcessorShipmentService
{
    Task<ProcessorShipmentPageViewModel> GetPageAsync(ProcessorShipmentForm? form, bool review, string? from, string? to, int? processorId, int? warehouseId, CancellationToken cancellationToken);
    Task<ProcessorShipmentWriteResult> CreateAsync(ProcessorShipmentForm form, CancellationToken cancellationToken);
    Task<ProcessorShipmentDetailViewModel?> GetDetailsAsync(long id, CancellationToken cancellationToken);
    Task<string?> CorrectPriceAsync(ProcessorShipmentPriceCorrectionForm form, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(ProcessorShipmentReversalForm form, CancellationToken cancellationToken);
}

public sealed class ProcessorShipmentService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IRoomTreatmentService roomTreatments,
    IProcessorTreatmentLineageService treatmentLineage,
    IInventoryDeductionInvariantService invariant,
    IUserAccessService access,
    IHttpContextAccessor httpContextAccessor,
    IBusinessTimeService businessTime) : IProcessorShipmentService
{
    private const string AuditSource = "CropQc.Web processor shipment workflow";
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<ProcessorShipmentPageViewModel> GetPageAsync(
        ProcessorShipmentForm? form,
        bool review,
        string? from,
        string? to,
        int? processorId,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        form ??= new ProcessorShipmentForm { ShippedAt = businessTime.NowPacific.DateTime };
        if (form.ShippedAt == default) form.ShippedAt = businessTime.NowPacific.DateTime;
        if (string.IsNullOrWhiteSpace(form.OperationKey)) form.OperationKey = Guid.NewGuid().ToString("N");
        var options = await GetInventoryOptionsAsync(cancellationToken);
        var selected = ResolveSelectedLines(form, options, out var error);
        if (review) error ??= ValidateHeader(form);
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var canCreate = await access.HasAccessAsync(principal, ApplicationAreas.ProcessorShipments, PageAccessLevel.Edit, cancellationToken);
        var canAdmin = await access.HasAccessAsync(principal, ApplicationAreas.ProcessorShipments, PageAccessLevel.Admin, cancellationToken);

        var historyQuery = dbContext.ProcessorShipments.AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.Warehouse)
            .Include(x => x.CreatedByUser)
            .AsQueryable();
        if (processorId is not null) historyQuery = historyQuery.Where(x => x.ProcessorId == processorId);
        if (warehouseId is not null) historyQuery = historyQuery.Where(x => x.Lines.Any(y => y.WarehouseId == warehouseId));
        if (DateTime.TryParse(from, out var fromDate))
            historyQuery = historyQuery.Where(x => x.ShippedAt >= businessTime.PacificLocalToUtc(fromDate.Date));
        if (DateTime.TryParse(to, out var toDate))
            historyQuery = historyQuery.Where(x => x.ShippedAt < businessTime.PacificLocalToUtc(toDate.Date.AddDays(1)));
        var history = await historyQuery.OrderByDescending(x => x.ShippedAt).Take(500)
            .Select(x => new ProcessorShipmentHistoryViewModel(
                x.Id, x.ShippedAt, x.ProcessorNameSnapshot, x.Lines.Sum(y => y.BinsSent),
                x.SaleRate, x.PricingBasis, x.Currency, x.ReferenceNumber,
                x.CreatedByUser.DisplayName ?? x.CreatedByUser.Email, x.ReversedAt != null))
            .ToListAsync(cancellationToken);
        var processors = await dbContext.Processors.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new { Option = new ProcessorOptionViewModel(x.Id, x.Name, x.Code), x.IsActive })
            .ToListAsync(cancellationToken);

        return new ProcessorShipmentPageViewModel
        {
            Form = form,
            Processors = processors.Where(x => x.IsActive).Select(x => x.Option).ToList(),
            ReportProcessors = processors.Select(x => x.Option).ToList(),
            Inventory = options,
            History = history,
            ReviewLines = selected.Select(x => ToLineView(x.Option, x.Form.BinsSent, form.SaleRate ?? 0, form.PricingBasis)).ToList(),
            IsReview = review && error is null,
            CanCreate = canCreate,
            CanAdmin = canAdmin,
            Error = error,
            FilterFrom = from,
            FilterTo = to,
            FilterProcessorId = processorId,
            FilterWarehouseId = warehouseId
        };
    }

    public async Task<ProcessorShipmentWriteResult> CreateAsync(ProcessorShipmentForm form, CancellationToken cancellationToken)
    {
        if (!form.ConfirmedReview) return new(false, false, null, "Review the Processor Shipment before confirming it.");
        var headerError = ValidateHeader(form);
        if (headerError is not null) return new(false, false, null, headerError);
        var operationKey = Normalize(form.OperationKey);
        if (operationKey is null || operationKey.Length > 150) return new(false, false, null, "The shipment operation key is invalid. Refresh and retry.");
        var existing = await dbContext.ProcessorShipments.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
        if (existing is not null) return new(true, true, existing.Id, null);
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return new(false, false, null, "The current active user could not be resolved.");

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var processor = await dbContext.Processors.SingleOrDefaultAsync(x => x.Id == form.ProcessorId && x.IsActive, cancellationToken);
            if (processor is null) return new(false, false, null, "Select an active Processor.");
            var options = await GetInventoryOptionsAsync(cancellationToken);
            var selected = ResolveSelectedLines(form, options, out var error);
            if (error is not null) return new(false, false, null, error);

            var now = businessTime.UtcNow;
            var shipment = new ProcessorShipment
            {
                OperationKey = operationKey,
                ProcessorId = processor.Id,
                ProcessorNameSnapshot = processor.Name,
                ShippedAt = businessTime.PacificLocalToUtc(form.ShippedAt),
                OriginalSaleRate = form.SaleRate!.Value,
                OriginalPricingBasis = form.PricingBasis,
                SaleRate = form.SaleRate.Value,
                PricingBasis = form.PricingBasis,
                Currency = form.Currency.Trim().ToUpperInvariant(),
                ReferenceNumber = Normalize(form.ReferenceNumber),
                Notes = Normalize(form.Notes),
                CreatedByUserId = actor.Id,
                CreatedAt = now
            };
            foreach (var item in selected)
            {
                var option = item.Option;
                shipment.Lines.Add(new ProcessorShipmentLine
                {
                    WarehouseId = option.WarehouseId,
                    RoomId = option.RoomId,
                    CropYear = option.CropYear,
                    ReceiptId = option.ReceiptId,
                    SourceInventoryAdjustmentId = option.SourceInventoryAdjustmentId,
                    GrowerLotId = option.GrowerLotId,
                    FruitProfileId = option.FruitProfileId,
                    GrowerNumberSnapshot = option.GrowerNumber,
                    GrowerNameSnapshot = option.GrowerName,
                    LotNumberSnapshot = option.LotNumber,
                    VarietyCodeSnapshot = option.VarietyCode,
                    ProductionTypeSnapshot = option.ProductionType,
                    IsOrganicSnapshot = option.IsOrganic,
                    InventoryStatusSnapshot = option.InventoryStatus,
                    TreatmentStateSnapshot = option.TreatmentState,
                    TreatmentSignatureSnapshot = option.TreatmentSignature,
                    TreatmentSummarySnapshot = option.TreatmentSummary,
                    BinsSent = item.Form.BinsSent,
                    PoundsPerBinSnapshot = option.PoundsPerBin
                });
            }
            dbContext.ProcessorShipments.Add(shipment);
            await dbContext.SaveChangesAsync(cancellationToken);

            var balances = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < selected.Count; i++)
            {
                var item = selected[i];
                var option = item.Option;
                var line = shipment.Lines.ElementAt(i);
                var key = AggregateKey(option);
                var authoritativeSnapshot = await FindSnapshotAsync(option, cancellationToken);
                if (authoritativeSnapshot is null) throw new InvalidOperationException("The exact source inventory is no longer available. Refresh and retry.");
                if (!balances.TryGetValue(key, out var oldBalance)) oldBalance = authoritativeSnapshot.CurrentBins;
                var adjustment = new RoomInventoryAdjustment
                {
                    CropYear = option.CropYear,
                    ReceiptId = option.ReceiptId,
                    WarehouseId = option.WarehouseId,
                    RoomId = option.RoomId,
                    GrowerLotId = option.GrowerLotId,
                    FruitProfileId = option.FruitProfileId,
                    GrowerName = option.GrowerName,
                    LotNumber = option.LotNumber,
                    VarietyCode = option.VarietyCode,
                    OldBinCount = oldBalance,
                    ChangeAmount = -item.Form.BinsSent,
                    NewBinCount = oldBalance - item.Form.BinsSent,
                    AdjustmentType = ProcessorShipmentAdjustmentTypes.Shipment,
                    Source = "Processor Shipment",
                    InventoryStatus = option.InventoryStatus,
                    Reason = $"Sent to {shipment.ProcessorNameSnapshot}",
                    Notes = shipment.ReferenceNumber,
                    AdjustmentAt = shipment.ShippedAt,
                    CreatedByUserId = actor.Id,
                    CreatedAt = now,
                    InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
                    InventoryOperationKey = $"processor-shipment:{operationKey}:line:{line.Id}",
                    ProcessorShipmentLine = line
                };
                balances[key] = adjustment.NewBinCount;
                dbContext.RoomInventoryAdjustments.Add(adjustment);
                await invariant.ValidateBeforeCommitAsync(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                var lineageResult = await treatmentLineage.MoveToProcessorAsync(
                    authoritativeSnapshot, option.TreatmentSignature, item.Form.BinsSent,
                    $"processor-shipment:{operationKey}:line:{line.Id}:treatment", line.Id,
                    shipment.ShippedAt, actor.Id, cancellationToken);
                if (!lineageResult.Success) throw new InvalidOperationException(lineageResult.Error);
            }

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = "ProcessorShipmentCreated",
                EntityName = nameof(ProcessorShipment),
                EntityKey = shipment.Id.ToString(CultureInfo.InvariantCulture),
                AfterValuesJson = JsonSerializer.Serialize(new { shipment.ProcessorId, shipment.ProcessorNameSnapshot, shipment.SaleRate, shipment.PricingBasis, shipment.Currency, LineCount = shipment.Lines.Count, TotalBins = shipment.Lines.Sum(x => x.BinsSent), shipment.ReferenceNumber }, AuditJson),
                SourceApplication = AuditSource,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(true, false, shipment.Id, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return new(false, false, null, ex.Message);
        }
    }

    public async Task<ProcessorShipmentDetailViewModel?> GetDetailsAsync(long id, CancellationToken cancellationToken)
    {
        var shipment = await dbContext.ProcessorShipments.AsNoTracking()
            .Include(x => x.CreatedByUser).Include(x => x.Lines).ThenInclude(x => x.Warehouse)
            .Include(x => x.Lines).ThenInclude(x => x.Room)
            .Include(x => x.PriceCorrections).ThenInclude(x => x.CorrectedByUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (shipment is null) return null;
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        return new ProcessorShipmentDetailViewModel
        {
            Id = shipment.Id,
            Processor = shipment.ProcessorNameSnapshot,
            ShippedAt = shipment.ShippedAt,
            OriginalSaleRate = shipment.OriginalSaleRate,
            OriginalPricingBasis = shipment.OriginalPricingBasis,
            SaleRate = shipment.SaleRate,
            PricingBasis = shipment.PricingBasis,
            Currency = shipment.Currency,
            ReferenceNumber = shipment.ReferenceNumber,
            Notes = shipment.Notes,
            CreatedBy = shipment.CreatedByUser.DisplayName ?? shipment.CreatedByUser.Email,
            IsReversed = shipment.ReversedAt is not null,
            ReversedAt = shipment.ReversedAt,
            ReversalReason = shipment.ReversalReason,
            CanAdmin = await access.HasAccessAsync(principal, ApplicationAreas.ProcessorShipments, PageAccessLevel.Admin, cancellationToken),
            Lines = shipment.Lines.Select(x => ToLineView(x, shipment.SaleRate, shipment.PricingBasis)).ToList(),
            Corrections = shipment.PriceCorrections.OrderBy(x => x.CorrectedAt).Select(x => new ProcessorShipmentPriceCorrectionViewModel(
                x.OriginalSaleRate, x.OriginalPricingBasis, x.CorrectedSaleRate, x.CorrectedPricingBasis,
                x.Reason, x.CorrectedByUser.DisplayName ?? x.CorrectedByUser.Email, x.CorrectedAt)).ToList()
        };
    }

    public async Task<string?> CorrectPriceAsync(ProcessorShipmentPriceCorrectionForm form, CancellationToken cancellationToken)
    {
        if (form.SaleRate is null || form.SaleRate <= 0) return "Sale Rate must be greater than zero.";
        if (!ProcessorPricingBases.IsValid(form.PricingBasis)) return "Pricing Basis must be Per Ton or Per Bin.";
        if (string.IsNullOrWhiteSpace(form.Reason)) return "A price-correction reason is required.";
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return "The current active user could not be resolved.";
        var key = Normalize(form.OperationKey);
        if (key is null) return "The correction operation key is invalid.";
        if (await dbContext.ProcessorShipmentPriceCorrections.AnyAsync(x => x.OperationKey == key, cancellationToken)) return null;
        var shipment = await dbContext.ProcessorShipments.SingleOrDefaultAsync(x => x.Id == form.ShipmentId, cancellationToken);
        if (shipment is null) return "Processor Shipment was not found.";
        if (shipment.ReversedAt is not null) return "A reversed Processor Shipment cannot receive a price correction.";
        var now = businessTime.UtcNow;
        var correction = new ProcessorShipmentPriceCorrection
        {
            ProcessorShipmentId = shipment.Id,
            OperationKey = key,
            OriginalSaleRate = shipment.SaleRate,
            OriginalPricingBasis = shipment.PricingBasis,
            CorrectedSaleRate = form.SaleRate.Value,
            CorrectedPricingBasis = form.PricingBasis,
            Reason = form.Reason.Trim(),
            CorrectedByUserId = actor.Id,
            CorrectedAt = now
        };
        shipment.SaleRate = correction.CorrectedSaleRate;
        shipment.PricingBasis = correction.CorrectedPricingBasis;
        shipment.ConcurrencyVersion++;
        dbContext.ProcessorShipmentPriceCorrections.Add(correction);
        dbContext.AuditLogs.Add(new AuditLog { UserId = actor.Id, Action = "ProcessorShipmentPriceCorrected", EntityName = nameof(ProcessorShipment), EntityKey = shipment.Id.ToString(), BeforeValuesJson = JsonSerializer.Serialize(new { correction.OriginalSaleRate, correction.OriginalPricingBasis }, AuditJson), AfterValuesJson = JsonSerializer.Serialize(new { correction.CorrectedSaleRate, correction.CorrectedPricingBasis, correction.Reason, InventoryDelta = 0, TreatmentDelta = 0 }, AuditJson), SourceApplication = AuditSource, CreatedAt = now });
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ReverseAsync(ProcessorShipmentReversalForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Reason)) return "A physical-reversal reason is required.";
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return "The current active user could not be resolved.";
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var shipment = await dbContext.ProcessorShipments.Include(x => x.Lines).ThenInclude(x => x.InventoryAdjustments)
                .SingleOrDefaultAsync(x => x.Id == form.ShipmentId, cancellationToken);
            if (shipment is null) return "Processor Shipment was not found.";
            if (shipment.ReversedAt is not null) return "Processor Shipment was already reversed.";
            var operationKey = Normalize(form.OperationKey);
            if (operationKey is null) return "The reversal operation key is invalid.";
            var now = businessTime.UtcNow;
            foreach (var line in shipment.Lines.OrderBy(x => x.Id))
            {
                var original = line.InventoryAdjustments.SingleOrDefault(x => x.AdjustmentType == ProcessorShipmentAdjustmentTypes.Shipment && x.ChangeAmount == -line.BinsSent);
                if (original is null || line.InventoryAdjustments.Any(x => x.AdjustmentType == ProcessorShipmentAdjustmentTypes.Reversal))
                    throw new InvalidOperationException("The exact Processor Shipment ledger history cannot be deterministically reversed.");
                var current = await FindSnapshotAsync(line, cancellationToken);
                var oldBalance = current?.CurrentBins ?? 0;
                dbContext.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
                {
                    CropYear = line.CropYear,
                    ReceiptId = line.ReceiptId,
                    WarehouseId = line.WarehouseId,
                    RoomId = line.RoomId,
                    GrowerLotId = line.GrowerLotId,
                    FruitProfileId = line.FruitProfileId,
                    GrowerName = line.GrowerNameSnapshot,
                    LotNumber = line.LotNumberSnapshot,
                    VarietyCode = line.VarietyCodeSnapshot,
                    OldBinCount = oldBalance,
                    ChangeAmount = line.BinsSent,
                    NewBinCount = oldBalance + line.BinsSent,
                    AdjustmentType = ProcessorShipmentAdjustmentTypes.Reversal,
                    Source = "Processor Shipment Reversal",
                    InventoryStatus = line.InventoryStatusSnapshot,
                    Reason = form.Reason.Trim(),
                    AdjustmentAt = now,
                    CreatedByUserId = actor.Id,
                    CreatedAt = now,
                    InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
                    InventoryOperationKey = $"processor-shipment-reversal:{operationKey}:line:{line.Id}",
                    ProcessorShipmentLine = line
                });
                await invariant.ValidateBeforeCommitAsync(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                var lineage = await treatmentLineage.ReverseProcessorMovementAsync($"processor-shipment-reversal:{operationKey}:line:{line.Id}", line.Id, now, actor.Id, cancellationToken);
                if (!lineage.Success || lineage.MovementId is null) throw new InvalidOperationException(lineage.Error ?? "The exact treatment lineage could not be restored.");
            }
            shipment.ReversedAt = now;
            shipment.ReversedByUserId = actor.Id;
            shipment.ReversalReason = form.Reason.Trim();
            shipment.ConcurrencyVersion++;
            dbContext.AuditLogs.Add(new AuditLog { UserId = actor.Id, Action = "ProcessorShipmentReversed", EntityName = nameof(ProcessorShipment), EntityKey = shipment.Id.ToString(), AfterValuesJson = JsonSerializer.Serialize(new { Reason = shipment.ReversalReason, LineCount = shipment.Lines.Count, TotalBins = shipment.Lines.Sum(x => x.BinsSent) }, AuditJson), SourceApplication = AuditSource, CreatedAt = now });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return ex.Message;
        }
    }

    private async Task<List<ProcessorInventoryOptionViewModel>> GetInventoryOptionsAsync(CancellationToken cancellationToken)
    {
        var snapshots = (await ledger.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .GroupBy(RoomTreatmentService.SelectionLookupKey, StringComparer.Ordinal)
            .Select(x => ConsolidateSnapshots(x.ToList()))
            .ToList();
        var adjustmentIds = snapshots.Select(x => x.LatestAdjustmentId).Distinct().ToList();
        var receiptIds = await dbContext.RoomInventoryAdjustments.AsNoTracking().Where(x => adjustmentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ReceiptId }).ToDictionaryAsync(x => x.Id, x => x.ReceiptId, cancellationToken);
        var weightConfig = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => x.Key == RunProjectionSettings.ApplePoundsPerBinKey || x.Key == RunProjectionSettings.PearPoundsPerBinKey)
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        var result = new List<ProcessorInventoryOptionViewModel>();
        foreach (var snapshot in snapshots)
        {
            var selections = (await roomTreatments.GetSelectionsAsync(snapshot, cancellationToken))
                .Where(x => x.CurrentBins > 0)
                .GroupBy(x => new { x.IdentityKey, x.TreatmentSignature, x.TreatmentState, x.Label, x.ReceiptId })
                .Select(x => new TreatmentSegmentSelection(
                    x.Key.IdentityKey, x.Key.TreatmentSignature, x.Key.TreatmentState, x.Sum(y => y.CurrentBins), x.Key.Label, x.Key.ReceiptId))
                .ToList();
            var pounds = PoundsPerBin(snapshot.FruitType, weightConfig);
            foreach (var selection in selections)
            {
                var receiptId = selection.ReceiptId ?? receiptIds.GetValueOrDefault(snapshot.LatestAdjustmentId);
                var key = SourceKey(snapshot, selection.IdentityKey, selection.TreatmentSignature, receiptId);
                result.Add(new ProcessorInventoryOptionViewModel(
                    key, snapshot.WarehouseId, snapshot.Facility, snapshot.RoomId, snapshot.Room,
                    snapshot.CropYear, snapshot.GrowerLotId, snapshot.FruitProfileId, snapshot.Grower,
                    snapshot.GrowerNumber, snapshot.Lot, snapshot.Variety, snapshot.VarietyName,
                    snapshot.FruitType, snapshot.ProductionType, snapshot.IsOrganic, snapshot.InventoryStatus,
                    selection.TreatmentState, selection.TreatmentSignature, selection.Label,
                    selection.CurrentBins, snapshot.LatestAdjustmentId,
                    receiptId, pounds));
            }
        }
        return result.OrderBy(x => x.Facility).ThenBy(x => x.Room).ThenBy(x => x.GrowerNumber).ThenBy(x => x.VarietyName).ThenBy(x => x.TreatmentSummary).ToList();
    }

    private static List<(ProcessorShipmentLineForm Form, ProcessorInventoryOptionViewModel Option)> ResolveSelectedLines(
        ProcessorShipmentForm form, IReadOnlyList<ProcessorInventoryOptionViewModel> options, out string? error)
    {
        error = null;
        var active = form.Lines.Where(x => x.BinsSent != 0).ToList();
        if (active.Count == 0) { error = "Add at least one exact inventory source line."; return []; }
        if (active.Any(x => x.BinsSent <= 0)) { error = "Bins Sent must be greater than zero on every source line."; return []; }
        if (active.GroupBy(x => x.SourceKey).Any(x => x.Count() > 1)) { error = "The same exact treatment source cannot be added twice."; return []; }
        var lookup = options.ToDictionary(x => x.SourceKey, StringComparer.Ordinal);
        var result = new List<(ProcessorShipmentLineForm, ProcessorInventoryOptionViewModel)>();
        foreach (var line in active)
        {
            if (!lookup.TryGetValue(line.SourceKey, out var option)) { error = "A selected source is no longer available. Refresh and retry."; return []; }
            if (line.ExpectedAvailableBins != option.AvailableBins) { error = "Source inventory changed after this page loaded. Refresh before retrying."; return []; }
            if (line.BinsSent > option.AvailableBins) { error = $"Only {option.AvailableBins} bins remain in the selected treatment segment."; return []; }
            result.Add((line, option));
        }
        return result;
    }

    private static string? ValidateHeader(ProcessorShipmentForm form)
    {
        if (form.ProcessorId is null) return "Select a Processor.";
        if (form.SaleRate is null || form.SaleRate <= 0) return "Sale Rate must be greater than zero.";
        if (!ProcessorPricingBases.IsValid(form.PricingBasis)) return "Pricing Basis must be Per Ton or Per Bin.";
        var currency = Normalize(form.Currency)?.ToUpperInvariant();
        if (currency is null || currency.Length != 3 || !currency.All(char.IsLetter)) return "Currency must be a three-letter code such as USD.";
        if (form.ShippedAt == default) return "Shipment date and time are required.";
        return null;
    }

    private async Task<RoomInventoryLedgerSnapshot?> FindSnapshotAsync(ProcessorInventoryOptionViewModel option, CancellationToken cancellationToken)
    {
        var matches = (await ledger.GetSnapshotsAsync(option.WarehouseId, [option.RoomId], cancellationToken)).Where(x =>
            x.CropYear == option.CropYear && x.GrowerLotId == option.GrowerLotId && x.FruitProfileId == option.FruitProfileId
            && Same(x.GrowerNumber, option.GrowerNumber) && Same(x.Lot, option.LotNumber) && Same(x.Variety, option.VarietyCode)
            && Same(x.ProductionType, option.ProductionType) && x.IsOrganic == option.IsOrganic
            && Same(x.InventoryStatus, option.InventoryStatus)).ToList();
        return matches.Count == 0 ? null : ConsolidateSnapshots(matches);
    }

    private async Task<RoomInventoryLedgerSnapshot?> FindSnapshotAsync(ProcessorShipmentLine line, CancellationToken cancellationToken)
    {
        var matches = (await ledger.GetSnapshotsAsync(line.WarehouseId, [line.RoomId], cancellationToken)).Where(x =>
            x.CropYear == line.CropYear && x.GrowerLotId == line.GrowerLotId && x.FruitProfileId == line.FruitProfileId
            && Same(x.GrowerNumber, line.GrowerNumberSnapshot) && Same(x.Lot, line.LotNumberSnapshot) && Same(x.Variety, line.VarietyCodeSnapshot)
            && Same(x.ProductionType, line.ProductionTypeSnapshot) && x.IsOrganic == line.IsOrganicSnapshot
            && Same(x.InventoryStatus, line.InventoryStatusSnapshot)).ToList();
        return matches.Count == 0 ? null : ConsolidateSnapshots(matches);
    }

    private static ProcessorShipmentLineViewModel ToLineView(ProcessorInventoryOptionViewModel x, int bins, decimal rate, string basis)
    {
        var pounds = x.PoundsPerBin is null ? null : bins * x.PoundsPerBin;
        var tons = pounds / 2000m;
        return new(0, x.ReceiptId, x.GrowerLotId, x.Facility, x.Room, x.GrowerNumber, x.GrowerName, x.LotNumber, x.VarietyName,
            x.ProductionType, OrganicLabel(x.IsOrganic, x.ProductionType), x.InventoryStatus, x.TreatmentSummary,
            bins, x.PoundsPerBin, pounds, tons, basis == ProcessorPricingBases.PerTon ? tons * rate : bins * rate);
    }

    private static ProcessorShipmentLineViewModel ToLineView(ProcessorShipmentLine x, decimal rate, string basis)
    {
        var pounds = x.PoundsPerBinSnapshot is null ? null : x.BinsSent * x.PoundsPerBinSnapshot;
        var tons = pounds / 2000m;
        return new(x.Id, x.ReceiptId, x.GrowerLotId, x.Warehouse.Code, x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
            x.GrowerNumberSnapshot, x.GrowerNameSnapshot, x.LotNumberSnapshot, x.VarietyCodeSnapshot,
            x.ProductionTypeSnapshot, OrganicLabel(x.IsOrganicSnapshot, x.ProductionTypeSnapshot), x.InventoryStatusSnapshot ?? "",
            x.TreatmentSummarySnapshot, x.BinsSent, x.PoundsPerBinSnapshot, pounds, tons,
            basis == ProcessorPricingBases.PerTon ? tons * rate : x.BinsSent * rate);
    }

    private static decimal? PoundsPerBin(string fruitType, IReadOnlyDictionary<string, string> settings)
    {
        var key = fruitType.StartsWith("Apple", StringComparison.OrdinalIgnoreCase) ? RunProjectionSettings.ApplePoundsPerBinKey
            : fruitType.StartsWith("Pear", StringComparison.OrdinalIgnoreCase) ? RunProjectionSettings.PearPoundsPerBinKey : null;
        return key is not null && settings.TryGetValue(key, out var value)
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var pounds) && pounds > 0 ? pounds : null;
    }

    private static string SourceKey(RoomInventoryLedgerSnapshot x, string identityKey, string signature, long? receiptId)
    {
        var raw = $"{x.WarehouseId}|{x.RoomId}|{identityKey}|{signature}|{receiptId?.ToString() ?? "-"}|{x.LatestAdjustmentId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string AggregateKey(ProcessorInventoryOptionViewModel x) =>
        $"{x.WarehouseId}|{x.RoomId}|{x.CropYear}|{x.GrowerLotId}|{x.FruitProfileId}|{Normalize(x.GrowerNumber)}|{Normalize(x.LotNumber)}|{Normalize(x.VarietyCode)}|{Normalize(x.ProductionType)}|{x.IsOrganic}|{Normalize(x.InventoryStatus)}";

    private static RoomInventoryLedgerSnapshot ConsolidateSnapshots(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots)
    {
        var distinct = snapshots.Distinct().ToList();
        var latest = distinct.OrderByDescending(x => x.LastTransactionAt).ThenByDescending(x => x.LatestAdjustmentId).First();
        return latest with
        {
            PositiveBins = distinct.Sum(x => x.PositiveBins),
            NegativeBins = distinct.Sum(x => x.NegativeBins),
            ActualRunDepletionBins = distinct.Sum(x => x.ActualRunDepletionBins),
            ActualRunReversalBins = distinct.Sum(x => x.ActualRunReversalBins),
            LegacyBinsRunDepletionBins = distinct.Sum(x => x.LegacyBinsRunDepletionBins),
            TransferInBins = distinct.Sum(x => x.TransferInBins),
            TransferOutBins = distinct.Sum(x => x.TransferOutBins),
            TrueUpBins = distinct.Sum(x => x.TrueUpBins),
            OtherAdjustmentBins = distinct.Sum(x => x.OtherAdjustmentBins),
            CurrentBins = distinct.Sum(x => x.CurrentBins),
            TransactionCount = distinct.Sum(x => x.TransactionCount),
            FirstTransactionAt = distinct.Min(x => x.FirstTransactionAt),
            LastTransactionAt = distinct.Max(x => x.LastTransactionAt),
            LatestAdjustmentId = distinct.Max(x => x.LatestAdjustmentId),
            DroppedBins = distinct.Sum(x => x.DroppedBins),
            DroppedBinsRestored = distinct.Sum(x => x.DroppedBinsRestored)
        };
    }

    private async Task<User?> GetActorAsync(CancellationToken cancellationToken)
    {
        var email = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        return email is null ? null : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null || (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase)) return null;
        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private static string OrganicLabel(bool? organic, string production) => organic switch { true => "Organic", false => "Conventional", _ => production };
    private static bool Same(string? left, string? right) => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
