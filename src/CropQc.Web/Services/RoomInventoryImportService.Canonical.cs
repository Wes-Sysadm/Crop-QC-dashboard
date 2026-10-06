using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class RoomInventoryImportService
{
    private static ImmutableArray<InventoryBaselineRow> BaselineRows(RoomInventoryImportPreviewViewModel preview) => preview.Rows.Select(x =>
        new InventoryBaselineRow(x.CropYear, x.WarehouseId ?? 0, x.RoomId ?? 0, x.GrowerLotId ?? 0, x.FruitProfileId ?? 0,
            x.LotNumber, x.Variety, x.InventoryStatus, x.NewBinCount ?? x.BinCount ?? -1, x.EffectiveDate.ToUniversalTime(),
            x.Source, x.Notes, x.CompuTechRoomCode, x.SubLocation, x.CropQcRoomName, EbsRoomSortOrder(x.SubLocation, x.CropQcRoomName))).ToImmutableArray();

    private async Task AddCanonicalPreviewAsync(RoomInventoryImportPreviewViewModel preview, CancellationToken ct)
    {
        if (preview.InvalidCount != 0 || preview.DuplicateCount != 0) return;
        try
        {
            preview.CanonicalReview = await new CanonicalBaselinePreview(dbContext).PreviewAsync(BaselineRows(preview), ct);
            preview.OperationKey = Guid.NewGuid().ToString("N");
            preview.ExpectedFingerprint = preview.CanonicalReview.Fingerprint;
        }
        catch (InvalidOperationException ex) { preview.CanonicalError = ex.Message; }
    }

    private async Task<(RoomInventoryImportPreviewViewModel Preview, string? Error)> ApplyCanonicalBaselineAsync(
        RoomInventoryImportForm form, string csv, string email, CancellationToken ct)
    {
        var empty = new RoomInventoryImportPreviewViewModel { CsvText = csv, IsBuiltInSeed = form.UseBuiltInSeed };
        if (canonicalCommands == null || !form.ConfirmImport || string.IsNullOrWhiteSpace(form.OperationKey) || string.IsNullOrWhiteSpace(form.ExpectedFingerprint))
            return (empty, "Preview and confirm the baseline import before applying it.");
        var actor = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);
        if (actor == null) return (empty, "An active operator is required.");
        var submission = JsonSerializer.Serialize(new { Csv = csv, form.UseBuiltInSeed, form.ConfirmImport, form.ConfirmReplaceExistingBatch, form.ExpectedFingerprint });
        var prior = await dbContext.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == form.OperationKey, ct);
        if (prior != null)
        {
            var old = JsonSerializer.Deserialize<InventoryCommand>(prior.IntentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (old.Kind != InventoryCommandKind.ImportBaseline || old.ActorId != actor.Id || old.ApplicationIntent != submission)
                return (empty, "Operation key was used with different input.");
            return (empty, CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(old, ct)));
        }
        var preview = await BuildPreviewAsync(csv, form.UseBuiltInSeed, ct);
        if (preview.InvalidCount != 0 || preview.DuplicateCount != 0) return (preview, "Resolve invalid or duplicate rows before importing.");
        // The executor repeats the forecast under its own Serializable transaction;
        // never replace the submitted review fingerprint with a freshly read value.
        var command = new InventoryCommand(form.OperationKey, InventoryCommandKind.ImportBaseline, actor.Id, DateTimeOffset.UtcNow,
            "Reviewed current inventory baseline import", [], ApplicationIntent: submission,
            Baseline: new(BaselineRows(preview), form.ExpectedFingerprint, form.ConfirmReplaceExistingBatch));
        return (preview, CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(command, ct)));
    }
}
