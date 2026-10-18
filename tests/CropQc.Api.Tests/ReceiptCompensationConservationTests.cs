using System.Collections.Immutable;
using System.Text.Json;
using static CropQc.Api.Tests.ReceiptPlacementBaselineTests;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptCompensationConservationTests
{
    [InventoryPostgresFact]
    public async Task Mixed_compensation_preserves_treatment_unknown_ancestry_and_original_history()
    {
        await using var f = await RecordedMixedDispatchTests.History(29);
        await using var db = f.CreateDbContext();
        db.Rooms.Add(new() { Id = 9010, WarehouseId = 1, Code = "BASELINE", Name = "Synthetic placement" });
        await db.SaveChangesAsync();
        var source = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(652, source.AuthoritativeQuantity);
        Assert.Equal(442, source.RawProjectionQuantity);
        var originals = await History(db);
        var dispatch = new InventoryCommand("baseline-mixed", InventoryCommandKind.InterCompanyDispatch, 8000, DateTimeOffset.UtcNow,
            "Synthetic mixed placement", [Line(source, 2, "u"), Line(source, 2, "u|a:29")], CustodyGroup: "EBS");
        var sent = await f.Execute(dispatch); Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var parent = await db.InterCrewTransfers.SingleAsync(x => x.Id == sent.Effects[0].ParentId);
        var receipt = new Receipt
        {
            CropYear = 2026,
            CompuTechReceiptId = "SYNTH-BASELINE",
            ReceiptType = "Truck receipt",
            IsTransferReceipt = true,
            WarehouseId = 1,
            RoomId = 9010,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            GrowerName = "Corpus",
            LotCode = "CORPUS-100000",
            BinCount = 4,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        receipt.VarietyLines.Add(new() { FruitProfileId = 9004, BinCount = 4 });
        parent.ReceivingReceipt = receipt; parent.ConcurrencyVersion++; await db.SaveChangesAsync();
        var moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.InterCrewTransferId == parent.Id).OrderBy(x => x.Id).ToArrayAsync();
        await Commit(f, await Command(f, parent.Id, receipt.Id, InventoryCommandKind.AcknowledgeTransfer,
            moves.Select(x => new InventoryCustodyQuantity(x.Id, 2)).ToImmutableArray(), 1, 9010));
        var acks = await db.ReceiptCustodyAcknowledgments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        await MixedProof(db, 4, 0);
        var place = await Command(f, parent.Id, receipt.Id, InventoryCommandKind.PlaceReceiptCustody,
            acks.Select(x => new InventoryCustodyQuantity(x.Id, x.Quantity)).ToImmutableArray(), 1, 9010);
        await Commit(f, place);
        await MixedProof(db, 0, 4);
        var placements = await db.ReceiptCustodyPlacements.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        await Commit(f, await Command(f, parent.Id, receipt.Id, InventoryCommandKind.ReverseReceiptPlacement,
            placements.Select(x => new InventoryCustodyQuantity(x.Id, 1)).ToImmutableArray(), 1, 9010));
        await MixedProof(db, 2, 2);
        var saved = await f.Snapshot();
        var report = await Report(db);
        using var sql = await SqlReport(f.Connection);
        Assert.Equal(saved, await f.Snapshot());
        Assert.True(report.IsReady, JsonSerializer.Serialize(report));
        Assert.True(sql.RootElement.GetProperty("receipt_compensations_ready").GetBoolean(), sql.RootElement.ToString());
        Assert.Equal(2, report.ReceiptHeldBins);
        Assert.Equal(originals, await History(db));
        Assert.Equal(10, await db.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        Assert.All(await db.TreatmentLineageMovements.Where(x => x.MovementType == "ReceiptPlacementReversal").ToArrayAsync(), x => Assert.Equal(moves.Single(d => d.TreatmentSignatureSnapshot == x.TreatmentSignatureSnapshot).ReceiptId, x.ReceiptId));
    }

    [InventoryPostgresTheory]
    [InlineData("orphan")]
    [InlineData("missing-placement")]
    [InlineData("wrong-ack")]
    [InlineData("wrong-transfer")]
    [InlineData("debit")]
    [InlineData("missing-movement")]
    [InlineData("reverses-wrong-movement")]
    [InlineData("treatment")]
    [InlineData("source")]
    [InlineData("identity")]
    [InlineData("duplicate")]
    [InlineData("over-reversal")]
    [InlineData("held-accounting")]
    [InlineData("missing-audit")]
    [InlineData("intent")]
    [InlineData("unknown")]
    [InlineData("before-opening")]
    public async Task Malformed_compensation_fails_both_reports_without_writing(string scenario)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand(treated: true);
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 7);
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.ReverseReceiptPlacement, 2);
        await using var db = f.CreateDbContext();
        var r = await db.ReceiptCustodyReversals.AsNoTracking().SingleAsync();
        var p = await db.ReceiptCustodyPlacements.AsNoTracking().SingleAsync();
        // Deliberate malformed history belongs only to the disposable synthetic database.
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
        var damage = scenario switch
        {
            "orphan" => "DELETE FROM \"ReceiptCustodyReversals\" WHERE \"Id\"=@r",
            "missing-placement" => "UPDATE \"ReceiptCustodyReversals\" SET \"PlacementId\"=NULL WHERE \"Id\"=@r",
            "wrong-ack" => "INSERT INTO \"ReceiptCustodyAcknowledgments\" SELECT (jsonb_populate_record(NULL::\"ReceiptCustodyAcknowledgments\",to_jsonb(a)||jsonb_build_object('Id',a.\"Id\"+10000,'OperationKey','different-ack'))).* FROM \"ReceiptCustodyAcknowledgments\" a WHERE a.\"Id\"=@a; UPDATE \"ReceiptCustodyReversals\" SET \"AcknowledgmentId\"=@a+10000 WHERE \"Id\"=@r",
            "wrong-transfer" => "UPDATE \"RoomInventoryAdjustments\" SET \"InterCrewTransferId\"=NULL WHERE \"Id\"=@l",
            "debit" or "before-opening" => "UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=-3,\"NewBinCount\"=4 WHERE \"Id\"=@l",
            "missing-movement" => "UPDATE \"ReceiptCustodyReversals\" SET \"MovementId\"=NULL WHERE \"Id\"=@r",
            "reverses-wrong-movement" => "UPDATE \"TreatmentLineageMovements\" SET \"ReversesTreatmentLineageMovementId\"=NULL WHERE \"Id\"=@m",
            "treatment" => "UPDATE \"TreatmentLineageMovements\" SET \"TreatmentStateSnapshot\"='Untreated',\"TreatmentSignatureSnapshot\"='u' WHERE \"Id\"=@m",
            "source" => "UPDATE \"TreatmentLineageMovements\" SET \"SourceSegmentId\"=(SELECT \"SourceSegmentId\" FROM \"TreatmentLineageMovements\" WHERE \"Id\"=@original) WHERE \"Id\"=@m",
            "identity" => "UPDATE \"TreatmentLineageMovements\" SET \"IdentityKey\"='unproven' WHERE \"Id\"=@m",
            "duplicate" => "INSERT INTO \"ReceiptCustodyReversals\" SELECT (jsonb_populate_record(NULL::\"ReceiptCustodyReversals\",to_jsonb(r)||jsonb_build_object('Id',r.\"Id\"+10000,'OperationKey','duplicate-compensation','InventoryAdjustmentId',NULL,'MovementId',NULL))).* FROM \"ReceiptCustodyReversals\" r WHERE r.\"Id\"=@r",
            "over-reversal" => "UPDATE \"ReceiptCustodyReversals\" SET \"Quantity\"=8 WHERE \"Id\"=@r",
            "held-accounting" => "UPDATE \"InterCrewTransfers\" SET \"BinsReceived\"=18,\"VarianceBins\"=-1",
            "missing-audit" => "DELETE FROM \"AuditLogs\" WHERE \"Action\"='CanonicalInventoryCommand' AND \"EntityKey\"=@key",
            "intent" => "UPDATE \"InventoryCommands\" SET \"IntentJson\"='{' WHERE \"OperationKey\"=@key",
            "unknown" => "UPDATE \"RoomInventoryAdjustments\" SET \"AdjustmentType\"='UnreviewedExternalAddition' WHERE \"Id\"=@l",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        await using (var c = new NpgsqlConnection(f.Connection))
        {
            await c.OpenAsync();
            // Prove the database constraint rejects these malformed shapes first.
            // Only this isolated negative fixture removes the check to model broken
            // historical evidence; the reports must independently reject it too.
            if (scenario is "missing-placement" or "missing-movement" or "duplicate")
            {
                await using var rejected = new NpgsqlCommand(damage, c);
                rejected.Parameters.AddWithValue("r", r.Id);
                var error = await Assert.ThrowsAsync<PostgresException>(() => rejected.ExecuteNonQueryAsync());
                Assert.Equal("23514", error.SqlState);
                await new NpgsqlCommand("ALTER TABLE \"ReceiptCustodyReversals\" DROP CONSTRAINT \"CK_ReceiptCustodyReversals_Evidence\"", c).ExecuteNonQueryAsync();
            }
            await using var cmd = new NpgsqlCommand(damage, c);
            cmd.Parameters.AddWithValue("r", r.Id); cmd.Parameters.AddWithValue("a", r.AcknowledgmentId);
            cmd.Parameters.AddWithValue("l", r.InventoryAdjustmentId!.Value); cmd.Parameters.AddWithValue("m", r.MovementId!.Value);
            cmd.Parameters.AddWithValue("original", p.MovementId); cmd.Parameters.AddWithValue("key", r.OperationKey);
            Assert.True(await cmd.ExecuteNonQueryAsync() > 0);
            if (scenario == "before-opening")
            {
                await using var opening = new NpgsqlCommand("INSERT INTO \"RoomInventoryAdjustments\" SELECT (jsonb_populate_record(NULL::\"RoomInventoryAdjustments\",to_jsonb(l)||jsonb_build_object('Id',l.\"Id\"+10000,'AdjustmentType','StartingInventoryImport','ReceiptId',NULL,'InventoryOperationKey','synthetic-opening','AdjustmentAt',l.\"AdjustmentAt\"+interval '1 day','CreatedAt',l.\"CreatedAt\"+interval '1 day','ChangeAmount',5,'NewBinCount',5))).* FROM \"RoomInventoryAdjustments\" l WHERE l.\"Id\"=@l", c);
                opening.Parameters.AddWithValue("l", r.InventoryAdjustmentId.Value);
                Assert.Equal(1, await opening.ExecuteNonQueryAsync());
            }
        }
        var saved = await f.Snapshot();
        var report = await Report(db);
        using var sql = await SqlReport(f.Connection);
        Assert.Equal(saved, await f.Snapshot());
        Assert.False(report.IsReady); Assert.NotEmpty(report.InvalidReceiptCompensations);
        Assert.False(sql.RootElement.GetProperty("receipt_compensations_ready").GetBoolean(), sql.RootElement.ToString());
        Assert.Equal(report.InvalidReceiptCompensations.Where(x => x.ReversalId != null).Select(x => x.ReversalId!.Value).Order(),
            sql.RootElement.GetProperty("invalid_receipt_compensation_ids").EnumerateArray().Select(x => x.GetInt64()).Order());
        Assert.Equal(report.InvalidReceiptCompensations.Where(x => x.ReversalId == null).SelectMany(x => x.AdjustmentIds).Order(),
            sql.RootElement.GetProperty("orphan_receipt_compensation_adjustment_ids").EnumerateArray().Select(x => x.GetInt64()).Order());
        Assert.Equal(report.InvalidReceiptCompensations.Where(x => x.ReversalId == null).SelectMany(x => x.MovementIds).Order(),
            sql.RootElement.GetProperty("orphan_receipt_compensation_movement_ids").EnumerateArray().Select(x => x.GetInt64()).Order());
        if (scenario == "unknown")
        {
            Assert.Contains("UnreviewedExternalAddition", report.UnclassifiedTypes);
            Assert.Contains("UnreviewedExternalAddition", sql.RootElement.GetProperty("unclassified_types").EnumerateArray().Select(x => x.GetString()));
        }
    }

    [InventoryPostgresTheory]
    [InlineData("complete", false)]
    [InlineData("partial", false)]
    [InlineData("successive", true)]
    [InlineData("replace", true)]
    [InlineData("occupied", true)]
    [InlineData("stale", false)]
    public async Task Recorded_compensation_passes_both_reports_without_writing(string scenario, bool treated)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand(treated: treated, prepareDestination: scenario is "occupied" or "stale"
            ? () => InventoryMovementDestinationTests.SeedDestination(f, scenario == "stale" ? 9 : 17, 9007, 9006) : null);
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 7);
        await using var db = f.CreateDbContext();
        var origins = await db.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd");
        var reversed = scenario == "complete" ? 7 : 2;
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.ReverseReceiptPlacement, reversed);
        if (scenario == "successive") { await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.ReverseReceiptPlacement, 2); reversed += 2; }
        if (scenario == "replace") { await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, reversed); reversed = 0; }
        await ReceiptCustodyCorrectionTests.Prove(f, receiving, 19, 7 - reversed);
        Assert.Equal(origins, await db.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        Assert.False(InventoryConservationReportService.IsKnownMovement("ReceiptPlacementReversal"));
        var before = await f.Snapshot();
        var report = await Report(db);
        using var sql = await SqlReport(f.Connection);
        Assert.Equal(before, await f.Snapshot());
        Assert.True(report.IsReady, JsonSerializer.Serialize(report));
        Assert.Empty(sql.RootElement.GetProperty("unclassified_types").EnumerateArray());
        Assert.True(sql.RootElement.GetProperty("receipt_compensations_ready").GetBoolean(), sql.RootElement.ToString());
        Assert.Equal(report.ReceiptHeldBins, sql.RootElement.GetProperty("receipt_held_bins").GetInt64());
        Assert.Equal(report.InterCrewTransitBins, sql.RootElement.GetProperty("intercrew_transit").GetInt64());
        Assert.Equal(0, report.Global.Difference);
        Assert.Equal(12 + reversed, report.ReceiptHeldBins);
    }

    internal static Task<InventoryConservationReport> Report(CropQcDbContext db) =>
        new InventoryConservationReportService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), 2026).AnalyzeAsync(default);

    internal static async Task<JsonDocument> SqlReport(string connection)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CropQc.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "scripts/postgresql/report-receipt-inventory-conservation.sql"));
        await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx).ExecuteNonQueryAsync();
        return JsonDocument.Parse((string)(await new NpgsqlCommand(sql, c, tx).ExecuteScalarAsync())!);
    }
}
