using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptSpecificCustodyEvidenceTests
{
    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_receipt_survives_repeated_partial_returns_without_claiming_shared_stock_or_treatment(bool treatedRemainder)
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        if (treatedRemainder)
        {
            var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
            var treatment = (await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id };
            Assert.Equal(InventoryCommandStatus.Committed, (await executor.ExecuteAsync(treatment)).Status);
        }
        var receipt = await Receive(db, executor);
        var room = Assert.Single((await Resolve(db, InventoryCustody.Room)).Positions);
        var sent = await executor.ExecuteAsync(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.InterCompanyDispatch,
            8000, DateTimeOffset.UtcNow, "Recorded mixed dispatch", room.TreatmentSlices.GroupBy(s => s.Signature)
                .Select(g => new InventoryCommandLine(Source(room), g.Sum(s => s.Quantity), g.Key)).ToImmutableArray(), CustodyGroup: "EBS"));
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var parent = sent.Effects[0].ParentId!.Value;
        var departure = await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(m => m.InterCrewTransferId == parent && m.ReceiptId == receipt);
        var original = JsonSerializer.Serialize(departure);
        async Task<string> TreatmentHistory() => JsonSerializer.Serialize(new
        {
            Applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
        var applicationHistory = await TreatmentHistory();
        var returned = 0;
        foreach (var bins in new[] { 2, 3, 2 })
        {
            var custody = Assert.Single((await Resolve(db, InventoryCustody.InTransit, parent)).Positions);
            Assert.Equal(26 - returned, custody.AuthoritativeQuantity);
            Assert.Equal(treatedRemainder ? InventoryConfidence.Proven : InventoryConfidence.Unknown, custody.ReceiptProvenance.Confidence);
            Assert.Equal(19, custody.TreatmentSlices.Where(s => !s.ReceiptEvidenceIds.Contains(receipt)).Sum(s => s.Quantity));
            var before = await f.Snapshot();
            var state = (await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!;
            Assert.Null(state.Blocker);
            Assert.Equal(returned, state.RoomQuantity);
            Assert.Equal(7 - returned, state.CustodyQuantity);
            Assert.All(state.Allocations, a => { Assert.Equal(receipt, Assert.Single(a.Slice.ReceiptEvidenceIds)); Assert.Equal("u", a.Slice.Signature); });
            Assert.Equal(before, await f.Snapshot());
            var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReturnTransitAllocation,
                8000, DateTimeOffset.UtcNow, "Return only the exact receipt departure", [new(Source(custody), bins, "u")],
                PhysicalParentId: parent, ExpectedTransferVersion: await db.InterCrewTransfers.Where(t => t.Id == parent).Select(t => t.ConcurrencyVersion).SingleAsync(),
                DispatchMovementId: departure.Id);
            var result = await executor.ExecuteAsync(command);
            Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
            returned += bins;
            Assert.Equal(returned, await f.Physical());
            Assert.Equal(26, await f.Physical() + (await Resolve(db, InventoryCustody.InTransit, parent)).Positions.Sum(p => p.AuthoritativeQuantity));
            var saved = await f.Snapshot();
            Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(command)).Status);
            Assert.Equal(saved, await f.Snapshot());
            Assert.Equal(InventoryCommandStatus.Conflict, (await executor.ExecuteAsync(command with { Reason = "Changed retry" })).Status);
            Assert.Equal(saved, await f.Snapshot());
            if (returned < 7)
            {
                Assert.Equal(InventoryCommandStatus.Stale, (await executor.ExecuteAsync(command with { OperationKey = Guid.NewGuid().ToString("N") })).Status);
                Assert.Equal(saved, await f.Snapshot());
            }
        }
        var final = (await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!;
        Assert.Null(final.Blocker); Assert.Equal(7, final.RoomQuantity); Assert.Equal(0, final.CustodyQuantity);
        var remaining = Assert.Single((await Resolve(db, InventoryCustody.InTransit, parent)).Positions);
        Assert.Equal(19, remaining.AuthoritativeQuantity);
        Assert.All(remaining.TreatmentSlices, s =>
        {
            if (treatedRemainder) Assert.Equal(100000, Assert.Single(s.ReceiptEvidenceIds));
            else Assert.Empty(s.ReceiptEvidenceIds);
            Assert.Equal(treatedRemainder ? "Confirmed" : "Untreated", s.State);
        });
        Assert.Equal(original, JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(m => m.Id == departure.Id)));
        Assert.Equal(applicationHistory, await TreatmentHistory());
    }

    [InventoryPostgresTheory]
    [InlineData("treatment")]
    [InlineData("membership")]
    [InlineData("custody")]
    public async Task Missing_or_contradictory_selected_departure_proof_blocks_service_and_command_without_writes(string defect)
    {
        await using var f = await Fixture.Create(2);
        await using (var seed = f.CreateDbContext())
        {
            seed.OutsideWarehouses.Add(new() { Id = 9005, Code = "OUT", Name = "Local outside" });
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var receipt = await Receive(db, executor);
        var room = (await Resolve(db, InventoryCustody.Room)).Positions.Single(p => p.Identity.GrowerLotId == 100000);
        var sent = await executor.ExecuteAsync(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.OutsideWarehouseTransfer,
            8000, DateTimeOffset.UtcNow, "Mixed custody negative proof fixture", [new(Source(room), 26, "u")], CounterpartyId: 9005));
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var parent = sent.Effects[0].ParentId!.Value;
        var custody = Assert.Single((await Resolve(db, InventoryCustody.OutsideWarehouse, parent)).Positions);
        Assert.Equal(InventoryConfidence.Unknown, custody.ReceiptProvenance.Confidence);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, receipt, 7);
        form.GrowerLotId = 100001;
        var command = await IdentityCommand(db, receipt);
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
        // Deliberately malformed disposable history; never grant the application
        // a writer capability or disable its canonical guard to seed negatives.
        if (defect == "treatment") await FixtureSql(f, "UPDATE \"TreatmentLineageMovements\" SET \"TreatmentStateSnapshot\"='Unknown', \"TreatmentSignatureSnapshot\"='x' WHERE \"OutsideWarehouseTransferId\"=@p0 AND \"ReceiptId\"=@p1", parent, receipt);
        if (defect == "membership") await FixtureSql(f, "UPDATE \"TreatmentLineageMovements\" SET \"ReceiptId\"=NULL WHERE \"OutsideWarehouseTransferId\"=@p0 AND \"ReceiptId\"=@p1", parent, receipt);
        if (defect == "custody") await FixtureSql(f, "UPDATE \"OutsideWarehouseTransfers\" SET \"BinCount\"=\"BinCount\"+1 WHERE \"Id\"=@p0", parent);
        var before = await f.Snapshot();
        Assert.NotNull((await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!.Blocker);
        Assert.NotNull((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        Assert.Equal(InventoryCommandStatus.Blocked, (await executor.ExecuteAsync(command)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Unallocated_historical_withdrawal_does_not_prove_exact_surviving_receipt_or_absence()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var receipt = await Receive(db, executor);
        var command = await IdentityCommand(db, receipt);
        var room = (await Resolve(db, InventoryCustody.Room)).Positions.Single(p => p.Identity.GrowerLotId == 100000);
        var dump = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.Dump, 8000, DateTimeOffset.UtcNow,
            "Recorded withdrawal with subsequently missing allocation evidence", [new(Source(room), 5, "u")]);
        var consumed = await executor.ExecuteAsync(dump);
        Assert.True(consumed.Status == InventoryCommandStatus.Committed, consumed.Detail);
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
        // Keep the real committed debit; simulate missing historical allocation
        // evidence in this disposable fixture only.
        foreach (var id in consumed.Effects.SelectMany(e => e.MovementIds))
            await FixtureSql(f, "DELETE FROM \"TreatmentLineageMovements\" WHERE \"Id\"=@p0", id);
        var before = await f.Snapshot();
        Assert.NotNull((await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!.Blocker);
        Assert.Equal(InventoryCommandStatus.Blocked, (await executor.ExecuteAsync(command)).Status);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(40, await f.Physical()); // 21 in the source and 19 in the independent target lot.
    }

    [InventoryPostgresFact]
    public async Task Concurrent_receipt_identity_corrections_commit_once_and_preserve_other_stock()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var receipt = await Receive(db, new InventoryCommandExecutor(factory));
        var command = await IdentityCommand(db, receipt);
        var entered = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref entered) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(45));
        });
        var executor = new InventoryCommandExecutor(factory, observer);
        var other = command with { OperationKey = Guid.NewGuid().ToString("N") };
        var results = await Task.WhenAll(executor.ExecuteAsync(command), executor.ExecuteAsync(other));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Stale);
        Assert.Equal(45, await f.Physical());
        Assert.Single(await db.InventoryIdentityCorrections.ToArrayAsync());
        var state = (await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!;
        Assert.Null(state.Blocker); Assert.Equal(7, state.RoomQuantity);
        Assert.All(state.Allocations, a => Assert.Equal(100001, a.Position.Identity.GrowerLotId));
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(results[0].Status == InventoryCommandStatus.Committed ? command : other)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    private static async Task<InventoryCommand> IdentityCommand(CropQcDbContext db, long receipt)
    {
        var state = (await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!;
        Assert.Null(state.Blocker);
        var r = state.Receipt;
        var target = await db.GrowerLots.SingleAsync(x => x.Id == 100001);
        return new(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectReceiptIdentity, 8000, DateTimeOffset.UtcNow,
            "Exact receipt identity evidence", [], ReceiptIdentity: new(r.Id, r.ConcurrencyVersion, state.Fingerprint,
                new(r.CropYear, r.WarehouseId, r.RoomId, r.GrowerLotId!.Value, r.FruitProfileId, r.CompuTechReceiptId, r.BinCount),
                InventoryReceiptAvailability.ReceiptIdentity(r) with { GrowerLotId = target.Id, GrowerNumber = target.LotNumber, Lot = target.LotNumber }));
    }

    private static async Task FixtureSql(Fixture fixture, string sql, params object[] values)
    {
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(fixture.Connection);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(fixture.Connection);
        Assert.Contains(builder.Host, new[] { "127.0.0.1", "localhost", "::1" });
        await using var connection = new Npgsql.NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("p" + i, values[i]);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    internal static async Task<long> Receive(CropQcDbContext db, InventoryCommandExecutor executor)
    {
        var received = await new CanonicalReceivingService(db, executor).ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
            DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "EXACT-SEVEN", 7, "Disposable exact origin", default);
        Assert.True(received.Status == InventoryCommandStatus.Committed, received.Detail);
        return received.Effects.Single().ParentId!.Value;
    }

    private static InventoryCommandSource Source(InventoryAvailabilityResult p) => new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions);
    private static Task<InventoryAvailabilityBatch> Resolve(CropQcDbContext db, InventoryCustody custody, long? parent = null) =>
        new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, custody == InventoryCustody.Room ? [9002] : [], custody, parent),
            new(AllowedCustody: custody), DateTimeOffset.UtcNow);
}
