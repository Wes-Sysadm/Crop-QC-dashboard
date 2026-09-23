using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class TruckReceiptReconciliationTests
{
    [Theory]
    [InlineData("EBS", "WP", true)]
    [InlineData("EBS", "DH", true)]
    [InlineData("EBS", "McDougall", true)]
    [InlineData("WP", "EBS", true)]
    [InlineData("DH", "EBS", true)]
    [InlineData("McDougall", "EBS", true)]
    [InlineData("WP", "DH", false)]
    [InlineData("WP", "McDougall", false)]
    [InlineData("DH", "McDougall", false)]
    [InlineData("DH", "WP", false)]
    [InlineData("McDougall", "WP", false)]
    [InlineData("McDougall", "DH", false)]
    [InlineData("Unknown", "EBS", false)]
    public void Routing_uses_existing_warehouse_codes(string source, string destination, bool expected) =>
        Assert.Equal(expected, TruckReceiptRoutes.RequiresReceipt(source, destination));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normal_receipts_post_once_but_transfer_receipts_do_not_post_inventory(bool transferReceipt)
    {
        await using var f = await Fixture.CreateAsync();
        var form = f.ReceiptForm(70, transferReceipt);
        var created = await f.Dashboard.CreateReceiptAsync(form, default);
        Assert.True(created.Succeeded, created.Error);
        Assert.Equal(transferReceipt ? 0 : 70, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(transferReceipt ? 0 : 1, await f.Db.RoomInventoryAdjustments.CountAsync(x => x.ReceiptId == created.ReceiptId));
        Assert.Equal(transferReceipt ? 1 : 0, await f.Db.ReceiptVarietyLines.CountAsync(x => x.ReceiptId == created.ReceiptId));
        if (transferReceipt)
        {
            Assert.DoesNotContain(await f.Inventory.GetInventoryAsync(default), x => x.RoomId == f.Destination.Id);
            var rooms = await f.Dashboard.GetAuthoritativeCurrentRoomLotsAsync([f.Destination.Id], default);
            Assert.DoesNotContain(rooms, x => x.CurrentBins > 0);
        }
    }

    [Fact]
    public async Task Dispatch_removes_source_availability_and_cannot_be_received_through_legacy_bypass()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70);
        Assert.Equal(230, await f.BalanceAsync(f.Source.Id));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(70, (await f.Service.ActiveAllocationsAsync(transfer.Id, default)).Sum(x => x.Bins));
        Assert.Equal(InterCrewTransferStatuses.InTransit, transfer.Status);
        var bypass = await f.Transfers.ReceiveAsync(new() { TransferId = transfer.Id, DestinationRoomId = f.Destination.Id, BinsReceived = 70, ReceivedAt = DateTime.Now }, default);
        Assert.False(bypass.Success);
        Assert.Contains("Truck Receipt", bypass.Error);
        var source = Assert.Single(await f.Inventory.GetInventoryAsync(default), x => x.RoomId == f.Source.Id && x.FruitProfileId == f.First.Id);
        Assert.Equal(230, source.AvailableBins);
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
    }

    [Theory]
    [InlineData(68)]
    [InlineData(70)]
    [InlineData(72)]
    public async Task Matching_is_deliberate_and_keeps_quantity_mismatches_visible(int received)
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70);
        var receipt = await f.CreateReceiptAsync(received);
        var page = await f.Service.GetAsync(receipt.Id, null, default);
        Assert.Null(page.Transfer);
        Assert.Contains(page.Candidates, x => x.Id == transfer.Id);
        Assert.Null((await f.Db.InterCrewTransfers.SingleAsync()).ReceivingReceiptId);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        page = await f.Service.GetAsync(receipt.Id, null, default);
        Assert.Equal(received == 70, page.IsReconciled);
        Assert.Equal(received - 70, Assert.Single(page.Comparison).Difference);
        var completion = await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default);
        if (received == 70) Assert.Null(completion);
        else
        {
            Assert.Contains("Reconciliation Required", completion);
            Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
        }
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("variety")]
    public async Task Invalid_candidates_are_filtered(string invalid)
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70);
        var receipt = await f.CreateReceiptAsync(70);
        if (invalid == "destination") receipt.WarehouseId = f.Source.WarehouseId;
        if (invalid == "completed") transfer.Status = InterCrewTransferStatuses.Received;
        if (invalid == "cancelled") transfer.Status = InterCrewTransferStatuses.Reversed;
        if (invalid == "variety") receipt.VarietyLines.Single().FruitProfileId = f.Second.Id;
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        Assert.Empty((await f.Service.GetAsync(receipt.Id, null, default)).Candidates);
    }

    [Fact]
    public async Task One_transfer_and_one_receipt_cannot_be_double_linked()
    {
        await using var f = await Fixture.CreateAsync();
        var one = await f.DispatchAsync(70); var two = await f.DispatchAsync(20);
        var first = await f.CreateReceiptAsync(70); var second = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(first.Id, one.Id), default));
        Assert.NotNull(await f.Service.MatchAsync(await f.FormAsync(second.Id, one.Id), default));
        Assert.NotNull(await f.Service.MatchAsync(await f.FormAsync(first.Id, two.Id), default));
        Assert.Single(await f.Db.InterCrewTransfers.Where(x => x.ReceivingReceiptId != null).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multiple_varieties_must_match_individually_and_preserve_source_identity(bool exact)
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70);
        await f.AddSecondVarietyAsync(transfer.Id, 30);
        var receipt = await f.CreateReceiptAsync(100);
        var form = await f.FormAsync(receipt.Id, transfer.Id);
        form.Lines = [new() { FruitProfileId = f.First.Id, BinCount = exact ? 70 : 69 }, new() { FruitProfileId = f.Second.Id, BinCount = exact ? 30 : 31 }];
        Assert.Null(await f.Service.EditReceiptAsync(form, default));
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var result = await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default);
        if (!exact) { Assert.NotNull(result); Assert.Equal(0, await f.BalanceAsync(f.Destination.Id)); return; }
        Assert.Null(result);
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
        var lineage = await f.Db.TreatmentLineageSegments.Where(x => x.RoomId == f.Destination.Id).ToListAsync();
        Assert.Equal(2, lineage.Count);
        Assert.Equal(70, lineage.Single(x => x.FruitProfileId == f.First.Id).CurrentBins);
        Assert.Equal(30, lineage.Single(x => x.FruitProfileId == f.Second.Id).CurrentBins);
        Assert.All(lineage, x => { Assert.NotEqual(receipt.Id, x.ReceiptId); Assert.Equal(f.Grower.Id, x.GrowerLotId); Assert.Equal(2026, x.CropYear); Assert.False(x.IsOrganicSnapshot); });
        Assert.Empty(await f.Db.RoomInventoryAdjustments.Where(x => x.ReceiptId == receipt.Id).ToListAsync());
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("transfer")]
    public async Task Stale_versions_cannot_complete(string changed)
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var stale = await f.FormAsync(receipt.Id, transfer.Id);
        if (changed == "receipt")
        {
            var edit = await f.FormAsync(receipt.Id, transfer.Id); edit.Lines = [new() { FruitProfileId = f.First.Id, BinCount = 71 }];
            Assert.Null(await f.Service.EditReceiptAsync(edit, default));
        }
        else await f.AddSecondVarietyAsync(transfer.Id, 1);
        Assert.NotNull(await f.Service.CompleteAsync(stale, default));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
    }

    [Fact]
    public async Task Completion_is_idempotent_and_does_not_create_receipt_inventory()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var form = await f.FormAsync(receipt.Id, transfer.Id);
        Assert.Null(await f.Service.CompleteAsync(form, default));
        var count = await f.Db.TreatmentLineageMovements.CountAsync();
        Assert.Null(await f.Service.CompleteAsync(form, default));
        Assert.Equal(count, await f.Db.TreatmentLineageMovements.CountAsync());
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
        Assert.NotNull((await f.Db.Receipts.SingleAsync(x => x.Id == receipt.Id)).TransferCompletedAt);
        Assert.Equal(InterCrewTransferStatuses.Received, (await f.Db.InterCrewTransfers.SingleAsync()).Status);
        Assert.Single(await f.Db.AuditLogs.Where(x => x.Action == "CompleteTransferReceipt").ToListAsync());
    }

    [Fact]
    public async Task Pending_transfer_edits_return_only_owned_bins_and_recompute_reconciliation()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(68);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var allocation = Assert.Single(await f.Service.ActiveAllocationsAsync(transfer.Id, default));
        var form = new TransitEditForm
        {
            TransferId = transfer.Id,
            TransferVersion = (await f.FormAsync(receipt.Id, transfer.Id)).TransferVersion,
            DispatchMovementId = allocation.Movement.Id,
            Bins = 2,
            Reason = "Two bins were not loaded"
        };
        Assert.Null(await f.Service.EditTransferAsync(form, default));
        Assert.Equal(232, await f.BalanceAsync(f.Source.Id));
        Assert.True((await f.Service.GetAsync(receipt.Id, null, default)).IsReconciled);
        Assert.NotNull(await f.Service.EditTransferAsync(form, default)); // stale edit
        form.TransferVersion++; form.DispatchMovementId = 999999;
        Assert.NotNull(await f.Service.EditTransferAsync(form, default));
        Assert.Equal(232, await f.BalanceAsync(f.Source.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_reopen_preserves_history_and_returns_inventory_to_transit(bool downstream)
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var form = await f.FormAsync(receipt.Id, transfer.Id); form.Reason = "Wrong Truck Receipt";
        f.Access.Admin = false;
        Assert.Contains("Admin", await f.Service.ReopenAsync(form, default));
        f.Access.Admin = true;
        if (downstream)
        {
            var destination = await f.Db.TreatmentLineageSegments.SingleAsync(x => x.RoomId == f.Destination.Id);
            f.Db.TreatmentLineageMovements.Add(new()
            {
                OperationKey = Guid.NewGuid().ToString(),
                MovementType = "Treatment",
                SourceSegmentId = destination.Id,
                SourceRoomId = destination.RoomId,
                IdentityKey = destination.IdentityKey,
                TreatmentSignatureSnapshot = "u",
                TreatmentStateSnapshot = "Untreated",
                BinCount = 1,
                CreatedAt = f.Now,
                OccurredAt = f.Now
            });
            await f.Db.SaveChangesAsync();
            Assert.Contains("subsequent", await f.Service.ReopenAsync(form, default));
            Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
            return;
        }
        var originals = await f.Db.TreatmentLineageMovements.Select(x => x.Id).ToListAsync();
        Assert.Null(await f.Service.ReopenAsync(form, default));
        Assert.Equal(230, await f.BalanceAsync(f.Source.Id));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(70, (await f.Service.ActiveAllocationsAsync(transfer.Id, default)).Sum(x => x.Bins));
        Assert.All(originals, id => Assert.True(f.Db.TreatmentLineageMovements.Any(x => x.Id == id)));
        Assert.Single(await f.Db.AuditLogs.Where(x => x.Action == "ReopenUnlinkTransferReceipt").ToListAsync());
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("lineage")]
    [InlineData("identity")]
    public async Task Changed_transit_evidence_fails_closed(string changed)
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        if (changed == "ledger") (await f.Db.RoomInventoryAdjustments.SingleAsync(x => x.InterCrewTransferId == transfer.Id)).ChangeAmount--;
        else if (changed == "lineage") (await f.Db.TreatmentLineageMovements.SingleAsync(x => x.InterCrewTransferId == transfer.Id)).BinCount++;
        else (await f.Db.TreatmentLineageMovements.Include(x => x.SourceSegment).SingleAsync(x => x.InterCrewTransferId == transfer.Id)).SourceSegment!.IdentityKey += "changed";
        await f.Db.SaveChangesAsync();
        Assert.NotNull(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
    }

    [Fact]
    public async Task Returning_last_allocation_cancels_and_unlinks_without_deleting_history()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var allocation = Assert.Single(await f.Service.ActiveAllocationsAsync(transfer.Id, default));
        Assert.Null(await f.Service.EditTransferAsync(new()
        {
            TransferId = transfer.Id,
            TransferVersion = (await f.FormAsync(receipt.Id, transfer.Id)).TransferVersion,
            DispatchMovementId = allocation.Movement.Id,
            Bins = 70,
            Reason = "Load cancelled"
        }, default));
        Assert.Equal(300, await f.BalanceAsync(f.Source.Id));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
        Assert.Empty(await f.Service.ActiveAllocationsAsync(transfer.Id, default));
        Assert.Equal(InterCrewTransferStatuses.Reversed, (await f.Db.InterCrewTransfers.SingleAsync()).Status);
        Assert.Null((await f.Db.InterCrewTransfers.SingleAsync()).ReceivingReceiptId);
        Assert.True(await f.Db.TreatmentLineageMovements.AnyAsync(x => x.Id == allocation.Movement.Id));
        Assert.Empty((await f.Service.GetAsync(receipt.Id, null, default)).Candidates);
    }

    [Fact]
    public async Task Unavailable_in_transit_inventory_cannot_be_added_again()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(300);
        Assert.DoesNotContain(await f.Inventory.GetInventoryAsync(default), x => x.RoomId == f.Source.Id && x.FruitProfileId == f.First.Id && x.IsAvailable);
        Assert.DoesNotContain(await f.Inventory.GetInventoryAsync(default), x => x.RoomId == f.Destination.Id);
        Assert.NotNull(await f.Service.EditTransferAsync(new()
        {
            TransferId = transfer.Id,
            TransferVersion = transfer.ConcurrencyVersion,
            SourceKey = "unavailable",
            ExpectedAvailableBins = 300,
            Bins = 1,
            Reason = "Cannot ship again"
        }, default));
        Assert.Equal(0, await f.BalanceAsync(f.Source.Id));
    }

    [Fact]
    public async Task Linked_receipt_cannot_be_deleted_to_bypass_reopen()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.NotNull(await f.Dashboard.SoftDeleteReceiptAsync(new() { Id = receipt.Id, Reason = "Wrong receipt" }, default));
        Assert.False((await f.Db.Receipts.SingleAsync(x => x.Id == receipt.Id)).IsDeleted);
    }

    [Fact]
    public async Task Treatment_application_and_source_receipt_provenance_survive_receiving()
    {
        await using var f = await Fixture.CreateAsync();
        var chemical = new TreatmentChemical
        {
            ProductName = "Test MCP",
            CommonName = "MCP",
            Crop = "Pears",
            ApplicationLevel = TreatmentApplicationLevels.Receiving,
            Unit = "bin",
            Currency = "USD",
            CreatedAt = f.Now,
            UpdatedAt = f.Now
        };
        f.Db.Add(chemical); await f.Db.SaveChangesAsync();
        var originalReceipt = await f.Db.Receipts.SingleAsync(x => x.RoomId == f.Source.Id && x.FruitProfileId == f.First.Id);
        var treatment = await f.Treatments.ApplyReceiptAsync(new()
        {
            ReceiptId = originalReceipt.Id,
            TreatmentChemicalId = chemical.Id,
            AppliedAt = f.Now,
            OperationKey = "test-treatment",
            ConfirmedReview = true
        }, default);
        Assert.Null(treatment.Error);
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var segment = await f.Db.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.RoomId == f.Destination.Id);
        Assert.Equal(originalReceipt.Id, segment.ReceiptId);
        Assert.Equal(treatment.ApplicationId, Assert.Single(segment.Applications).RoomTreatmentApplicationId);
        Assert.Equal(transfer.TreatmentSignatureSnapshot, segment.TreatmentSignature);
        Assert.True(await f.Db.RoomTreatmentApplications.AnyAsync(x => x.Id == treatment.ApplicationId && x.ReversedAt == null));
    }

    [Fact]
    public async Task Reports_exclude_pending_receipts_and_count_completed_varieties_without_double_inventory()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); await f.AddSecondVarietyAsync(transfer.Id, 30);
        var receipt = await f.CreateReceiptAsync(100);
        var edit = await f.FormAsync(receipt.Id, transfer.Id);
        edit.Lines = [new() { FruitProfileId = f.First.Id, BinCount = 70 }, new() { FruitProfileId = f.Second.Id, BinCount = 30 }];
        Assert.Null(await f.Service.EditReceiptAsync(edit, default));
        var reporting = new RunReportingService(f.Db, new PacificBusinessTimeService(new FixedClock(f.Now)), f.Access, new ConfigurationBuilder().Build());
        var filter = new BinsRunFilterForm { Section = "RunTotals", ReportFacility = "WP", ReportCropYear = 2026 };
        Assert.Equal(0, (await reporting.GetAsync(filter, new ClaimsPrincipal(), default)).Detail!.TotalReceivedBins);
        var conservation = new InventoryConservationReportService(f.Db, f.Ledger, 2026);
        Assert.Equal(0, (await conservation.ReconcileReceiptsAsync(default)).MismatchCount);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var detail = (await reporting.GetAsync(filter, new ClaimsPrincipal(), default)).Detail!;
        Assert.Equal(100, detail.TotalReceivedBins);
        Assert.Equal(70, detail.Varieties.Single(x => x.FruitProfileId == f.First.Id).ReceivedBins);
        Assert.Equal(30, detail.Varieties.Single(x => x.FruitProfileId == f.Second.Id).ReceivedBins);
        Assert.Equal(0, (await conservation.ReconcileReceiptsAsync(default)).MismatchCount);
        var reopen = await f.FormAsync(receipt.Id, transfer.Id); reopen.Reason = "Report reversal";
        Assert.Null(await f.Service.ReopenAsync(reopen, default));
        Assert.Equal(0, (await reporting.GetAsync(filter, new ClaimsPrincipal(), default)).Detail!.TotalReceivedBins);
        Assert.Empty((await conservation.AnalyzeAsync(default)).UnclassifiedTypes);
    }

    [Fact]
    public async Task Completed_inventory_can_move_independently_and_then_blocks_reopen()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var destination = new Room { WarehouseId = f.Destination.WarehouseId, Code = "INDEPENDENT", Name = "Independent", CapacityBins = 1000 };
        f.Db.Add(destination); await f.Db.SaveChangesAsync();
        var page = await f.Dashboard.GetRoomDetailAsync(f.Destination.Id, default);
        Assert.True(page.TransferInventoryReconciles, page.TransferInventoryError);
        var option = Assert.Single(page.TransferLotOptions, x => x.CurrentBins > 0);
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(new()
        {
            FromRoomId = f.Destination.Id,
            DestinationWarehouseId = destination.WarehouseId,
            DestinationRoomId = destination.Id,
            SourceLotKey = option.LotKey,
            TreatmentSegmentId = option.TreatmentSegmentId,
            TreatmentSignature = option.TreatmentSignature,
            BinCount = 10,
            TransferAt = f.Now,
            Reason = "Independent placement"
        }, default));
        Assert.Equal(60, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(10, await f.BalanceAsync(destination.Id));
        var form = await f.FormAsync(receipt.Id, transfer.Id); form.Reason = "Unsafe reopen";
        Assert.Contains("subsequent", await f.Service.ReopenAsync(form, default));
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id) + await f.BalanceAsync(destination.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfers_after_opening_baselines_count_by_movement_time_and_keep_original_receipt_lineage(bool sourceBaseline)
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var room in sourceBaseline ? new[] { f.Source, f.Destination } : new[] { f.Destination })
        {
            var bins = room.Id == f.Source.Id ? 300 : 0;
            f.Db.RoomInventoryAdjustments.Add(new()
            {
                WarehouseId = room.WarehouseId,
                RoomId = room.Id,
                CropYear = 2026,
                GrowerLotId = f.Grower.Id,
                FruitProfileId = f.First.Id,
                GrowerName = f.Grower.Grower,
                LotNumber = f.Grower.LotNumber,
                VarietyCode = f.First.VarietyCode,
                InventoryStatus = "Conventional",
                OldBinCount = 0,
                NewBinCount = bins,
                ChangeAmount = bins,
                AdjustmentType = "StartingInventoryImport",
                Source = "Test baseline",
                Reason = "Test baseline",
                CreatedAt = f.Now.AddHours(-1),
                AdjustmentAt = f.Now.AddHours(-1)
            });
        }
        await f.Db.SaveChangesAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Equal(230, await f.BalanceAsync(f.Source.Id));
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
        var movements = await f.Db.TreatmentLineageMovements.Include(x => x.SourceSegment).Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync();
        Assert.All(movements, x => Assert.Equal(x.SourceSegment!.ReceiptId, x.ReceiptId));
        // Unassigned aggregate provenance stays unassigned; do not invent a receipt link. Explicit treated receipt provenance has its own regression test.
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private static readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot Root = new();
        public RoomTreatmentService Treatments = null!;
        public CropQcDbContext Db = null!;
        public DbContextOptions<CropQcDbContext> Options = null!;
        public Room Source = null!, Destination = null!;
        public FruitProfile First = null!, Second = null!;
        public GrowerLot Grower = null!;
        public User Actor = null!;
        public TestAccess Access = new();
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public RoomInventoryLedgerQueryService Ledger = null!;
        public OutsideWarehouseTransferService Inventory = null!;
        public InterCrewTransferService Transfers = null!;
        public TruckReceiptReconciliationService Service = null!;
        public DashboardDataService Dashboard = null!;
        public Func<IInventoryDeductionInvariantService, TruckReceiptReconciliationService> ServiceWithInvariant = null!;
        public Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? Transaction;
        public static async Task<Fixture> CreateAsync(string? connection = null, string sourceCode = "EBS", string destinationCode = "WP")
        {
            var f = new Fixture();
            var options = new DbContextOptionsBuilder<CropQcDbContext>();
            if (connection is null) options.UseInMemoryDatabase(Guid.NewGuid().ToString(), Root);
            else { ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection); options.UseNpgsql(connection); }
            f.Options = options.Options;
            f.Db = new(f.Options);
            if (connection is null) await f.Db.Database.EnsureCreatedAsync();
            else f.Transaction = await f.Db.Database.BeginTransactionAsync();
            var key = Guid.NewGuid().ToString("N")[..12];
            var source = await f.Db.Warehouses.SingleOrDefaultAsync(x => x.Code == sourceCode) ?? new Warehouse { Code = sourceCode, Name = sourceCode };
            var destination = await f.Db.Warehouses.SingleOrDefaultAsync(x => x.Code == destinationCode) ?? new Warehouse { Code = destinationCode, Name = destinationCode };
            f.Source = new() { Warehouse = source, Code = "SRC-" + key, Name = "Source", CapacityBins = 1000 };
            f.Destination = new() { Warehouse = destination, Code = "DST-" + key, Name = "Destination", CapacityBins = 1000 };
            f.First = new() { Name = "Bartlett test", VarietyCode = "B-" + key, FruitType = "Pear", ProductionType = "Conventional" };
            f.Second = new() { Name = "Anjou test", VarietyCode = "A-" + key, FruitType = "Pear", ProductionType = "Conventional" };
            f.Grower = new() { Grower = "TEST " + key, LotNumber = "9" + key, CreatedAt = f.Now, UpdatedAt = f.Now };
            f.Actor = new() { IsActive = true, DisplayName = "Test", Email = key + "@test.local", Domain = "test.local", EmploymentFacility = EmploymentFacilities.Shared, CreatedAt = f.Now };
            f.Db.AddRange(f.Source, f.Destination, f.First, f.Second, f.Grower, f.Actor);
            await f.Db.SaveChangesAsync();
            var accessor = new TestContextAccessor { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.Email, f.Actor.Email)], "Test")) } };
            var time = new PacificBusinessTimeService(new FixedClock(f.Now));
            f.Ledger = new(f.Db);
            var treatment = new RoomTreatmentService(f.Db, f.Ledger, f.Access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
            f.Treatments = treatment;
            var invariant = new InventoryDeductionInvariantService(f.Db, NullLogger<InventoryDeductionInvariantService>.Instance);
            f.Inventory = new(f.Db, f.Ledger, treatment, treatment, invariant, f.Access, accessor, time);
            f.ServiceWithInvariant = guard => new(f.Db, f.Inventory, treatment, f.Ledger, guard, f.Access, accessor, time);
            f.Service = f.ServiceWithInvariant(invariant);
            f.Transfers = new(f.Db, f.Inventory, f.Ledger, treatment, new InventoryIdentityService(f.Db), invariant, f.Access, accessor, time, f.Service);
            var configuration = new ConfigurationBuilder().Build();
            f.Dashboard = new(f.Db, null!, new FileStorageOptions(), new EmailOptions(), null!, new GoogleAuthenticationOptions(), null!, null!, null!, null!,
                new CropYearService(f.Db, configuration), accessor, configuration, NullLogger<DashboardDataService>.Instance, f.Access,
                businessTime: time, roomInventoryLedgerQueryService: f.Ledger, inventoryDeductionInvariantService: invariant, roomTreatmentService: treatment);
            foreach (var fruit in new[] { f.First, f.Second })
            {
                var form = f.ReceiptForm(fruit == f.First ? 300 : 100, false); form.WarehouseId = f.Source.WarehouseId; form.RoomId = f.Source.Id; form.FruitProfileId = fruit.Id;
                Assert.True((await f.Dashboard.CreateReceiptAsync(form, default)).Succeeded);
            }
            return f;
        }
        public CreateReceiptForm ReceiptForm(int bins, bool transfer) => new()
        {
            CropYear = 2026,
            ReceivedAt = Now.AddHours(-2),
            ConfirmCropYear = true,
            CompuTechReceiptId = "TR" + Guid.NewGuid().ToString("N"),
            WarehouseId = Destination.WarehouseId,
            RoomId = Destination.Id,
            FruitProfileId = First.Id,
            GrowerLotId = Grower.Id,
            BinCount = bins,
            IsTransferReceipt = transfer
        };
        public async Task<Receipt> CreateReceiptAsync(int bins)
        {
            var result = await Dashboard.CreateReceiptAsync(ReceiptForm(bins, true), default);
            Assert.True(result.Succeeded, result.Error);
            return await Db.Receipts.Include(x => x.VarietyLines).SingleAsync(x => x.Id == result.ReceiptId);
        }
        public async Task<InterCrewTransfer> DispatchAsync(int bins)
        {
            var option = Assert.Single(await Inventory.GetInventoryAsync(default), x => x.RoomId == Source.Id && x.FruitProfileId == First.Id);
            var result = await Transfers.DispatchAsync(new()
            {
                SourceWarehouseId = Source.WarehouseId,
                SourceRoomId = Source.Id,
                SourceKey = option.SourceKey,
                ExpectedAvailableBins = option.AvailableBins,
                DestinationCustodyGroup = TruckReceiptRoutes.Group(Destination.Warehouse.Code)!,
                BinsLoaded = bins,
                ConfirmedReview = true,
                LoadedAt = Now.UtcDateTime.AddHours(-7)
            }, default);
            Assert.True(result.Success, result.Error);
            return await Db.InterCrewTransfers.SingleAsync(x => x.Id == result.TransferId);
        }
        public async Task AddSecondVarietyAsync(long transferId, int bins)
        {
            var option = Assert.Single(await Inventory.GetInventoryAsync(default), x => x.RoomId == Source.Id && x.FruitProfileId == Second.Id);
            var transfer = await Db.InterCrewTransfers.SingleAsync(x => x.Id == transferId);
            Assert.Null(await Service.EditTransferAsync(new()
            {
                TransferId = transferId,
                TransferVersion = transfer.ConcurrencyVersion,
                SourceKey = option.SourceKey,
                ExpectedAvailableBins = option.AvailableBins,
                Bins = bins,
                Reason = "Correct loading manifest"
            }, default));
        }
        public async Task<TruckReceiptActionForm> FormAsync(long receipt, long transfer) => new()
        {
            ReceiptId = receipt,
            TransferId = transfer,
            ReceiptVersion = await Db.Receipts.Where(x => x.Id == receipt).Select(x => x.ConcurrencyVersion).SingleAsync(),
            TransferVersion = await Db.InterCrewTransfers.Where(x => x.Id == transfer).Select(x => x.ConcurrencyVersion).SingleAsync()
        };
        public async Task<int> BalanceAsync(int room) => (await Ledger.GetSnapshotsAsync(null, [room], default)).Where(x => x.FruitProfileId == First.Id).Sum(x => x.CurrentBins);
        public async ValueTask DisposeAsync() { if (Transaction is not null) await Transaction.DisposeAsync(); await Db.DisposeAsync(); }
    }
    internal sealed class TestAccess : IUserAccessService
    {
        public bool Admin = true;
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(minimumLevel < PageAccessLevel.Admin || Admin);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(Admin ? PageAccessLevel.Admin : PageAccessLevel.Edit);
        public void InvalidateAll() { }
    }
    internal sealed class TestContextAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}
