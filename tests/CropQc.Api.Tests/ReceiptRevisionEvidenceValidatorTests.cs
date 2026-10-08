using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

public sealed class ReceiptRevisionEvidenceValidatorTests
{
    [Theory]
    [InlineData(1373, 66, true)]
    [InlineData(1339, 6, true)]
    [InlineData(1754, 64, false)]
    public void Valid_revision_arithmetic_never_proves_surviving_custody(long id, int original, bool deleted)
    {
        var f = Fixture(id, original, deleted);
        var result = ReceiptRevisionEvidenceValidator.Evaluate(f.Evidence, f.Receipt, [f.Revision], [f.Debit], [f.Audit]);
        Assert.True(result.CorrectionChainValid, string.Join(';', result.MissingEvidence));
        Assert.Equal(original, result.OriginalQuantity);
        Assert.Equal(0, result.EffectiveReceiptQuantity);
        Assert.False(result.ExactRemainingAllocationProven);
        Assert.False(result.UntreatedPoolProven);
        Assert.Contains(result.MissingEvidence, x => x.Contains("Ledger debit 2"));
        f.Revision.Reason = "Supervisor says every bin is untreated";
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(
            ReceiptRevisionEvidenceValidator.Evaluate(f.Evidence, f.Receipt, [f.Revision], [f.Debit], [f.Audit])));
    }

    [Theory]
    [InlineData("missing-debit")]
    [InlineData("wrong-identity")]
    [InlineData("changed-current")]
    [InlineData("changed-lot")]
    [InlineData("duplicate-operation")]
    [InlineData("changed-audit")]
    [InlineData("malformed")]
    [InlineData("chronology")]
    public void Inconsistent_corrections_are_reported_without_throwing_or_promoting_proof(string fault)
    {
        var f = Fixture(1373, 66, true);
        if (fault == "wrong-identity") f.Debit.GrowerLotId++;
        if (fault == "changed-current") f.Receipt.BinCount++;
        if (fault == "changed-lot") f.Receipt.LotCode = "OTHER";
        if (fault == "changed-audit") f.Audit.BeforeValuesJson = "{}";
        if (fault == "malformed") f.Revision.AfterReceiptSnapshotJson = "{}";
        if (fault == "chronology") f.Audit.CreatedAt = f.Audit.CreatedAt.AddSeconds(1);
        var result = ReceiptRevisionEvidenceValidator.Evaluate(f.Evidence, f.Receipt,
            fault == "duplicate-operation" ? [f.Revision, f.Revision] : [f.Revision],
            fault == "missing-debit" ? [] : [f.Debit], [f.Audit]);
        Assert.False(result.CorrectionChainValid);
        Assert.False(result.ExactRemainingAllocationProven);
        Assert.False(result.UntreatedPoolProven);
        Assert.NotEmpty(result.MissingEvidence);
    }

    private static (InventoryPositionEvidence Evidence, Receipt Receipt, ReceiptInventoryOverride Revision, RoomInventoryAdjustment Debit, AuditLog Audit)
        Fixture(long id, int original, bool deleted)
    {
        var at = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        string Snapshot(int bins, bool isDeleted, long version) => JsonSerializer.Serialize(new
        {
            id,
            cropYear = 2026,
            warehouseId = 1,
            roomId = 2,
            fruitProfileId = 3,
            growerLotId = 4,
            growerNumber = "2350",
            lotCode = "2350",
            receiptType = "Truck receipt",
            receivedAt = at.AddDays(-1),
            binCount = bins,
            isDeleted,
            concurrencyVersion = version
        });
        var receipt = new Receipt
        {
            Id = id,
            CropYear = 2026,
            WarehouseId = 1,
            RoomId = 2,
            FruitProfileId = 3,
            GrowerLotId = 4,
            GrowerNumber = "2350",
            LotCode = "2350",
            GrowerName = "Test",
            CompuTechReceiptId = "TEST",
            ReceivedAt = at.AddDays(-1),
            BinCount = deleted ? original : 0,
            IsDeleted = deleted,
            ConcurrencyVersion = 1
        };
        var revision = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            ReceiptId = id,
            ActionType = deleted ? "VoidReceipt" : "QuantityCorrection",
            OldReceiptBinCount = original,
            NewReceiptBinCount = 0,
            InventoryDelta = -original,
            AdministratorUserId = 9,
            CreatedAt = at,
            Reason = "Duplicate ticket",
            OperationKey = "test",
            BeforeReceiptSnapshotJson = Snapshot(original, false, 0),
            AfterReceiptSnapshotJson = Snapshot(receipt.BinCount, deleted, 1),
            AffectedInventorySnapshotJson = "[]",
            ExpectedAdjustmentCount = 1,
            IsComplete = true
        };
        var debit = new RoomInventoryAdjustment
        {
            Id = 2,
            ReceiptId = id,
            ReceiptInventoryOverrideId = revision.Id,
            RoomId = 2,
            WarehouseId = 1,
            GrowerLotId = 4,
            FruitProfileId = 3,
            CropYear = 2026,
            LotNumber = "2350",
            GrowerName = "Test",
            ChangeAmount = -original,
            AdjustmentType = "ReceiptAdminOverride",
            CreatedAt = at,
            AdjustmentAt = at
        };
        var audit = new AuditLog
        {
            Id = 10,
            UserId = 9,
            CreatedAt = at,
            EntityName = "ReceiptInventoryOverride",
            EntityKey = revision.Id.ToString(),
            Action = revision.ActionType,
            BeforeValuesJson = revision.BeforeReceiptSnapshotJson,
            AfterValuesJson = JsonSerializer.Serialize(new { afterReceiptSnapshotJson = revision.AfterReceiptSnapshotJson }),
            SourceApplication = "Test"
        };
        var evidence = new InventoryPositionEvidence(new(2026, 4, 3, "2350", "2350", "DANJ", "Conventional", false, ""),
            new(InventoryCustody.Room, 1, 2, "Test", "Test"), 202, 0, true, true,
            [new(1, original, "ReceiptAdd", at.AddDays(-1), id, null, true), new(2, -original, "ReceiptAdminOverride", at, id, null, true)],
            [], [], [new(id, receipt.BinCount, true, deleted, false, at, 1)], [], new("test", "Serializable", []));
        return (evidence, receipt, revision, debit, audit);
    }
}
