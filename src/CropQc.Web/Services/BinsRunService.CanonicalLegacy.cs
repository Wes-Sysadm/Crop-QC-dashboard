using System.Security.Claims;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class BinsRunService
{
    private async Task<string?> SaveCanonicalLegacyRunAsync(long? entryId, BinsRunForm form, ClaimsPrincipal user, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        if (form.BinsRun <= 0 || string.IsNullOrWhiteSpace(form.OperationKey)) return "A positive quantity and save identifier are required.";
        if (form.RunProjectionId != null || form.RunProjectionSourceId != null) return "Planning projections cannot authorize physical inventory deductions.";
        var actor = await CurrentUserIdAsync(user, ct);
        if (actor == null) return "The active operator could not be resolved.";
        var kind = entryId == null ? InventoryCommandKind.LegacyDump : InventoryCommandKind.ReviseLegacyDump;
        var submission = JsonSerializer.Serialize(new { entryId, form });
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Value, kind, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        if (!TryParseLedgerInventoryKey(form.InventoryKey, out var wh, out var room, out var crop, out var lot, out _, out var profile, out var grower, requireVariety: false)
            || form.RoomId != null && form.RoomId != room || form.WarehouseId != null && form.WarehouseId != wh)
            return "Select current inventory from the requested facility and room.";
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(wh, [room]), new(), BusinessTime.UtcNow, ct);
        var p = batch.Positions.SingleOrDefault(x => x.Identity.CropYear == crop && x.Identity.Lot == lot && x.Identity.FruitProfileId == profile && x.Identity.GrowerLotId == grower);
        if (p == null || !p.IsOperable) return "Current inventory or treatment identity cannot be proven.";
        if (form.CanonicalFingerprint != p.Watermark.Fingerprint) return "Inventory changed; reload and try again.";
        var existing = entryId is long id ? await dbContext.BinsRunEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (entryId != null && existing == null) return "The original Bins Run entry was not found.";
        var facility = await ResolveLegacyReportingFacilityAsync(actor, user, existing, form.RunAt, crop >= AuthoritativeStartCropYear, ct);
        if (facility.Error != null) return facility.Error;
        var available = CanonicalTreatmentSelections.MovementSlices(p).SingleOrDefault(x => x.Signature == form.TreatmentSignature)?.Quantity ?? 0;
        if (entryId is long original)
        {
            var own = (await new InventoryRunCorrectionAvailability(dbContext).ReadLegacyAsync(batch, original, ct))[p.PositionKey];
            if (own.Blocker != null) return own.Blocker;
            available = own.AvailableAfterOwnReversal.SingleOrDefault(x => x.Signature == form.TreatmentSignature)?.Quantity ?? 0;
        }
        if (form.ExpectedAvailableBins != available || form.BinsRun > available) return "Selected quantity changed or cannot cover this run.";
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, kind, actor.Value, form.RunAt,
            entryId == null ? "Record legacy Bins Run" : "Correct legacy Bins Run", [new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions),
                form.BinsRun, form.TreatmentSignature)], ApplicationIntent: submission, PhysicalParentId: entryId, Dispatch: new(null, form.Notes),
            LegacyRun: new(facility.WarehouseId, facility.Code, facility.AssignmentSource ?? "")), ct));
    }
}
