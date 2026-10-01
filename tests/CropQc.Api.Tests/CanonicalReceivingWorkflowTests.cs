using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using CropQc.Web.Auth;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalReceivingWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Ordinary_receipt_conversion_and_unposted_receiving_use_one_audited_stock_creation()
    {
        await using var f = await Fixture.Create();
        long[] ids;
        await using (var seed = f.CreateDbContext())
        {
            var grower = await seed.GrowerLots.SingleAsync(x => x.Id == 100000);
            var receipts = new[] { "Door sample", "Truck receipt" }.Select(type => new CropQc.Data.Entities.Receipt
            {
                CropYear = 2026,
                WarehouseId = 9001,
                RoomId = 9002,
                FruitProfileId = 9004,
                GrowerLotId = grower.Id,
                GrowerName = grower.Grower,
                GrowerNumber = grower.LotNumber,
                LotCode = grower.LotNumber,
                CompuTechReceiptId = "LOCAL-ACTIVATE-" + type,
                ReceiptType = type,
                BinCount = 7,
                ReceivedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }).ToArray();
            seed.Receipts.AddRange(receipts); await seed.SaveChangesAsync(); ids = receipts.Select(x => x.Id).ToArray();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        foreach (var id in ids)
        {
            var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id);
            var form = new UpdateReceiptForm
            {
                Id = id,
                ReceiptVersion = receipt.ConcurrencyVersion,
                CropYear = 2026,
                ConfirmCropYear = true,
                ReceivedAt = receipt.ReceivedAt,
                WarehouseId = 9001,
                RoomId = 9002,
                FruitProfileId = 9004,
                GrowerLotId = 100000,
                GrowerName = receipt.GrowerName,
                GrowerNumber = receipt.GrowerNumber!,
                LotCode = receipt.LotCode,
                CompuTechReceiptId = receipt.CompuTechReceiptId,
                ReceiptType = "Truck receipt",
                BinCount = 7
            };
            var before = await f.Snapshot(); var beforeQuantity = await f.Physical();
            foreach (var stage in new[] { "Movement", "BeforeCommit" })
            {
                var reached = false;
                var failing = new InventoryCommandExecutor(factory, new Observer((at, _) =>
                {
                    if (at != stage) return Task.CompletedTask;
                    reached = true; return Task.FromException(new InvalidOperationException("Local activation failure"));
                }));
                Assert.NotNull(await Dashboard(db, failing).UpdateReceiptAsync(form, default));
                Assert.True(reached); Assert.Equal(before, await f.Snapshot());
            }
            var web = Dashboard(db, executor);
            Assert.Null(await web.UpdateReceiptAsync(form, default));
            Assert.Equal(beforeQuantity + 7, await f.Physical());
            Assert.Equal(1, await db.RoomInventoryAdjustments.CountAsync(x => x.ReceiptId == id));
            Assert.Equal(1, await db.TreatmentLineageMovements.CountAsync(x => x.ReceiptId == id));
            Assert.Equal("Truck receipt", await db.Receipts.Where(x => x.Id == id).Select(x => x.ReceiptType).SingleAsync());
            var saved = await f.Snapshot(); Assert.Null(await web.UpdateReceiptAsync(form, default)); Assert.Equal(saved, await f.Snapshot());
            form.OperationKey = Guid.NewGuid().ToString("N"); form.BinCount = 8;
            Assert.NotNull(await web.UpdateReceiptAsync(form, default)); Assert.Equal(saved, await f.Snapshot());
        }
        Assert.Equal(33, await f.Physical());
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalReceiptInventoryActivated"));
    }

    [InventoryPostgresFact]
    public async Task Receipt_treatment_only_changes_exact_receipt_stock_and_reversal_preserves_other_receipt()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var received = await new CanonicalReceivingService(db, executor).ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026, DateTimeOffset.UtcNow,
            9001, 9002, 9004, 100000, "", "LOCAL-RECEIPT-TREAT", 7, "exact receipt treatment", default);
        Assert.Equal(InventoryCommandStatus.Committed, received.Status);
        var receiptId = received.Effects.Single().ParentId!.Value;
        var treatment = new RoomTreatmentService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), new CanonicalOutsideWorkflowTests.Access(),
            Operator(), new CropQc.Shared.Time.PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()), NullLogger<RoomTreatmentService>.Instance, executor);
        var chemical = new CropQc.Data.Entities.TreatmentChemical
        {
            ProductName = "Local receiving treatment",
            ApplicationLevel = "Receiving",
            Crop = "Apples",
            Unit = "bin",
            Currency = "USD",
            UnitPrice = 2.5m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.TreatmentChemicals.Add(chemical); await db.SaveChangesAsync();
        var form = new ReceiptTreatmentApplyForm { ReceiptId = receiptId, TreatmentChemicalId = chemical.Id, AppliedAt = DateTimeOffset.UtcNow, Notes = "Exact receipt only" };
        var page = await treatment.GetReceiptApplyPageAsync(form, true, default);
        Assert.Null(page.Error); Assert.Equal(7, page.TotalBins);
        form.ConfirmedReview = true;
        var result = await treatment.ApplyReceiptAsync(form, default);
        Assert.Null(result.Error);
        var app = await db.RoomTreatmentApplications.AsNoTracking().SingleAsync(x => x.Id == result.ApplicationId);
        Assert.Equal(receiptId, app.ReceiptId); Assert.Equal("Receiving", app.ApplicationLevel); Assert.Equal(7, app.TotalBinsSnapshot);
        Assert.Equal(form.Notes, app.Notes); Assert.Equal(decimal.Round(7 * chemical.UnitPrice, 2), app.EstimatedCostSnapshot);
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var p = Assert.Single((await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.True(p.IsOperable); Assert.Equal(26, p.AuthoritativeQuantity);
        Assert.Equal(7, p.TreatmentSlices.Single(x => x.State == "Confirmed").Quantity);
        Assert.Equal(19, p.TreatmentSlices.Single(x => x.State == "Untreated").Quantity);
        var saved = await f.Snapshot();
        Assert.Null((await treatment.ApplyReceiptAsync(form, default)).Error); Assert.Equal(saved, await f.Snapshot());
        Assert.Null(await treatment.ReverseReceiptAsync(new() { Id = app.Id, Reason = "Local reversal" }, default));
        p = Assert.Single((await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.True(p.IsOperable); Assert.Equal(26, p.AvailableQuantity); Assert.All(p.TreatmentSlices, x => Assert.Equal("Untreated", x.State));
    }

    [InventoryPostgresFact]
    public async Task New_receipt_after_room_treatment_is_separate_untreated_stock_and_reversal_preserves_both_receipts()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var time = new CropQc.Shared.Time.PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock());
        var treatment = new RoomTreatmentService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db),
            new CanonicalOutsideWorkflowTests.Access(), Operator(), time, NullLogger<RoomTreatmentService>.Instance, executor);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room" && x.Crop == "Apples");
        var form = new RoomTreatmentApplyForm { RoomId = 9002, TreatmentChemicalId = chemical.Id, AppliedAt = DateTimeOffset.UtcNow };
        var review = await treatment.GetApplyPageAsync(form, true, default);
        Assert.Null(review.Error);
        Assert.Equal(19, review.TotalBins);
        form.ConfirmedReview = true;
        var applied = await treatment.ApplyAsync(form, default);
        Assert.Null(applied.Error);
        var receiving = new CanonicalReceivingService(db, executor);
        var received = await receiving.ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026, DateTimeOffset.UtcNow,
            9001, 9002, 9004, 100000, "", "LOCAL-LATER", 7, "later receipt", default);
        Assert.True(received.Status == InventoryCommandStatus.Committed, received.Detail);
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var position = Assert.Single((await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.True(position.IsOperable);
        Assert.Equal(19, position.TreatmentSlices.Single(x => x.State == "Confirmed").Quantity);
        Assert.Equal(7, position.TreatmentSlices.Single(x => x.State == "Untreated").Quantity);
        var snapshot = await f.Snapshot();
        Assert.Null((await treatment.ApplyAsync(form, default)).Error);
        Assert.Equal(snapshot, await f.Snapshot());
        Assert.Null(await treatment.ReverseAsync(new() { Id = applied.ApplicationId!.Value, Reason = "Local treatment reversal" }, default));
        position = Assert.Single((await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.True(position.IsOperable);
        Assert.Equal(26, position.AvailableQuantity);
        Assert.All(position.TreatmentSlices, x => Assert.Equal("Untreated", x.State));
        Assert.Equal(2, position.ReceiptProvenance.ReceiptIds.Length);
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
    }

    [InventoryPostgresFact]
    public async Task Web_and_API_receiving_share_creation_proof_and_replay_without_duplicate_stock()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var http = Operator();
        var web = Dashboard(db, executor, http);
        var grower = await db.GrowerLots.SingleAsync(x => x.Id == 100000);
        var form = new CreateReceiptForm
        {
            CropYear = 2026,
            ConfirmCropYear = true,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "LOCAL-WEB",
            WarehouseId = 9001,
            RoomId = 9002,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            GrowerNumber = grower.LotNumber,
            GrowerName = grower.Grower,
            BinCount = 7,
            ReceiptType = "Truck receipt"
        };
        var created = await web.CreateReceiptAsync(form, default);
        Assert.Null(created.Error);
        Assert.Equal(26, await f.Physical());
        var saved = await f.Snapshot();
        var replay = await web.CreateReceiptAsync(form, default);
        Assert.Equal(created.ReceiptId, replay.ReceiptId);
        Assert.Null(replay.Error);
        Assert.Equal(saved, await f.Snapshot());
        var api = new CropQc.Api.Services.ReceiptService(db, new CropQc.Api.Services.AuditService(db), new(db, executor), http);
        await CanonicalOperatorSessionTests.GrantReceivingAsync(db);
        var request = new CropQc.Api.Dtos.CreateReceiptRequest(2026, DateTimeOffset.UtcNow, "LOCAL-API", 9001, 9003,
            9004, grower.Grower, grower.LotNumber, 11, Guid.NewGuid().ToString("N"), 100000);
        var apiCreated = await api.CreateAsync(request, default);
        Assert.Null(apiCreated.Error);
        Assert.NotNull(apiCreated.Receipt);
        Assert.Equal(11, await f.Physical(9003));
        saved = await f.Snapshot();
        Assert.Equal(apiCreated.Receipt, (await api.CreateAsync(request, default)).Receipt);
        Assert.Equal(saved, await f.Snapshot());
        Assert.NotNull((await api.CreateAsync(request with { BinCount = 12 }, default)).Error);
        Assert.Equal(saved, await f.Snapshot());
        var positions = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002, 9003]), new(), DateTimeOffset.UtcNow)).Positions;
        Assert.All(positions, p => { Assert.True(p.IsOperable); Assert.Equal(p.AuthoritativeQuantity, p.RawProjectionQuantity); });
        Assert.Equal(37, positions.Sum(p => p.AuthoritativeQuantity));
        Assert.Equal(2, await db.InventoryCommands.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalReceiptCreated"));
    }

    [InventoryPostgresFact]
    public async Task Receiving_failure_rolls_back_receipt_ledger_projection_and_normalization_together()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var before = await f.Snapshot();
        foreach (var stage in new[] { "Movement", "OperationAudit", "BeforeCommit" })
        {
            var executor = new InventoryCommandExecutor(factory, new Observer((s, _) => s == stage
                ? Task.FromException(new IOException(stage)) : Task.CompletedTask));
            var receiving = new CanonicalReceivingService(db, executor);
            await Assert.ThrowsAsync<IOException>(() => receiving.ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
                DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "LOCAL-ROLLBACK", 7, "receiving rollback", default));
            Assert.Equal(before, await f.Snapshot());
        }
    }

    private sealed class RequestContext : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    internal static IHttpContextAccessor Operator() => new RequestContext()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "canonical-test@example.invalid")], "test"))
        }
    };
    internal static DashboardDataService Dashboard(CropQcDbContext db, IInventoryCommandExecutor executor, IHttpContextAccessor? http = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new(db, null!, new FileStorageOptions(), new EmailOptions(), null!, new GoogleAuthenticationOptions(),
            null!, null!, new QcPhotoRequirementPolicy(), null!, new CropYearService(db, configuration), http ?? Operator(),
            configuration, NullLogger<DashboardDataService>.Instance, new CanonicalOutsideWorkflowTests.Access(), canonicalCommands: executor);
    }
}
