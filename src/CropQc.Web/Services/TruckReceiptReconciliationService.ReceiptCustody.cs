using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class TruckReceiptReconciliationService
{
    public Task<string?> AcknowledgeAsync(TruckReceiptActionForm form, CancellationToken ct) =>
        WriteReceiptCustodyAsync(form, InventoryCommandKind.AcknowledgeTransfer, ct);

    public Task<string?> PlaceCustodyAsync(TruckReceiptActionForm form, CancellationToken ct) =>
        WriteReceiptCustodyAsync(form, InventoryCommandKind.PlaceReceiptCustody, ct);

    private Task<string?> WriteReceiptCustodyAsync(TruckReceiptActionForm form, InventoryCommandKind kind, CancellationToken ct) =>
        CanonicalTruckWriteAsync(async actor =>
        {
            Require(db.CanonicalInventoryEnabled, "Receipt custody requires the canonical inventory workflow.");
            await RequireAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, ct);
            var transfer = await db.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
            var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.ReceiptId && !x.IsDeleted, ct);
            Require(transfer != null && receipt != null && transfer.ReceivingReceiptId == receipt.Id, "The matched receipt was not found.");
            await RequireCrewAsync(actor, transfer!.DestinationCustodyGroup, ct);
            var key = CanonicalTruckKey(kind.ToString(), form);
            var submission = JsonSerializer.Serialize(form);
            var replay = await CanonicalApplicationReplay.TryAsync(db, canonicalCommands!, key, actor.Id, kind, submission, ct);
            if (replay != null) return CanonicalInventoryMessages.Result(replay);
            Require(form.Allocations.All(x => x.Quantity >= 0), "Allocation quantities cannot be negative.");
            var intent = new InventoryReceiptCustodyIntent(form.TransferId, form.ReceiptId, form.TransferVersion, form.ReceiptVersion,
                form.Allocations.Where(x => x.Quantity > 0).Select(x => new InventoryCustodyQuantity(x.Id, x.Quantity)).ToImmutableArray(),
                kind == InventoryCommandKind.PlaceReceiptCustody ? new(receipt!.WarehouseId, receipt.RoomId) : null);
            return CanonicalInventoryMessages.Result(await canonicalCommands!.ExecuteAsync(new(key, kind, actor.Id, time.UtcNow,
                string.IsNullOrWhiteSpace(form.Reason) ? "Operator confirmed exact receipt custody allocations" : form.Reason,
                [], ApplicationIntent: submission, ReceiptCustody: intent), ct));
        }, ct);
}
