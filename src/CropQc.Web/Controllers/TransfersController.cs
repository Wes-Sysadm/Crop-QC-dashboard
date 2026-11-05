using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryStatusIdentity = CropQc.Shared.Inventory.InventoryStatusIdentity;

namespace CropQc.Web.Controllers;

[Authorize(Policy = AccessPolicyNames.BinsRunView)]
[Route("Transfers")]
public sealed class TransfersController(CropQcDbContext db, IInventoryCommandExecutor commands, IUserAccessService access,
    IConfiguration configuration) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? sourceRoomId, CancellationToken ct)
    {
        var locations = await Locations(ct);
        var positions = sourceRoomId is int id ? await Positions(id, ct) : [];
        var applicationIds = positions.SelectMany(p => p.TreatmentSlices).SelectMany(s => s.ApplicationIds).Distinct().ToArray();
        var applications = await db.RoomTreatmentApplications.AsNoTracking().Where(x => applicationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        string Treatment(InventoryTreatmentSlice slice) => slice.ApplicationIds.IsEmpty ? slice.State
            : string.Join(", ", slice.ApplicationIds.Select(id => applications.TryGetValue(id, out var application)
                ? $"{application.ProductNameSnapshot} ({application.AppliedAt:yyyy-MM-dd})" : "Treatment evidence unavailable"));
        var inventory = positions.Where(p => p.IsOperable).SelectMany(p => CanonicalTreatmentSelections.MovementSlices(p)
            .Where(s => s.Quantity > 0 && s.Confidence == InventoryConfidence.Proven)
            .Select(s => new DirectTransferOption(p.Identity.Key, s.Signature, p.Watermark.Fingerprint,
                $"{p.Identity.GrowerNumber} · {p.Identity.Lot} · {p.Identity.Variety} · {p.Identity.ProductionType} · {Treatment(s)}", s.Quantity))).ToArray();
        var warnings = positions.Where(p => !p.IsOperable || p.AvailableQuantity < p.AuthoritativeQuantity)
            .Select(p => $"{p.Identity.Lot}: {CanonicalInventoryMessages.Blocker(p) ?? "Some treatment allocations remain unresolved."}").ToList();
        var history = await db.RoomTransfers.AsNoTracking().Include(x => x.SourceRoom).Include(x => x.DestinationRoom)
            .Where(x => x.IsDirectTransfer && (sourceRoomId == null || x.SourceRoomId == sourceRoomId || x.DestinationRoomId == sourceRoomId))
            .OrderByDescending(x => x.Id).Take(100).ToListAsync(ct);
        if ((await DirectTransferAssignmentHistory.ForRoomAsync(db, sourceRoomId, ct)).Count > 0)
            warnings.Add("Transfer history includes unverified source assignments. Assigned inventory is not a physical count; review correction evidence before relying on its location.");
        if (!Enabled) warnings.Add("New direct transfers are disabled in this compatible rollback configuration. History remains available.");
        return View(new DirectTransferPage(new() { SourceRoomId = sourceRoomId ?? 0 }, locations, inventory, history, warnings));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AccessPolicyNames.TransfersCreate)]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    public async Task<IActionResult> Create(DirectTransferForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return Failure("Enter whole-bin quantities and valid locations.", form.SourceRoomId);
        var actor = await Actor(ct);
        if (actor == null) return Forbid();
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(db, commands, form.OperationKey, actor.Value, InventoryCommandKind.DirectTransfer, submission, ct);
        if (replay != null) return Completed(replay, form.SourceRoomId);
        if (!Enabled || !db.CanonicalInventoryEnabled) return Failure("Direct transfer creation is unavailable.", form.SourceRoomId);
        var locations = await Locations(ct);
        var target = locations.SingleOrDefault(x => x.Id == form.DestinationRoomId);
        if (target == null || target.Id == form.SourceRoomId) return Failure("Choose a different active, open destination.", form.SourceRoomId);
        var positions = await Positions(form.SourceRoomId, ct);
        var lines = ImmutableArray.CreateBuilder<InventoryCommandLine>();
        foreach (var selection in form.Selections.Where(x => x.Quantity > 0))
        {
            var p = positions.SingleOrDefault(x => x.Identity.Key == selection.IdentityKey);
            if (p == null || !p.IsOperable || p.Watermark.Fingerprint != selection.Fingerprint)
                return Failure("Source inventory changed. Review the refreshed quantities before confirming.", form.SourceRoomId);
            lines.Add(new(new(p.Identity, p.Location, selection.Fingerprint, p.Watermark.Versions), selection.Quantity, selection.Signature, new(target.WarehouseId, target.Id)));
        }
        if (lines.Count == 0) return Failure("Select at least one inventory quantity to transfer.", form.SourceRoomId);
        return Completed(await commands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.DirectTransfer, actor.Value,
            DateTimeOffset.UtcNow, "Operator confirmed direct location transfer", lines.ToImmutable(), ApplicationIntent: submission), ct), form.SourceRoomId);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var transfer = await db.RoomTransfers.AsNoTracking().Include(x => x.SourceRoom).Include(x => x.DestinationRoom)
            .Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse).Include(x => x.CreatedByUser)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsDirectTransfer, ct);
        if (transfer == null) return NotFound();
        var corrections = await db.RoomTransfers.AsNoTracking().Where(x => x.CorrectsRoomTransferId == id).OrderBy(x => x.Id).ToListAsync(ct);
        var moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.RoomTransferId == id).ToListAsync(ct);
        var identity = moves.Select(x => InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey)).Distinct().SingleOrDefault();
        var p = (await Positions(transfer.DestinationRoomId, ct)).SingleOrDefault(x => x.Identity.Key == identity);
        var ids = moves.Select(x => x.DestinationSegmentId).ToArray();
        var remaining = await db.TreatmentLineageSegments.Where(x => ids.Contains(x.Id) && x.Disposition == "Current").SumAsync(x => x.CurrentBins, ct);
        var quantity = Math.Min(remaining, transfer.BinCount - corrections.Sum(x => x.BinCount));
        return View(new DirectTransferDetail(transfer, corrections, await Locations(ct), p?.Watermark.Fingerprint ?? "", quantity,
            Enabled && quantity > 0 && p?.IsOperable == true && await access.HasAccessAsync(User, ApplicationAreas.Transfers, PageAccessLevel.Admin, ct)));
    }

    [HttpPost("Correct")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AccessPolicyNames.TransfersAdmin)]
    public async Task<IActionResult> Correct(DirectTransferCorrectionForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return CorrectionFailure(form.TransferId, "Enter a positive whole-bin quantity, reason and actual evidence.");
        var actor = await Actor(ct);
        if (actor == null) return Forbid();
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(db, commands, form.OperationKey, actor.Value, InventoryCommandKind.CorrectDirectTransfer, submission, ct);
        if (replay != null) return Completed(replay, 0);
        if (!Enabled || !db.CanonicalInventoryEnabled) return CorrectionFailure(form.TransferId, "Direct transfer corrections are unavailable in this configuration.");
        var original = await db.RoomTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.TransferId && x.IsDirectTransfer, ct);
        if (original == null) return NotFound();
        var moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.RoomTransferId == original.Id).ToListAsync(ct);
        var keys = moves.Select(x => InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey)).Distinct().ToArray();
        var signatures = moves.Select(x => x.TreatmentSignatureSnapshot).Distinct().ToArray();
        if (keys.Length != 1 || signatures.Length != 1) return CorrectionFailure(original.Id, "Original inventory and treatment allocations cannot be proven.");
        var p = (await Positions(original.DestinationRoomId, ct)).SingleOrDefault(x => x.Identity.Key == keys[0]);
        if (p == null || p.Watermark.Fingerprint != form.Fingerprint) return CorrectionFailure(original.Id, "Inventory changed; reload the correction.");
        var destinationId = form.PhysicallyVerified ? form.VerifiedDestinationRoomId : original.SourceRoomId;
        var destination = (await Locations(ct)).SingleOrDefault(x => x.Id == destinationId);
        if (destination == null) return CorrectionFailure(original.Id, "Select an active, open destination.");
        var result = await commands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.CorrectDirectTransfer, actor.Value,
            DateTimeOffset.UtcNow, form.Reason, [new(new(p.Identity, p.Location, form.Fingerprint, p.Watermark.Versions), form.Quantity,
                signatures[0], new(destination.WarehouseId, destination.Id))], PhysicalParentId: original.Id,
            ApplicationIntent: submission, LocationVerification: new(form.PhysicallyVerified, form.Evidence)), ct);
        return result.Status is InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed ? Completed(result, 0)
            : CorrectionFailure(original.Id, result.Detail);
    }

    private bool Enabled => configuration.GetValue("DirectTransfers:Enabled", true);
    private Task<int?> Actor(CancellationToken ct) => db.Users.Where(x => x.Email == User.FindFirstValue(ClaimTypes.Email) && x.IsActive).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
    private Task<List<DirectTransferLocation>> Locations(CancellationToken ct) => db.Rooms.AsNoTracking()
        .Where(x => x.IsActive && x.Warehouse.IsActive && !x.IsSealed).OrderBy(x => x.Warehouse.Code).ThenBy(x => x.SortOrder)
        .Select(x => new DirectTransferLocation(x.Id, x.WarehouseId, x.Warehouse.Code + " — " + (x.CropQcRoomName ?? x.DisplayName ?? x.Code))).ToListAsync(ct);
    private async Task<ImmutableArray<InventoryAvailabilityResult>> Positions(int room, CancellationToken ct) =>
        (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(null, [room]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow, ct)).Positions;
    private IActionResult Failure(string error, int room) { TempData["Error"] = error; return RedirectToAction(nameof(Index), new { sourceRoomId = room }); }
    private IActionResult CorrectionFailure(long id, string error) { TempData["Error"] = error; return RedirectToAction(nameof(Details), new { id }); }
    private IActionResult Completed(InventoryCommandResult result, int room)
    {
        if (result.Status is not (InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed)) return Failure(result.Detail, room);
        TempData["Success"] = result.Status == InventoryCommandStatus.Replayed ? "Transfer already recorded; no inventory moved twice." : "Transfer completed. Inventory and custody updated.";
        return RedirectToAction(nameof(Details), new { id = result.Effects.First().ParentId });
    }
}
