using System.Globalization;
using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class OutsideWarehouseTransferTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-27T18:00:00Z");

    [Fact]
    public async Task Partial_transfer_removes_exact_bins_without_creating_destination_inventory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var roomCountBefore = await fixture.Db.Rooms.CountAsync();
        var warehouseCountBefore = await fixture.Db.Warehouses.CountAsync();
        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-partial", 200), default);

        Assert.True(result.Success, result.Error);
        var transfer = Assert.Single(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Equal(200, transfer.BinCount);
        Assert.Equal("CUSTOM", transfer.OutsideWarehouseCodeSnapshot);
        Assert.Equal("9350", transfer.GrowerNumberSnapshot);
        Assert.Equal("9350", transfer.LotNumberSnapshot);
        Assert.Equal("TEST-GALA", transfer.VarietyCodeSnapshot);
        Assert.Equal("LOAD-42", transfer.TruckLoadBolNumber);
        Assert.Equal(100, await fixture.CurrentBinsAsync());
        Assert.Equal(roomCountBefore, await fixture.Db.Rooms.CountAsync());
        Assert.Equal(warehouseCountBefore, await fixture.Db.Warehouses.CountAsync());
        Assert.DoesNotContain(await fixture.Db.Rooms.ToListAsync(), x => x.Code == "CUSTOM");
        var adjustment = Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId != null).ToListAsync());
        Assert.Equal(-200, adjustment.ChangeAmount);
        Assert.Equal(OutsideWarehouseTransferAdjustmentTypes.Transfer, adjustment.AdjustmentType);
        Assert.Equal(transfer.Id, adjustment.OutsideWarehouseTransferId);
        Assert.Empty(await fixture.Db.ActualRuns.ToListAsync());
        Assert.Empty(await fixture.Db.BinsRunEntries.ToListAsync());
        Assert.Empty(await fixture.Db.RoomTransfers.ToListAsync());
    }

    [Fact]
    public async Task Full_transfer_removes_source_from_all_shared_current_inventory_selectors()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-full", 300), default);
        Assert.True(result.Success, result.Error);
        Assert.Equal(0, await fixture.CurrentBinsAsync());
        Assert.Empty((await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default)).Inventory);
        Assert.DoesNotContain(await fixture.Db.TreatmentLineageSegments.ToListAsync(), x => x.CurrentBins > 0);
    }

    [Fact]
    public async Task Duplicate_operation_key_is_idempotent_and_deducts_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var form = await fixture.FormAsync("outside-retry", 80);
        var first = await fixture.Service.CreateAsync(form, default);
        var second = await fixture.Service.CreateAsync(form, default);
        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.True(second.AlreadyApplied);
        Assert.Equal(first.TransferId, second.TransferId);
        Assert.Single(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId != null).ToListAsync());
        Assert.Equal(220, await fixture.CurrentBinsAsync());
    }

    [Fact]
    public async Task Shared_identity_across_receipts_is_one_operator_choice_but_retains_exact_receipt_lineage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMatchingReceiptAsync(40);

        var page = await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default);
        var choice = Assert.Single(page.Inventory);
        Assert.Equal(340, choice.AvailableBins);

        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-multi-receipt", 320), default);
        Assert.True(result.Success, result.Error);
        var transfer = await fixture.Db.OutsideWarehouseTransfers.SingleAsync();
        Assert.Null(transfer.ReceiptId);
        Assert.Equal(20, await fixture.CurrentBinsAsync());

        var movements = await fixture.Db.TreatmentLineageMovements
            .Where(x => x.OutsideWarehouseTransferId == transfer.Id && x.ReversesTreatmentLineageMovementId == null)
            .ToListAsync();
        Assert.Equal(320, movements.Sum(x => x.BinCount));
        Assert.Equal([8844L, 8850L], movements.Select(x => x.ReceiptId!.Value).Distinct().Order().ToArray());
    }

    [Fact]
    public async Task Insufficient_or_stale_inventory_fails_with_zero_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var insufficient = await fixture.FormAsync("outside-overdraw", 301);
        var overdraw = await fixture.Service.CreateAsync(insufficient, default);
        Assert.False(overdraw.Success);
        Assert.Contains("Only 300", overdraw.Error);

        var stale = await fixture.FormAsync("outside-stale", 10);
        stale.ExpectedAvailableBins = 299;
        var staleResult = await fixture.Service.CreateAsync(stale, default);
        Assert.False(staleResult.Success);
        Assert.Contains("changed", staleResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Empty(await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId != null).ToListAsync());
        Assert.Equal(300, await fixture.CurrentBinsAsync());
    }

    [Fact]
    public async Task Sealed_source_fails_closed_without_transfer_ledger_or_audit_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var room = await fixture.Db.Rooms.SingleAsync();
        room.IsSealed = true;
        room.SealedAt = Now.AddMinutes(-1);
        room.SealRecordedAt = Now.AddMinutes(-2);
        await fixture.Db.SaveChangesAsync();
        var auditBefore = await fixture.Db.AuditLogs.CountAsync();
        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-sealed", 20), default);
        Assert.False(result.Success);
        Assert.Contains("sealed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Equal(auditBefore, await fixture.Db.AuditLogs.CountAsync());
        Assert.Equal(300, await fixture.CurrentBinsAsync());
    }

    [Fact]
    public async Task Reversal_restores_exact_identity_and_treatment_provenance_and_retains_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-reverse", 125), default);
        Assert.True(created.Success, created.Error);
        var originalMovements = await fixture.Db.TreatmentLineageMovements.Where(x => x.OutsideWarehouseTransferId == created.TransferId && x.ReversesTreatmentLineageMovementId == null).ToListAsync();
        Assert.NotEmpty(originalMovements);
        Assert.Equal(125, originalMovements.Sum(x => x.BinCount));

        var error = await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId!.Value, OperationKey = "outside-reverse-op", Reason = "Incorrect destination entry" }, default);
        Assert.Null(error);
        Assert.Equal(300, await fixture.CurrentBinsAsync());
        var saved = await fixture.Db.OutsideWarehouseTransfers.SingleAsync();
        Assert.True(saved.IsReversed);
        Assert.Equal("Incorrect destination entry", saved.ReverseReason);
        Assert.Equal("9350", saved.GrowerNumberSnapshot);
        Assert.Equal("TEST-GALA", saved.VarietyCodeSnapshot);
        var adjustments = await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId == saved.Id).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal([-125, 125], adjustments.Select(x => x.ChangeAmount));
        var reversals = await fixture.Db.TreatmentLineageMovements.Where(x => x.OutsideWarehouseTransferId == saved.Id && x.ReversesTreatmentLineageMovementId != null).ToListAsync();
        Assert.Equal(125, reversals.Sum(x => x.BinCount));
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x => x.EntityName == nameof(OutsideWarehouseTransfer)));
    }

    [Fact]
    public async Task Reversal_requires_admin_and_reason_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync(canAdmin: false);
        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-auth", 50), default);
        Assert.True(created.Success, created.Error);
        var denied = await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId!.Value, OperationKey = "denied", Reason = "Correction" }, default);
        Assert.Contains("Admin", denied);

        fixture.Access.CanAdmin = true;
        var blank = await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId.Value, OperationKey = "blank", Reason = "  " }, default);
        Assert.Contains("reason", blank, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId.Value, OperationKey = "valid-reverse", Reason = "Entry mistake" }, default));
        var adjustmentCount = await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.OutsideWarehouseTransferId != null);
        Assert.Null(await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId.Value, OperationKey = "valid-reverse", Reason = "Entry mistake" }, default));
        Assert.Equal(adjustmentCount, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.OutsideWarehouseTransferId != null));
    }

    [Fact]
    public async Task Active_location_is_selectable_inactive_location_is_history_only()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.OutsideWarehouses.Add(new OutsideWarehouse { Name = "Inactive Historic", Code = "OLD", IsActive = false, CreatedAt = Now, UpdatedAt = Now });
        await fixture.Db.SaveChangesAsync();
        var page = await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default);
        Assert.Contains(page.OutsideWarehouses, x => x.Code == "CUSTOM");
        Assert.DoesNotContain(page.OutsideWarehouses, x => x.Code == "OLD");
        Assert.Contains(page.ReportOutsideWarehouses, x => x.Code == "OLD" && !x.IsActive);
    }

    [Fact]
    public async Task Source_facility_and_room_filter_limit_outside_transfer_inventory()
    {
        await using var fixture = await Fixture.CreateAsync();

        var matching = await fixture.Service.GetPageAsync(new()
        {
            TransferType = "Outside",
            WarehouseId = Fixture.WarehouseId,
            RoomId = Fixture.RoomId
        }, default);
        Assert.Single(matching.Inventory);

        var noMatch = await fixture.Service.GetPageAsync(new()
        {
            TransferType = "Outside",
            WarehouseId = Fixture.WarehouseId,
            RoomId = 999999
        }, default);
        Assert.Empty(noMatch.Inventory);

        var view = Source("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        Assert.Contains("name=\"TransferType\" value=\"@Model.Filter.TransferType\"", view);
        Assert.Contains("TransferType=Outside&amp;WarehouseId=@Model.Filter.WarehouseId&amp;RoomId=@Model.Filter.RoomId", view);
    }

    [Fact]
    public async Task Outside_warehouse_master_lifecycle_is_unique_audited_and_history_safe()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = new AdminManagementService(fixture.Db, new VarietyColorService(fixture.Db));
        var actor = "outside@test.local";
        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "outside-warehouses",
            Name = "Other Synthetic Packer",
            Code = " other ",
            Address = "200 Test Way",
            Description = "Disposable validation",
            IsActive = true
        }, actor, default));
        var other = await fixture.Db.OutsideWarehouses.SingleAsync(x => x.Code == "OTHER");
        Assert.Equal("Other Synthetic Packer", other.Name);
        Assert.Equal("200 Test Way", other.Address);
        Assert.Contains("already exists", await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "outside-warehouses",
            Name = "Duplicate",
            Code = "OTHER",
            IsActive = true
        }, actor, default), StringComparison.OrdinalIgnoreCase);

        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-history-location", 10), default);
        Assert.True(created.Success, created.Error);
        Assert.Null(await admin.DeactivateAsync("outside-warehouses", 8845, actor, default));
        Assert.False((await fixture.Db.OutsideWarehouses.FindAsync(8845))!.IsActive);
        var page = await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default);
        Assert.DoesNotContain(page.OutsideWarehouses, x => x.Id == 8845);
        Assert.Contains(page.History, x => x.OutsideWarehouseCode == "CUSTOM");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "OutsideWarehouseCreated");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "OutsideWarehouseDeactivated");
    }

    [Fact]
    public void Combined_migration_compatibility_and_836_object_gate_are_exact_and_bounded()
    {
        var migration = Source("src", "CropQc.Data", "Migrations", "20260828033737_AddTransferCustodyWorkflow.cs");
        var preflight = Source("scripts", "postgresql", "preflight-transfer-custody-workflow.sql");
        var apply = Source("scripts", "postgresql", "apply-transfer-custody-workflow-schema.sql");
        var verify = Source("scripts", "postgresql", "verify-transfer-custody-workflow.sql");
        var gate = Source("src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs");
        Assert.Contains("name: \"OutsideWarehouses\"", migration);
        Assert.Contains("name: \"OutsideWarehouseTransfers\"", migration);
        Assert.Contains("name: \"InterCrewTransfers\"", migration);
        Assert.Contains("MigrationProviderTypes.StoreType", migration);
        Assert.Contains("Npgsql:ValueGenerationStrategy", migration);
        Assert.Contains("State C", preflight);
        Assert.Contains("state_a_absent", preflight);
        Assert.Contains("state_b_complete_exact", preflight);
        Assert.Contains("BEGIN;", apply);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.DoesNotContain("__EFMigrationsHistory", apply);
        Assert.Contains("162 AS checked_target_objects", verify);
        Assert.Equal("20260828033737_AddTransferCustodyWorkflow", DatabaseStartupDiagnostics.ExpectedSchemaMigration);
        Assert.Equal(836, gate.Split('\n').Count(x => x.TrimStart().StartsWith("new(", StringComparison.Ordinal) || x.TrimStart().StartsWith(",new(", StringComparison.Ordinal)));
    }

    [Fact]
    public void UI_keeps_internal_transfer_default_and_exposes_outside_history_review_and_mobile_safe_fields()
    {
        var index = Source("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var outside = Source("src", "CropQc.Web", "Views", "BinsRun", "_OutsideWarehouseTransfer.cshtml");
        var detail = Source("src", "CropQc.Web", "Views", "BinsRun", "OutsideTransferDetails.cshtml");
        var master = Source("src", "CropQc.Web", "Views", "MasterData", "_MasterDataFields.cshtml");
        Assert.Contains("Internal Room Transfer", index);
        Assert.Contains("Outside Warehouse", index);
        Assert.Contains("Current inventory selection", outside);
        Assert.Contains("Truck / Load / BOL #", outside);
        Assert.Contains("Review Outside Transfer", outside);
        Assert.Contains("Outside Transfer History", outside);
        Assert.Contains("inputmode=\"numeric\"", outside);
        Assert.DoesNotContain("Destination Room", outside);
        Assert.Contains("Required reversal reason", detail);
        Assert.Contains("Address", master);
    }

    [Fact]
    public async Task Inter_crew_dispatch_receive_review_and_reversal_preserve_exact_custody_counts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (service, ebsRoom) = await fixture.CreateInterCrewServiceAsync();
        var page = await service.GetPageAsync(SourceFilter(), default);
        var source = Assert.Single(page.Inventory);
        var dispatched = await service.DispatchAsync(new()
        {
            OperationKey = "crew-dispatch",
            SourceWarehouseId = source.WarehouseId,
            SourceRoomId = source.RoomId,
            SourceKey = source.SourceKey,
            ExpectedAvailableBins = source.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 70,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            TruckLoadBolNumber = "BOL-70",
            ConfirmedReview = true
        }, default);
        Assert.True(dispatched.Success, dispatched.Error);
        Assert.Equal(230, await fixture.CurrentBinsAsync());
        var transfer = await fixture.Db.InterCrewTransfers.SingleAsync();
        Assert.Equal(InterCrewTransferStatuses.InTransit, transfer.Status);
        Assert.Null(transfer.DestinationRoomId);
        Assert.Equal(-70, Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync()).ChangeAmount);
        Assert.Equal(70, await fixture.Db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == transfer.Id && x.MovementType == TreatmentLineageMovementTypes.InterCrewDispatch).SumAsync(x => x.BinCount));

        // Receiving occurs in a later HTTP request with a fresh DbContext. Ensure the
        // persisted dispatch ledger remains part of invariant validation in that shape.
        fixture.Db.ChangeTracker.Clear();
        var received = await service.ReceiveAsync(new()
        {
            TransferId = transfer.Id,
            OperationKey = "crew-receive",
            DestinationRoomId = ebsRoom.Id,
            BinsReceived = 68,
            ReceivedAt = DateTime.Parse("2026-08-27T12:00"),
            Note = "Physical count"
        }, default);
        Assert.True(received.Success, received.Error);
        fixture.Db.ChangeTracker.Clear();
        transfer = await fixture.Db.InterCrewTransfers.SingleAsync();
        Assert.Equal(70, transfer.BinsLoaded);
        Assert.Equal(68, transfer.BinsReceived);
        Assert.Equal(-2, transfer.VarianceBins);
        Assert.Equal(InterCrewTransferStatuses.ReceivedNeedsReview, transfer.Status);
        Assert.Equal(68, (await fixture.Ledger.GetSnapshotsAsync(ebsRoom.WarehouseId, [ebsRoom.Id], default)).Sum(x => x.CurrentBins));

        Assert.Null(await service.ReviewAsync(new() { TransferId = transfer.Id, OperationKey = "crew-review", Note = "Verified against unload tally" }, default));
        fixture.Db.ChangeTracker.Clear();
        transfer = await fixture.Db.InterCrewTransfers.SingleAsync();
        Assert.Equal(InterCrewTransferStatuses.Received, transfer.Status);
        Assert.Equal(70, transfer.BinsLoaded);
        Assert.Equal(68, transfer.BinsReceived);

        Assert.Null(await service.ReverseAsync(new() { TransferId = transfer.Id, OperationKey = "crew-reverse", Reason = "Wrong truck selected" }, default));
        fixture.Db.ChangeTracker.Clear();
        transfer = await fixture.Db.InterCrewTransfers.SingleAsync();
        Assert.Equal(InterCrewTransferStatuses.Reversed, transfer.Status);
        Assert.Equal(300, await fixture.CurrentBinsAsync());
        Assert.Equal(0, (await fixture.Ledger.GetSnapshotsAsync(ebsRoom.WarehouseId, [ebsRoom.Id], default)).Sum(x => x.CurrentBins));
        Assert.Equal(new[] { -70, 68, -68, 70 }, await fixture.Db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id).OrderBy(x => x.Id).Select(x => x.ChangeAmount).ToArrayAsync());
        Assert.Equal(4, await fixture.Db.AuditLogs.CountAsync(x => x.EntityName == nameof(InterCrewTransfer)));
    }

    [Fact]
    public async Task Inter_crew_dispatch_and_receive_are_idempotent_and_McDougall_is_never_a_destination()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (service, ebsRoom) = await fixture.CreateInterCrewServiceAsync();
        var source = Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Inventory);
        var form = new InterCrewDispatchForm { OperationKey = "same-dispatch", SourceWarehouseId = source.WarehouseId, SourceRoomId = source.RoomId, SourceKey = source.SourceKey, ExpectedAvailableBins = source.AvailableBins, DestinationCustodyGroup = TransferCustodyGroups.Ebs, BinsLoaded = 20, LoadedAt = DateTime.Parse("2026-08-27T10:00"), ConfirmedReview = true };
        var first = await service.DispatchAsync(form, default);
        var duplicate = await service.DispatchAsync(form, default);
        Assert.True(first.Success, first.Error); Assert.True(duplicate.AlreadyApplied);
        var receive = new InterCrewReceiveForm { TransferId = first.TransferId!.Value, OperationKey = "same-receive", DestinationRoomId = ebsRoom.Id, BinsReceived = 20, ReceivedAt = DateTime.Parse("2026-08-27T12:00") };
        Assert.True((await service.ReceiveAsync(receive, default)).Success);
        Assert.True((await service.ReceiveAsync(receive, default)).AlreadyApplied);
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.InterCrewTransferId == first.TransferId));
        Assert.DoesNotContain((await service.GetDetailsAsync(first.TransferId.Value, default))!.DestinationRooms, x => x.Facility.Contains("McDougall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Inter_crew_page_requires_a_source_room_and_filters_inventory_to_the_exact_room()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sourceWarehouse = await fixture.Db.Warehouses.SingleAsync(x => x.Id == Fixture.WarehouseId);
        sourceWarehouse.Code = "McDougall";
        sourceWarehouse.Name = "McDougall";
        var sourceRoom = await fixture.Db.Rooms.SingleAsync(x => x.Id == Fixture.RoomId);
        sourceRoom.Code = "MCD-10";
        sourceRoom.Name = "McDougall Room 10";
        sourceRoom.CropQcRoomName = "McDougall Room 10";
        var room9 = await fixture.AddInventoryRoomAsync(8870, "MCD-9", "McDougall Room 9", 8871, 8872, 8873, 45);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var (service, ebsRoom) = await fixture.CreateInterCrewServiceAsync();

        var noRoom = await service.GetPageAsync(new() { WarehouseId = Fixture.WarehouseId }, default);
        Assert.Empty(noRoom.Inventory);
        Assert.Contains("Select a source Facility and Room", noRoom.SourceSelectionMessage);

        var room10Page = await service.GetPageAsync(new() { WarehouseId = Fixture.WarehouseId, RoomId = Fixture.RoomId }, default);
        var room10Option = Assert.Single(room10Page.Inventory);
        Assert.Equal(Fixture.RoomId, room10Option.RoomId);
        Assert.Equal("McDougall", room10Page.SourceFacility);
        Assert.Equal("McDougall Room 10", room10Page.SourceRoom);
        Assert.Equal(Fixture.WarehouseId, room10Page.Form.SourceWarehouseId);
        Assert.Equal(Fixture.RoomId, room10Page.Form.SourceRoomId);

        var room9Page = await service.GetPageAsync(new() { WarehouseId = Fixture.WarehouseId, RoomId = room9.Id }, default);
        Assert.All(room9Page.Inventory, x => Assert.Equal(room9.Id, x.RoomId));
        Assert.DoesNotContain(room9Page.Inventory, x => x.RoomId == Fixture.RoomId);

        var mismatch = await service.GetPageAsync(new() { WarehouseId = ebsRoom.WarehouseId, RoomId = Fixture.RoomId }, default);
        Assert.Empty(mismatch.Inventory);
        Assert.Contains("does not belong", mismatch.SourceSelectionMessage);
    }

    [Fact]
    public async Task Inter_crew_dispatch_rejects_a_valid_key_from_a_different_selected_room_and_a_stale_key()
    {
        await using var fixture = await Fixture.CreateAsync();
        var room9 = await fixture.AddInventoryRoomAsync(8870, "MCD-9", "McDougall Room 9", 8871, 8872, 8873, 45);
        var (service, _) = await fixture.CreateInterCrewServiceAsync();
        var room9Source = Assert.Single((await service.GetPageAsync(new()
        {
            WarehouseId = Fixture.WarehouseId,
            RoomId = room9.Id
        }, default)).Inventory);

        var crafted = await service.DispatchAsync(new()
        {
            OperationKey = "crafted-room-context",
            SourceWarehouseId = Fixture.WarehouseId,
            SourceRoomId = Fixture.RoomId,
            SourceKey = room9Source.SourceKey,
            ExpectedAvailableBins = room9Source.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 5,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            ConfirmedReview = true
        }, default);
        Assert.False(crafted.Success);
        Assert.Empty(await fixture.Db.InterCrewTransfers.ToListAsync());

        var room10Source = Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Inventory);
        await fixture.AddMatchingReceiptAsync(1);
        var stale = await service.DispatchAsync(new()
        {
            OperationKey = "stale-room-context",
            SourceWarehouseId = room10Source.WarehouseId,
            SourceRoomId = room10Source.RoomId,
            SourceKey = room10Source.SourceKey,
            ExpectedAvailableBins = room10Source.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 5,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            ConfirmedReview = true
        }, default);
        Assert.False(stale.Success);
        Assert.Empty(await fixture.Db.InterCrewTransfers.ToListAsync());
    }

    [Fact]
    public async Task Inter_crew_receiving_queue_is_routed_by_employment_facility_and_unassigned_fails_closed()
    {
        await using var fixture = await Fixture.CreateAsync(canAdmin: false);
        fixture.Access.CanAdmin = true;
        var (service, _) = await fixture.CreateInterCrewServiceAsync();
        var source = Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Inventory);
        var dispatched = await service.DispatchAsync(new()
        {
            OperationKey = "crew-routing",
            SourceWarehouseId = source.WarehouseId,
            SourceRoomId = source.RoomId,
            SourceKey = source.SourceKey,
            ExpectedAvailableBins = source.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 10,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            ConfirmedReview = true
        }, default);
        Assert.True(dispatched.Success, dispatched.Error);

        fixture.Access.CanAdmin = false;
        var user = await fixture.Db.Users.SingleAsync(x => x.Id == 8843);
        user.EmploymentFacility = EmploymentFacilities.Wp;
        await fixture.Db.SaveChangesAsync();
        Assert.Empty((await service.GetPageAsync(SourceFilter(), default)).Queue);
        Assert.False((await service.GetDetailsAsync(dispatched.TransferId!.Value, default))!.CanReceive);

        user.EmploymentFacility = EmploymentFacilities.Ebs;
        await fixture.Db.SaveChangesAsync();
        Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Queue);
        Assert.True((await service.GetDetailsAsync(dispatched.TransferId.Value, default))!.CanReceive);

        user.EmploymentFacility = EmploymentFacilities.Unassigned;
        await fixture.Db.SaveChangesAsync();
        Assert.Empty((await service.GetPageAsync(SourceFilter(), default)).Queue);
        Assert.False((await service.GetDetailsAsync(dispatched.TransferId.Value, default))!.CanReceive);

        user.EmploymentFacility = EmploymentFacilities.Shared;
        await fixture.Db.SaveChangesAsync();
        Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Queue);
    }

    [Theory]
    [InlineData("WP", TransferCustodyGroups.Ebs)]
    [InlineData("DH", TransferCustodyGroups.Ebs)]
    [InlineData("EBS", TransferCustodyGroups.WpDh)]
    [InlineData("McDougall", TransferCustodyGroups.WpDh)]
    [InlineData("McDougall", TransferCustodyGroups.Ebs)]
    public async Task Inter_crew_dispatch_matrix_creates_only_an_in_transit_source_deduction(
        string sourceFacility,
        string destinationGroup)
    {
        await using var fixture = await Fixture.CreateAsync();
        var sourceWarehouse = await fixture.Db.Warehouses.SingleAsync(x => x.Id == Fixture.WarehouseId);
        sourceWarehouse.Code = sourceFacility;
        sourceWarehouse.Name = sourceFacility;
        await fixture.Db.SaveChangesAsync();
        var (service, _) = await fixture.CreateInterCrewServiceAsync();
        var source = Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Inventory);

        var result = await service.DispatchAsync(new()
        {
            OperationKey = $"matrix-{sourceFacility}-{destinationGroup}",
            SourceWarehouseId = source.WarehouseId,
            SourceRoomId = source.RoomId,
            SourceKey = source.SourceKey,
            ExpectedAvailableBins = source.AvailableBins,
            DestinationCustodyGroup = destinationGroup,
            BinsLoaded = 54,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            ConfirmedReview = true
        }, default);

        Assert.True(result.Success, result.Error);
        var transfer = await fixture.Db.InterCrewTransfers.SingleAsync();
        Assert.Equal(InterCrewTransferStatuses.InTransit, transfer.Status);
        Assert.Null(transfer.DestinationWarehouseId);
        Assert.Null(transfer.DestinationRoomId);
        Assert.Equal(246, await fixture.CurrentBinsAsync());
        Assert.Equal(-54, Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync()).ChangeAmount);
    }

    [Theory]
    [InlineData("WP", TransferCustodyGroups.WpDh)]
    [InlineData("DH", TransferCustodyGroups.WpDh)]
    [InlineData("EBS", TransferCustodyGroups.Ebs)]
    [InlineData("WP", "MCD")]
    [InlineData("EBS", "McDougall")]
    public async Task Inter_crew_dispatch_rejects_same_crew_and_McDougall_destinations_without_writes(
        string sourceFacility,
        string destinationGroup)
    {
        await using var fixture = await Fixture.CreateAsync();
        var sourceWarehouse = await fixture.Db.Warehouses.SingleAsync(x => x.Id == Fixture.WarehouseId);
        sourceWarehouse.Code = sourceFacility;
        sourceWarehouse.Name = sourceFacility;
        await fixture.Db.SaveChangesAsync();
        var (service, _) = await fixture.CreateInterCrewServiceAsync();
        var source = Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Inventory);

        var result = await service.DispatchAsync(new()
        {
            OperationKey = $"reject-{sourceFacility}-{destinationGroup}",
            SourceWarehouseId = source.WarehouseId,
            SourceRoomId = source.RoomId,
            SourceKey = source.SourceKey,
            ExpectedAvailableBins = source.AvailableBins,
            DestinationCustodyGroup = destinationGroup,
            BinsLoaded = 54,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            ConfirmedReview = true
        }, default);

        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.InterCrewTransfers.ToListAsync());
        Assert.Equal(300, await fixture.CurrentBinsAsync());
    }

    [Fact]
    public async Task Inter_crew_receive_enforces_destination_group_and_posts_one_atomic_room_count()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (service, ebsRoom) = await fixture.CreateInterCrewServiceAsync();
        var source = Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Inventory);
        var dispatched = await service.DispatchAsync(new()
        {
            OperationKey = "receive-destination",
            SourceWarehouseId = source.WarehouseId,
            SourceRoomId = source.RoomId,
            SourceKey = source.SourceKey,
            ExpectedAvailableBins = source.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 70,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            ConfirmedReview = true
        }, default);
        Assert.True(dispatched.Success, dispatched.Error);

        var wrongCrew = await service.ReceiveAsync(new()
        {
            TransferId = dispatched.TransferId!.Value,
            OperationKey = "wrong-wp-room",
            DestinationRoomId = Fixture.RoomId,
            BinsReceived = 70,
            ReceivedAt = DateTime.Parse("2026-08-27T12:00")
        }, default);
        var mcDougall = await service.ReceiveAsync(new()
        {
            TransferId = dispatched.TransferId.Value,
            OperationKey = "wrong-mcd-room",
            DestinationRoomId = 8863,
            BinsReceived = 70,
            ReceivedAt = DateTime.Parse("2026-08-27T12:00")
        }, default);
        Assert.False(wrongCrew.Success);
        Assert.False(mcDougall.Success);
        Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == dispatched.TransferId).ToListAsync());

        var received = await service.ReceiveAsync(new()
        {
            TransferId = dispatched.TransferId.Value,
            OperationKey = "right-ebs-room",
            DestinationRoomId = ebsRoom.Id,
            BinsReceived = 68,
            ReceivedAt = DateTime.Parse("2026-08-27T12:00")
        }, default);
        Assert.True(received.Success, received.Error);
        Assert.Equal(68, (await fixture.Ledger.GetSnapshotsAsync(ebsRoom.WarehouseId, [ebsRoom.Id], default)).Sum(x => x.CurrentBins));
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.InterCrewTransferId == dispatched.TransferId));
    }

    [Fact]
    public async Task Inter_crew_in_transit_reversal_is_admin_only_reasoned_idempotent_and_restores_loaded_bins()
    {
        await using var fixture = await Fixture.CreateAsync(canAdmin: false);
        var (service, _) = await fixture.CreateInterCrewServiceAsync();
        var source = Assert.Single((await service.GetPageAsync(SourceFilter(), default)).Inventory);
        var dispatched = await service.DispatchAsync(new()
        {
            OperationKey = "reverse-in-transit",
            SourceWarehouseId = source.WarehouseId,
            SourceRoomId = source.RoomId,
            SourceKey = source.SourceKey,
            ExpectedAvailableBins = source.AvailableBins,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            BinsLoaded = 70,
            LoadedAt = DateTime.Parse("2026-08-27T10:00"),
            ConfirmedReview = true
        }, default);
        Assert.True(dispatched.Success, dispatched.Error);
        Assert.Contains("Admin", await service.ReverseAsync(new() { TransferId = dispatched.TransferId!.Value, OperationKey = "denied", Reason = "Wrong load" }, default));

        fixture.Access.CanAdmin = true;
        Assert.Contains("reason", await service.ReverseAsync(new() { TransferId = dispatched.TransferId.Value, OperationKey = "blank", Reason = " " }, default), StringComparison.OrdinalIgnoreCase);
        Assert.Null(await service.ReverseAsync(new() { TransferId = dispatched.TransferId.Value, OperationKey = "reverse-valid", Reason = "Wrong load" }, default));
        var writes = await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.InterCrewTransferId == dispatched.TransferId);
        Assert.Null(await service.ReverseAsync(new() { TransferId = dispatched.TransferId.Value, OperationKey = "reverse-valid", Reason = "Wrong load" }, default));
        Assert.Equal(writes, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.InterCrewTransferId == dispatched.TransferId));
        Assert.Equal(300, await fixture.CurrentBinsAsync());
        var saved = await fixture.Db.InterCrewTransfers.SingleAsync();
        Assert.Equal(InterCrewTransferStatuses.Reversed, saved.Status);
        Assert.Null(saved.DestinationRoomId);
    }

    [Fact]
    public void Inter_crew_UI_exposes_three_modes_queue_variance_review_and_no_partial_receive()
    {
        var index = Source("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var page = Source("src", "CropQc.Web", "Views", "BinsRun", "_InterCrewTransfer.cshtml");
        var detail = Source("src", "CropQc.Web", "Views", "BinsRun", "InterCrewTransferDetails.cshtml");
        Assert.Contains("Internal Room Transfer", index);
        Assert.Contains("Transfer to Another Crew", index);
        Assert.Contains("Outside Warehouse", index);
        Assert.Contains("TransferType=Internal&amp;WarehouseId=@Model.Filter.WarehouseId&amp;RoomId=@Model.Filter.RoomId", index);
        Assert.Contains("TransferType=InterCrew&amp;WarehouseId=@Model.Filter.WarehouseId&amp;RoomId=@Model.Filter.RoomId", index);
        Assert.Contains("name=\"SourceWarehouseId\"", page);
        Assert.Contains("name=\"SourceRoomId\"", page);
        Assert.Contains("Source inventory:", page);
        Assert.Contains("Receiving Queue", page);
        Assert.Contains("In Transit", page);
        Assert.Contains("WP / DH", page);
        Assert.Contains("EBS", page);
        Assert.Contains("authoritative received count", page);
        Assert.Contains("Receive Entire Load", detail);
        Assert.Contains("Review Count Variance", detail);
        Assert.Contains("does not rewrite either count", detail);
        Assert.Contains("partial receiving is not supported", detail, StringComparison.OrdinalIgnoreCase);
    }

    private static string Source(params string[] segments) => File.ReadAllText(FindRepositoryFile(segments));

    private static BinsRunFilterForm SourceFilter() => new()
    {
        Section = "Transfer",
        TransferType = "InterCrew",
        WarehouseId = Fixture.WarehouseId,
        RoomId = Fixture.RoomId
    };

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const int WarehouseId = 8840;
        public const int RoomId = 8841;
        private Fixture(CropQcDbContext db, OutsideWarehouseTransferService service, RoomInventoryLedgerQueryService ledger, RoomTreatmentService treatments, ConfigurableAccess access)
        {
            Db = db; Service = service; Ledger = ledger; Treatments = treatments; Access = access;
        }
        public CropQcDbContext Db { get; }
        public OutsideWarehouseTransferService Service { get; }
        public RoomInventoryLedgerQueryService Ledger { get; }
        public RoomTreatmentService Treatments { get; }
        public ConfigurableAccess Access { get; }

        public static async Task<Fixture> CreateAsync(bool canAdmin = true)
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase($"outside-transfer-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = WarehouseId, Code = "WP", Name = "Warehouse Production" };
            var room = new Room { Id = RoomId, Warehouse = warehouse, WarehouseId = WarehouseId, Code = "TEST-12", Name = "Test 12", CropQcRoomName = "Test 12", CapacityBins = 1000 };
            var fruit = new FruitProfile { Id = 8842, Name = "Test Gala", VarietyCode = "TEST-GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var user = new User { Id = 8843, Email = "outside@test.local", DisplayName = "Outside Tester", Domain = "test.local", CreatedAt = Now };
            var receipt = new Receipt { Id = 8844, CropYear = 2026, CompuTechReceiptId = "TR109999", ReceivedAt = Now.AddDays(-1), Warehouse = warehouse, WarehouseId = WarehouseId, Room = room, RoomId = RoomId, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerNumber = "9350", GrowerName = "TEST GROWER", LotCode = "9350", BinCount = 300, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
            var outside = new OutsideWarehouse { Id = 8845, Name = "Custom Apple", Code = "CUSTOM", Address = "123 Test Road", IsActive = true, CreatedAt = Now, UpdatedAt = Now, CreatedByUserId = user.Id, UpdatedByUserId = user.Id };
            db.AddRange(warehouse, room, fruit, user, receipt, outside);
            db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment { Id = 8846, CropYear = 2026, Receipt = receipt, ReceiptId = receipt.Id, Warehouse = warehouse, WarehouseId = WarehouseId, Room = room, RoomId = RoomId, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerName = receipt.GrowerName, LotNumber = receipt.LotCode, VarietyCode = fruit.VarietyCode, OldBinCount = 0, ChangeAmount = 300, NewBinCount = 300, AdjustmentType = "Receipt", InventoryStatus = "Packable", AdjustmentAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1) });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, user.Email)], "Test"));
            var accessor = new FixedHttpContextAccessor(new DefaultHttpContext { User = principal });
            var access = new ConfigurableAccess { CanAdmin = canAdmin };
            var time = new PacificBusinessTimeService(new FixedClock(Now));
            var ledger = new RoomInventoryLedgerQueryService(db);
            var treatments = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
            var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
            var service = new OutsideWarehouseTransferService(db, ledger, treatments, treatments, invariant, access, accessor, time);
            return new Fixture(db, service, ledger, treatments, access);
        }

        public async Task<OutsideWarehouseTransferForm> FormAsync(string key, int bins)
        {
            var page = await Service.GetPageAsync(new() { TransferType = "Outside" }, default);
            var option = Assert.Single(page.Inventory);
            return new() { OperationKey = key, OutsideWarehouseId = 8845, SourceKey = option.SourceKey, ExpectedAvailableBins = option.AvailableBins, BinCount = bins, TransferredAt = DateTime.Parse("2026-08-27T10:00"), TruckLoadBolNumber = "LOAD-42", Notes = "Disposable proof", ConfirmedReview = true };
        }

        public async Task AddMatchingReceiptAsync(int bins)
        {
            var warehouse = await Db.Warehouses.SingleAsync(x => x.Id == WarehouseId);
            var room = await Db.Rooms.SingleAsync(x => x.Id == RoomId);
            var fruit = await Db.FruitProfiles.SingleAsync(x => x.Id == 8842);
            var receipt = new Receipt
            {
                Id = 8850,
                CropYear = 2026,
                CompuTechReceiptId = "TR110000",
                ReceivedAt = Now.AddHours(-12),
                Warehouse = warehouse,
                WarehouseId = WarehouseId,
                Room = room,
                RoomId = RoomId,
                FruitProfile = fruit,
                FruitProfileId = fruit.Id,
                GrowerNumber = "9350",
                GrowerName = "TEST GROWER",
                LotCode = "9350",
                BinCount = bins,
                CreatedAt = Now.AddHours(-12),
                UpdatedAt = Now.AddHours(-12)
            };
            Db.Receipts.Add(receipt);
            Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                Id = 8851,
                CropYear = 2026,
                Receipt = receipt,
                ReceiptId = receipt.Id,
                Warehouse = warehouse,
                WarehouseId = WarehouseId,
                Room = room,
                RoomId = RoomId,
                FruitProfile = fruit,
                FruitProfileId = fruit.Id,
                GrowerName = receipt.GrowerName,
                LotNumber = receipt.LotCode,
                VarietyCode = fruit.VarietyCode,
                OldBinCount = 300,
                ChangeAmount = bins,
                NewBinCount = 300 + bins,
                AdjustmentType = "Receipt",
                InventoryStatus = "Packable",
                AdjustmentAt = Now.AddHours(-12),
                CreatedAt = Now.AddHours(-12)
            });
            await Db.SaveChangesAsync();
            var snapshot = Assert.Single(await Ledger.GetSnapshotsAsync(WarehouseId, [RoomId], default));
            var identityKey = RoomTreatmentService.IdentityKey(snapshot);
            Db.TreatmentLineageSegments.AddRange(
                new TreatmentLineageSegment
                {
                    Id = 8852,
                    WarehouseId = WarehouseId,
                    RoomId = RoomId,
                    ReceiptId = 8844,
                    CropYear = 2026,
                    FruitProfileId = fruit.Id,
                    IdentityKey = identityKey,
                    GrowerNumberSnapshot = "9350",
                    GrowerNameSnapshot = "TEST GROWER",
                    LotNumberSnapshot = "9350",
                    VarietyCodeSnapshot = fruit.VarietyCode,
                    ProductionTypeSnapshot = fruit.ProductionType,
                    InventoryStatusSnapshot = "Packable",
                    TreatmentState = TreatmentLineageStates.Untreated,
                    TreatmentSignature = "u",
                    CurrentBins = 300,
                    CreatedAt = Now,
                    UpdatedAt = Now
                },
                new TreatmentLineageSegment
                {
                    Id = 8853,
                    WarehouseId = WarehouseId,
                    RoomId = RoomId,
                    ReceiptId = receipt.Id,
                    CropYear = 2026,
                    FruitProfileId = fruit.Id,
                    IdentityKey = identityKey,
                    GrowerNumberSnapshot = "9350",
                    GrowerNameSnapshot = "TEST GROWER",
                    LotNumberSnapshot = "9350",
                    VarietyCodeSnapshot = fruit.VarietyCode,
                    ProductionTypeSnapshot = fruit.ProductionType,
                    InventoryStatusSnapshot = "Packable",
                    TreatmentState = TreatmentLineageStates.Untreated,
                    TreatmentSignature = "u",
                    CurrentBins = bins,
                    CreatedAt = Now,
                    UpdatedAt = Now
                });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<Room> AddInventoryRoomAsync(
            int roomId,
            string roomCode,
            string roomName,
            long receiptId,
            long adjustmentId,
            long segmentId,
            int bins)
        {
            var warehouse = await Db.Warehouses.SingleAsync(x => x.Id == WarehouseId);
            var fruit = await Db.FruitProfiles.SingleAsync(x => x.Id == 8842);
            var room = new Room
            {
                Id = roomId,
                Warehouse = warehouse,
                WarehouseId = warehouse.Id,
                Code = roomCode,
                Name = roomName,
                CropQcRoomName = roomName,
                CapacityBins = 1000
            };
            var receipt = new Receipt
            {
                Id = receiptId,
                CropYear = 2026,
                CompuTechReceiptId = $"TR{receiptId}",
                ReceivedAt = Now.AddHours(-8),
                Warehouse = warehouse,
                WarehouseId = warehouse.Id,
                Room = room,
                RoomId = room.Id,
                FruitProfile = fruit,
                FruitProfileId = fruit.Id,
                GrowerNumber = roomId.ToString(CultureInfo.InvariantCulture),
                GrowerName = $"ROOM {roomId} GROWER",
                LotCode = roomId.ToString(CultureInfo.InvariantCulture),
                BinCount = bins,
                CreatedAt = Now.AddHours(-8),
                UpdatedAt = Now.AddHours(-8)
            };
            Db.AddRange(room, receipt);
            Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                Id = adjustmentId,
                CropYear = 2026,
                Receipt = receipt,
                ReceiptId = receipt.Id,
                Warehouse = warehouse,
                WarehouseId = warehouse.Id,
                Room = room,
                RoomId = room.Id,
                FruitProfile = fruit,
                FruitProfileId = fruit.Id,
                GrowerName = receipt.GrowerName,
                LotNumber = receipt.LotCode,
                VarietyCode = fruit.VarietyCode,
                OldBinCount = 0,
                ChangeAmount = bins,
                NewBinCount = bins,
                AdjustmentType = "Receipt",
                InventoryStatus = "Packable",
                AdjustmentAt = Now.AddHours(-8),
                CreatedAt = Now.AddHours(-8)
            });
            await Db.SaveChangesAsync();
            var snapshot = Assert.Single(await Ledger.GetSnapshotsAsync(warehouse.Id, [room.Id], default));
            Db.TreatmentLineageSegments.Add(new TreatmentLineageSegment
            {
                Id = segmentId,
                WarehouseId = warehouse.Id,
                RoomId = room.Id,
                ReceiptId = receipt.Id,
                CropYear = 2026,
                FruitProfileId = fruit.Id,
                IdentityKey = RoomTreatmentService.IdentityKey(snapshot),
                GrowerNumberSnapshot = receipt.GrowerNumber,
                GrowerNameSnapshot = receipt.GrowerName,
                LotNumberSnapshot = receipt.LotCode,
                VarietyCodeSnapshot = fruit.VarietyCode,
                ProductionTypeSnapshot = fruit.ProductionType,
                InventoryStatusSnapshot = "Packable",
                TreatmentState = TreatmentLineageStates.Untreated,
                TreatmentSignature = "u",
                CurrentBins = bins,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return room;
        }

        public async Task<(InterCrewTransferService Service, Room EbsRoom)> CreateInterCrewServiceAsync()
        {
            var ebs = new Warehouse { Id = 8860, Code = "EBS", Name = "EBS" };
            var ebsRoom = new Room { Id = 8861, Warehouse = ebs, WarehouseId = ebs.Id, Code = "EVANS-7", Name = "EVANS-7", CropQcRoomName = "EVANS-7", CapacityBins = 1000 };
            var mcd = new Warehouse { Id = 8862, Code = "McDougall", Name = "McDougall" };
            var mcdRoom = new Room { Id = 8863, Warehouse = mcd, WarehouseId = mcd.Id, Code = "MCD-3", Name = "MCD-3", CropQcRoomName = "MCD-3", CapacityBins = 1000 };
            Db.AddRange(ebs, ebsRoom, mcd, mcdRoom);
            var user = await Db.Users.SingleAsync(x => x.Id == 8843);
            user.EmploymentFacility = EmploymentFacilities.Shared;
            await Db.SaveChangesAsync();
            var invariant = new InventoryDeductionInvariantService(Db, NullLogger<InventoryDeductionInvariantService>.Instance);
            return (new InterCrewTransferService(Db, Service, Ledger, Treatments, invariant, Access,
                new FixedHttpContextAccessor(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, user.Email)], "Test")) }),
                new PacificBusinessTimeService(new FixedClock(Now))), ebsRoom);
        }

        public async Task<int> CurrentBinsAsync() => (await Ledger.GetSnapshotsAsync(WarehouseId, [RoomId], default)).Sum(x => x.CurrentBins);
        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = context; }
    private sealed class ConfigurableAccess : IUserAccessService
    {
        public bool CanAdmin { get; set; } = true;
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) =>
            Task.FromResult(minimumLevel <= PageAccessLevel.Create || CanAdmin);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(CanAdmin ? PageAccessLevel.Admin : PageAccessLevel.Create);
        public void InvalidateAll() { }
    }
}
