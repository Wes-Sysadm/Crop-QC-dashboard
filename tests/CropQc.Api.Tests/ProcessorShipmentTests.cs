using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class ProcessorShipmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-20T18:00:00Z");

    [Fact]
    public async Task Shipment_deducts_exact_inventory_once_with_real_parent_and_no_run_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var form = await fixture.FormAsync("shipment-one", 20, 62m, ProcessorPricingBases.PerTon);

        var result = await fixture.Service.CreateAsync(form, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var shipment = Assert.Single(await fixture.Db.ProcessorShipments.Include(x => x.Lines).ToListAsync());
        var line = Assert.Single(shipment.Lines);
        Assert.Equal(20, line.BinsSent);
        Assert.Equal(880m, line.PoundsPerBinSnapshot);
        var adjustment = Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.ProcessorShipmentLineId != null).ToListAsync());
        Assert.Equal(-20, adjustment.ChangeAmount);
        Assert.Equal(line.Id, adjustment.ProcessorShipmentLineId);
        Assert.Equal(80, (await fixture.Ledger.GetSnapshotsAsync(Fixture.WarehouseId, [Fixture.RoomId], CancellationToken.None)).Single().CurrentBins);
        Assert.Empty(await fixture.Db.ActualRuns.ToListAsync());
        Assert.Empty(await fixture.Db.ActualRunRevisions.ToListAsync());
        Assert.Empty(await fixture.Db.RunExpectations.ToListAsync());
        Assert.Empty(await fixture.Db.PackoutRuns.ToListAsync());
        var movement = Assert.Single(await fixture.Db.TreatmentLineageMovements.Where(x => x.ProcessorShipmentLineId != null).ToListAsync());
        Assert.Equal(line.Id, movement.ProcessorShipmentLineId);
        Assert.Equal(TreatmentLineageMovementTypes.ProcessorShipment, movement.MovementType);
    }

    [Fact]
    public async Task Double_submit_is_idempotent_and_does_not_double_deplete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var form = await fixture.FormAsync("same-operation", 10, 4.75m, ProcessorPricingBases.PerBin);
        var first = await fixture.Service.CreateAsync(form, CancellationToken.None);
        var second = await fixture.Service.CreateAsync(form, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(second.AlreadyApplied);
        Assert.Single(await fixture.Db.ProcessorShipments.ToListAsync());
        Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.ProcessorShipmentLineId != null).ToListAsync());
        Assert.Equal(90, (await fixture.Ledger.GetSnapshotsAsync(Fixture.WarehouseId, [Fixture.RoomId], CancellationToken.None)).Single().CurrentBins);
    }

    [Fact]
    public async Task Overdraw_and_stale_source_fail_before_shipment_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var overdraw = await fixture.FormAsync("overdraw", 101, 50m, ProcessorPricingBases.PerTon);
        var stale = await fixture.FormAsync("stale", 10, 50m, ProcessorPricingBases.PerTon);
        stale.Lines[0].ExpectedAvailableBins--;

        var overdrawResult = await fixture.Service.CreateAsync(overdraw, CancellationToken.None);
        var staleResult = await fixture.Service.CreateAsync(stale, CancellationToken.None);

        Assert.False(overdrawResult.Success);
        Assert.False(staleResult.Success);
        Assert.Empty(await fixture.Db.ProcessorShipments.ToListAsync());
    }

    [Fact]
    public async Task Inactive_processor_is_excluded_and_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var form = await fixture.FormAsync("inactive", 5, 50m, ProcessorPricingBases.PerTon);
        var processor = await fixture.Db.Processors.SingleAsync();
        processor.IsActive = false;
        await fixture.Db.SaveChangesAsync();

        var page = await fixture.Service.GetPageAsync(null, false, null, null, null, null, CancellationToken.None);
        var result = await fixture.Service.CreateAsync(form, CancellationToken.None);

        Assert.Empty(page.Processors);
        Assert.False(result.Success);
        Assert.Contains("active Processor", result.Error);
    }

    [Fact]
    public async Task Price_correction_preserves_original_and_changes_zero_physical_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("price-source", 10, 62m, ProcessorPricingBases.PerTon), CancellationToken.None);
        var adjustmentsBefore = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var movementsBefore = await fixture.Db.TreatmentLineageMovements.CountAsync();

        var error = await fixture.Service.CorrectPriceAsync(new ProcessorShipmentPriceCorrectionForm
        {
            ShipmentId = created.ShipmentId!.Value,
            OperationKey = "price-correction",
            SaleRate = 4.75m,
            PricingBasis = ProcessorPricingBases.PerBin,
            Reason = "Negotiated invoice basis corrected"
        }, CancellationToken.None);

        Assert.Null(error);
        var shipment = await fixture.Db.ProcessorShipments.SingleAsync();
        Assert.Equal(62m, shipment.OriginalSaleRate);
        Assert.Equal(ProcessorPricingBases.PerTon, shipment.OriginalPricingBasis);
        Assert.Equal(4.75m, shipment.SaleRate);
        Assert.Equal(ProcessorPricingBases.PerBin, shipment.PricingBasis);
        Assert.Single(await fixture.Db.ProcessorShipmentPriceCorrections.ToListAsync());
        Assert.Equal(adjustmentsBefore, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(movementsBefore, await fixture.Db.TreatmentLineageMovements.CountAsync());
    }

    [Fact]
    public async Task Physical_reversal_restores_exact_inventory_and_treatment_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("reverse-source", 15, 55m, ProcessorPricingBases.PerTon), CancellationToken.None);

        var error = await fixture.Service.ReverseAsync(new ProcessorShipmentReversalForm { ShipmentId = created.ShipmentId!.Value, OperationKey = "reverse-op", Reason = "Wrong physical disposition" }, CancellationToken.None);
        var second = await fixture.Service.ReverseAsync(new ProcessorShipmentReversalForm { ShipmentId = created.ShipmentId.Value, OperationKey = "reverse-op-2", Reason = "Again" }, CancellationToken.None);

        Assert.Null(error);
        Assert.Contains("already reversed", second);
        Assert.Equal(100, (await fixture.Ledger.GetSnapshotsAsync(Fixture.WarehouseId, [Fixture.RoomId], CancellationToken.None)).Single().CurrentBins);
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.ProcessorShipmentLineId != null));
        Assert.Equal(2, await fixture.Db.TreatmentLineageMovements.CountAsync(x => x.ProcessorShipmentLineId != null));
        Assert.NotNull((await fixture.Db.ProcessorShipments.SingleAsync()).ReversedAt);
    }

    [Fact]
    public void Processor_master_contains_no_price_or_rate_fields()
    {
        var names = typeof(Processor).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CurrentPrice", names);
        Assert.DoesNotContain("CurrentRate", names);
        Assert.DoesNotContain("DefaultPrice", names);
        Assert.DoesNotContain("DefaultRate", names);
        Assert.Equal([ProcessorPricingBases.PerTon, ProcessorPricingBases.PerBin], new[] { ProcessorPricingBases.PerTon, ProcessorPricingBases.PerBin });
    }

    [Fact]
    public void Built_in_roles_map_processor_shipments_narrowly()
    {
        Assert.Equal(PageAccessLevel.View, BuiltInRoleAccessDefaults.For(BuiltInRoleNames.Viewer)[ApplicationAreas.ProcessorShipments]);
        Assert.Equal(PageAccessLevel.View, BuiltInRoleAccessDefaults.For(BuiltInRoleNames.QcTech)[ApplicationAreas.ProcessorShipments]);
        Assert.Equal(PageAccessLevel.Admin, BuiltInRoleAccessDefaults.For(BuiltInRoleNames.Manager)[ApplicationAreas.ProcessorShipments]);
        Assert.Equal(PageAccessLevel.Admin, BuiltInRoleAccessDefaults.For(BuiltInRoleNames.Admin)[ApplicationAreas.ProcessorShipments]);
    }

    [Fact]
    public async Task Processor_master_lifecycle_is_audited_and_does_not_rewrite_shipment_snapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = new AdminManagementService(fixture.Db, new VarietyColorService(fixture.Db));
        var createdShipment = await fixture.Service.CreateAsync(
            await fixture.FormAsync("master-snapshot", 1, 62m, ProcessorPricingBases.PerTon), CancellationToken.None);
        Assert.True(createdShipment.Success);

        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "processors",
            Name = "Second Processor",
            Code = "SECOND",
            Description = "Disposable",
            IsActive = true
        }, ApplicationAreas.OwnerEmail, CancellationToken.None));
        var second = await fixture.Db.Processors.SingleAsync(x => x.Name == "Second Processor");
        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "processors",
            Id = second.Id,
            Name = "Second Processor Updated",
            Code = "SECOND",
            Description = "Updated",
            IsActive = true
        }, ApplicationAreas.OwnerEmail, CancellationToken.None));
        Assert.Null(await admin.DeactivateAsync("processors", second.Id, ApplicationAreas.OwnerEmail, CancellationToken.None));
        Assert.False((await fixture.Db.Processors.FindAsync(second.Id))!.IsActive);
        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "processors",
            Id = second.Id,
            Name = "Second Processor Updated",
            Code = "SECOND",
            Description = "Updated",
            IsActive = true
        }, ApplicationAreas.OwnerEmail, CancellationToken.None));
        Assert.True((await fixture.Db.Processors.FindAsync(second.Id))!.IsActive);

        var original = await fixture.Db.Processors.SingleAsync(x => x.Id == 9944);
        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "processors",
            Id = original.Id,
            Name = "ABC Processing Renamed",
            Code = original.Code ?? "",
            IsActive = true
        }, ApplicationAreas.OwnerEmail, CancellationToken.None));
        Assert.Equal("ABC Processing", (await fixture.Db.ProcessorShipments.FindAsync(createdShipment.ShipmentId))!.ProcessorNameSnapshot);
        var actions = await fixture.Db.AuditLogs.Where(x => x.EntityName == "Processor").Select(x => x.Action).ToListAsync();
        Assert.Contains("ProcessorCreated", actions);
        Assert.Contains("ProcessorUpdated", actions);
        Assert.Contains("ProcessorDeactivated", actions);
    }

    [Fact]
    public async Task Same_processor_supports_distinct_per_ton_and_per_bin_sales_with_durable_values()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(await fixture.FormAsync("distinct-ton", 10, 62m, ProcessorPricingBases.PerTon), CancellationToken.None);
        var second = await fixture.Service.CreateAsync(await fixture.FormAsync("distinct-bin", 5, 4.75m, ProcessorPricingBases.PerBin), CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        var shipments = await fixture.Db.ProcessorShipments.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(62m, shipments[0].SaleRate);
        Assert.Equal(ProcessorPricingBases.PerTon, shipments[0].PricingBasis);
        Assert.Equal(4.75m, shipments[1].SaleRate);
        Assert.Equal(ProcessorPricingBases.PerBin, shipments[1].PricingBasis);
        var detail = await fixture.Service.GetDetailsAsync(second.ShipmentId!.Value, CancellationToken.None);
        Assert.Equal(23.75m, detail!.EstimatedValue);
        Assert.Contains("reason", await fixture.Service.CorrectPriceAsync(new ProcessorShipmentPriceCorrectionForm
        {
            ShipmentId = second.ShipmentId.Value,
            OperationKey = "blank-reason",
            SaleRate = 5m,
            PricingBasis = ProcessorPricingBases.PerTon,
            Reason = " "
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Multi_line_per_ton_uses_each_authoritative_weight_and_exact_source_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var warehouse = await fixture.Db.Warehouses.SingleAsync(x => x.Id == Fixture.WarehouseId);
        var room = new Room { Id = 9950, Warehouse = warehouse, WarehouseId = warehouse.Id, Code = "PEAR-ROOM", Name = "Pear Room", CropQcRoomName = "Pear Room", CapacityBins = 500 };
        var fruit = new FruitProfile { Id = 9951, Name = "Processor Bartlett", VarietyCode = "PROC-BART", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true };
        var receipt = new Receipt { Id = 9952, CropYear = 2026, CompuTechReceiptId = "TR109501", ReceivedAt = Now.AddDays(-1), Warehouse = warehouse, WarehouseId = warehouse.Id, Room = room, RoomId = room.Id, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerNumber = "9392", GrowerName = "SECOND GROWER", LotCode = "9392", BinCount = 50, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
        fixture.Db.AddRange(room, fruit, receipt,
            new RoomInventoryAdjustment { Id = 9953, CropYear = 2026, Receipt = receipt, ReceiptId = receipt.Id, Warehouse = warehouse, WarehouseId = warehouse.Id, Room = room, RoomId = room.Id, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerName = receipt.GrowerName, LotNumber = receipt.LotCode, VarietyCode = fruit.VarietyCode, OldBinCount = 0, ChangeAmount = 50, NewBinCount = 50, AdjustmentType = "Receipt", InventoryStatus = "Packable", AdjustmentAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1) },
            new DashboardConfiguration { Key = RunProjectionSettings.PearPoundsPerBinKey, Value = "920", Description = "Test", ValueType = "Decimal", CreatedAt = Now });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var page = await fixture.Service.GetPageAsync(null, false, null, null, null, null, CancellationToken.None);
        Assert.Equal(2, page.Inventory.Count);
        var form = new ProcessorShipmentForm
        {
            OperationKey = "multi-weight",
            ProcessorId = 9944,
            SaleRate = 50m,
            PricingBasis = ProcessorPricingBases.PerTon,
            Currency = "USD",
            ShippedAt = DateTime.Parse("2026-08-20T10:00"),
            ConfirmedReview = true,
            Lines = page.Inventory.Select(x => new ProcessorShipmentLineForm { SourceKey = x.SourceKey, ExpectedAvailableBins = x.AvailableBins, BinsSent = 2 }).ToList()
        };
        var result = await fixture.Service.CreateAsync(form, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var detail = await fixture.Service.GetDetailsAsync(result.ShipmentId!.Value, CancellationToken.None);
        Assert.Equal(4, detail!.TotalBins);
        Assert.Equal(3600m, detail.EstimatedPounds);
        Assert.Equal(1.8m, detail.EstimatedTons);
        Assert.Equal(90m, detail.EstimatedValue);
        Assert.Equal(["9350", "9392"], detail.Lines.Select(x => x.GrowerNumber!).Order().ToArray());
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.ProcessorShipmentLineId != null));
    }

    [Fact]
    public async Task Missing_tonnage_conversion_does_not_block_physical_shipment()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.DashboardConfigurations.Remove(await fixture.Db.DashboardConfigurations.SingleAsync(x => x.Key == RunProjectionSettings.ApplePoundsPerBinKey));
        await fixture.Db.SaveChangesAsync();
        var form = await fixture.FormAsync("missing-weight", 3, 55m, ProcessorPricingBases.PerTon);

        var result = await fixture.Service.CreateAsync(form, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var detail = await fixture.Service.GetDetailsAsync(result.ShipmentId!.Value, CancellationToken.None);
        Assert.Null(detail!.EstimatedPounds);
        Assert.Null(detail.EstimatedTons);
        Assert.Null(detail.EstimatedValue);
    }

    [Fact]
    public void Processor_UI_is_reviewed_antiforgery_protected_and_has_only_allowed_pricing_choices()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "ProcessorShipmentsController.cs"));
        var index = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "ProcessorShipments", "Index.cshtml"));
        Assert.Contains("ValidateAntiForgeryToken", controller);
        Assert.Contains("ProcessorShipmentsAdmin", controller);
        Assert.Contains("ConfirmedReview", index);
        Assert.Contains("Per Ton", index);
        Assert.Contains("Per Bin", index);
        Assert.DoesNotContain("Per Pound", index);
        Assert.DoesNotContain("CWT", index);
        Assert.Contains("@ProcessorPricingBases.Suffix", index);
    }

    [Fact]
    public void Compatibility_package_is_bounded_and_never_updates_migration_history()
    {
        foreach (var file in new[] { "preflight-processor-shipments.sql", "apply-processor-shipments-schema.sql", "verify-processor-shipments.sql" })
        {
            var sql = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", file));
            Assert.DoesNotContain("__EFMigrationsHistory", sql, StringComparison.OrdinalIgnoreCase);
        }
        var apply = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "apply-processor-shipments-schema.sql"));
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("cropqc.test_force_processor_shipment_failure", apply);
    }

    [Fact]
    public void Application_gate_targets_processor_migration_and_all_new_objects()
    {
        Assert.Equal("20260821031442_AddProcessorShipments", DatabaseStartupDiagnostics.ExpectedSchemaMigration);
        var source = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs"));
        Assert.Contains("ProcessorShipmentLines.PoundsPerBinSnapshot", source);
        Assert.Contains("FK_TreatmentLineageMovements_ProcessorShipmentLines_ProcessorShipmentLineId", source);
    }

    [Fact]
    public async Task Restored_production_postgresql_processor_workflow_when_requested()
    {
        var connectionString = Environment.GetEnvironmentVariable("PROCESSOR_SHIPMENT_RESTORE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options);
        var actor = await db.Users.AsNoTracking().FirstAsync(x => x.IsActive && x.Email == ApplicationAreas.OwnerEmail);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, actor.Email)], "RestoreTest"));
        var accessor = new FixedHttpContextAccessor(new DefaultHttpContext { User = principal });
        var access = new UserAccessService(db, new ConfigurationBuilder().Build());
        var ledger = new RoomInventoryLedgerQueryService(db);
        var time = new PacificBusinessTimeService(new FixedClock(Now));
        var treatments = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
        var service = new ProcessorShipmentService(db, ledger, treatments, treatments,
            new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), access, accessor, time);

        var protectedBefore = new
        {
            Receipts = await db.Receipts.CountAsync(),
            ReceiptBins = await db.Receipts.SumAsync(x => (long)x.BinCount),
            ActualRuns = await db.ActualRuns.CountAsync(),
            ActualRevisions = await db.ActualRunRevisions.CountAsync(),
            BinsRuns = await db.BinsRunEntries.CountAsync(),
            Transfers = await db.RoomTransfers.CountAsync(),
            RunExpectations = await db.RunExpectations.CountAsync(),
            Packouts = await db.PackoutRuns.CountAsync()
        };
        var inventoryBefore = (await ledger.GetSnapshotsAsync(null, null, CancellationToken.None)).Sum(x => x.CurrentBins);
        var processor = new Processor { Name = $"DISPOSABLE RESTORE PROCESSOR {Guid.NewGuid():N}", Code = "DSP", IsActive = true, CreatedAt = Now, UpdatedAt = Now, CreatedByUserId = actor.Id, UpdatedByUserId = actor.Id };
        db.Processors.Add(processor);
        await db.SaveChangesAsync();

        var page = await service.GetPageAsync(null, false, null, null, null, null, CancellationToken.None);
        var sources = page.Inventory.Where(x => x.AvailableBins >= 2).GroupBy(x => new { x.RoomId, x.GrowerNumber, x.VarietyCode, x.TreatmentSignature }).Select(x => x.First()).Take(2).ToList();
        Assert.Equal(2, sources.Count);
        async Task<IReadOnlyList<ProcessorInventoryOptionViewModel>> RefreshSourcesAsync(
            IReadOnlyList<ProcessorInventoryOptionViewModel> selected)
        {
            var current = await service.GetPageAsync(null, false, null, null, null, null, CancellationToken.None);
            return selected.Select(expected => current.Inventory.Single(actual =>
                actual.RoomId == expected.RoomId &&
                actual.GrowerNumber == expected.GrowerNumber &&
                actual.LotNumber == expected.LotNumber &&
                actual.VarietyCode == expected.VarietyCode &&
                actual.ProductionType == expected.ProductionType &&
                actual.IsOrganic == expected.IsOrganic &&
                actual.InventoryStatus == expected.InventoryStatus &&
                actual.TreatmentSignature == expected.TreatmentSignature)).ToList();
        }
        async Task<long> CreateAndReverse(string key, string basis, decimal price, IReadOnlyList<ProcessorInventoryOptionViewModel> selected, bool provePriceCorrection = false)
        {
            selected = await RefreshSourcesAsync(selected);
            var form = new ProcessorShipmentForm
            {
                OperationKey = key,
                ProcessorId = processor.Id,
                SaleRate = price,
                PricingBasis = basis,
                Currency = "USD",
                ShippedAt = DateTime.Parse("2026-08-20T10:00"),
                ConfirmedReview = true,
                Lines = selected.Select(x => new ProcessorShipmentLineForm { SourceKey = x.SourceKey, ExpectedAvailableBins = x.AvailableBins, BinsSent = 1 }).ToList()
            };
            var result = await service.CreateAsync(form, CancellationToken.None);
            Assert.True(result.Success, result.Error);
            if (provePriceCorrection)
            {
                var adjustments = await db.RoomInventoryAdjustments.CountAsync();
                var movements = await db.TreatmentLineageMovements.CountAsync();
                Assert.Null(await service.CorrectPriceAsync(new ProcessorShipmentPriceCorrectionForm { ShipmentId = result.ShipmentId!.Value, OperationKey = $"{key}-price", SaleRate = price - 1m, PricingBasis = basis, Reason = "Disposable price proof" }, CancellationToken.None));
                Assert.Equal(adjustments, await db.RoomInventoryAdjustments.CountAsync());
                Assert.Equal(movements, await db.TreatmentLineageMovements.CountAsync());
            }
            Assert.Null(await service.ReverseAsync(new ProcessorShipmentReversalForm { ShipmentId = result.ShipmentId!.Value, OperationKey = $"{key}-reverse", Reason = "Disposable restored-copy proof" }, CancellationToken.None));
            return result.ShipmentId.Value;
        }

        var perTonId = await CreateAndReverse($"restore-per-ton-{Guid.NewGuid():N}", ProcessorPricingBases.PerTon, 62m, [sources[0]], true);
        var perBinId = await CreateAndReverse($"restore-per-bin-{Guid.NewGuid():N}", ProcessorPricingBases.PerBin, 4.75m, [sources[0]]);
        var multiId = await CreateAndReverse($"restore-multi-{Guid.NewGuid():N}", ProcessorPricingBases.PerTon, 55m, sources);
        var beforeCorrectionAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        Assert.Contains("reversed", await service.CorrectPriceAsync(new ProcessorShipmentPriceCorrectionForm { ShipmentId = perTonId, OperationKey = "cannot-correct-reversed", SaleRate = 60m, PricingBasis = ProcessorPricingBases.PerTon, Reason = "Proof" }, CancellationToken.None));
        Assert.Equal(beforeCorrectionAdjustments, await db.RoomInventoryAdjustments.CountAsync());

        var saved = await db.ProcessorShipments.AsNoTracking().Where(x => x.Id == perTonId || x.Id == perBinId || x.Id == multiId).Include(x => x.Lines).ToListAsync();
        Assert.Contains(saved, x => x.OriginalSaleRate == 62m && x.OriginalPricingBasis == ProcessorPricingBases.PerTon);
        Assert.Contains(saved, x => x.SaleRate == 4.75m && x.PricingBasis == ProcessorPricingBases.PerBin);
        Assert.Equal(2, saved.Single(x => x.Id == multiId).Lines.Count);
        Assert.All(saved, x => Assert.NotNull(x.ReversedAt));
        Assert.Equal(inventoryBefore, (await ledger.GetSnapshotsAsync(null, null, CancellationToken.None)).Sum(x => x.CurrentBins));
        Assert.Equal(protectedBefore.Receipts, await db.Receipts.CountAsync());
        Assert.Equal(protectedBefore.ReceiptBins, await db.Receipts.SumAsync(x => (long)x.BinCount));
        Assert.Equal(protectedBefore.ActualRuns, await db.ActualRuns.CountAsync());
        Assert.Equal(protectedBefore.ActualRevisions, await db.ActualRunRevisions.CountAsync());
        Assert.Equal(protectedBefore.BinsRuns, await db.BinsRunEntries.CountAsync());
        Assert.Equal(protectedBefore.Transfers, await db.RoomTransfers.CountAsync());
        Assert.Equal(protectedBefore.RunExpectations, await db.RunExpectations.CountAsync());
        Assert.Equal(protectedBefore.Packouts, await db.PackoutRuns.CountAsync());
    }

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
        public const int WarehouseId = 9940;
        public const int RoomId = 9941;
        private Fixture(CropQcDbContext db, ProcessorShipmentService service, IRoomInventoryLedgerQueryService ledger)
        {
            Db = db;
            Service = service;
            Ledger = ledger;
        }

        public CropQcDbContext Db { get; }
        public ProcessorShipmentService Service { get; }
        public IRoomInventoryLedgerQueryService Ledger { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase($"processor-shipment-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = WarehouseId, Code = "PROC-WH", Name = "Processor Test Warehouse" };
            var room = new Room { Id = RoomId, Warehouse = warehouse, WarehouseId = WarehouseId, Code = "PROC-ROOM", Name = "Processor Test Room", CropQcRoomName = "Processor Test Room", CapacityBins = 1000 };
            var fruit = new FruitProfile { Id = 9942, Name = "Processor Gala", VarietyCode = "PROC-GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var user = new User { Id = 9943, Email = ApplicationAreas.OwnerEmail, DisplayName = "Wes", Domain = "fruitandland.com", CreatedAt = Now };
            var processor = new Processor { Id = 9944, Name = "ABC Processing", Code = "ABC", IsActive = true, CreatedAt = Now, UpdatedAt = Now, CreatedByUserId = user.Id, UpdatedByUserId = user.Id };
            var receipt = new Receipt { Id = 9945, CropYear = 2026, CompuTechReceiptId = "TR109500", ReceivedAt = Now.AddDays(-1), Warehouse = warehouse, WarehouseId = WarehouseId, Room = room, RoomId = RoomId, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerNumber = "9350", GrowerName = "ROLOFF FARM-NAGLE CONV", LotCode = "9350", BinCount = 100, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
            db.AddRange(warehouse, room, fruit, user, processor, receipt);
            db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment { Id = 9946, CropYear = 2026, Receipt = receipt, ReceiptId = receipt.Id, Warehouse = warehouse, WarehouseId = WarehouseId, Room = room, RoomId = RoomId, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerName = receipt.GrowerName, LotNumber = receipt.LotCode, VarietyCode = fruit.VarietyCode, OldBinCount = 0, ChangeAmount = 100, NewBinCount = 100, AdjustmentType = "Receipt", InventoryStatus = "Packable", AdjustmentAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1) });
            var appleConfig = await db.DashboardConfigurations.SingleOrDefaultAsync(x => x.Key == RunProjectionSettings.ApplePoundsPerBinKey);
            if (appleConfig is null)
            {
                db.DashboardConfigurations.Add(new DashboardConfiguration { Key = RunProjectionSettings.ApplePoundsPerBinKey, Value = "880", Description = "Test", ValueType = "Decimal", CreatedAt = Now });
            }
            else appleConfig.Value = "880";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));
            var accessor = new FixedHttpContextAccessor(new DefaultHttpContext { User = principal });
            var access = new UserAccessService(db, new ConfigurationBuilder().Build());
            var ledger = new RoomInventoryLedgerQueryService(db);
            var time = new PacificBusinessTimeService(new FixedClock(Now));
            var treatments = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
            var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
            var service = new ProcessorShipmentService(db, ledger, treatments, treatments, invariant, access, accessor, time);
            return new Fixture(db, service, ledger);
        }

        public async Task<ProcessorShipmentForm> FormAsync(string operationKey, int bins, decimal rate, string basis)
        {
            var page = await Service.GetPageAsync(null, false, null, null, null, null, CancellationToken.None);
            var option = Assert.Single(page.Inventory);
            return new ProcessorShipmentForm { OperationKey = operationKey, ProcessorId = 9944, SaleRate = rate, PricingBasis = basis, Currency = "USD", ShippedAt = DateTime.Parse("2026-08-20T10:00"), ConfirmedReview = true, Lines = [new() { SourceKey = option.SourceKey, ExpectedAvailableBins = option.AvailableBins, BinsSent = bins }] };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = context; }
}
