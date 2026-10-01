using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryCommandMixedLoadTests
{
    [InventoryPostgresFact]
    public async Task Matched_dispatch_return_requires_and_preserves_receiving_evidence()
    {
        await using var f = await Fixture.Create();
        var receive = await f.ReceiveCommand();
        await using var db = f.CreateDbContext();
        var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        var ret = (await f.CustodyCommand(InventoryCommandKind.Return, InventoryCustody.InTransit, parent.Id))
            with
        { OriginalOperationKey = parent.OperationKey[..^5] };
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(ret)).Status);
        Assert.Equal(before, await f.Snapshot());
        var result = await f.Execute(ret with { ReceivingEvidence = receive.ReceivingEvidence });
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(19, await f.Physical());
        Assert.Null((await db.InterCrewTransfers.AsNoTracking().SingleAsync()).ReceivingReceiptId);
        Assert.Equal(19, (await db.Receipts.SingleAsync(x => x.Id == receive.ReceivingEvidence!.ReceiptId)).BinCount);
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalTruckReceiptUnlink"));
    }
    [InventoryPostgresFact]
    public async Task Mixed_varieties_dispatch_receive_reopen_and_return_as_whole_commands()
    {
        await using var f = await Fixture.Create(2);
        await using var db = f.CreateDbContext();
        db.FruitProfiles.Add(new() { Id = 9009, Name = "Bartlett test", VarietyCode = "CBART", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false });
        db.Rooms.Add(new() { Id = 9008, WarehouseId = 1, Code = "MIXED-RECV", Name = "Mixed receiving room" });
        var second = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100001);
        second.FruitProfileId = 9009; second.VarietyCodeSnapshot = "CBART";
        second.IdentityKey = new InventoryIdentity(2026, 100001, 9009, second.LotNumberSnapshot, second.GrowerNumberSnapshot, "CBART", "Conventional", false, "").Key;
        var receiptSource = await db.Receipts.SingleAsync(x => x.Id == 100001); receiptSource.FruitProfileId = 9009;
        var adjustment = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100001); adjustment.FruitProfileId = 9009; adjustment.VarietyCode = "CBART";
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        static InventoryCommandLine Line(InventoryAvailabilityResult r, InventoryCommandDestination? destination = null) =>
            new(new(r.Identity, r.Location, r.Watermark.Fingerprint, r.Watermark.Versions), r.AuthoritativeQuantity, "u", destination);
        static InventoryCommand Command(InventoryCommandKind kind, IEnumerable<InventoryCommandLine> lines) =>
            new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Mixed-load test", lines.ToImmutableArray());
        var source = await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        var dispatch = Command(InventoryCommandKind.InterCompanyDispatch, source.Positions.Select(x => Line(x))) with { CustodyGroup = "EBS" };
        var sent = await f.Execute(dispatch);
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var parent = Assert.Single(sent.Effects.Select(x => x.ParentId!.Value).Distinct());
        Assert.Equal(0, await f.Physical());
        var receipt = new Receipt
        {
            GrowerName = "Transfer",
            LotCode = "TRANSFER",
            CompuTechReceiptId = "TR-MIXED",
            CropYear = 2026,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            WarehouseId = 1,
            RoomId = 9008,
            BinCount = 38,
            ReceiptType = "Truck receipt",
            IsTransferReceipt = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        receipt.VarietyLines.Add(new() { FruitProfileId = 9004, BinCount = 19 });
        receipt.VarietyLines.Add(new() { FruitProfileId = 9009, BinCount = 19 });
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == parent);
        transfer.ReceivingReceipt = receipt; transfer.ConcurrencyVersion++;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var transit = await resolver.ResolveAsync(new(9001, [], InventoryCustody.InTransit, parent), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        Assert.Equal(2, transit.Positions.Length); Assert.All(transit.Positions, x => Assert.Equal(19, x.AvailableQuantity));
        var receive = Command(InventoryCommandKind.ReceiveTransfer, transit.Positions.Select(x => Line(x, new(1, 9008))))
            with
        { ReceivingEvidence = new(receipt.Id, receipt.ConcurrencyVersion) };
        var received = await f.Execute(receive);
        Assert.True(received.Status == InventoryCommandStatus.Committed, received.Detail);
        var room = await resolver.ResolveAsync(new(1, [9008]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(38, room.Positions.Sum(x => x.AvailableQuantity));
        var reopen = Command(InventoryCommandKind.ReopenTransfer, room.Positions.Select(x => Line(x)))
            with
        { OriginalOperationKey = receive.OperationKey, ReceivingEvidence = new(receipt.Id, receipt.ConcurrencyVersion + 1) };
        var reopened = await f.Execute(reopen);
        Assert.True(reopened.Status == InventoryCommandStatus.Committed, reopened.Detail);
        transit = await resolver.ResolveAsync(new(9001, [], InventoryCustody.InTransit, parent), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        Assert.Equal(38, transit.Positions.Sum(x => x.AvailableQuantity));
        var ret = Command(InventoryCommandKind.Return, transit.Positions.Select(x => Line(x))) with { OriginalOperationKey = dispatch.OperationKey };
        var returned = await f.Execute(ret);
        Assert.True(returned.Status == InventoryCommandStatus.Committed, returned.Detail);
        Assert.Equal(38, await f.Physical());
        Assert.Equal(0, (await resolver.ResolveAsync(new(1, [9008]), new(), DateTimeOffset.UtcNow)).Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(0, await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == parent).SumAsync(x => x.ChangeAmount));
        Assert.Equal(8, await db.TreatmentLineageMovements.CountAsync(x => x.InterCrewTransferId == parent));
    }
}
