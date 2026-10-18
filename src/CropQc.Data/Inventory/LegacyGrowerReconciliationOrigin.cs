using System.Text.Json;
using CropQc.Data.Entities;

namespace CropQc.Data.Inventory;

/// <summary>Proves the recorded reconciliation of a missing GrowerLot key in
/// one physical pool. It never moves a later credit into earlier custody.</summary>
internal sealed record LegacyGrowerReconciliationOrigin(int WarehouseId, int RoomId, int CropYear,
    int FruitProfileId, string Lot, int TargetGrowerLotId, DateTimeOffset RecordedAt = default, Guid CorrectionId = default)
{
    internal bool Contains(RoomInventoryAdjustment row) => row.WarehouseId == WarehouseId && row.RoomId == RoomId
        && row.CropYear == CropYear && row.FruitProfileId == FruitProfileId
        && string.Equals(row.LotNumber.Trim(), Lot.Trim(), StringComparison.OrdinalIgnoreCase)
        && (row.GrowerLotId == null || row.GrowerLotId == TargetGrowerLotId);

    internal bool Authorizes(RoomInventoryAdjustment row) => Contains(row)
        && (row.GrowerLotId != null || row.CreatedAt < RecordedAt || row.InventoryIdentityCorrectionId == CorrectionId);

    internal static LegacyGrowerReconciliationOrigin? Scope(InventoryIdentityCorrection change)
    {
        try
        {
            using var doc = JsonDocument.Parse(change.SourceIdentitySnapshotJson);
            var s = doc.RootElement;
            return new(s.GetProperty("WarehouseId").GetInt32(), s.GetProperty("RoomId").GetInt32(),
                change.SourceCropYear, change.SourceFruitProfileId, s.GetProperty("Lot").GetString()!, change.TargetGrowerLotId,
                change.CreatedAt, change.Id);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return null; }
    }

    internal bool IsProven(InventoryIdentityCorrection change, RoomInventoryAdjustment[] ledger, AuditLog[] audits, DateTimeOffset asOf)
    {
        if (!change.IsActive || !change.IsComplete || change.CreatedAt > asOf || change.SourceGrowerLotId != null
            || change.CorrectedReceiptId != null || change.ReceiptInventoryOverrideId != null
            || change.SourceCropYear != change.TargetCropYear || change.SourceFruitProfileId != change.TargetFruitProfileId
            || string.IsNullOrWhiteSpace(Lot) || string.IsNullOrWhiteSpace(change.OperationKey) || string.IsNullOrWhiteSpace(change.Reason)
            || change.ExpectedAdjustmentCount != 2 || change.ExpectedTreatmentMovementCount != 0 || audits.Length != 1) return false;
        var legs = ledger.Where(x => x.InventoryIdentityCorrectionId == change.Id).OrderBy(x => x.Id).ToArray();
        if (legs.Length != 2) return false;
        var debit = legs[0]; var credit = legs[1]; var audit = audits[0];
        if (debit.GrowerLotId != null || credit.GrowerLotId != TargetGrowerLotId || debit.ChangeAmount >= 0
            || -(long)debit.ChangeAmount != credit.ChangeAmount || debit.OldBinCount != credit.ChangeAmount || debit.NewBinCount != 0
            || credit.OldBinCount == null || (long)credit.OldBinCount + credit.ChangeAmount != credit.NewBinCount
            || audit.Action != "LegacyGrowerLotReconciliation" || audit.UserId != change.CreatedByUserId
            || audit.CreatedAt != change.CreatedAt || audit.SourceApplication != "CropQc.Web reviewed legacy Grower Lot reconciliation") return false;
        for (var i = 0; i < legs.Length; i++)
        {
            var leg = legs[i];
            if (!Contains(leg) || leg.AdjustmentType != "InventoryIdentityCorrection" || leg.InventoryInvariantVersion != 0
                || leg.InventoryOperationKey != $"identity-correction:{change.OperationKey}:{i + 1}"
                || leg.CreatedAt != change.CreatedAt || leg.AdjustmentAt != change.CreatedAt
                || leg.CreatedByUserId != change.CreatedByUserId || leg.Reason != change.Reason
                || leg.ReceiptId != null || leg.ReceiptInventoryOverrideId != null || leg.RoomTransferId != null
                || leg.InterCrewTransferId != null || leg.OutsideWarehouseTransferId != null || leg.ProcessorShipmentLineId != null
                || leg.ActualRunId != null || leg.ActualRunRevisionId != null || leg.RoomInventoryLossId != null || leg.RoomDepletionId != null) return false;
        }
        var prior = ledger.Where(x => Contains(x) && x.CreatedAt < change.CreatedAt).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
        if (prior.Where(x => x.GrowerLotId == null).Sum(x => (long)x.ChangeAmount) != credit.ChangeAmount
            || prior.Where(x => x.GrowerLotId == TargetGrowerLotId).Sum(x => (long)x.ChangeAmount) != credit.OldBinCount) return false;
        // The original recorded arrivals must fund every earlier withdrawal.
        // A postdated arrival or compensating credit cannot cure a historical deficit.
        long running = 0;
        foreach (var row in prior)
            if ((running += row.ChangeAmount) < 0) return false;
        try
        {
            using var sourceDoc = JsonDocument.Parse(change.SourceIdentitySnapshotJson);
            using var targetDoc = JsonDocument.Parse(change.TargetIdentitySnapshotJson);
            using var beforeDoc = JsonDocument.Parse(audit.BeforeValuesJson ?? "null");
            using var afterDoc = JsonDocument.Parse(audit.AfterValuesJson ?? "null");
            var source = sourceDoc.RootElement; var target = targetDoc.RootElement;
            if (!JsonElement.DeepEquals(source, beforeDoc.RootElement) || !JsonElement.DeepEquals(target, afterDoc.RootElement)
                || source.GetProperty("GrowerLotId").ValueKind != JsonValueKind.Null
                || target.GetProperty("GrowerLotId").GetInt32() != TargetGrowerLotId
                || source.GetProperty("CurrentBins").GetInt32() != credit.ChangeAmount
                || target.GetProperty("ExistingTargetBins").GetInt32() != credit.OldBinCount
                || target.GetProperty("CurrentBins").GetInt32() != credit.NewBinCount
                || string.IsNullOrWhiteSpace(source.GetProperty("StateToken").GetString())) return false;
            foreach (var snapshot in new[] { source, target })
                if (snapshot.GetProperty("WarehouseId").GetInt32() != WarehouseId || snapshot.GetProperty("RoomId").GetInt32() != RoomId
                    || snapshot.GetProperty("CropYear").GetInt32() != CropYear || snapshot.GetProperty("FruitProfileId").GetInt32() != FruitProfileId
                    || snapshot.GetProperty("GrowerNumber").GetString() != Lot
                    || snapshot.GetProperty("IsOrganic").GetBoolean() != source.GetProperty("IsOrganic").GetBoolean()) return false;
            var expected = prior.Where(x => x.GrowerLotId == null && x.ChangeAmount > 0).Select(x => x.Id).Order().ToArray();
            var recorded = source.GetProperty("PositiveSourceAdjustmentIds").EnumerateArray().Select(x => x.GetInt64()).Order().ToArray();
            return expected.Length > 0 && recorded.SequenceEqual(expected);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return false; }
    }
}
