using System.Reflection;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class LegacyReceiptReclassificationOriginTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [InventoryPostgresFact]
    public async Task Recorded_legacy_reclassification_proves_origin_without_rewriting_history()
    {
        await using var f = await Seed();
        var before = await f.Snapshot();
        await using var db = f.CreateDbContext();
        Assert.Null(await Origin(db));
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("parent")]
    [InlineData("debit")]
    [InlineData("duplicate-credit")]
    [InlineData("excess-credit")]
    [InlineData("quantity")]
    [InlineData("receipt")]
    [InlineData("identity")]
    [InlineData("audit-missing")]
    [InlineData("audit-contradictory")]
    [InlineData("audit-duplicate")]
    [InlineData("chronology")]
    [InlineData("origin")]
    [InlineData("missing-origin-shared-pool")]
    [InlineData("intervening-withdrawal")]
    [InlineData("actor")]
    [InlineData("operation-key")]
    [InlineData("incomplete")]
    [InlineData("malformed-snapshot")]
    public async Task Unsupported_reclassification_is_rejected_without_writes(string corruption)
    {
        await using var f = await Seed();
        await using var db = f.CreateDbContext();
        var change = await db.ReceiptInventoryOverrides.SingleAsync();
        var legs = await db.RoomInventoryAdjustments.Where(x => x.ReceiptInventoryOverrideId == change.Id).OrderBy(x => x.Id).ToArrayAsync();
        var audit = await db.AuditLogs.SingleAsync();
        switch (corruption)
        {
            case "parent": foreach (var leg in legs) leg.ReceiptInventoryOverrideId = null; db.ReceiptInventoryOverrides.Remove(change); break;
            case "debit": db.RoomInventoryAdjustments.Remove(legs[0]); break;
            case "duplicate-credit":
                var copy = (RoomInventoryAdjustment)db.Entry(legs[1]).CurrentValues.ToObject();
                copy.Id = 0; copy.InventoryOperationKey += ":duplicate"; copy.InventoryInvariantVersion = 3;
                db.RoomInventoryAdjustments.Add(copy); break;
            case "excess-credit": legs[1].ChangeAmount++; break;
            case "quantity": change.NewReceiptBinCount++; break;
            case "receipt": legs[1].ReceiptId = 100001; break;
            case "identity": change.AfterReceiptSnapshotJson = change.AfterReceiptSnapshotJson.Replace("9004", "9005"); break;
            case "audit-missing": db.AuditLogs.Remove(audit); break;
            case "audit-contradictory": audit.AfterValuesJson = "{}"; break;
            case "audit-duplicate": db.AuditLogs.Add(new() { Action = audit.Action, EntityName = audit.EntityName, EntityKey = audit.EntityKey, CreatedAt = audit.CreatedAt }); break;
            case "chronology": legs[1].CreatedAt = change.CreatedAt.AddSeconds(-1); break;
            case "origin": (await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000)).ChangeAmount = 55; break;
            case "missing-origin-shared-pool":
                // Enough unrelated stock must not stand in for this receipt's origin.
                (await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000)).ReceiptId = 100001;
                var other = await db.Receipts.SingleAsync(x => x.Id == 100001);
                other.FruitProfileId = 9005; other.GrowerLotId = 100000; other.LotCode = "1372"; other.BinCount = 56;
                db.RoomInventoryAdjustments.Remove(await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100001));
                break;
            case "intervening-withdrawal":
                var neighbor = (Receipt)db.Entry(await db.Receipts.SingleAsync(x => x.Id == 100000)).CurrentValues.ToObject();
                neighbor.Id = 200000; neighbor.CompuTechReceiptId = "Synthetic neighbor"; neighbor.FruitProfileId = 9005; neighbor.BinCount = 10;
                db.Receipts.Add(neighbor);
                var neighborOrigin = (RoomInventoryAdjustment)db.Entry(await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000)).CurrentValues.ToObject();
                neighborOrigin.Id = 200000; neighborOrigin.ReceiptId = neighbor.Id; neighborOrigin.ChangeAmount = 10; neighborOrigin.NewBinCount = 10;
                neighborOrigin.CreatedAt = InventoryEvidenceCorpus.Start.AddSeconds(-1); db.RoomInventoryAdjustments.Add(neighborOrigin);
                var withdrawal = (RoomInventoryAdjustment)db.Entry(legs[0]).CurrentValues.ToObject();
                withdrawal.Id = 0; withdrawal.ReceiptInventoryOverrideId = null; withdrawal.InventoryOperationKey = null;
                withdrawal.InventoryInvariantVersion = 0; withdrawal.AdjustmentType = "BinsRun"; withdrawal.ChangeAmount = -1;
                withdrawal.CreatedAt = change.CreatedAt.AddSeconds(-1); db.RoomInventoryAdjustments.Add(withdrawal);
                break;
            case "actor": audit.UserId = null; break;
            case "operation-key": legs[1].InventoryOperationKey += "-wrong"; break;
            case "incomplete": change.IsComplete = false; break;
            case "malformed-snapshot": change.BeforeReceiptSnapshotJson = "{"; break;
        }
        await db.SaveChangesAsync();
        var before = await f.Snapshot();
        Assert.NotNull(await Origin(db));
        Assert.Equal(before, await f.Snapshot());
        Assert.Empty(await db.ActualRuns.ToArrayAsync());
        Assert.Empty(await db.InventoryCommands.ToArrayAsync());
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_ActualRun_consumes_44_and_74_once_and_rolls_back_every_failure_boundary(bool initialIncrease)
    {
        await using var f = await Seed();
        if (initialIncrease)
        {
            await SeedInitialIncrease(f);
            await LegacyGrowerReconciliationOriginTests.Seed(f);
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        BinsRunService Service(IInventoryCommandObserver? observer = null) => new(db, new CanonicalOutsideWorkflowTests.Access(),
            NullLogger<BinsRunService>.Instance, canonicalCommands: new InventoryCommandExecutor(factory, observer, new CanonicalRunExpectationWriter()));
        var service = Service();
        var history = await HistoricalRows(db);
        var options = (await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, principal, default)).AvailableInventory;
        Assert.Equal(2, options.Count);
        Assert.All(options, x => Assert.True(x.IsAvailable, x.UnavailableReason));
        var form = new ActualRunForm
        {
            RunFacilityWarehouseId = 9001,
            RunAt = new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.Zero),
            SalesDeskId = await db.SalesDesks.Where(x => x.IsActive && x.Name == "Honey Bear").Select(x => x.Id).SingleAsync(),
            Lines = options.OrderBy(x => x.Lot).Select(x => new ActualRunLineForm
            {
                InventoryKey = x.InventoryKey,
                TreatmentSignature = x.TreatmentSignature,
                CanonicalFingerprint = x.CanonicalFingerprint,
                ExpectedAvailableBins = x.CurrentBins,
                BinsRun = x.Lot == "1372" ? 44 : 74
            }).ToList()
        };
        var before = await f.Snapshot();
        foreach (var stage in new[] { "Normalized", "Parent", "Source1", "PersistedSource1", "Source2", "PersistedSource2", "Movement", "OperationAudit", "BeforeCommit" })
        {
            var failing = Service(new Observer((s, _) => s == stage ? Task.FromException(new IOException(stage)) : Task.CompletedTask));
            await Assert.ThrowsAsync<IOException>(() => failing.CreateActualRunAsync(form, principal, default));
            Assert.Equal(before, await f.Snapshot());
        }
        foreach (var failure in new[] { "quantity", "treatment", "fingerprint" })
        {
            var invalid = JsonSerializer.Deserialize<ActualRunForm>(JsonSerializer.Serialize(form))!;
            if (failure == "quantity") invalid.Lines[0].BinsRun = initialIncrease ? 401 : 57;
            if (failure == "treatment") invalid.Lines[0].TreatmentSignature = "unknown";
            if (failure == "fingerprint") invalid.Lines[0].CanonicalFingerprint = "stale";
            Assert.NotNull(await service.CreateActualRunAsync(invalid, principal, default));
            Assert.Equal(before, await f.Snapshot());
        }
        Assert.Null(await service.CreateActualRunAsync(form, principal, default));
        Assert.Equal(history, await HistoricalRows(db));
        Assert.Equal(initialIncrease ? 356 : 12, await f.Physical());
        var run = await db.ActualRuns.AsNoTracking().SingleAsync();
        Assert.Equal(form.RunAt, run.RunAt); Assert.Equal(form.SalesDeskId, run.SalesDeskId); Assert.Equal(9001, run.RunFacilityWarehouseId);
        var entries = await db.BinsRunEntries.AsNoTracking().OrderBy(x => x.LotNumber).ToArrayAsync();
        Assert.Equal(new[] { 44, 74 }, entries.Select(x => x.BinsRun));
        Assert.All(entries, x => { Assert.Equal(9002, x.RoomId); Assert.Equal(9004, x.FruitProfileId); Assert.Equal(run.Id, x.ActualRunId); Assert.Null(x.ReceiptId); });
        Assert.Equal(-118, await db.RoomInventoryAdjustments.Where(x => x.ActualRunId == run.Id).SumAsync(x => x.ChangeAmount));
        Assert.Equal(118, await db.TreatmentLineageMovements.Where(x => x.BinsRunEntryId != null).SumAsync(x => x.BinCount));
        Assert.All(await db.TreatmentLineageMovements.Where(x => x.BinsRunEntryId != null).ToArrayAsync(), x => Assert.Equal("u", x.TreatmentSignatureSnapshot));
        Assert.Single(await db.ActualRunRevisions.ToArrayAsync());
        Assert.Single(await db.InventoryCommands.ToArrayAsync());
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "CanonicalInventoryCommand" && x.EntityKey == form.OperationKey).ToArrayAsync());
        var after = await f.Snapshot();
        Assert.Null(await service.CreateActualRunAsync(form, principal, default)); Assert.Equal(after, await f.Snapshot());
        form.Lines[0].BinsRun++;
        Assert.NotNull(await service.CreateActualRunAsync(form, principal, default)); Assert.Equal(after, await f.Snapshot());
        form.Lines[0].BinsRun--; form.OperationKey = Guid.NewGuid().ToString("N");
        Assert.NotNull(await service.CreateActualRunAsync(form, principal, default)); Assert.Equal(after, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("audit-missing")]
    [InlineData("audit-duplicate")]
    [InlineData("audit-before")]
    [InlineData("audit-after")]
    [InlineData("audit-actor")]
    [InlineData("audit-identity")]
    [InlineData("audit-malformed")]
    [InlineData("chronology")]
    [InlineData("quantity")]
    [InlineData("receipt")]
    [InlineData("identity")]
    [InlineData("parent")]
    [InlineData("origin-missing")]
    [InlineData("duplicate-edit")]
    [InlineData("later-version")]
    [InlineData("prior-withdrawal")]
    public async Task Unsupported_initial_increase_blocks_the_run_without_writes(string corruption)
    {
        await using var f = await Seed();
        await SeedInitialIncrease(f);
        await using var db = f.CreateDbContext();
        var receipt = await db.Receipts.SingleAsync(x => x.Id == 200000);
        var edit = await db.RoomInventoryAdjustments.SingleAsync(x => x.AdjustmentType == "ReceiptEdit");
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityName == nameof(Receipt));
        switch (corruption)
        {
            case "audit-missing": db.AuditLogs.Remove(audit); break;
            case "audit-duplicate": var extraAudit = (AuditLog)db.Entry(audit).CurrentValues.ToObject(); extraAudit.Id = 0; db.AuditLogs.Add(extraAudit); break;
            case "audit-before": audit.BeforeValuesJson = audit.AfterValuesJson; break;
            case "audit-after": audit.AfterValuesJson = audit.BeforeValuesJson; break;
            case "audit-actor": audit.UserId = null; break;
            case "audit-identity": audit.BeforeValuesJson = audit.BeforeValuesJson!.Replace("9004", "9005"); break;
            case "audit-malformed": audit.BeforeValuesJson = "{"; break;
            case "chronology": audit.CreatedAt = edit.CreatedAt.AddSeconds(-1); break;
            case "quantity": edit.ChangeAmount++; break;
            case "receipt": edit.ReceiptId = 100000; break;
            case "identity": edit.FruitProfileId = 9005; break;
            case "parent": edit.ReceiptInventoryOverrideId = (await db.ReceiptInventoryOverrides.SingleAsync()).Id; break;
            case "origin-missing": db.RoomInventoryAdjustments.Remove(await db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptId == 200000 && x.AdjustmentType == "ReceiptAdd")); break;
            case "duplicate-edit": var extraEdit = (RoomInventoryAdjustment)db.Entry(edit).CurrentValues.ToObject(); extraEdit.Id = 0; db.RoomInventoryAdjustments.Add(extraEdit); break;
            case "later-version": receipt.ConcurrencyVersion++; break;
            case "prior-withdrawal":
                var withdrawal = (RoomInventoryAdjustment)db.Entry(edit).CurrentValues.ToObject();
                withdrawal.Id = 0; withdrawal.AdjustmentType = "BinsRun"; withdrawal.ChangeAmount = -1;
                withdrawal.CreatedAt = receipt.UpdatedAt.AddSeconds(-1); db.RoomInventoryAdjustments.Add(withdrawal); break;
        }
        await db.SaveChangesAsync();
        var command = await PoolCommand(db, InventoryCommandKind.Dump, 44);
        var before = await f.Snapshot();
        Assert.NotNull(await Origin(db));
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(command)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Same_identity_legacy_location_correction_keeps_existing_custody_validation()
    {
        await using var f = await Seed();
        await using var db = f.CreateDbContext();
        var origin = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
        var debit = await db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptInventoryOverrideId != null && x.ChangeAmount < 0);
        origin.FruitProfileId = debit.FruitProfileId = 9004;
        origin.RoomId = debit.RoomId = 9003;
        var parent = await db.ReceiptInventoryOverrides.SingleAsync();
        parent.BeforeReceiptSnapshotJson = parent.BeforeReceiptSnapshotJson.Replace("9005", "9004").Replace("9002", "9003");
        // Same-identity custody already uses its funded debit; the new audit
        // interpretation is needed only to cross a historical identity change.
        db.AuditLogs.RemoveRange(await db.AuditLogs.ToArrayAsync());
        await db.SaveChangesAsync();
        var before = await f.Snapshot();
        Assert.Null(await Origin(db));
        Assert.Equal(before, await f.Snapshot());
    }

    private static async Task SeedInitialIncrease(Fixture f)
    {
        await using var db = f.CreateDbContext();
        var receipt = (Receipt)db.Entry(await db.Receipts.SingleAsync(x => x.Id == 100000)).CurrentValues.ToObject();
        receipt.Id = 200000; receipt.CompuTechReceiptId = "Synthetic initial increase";
        receipt.BinCount = 64; receipt.ConcurrencyVersion = 1;
        receipt.UpdatedAt = InventoryEvidenceCorpus.Start.AddSeconds(30); db.Receipts.Add(receipt);
        var origin = (RoomInventoryAdjustment)db.Entry(await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000)).CurrentValues.ToObject();
        origin.Id = 200000; origin.ReceiptId = receipt.Id; origin.FruitProfileId = 9004;
        origin.VarietyCode = "BART"; origin.InventoryStatus = "Conventional"; origin.AdjustmentAt = receipt.ReceivedAt;
        db.RoomInventoryAdjustments.Add(origin);
        var edit = (RoomInventoryAdjustment)db.Entry(origin).CurrentValues.ToObject();
        edit.Id = 200001; edit.AdjustmentType = "ReceiptEdit"; edit.ChangeAmount = 8; edit.OldBinCount = 56; edit.NewBinCount = 64;
        edit.Reason = "Receipt source adjustment"; edit.CreatedByUserId = 8000;
        edit.AdjustmentAt = receipt.UpdatedAt.AddMilliseconds(1); edit.CreatedAt = receipt.UpdatedAt.AddMilliseconds(2);
        db.RoomInventoryAdjustments.Add(edit);
        string Snapshot(int bins) => JsonSerializer.Serialize(new
        {
            receipt.CropYear,
            receipt.ReceivedAt,
            receipt.CompuTechReceiptId,
            receipt.ReceiptType,
            receipt.WarehouseId,
            receipt.RoomId,
            receipt.FruitProfileId,
            receipt.GrowerLotId,
            receipt.GrowerNumber,
            receipt.GrowerName,
            receipt.LotCode,
            BinCount = bins
        });
        db.AuditLogs.Add(new()
        {
            EntityName = nameof(Receipt),
            EntityKey = receipt.Id.ToString(),
            Action = "Update",
            UserId = 8000,
            SourceApplication = "Web",
            CreatedAt = receipt.UpdatedAt.AddMilliseconds(3),
            BeforeValuesJson = Snapshot(56),
            AfterValuesJson = Snapshot(64)
        });
        (await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000)).CurrentBins += 64;
        await db.SaveChangesAsync();
    }

    [InventoryPostgresFact]
    public async Task Corrected_pool_moves_and_restores_with_original_history_and_treatment_intact()
    {
        await using var f = await Seed();
        await using var db = f.CreateDbContext();
        var history = await HistoricalRows(db);
        var command = await PoolCommand(db, InventoryCommandKind.RoomMove, 44);
        var moved = await f.Execute(command);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        Assert.Equal(86, await f.Physical()); Assert.Equal(44, await f.Physical(9003));
        var destination = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9003]), new(), DateTimeOffset.UtcNow)).Positions);
        var reversal = command with
        {
            OperationKey = Guid.NewGuid().ToString("N"),
            Kind = InventoryCommandKind.ReverseRoomMove,
            PhysicalParentId = moved.Effects[0].ParentId,
            Lines = [new(new(destination.Identity, destination.Location, destination.Watermark.Fingerprint, destination.Watermark.Versions), 44, "u", new(9001, 9002))]
        };
        var restored = await f.Execute(reversal);
        Assert.True(restored.Status == InventoryCommandStatus.Committed, restored.Detail);
        Assert.Equal(130, await f.Physical()); Assert.Equal(0, await f.Physical(9003));
        Assert.Equal(history, await HistoricalRows(db));
        Assert.Null(await Origin(db));
        Assert.All(await db.TreatmentLineageMovements.ToArrayAsync(), x => Assert.Equal("u", x.TreatmentSignatureSnapshot));
    }

    [InventoryPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Independent_sessions_consume_corrected_stock_once(bool identicalIntent)
    {
        await using var f = await Seed();
        await using var db = f.CreateDbContext();
        var a = await PoolCommand(db, InventoryCommandKind.Dump, 44);
        var b = identicalIntent ? a : a with { OperationKey = Guid.NewGuid().ToString("N") };
        var ready = 0; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref ready) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var results = await Task.WhenAll(f.Execute(a, observer), f.Execute(b, observer));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => identicalIntent ? x.Status == InventoryCommandStatus.Replayed
            : x.Status is InventoryCommandStatus.Stale or InventoryCommandStatus.Conflict or InventoryCommandStatus.RetryRequired);
        Assert.Equal(86, await f.Physical());
        Assert.Equal(44, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
        Assert.Single(await db.InventoryCommands.ToArrayAsync());
    }

    [InventoryPostgresFact]
    public async Task Stale_version_and_missing_correction_audit_block_before_any_run_write()
    {
        await using var f = await Seed();
        await using var db = f.CreateDbContext();
        var command = await PoolCommand(db, InventoryCommandKind.Dump, 44);
        var line = command.Lines[0];
        var stale = command with { Lines = [line with { Source = line.Source with { ExpectedVersions = [new("Receipt", "100000", 999, DateTimeOffset.UtcNow)] } }] };
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Stale, (await f.Execute(stale)).Status); Assert.Equal(before, await f.Snapshot());
        db.AuditLogs.Remove(await db.AuditLogs.SingleAsync()); await db.SaveChangesAsync();
        before = await f.Snapshot();
        var result = await f.Execute(command);
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Contains("legacy receipt reclassification", result.Detail);
        Assert.Equal(before, await f.Snapshot());
    }

    private static async Task<InventoryCommand> PoolCommand(CropQcDbContext db, InventoryCommandKind kind, int quantity)
    {
        var result = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions, x => x.Identity.Lot == "1372" && x.Identity.FruitProfileId == 9004 && x.AuthoritativeQuantity > 0);
        return new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Synthetic corrected pool",
            [new(new(result.Identity, result.Location, result.Watermark.Fingerprint, result.Watermark.Versions), quantity, "u",
                kind == InventoryCommandKind.RoomMove ? new(9001, 9003) : null)]);
    }

    private static async Task<string> HistoricalRows(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        receipts = await db.Receipts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        corrections = await db.ReceiptInventoryOverrides.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        identities = await db.InventoryIdentityCorrections.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        transfers = await db.RoomTransfers.AsNoTracking().Where(x => x.CreatedAt <= InventoryEvidenceCorpus.Start.AddMinutes(10)).OrderBy(x => x.Id).ToArrayAsync(),
        ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.CreatedAt <= InventoryEvidenceCorpus.Start.AddMinutes(10)).OrderBy(x => x.Id).ToArrayAsync(),
        audits = await db.AuditLogs.AsNoTracking().Where(x => x.CreatedAt <= InventoryEvidenceCorpus.Start.AddMinutes(10)).OrderBy(x => x.Id).ToArrayAsync()
    });

    internal static async Task<string?> Origin(CropQcDbContext db)
    {
        var evidence = await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9002]), DateTimeOffset.UtcNow, default);
        var guard = typeof(InventoryEvidenceLoader).Assembly.GetType("CropQc.Data.Inventory.InventoryOriginGuard")!;
        return await (Task<string?>)guard.GetMethod("ValidateAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [db, evidence.Positions, DateTimeOffset.UtcNow, CancellationToken.None, false])!;
    }

    internal static async Task<Fixture> Seed()
    {
        var f = await Fixture.Create(2);
        await using var db = f.CreateDbContext();
        var at = InventoryEvidenceCorpus.Start.AddMinutes(1);
        foreach (var existing in await db.FruitProfiles.Where(x => x.VarietyCode == "BART" || x.VarietyCode == "ORGA").ToArrayAsync())
            existing.VarietyCode = "BASE-" + existing.VarietyCode;
        await db.SaveChangesAsync();
        db.FruitProfiles.Add(new() { Id = 9005, Name = "Original organic apple", VarietyCode = "ORGA", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true });
        var profile = await db.FruitProfiles.SingleAsync(x => x.Id == 9004);
        profile.Name = "Bartlett"; profile.VarietyCode = "BART"; profile.FruitType = "Pear";
        var actor = await db.Users.SingleAsync(x => x.Id == 8000);
        actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = InventoryEvidenceCorpus.Start;
        foreach (var id in new[] { 100000, 100001 })
        {
            var quantity = id == 100000 ? 56 : 74;
            var lot = id == 100000 ? "1372" : "2350";
            var grower = await db.GrowerLots.SingleAsync(x => x.Id == id);
            grower.LotNumber = lot; grower.Grower = id == 100000 ? "Synthetic Top Pear" : "Synthetic Whitestone";
            var receipt = await db.Receipts.SingleAsync(x => x.Id == id);
            receipt.BinCount = quantity; receipt.LotCode = lot; receipt.GrowerNumber = lot; receipt.GrowerName = grower.Grower;
            var origin = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == id);
            origin.ChangeAmount = quantity; origin.NewBinCount = quantity; origin.LotNumber = lot;
            origin.VarietyCode = id == 100000 ? "ORGA" : "BART";
            origin.FruitProfileId = id == 100000 ? 9005 : 9004;
            var segment = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == id);
            segment.CurrentBins = quantity; segment.GrowerNumberSnapshot = lot; segment.LotNumberSnapshot = lot;
            segment.VarietyCodeSnapshot = "BART";
            segment.IdentityKey = new InventoryIdentity(2026, id, 9004, lot, lot, "BART", "Conventional", false, "").Key;
            segment.CreatedAt = at.AddMinutes(1); segment.UpdatedAt = segment.CreatedAt;
        }
        var revision = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            ReceiptId = 100000,
            ActionType = ReceiptInventoryOverrideActionTypes.InventoryReclassification,
            OldReceiptBinCount = 56,
            NewReceiptBinCount = 56,
            InventoryDelta = 0,
            CurrentInventoryBefore = 56,
            CurrentInventoryAfter = 56,
            AdministratorUserId = 8000,
            Reason = "Correct original fruit identity",
            OperationKey = Guid.NewGuid().ToString("N"),
            CreatedAt = at,
            IsComplete = true,
            ExpectedAdjustmentCount = 2,
            BeforeReceiptSnapshotJson = Snapshot(9005, 0),
            AfterReceiptSnapshotJson = Snapshot(9004, 1),
            AffectedInventorySnapshotJson = JsonSerializer.Serialize(new[] { new { warehouseId = 9001, roomId = 9002, cropYear = 2026,
                growerLotId = 100000, fruitProfileId = 9005, lot = "1372", variety = "ORGA", inventoryStatus = "Organic", currentBins = 56 } }, Json)
        };
        db.ReceiptInventoryOverrides.Add(revision);
        var debit = Leg(-56, 9005, 1); var credit = Leg(56, 9004, 2);
        db.RoomInventoryAdjustments.AddRange(debit, credit);
        db.AuditLogs.Add(new()
        {
            Action = revision.ActionType,
            EntityName = nameof(ReceiptInventoryOverride),
            EntityKey = revision.Id.ToString("D"),
            UserId = 8000,
            CreatedAt = at,
            SourceApplication = "CropQc.Web",
            BeforeValuesJson = revision.BeforeReceiptSnapshotJson,
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                revision.AfterReceiptSnapshotJson,
                revision.InventoryDelta,
                revision.CurrentInventoryBefore,
                revision.CurrentInventoryAfter,
                revision.Reason,
                ledgerAdjustments = new[] { debit, credit }.Select(x => new { x.WarehouseId, x.RoomId, x.ChangeAmount, x.NewBinCount })
            }, Json)
        });
        await db.SaveChangesAsync();
        return f;

        string Snapshot(int profileId, int version) => JsonSerializer.Serialize(new
        {
            id = 100000,
            cropYear = 2026,
            receivedAt = InventoryEvidenceCorpus.Start,
            warehouseId = 9001,
            roomId = 9002,
            fruitProfileId = profileId,
            growerLotId = 100000,
            lotCode = "1372",
            binCount = 56,
            isDeleted = false,
            concurrencyVersion = version
        }, Json);
        RoomInventoryAdjustment Leg(int quantity, int profileId, int sequence) => new()
        {
            ReceiptId = 100000,
            ReceiptInventoryOverrideId = revision.Id,
            CropYear = 2026,
            WarehouseId = 9001,
            RoomId = 9002,
            GrowerLotId = 100000,
            FruitProfileId = profileId,
            GrowerName = "Synthetic Top Pear",
            LotNumber = "1372",
            VarietyCode = quantity < 0 ? "ORGA" : "BART",
            InventoryStatus = quantity < 0 ? "Organic" : "Conventional",
            ChangeAmount = quantity,
            OldBinCount = quantity < 0 ? 56 : 0,
            NewBinCount = quantity < 0 ? 0 : 56,
            AdjustmentType = "ReceiptAdminOverride",
            InventoryInvariantVersion = 1,
            InventoryOperationKey = $"receipt-override:{revision.OperationKey}:{sequence}",
            Reason = revision.Reason,
            CreatedByUserId = 8000,
            CreatedAt = at,
            AdjustmentAt = at
        };
    }
}
