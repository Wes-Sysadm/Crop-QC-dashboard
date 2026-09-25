using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropQc.Api.Tests;

// These tests intentionally need the reviewed, unadopted production-shaped restore.
// Every test rolls back its entire transaction, including the release adoption.
[Collection("TruckReceiptAdoptionSerial")]
public sealed class TruckReceiptAdoptionTests
{
    private static string? Connection => Environment.GetEnvironmentVariable("CROPQC_TRUCK_ADOPTION_TEST_CONNECTION");

    [AdoptionFact]
    public async Task All_15_adopt_without_inventory_changes_then_match_and_reconcile_exactly()
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync(Connection, serializable: true);
        await f.Db.Database.ExecuteSqlRawAsync("SET LOCAL TIME ZONE 'UTC'");
        f.Feature.Enabled = false;
        var ordinaryLegacy = await f.DispatchAsync(10);
        var before = await ProtectedFingerprintAsync(f.Db);
        var auditBefore = await f.Db.AuditLogs.CountAsync();
        var oldRows = await f.Db.InterCrewTransfers.AsNoTracking().Where(x => x.Id >= 1 && x.Id <= 15).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(15, oldRows.Count);
        Assert.Equal(630, oldRows.Sum(x => x.BinsLoaded));
        Assert.All(oldRows, x => Assert.False(x.RequiresTruckReceipt));
        await RunCoreAsync(f, apply: false);
        Assert.Equal(before, await ProtectedFingerprintAsync(f.Db));
        await RunCoreAsync(f, apply: true);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(before, await ProtectedFingerprintAsync(f.Db));
        Assert.Equal(auditBefore + 15, await f.Db.AuditLogs.CountAsync());
        var rows = await f.Db.InterCrewTransfers.AsNoTracking().Where(x => x.Id >= 1 && x.Id <= 15).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(oldRows.Select(x => x.BinsLoaded), rows.Select(x => x.BinsLoaded));
        Assert.All(rows, x =>
        {
            Assert.True(x.RequiresTruckReceipt); Assert.Equal(2, x.ConcurrencyVersion);
            Assert.Equal(InterCrewTransferStatuses.InTransit, x.Status);
            Assert.Null(x.ReceivingReceiptId); Assert.Null(x.DestinationRoomId); Assert.Null(x.DestinationWarehouseId);
        });
        Assert.False((await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == ordinaryLegacy.Id)).RequiresTruckReceipt);
        Assert.False(await TruckReceiptReleaseSafety.CanUsePreFeatureApplicationAsync(f.Db, default));
        foreach (var row in rows)
        {
            var detail = (await f.Transfers.GetDetailsAsync(row.Id, default))!;
            Assert.False(detail.CanReceive); Assert.Equal("Awaiting Receipt", detail.ReconciliationStatus);
            Assert.True(detail.RequiresTruckReceipt);
            var audit = await f.Db.AuditLogs.SingleAsync(x => x.Action == "AdoptTruckReceiptWorkflow" && x.EntityKey == $"truck-receipt-initial-adoption-20260924:load:{row.Id}");
            using var beforeAudit = System.Text.Json.JsonDocument.Parse(audit.BeforeValuesJson!);
            using var afterAudit = System.Text.Json.JsonDocument.Parse(audit.AfterValuesJson!);
            Assert.False(beforeAudit.RootElement.GetProperty("RequiresTruckReceipt").GetBoolean());
            Assert.True(afterAudit.RootElement.GetProperty("RequiresTruckReceipt").GetBoolean());
            Assert.Equal(1, beforeAudit.RootElement.GetProperty("ConcurrencyVersion").GetInt64());
            Assert.Equal(2, afterAudit.RootElement.GetProperty("ConcurrencyVersion").GetInt64());
        }
        // A safe repeat fails before any additional audit or state change.
        await f.Transaction!.CreateSavepointAsync("repeat");
        var repeat = await Assert.ThrowsAsync<PostgresException>(() => RunCoreAsync(f, true));
        Assert.Contains("already adopted", repeat.MessageText);
        await f.Transaction.RollbackToSavepointAsync("repeat");
        Assert.Equal(auditBefore + 15, await f.Db.AuditLogs.CountAsync());
        Assert.Equal(before, await ProtectedFingerprintAsync(f.Db));

        f.First = await f.Db.FruitProfiles.SingleAsync(x => x.Id == 10);
        f.Grower = await f.Db.GrowerLots.SingleAsync(x => x.Id == 250);
        f.Feature.Enabled = true; f.Configuration["TruckReceiptReconciliation:Enabled"] = "true";
        var receipt = await f.CreateReceiptAsync(68); // Synthetic receipt only in rolled-back local rehearsal.
        await TruckReceiptHttpTests.VerifyAdoptedUiAsync(f, receipt.Id);
        var candidates = (await f.Service.GetAsync(receipt.Id, null, default)).Candidates;
        Assert.Equal(Enumerable.Range(1, 15).Select(x => (long)x), candidates.Where(x => x.Id <= 15).Select(x => x.Id).Order());
        Assert.DoesNotContain(candidates, x => x.Id == ordinaryLegacy.Id);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, 4), default));
        Assert.Contains("Reconciliation Required", await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, 4), default));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(InterCrewTransferStatuses.InTransit, (await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == 4)).Status);
        f.Feature.Enabled = false;
        Assert.True((await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == 4)).RequiresTruckReceipt);
        Assert.Contains("paused", await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, 4), default));
        Assert.False((await f.Transfers.ReceiveAsync(new() { TransferId = 4, BinsReceived = 70, DestinationRoomId = f.Destination.Id, ReceivedAt = f.Now.UtcDateTime.AddHours(-7), OperationKey = "adopted-no-bypass" }, default)).Success);
        f.Feature.Enabled = true;
        var edit = await f.FormAsync(receipt.Id, 4);
        edit.Lines = [new() { FruitProfileId = 10, BinCount = 35 }, new() { FruitProfileId = f.Second.Id, BinCount = 35 }];
        Assert.Null(await f.Service.EditReceiptAsync(edit, default));
        Assert.Contains("Reconciliation Required", await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, 4), default));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
        edit = await f.FormAsync(receipt.Id, 4);
        edit.Lines = [new() { FruitProfileId = 10, BinCount = 70 }];
        Assert.Null(await f.Service.EditReceiptAsync(edit, default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, 4), default));
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
        Assert.False(await f.Db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id));
    }

    [AdoptionTheory]
    [InlineData("count", "UPDATE \"InterCrewTransfers\" SET \"BinsLoaded\"=69 WHERE \"Id\"=4")]
    [InlineData("source", "UPDATE \"InterCrewTransfers\" SET \"SourceRoomId\"=66 WHERE \"Id\"=4")]
    [InlineData("destination", "UPDATE \"InterCrewTransfers\" SET \"DestinationCustodyGroup\"='EBS' WHERE \"Id\"=4")]
    [InlineData("destination", "UPDATE \"InterCrewTransfers\" SET \"DestinationRoomId\"=66 WHERE \"Id\"=4")]
    [InlineData("not untouched", "UPDATE \"InterCrewTransfers\" SET \"Status\"='Received' WHERE \"Id\"=4")]
    [InlineData("not untouched", "UPDATE \"InterCrewTransfers\" SET \"Status\"='Reversed' WHERE \"Id\"=4")]
    [InlineData("already adopted", "UPDATE \"InterCrewTransfers\" SET \"RequiresTruckReceipt\"=true WHERE \"Id\"=4")]
    [InlineData("already adopted or matched", "UPDATE \"InterCrewTransfers\" SET \"ReceivingReceiptId\"=(SELECT min(\"Id\") FROM \"Receipts\") WHERE \"Id\"=4")]
    [InlineData("movement", "UPDATE \"TreatmentLineageMovements\" SET \"DestinationRoomId\"=66 WHERE \"Id\"=272")]
    [InlineData("movement", "UPDATE \"TreatmentLineageMovements\" SET \"MovementType\"='InterCrewReceive' WHERE \"Id\"=272")]
    [InlineData("ledger", "UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=-69 WHERE \"InterCrewTransferId\"=4")]
    [InlineData("version", "UPDATE \"InterCrewTransfers\" SET \"ConcurrencyVersion\"=2 WHERE \"Id\"=4")]
    [InlineData("identity", "UPDATE \"TreatmentLineageSegments\" SET \"IdentityKey\"='changed' WHERE \"Id\"=216")]
    public async Task Any_bad_load_aborts_the_entire_batch(string reason, string corrupt)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync(Connection, serializable: true);
        await f.Db.Database.ExecuteSqlRawAsync("SET LOCAL TIME ZONE 'UTC'");
        await f.Db.Database.ExecuteSqlRawAsync(corrupt);
        var before = await ProtectedFingerprintAsync(f.Db);
        var modes = await f.Db.InterCrewTransfers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RequiresTruckReceipt, x.ConcurrencyVersion }).ToListAsync();
        var audits = await f.Db.AuditLogs.CountAsync();
        await f.Transaction!.CreateSavepointAsync("bad");
        var error = await Assert.ThrowsAsync<PostgresException>(() => RunCoreAsync(f, true));
        Assert.Contains("Load 4:", error.MessageText); Assert.Contains(reason, error.MessageText);
        await f.Transaction.RollbackToSavepointAsync("bad");
        Assert.Equal(before, await ProtectedFingerprintAsync(f.Db));
        Assert.Equal(modes, await f.Db.InterCrewTransfers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RequiresTruckReceipt, x.ConcurrencyVersion }).ToListAsync());
        Assert.Equal(audits, await f.Db.AuditLogs.CountAsync());
    }

    private static async Task RunCoreAsync(TruckReceiptReconciliationTests.Fixture f, bool apply)
    {
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('cropqc.truck_adoption_apply',{apply.ToString().ToLowerInvariant()},true),set_config('cropqc.repair_actor',{f.Actor.Id.ToString()},true)");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var sql = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "scripts", "postgresql", "truck-receipt-initial-adoption-core.sql"));
        await f.Db.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task<string> ProtectedFingerprintAsync(CropQcDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT concat_ws('|',
          (SELECT md5(string_agg(to_jsonb(a)::text,',' ORDER BY "Id")) FROM "RoomInventoryAdjustments" a),
          (SELECT md5(string_agg(to_jsonb(m)::text,',' ORDER BY "Id")) FROM "TreatmentLineageMovements" m),
          (SELECT md5(string_agg(to_jsonb(s)::text,',' ORDER BY "Id")) FROM "TreatmentLineageSegments" s),
          (SELECT md5(string_agg(to_jsonb(r)::text,',' ORDER BY "Id")) FROM "Receipts" r),
          (SELECT md5(string_agg((CASE WHEN "Id" BETWEEN 1 AND 15 THEN to_jsonb(t)-'RequiresTruckReceipt'-'ConcurrencyVersion' ELSE to_jsonb(t) END)::text,',' ORDER BY "Id")) FROM "InterCrewTransfers" t)
        ) AS "Value"
        """).SingleAsync();

    public sealed class AdoptionFactAttribute : FactAttribute
    {
        public AdoptionFactAttribute() { if (string.IsNullOrWhiteSpace(Connection)) Skip = "Set CROPQC_TRUCK_ADOPTION_TEST_CONNECTION to the reviewed unadopted disposable production restore."; }
    }
    public sealed class AdoptionTheoryAttribute : TheoryAttribute
    {
        public AdoptionTheoryAttribute() { if (string.IsNullOrWhiteSpace(Connection)) Skip = "Set CROPQC_TRUCK_ADOPTION_TEST_CONNECTION to the reviewed unadopted disposable production restore."; }
    }
}

[CollectionDefinition("TruckReceiptAdoptionSerial", DisableParallelization = true)]
public sealed class TruckReceiptAdoptionSerialCollection { }
