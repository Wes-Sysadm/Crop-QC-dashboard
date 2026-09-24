using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class TruckReceiptReleaseSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_type_round_trip_cannot_recreate_transferred_inventory(bool completed)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        if (completed) Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var edit = (await f.Dashboard.GetReceiptEditAsync(receipt.Id, default)).Form;
        edit.ConfirmCropYear = true;
        edit.ReceiptType = "Lot sample";
        Assert.NotNull(await f.Dashboard.UpdateReceiptAsync(edit, default));
        edit.ReceiptType = "Truck receipt";
        await f.Dashboard.UpdateReceiptAsync(edit, default);
        Assert.Equal(completed ? 70 : 0, await f.BalanceAsync(f.Destination.Id));
        Assert.False(await f.Db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id));
        Assert.Equal("Truck receipt", (await f.Db.Receipts.SingleAsync(x => x.Id == receipt.Id)).ReceiptType);
    }

    [Fact]
    public async Task Flag_defaults_off_normal_receiving_and_untouched_legacy_loads_continue()
    {
        Assert.False(new TruckReceiptOptions().Enabled);
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        f.Feature.Enabled = false;
        f.Configuration["TruckReceiptReconciliation:Enabled"] = "false";
        var transfer = await f.DispatchAsync(70);
        Assert.False(transfer.RequiresTruckReceipt);
        Assert.True((await f.Dashboard.CreateReceiptAsync(f.ReceiptForm(17, false), default)).Succeeded);
        Assert.Contains("paused", (await f.Dashboard.CreateReceiptAsync(f.ReceiptForm(70, true), default)).Error);
        Assert.Empty(await f.Db.ReceiptVarietyLines.ToListAsync());
        Assert.True(await TruckReceiptReleaseSafety.CanUsePreFeatureApplicationAsync(f.Db, default));
        var receive = await f.Transfers.ReceiveAsync(new()
        {
            TransferId = transfer.Id,
            DestinationRoomId = f.Destination.Id,
            BinsReceived = 70,
            ReceivedAt = f.Now.UtcDateTime.AddHours(-7),
            OperationKey = "legacy-before-activation"
        }, default);
        Assert.True(receive.Success, receive.Error);
        Assert.Equal(87, await f.BalanceAsync(f.Destination.Id));
    }

    [Theory]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    public async Task Disabling_never_reinterprets_existing_state_or_enables_a_legacy_bypass(string stage)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        Assert.True(await TruckReceiptReleaseSafety.CanUsePreFeatureApplicationAsync(f.Db, default));
        var transfer = await f.DispatchAsync(70);
        var receipt = await f.CreateReceiptAsync(70);
        if (stage != "B") Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        if (stage == "D") Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        f.Feature.Enabled = false;
        f.Configuration["TruckReceiptReconciliation:Enabled"] = "false";
        var before = await f.Db.AuditLogs.CountAsync();
        var form = await f.FormAsync(receipt.Id, transfer.Id);
        Assert.Contains("paused", await f.Service.MatchAsync(form, default));
        Assert.Contains("paused", await f.Service.CompleteAsync(form, default));
        Assert.Contains("paused", await f.Service.ReopenAsync(form, default));
        Assert.Contains("paused", await f.Service.EditReceiptAsync(form, default));
        Assert.Contains("paused", await f.Service.EditTransferAsync(new() { TransferId = transfer.Id }, default));
        var page = await f.Service.GetAsync(receipt.Id, null, default);
        Assert.False(page.CanEditReceipt || page.CanEditTransfer || page.CanAdmin || page.WritesEnabled);
        Assert.False((await f.Transfers.ReceiveAsync(new()
        {
            TransferId = transfer.Id,
            DestinationRoomId = f.Destination.Id,
            BinsReceived = 70,
            ReceivedAt = f.Now.UtcDateTime.AddHours(-7),
            OperationKey = "no-legacy-bypass"
        }, default)).Success);
        Assert.NotNull(await f.Transfers.ReverseAsync(new() { TransferId = transfer.Id, Reason = "No bypass", OperationKey = "no-reverse" }, default));
        Assert.False(await TruckReceiptReleaseSafety.CanUsePreFeatureApplicationAsync(f.Db, default));
        Assert.Equal(before, await f.Db.AuditLogs.CountAsync());
        Assert.Equal(230, await f.BalanceAsync(f.Source.Id));
        Assert.Equal(stage == "D" ? 70 : 0, await f.BalanceAsync(f.Destination.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_downstream_treatment_blocks_reopen_even_after_treatment_reversal(bool reverseTreatment)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var chemical = new TreatmentChemical
        {
            ProductName = "Reopen test",
            Crop = "Pears",
            ApplicationLevel = TreatmentApplicationLevels.Room,
            Unit = "bin",
            Currency = "USD",
            CreatedAt = f.Now,
            UpdatedAt = f.Now
        };
        f.Db.Add(chemical); await f.Db.SaveChangesAsync();
        var treatment = await f.Treatments.ApplyAsync(new()
        {
            RoomId = f.Destination.Id,
            TreatmentChemicalId = chemical.Id,
            AppliedAt = f.Now,
            OperationKey = "downstream-room-treatment",
            ConfirmedReview = true
        }, default);
        Assert.Null(treatment.Error);
        if (reverseTreatment) Assert.Null(await f.Treatments.ReverseAsync(new() { Id = treatment.ApplicationId!.Value, Reason = "Treatment correction" }, default));
        var movements = await f.Db.TreatmentLineageMovements.Select(x => x.Id).ToListAsync();
        var audits = await f.Db.AuditLogs.CountAsync();
        var form = await f.FormAsync(receipt.Id, transfer.Id); form.Reason = "Unsafe reopen";
        Assert.Contains("treatment", await f.Service.ReopenAsync(form, default));
        Assert.Equal(70, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(audits, await f.Db.AuditLogs.CountAsync());
        Assert.Equal(movements, await f.Db.TreatmentLineageMovements.Select(x => x.Id).ToListAsync());
        Assert.True(await f.Db.RoomTreatmentApplications.AnyAsync(x => x.Id == treatment.ApplicationId));
    }

    [Fact]
    public async Task Actual_subsequent_dispatch_blocks_reopen_and_preserves_both_loads()
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var option = Assert.Single(await f.Inventory.GetInventoryAsync(default), x => x.RoomId == f.Destination.Id && x.IsAvailable);
        var dispatched = await f.Transfers.DispatchAsync(new()
        {
            SourceWarehouseId = f.Destination.WarehouseId,
            SourceRoomId = f.Destination.Id,
            SourceKey = option.SourceKey,
            ExpectedAvailableBins = option.AvailableBins,
            BinsLoaded = 10,
            DestinationCustodyGroup = TransferCustodyGroups.Ebs,
            LoadedAt = f.Now.UtcDateTime.AddHours(-7),
            ConfirmedReview = true
        }, default);
        Assert.True(dispatched.Success, dispatched.Error);
        var form = await f.FormAsync(receipt.Id, transfer.Id); form.Reason = "Unsafe reopen";
        Assert.Contains("subsequent", await f.Service.ReopenAsync(form, default));
        Assert.Equal(60, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(10, (await f.Service.ActiveAllocationsAsync(dispatched.TransferId!.Value, default)).Sum(x => x.Bins));
        Assert.Equal(InterCrewTransferStatuses.Received, (await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == transfer.Id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Evidence_receipt_cannot_claim_original_lineage_through_receipt_treatment(bool completed)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        var transfer = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        if (completed) Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var result = await f.Treatments.ApplyReceiptAsync(new()
        {
            ReceiptId = receipt.Id,
            TreatmentChemicalId = 1,
            AppliedAt = f.Now,
            OperationKey = "evidence-cannot-claim-treatment",
            ConfirmedReview = true
        }, default);
        Assert.Contains("receiving evidence", result.Error);
        Assert.Empty(await f.Db.RoomTreatmentApplications.ToListAsync());
        Assert.False(await f.Db.TreatmentLineageSegments.AnyAsync(x => x.ReceiptId == receipt.Id));
    }
}
