using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class RoomTreatmentService
{
    private async Task<(Receipt? Receipt, InventoryAvailabilityBatch? Batch, string? Error)> CanonicalReceiptTreatmentAsync(long id, CancellationToken ct)
    {
        var receipt = await dbContext.Receipts.AsNoTracking().Include(x => x.FruitProfile).Include(x => x.Warehouse).Include(x => x.Room)
            .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted && !x.IsTransferReceipt, ct);
        if (receipt == null) return (null, null, "An active ordinary receipt is required.");
        var rooms = await dbContext.TreatmentLineageSegments.Where(x => x.ReceiptId == id).Select(x => x.RoomId).Distinct().ToListAsync(ct);
        rooms.Add(receipt.RoomId);
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(null, rooms.Distinct().ToImmutableArray()),
            new(RequireExactReceipt: true, ReceiptId: id) { AllowIndependentCohorts = true }, businessTime.UtcNow, ct);
        var positions = batch.Positions.Where(x => x.Identity.CropYear == receipt.CropYear && x.Identity.GrowerLotId == receipt.GrowerLotId
            && x.Identity.FruitProfileId == receipt.FruitProfileId && x.AuthoritativeQuantity > 0).ToImmutableArray();
        batch = batch with { Positions = positions };
        if (positions.Any(x => !x.IsOperable)) return (receipt, batch, "Exact receipt inventory or treatment identity cannot be proven.");
        var owned = positions.Where(x => x.TreatmentSlices.Any(s => s.ReceiptEvidenceIds.Contains(id) && s.Quantity > 0)).ToArray();
        if (owned.Length != 1) return (receipt, batch, "Receiving treatment requires exact remaining receipt bins in one current room.");
        return (receipt, batch with { Positions = owned.ToImmutableArray() }, null);
    }

    private async Task<ReceiptTreatmentApplyPageViewModel> GetCanonicalReceiptTreatmentPageAsync(ReceiptTreatmentApplyForm form, bool review, CancellationToken ct)
    {
        var (receipt, batch, error) = await CanonicalReceiptTreatmentAsync(form.ReceiptId, ct);
        if (receipt == null) return new() { Form = form, Error = error };
        var crop = NormalizeCrop(receipt.FruitProfile.FruitType);
        var chemicals = await dbContext.TreatmentChemicals.AsNoTracking().Where(x => x.IsActive && x.ApplicationLevel == TreatmentApplicationLevels.Receiving && x.Crop == crop)
            .OrderBy(x => x.CommonName ?? x.ProductName).ThenBy(x => x.ProductName)
            .Select(x => new TreatmentChemicalOptionViewModel(x.Id, x.ProductName, x.CommonName, x.Crop, x.Volume, x.Unit, x.UnitPrice, x.Currency)).ToListAsync(ct);
        var selected = chemicals.SingleOrDefault(x => x.Id == form.TreatmentChemicalId);
        if (crop == null) error ??= "Receipt crop must be Apples or Pears.";
        if (review && selected == null) error ??= "Select an active Receiving treatment for this crop.";
        if (form.AppliedAt > businessTime.UtcNow || form.AppliedAt < receipt.ReceivedAt) error ??= "Application time must be after receiving and no later than now.";
        if (batch != null) form.CanonicalSnapshot = TreatmentWatermark(batch);
        var bins = batch?.Positions.SelectMany(x => x.TreatmentSlices).Where(x => x.ReceiptEvidenceIds.Contains(receipt.Id)).Sum(x => x.Quantity) ?? 0;
        var location = batch?.Positions.FirstOrDefault()?.Location;
        return new()
        {
            Form = form,
            Error = error,
            IsReview = review && error == null,
            ReceiptNumber = receipt.CompuTechReceiptId,
            Grower = receipt.GrowerName,
            GrowerNumber = receipt.GrowerNumber ?? receipt.LotCode,
            Warehouse = location?.Facility ?? receipt.Warehouse.Code,
            Room = location?.Name ?? receipt.Room.Code,
            Variety = receipt.FruitProfile.Name,
            ProductionType = receipt.FruitProfile.ProductionType,
            TotalBins = bins,
            TreatmentOptions = chemicals,
            SelectedTreatment = selected,
            EstimatedCost = decimal.Round(bins * (selected?.UnitPrice ?? 0), 2)
        };
    }

    private async Task<(string? Error, long? ApplicationId)> ApplyCanonicalReceiptTreatmentAsync(ReceiptTreatmentApplyForm form, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return ("Canonical inventory command execution is not configured.", null);
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.ReceiptTreatmentAssignment, submission, ct);
        if (replay != null) return (CanonicalInventoryMessages.Result(replay), replay.Effects.FirstOrDefault()?.ParentId);
        var (_, batch, error) = await CanonicalReceiptTreatmentAsync(form.ReceiptId, ct);
        if (error != null || batch == null) return (error ?? "Receipt inventory is unavailable.", null);
        if (TreatmentWatermark(batch) != form.CanonicalSnapshot) return ("Inventory changed; reload and review treatment again.", null);
        var lines = batch.Positions.SelectMany(p => p.TreatmentSlices.Where(s => s.ReceiptEvidenceIds.Contains(form.ReceiptId))
            .GroupBy(s => s.Signature).Select(g => new InventoryCommandLine(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions),
                g.Sum(s => s.Quantity), g.Key, ReceiptId: form.ReceiptId))).ToImmutableArray();
        var result = await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ReceiptTreatmentAssignment, actor, form.AppliedAt,
            "Receipt treatment", lines, TreatmentChemicalId: form.TreatmentChemicalId, ApplicationIntent: submission, Dispatch: new(null, form.Notes)), ct);
        return (CanonicalInventoryMessages.Result(result), result.Effects.FirstOrDefault()?.ParentId);
    }
}
