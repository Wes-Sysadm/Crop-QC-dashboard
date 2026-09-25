using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class TruckReceiptGrandfatheringTests
{
    [Fact]
    public Task Creation_mode_survives_activation_pause_and_reactivation() => VerifyLifecycleAsync(null);

    [TruckPostgresFact]
    public Task PostgreSql_creation_mode_survives_activation_pause_and_reactivation() => VerifyLifecycleAsync(TruckReceiptPostgresTests.Connection);

    private static async Task VerifyLifecycleAsync(string? connection)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync(connection);
        SetEnabled(false);
        var legacy = await f.DispatchAsync(10);
        Assert.False(legacy.RequiresTruckReceipt);
        // Existing destination inventory is unrelated to subsequent feature activation.
        Assert.True((await f.Dashboard.CreateReceiptAsync(f.ReceiptForm(17, false), default)).Succeeded);
        Assert.True(await TruckReceiptReleaseSafety.CanUsePreFeatureApplicationAsync(f.Db, default));
        SetEnabled(true);
        var current = await f.DispatchAsync(70);
        var receipt = await f.CreateReceiptAsync(70);
        Assert.True(current.RequiresTruckReceipt);
        Assert.False(await TruckReceiptReleaseSafety.CanUsePreFeatureApplicationAsync(f.Db, default));
        foreach (var enabled in new[] { true, false, true })
        {
            SetEnabled(enabled);
            f.Db.ChangeTracker.Clear();
            Assert.False((await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == legacy.Id)).RequiresTruckReceipt);
            Assert.True((await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == current.Id)).RequiresTruckReceipt);
            var oldDetail = (await f.Transfers.GetDetailsAsync(legacy.Id, default))!;
            Assert.True(oldDetail.CanReceive);
            Assert.False(oldDetail.RequiresTruckReceipt);
            Assert.Null(oldDetail.ReconciliationStatus);
            var newDetail = (await f.Transfers.GetDetailsAsync(current.Id, default))!;
            Assert.False(newDetail.CanReceive);
            Assert.True(newDetail.RequiresTruckReceipt);
            Assert.Equal("Awaiting Receipt", newDetail.ReconciliationStatus);
            var queue = (await f.Transfers.GetPageAsync(new(), default)).Queue;
            Assert.Null(Assert.Single(queue, x => x.Id == legacy.Id).ReconciliationStatus);
            Assert.Equal("Awaiting Receipt", Assert.Single(queue, x => x.Id == current.Id).ReconciliationStatus);
            var candidates = (await f.Service.GetAsync(receipt.Id, null, default)).Candidates;
            Assert.DoesNotContain(candidates, x => x.Id == legacy.Id);
            Assert.Contains(candidates, x => x.Id == current.Id);
            var legacyPage = await f.Service.GetAsync(null, legacy.Id, default);
            Assert.Equal(TruckReceiptReconciliationService.LegacyTransferMessage, legacyPage.Error);
            Assert.False(legacyPage.WritesEnabled || legacyPage.CanEditTransfer || legacyPage.CanEditReceipt || legacyPage.CanAdmin);
            Assert.Empty(legacyPage.Available);
            Assert.Equal(17, await f.BalanceAsync(f.Destination.Id));
        }

        var before = await CountsAsync();
        var form = await f.FormAsync(receipt.Id, legacy.Id);
        Assert.Equal(TruckReceiptReconciliationService.LegacyTransferMessage, await f.Service.MatchAsync(form, default));
        Assert.Equal(TruckReceiptReconciliationService.LegacyTransferMessage, await f.Service.CompleteAsync(form, default));
        form.Reason = "No historical conversion";
        Assert.Equal(TruckReceiptReconciliationService.LegacyTransferMessage, await f.Service.ReopenAsync(form, default));
        Assert.Equal(TruckReceiptReconciliationService.LegacyTransferMessage, await f.Service.EditTransferAsync(new()
        {
            TransferId = legacy.Id,
            TransferVersion = form.TransferVersion,
            Bins = 1,
            Reason = "No historical conversion"
        }, default));
        Assert.Equal(before, await CountsAsync());
        Assert.Null((await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == legacy.Id)).ReceivingReceiptId);
        // Activation does not remove legacy receiving, even after a new-workflow load exists.
        Assert.True((await ReceiveAsync(legacy.Id, 10)).Success);
        Assert.Equal(27, await f.BalanceAsync(f.Destination.Id));

        SetEnabled(false);
        var pausedLegacy = await f.DispatchAsync(20);
        Assert.False(pausedLegacy.RequiresTruckReceipt);
        Assert.Contains("paused", (await f.Dashboard.CreateReceiptAsync(f.ReceiptForm(70, true), default)).Error);
        Assert.False((await ReceiveAsync(current.Id, 70)).Success);
        Assert.Contains("paused", await f.Service.MatchAsync(await f.FormAsync(receipt.Id, current.Id), default));
        Assert.True((await ReceiveAsync(pausedLegacy.Id, 20)).Success);
        SetEnabled(true);
        Assert.False((await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == pausedLegacy.Id)).RequiresTruckReceipt);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, current.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, current.Id), default));
        Assert.Equal(117, await f.BalanceAsync(f.Destination.Id));
        Assert.Equal(200, await f.BalanceAsync(f.Source.Id));
        Assert.False(await f.Db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id));
        Assert.Single(await f.Db.InterCrewTransfers.Where(x => x.SourceRoomId == f.Source.Id && x.RequiresTruckReceipt).ToListAsync());
        Assert.Equal(2, await f.Db.InterCrewTransfers.CountAsync(x => x.SourceRoomId == f.Source.Id && !x.RequiresTruckReceipt));
        Assert.Equal(InterCrewTransferStatuses.Received, (await f.Db.InterCrewTransfers.SingleAsync(x => x.Id == legacy.Id)).Status);

        void SetEnabled(bool enabled)
        {
            f.Feature.Enabled = enabled;
            f.Configuration["TruckReceiptReconciliation:Enabled"] = enabled.ToString();
        }
        Task<InterCrewWriteResult> ReceiveAsync(long id, int bins) => f.Transfers.ReceiveAsync(new()
        {
            TransferId = id,
            DestinationRoomId = f.Destination.Id,
            BinsReceived = bins,
            ReceivedAt = f.Now.UtcDateTime.AddHours(-7),
            OperationKey = $"grandfather-{id}"
        }, default);
        async Task<string> CountsAsync() => $"{await f.Db.AuditLogs.CountAsync()}/{await f.Db.RoomInventoryAdjustments.CountAsync()}/{await f.Db.TreatmentLineageMovements.CountAsync()}/{await f.Db.TreatmentLineageSegments.SumAsync(x => x.CurrentBins)}";
    }
}
