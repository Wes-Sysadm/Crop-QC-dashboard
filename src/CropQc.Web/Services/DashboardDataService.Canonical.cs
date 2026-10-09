using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class DashboardDataService
{
    private async Task<string?> ReverseCanonicalRoomTransferAsync(ReverseRoomTransferForm form, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var actor = await GetCurrentUserAsync(ct);
        if (actor == null) return "The current active user could not be resolved.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.ReverseRoomMove, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var old = await dbContext.RoomTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.Id, ct);
        if (old == null || old.IsReversed) return "Transfer is missing or already reversed.";
        var movements = await dbContext.TreatmentLineageMovements.AsNoTracking().Where(x => x.RoomTransferId == old.Id).ToListAsync(ct);
        var keys = movements.Select(x => InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey)).Distinct().ToArray();
        var signatures = movements.Select(x => x.TreatmentSignatureSnapshot).Distinct().ToArray();
        if (keys.Length != 1 || signatures.Length != 1) return "The exact original transfer lineage could not be proven.";
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(old.DestinationWarehouseId, [old.DestinationRoomId]), new() { AllowIndependentCohorts = true }, BusinessTime.UtcNow, ct);
        var position = batch.Positions.SingleOrDefault(x => x.Identity.Key == keys[0]);
        if (position == null || !position.IsOperable) return "Destination inventory cannot be proven for this reversal.";
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ReverseRoomMove,
            actor.Id, BusinessTime.UtcNow, form.Reason, [new(new(position.Identity, position.Location, position.Watermark.Fingerprint, position.Watermark.Versions),
                old.BinCount, signatures[0], new(old.SourceWarehouseId, old.SourceRoomId))], ApplicationIntent: submission, PhysicalParentId: old.Id), ct));
    }
    private async Task<RoomTransferInventoryProjection> BuildCanonicalTransferProjectionAsync(int roomId, CancellationToken ct)
    {
        var snapshots = await RoomInventoryLedger.GetSnapshotsAsync(null, [roomId], ct);
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(null, [roomId]), new() { AllowIndependentCohorts = true }, BusinessTime.UtcNow, ct);
        var entries = new List<RoomTransferInventoryEntry>();
        foreach (var r in batch.Positions.Where(x => x.AuthoritativeQuantity != 0))
        {
            var s = snapshots.FirstOrDefault(x => CanonicalTreatmentSelections.Identity(x).Key == r.Identity.Key);
            if (s == null) continue;
            if (!r.IsOperable) { entries.Add(UnavailableTransferEntry(s, CanonicalInventoryMessages.Blocker(r)!)); continue; }
            foreach (var slice in CanonicalTreatmentSelections.MovementSlices(r).Where(x => x.Quantity > 0))
                entries.Add(new(new(OperationalInventoryPosition.Key(s) + ":" + r.Watermark.Fingerprint,
                    $"{s.Grower} {s.Lot} {s.Variety} - {slice.State} ({slice.Quantity} bins)", slice.Quantity,
                    slice.Signature, slice.State, s.Grower, s.Variety, IsAvailable: slice.Confidence == InventoryConfidence.Proven), s));
        }
        var available = batch.Positions.Sum(x => x.AvailableQuantity);
        var total = batch.Positions.Sum(x => x.AuthoritativeQuantity);
        return new(total, available, total - available, true, null, total == available ? null : "Some inventory requires administrator review.", entries);
    }

    private async Task<string?> CreateCanonicalRoomTransferAsync(RoomTransferForm form, CancellationToken ct)
    {
        if (!await HasAccessAsync(ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, ct)) return "Room Transactions Edit access is required.";
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        if (form.BinCount <= 0 || string.IsNullOrWhiteSpace(form.Reason) || string.IsNullOrWhiteSpace(form.OperationKey)) return "A positive quantity, reason and operation key are required.";
        var actor = await GetCurrentUserAsync(ct);
        if (actor == null) return "The current active user could not be resolved.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.RoomMove, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var projection = await BuildCanonicalTransferProjectionAsync(form.FromRoomId, ct);
        IReadOnlyList<RoomTransferInventoryEntry> selected;
        if (form.TransferAllEligible)
        {
            if (TransferInventoryToken(projection) != form.ExpectedInventoryToken || projection.AvailableBins != form.BinCount)
                return "Inventory changed; reload and try again.";
            selected = projection.Entries.Where(x => x.Option.IsAvailable).ToArray();
        }
        else
        {
            var matches = projection.Entries.Where(x => x.Option.LotKey == form.SourceLotKey && x.Option.TreatmentSignature == form.TreatmentSignature && x.Option.IsAvailable).ToArray();
            if (matches.Length != 1) return "Inventory changed; reload and try again.";
            selected = matches;
        }
        var lines = selected.Select(x => new InventoryCommandLine(new(CanonicalTreatmentSelections.Identity(x.Snapshot),
            new(InventoryCustody.Room, x.Snapshot.WarehouseId, x.Snapshot.RoomId, x.Snapshot.Facility, x.Snapshot.Room),
            x.Option.LotKey[(x.Option.LotKey.LastIndexOf(':') + 1)..], []), form.TransferAllEligible ? x.Option.CurrentBins : form.BinCount,
            x.Option.TreatmentSignature, new(form.DestinationWarehouseId, form.DestinationRoomId))).ToImmutableArray();
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.RoomMove,
            actor.Id, form.TransferAt, form.Reason.Trim(), lines, ApplicationIntent: submission, Dispatch: new(null, form.Notes)), ct));
    }
}
