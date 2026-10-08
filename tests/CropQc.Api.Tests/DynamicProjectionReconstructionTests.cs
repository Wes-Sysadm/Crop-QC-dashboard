using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Fixture = CropQc.Api.Tests.ProjectionReconstructionTests.Fixture;

namespace CropQc.Api.Tests;

public sealed class DynamicProjectionReconstructionTests
{
    [InventoryPostgresTheory]
    [InlineData("state")]
    [InlineData("signature")]
    [InlineData("application")]
    [InlineData("identity-key")]
    [InlineData("warehouse")]
    [InlineData("room")]
    [InlineData("lot")]
    [InlineData("variety")]
    [InlineData("crop")]
    [InlineData("organic")]
    [InlineData("production-type")]
    [InlineData("grower-number")]
    [InlineData("status")]
    [InlineData("disposition")]
    [InlineData("quantity")]
    [InlineData("receipt")]
    [InlineData("unexpected-current")]
    [InlineData("retired-quantity")]
    [InlineData("retired-metadata")]
    [InlineData("approval")]
    [InlineData("seal")]
    [InlineData("ledger")]
    public async Task Independent_verifier_rejects_tampering_even_after_valid_new_receiving(string mutation)
    {
        await using var f = await Fixture.Create();
        var request = await f.Request();
        var repaired = await f.Executor.ReconstructProjectionAsync(request, true);
        Assert.Equal("Committed", repaired.Status);
        Assert.Equal("Verified", (await f.Executor.VerifyProjectionReconstructionAsync(request.OperationKey)).Status);
        // A genuine later event must never mask an invalid original replacement.
        await Receive(f, 7, "AFTER-REPAIR");
        await using (var db = f.LegacyDb())
        {
            var row = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == repaired.ReplacementSegmentId);
            switch (mutation)
            {
                case "state": row.TreatmentState = "Unknown"; break;
                case "signature": row.TreatmentSignature = "unknown"; break;
                case "identity-key": row.IdentityKey += "X"; break;
                case "warehouse": row.WarehouseId = 1; break;
                case "room":
                    db.Rooms.Add(new() { Id = 9008, WarehouseId = 9001, Code = "WRONG", Name = "Wrong room" }); await db.SaveChangesAsync();
                    row.RoomId = 9008; break;
                case "lot": row.LotNumberSnapshot = "WRONG"; break;
                case "variety": row.VarietyCodeSnapshot = "WRONG"; break;
                case "crop": row.CropYear--; break;
                case "organic": row.IsOrganicSnapshot = true; break;
                case "production-type": row.ProductionTypeSnapshot = "Organic"; break;
                case "grower-number": row.GrowerNumberSnapshot = "WRONG"; break;
                case "status": row.InventoryStatusSnapshot = "WRONG"; break;
                case "disposition": row.Disposition = "Historical"; break;
                case "quantity": row.CurrentBins++; break;
                case "receipt": row.ReceiptId = 100000; break;
                case "unexpected-current": db.TreatmentLineageSegments.Add(Clone(row, 400000, 1)); break;
                case "retired-quantity": (await db.TreatmentLineageSegments.SingleAsync(x => x.Id == request.Preview.Plan!.Changes[0].Id)).RetiredQuantity++; break;
                case "retired-metadata": (await db.TreatmentLineageSegments.SingleAsync(x => x.Id == request.Preview.Plan!.Changes[0].Id)).GrowerNameSnapshot = "CHANGED"; break;
                case "approval": (await db.AuditLogs.SingleAsync(x => x.Id == request.ApprovalAuditId)).Action = "CHANGED"; break;
                case "seal":
                    var command = await db.InventoryCommands.SingleAsync(x => x.OperationKey == request.OperationKey);
                    command.ResultJson = command.ResultJson.Replace(repaired.VerificationSeal!, "CHANGED", StringComparison.Ordinal); break;
                case "ledger": (await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000)).Notes = "CHANGED"; break;
                case "application":
                    var app = new RoomTreatmentApplication
                    {
                        RoomId = 9002,
                        WarehouseId = 9001,
                        TreatmentChemicalId = 1,
                        OperationKey = "tamper-app",
                        AppliedByUserId = 8000,
                        CreatedByUserId = 8000,
                        ProductNameSnapshot = "Test",
                        CropSnapshot = "Apples",
                        UnitSnapshot = "L",
                        CurrencySnapshot = "USD",
                        AppliedAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    db.RoomTreatmentApplications.Add(app); await InjectFixtureChanges(f, db);
                    row.Applications.Add(new() { RoomTreatmentApplicationId = app.Id }); break;
            }
            await InjectFixtureChanges(f, db);
        }
        var before = await f.Snapshot();
        await Assert.ThrowsAnyAsync<Exception>(() => f.Executor.VerifyProjectionReconstructionAsync(request.OperationKey));
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("one-receipt", 13, 0, 0)]
    [InlineData("multiple-receipts", 11, 17, 0)]
    [InlineData("packing-after-receiving", 23, 4, 9)]
    [InlineData("old-stock-depleted", 31, 0, 19)]
    [InlineData("transfer-out", 21, 8, 7)]
    [InlineData("both-directions", 17, 9, 6)]
    [InlineData("reversal", 16, 7, 4)]
    [InlineData("loss", 14, 6, 5)]
    [InlineData("different-lot", 18, 0, 0)]
    public async Task Fresh_authority_follows_committed_transactions_and_preserves_current_populations(
        string scenario, int first, int second, int deducted)
    {
        await using var f = await Fixture.Create();
        var opening = await Authority(f);
        await Receive(f, first, "NEW-ONE");
        if (second > 0) await Receive(f, second, "NEW-TWO");
        if (scenario == "different-lot")
        {
            await using var db = f.LegacyDb();
            db.GrowerLots.Add(new() { Id = 100500, Grower = "Other", LotNumber = "OTHER-LOT" }); await db.SaveChangesAsync();
            await Receive(f, 33, "OTHER-RECEIPT", 100500);
        }
        if (deducted > 0)
        {
            var kind = scenario is "transfer-out" or "both-directions" or "reversal" ? InventoryCommandKind.RoomMove
                : scenario == "loss" ? InventoryCommandKind.Loss : InventoryCommandKind.Dump;
            await Operate(f, kind, deducted);
            if (scenario == "both-directions") await Operate(f, InventoryCommandKind.RoomMove, deducted, 9003, 9002);
            if (scenario == "reversal")
            {
                await using var db = f.CreateDbContext();
                var parent = await db.RoomTransfers.SingleAsync();
                var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, f.Executor);
                Assert.Null(await dashboard.ReverseRoomTransferAsync(new() { Id = parent.Id, Reason = "Disposable compensation" }, default));
            }
        }
        var expected = opening + first + second - (scenario is "both-directions" or "reversal" ? 0 : deducted);
        Assert.Equal(expected, await Authority(f));
        var preserved = await Current(f);
        await AddStaleAlias(f);
        var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.True(p.Eligible, string.Join(";", p.Blockers));
        Assert.Equal(expected, p.AuthoritativeQuantity);
        Assert.All(p.Plan!.Changes, x => Assert.DoesNotContain(x.Id, preserved.Select(s => s.Id)));
        var result = await Repair(f, p);
        Assert.Equal("Verified", (await f.Executor.VerifyProjectionReconstructionAsync(result.OperationKey)).Status);
        Assert.Equal(expected, await Authority(f));
        Assert.Equal(JsonSerializer.Serialize(preserved), JsonSerializer.Serialize(await Current(f)));
        Assert.Equal(0, result.AuthoritativeQuantityDelta);
    }

    [InventoryPostgresFact]
    public async Task Later_receiving_is_distinguished_from_an_invalid_historical_repair_and_replay_is_read_only()
    {
        await using var f = await Fixture.Create();
        var request = await f.Request();
        var result = await f.Executor.ReconstructProjectionAsync(request, true);
        var opening = await Authority(f);
        await Receive(f, 9, "AFTER-ONE"); await Receive(f, 16, "AFTER-TWO");
        Assert.Equal(opening + 9 + 16, await Authority(f));
        var before = await f.Snapshot();
        Assert.Equal("VerifiedWithLaterActivity", (await f.Executor.VerifyProjectionReconstructionAsync(result.OperationKey)).Status);
        Assert.Equal("Replayed", (await f.Executor.ReconstructProjectionAsync(request, true)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Invalid_original_commit_cannot_be_excused_by_a_valid_current_replacement_or_later_receiving()
    {
        await using var f = await Fixture.Create();
        var request = await f.Request(); var repair = await f.Executor.ReconstructProjectionAsync(request, true);
        await Receive(f, 8, "LATER-VALID");
        await using (var db = f.LegacyDb())
        {
            var audit = await db.AuditLogs.SingleAsync(x => x.Id == repair.RepairAuditId);
            var after = JsonNode.Parse(audit.AfterValuesJson!)!;
            var evidence = after["committedEvidence"]!;
            var projection = evidence["position"]!["projections"]!.AsArray().Single(x => x!["id"]!.GetValue<long>() == repair.ReplacementSegmentId)!;
            projection["state"] = "Unknown";
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
            var type = typeof(InventoryCommandExecutor).GetNestedType("ReconstructionCommitEvidence", System.Reflection.BindingFlags.NonPublic)!;
            var typed = JsonSerializer.Deserialize(evidence.ToJsonString(options), type, options);
            var seal = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(typed, type, options))));
            audit.AfterValuesJson = after.ToJsonString(options);
            var command = await db.InventoryCommands.SingleAsync(x => x.OperationKey == request.OperationKey);
            var result = JsonNode.Parse(command.ResultJson)!; result["verificationSeal"] = seal; command.ResultJson = result.ToJsonString(options);
            await InjectFixtureChanges(f, db);
        }
        var before = await f.Snapshot();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => f.Executor.VerifyProjectionReconstructionAsync(request.OperationKey));
        Assert.Contains("Committed replacement violates", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Later_treatment_is_recognized_without_certifying_unsupported_current_allocation()
    {
        await using var f = await Fixture.Create(); var request = await f.Request();
        var repaired = await f.Executor.ReconstructProjectionAsync(request, true);
        await Operate(f, InventoryCommandKind.TreatmentAssignment, await Authority(f));
        var before = await f.Snapshot();
        Assert.Equal("CommittedEvidenceVerifiedCurrentReviewRequired", (await f.Executor.VerifyProjectionReconstructionAsync(repaired.OperationKey)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("receiving")]
    [InlineData("transfer")]
    [InlineData("packing")]
    public async Task Committed_activity_between_preview_and_execution_requires_fresh_approval(string operation)
    {
        await using var f = await Fixture.Create();
        var request = await f.Request();
        if (operation == "receiving") await Receive(f, 8, "STALE-RECEIPT");
        else await Operate(f, operation == "transfer" ? InventoryCommandKind.RoomMove : InventoryCommandKind.Dump, 4);
        var snapshot = await f.Snapshot();
        Assert.Equal("Blocked", (await f.Executor.ReconstructProjectionAsync(request, true)).Status);
        Assert.Equal(snapshot, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Duplicate_receipt_ticket_is_not_a_second_origin()
    {
        await using var f = await Fixture.Create();
        await Receive(f, 8, "DUPLICATE-TICKET");
        var before = await f.Snapshot();
        var duplicate = await f.Executor.ExecuteAsync(ReceiptCommand(8, "DUPLICATE-TICKET"));
        Assert.Equal(InventoryCommandStatus.Conflict, duplicate.Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Missing_movement_parent_cannot_be_hidden_by_balanced_new_populations()
    {
        await using var f = await Fixture.Create();
        await Receive(f, 13, "PARENT-RECEIPT");
        await Operate(f, InventoryCommandKind.RoomMove, 5);
        await AddStaleAlias(f);
        await using (var db = f.LegacyDb())
        { foreach (var move in await db.TreatmentLineageMovements.Where(x => x.RoomTransferId != null).ToArrayAsync()) move.RoomTransferId = null; await InjectFixtureChanges(f, db); }
        var before = await f.Snapshot();
        var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.False(p.Eligible); Assert.NotEmpty(p.Blockers);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData(16, false, false)]
    [InlineData(5, false, false)]
    [InlineData(0, true, false)]
    [InlineData(16, false, true)]
    [InlineData(5, false, true)]
    public async Task Audited_receipt_corrections_and_void_use_committed_effects(int corrected, bool isVoid, bool afterMovement)
    {
        await using var f = await Fixture.Create();
        var opening = await Authority(f);
        await Receive(f, 11, "CORRECT-LATER");
        if (afterMovement)
        {
            await Operate(f, InventoryCommandKind.RoomMove, 4); await Operate(f, InventoryCommandKind.Dump, 3);
            opening -= 4 + 3;
        }
        await using (var db = f.CreateDbContext())
        {
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "CORRECT-LATER");
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, f.Executor);
            var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
            if (!isVoid)
            {
                var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, receipt.Id, corrected);
                Assert.Null((await service.ApplyEditAsync(form, principal, default)).Error);
            }
            else
            {
                var p = (await service.GetPreviewAsync(receipt.Id, default))!;
                Assert.Null((await service.VoidAsync(new()
                {
                    Id = receipt.Id,
                    ExpectedConcurrencyVersion = p.ConcurrencyVersion,
                    ExpectedInventoryStateToken = p.InventoryStateToken,
                    ConfirmationValue = "CORRECT-LATER",
                    ConfirmDeletion = true,
                    ConfirmInventoryChange = true,
                    Reason = "Disposable void",
                    OperationToken = Guid.NewGuid().ToString("N")
                }, principal, default)).Error);
            }
        }
        Assert.Equal(opening + corrected, await Authority(f));
        var preserved = await Current(f); await AddStaleAlias(f);
        var preview = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.True(preview.Eligible, string.Join(';', preview.Blockers));
        Assert.Equal(opening + corrected, preview.AuthoritativeQuantity);
        var result = await Repair(f, preview);
        Assert.Equal("Verified", (await f.Executor.VerifyProjectionReconstructionAsync(result.OperationKey)).Status);
        Assert.Equal(JsonSerializer.Serialize(preserved), JsonSerializer.Serialize(await Current(f)));
    }

    [InventoryPostgresTheory]
    [InlineData("PartiallyReceived", 4)]
    [InlineData("InTransit", 4)]
    [InlineData("Received", 4)]
    [InlineData("ReceivedNeedsReview", 4)]
    public async Task Unsupported_partial_acknowledged_held_or_incomplete_settlement_is_never_room_stock(string status, int acknowledged)
    {
        await using var f = await Fixture.Create();
        await Receive(f, 13, "PARTIAL-ORIGIN");
        await Operate(f, InventoryCommandKind.InterCompanyDispatch, 7);
        var physical = await Authority(f); await AddStaleAlias(f);
        long id;
        await using (var db = f.LegacyDb())
        {
            var parent = await db.InterCrewTransfers.SingleAsync(); id = parent.Id;
            parent.Status = status; parent.BinsReceived = acknowledged;
            await InjectFixtureChanges(f, db);
        }
        var before = await f.Snapshot();
        var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.False(p.Eligible); Assert.Contains(p.Blockers, x => x.Contains($"InterCrewTransfer {id}", StringComparison.Ordinal));
        Assert.Equal(physical, p.AuthoritativeQuantity); Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task In_transit_dispatch_is_counted_once_and_kept_separate_from_room_quantity()
    {
        await using var f = await Fixture.Create();
        var original = await Authority(f); await Receive(f, 13, "TRANSIT-ORIGIN");
        await Operate(f, InventoryCommandKind.InterCompanyDispatch, 7); await AddStaleAlias(f);
        var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.True(p.Eligible, string.Join(';', p.Blockers));
        Assert.Equal(original + 13 - 7, p.AuthoritativeQuantity);
        Assert.Equal(7, Assert.Single(p.OtherCustody).AuthoritativeQuantity);
        var result = await Repair(f, p);
        Assert.Equal("Verified", (await f.Executor.VerifyProjectionReconstructionAsync(result.OperationKey)).Status);
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Point_in_time_treatment_and_new_untreated_receiving_are_preserved_when_mixed_repair_is_blocked(bool transfer)
    {
        await using var f = await Fixture.Create();
        await Receive(f, 12, "BEFORE-TREATMENT");
        await Operate(f, InventoryCommandKind.TreatmentAssignment, await Authority(f));
        await Receive(f, 9, "AFTER-TREATMENT");
        var current = await Current(f);
        Assert.Equal(9, current.Where(x => x.CurrentBins > 0 && x.TreatmentState == "Untreated").Sum(x => x.CurrentBins));
        Assert.Contains(current, x => x.CurrentBins > 0 && x.TreatmentState == "Confirmed");
        if (transfer)
        {
            var signature = current.First(x => x.TreatmentState == "Confirmed").TreatmentSignature;
            await Operate(f, InventoryCommandKind.RoomMove, 3, signature: signature);
            await using var db = f.CreateDbContext();
            var moved = await db.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.RoomId == 9003 && x.CurrentBins > 0);
            Assert.Equal(3, moved.CurrentBins); Assert.Equal("Confirmed", moved.TreatmentState);
            Assert.Equal(signature, moved.TreatmentSignature); Assert.NotEmpty(moved.Applications);
        }
        await AddStaleAlias(f);
        var before = await f.Snapshot(); var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.False(p.Eligible); Assert.NotEmpty(p.Blockers); Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Completely_depleted_history_does_not_overwrite_a_subsequent_receipt()
    {
        await using var f = await Fixture.Create();
        await Operate(f, InventoryCommandKind.Dump, await Authority(f));
        Assert.Equal(0, await Authority(f));
        await Receive(f, 27, "NEW-OCCUPANCY");
        var current = await Current(f); await AddStaleAlias(f);
        var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.True(p.Eligible, string.Join(';', p.Blockers)); Assert.Equal(27, p.AuthoritativeQuantity);
        await Repair(f, p); Assert.Equal(JsonSerializer.Serialize(current), JsonSerializer.Serialize(await Current(f)));
    }

    [InventoryPostgresFact]
    public async Task Cross_company_matched_truck_receipt_is_not_another_physical_origin()
    {
        await using var f = await Fixture.Create();
        await using (var db = f.LegacyDb())
        {
            (await db.Warehouses.SingleAsync(x => x.Id == 4)).Code = "BASE-WP"; await db.SaveChangesAsync();
            (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = "WP";
            db.Rooms.Add(new() { Id = 9003, WarehouseId = 9001, Code = "DEST", Name = "Destination" }); await db.SaveChangesAsync();
        }
        var legacy = new InventoryCommandTests.Fixture(f.Connection);
        var receive = await legacy.ReceiveCommand();
        var received = await f.Executor.ExecuteAsync(receive);
        Assert.True(received.Status == InventoryCommandStatus.Committed, received.Detail);
        var extra = ReceiptCommand(12, "NEW-DESTINATION") with { Receipt = new(2026, 9006, 9007, 100000, 9004, "NEW-DESTINATION", 12) };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Executor.ExecuteAsync(extra)).Status);
        TreatmentLineageSegment[] current;
        await using (var db = f.LegacyDb())
        {
            Assert.Equal(0, await db.RoomInventoryAdjustments.CountAsync(x => x.ReceiptId == receive.ReceivingEvidence!.ReceiptId && x.AdjustmentType == "ReceiptAdd"));
            current = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == 9007 && x.Disposition == "Current").OrderBy(x => x.Id).ToArrayAsync();
            db.TreatmentLineageSegments.Add(Clone(current[0], 400000, 8)); await InjectFixtureChanges(f, db);
        }
        var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target with { WarehouseId = 9006, RoomId = 9007 });
        Assert.True(p.Eligible, string.Join(';', p.Blockers));
        Assert.Equal(receive.Lines.Sum(x => x.Quantity) + extra.Receipt.Quantity, p.AuthoritativeQuantity);
        var result = await Repair(f, p);
        Assert.Equal("Verified", (await f.Executor.VerifyProjectionReconstructionAsync(result.OperationKey)).Status);
        await using var after = f.LegacyDb();
        Assert.Equal(JsonSerializer.Serialize(current), JsonSerializer.Serialize(await after.TreatmentLineageSegments.AsNoTracking()
            .Where(x => x.RoomId == 9007 && x.Disposition == "Current").OrderBy(x => x.Id).ToArrayAsync()));
    }

    private static InventoryCommand ReceiptCommand(int quantity, string ticket, int grower = 100000) => new(
        Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000, DateTimeOffset.UtcNow, "Disposable later receiving", [],
        Receipt: new(2026, 9001, 9002, grower, 9004, ticket, quantity));
    private static async Task Receive(Fixture f, int quantity, string ticket, int grower = 100000)
    {
        var result = await f.Executor.ExecuteAsync(ReceiptCommand(quantity, ticket, grower));
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
    }
    private static async Task Operate(Fixture f, InventoryCommandKind kind, int quantity, int room = 9002, int destination = 9003, string signature = "u")
    {
        await using (var db = f.LegacyDb())
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == 9003)) db.Rooms.Add(new() { Id = 9003, WarehouseId = 9001, Code = "DEST", Name = "Destination" });
            if (kind is InventoryCommandKind.Dump or InventoryCommandKind.InterCompanyDispatch)
            {
                (await db.Warehouses.SingleAsync(x => x.Id == 4)).Code = "BASE-WP"; await db.SaveChangesAsync();
                (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = "WP";
            }
            await db.SaveChangesAsync();
        }
        await using var read = f.CreateDbContext();
        var e = (await new InventoryEvidenceLoader(read).LoadAsync(new(9001, [room]), DateTimeOffset.UtcNow, default)).Positions.Single(x => x.Identity.Key == f.Target.Identity.Key);
        var r = InventoryAvailabilityResolver.Resolve(e, new());
        var result = await f.Executor.ExecuteAsync(new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Disposable later event",
            [new(new(r.Identity, r.Location, r.Watermark.Fingerprint, r.Watermark.Versions), quantity, signature, kind == InventoryCommandKind.RoomMove ? new(9001, destination) : null)],
            CustodyGroup: kind == InventoryCommandKind.InterCompanyDispatch ? "EBS" : null,
            TreatmentChemicalId: kind == InventoryCommandKind.TreatmentAssignment ? await read.TreatmentChemicals.Where(x => x.ApplicationLevel == "Room" && x.Crop == "Apples" && x.IsActive).Select(x => x.Id).FirstAsync() : null));
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
    }
    private static async Task<int> Authority(Fixture f)
    {
        await using var db = f.CreateDbContext();
        return (await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(9001, [9002], default))
            .Where(x => x.GrowerLotId == f.Target.Identity.GrowerLotId).Sum(x => x.CurrentBins);
    }
    private static async Task<TreatmentLineageSegment[]> Current(Fixture f)
    {
        await using var db = f.CreateDbContext();
        return await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == 9002 && x.GrowerLotId == 100000
            && x.Disposition == "Current").OrderBy(x => x.Id).ToArrayAsync();
    }
    private static TreatmentLineageSegment Clone(TreatmentLineageSegment row, long id, int quantity)
    {
        var clone = JsonSerializer.Deserialize<TreatmentLineageSegment>(JsonSerializer.Serialize(row))!;
        clone.Id = id; clone.IdentityKey += "CONVENTIONAL"; clone.InventoryStatusSnapshot = "Conventional";
        clone.CurrentBins = quantity; clone.ReceiptId = null; clone.Applications.Clear(); return clone;
    }
    private static async Task AddStaleAlias(Fixture f)
    {
        await using var db = f.LegacyDb();
        var row = await db.TreatmentLineageSegments.AsNoTracking().FirstAsync(x => x.RoomId == 9002 && x.GrowerLotId == 100000 && x.Disposition == "Current" && x.CurrentBins > 0);
        db.TreatmentLineageSegments.Add(Clone(row, 400000, 12)); await InjectFixtureChanges(f, db);
    }
    private static async Task<ProjectionReconstructionResult> Repair(Fixture f, ProjectionReconstructionPreview p)
    {
        var approval = await f.Executor.ApproveProjectionReconstructionAsync(f.Approval(p), true);
        var result = await f.Executor.ReconstructProjectionAsync(new(Guid.NewGuid().ToString("N"), approval, 8000, true, p), true);
        Assert.Equal("Committed", result.Status); return result;
    }

    // Deliberate corruption/projection-defect injection is not an application
    // workflow. Bypass no production guard: SQL is restricted to this disposable
    // database, with table/column names supplied only by EF model metadata.
    private static async Task InjectFixtureChanges(Fixture f, CropQcDbContext db)
    {
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
        db.ChangeTracker.DetectChanges();
        await using var connection = new NpgsqlConnection(f.Connection); await connection.OpenAsync();
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified).ToArray())
        {
            var table = entry.Metadata.GetTableName()!;
            var key = entry.Metadata.FindPrimaryKey()!.Properties;
            var values = entry.Properties.ToDictionary(x => x.Metadata.Name, x => x.CurrentValue);
            var columns = entry.State == EntityState.Modified ? entry.Properties.Where(x => x.IsModified).Select(x => x.Metadata.Name).ToArray()
                : entry.Properties.Where(x => !(x.Metadata.IsPrimaryKey() && x.Metadata.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never
                    && (x.IsTemporary || x.CurrentValue?.ToString() == "0"))).Select(x => x.Metadata.Name).ToArray();
            var sql = entry.State == EntityState.Modified
                ? $"UPDATE \"{table}\" t SET " + string.Join(',', columns.Select(x => $"\"{x}\"=v.\"{x}\""))
                    + $" FROM jsonb_populate_record(NULL::\"{table}\",CAST(@data AS jsonb)) v WHERE " + string.Join(" AND ", key.Select(x => $"t.\"{x.Name}\"=v.\"{x.Name}\""))
                : $"INSERT INTO \"{table}\" (" + string.Join(',', columns.Select(x => $"\"{x}\"")) + ") SELECT "
                    + string.Join(',', columns.Select(x => $"v.\"{x}\"")) + $" FROM jsonb_populate_record(NULL::\"{table}\",CAST(@data AS jsonb)) v"
                    + (key.Count == 1 ? $" RETURNING \"{key[0].Name}\"" : "");
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("data", JsonSerializer.Serialize(values));
            var saved = await command.ExecuteScalarAsync();
            if (entry.State == EntityState.Added && key.Count == 1 && saved != null)
            { entry.Property(key[0].Name).CurrentValue = saved; entry.Property(key[0].Name).IsTemporary = false; }
            entry.State = EntityState.Unchanged;
        }
    }
}
