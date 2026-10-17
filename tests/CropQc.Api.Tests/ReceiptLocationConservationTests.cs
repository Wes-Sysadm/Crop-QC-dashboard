using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptLocationConservationTests
{
    [InventoryPostgresFact]
    public async Task Canonical_location_correction_conserves_receipt_treatment_history_and_passes_both_reports()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var treated = await f.Execute((await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id });
        Assert.Equal(InventoryCommandStatus.Committed, treated.Status);
        var original = JsonSerializer.Serialize(await db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        var applications = JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        var audits = await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var receiptBefore = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        var before = await new InventoryReceiptAvailability(db).ReadAsync(100000, default);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 19);
        form.RoomId = 9003;
        Assert.Null((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        db.ChangeTracker.Clear();
        var op = await db.ReceiptInventoryOverrides.SingleAsync();
        var pair = await db.RoomInventoryAdjustments.Where(x => x.ReceiptInventoryOverrideId == op.Id).OrderBy(x => x.ChangeAmount).ToArrayAsync();
        Assert.Equal(new[] { -19, 19 }, pair.Select(x => x.ChangeAmount));
        Assert.Equal(new[] { "CorrectOriginalRoomOut", "CorrectOriginalRoomIn" }, pair.Select(x => x.AdjustmentType));
        Assert.All(pair, x => Assert.Equal(100000, x.ReceiptId));
        Assert.True(op.IsComplete); Assert.Equal(2, op.ExpectedAdjustmentCount); Assert.Equal(0, op.InventoryDelta);
        Assert.Equal(0, await f.Physical()); Assert.Equal(19, await f.Physical(9003));
        var receiptAfter = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        Assert.Equal(receiptBefore.BinCount, receiptAfter.BinCount);
        Assert.Equal(receiptBefore.ReceivedAt, receiptAfter.ReceivedAt);
        Assert.Equal(1, await db.Receipts.CountAsync());
        Assert.Equal(original, JsonSerializer.Serialize(await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.ReceiptInventoryOverrideId == null).OrderBy(x => x.Id).ToArrayAsync()));
        Assert.Equal(applications, JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
        Assert.Equal(JsonSerializer.Serialize(audits), JsonSerializer.Serialize(await db.AuditLogs.AsNoTracking().Where(x => x.Id <= audits.Last().Id).OrderBy(x => x.Id).ToArrayAsync()));
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "CanonicalReceiptLocationCorrected" && x.EntityKey == op.Id.ToString()).ToArrayAsync());
        var after = await new InventoryReceiptAvailability(db).ReadAsync(100000, default);
        static string Slices(InventoryReceiptAvailabilityResult x) => JsonSerializer.Serialize(x.Allocations.Select(a => new { a.Slice.Quantity, a.Slice.State, a.Slice.Signature, a.Slice.ApplicationIds }));
        Assert.Equal(Slices(before!), Slices(after!));
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
        var report = await Report(db);
        Assert.Equal(0, report.Global.Difference);
        Assert.Equal(0, report.Global.Categories["Identity/location corrections"]);
        Assert.True(report.IsReady, JsonSerializer.Serialize(report));
        using var sql = await SqlReport(f.Connection);
        Assert.Empty(sql.RootElement.GetProperty("unclassified_types").EnumerateArray());
        Assert.True(sql.RootElement.GetProperty("location_corrections_ready").GetBoolean());
        Assert.Empty(report.InvalidLocationCorrections);
        Assert.Equal(saved, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("missing-counterpart")]
    [InlineData("missing-pair-and-audit")]
    [InlineData("unequal")]
    [InlineData("wrong-parent")]
    [InlineData("incomplete-parent")]
    [InlineData("conflicting-receipt")]
    [InlineData("duplicate")]
    [InlineData("duplicate-direction")]
    [InlineData("missing-parent")]
    [InlineData("missing-parent-record")]
    [InlineData("missing-audit")]
    [InlineData("wrong-source")]
    [InlineData("malformed-snapshot")]
    [InlineData("unsupported")]
    public async Task Malformed_or_unknown_location_events_fail_both_reports_without_writing(string scenario)
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using (var db = factory.CreateDbContext())
        {
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 19);
            form.RoomId = 9003;
            Assert.Null((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        }
        // Deliberate corruption belongs only to the disposable synthetic fixture.
        await using var seed = f.CreateDbContext();
        var parent = await seed.ReceiptInventoryOverrides.SingleAsync();
        var rows = await seed.RoomInventoryAdjustments.Where(x => x.ReceiptInventoryOverrideId == parent.Id).OrderBy(x => x.ChangeAmount).ToArrayAsync();
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
        var damage = scenario switch
        {
            "missing-pair-and-audit" => "DELETE FROM \"RoomInventoryAdjustments\" WHERE \"ReceiptInventoryOverrideId\"=@parent; DELETE FROM \"AuditLogs\" WHERE \"Action\"='CanonicalReceiptLocationCorrected' AND \"EntityKey\"=@parent::text",
            "missing-parent-record" => "UPDATE \"RoomInventoryAdjustments\" SET \"ReceiptInventoryOverrideId\"=NULL WHERE \"ReceiptInventoryOverrideId\"=@parent; DELETE FROM \"ReceiptInventoryOverrides\" WHERE \"Id\"=@parent",
            "missing-counterpart" => "DELETE FROM \"RoomInventoryAdjustments\" WHERE \"Id\"=@incoming",
            "unequal" => "UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=\"ChangeAmount\"-1,\"NewBinCount\"=\"NewBinCount\"-1 WHERE \"Id\"=@incoming",
            "wrong-parent" => "UPDATE \"ReceiptInventoryOverrides\" SET \"ActionType\"='QuantityCorrection' WHERE \"Id\"=@parent",
            "incomplete-parent" => "UPDATE \"ReceiptInventoryOverrides\" SET \"IsComplete\"=false WHERE \"Id\"=@parent",
            "conflicting-receipt" => "UPDATE \"RoomInventoryAdjustments\" SET \"ReceiptId\"=100001 WHERE \"Id\"=@incoming",
            "duplicate" => "INSERT INTO \"RoomInventoryAdjustments\" SELECT (jsonb_populate_record(NULL::\"RoomInventoryAdjustments\",to_jsonb(r)||jsonb_build_object('Id',r.\"Id\"+100000,'InventoryOperationKey',r.\"InventoryOperationKey\"||':duplicate'))).* FROM \"RoomInventoryAdjustments\" r WHERE r.\"Id\"=@incoming",
            "duplicate-direction" => "UPDATE \"RoomInventoryAdjustments\" SET \"AdjustmentType\"='CorrectOriginalRoomOut' WHERE \"Id\"=@incoming",
            "missing-parent" => "UPDATE \"RoomInventoryAdjustments\" SET \"ReceiptInventoryOverrideId\"=NULL WHERE \"Id\"=@outgoing",
            "missing-audit" => "DELETE FROM \"AuditLogs\" WHERE \"Action\"='CanonicalReceiptLocationCorrected' AND \"EntityKey\"=@parent::text",
            "wrong-source" => "UPDATE \"ReceiptInventoryOverrides\" SET \"AffectedInventorySnapshotJson\"=replace(\"AffectedInventorySnapshotJson\",'\"sourceRoomId\":9002','\"sourceRoomId\":9003') WHERE \"Id\"=@parent",
            "malformed-snapshot" => "UPDATE \"ReceiptInventoryOverrides\" SET \"AffectedInventorySnapshotJson\"='{' WHERE \"Id\"=@parent",
            "unsupported" => "UPDATE \"RoomInventoryAdjustments\" SET \"AdjustmentType\"='UnreviewedExternalAddition' WHERE \"Id\"=@incoming",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        // App writer fences correctly reject corruption. Seed malformed evidence via
        // a separate test-only connection; never disable the application's fences.
        await using (var connection = new NpgsqlConnection(f.Connection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(damage, connection);
            command.Parameters.AddWithValue("parent", parent.Id);
            command.Parameters.AddWithValue("incoming", rows[1].Id);
            command.Parameters.AddWithValue("outgoing", rows[0].Id);
            Assert.Equal(scenario is "missing-pair-and-audit" or "missing-parent-record" ? 3 : 1, await command.ExecuteNonQueryAsync());
        }
        seed.ChangeTracker.Clear();
        var saved = await f.Snapshot();
        var report = await Report(seed);
        Assert.False(report.IsReady);
        Assert.NotEmpty(report.InvalidLocationCorrections);
        if (scenario == "unsupported") Assert.Contains("UnreviewedExternalAddition", report.UnclassifiedTypes);
        using var sql = await SqlReport(f.Connection);
        Assert.False(sql.RootElement.GetProperty("location_corrections_ready").GetBoolean());
        Assert.Equal(report.InvalidLocationCorrections.Where(x => x.CorrectionId != null).Select(x => x.CorrectionId!.Value).Order(),
            sql.RootElement.GetProperty("invalid_location_correction_ids").EnumerateArray().Select(x => x.GetGuid()).Order());
        Assert.Equal(report.InvalidLocationCorrections.Where(x => x.CorrectionId == null).SelectMany(x => x.AdjustmentIds).Order(),
            sql.RootElement.GetProperty("orphan_location_adjustment_ids").EnumerateArray().Select(x => x.GetInt64()).Order());
        if (scenario == "unsupported") Assert.Contains("UnreviewedExternalAddition", sql.RootElement.GetProperty("unclassified_types").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(saved, await f.Snapshot());
    }

    private static Task<InventoryConservationReport> Report(CropQcDbContext db) =>
        new InventoryConservationReportService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), 2026).AnalyzeAsync(default);

    private static async Task<JsonDocument> SqlReport(string connection)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CropQc.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "scripts/postgresql/report-receipt-inventory-conservation.sql"));
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx).ExecuteNonQueryAsync();
        var json = (string)(await new NpgsqlCommand(sql, c, tx).ExecuteScalarAsync())!;
        return JsonDocument.Parse(json);
    }
}
