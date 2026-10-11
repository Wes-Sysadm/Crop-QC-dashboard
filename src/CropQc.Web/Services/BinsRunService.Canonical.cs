using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class BinsRunService
{
    private async Task<string?> ReverseCanonicalRunAsync(long id, long? version, string key, string reason, ClaimsPrincipal user,
        InventoryCommandKind kind, string submission, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var actor = await CurrentUserIdAsync(user, ct);
        if (actor == null) return "The current active user could not be resolved.";
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, key, actor.Value, kind, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(key, kind, actor.Value, BusinessTime.UtcNow,
            reason, [], ApplicationIntent: submission, PhysicalParentId: id, ExpectedParentVersion: version), ct));
    }
    private async Task<IReadOnlyList<InventorySnapshot>> CanonicalPlanningSnapshotsAsync(IReadOnlyList<InventorySnapshot> snapshots, CancellationToken ct)
    {
        if (snapshots.Count == 0) return snapshots;
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(
            new(null, snapshots.Select(x => x.RoomId).Distinct().ToImmutableArray()), new() { AllowIndependentCohorts = true }, BusinessTime.UtcNow, ct);
        var positions = batch.Positions.ToDictionary(x => (x.Location.RoomId, x.Identity.Key));
        return snapshots.Select(x => x with { CurrentBins = positions.GetValueOrDefault((x.RoomId, CanonicalIdentity(x).Key))?.AvailableQuantity ?? 0 }).ToArray();
    }

    private static InventoryIdentity CanonicalIdentity(InventorySnapshot s) => new(s.CropYear, s.GrowerLotId, s.FruitProfileId,
        s.Lot, s.GrowerNumber, s.Variety, s.ProductionType, s.IsOrganic, s.InventoryStatus);

    private async Task<IReadOnlyList<BinsRunInventoryOptionViewModel>> BuildCanonicalRunOptionsAsync(
        IReadOnlyList<InventorySnapshot> snapshots, IReadOnlyDictionary<string, LotSampleDistribution> samples, CancellationToken ct, long? correctingRunId = null, long? correctingLegacyEntryId = null)
    {
        if (snapshots.Count == 0) return [];
        var rooms = snapshots.Select(x => x.RoomId).Distinct().ToImmutableArray();
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(null, rooms), new() { AllowIndependentCohorts = true }, BusinessTime.UtcNow, ct);
        var positions = batch.Positions.ToDictionary(x => (x.Location.RoomId, x.Identity.Key));
        var corrections = correctingRunId is long runId ? await new InventoryRunCorrectionAvailability(dbContext).ReadAsync(batch, runId, ct)
            : correctingLegacyEntryId is long entryId ? await new InventoryRunCorrectionAvailability(dbContext).ReadLegacyAsync(batch, entryId, ct) : null;
        var candidates = snapshots.ToList();
        if (corrections != null)
        {
            var missing = corrections.Values.Select(x => x.Current).Where(x => !positions.ContainsKey((x.Location.RoomId, x.Identity.Key))).ToArray();
            var growerIds = missing.Select(x => x.Identity.GrowerLotId).ToArray();
            var profileIds = missing.Select(x => x.Identity.FruitProfileId).ToArray();
            var growers = await dbContext.GrowerLots.AsNoTracking().Where(x => growerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var profiles = await dbContext.FruitProfiles.AsNoTracking().Where(x => profileIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            foreach (var r in missing)
            {
                var template = snapshots.First(x => x.RoomId == r.Location.RoomId);
                var i = r.Identity;
                positions.Add((r.Location.RoomId, i.Key), r);
                candidates.Add(template with
                {
                    InventoryKey = LedgerInventoryKey(r.Location.WarehouseId, r.Location.RoomId!.Value,
                    i.CropYear, i.Lot, i.Variety, i.FruitProfileId, i.GrowerLotId),
                    ReceiptId = null,
                    InventoryAdjustmentId = null,
                    ReceiptReference = "Exact current run restoration",
                    CropYear = i.CropYear,
                    GrowerLotId = i.GrowerLotId,
                    FruitProfileId = i.FruitProfileId,
                    Grower = growers[i.GrowerLotId!.Value].Grower,
                    GrowerNumber = i.GrowerNumber,
                    Lot = i.Lot,
                    Variety = i.Variety,
                    FruitType = profiles[i.FruitProfileId!.Value].FruitType,
                    ProductionType = i.ProductionType,
                    IsOrganic = i.IsOrganic,
                    InventoryStatus = i.Status,
                    CurrentBins = 0,
                    CanonicalOrchardBlockId = null
                });
            }
        }
        var sealedRooms = await dbContext.Rooms.Where(x => rooms.Contains(x.Id) && x.IsSealed).Select(x => x.Id).ToListAsync(ct);
        var options = new List<BinsRunInventoryOptionViewModel>();
        foreach (var s in candidates)
        {
            if (!positions.TryGetValue((s.RoomId, CanonicalIdentity(s).Key), out var r)) continue;
            samples.TryGetValue(QcIdentityKey(s), out var distribution);
            var correction = corrections?.GetValueOrDefault(r.PositionKey);
            var slices = correction != null ? correction.AvailableAfterOwnReversal.ToArray()
                : r.TreatmentSlices.Length > 0 ? CanonicalTreatmentSelections.MovementSlices(r) : [new InventoryTreatmentSlice("", "Unknown", 0, InventoryConfidence.Unknown, [], [], [])];
            foreach (var slice in slices)
                options.Add(new(s.InventoryKey, s.ReceiptId, s.InventoryAdjustmentId, s.WarehouseId, s.RoomId,
                    $"{s.Grower} - {s.Variety} - {s.Lot} - {slice.Quantity} bins available", s.Grower, s.Lot, s.Variety,
                    $"{s.Facility} / {s.Room}", r.IsOperable ? slice.Quantity : 0,
                    distribution == null ? "No grade data" : FormatGradeSummary(distribution.GradePercentages), s.ReceiptDate,
                    s.FruitProfileId, s.FruitType, s.CanonicalOrchardBlockId, s.CropYear, s.ProductionType, s.Facility, s.Room,
                    s.GrowerLotId, s.ReceiptReference ?? "Room inventory", slice.Signature,
                    slice.State == "Untreated" ? "Untreated" : "Confirmed treatment", s.GrowerNumber ?? "", sealedRooms.Contains(s.RoomId),
                    null, null, r.IsOperable && correction?.Blocker == null && slice.Confidence == InventoryConfidence.Proven,
                    correction?.Blocker ?? CanonicalInventoryMessages.Blocker(r), r.Watermark.Fingerprint));
        }
        return options;
    }

    private async Task<string?> SaveCanonicalActualRunAsync(ActualRunForm form, ClaimsPrincipal user, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        if (form.Id != null && string.IsNullOrWhiteSpace(form.CorrectionReason)) return "A correction reason is required.";
        if (string.IsNullOrWhiteSpace(form.OperationKey)) return "The save request identifier is required.";
        var actor = await CurrentUserIdAsync(user, ct);
        if (actor == null) return "The current user account could not be resolved.";
        var submission = JsonSerializer.Serialize(form);
        var prior = await dbContext.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == form.OperationKey, ct);
        if (prior != null)
        {
            var original = JsonSerializer.Deserialize<InventoryCommand>(prior.IntentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (original.ApplicationIntent != submission || original.ActorId != actor) return "The save request identifier was already used for different input.";
            return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(original, ct));
        }
        var selected = form.Lines.Where(x => !string.IsNullOrWhiteSpace(x.InventoryKey) || x.BinsRun != 0).ToArray();
        if (selected.Length == 0 || selected.Any(x => x.BinsRun <= 0)) return "Select at least one room-lot row with a positive quantity.";
        if (selected.Any(x => string.IsNullOrWhiteSpace(x.CanonicalFingerprint))) return "Inventory changed; reload and try again.";
        var parsed = new List<(ActualRunLineForm Line, int Warehouse, int Room, int? Crop, string Lot, int? Profile, int? Grower)>();
        foreach (var line in selected)
        {
            if (!TryParseLedgerInventoryKey(line.InventoryKey, out var wh, out var room, out var crop, out var lot, out _, out var profile, out var grower, requireVariety: false))
                return "Select current room inventory.";
            parsed.Add((line, wh, room, crop, lot, profile, grower));
        }
        if (parsed.Select(x => x.Warehouse).Distinct().Count() != 1) return "All room-lot rows in one Actual Run must belong to the same facility.";
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(
            new(parsed[0].Warehouse, parsed.Select(x => x.Room).Distinct().ToImmutableArray()), new() { AllowIndependentCohorts = true }, BusinessTime.UtcNow, ct);
        var choices = form.Id is long correctingId
            ? (await new InventoryRunCorrectionAvailability(dbContext).ReadAsync(batch, correctingId, ct)).Values.Select(x => x.Current).ToArray()
            : batch.Positions.ToArray();
        var lines = ImmutableArray.CreateBuilder<InventoryCommandLine>();
        foreach (var item in parsed)
        {
            var matches = choices.Where(x => x.Location.RoomId == item.Room && x.Identity.CropYear == item.Crop
                && x.Identity.Lot == item.Lot && x.Identity.FruitProfileId == item.Profile && x.Identity.GrowerLotId == item.Grower).ToArray();
            if (matches.Length != 1) return "Inventory identity changed; reload and try again.";
            var r = matches[0];
            if (!r.IsOperable) return CanonicalInventoryMessages.Blocker(r);
            lines.Add(new(new(r.Identity, r.Location, item.Line.CanonicalFingerprint, []), item.Line.BinsRun, item.Line.TreatmentSignature));
        }
        var admin = await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.Admin, ct)
            || await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Admin, ct);
        var existing = form.Id is long runId ? await dbContext.ActualRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct) : null;
        var facility = await ResolveRunFacilityAsync(actor.Value, form.RunFacilityWarehouseId, existing, admin, form.RunAt, true, ct);
        if (facility.Error != null) return facility.Error;
        var command = new InventoryCommand(form.OperationKey, form.Id == null ? InventoryCommandKind.Dump : InventoryCommandKind.ReviseRun, actor.Value, form.RunAt,
            form.CorrectionReason ?? "Record Actual Run", lines.ToImmutable(),
            Run: new(facility.WarehouseId!.Value, form.SalesDeskId, facility.AssignmentSource!, NormalizeOptional(form.Notes)), ApplicationIntent: submission,
            PhysicalParentId: form.Id, ExpectedParentVersion: form.Id == null ? null : form.ConcurrencyVersion);
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(command, ct));
    }
}
