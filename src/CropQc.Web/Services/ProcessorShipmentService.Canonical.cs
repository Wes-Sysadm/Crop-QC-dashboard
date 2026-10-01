using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class ProcessorShipmentService
{
    private async Task<ProcessorShipmentWriteResult> MapCanonicalAsync(InventoryCommandResult r, CancellationToken ct)
    {
        var line = r.Effects.FirstOrDefault()?.ParentId;
        var parent = line == null ? null : await dbContext.ProcessorShipmentLines.Where(x => x.Id == line).Select(x => (long?)x.ProcessorShipmentId).SingleAsync(ct);
        return new(r.Status is InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed,
            r.Status == InventoryCommandStatus.Replayed, parent, CanonicalInventoryMessages.Result(r));
    }

    private async Task<ProcessorShipmentWriteResult> CreateCanonicalAsync(ProcessorShipmentForm form, CancellationToken ct)
    {
        if (canonicalCommands == null) return new(false, false, null, "Canonical inventory command execution is not configured.");
        if (!await access.HasAccessAsync(httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(), ApplicationAreas.ProcessorShipments, PageAccessLevel.Edit, ct))
            return new(false, false, null, "Processor Shipments Edit access is required.");
        var actor = await GetActorAsync(ct);
        if (actor == null) return new(false, false, null, "The current active user could not be resolved.");
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.ProcessorSale, submission, ct);
        if (replay != null) return await MapCanonicalAsync(replay, ct);
        var selected = ResolveSelectedLines(form, await GetInventoryOptionsAsync(ct), out var error);
        if (error != null) return new(false, false, null, error);
        var lines = selected.Select(x => new InventoryCommandLine(new(new(x.Option.CropYear, x.Option.GrowerLotId, x.Option.FruitProfileId,
            x.Option.LotNumber, x.Option.GrowerNumber, x.Option.VarietyCode, x.Option.ProductionType, x.Option.IsOrganic, x.Option.InventoryStatus),
            new(InventoryCustody.Room, x.Option.WarehouseId, x.Option.RoomId, x.Option.Facility, x.Option.Room),
            x.Form.SourceKey[(x.Form.SourceKey.LastIndexOf(':') + 1)..], []), x.Form.BinsSent, x.Option.TreatmentSignature,
            PoundsPerBin: x.Option.PoundsPerBin)).ToImmutableArray();
        return await MapCanonicalAsync(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ProcessorSale, actor.Id,
            businessTime.PacificLocalToUtc(form.ShippedAt), "Processor shipment", lines, CounterpartyId: form.ProcessorId,
            ProcessorTerms: new(form.SaleRate!.Value, form.PricingBasis, form.Currency.Trim().ToUpperInvariant()), ApplicationIntent: submission,
            Dispatch: new(Normalize(form.ReferenceNumber), Normalize(form.Notes))), ct), ct);
    }

    private async Task<string?> ReverseCanonicalAsync(ProcessorShipmentReversalForm form, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        if (!await access.HasAccessAsync(httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(), ApplicationAreas.ProcessorShipments, PageAccessLevel.Admin, ct))
            return "Processor Shipments Admin access is required.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.Return, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var shipment = await dbContext.ProcessorShipments.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == form.ShipmentId, ct);
        if (shipment == null || shipment.ReversedAt != null) return "Shipment is missing or already reversed.";
        var separator = shipment.OperationKey.LastIndexOf(':');
        string? original = separator > 0 ? shipment.OperationKey[..separator] : null;
        if (!await dbContext.InventoryCommands.AnyAsync(x => x.OperationKey == original, ct)) original = null;
        var lines = ImmutableArray.CreateBuilder<InventoryCommandLine>();
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext));
        foreach (var row in shipment.Lines.OrderBy(x => x.Id))
        {
            var batch = await resolver.ResolveAsync(new(row.WarehouseId, [], InventoryCustody.Processor, row.Id), new(AllowedCustody: InventoryCustody.Processor), businessTime.UtcNow, ct);
            foreach (var p in batch.Positions)
            {
                if (!p.IsOperable || p.TreatmentSlices.Select(x => x.Signature).Distinct().Count() != 1) return "Processor custody cannot be proven.";
                lines.Add(new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), p.AuthoritativeQuantity, p.TreatmentSlices[0].Signature));
            }
        }
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.Return, actor,
            businessTime.UtcNow, form.Reason!, lines.ToImmutable(), OriginalOperationKey: original, ApplicationIntent: submission, PhysicalParentId: shipment.Id), ct));
    }
}
