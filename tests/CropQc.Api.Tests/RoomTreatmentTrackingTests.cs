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

public sealed class RoomTreatmentTrackingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-18T18:00:00Z");

    [Fact]
    public async Task Reviewed_ten_row_chemical_master_is_seeded_exactly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rows = await fixture.Db.TreatmentChemicals.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

        Assert.Equal(10, rows.Count);
        Assert.All(rows, x =>
        {
            Assert.True(x.IsActive);
            Assert.Null(x.CommonName);
            Assert.Equal(1.00m, x.Volume);
            Assert.Equal("BIN", x.Unit);
            Assert.Equal("USD", x.Currency);
        });
        Assert.Equal(new[]
        {
            (1, "eFOG-160 PYR FOGGING", "Apples", 5.25m),
            (2, "FOGGING EF 170,SB TBZ 99, EF80", "Apples", 5.67m),
            (3, "FOGGING EF 180, TBZ 99, EF 80", "Pears", 9.58m),
            (4, "eFOG-80 FDL FOGGING", "Pears", 5.25m),
            (5, "FOGGING EF 170, EF 160", "Apples", 5.67m),
            (6, "eFOG-180 FOGGING", "Pears", 4.95m),
            (7, "FOGGING EF 170, EF 80", "Apples", 5.67m),
            (8, "FOGGING EF 180, EF 160", "Pears", 9.27m),
            (9, "FOGGING EF 170, SB TBZ 99", "Apples", 5.25m),
            (10, "eFOG-170 DPA FOGGING", "Apples", 2.80m)
        }, rows.Select(x => (x.Id, x.ProductName, x.Crop, x.UnitPrice)).ToArray());
    }

    [Fact]
    public async Task Application_snapshots_exact_fruit_and_cost_without_inventory_quantity_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeAdjustments = await fixture.Db.RoomInventoryAdjustments.CountAsync();

        var result = await fixture.Service.ApplyAsync(fixture.ApplyForm("apply-exact", 1), default);

        Assert.Null(result.Error);
        var application = await fixture.Db.RoomTreatmentApplications.Include(x => x.Sources).SingleAsync();
        var source = Assert.Single(application.Sources);
        Assert.Equal(100, application.TotalBinsSnapshot);
        Assert.Equal(100, source.BinsTreated);
        Assert.Equal("9350", source.GrowerNumberSnapshot);
        Assert.Equal("GALA", source.VarietyCodeSnapshot);
        Assert.Equal(525m, application.EstimatedCostSnapshot);
        Assert.Equal(beforeAdjustments, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.Action == "ApplyTreatment").ToListAsync());
        var selection = Assert.Single(await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(), default));
        Assert.Equal(TreatmentLineageStates.Confirmed, selection.TreatmentState);
        Assert.Equal(100, selection.CurrentBins);
        Assert.Contains($"a:{application.Id}", selection.TreatmentSignature);
    }

    [Fact]
    public async Task Empty_mixed_crop_and_wrong_crop_applications_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Ledger.Current.Clear();
        fixture.Ledger.AsOf.Clear();
        Assert.Contains("empty room", (await fixture.Service.ApplyAsync(fixture.ApplyForm("empty", 1), default)).Error, StringComparison.OrdinalIgnoreCase);

        fixture.Ledger.Current.Add(fixture.AppleSnapshot(60));
        fixture.Ledger.Current.Add(fixture.PearSnapshot(40));
        fixture.Ledger.AsOf.AddRange(fixture.Ledger.Current);
        var mixed = await fixture.Service.GetApplyPageAsync(fixture.ApplyForm("mixed", 1), true, default);
        Assert.Contains("mixed or unresolved crops", mixed.Error);
        Assert.Empty(mixed.TreatmentOptions);

        fixture.Ledger.Current.RemoveAll(x => x.FruitType == "Pear");
        fixture.Ledger.AsOf.RemoveAll(x => x.FruitType == "Pear");
        Assert.Contains("not valid", (await fixture.Service.ApplyAsync(fixture.ApplyForm("wrong-crop", 3), default)).Error);
    }

    [Fact]
    public async Task Apple_and_pear_rooms_show_only_crop_appropriate_active_chemicals()
    {
        await using var fixture = await Fixture.CreateAsync();
        var apple = await fixture.Service.GetApplyPageAsync(fixture.ApplyForm("apple-options", 1), false, default);
        Assert.Equal(6, apple.TreatmentOptions.Count);
        Assert.All(apple.TreatmentOptions, x => Assert.Equal("Apples", x.Crop));

        fixture.Ledger.Current.Clear();
        fixture.Ledger.AsOf.Clear();
        fixture.Ledger.Current.Add(fixture.PearSnapshot(40));
        fixture.Ledger.AsOf.Add(fixture.PearSnapshot(40));
        var pear = await fixture.Service.GetApplyPageAsync(fixture.ApplyForm("pear-options", 3), false, default);
        Assert.Equal(4, pear.TreatmentOptions.Count);
        Assert.All(pear.TreatmentOptions, x => Assert.Equal("Pears", x.Crop));
    }

    [Fact]
    public async Task Backdated_application_uses_as_of_snapshot_and_refuses_later_treatment_ambiguity()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Ledger.AsOf.Clear();
        fixture.Ledger.AsOf.Add(fixture.AppleSnapshot(70));
        var first = await fixture.Service.ApplyAsync(fixture.ApplyForm("current-treatment", 1), default);
        Assert.Null(first.Error);

        var backdated = fixture.ApplyForm("backdated", 5);
        backdated.AppliedAt = Now.AddHours(-1);
        var rejected = await fixture.Service.ApplyAsync(backdated, default);

        Assert.Contains("cannot determine the exact room contents", rejected.Error);
        Assert.Single(await fixture.Db.RoomTreatmentApplications.ToListAsync());
    }

    [Fact]
    public async Task Deterministic_backdated_application_treats_only_as_of_bins_and_leaves_later_arrival_untreated()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Ledger.AsOf.Clear();
        fixture.Ledger.AsOf.Add(fixture.AppleSnapshot(70));
        fixture.Ledger.ReplaceCurrent(fixture.AppleSnapshot(100));
        var form = fixture.ApplyForm("deterministic-backdate", 1);
        form.AppliedAt = Now.AddHours(-1);

        var result = await fixture.Service.ApplyAsync(form, default);

        Assert.Null(result.Error);
        Assert.Equal(70, (await fixture.Db.RoomTreatmentApplications.SingleAsync()).TotalBinsSnapshot);
        var selections = await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(100), default);
        Assert.Contains(selections, x => x.TreatmentState == TreatmentLineageStates.Confirmed && x.CurrentBins == 70);
        Assert.Contains(selections, x => x.TreatmentState == TreatmentLineageStates.Untreated && x.CurrentBins == 30);
        Assert.Equal(100, selections.Sum(x => x.CurrentBins));
    }

    [Fact]
    public async Task Later_arrival_of_same_identity_remains_untreated()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null((await fixture.Service.ApplyAsync(fixture.ApplyForm("before-arrival", 1), default)).Error);
        fixture.Ledger.ReplaceCurrent(fixture.AppleSnapshot(125));

        var selections = await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(125), default);

        Assert.Equal(2, selections.Count);
        Assert.Contains(selections, x => x.TreatmentState == TreatmentLineageStates.Confirmed && x.CurrentBins == 100);
        Assert.Contains(selections, x => x.TreatmentState == TreatmentLineageStates.Untreated && x.CurrentBins == 25);
        Assert.Equal(125, selections.Sum(x => x.CurrentBins));
    }

    [Fact]
    public async Task Batch_projection_reconciles_duplicate_authoritative_selection_keys_exactly_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.AppleSnapshot(50) with { SourceReference = "Receipt A", LatestAdjustmentId = 101 };
        var second = fixture.AppleSnapshot(40) with { SourceReference = "Receipt B", LatestAdjustmentId = 102 };

        var projected = await fixture.Service.GetSelectionsAsync([first, second], default);

        var selection = Assert.Single(Assert.Single(projected).Value);
        Assert.Equal(90, selection.CurrentBins);
        Assert.Equal(TreatmentLineageStates.Untreated, selection.TreatmentState);
    }

    [Fact]
    public async Task Mixed_treatment_identity_requires_explicit_segment_and_partial_transfer_carries_exact_signature()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null((await fixture.Service.ApplyAsync(fixture.ApplyForm("mix-source", 1), default)).Error);
        fixture.Ledger.ReplaceCurrent(fixture.AppleSnapshot(120));
        var source = fixture.AppleSnapshot(120);
        var segments = await fixture.Service.GetSelectionsAsync(source, default);

        var ambiguous = await fixture.Service.MoveAsync(source, null, 10, Fixture.WarehouseId, Fixture.Room2Id,
            "ambiguous-transfer", TreatmentLineageMovementTypes.Transfer, 75, null, null, Now, Fixture.UserId, default);
        Assert.False(ambiguous.Success);
        Assert.Contains("multiple treatment histories", ambiguous.Error);

        var treated = segments.Single(x => x.TreatmentState == TreatmentLineageStates.Confirmed);
        var moved = await fixture.Service.MoveAsync(source, treated.TreatmentSignature, 30, Fixture.WarehouseId, Fixture.Room2Id,
            "exact-transfer", TreatmentLineageMovementTypes.Transfer, 76, null, null, Now, Fixture.UserId, default);
        Assert.True(moved.Success, moved.Error);
        var movement = await fixture.Db.TreatmentLineageMovements.SingleAsync();
        Assert.Equal(30, movement.BinCount);
        Assert.Equal(treated.TreatmentSignature, movement.TreatmentSignatureSnapshot);
        Assert.Equal(70, (await fixture.Service.GetSelectionsAsync(source, default)).Single(x => x.TreatmentSignature == treated.TreatmentSignature).CurrentBins);
        Assert.Equal(30, (await fixture.Service.GetSelectionsAsync(source with { RoomId = Fixture.Room2Id, CurrentBins = 30 }, default)).Single().CurrentBins);
    }

    [Fact]
    public async Task Cross_facility_transfer_preserves_exact_treated_segment_and_keeps_untreated_bins_distinct()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null((await fixture.Service.ApplyAsync(fixture.ApplyForm("cross-facility-treatment", 1), default)).Error);
        fixture.Ledger.ReplaceCurrent(fixture.AppleSnapshot(120));
        var source = fixture.AppleSnapshot(120);
        var treated = (await fixture.Service.GetSelectionsAsync(source, default))
            .Single(x => x.TreatmentState == TreatmentLineageStates.Confirmed);

        var moved = await fixture.Service.MoveAsync(
            source,
            treated.TreatmentSignature,
            25,
            Fixture.Warehouse2Id,
            Fixture.Room3Id,
            "cross-facility-lineage",
            TreatmentLineageMovementTypes.Transfer,
            80,
            null,
            null,
            Now,
            Fixture.UserId,
            default);

        Assert.True(moved.Success, moved.Error);
        var sourceSegments = await fixture.Service.GetSelectionsAsync(source with { CurrentBins = 95 }, default);
        Assert.Contains(sourceSegments, x => x.TreatmentState == TreatmentLineageStates.Confirmed && x.CurrentBins == 75);
        Assert.Contains(sourceSegments, x => x.TreatmentState == TreatmentLineageStates.Untreated && x.CurrentBins == 20);
        var destination = source with { WarehouseId = Fixture.Warehouse2Id, Facility = "MCD", RoomId = Fixture.Room3Id, Room = "MCD-03", CurrentBins = 25 };
        var destinationSegment = Assert.Single(await fixture.Service.GetSelectionsAsync(destination, default));
        Assert.Equal(TreatmentLineageStates.Confirmed, destinationSegment.TreatmentState);
        Assert.Equal(treated.TreatmentSignature, destinationSegment.TreatmentSignature);
        Assert.Equal(25, destinationSegment.CurrentBins);
    }

    [Fact]
    public async Task Movement_reversal_is_auditable_idempotent_and_does_not_overdraw_destination()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null((await fixture.Service.ApplyAsync(fixture.ApplyForm("move-reverse-app", 1), default)).Error);
        var signature = (await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(), default)).Single().TreatmentSignature;
        Assert.True((await fixture.Service.MoveAsync(fixture.AppleSnapshot(), signature, 30, Fixture.WarehouseId, Fixture.Room2Id,
            "move-reverse", TreatmentLineageMovementTypes.Transfer, 77, null, null, Now, Fixture.UserId, default)).Success);

        var reversed = await fixture.Service.ReverseMovementsAsync("move-reversal", TreatmentLineageMovementTypes.TransferReversal,
            77, null, null, Now, Fixture.UserId, default);
        var repeated = await fixture.Service.ReverseMovementsAsync("move-reversal", TreatmentLineageMovementTypes.TransferReversal,
            77, null, null, Now, Fixture.UserId, default);

        Assert.True(reversed.Success, reversed.Error);
        Assert.True(repeated.Success, repeated.Error);
        Assert.Equal(2, await fixture.Db.TreatmentLineageMovements.CountAsync());
        Assert.Equal(100, (await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(), default)).Single().CurrentBins);
    }

    [Fact]
    public async Task Multiple_treatments_preserve_order_and_master_edits_do_not_rewrite_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null((await fixture.Service.ApplyAsync(fixture.ApplyForm("treatment-one", 1), default)).Error);
        Assert.Null((await fixture.Service.ApplyAsync(fixture.ApplyForm("treatment-two", 5), default)).Error);
        var applications = await fixture.Db.RoomTreatmentApplications.OrderBy(x => x.Id).ToListAsync();
        var selection = Assert.Single(await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(), default));
        Assert.Equal($"u|a:{applications[0].Id},{applications[1].Id}", selection.TreatmentSignature);

        var chemical = await fixture.Db.TreatmentChemicals.FindAsync(1);
        chemical!.CommonName = "Reviewed Common Name";
        chemical.UnitPrice = 99m;
        chemical.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var historical = await fixture.Db.RoomTreatmentApplications.SingleAsync(x => x.Id == applications[0].Id);
        Assert.Null(historical.CommonNameSnapshot);
        Assert.Equal(5.25m, historical.UnitPriceSnapshot);
        Assert.Equal(525m, historical.EstimatedCostSnapshot);
    }

    [Fact]
    public async Task Application_reversal_after_transfer_updates_current_lineage_without_quantity_change_or_history_delete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.Service.ApplyAsync(fixture.ApplyForm("reverse-after-transfer", 1), default);
        var signature = (await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(), default)).Single().TreatmentSignature;
        Assert.True((await fixture.Service.MoveAsync(fixture.AppleSnapshot(), signature, 30, Fixture.WarehouseId, Fixture.Room2Id,
            "transfer-before-app-reversal", TreatmentLineageMovementTypes.Transfer, 78, null, null, Now, Fixture.UserId, default)).Success);
        fixture.Ledger.Current.Clear();
        fixture.Ledger.Current.Add(fixture.AppleSnapshot(70));
        fixture.Ledger.Current.Add(fixture.AppleSnapshot(30) with { RoomId = Fixture.Room2Id });

        var error = await fixture.Service.ReverseAsync(new ReverseRoomTreatmentApplicationForm { Id = applied.ApplicationId!.Value, Reason = "Wrong chemical selected" }, default);
        var repeated = await fixture.Service.ReverseAsync(new ReverseRoomTreatmentApplicationForm { Id = applied.ApplicationId.Value, Reason = "Repeat" }, default);

        Assert.Null(error);
        Assert.Contains("already reversed", repeated);
        Assert.Equal(TreatmentLineageStates.Untreated, (await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(70), default)).Single().TreatmentState);
        Assert.Equal(TreatmentLineageStates.Untreated, (await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(30) with { RoomId = Fixture.Room2Id }, default)).Single().TreatmentState);
        Assert.Equal(100, fixture.Ledger.Current.Sum(x => x.CurrentBins));
        Assert.NotNull((await fixture.Db.RoomTreatmentApplications.SingleAsync()).ReversedAt);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "ReverseTreatment");
    }

    [Fact]
    public async Task Unknown_true_up_lineage_and_loss_or_run_depletion_never_inherit_room_treatment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var unknown = await fixture.Service.AddUnknownAsync(fixture.AppleSnapshot(5), 5, "true-up-unknown", Now, Fixture.UserId, default);
        Assert.True(unknown.Success, unknown.Error);
        var selection = Assert.Single(await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(5), default));
        Assert.Equal(TreatmentLineageStates.Unknown, selection.TreatmentState);

        var depleted = await fixture.Service.MoveAsync(fixture.AppleSnapshot(5), selection.TreatmentSignature, 2, null, null,
            "loss-exact", TreatmentLineageMovementTypes.InventoryLoss, null, 79, null, Now, Fixture.UserId, default);
        Assert.True(depleted.Success, depleted.Error);
        Assert.Equal(3, (await fixture.Service.GetSelectionsAsync(fixture.AppleSnapshot(3), default)).Single().CurrentBins);
        Assert.Equal(TreatmentLineageStates.Unknown, (await fixture.Db.TreatmentLineageMovements.SingleAsync(x => x.MovementType == TreatmentLineageMovementTypes.InventoryLoss)).TreatmentStateSnapshot);
    }

    [Fact]
    public async Task Permissions_and_confirmation_are_enforced_in_service_and_controller_contract()
    {
        await using var fixture = await Fixture.CreateAsync(PageAccessLevel.View);
        Assert.Contains("Edit access", (await fixture.Service.ApplyAsync(fixture.ApplyForm("unauthorized", 1), default)).Error);

        fixture.Access.Level = PageAccessLevel.Edit;
        var unconfirmed = fixture.ApplyForm("unconfirmed", 1);
        unconfirmed.ConfirmedReview = false;
        Assert.Contains("Review the treatment", (await fixture.Service.ApplyAsync(unconfirmed, default)).Error);
        Assert.Contains("Admin access", await fixture.Service.ReverseAsync(new ReverseRoomTreatmentApplicationForm { Id = 1, Reason = "No admin" }, default));

        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "RoomTreatmentsController.cs"));
        Assert.Contains("AccessPolicyNames.RoomTransactionsEdit", controller);
        Assert.Contains("AccessPolicyNames.RoomTransactionsAdmin", controller);
        Assert.Equal(5, controller.Split("[ValidateAntiForgeryToken]", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Room_and_run_presentation_exposes_lineage_without_changing_calculation_paths()
    {
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var rooms = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Rooms.cshtml"));
        var run = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "ActualRunDetail.cshtml"));
        var apply = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RoomTreatments", "Apply.cshtml"));
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));
        Assert.Contains("Apply Treatment", rooms);
        Assert.Contains("Current Treatment Status", room);
        Assert.Contains("Treatment Application History", room);
        Assert.Contains("<th>Treatment</th>", run, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exact fruit affected", apply);
        Assert.Contains("Estimated Treatment Cost", apply);
        Assert.Contains("max-width: 430px", css);

        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "RoomTreatmentService.cs"));
        Assert.DoesNotContain("RoomInventoryAdjustments.Add", service);
        Assert.Contains("ProjectSelectionsBatchAsync", service);
        Assert.Contains("Take(200)", service);
    }

    [Fact]
    public void Migration_and_compatibility_package_are_additive_bounded_and_do_not_forge_history()
    {
        var migration = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Migrations", "20260818181556_AddRoomTreatmentTracking.cs"));
        var apply = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "apply-room-treatment-tracking-schema.sql"));
        var preflight = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "preflight-room-treatment-tracking.sql"));
        var verifier = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "verify-room-treatment-tracking.sql"));
        var gate = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs"));
        Assert.Contains("AddRoomTreatmentTracking", migration);
        Assert.DoesNotContain("migrationBuilder.Sql(\"DELETE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STATE A", preflight);
        Assert.Contains("STATE B", preflight);
        Assert.Contains("STATE C", preflight);
        Assert.Contains("BEGIN;", apply);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("migration_history_intentionally_unchanged", verifier);
        Assert.DoesNotContain("__EFMigrationsHistory", apply);
        Assert.Contains("20260819142656_AddTreatmentReportAttachments", gate);
        Assert.Equal(502, gate.Split('\n').Count(x => x.TrimStart().StartsWith("new(", StringComparison.Ordinal)));
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
        public const int UserId = 9901;
        public const int WarehouseId = 9902;
        public const int RoomId = 9903;
        public const int Room2Id = 9904;
        public const int AppleProfileId = 9905;
        public const int PearProfileId = 9906;
        public const int Warehouse2Id = 9907;
        public const int Room3Id = 9908;
        private Fixture(CropQcDbContext db, FakeLedger ledger, MutableAccess access, RoomTreatmentService service)
        {
            Db = db;
            Ledger = ledger;
            Access = access;
            Service = service;
        }

        public CropQcDbContext Db { get; }
        public FakeLedger Ledger { get; }
        public MutableAccess Access { get; }
        public RoomTreatmentService Service { get; }

        public static async Task<Fixture> CreateAsync(PageAccessLevel level = PageAccessLevel.Admin)
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"room-treatment-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = WarehouseId, Code = "EBS-T", Name = "Treatment Test" };
            var room = new Room { Id = RoomId, WarehouseId = WarehouseId, Warehouse = warehouse, Code = "EVANS-T1", Name = "Evans Treatment 1" };
            var room2 = new Room { Id = Room2Id, WarehouseId = WarehouseId, Warehouse = warehouse, Code = "EVANS-T2", Name = "Evans Treatment 2" };
            var warehouse2 = new Warehouse { Id = Warehouse2Id, Code = "McDougall", Name = "McDougall" };
            var room3 = new Room { Id = Room3Id, WarehouseId = Warehouse2Id, Warehouse = warehouse2, Code = "MCD-03", Name = "MCD 03" };
            var apple = new FruitProfile { Id = AppleProfileId, Name = "Treatment Gala", VarietyCode = "TRT-GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var pear = new FruitProfile { Id = PearProfileId, Name = "Treatment Bartlett", VarietyCode = "TRT-BART", FruitType = "Pear", ProductionType = "Conventional" };
            var user = new User { Id = UserId, Email = ApplicationAreas.OwnerEmail, DisplayName = "Wes", Domain = "fruitandland.com", CreatedAt = Now };
            db.AddRange(warehouse, warehouse2, room, room2, room3, apple, pear, user);
            db.RoomTransfers.AddRange(
                Transfer(75, 10),
                Transfer(76, 30),
                Transfer(77, 30),
                Transfer(78, 30),
                Transfer(80, 25, Warehouse2Id, Room3Id));
            db.RoomInventoryLosses.Add(new RoomInventoryLoss
            {
                Id = 79,
                OperationKey = "treatment-parent-loss",
                WarehouseId = WarehouseId,
                RoomId = RoomId,
                CropYear = 2026,
                FruitProfileId = AppleProfileId,
                GrowerName = "ROLOFF FARM-NAGLE CONV",
                GrowerNumber = "9350",
                LotNumber = "9350",
                VarietyCode = "GALA",
                LossType = RoomInventoryLossTypes.Dropped,
                BinCount = 2,
                Reason = "Treatment lineage parent fixture",
                CreatedByUserId = UserId,
                CreatedAt = Now
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var ledger = new FakeLedger();
            var appleSnapshot = Snapshot(100, RoomId, AppleProfileId, "Apple", "GALA", "Gala");
            ledger.Current.Add(appleSnapshot);
            ledger.AsOf.Add(appleSnapshot);
            var access = new MutableAccess { Level = level };
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"))
            };
            var service = new RoomTreatmentService(db, ledger, access, new FixedHttpContextAccessor(context),
                new PacificBusinessTimeService(new FixedClock(Now)), NullLogger<RoomTreatmentService>.Instance);
            return new Fixture(db, ledger, access, service);

            RoomTransfer Transfer(long id, int bins, int destinationWarehouseId = WarehouseId, int destinationRoomId = Room2Id) => new()
            {
                Id = id,
                OperationKey = $"treatment-parent-transfer-{id}",
                SourceWarehouseId = WarehouseId,
                SourceRoomId = RoomId,
                DestinationWarehouseId = destinationWarehouseId,
                DestinationRoomId = destinationRoomId,
                CropYear = 2026,
                FruitProfileId = AppleProfileId,
                GrowerName = "ROLOFF FARM-NAGLE CONV",
                LotNumber = "9350",
                VarietyCode = "GALA",
                BinCount = bins,
                Reason = "Treatment lineage parent fixture",
                TransferredAt = Now,
                CreatedByUserId = UserId,
                CreatedAt = Now
            };
        }

        public RoomInventoryLedgerSnapshot AppleSnapshot(int bins = 100) => Snapshot(bins, RoomId, AppleProfileId, "Apple", "GALA", "Gala");
        public RoomInventoryLedgerSnapshot PearSnapshot(int bins = 40) => Snapshot(bins, RoomId, PearProfileId, "Pear", "BART", "Bartlett");
        public RoomTreatmentApplyForm ApplyForm(string key, int chemicalId) => new()
        {
            RoomId = RoomId,
            TreatmentChemicalId = chemicalId,
            AppliedAt = Now,
            OperationKey = key,
            ConfirmedReview = true
        };

        private static RoomInventoryLedgerSnapshot Snapshot(int bins, int roomId, int fruitProfileId, string fruitType, string variety, string varietyName) => new(
            WarehouseId, "EBS", roomId, roomId == RoomId ? "EVANS-T1" : "EVANS-T2", "Evans", 2026, null,
            fruitProfileId, "ROLOFF FARM-NAGLE CONV", "9350", "9350", null, variety, variety, varietyName,
            fruitType, "Conventional", false, "Conventional", bins, 0, 0, 0, 0, 0, 0, 0, 0,
            bins, 1, Now.AddDays(-1), Now.AddDays(-1), 1);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }
    }

    private sealed class FakeLedger : IRoomInventoryLedgerQueryService
    {
        public List<RoomInventoryLedgerSnapshot> Current { get; } = [];
        public List<RoomInventoryLedgerSnapshot> AsOf { get; } = [];
        public void ReplaceCurrent(RoomInventoryLedgerSnapshot snapshot)
        {
            Current.Clear();
            Current.Add(snapshot);
        }
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, CancellationToken cancellationToken) =>
            Task.FromResult(Filter(Current, warehouseId, roomIds, null));
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(Filter(Current, warehouseId, roomIds, fruitProfileId));
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsOfAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            Task.FromResult(Filter(AsOf, warehouseId, roomIds, null));
        private static IReadOnlyList<RoomInventoryLedgerSnapshot> Filter(IEnumerable<RoomInventoryLedgerSnapshot> source, int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId) =>
            source.Where(x => warehouseId is null || x.WarehouseId == warehouseId)
                .Where(x => roomIds is null || roomIds.Contains(x.RoomId))
                .Where(x => fruitProfileId is null || x.FruitProfileId == fruitProfileId)
                .ToList();
    }

    private sealed class MutableAccess : IUserAccessService
    {
        public PageAccessLevel Level { get; set; }
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(Level >= minimumLevel);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(Level);
        public void InvalidateAll() { }
    }

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow => utcNow; }
}
