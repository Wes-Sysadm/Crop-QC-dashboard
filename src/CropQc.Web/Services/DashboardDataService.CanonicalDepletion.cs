using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class DashboardDataService
{
    private async Task<IReadOnlyList<RoomReceiptOptionViewModel>> CanonicalDepletionOptionsAsync(int room, CancellationToken ct)
    {
        var evidence = await new InventoryEvidenceLoader(dbContext).LoadAsync(new(null, [room]), BusinessTime.UtcNow, ct);
        // Enumerate receipt candidates, then ask the same canonical proof used by
        // submission. A different receipt's unassigned pool must not hide exact stock.
        var positions = evidence.Positions.SelectMany(p => p.Receipts.Select(x => x.Id).Distinct()
            .Select(id => (Id: id, Position: InventoryAvailabilityResolver.Resolve(p, new(RequireExactReceipt: true, ReceiptId: id)))))
            .Where(x => x.Position.IsOperable).ToArray();
        var ids = positions.Select(x => x.Id).Distinct().ToArray();
        var receipts = await dbContext.Receipts.AsNoTracking().Where(x => ids.Contains(x.Id) && x.RoomId == room && !x.IsDeleted && !x.IsTransferReceipt)
            .ToDictionaryAsync(x => x.Id, ct);
        return positions.SelectMany(item => item.Position.TreatmentSlices.Where(s => s.ReceiptEvidenceIds.Length == 1 && s.ReceiptEvidenceIds[0] == item.Id && s.Quantity > 0)
            .Where(s => receipts.ContainsKey(s.ReceiptEvidenceIds[0])).Select(s => new RoomReceiptOptionViewModel(s.ReceiptEvidenceIds[0],
                $"{receipts[s.ReceiptEvidenceIds[0]].CompuTechReceiptId} - {item.Position.Identity.Lot} {item.Position.Identity.Variety} - {s.State} ({s.Quantity} bins current)",
                s.Quantity, s.Signature, item.Position.Watermark.Fingerprint))).ToArray();
    }

    private async Task<string?> CreateCanonicalDepletionAsync(RoomDepletionForm form, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var actor = await GetCurrentUserAsync(ct);
        if (actor == null) return "The current active user could not be resolved.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.ReceiptDepletion, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(null, [form.RoomId]),
            new(RequireExactReceipt: true, ReceiptId: form.ReceiptId, TreatmentSignature: form.TreatmentSignature), BusinessTime.UtcNow, ct);
        var p = batch.Positions.SingleOrDefault(x => x.ReceiptProvenance.ReceiptIds.Contains(form.ReceiptId));
        if (p == null || !p.IsOperable) return "Exact receipt inventory or treatment identity cannot be proven.";
        if (form.CanonicalFingerprint != p.Watermark.Fingerprint) return "Inventory changed; reload and try again.";
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ReceiptDepletion, actor.Id,
            form.DepletedAt, "Receipt bins sent to line", [new(new(p.Identity, p.Location, form.CanonicalFingerprint, p.Watermark.Versions), form.BinCount,
                form.TreatmentSignature, ReceiptId: form.ReceiptId)], ApplicationIntent: submission, Dispatch: new(form.Destination, form.Notes)), ct));
    }

    private async Task<string?> ReverseCanonicalDepletionAsync(VoidRoomDepletionForm form, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var actor = await GetCurrentUserAsync(ct);
        if (actor == null) return "The current active administrator could not be resolved.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.ReverseDepletion, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        if (!await dbContext.RoomDepletions.AnyAsync(x => x.Id == form.DepletionId && x.RoomId == form.RoomId, ct)) return "Depletion was not found in this room.";
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ReverseDepletion,
            actor.Id, BusinessTime.UtcNow, form.Reason, [], ApplicationIntent: submission, PhysicalParentId: form.DepletionId), ct));
    }
}
