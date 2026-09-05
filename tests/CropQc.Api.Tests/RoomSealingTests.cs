using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class RoomSealingTests
{
    [Theory]
    [InlineData(BuiltInRoleNames.Manager)]
    [InlineData(BuiltInRoleNames.Admin)]
    public async Task Manager_or_admin_can_seal_and_unseal_with_immutable_history_and_audit(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = Principal(role);
        var baselineRooms = await fixture.Db.Rooms.CountAsync();
        var baselineAdjustments = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var baselineBins = await fixture.Db.Receipts.SumAsync(x => x.BinCount);

        var confirmation = await fixture.Service.GetConfirmationAsync(Fixture.RoomId, principal, default);
        Assert.NotNull(confirmation);
        Assert.False(confirmation!.IsSealed);
        Assert.Null(await fixture.Service.ChangeStateAsync(
            fixture.Form(false, note: "Closed for controlled atmosphere"),
            true,
            principal,
            default));

        fixture.Db.ChangeTracker.Clear();
        var room = await fixture.Db.Rooms.Include(x => x.SealedByUser).SingleAsync();
        Assert.True(room.IsSealed);
        Assert.NotNull(room.SealedAt);
        Assert.Equal(Fixture.UserId, room.SealedByUserId);
        var sealedEvent = Assert.Single(await fixture.Db.RoomSealEvents.ToListAsync());
        Assert.Equal(RoomSealActions.Seal, sealedEvent.Action);
        Assert.Equal("Closed for controlled atmosphere", sealedEvent.Note);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "RoomSealed" && x.EntityKey == Fixture.RoomId.ToString());

        Assert.Null(await fixture.Service.ChangeStateAsync(
            fixture.Form(true, room.SealedAt, "Room released"),
            false,
            principal,
            default));
        fixture.Db.ChangeTracker.Clear();
        room = await fixture.Db.Rooms.SingleAsync();
        Assert.False(room.IsSealed);
        Assert.Null(room.SealedAt);
        Assert.Null(room.SealedByUserId);
        var history = await fixture.Db.RoomSealEvents.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal([RoomSealActions.Seal, RoomSealActions.Unseal], history.Select(x => x.Action));
        Assert.Equal(baselineRooms, await fixture.Db.Rooms.CountAsync());
        Assert.Equal(baselineAdjustments, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(baselineBins, await fixture.Db.Receipts.SumAsync(x => x.BinCount));
    }

    [Theory]
    [InlineData(BuiltInRoleNames.Viewer)]
    [InlineData(BuiltInRoleNames.QcTech)]
    [InlineData(BuiltInRoleNames.QcAdmin)]
    public async Task Non_manager_roles_fail_closed(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ChangeStateAsync(
            fixture.Form(false),
            true,
            Principal(role),
            default));
        Assert.False((await fixture.Db.Rooms.SingleAsync()).IsSealed);
        Assert.Empty(await fixture.Db.RoomSealEvents.ToListAsync());
    }

    [Fact]
    public async Task Repeated_and_stale_commands_write_nothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = Principal(BuiltInRoleNames.Manager);
        Assert.Null(await fixture.Service.ChangeStateAsync(fixture.Form(false), true, principal, default));
        var effectiveAt = (await fixture.Db.Rooms.AsNoTracking().SingleAsync()).SealedAt;
        var events = await fixture.Db.RoomSealEvents.CountAsync();
        var audits = await fixture.Db.AuditLogs.CountAsync();

        Assert.Null(await fixture.Service.ChangeStateAsync(fixture.Form(true, effectiveAt), true, principal, default));
        Assert.Contains("changed", await fixture.Service.ChangeStateAsync(fixture.Form(false), false, principal, default));
        Assert.Equal(events, await fixture.Db.RoomSealEvents.CountAsync());
        Assert.Equal(audits, await fixture.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Confirmation_defaults_to_current_Pacific_business_date_and_time()
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = await fixture.Service.GetConfirmationAsync(Fixture.RoomId, Principal(BuiltInRoleNames.Manager), default);

        Assert.NotNull(page);
        Assert.Equal(new DateOnly(2026, 8, 22), page!.Form.EffectiveDate);
        Assert.Equal(new TimeOnly(13, 0), page.Form.EffectiveTime);
        Assert.False(page.HasActiveSeal);
        Assert.False(page.IsSealScheduled);
        Assert.False(page.IsSealed);
    }

    [Fact]
    public async Task Future_seal_is_scheduled_allows_movement_before_effective_time_and_blocks_at_boundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = Principal(BuiltInRoleNames.Manager);
        var adjustments = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var form = fixture.Form(false, date: new DateOnly(2026, 8, 22), time: new TimeOnly(14, 0));

        Assert.Null(await fixture.Service.ChangeStateAsync(form, true, principal, default));
        fixture.Db.ChangeTracker.Clear();
        var room = await fixture.Db.Rooms.AsNoTracking().SingleAsync();
        Assert.True(room.IsSealed);
        Assert.Equal(DateTimeOffset.Parse("2026-08-22T21:00:00Z"), room.SealedAt);
        Assert.Equal(Fixture.NowUtc, room.SealRecordedAt);
        Assert.Equal(RoomSealActions.SealScheduled, (await fixture.Db.RoomSealEvents.SingleAsync()).Action);

        var before = new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-22T20:59:59Z")));
        var boundary = new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-22T21:00:00Z")));
        Assert.Null(await RoomMovementSealGuard.ValidateAsync(fixture.Db, [Fixture.RoomId], [], before, default));
        Assert.Contains("sealed", await RoomMovementSealGuard.ValidateAsync(fixture.Db, [Fixture.RoomId], [], boundary, default), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sealed", await RoomMovementSealGuard.ValidateAsync(fixture.Db, [], [Fixture.RoomId], boundary, default), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(adjustments, await fixture.Db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task Scheduled_seal_can_be_edited_and_canceled_with_immutable_effective_time_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = Principal(BuiltInRoleNames.Admin);
        var original = DateTimeOffset.Parse("2026-08-22T22:00:00Z");
        var revised = DateTimeOffset.Parse("2026-08-22T23:00:00Z");
        Assert.Null(await fixture.Service.ChangeStateAsync(
            fixture.Form(false, date: new DateOnly(2026, 8, 22), time: new TimeOnly(15, 0)), true, principal, default));
        Assert.Null(await fixture.Service.ChangeStateAsync(
            fixture.Form(true, original, "Schedule moved", new DateOnly(2026, 8, 22), new TimeOnly(16, 0)), true, principal, default));
        Assert.Null(await fixture.Service.ChangeStateAsync(
            fixture.Form(true, revised, "Schedule canceled"), false, principal, default));

        fixture.Db.ChangeTracker.Clear();
        var room = await fixture.Db.Rooms.AsNoTracking().SingleAsync();
        Assert.False(room.IsSealed);
        Assert.Null(room.SealedAt);
        Assert.Null(room.SealRecordedAt);
        var history = await fixture.Db.RoomSealEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal([RoomSealActions.SealScheduled, RoomSealActions.ScheduleChanged, RoomSealActions.ScheduleCanceled], history.Select(x => x.Action));
        Assert.Equal(original, history[1].PreviousEffectiveAt);
        Assert.Equal(revised, history[1].EffectiveAt);
        Assert.Equal(revised, history[2].PreviousEffectiveAt);
        Assert.Equal(revised, history[2].EffectiveAt);
        Assert.All(history, x => Assert.Equal(Fixture.NowUtc, x.ChangedAt));
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "RoomSealScheduleChanged");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "RoomSealScheduleCanceled");
    }

    [Fact]
    public async Task Backdated_seal_is_immediately_active_and_backdated_unseal_is_allowed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = Principal(BuiltInRoleNames.Manager);
        Assert.Null(await fixture.Service.ChangeStateAsync(
            fixture.Form(false, date: new DateOnly(2026, 8, 21), time: new TimeOnly(10, 0)), true, principal, default));
        var room = await fixture.Db.Rooms.AsNoTracking().SingleAsync();
        Assert.Equal(DateTimeOffset.Parse("2026-08-21T17:00:00Z"), room.SealedAt);
        Assert.Contains("sealed", await RoomMovementSealGuard.ValidateAsync(fixture.Db, [Fixture.RoomId], [], fixture.BusinessTime, default), StringComparison.OrdinalIgnoreCase);

        Assert.Null(await fixture.Service.ChangeStateAsync(
            fixture.Form(true, room.SealedAt, "Backdated release", new DateOnly(2026, 8, 22), new TimeOnly(12, 0)), false, principal, default));
        var events = await fixture.Db.RoomSealEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(RoomSealActions.Seal, events[0].Action);
        Assert.Equal(RoomSealActions.Unseal, events[1].Action);
        Assert.Equal(DateTimeOffset.Parse("2026-08-22T19:00:00Z"), events[1].EffectiveAt);
        Assert.Equal(Fixture.NowUtc, events[1].ChangedAt);
    }

    [Fact]
    public async Task Active_seal_cannot_be_edited_and_future_unseal_fails_with_zero_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = Principal(BuiltInRoleNames.Manager);
        Assert.Null(await fixture.Service.ChangeStateAsync(fixture.Form(false), true, principal, default));
        var room = await fixture.Db.Rooms.AsNoTracking().SingleAsync();
        var events = await fixture.Db.RoomSealEvents.CountAsync();
        var audits = await fixture.Db.AuditLogs.CountAsync();

        Assert.Contains("already actively sealed", await fixture.Service.ChangeStateAsync(
            fixture.Form(true, room.SealedAt, date: new DateOnly(2026, 8, 22), time: new TimeOnly(14, 0)), true, principal, default));
        Assert.Contains("future Unseal", await fixture.Service.ChangeStateAsync(
            fixture.Form(true, room.SealedAt, date: new DateOnly(2026, 8, 22), time: new TimeOnly(14, 0)), false, principal, default));
        Assert.Equal(events, await fixture.Db.RoomSealEvents.CountAsync());
        Assert.Equal(audits, await fixture.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Seal_and_unseal_require_explicit_date_and_time_and_reject_invalid_DST_time()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = Principal(BuiltInRoleNames.Manager);
        Assert.Contains("required", await fixture.Service.ChangeStateAsync(new RoomSealForm
        {
            RoomId = Fixture.RoomId,
            ExpectedIsSealed = false
        }, true, principal, default));
        Assert.Contains("daylight-saving", await fixture.Service.ChangeStateAsync(fixture.Form(
            false,
            date: new DateOnly(2026, 3, 8),
            time: new TimeOnly(2, 30)), true, principal, default));
        Assert.Empty(await fixture.Db.RoomSealEvents.ToListAsync());
    }

    [Fact]
    public void Room_cards_and_detail_render_all_three_states_and_separate_recorded_metadata()
    {
        var rooms = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Rooms.cshtml"));
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));
        var detail = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var confirmation = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RoomSealing", "Confirm.cshtml"));
        foreach (var source in new[] { rooms, dashboard })
        {
            Assert.Contains("UNSEALED", source);
            Assert.Contains("SCHEDULED TO SEAL", source);
            Assert.Contains("SEALED", source);
        }
        Assert.Contains("Scheduled At", detail);
        Assert.Contains("Previous Effective", detail);
        Assert.Contains("Recorded", detail);
        Assert.Contains("Edit Scheduled Seal", confirmation);
        Assert.Contains("Cancel Scheduled Seal", confirmation);
        Assert.Contains("Cancel Scheduled Seal", rooms);
        Assert.Contains("EffectiveDate", confirmation);
        Assert.Contains("EffectiveTime", confirmation);
    }

    [Fact]
    public async Task Movement_guard_blocks_sealed_source_and_destination_but_allows_open_rooms()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null(await RoomMovementSealGuard.ValidateAsync(fixture.Db, [Fixture.RoomId], [], default));
        var room = await fixture.Db.Rooms.SingleAsync();
        room.IsSealed = true;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var source = await RoomMovementSealGuard.ValidateAsync(fixture.Db, [Fixture.RoomId], [], default);
        var destination = await RoomMovementSealGuard.ValidateAsync(fixture.Db, [], [Fixture.RoomId], default);
        Assert.Contains("moved out", source);
        Assert.Contains("moved in", destination);
    }

    [Fact]
    public void Controller_is_exact_role_restricted_confirmed_and_antiforgery_protected()
    {
        var authorize = Assert.Single(typeof(RoomSealingController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal($"{BuiltInRoleNames.Manager},{BuiltInRoleNames.Admin}", authorize.Roles);
        var post = typeof(RoomSealingController).GetMethod(nameof(RoomSealingController.Change))!;
        Assert.NotNull(post.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
        Assert.Contains("Confirm", File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RoomSealing", "Confirm.cshtml")));
    }

    [Fact]
    public void All_required_physical_movement_paths_use_the_transaction_guard()
    {
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var binsRun = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BinsRunService.cs"));
        var processor = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "ProcessorShipmentService.cs"));
        Assert.True(Count(dashboard, "RoomMovementSealGuard.ValidateAsync") >= 5);
        Assert.True(Count(binsRun, "RoomMovementSealGuard.ValidateAsync") >= 2);
        Assert.True(Count(processor, "RoomMovementSealGuard.ValidateAsync") >= 2);
    }

    [Fact]
    public void Administrative_corrections_and_treatments_are_not_seal_blocked()
    {
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var trueUp = Slice(dashboard, "CreateRoomInventoryTrueUpAsync", "CreateRoomTransferAsync");
        var treatment = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "RoomTreatmentService.cs"));
        var losses = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "RoomInventoryLossService.cs"));
        var overrides = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "ReceiptInventoryOverrideService.cs"));
        Assert.DoesNotContain("RoomMovementSealGuard", trueUp);
        Assert.DoesNotContain("RoomMovementSealGuard", treatment);
        Assert.DoesNotContain("RoomMovementSealGuard", losses);
        Assert.DoesNotContain("RoomMovementSealGuard", overrides);
    }

    [Fact]
    public void Compatibility_package_is_bounded_repeatable_and_never_updates_migration_history()
    {
        foreach (var file in new[] { "preflight-room-seal-effective-time.sql", "apply-room-seal-effective-time-schema.sql", "verify-room-seal-effective-time.sql" })
        {
            var sql = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", file));
            Assert.DoesNotContain("__EFMigrationsHistory", sql, StringComparison.OrdinalIgnoreCase);
        }
        var apply = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "apply-room-seal-effective-time-schema.sql"));
        var preflight = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "preflight-room-seal-effective-time.sql"));
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("cropqc.test_force_room_seal_effective_time_failure", apply);
        Assert.Contains("State C", preflight);
        Assert.Contains("state_a_absent", preflight);
        Assert.Contains("state_b_complete_exact", preflight);
    }

    [Fact]
    public void Application_gate_targets_latest_migration_and_909_objects()
    {
        Assert.Equal("20260905012129_ScopeInventoryIdentityCorrectionsToReceipts", DatabaseStartupDiagnostics.ExpectedSchemaMigration);
        var source = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs"));
        Assert.Contains("RoomSealEvents.RoomCodeSnapshot", source);
        Assert.Contains("RoomSealEvents.EffectiveAt", source);
        Assert.Contains("Rooms.SealRecordedAt", source);
        Assert.Contains("FK_Rooms_Users_SealedByUserId", source);
        Assert.Equal(909, source.Split('\n').Count(x => x.TrimStart().StartsWith("new(", StringComparison.Ordinal) || x.TrimStart().StartsWith(",new(", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Restored_production_seal_rehearsal_preserves_inventory_and_treatments_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_ROOM_SEALING_RESTORED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);

        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new CropQcDbContext(options);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var initialSnapshots = await ledger.GetSnapshotsAsync(null, null, default);
        var authoritativeTreatmentBins = initialSnapshots.Where(x => x.CurrentBins > 0)
            .GroupBy(x => (x.RoomId, Identity: RoomTreatmentService.IdentityKey(x)))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.CurrentBins));
        var explicitTreatmentBins = (await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.CurrentBins > 0).ToListAsync())
            .GroupBy(x => (x.RoomId, x.IdentityKey))
            .Select(x => new { x.Key.RoomId, x.Key.IdentityKey, Bins = x.Sum(y => y.CurrentBins) })
            .ToList();
        var treatmentIncompatibleRooms = explicitTreatmentBins
            .Where(x => !authoritativeTreatmentBins.TryGetValue((x.RoomId, x.IdentityKey), out var bins) || x.Bins > bins)
            .Select(x => x.RoomId).ToHashSet();
        var roomGroup = initialSnapshots
            .Where(x => x.CurrentBins > 0 && (x.FruitType == "Apple" || x.FruitType == "Pear"))
            .GroupBy(x => x.RoomId)
            .Where(x => x.Select(y => y.FruitType).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .Where(x => !treatmentIncompatibleRooms.Contains(x.Key))
            .OrderByDescending(x => x.Sum(y => y.CurrentBins))
            .First();
        var roomId = roomGroup.Key;
        var initialRoomSnapshots = await ledger.GetSnapshotsAsync(null, [roomId], default);
        var crop = initialRoomSnapshots[0].FruitType == "Pear" ? "Pears" : "Apples";
        var treatmentChemicalId = await db.TreatmentChemicals.AsNoTracking()
            .Where(x => x.IsActive && x.ApplicationLevel == TreatmentApplicationLevels.Room && x.Crop == crop)
            .OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var actor = await db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, actor.Email), new Claim(ClaimTypes.Role, BuiltInRoleNames.Manager)],
            "RestoredRoomSealingTest"));
        var baseline = await ProtectedStateAsync(db, ledger);
        var migrationHistory = await MigrationHistoryFingerprintAsync(db);
        var roomLabel = await db.Rooms.AsNoTracking().Where(x => x.Id == roomId)
            .Select(x => x.Warehouse.Code + " " + (x.CropQcRoomName ?? x.DisplayName ?? x.Code))
            .SingleAsync();
        var otherRoomId = await db.Rooms.AsNoTracking().Where(x => x.Id != roomId && !x.IsSealed)
            .OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var initialRoomFingerprint = InventoryFingerprint(initialRoomSnapshots);
        var sealEventsBefore = await db.RoomSealEvents.CountAsync();
        var sealAuditsBefore = await db.AuditLogs.CountAsync(x => x.Action == "RoomSealed" || x.Action == "RoomUnsealed");

        var service = new RoomSealingService(db);
        var sealForm = (await service.GetConfirmationAsync(roomId, principal, default))!.Form;
        sealForm.Note = "Disposable run-91 restored-production rehearsal";
        Assert.Null(await service.ChangeStateAsync(sealForm, true, principal, default));
        db.ChangeTracker.Clear();

        var sealedRoom = await db.Rooms.AsNoTracking().SingleAsync(x => x.Id == roomId);
        Assert.True(sealedRoom.IsSealed);
        Assert.NotNull(sealedRoom.SealedAt);
        Assert.Equal(actor.Id, sealedRoom.SealedByUserId);
        Assert.Equal(baseline, await ProtectedStateAsync(db, ledger));
        var sealedSnapshots = await ledger.GetSnapshotsAsync(null, [roomId], default);
        Assert.Equal(initialRoomFingerprint, InventoryFingerprint(sealedSnapshots));
        Assert.Contains(roomLabel, await RoomMovementSealGuard.ValidateAsync(db, [roomId, otherRoomId], [], default));
        Assert.Contains(roomLabel, await RoomMovementSealGuard.ValidateAsync(db, [], [roomId], default));

        var context = new DefaultHttpContext { User = principal };
        var treatmentService = new RoomTreatmentService(
            db,
            ledger,
            new AdminAccess(),
            new FixedHttpContextAccessor(context),
            new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-21T19:00:00Z"))),
            new ConsoleLogger<RoomTreatmentService>());
        var treatmentData = await treatmentService.GetRoomDataAsync(roomId, default);
        Assert.True(treatmentData.CanApply);
        Assert.NotEmpty(treatmentData.Current);
        var applyPage = await treatmentService.GetApplyPageAsync(new RoomTreatmentApplyForm
        {
            RoomId = roomId,
            AppliedAt = DateTimeOffset.Parse("2026-08-21T19:00:00Z")
        }, false, default);
        Assert.Equal(roomId, applyPage.Form.RoomId);
        Assert.True(applyPage.TotalBins > 0);
        var treatmentResult = await treatmentService.ApplyAsync(new RoomTreatmentApplyForm
        {
            RoomId = roomId,
            TreatmentChemicalId = treatmentChemicalId,
            AppliedAt = DateTimeOffset.Parse("2026-08-21T19:00:00Z"),
            OperationKey = "restored-room-sealing-treatment-20260821-v1",
            Notes = "Disposable proof that treatment remains available while sealed",
            ConfirmedReview = true
        }, default);
        Assert.Null(treatmentResult.Error);
        Assert.NotNull(treatmentResult.ApplicationId);
        Assert.Contains((await treatmentService.GetRoomDataAsync(roomId, default)).Current,
            x => x.Treatments.Any(y => y.Id == treatmentResult.ApplicationId));
        Assert.Equal(baseline, await ProtectedStateAsync(db, ledger));

        var unsealForm = (await service.GetConfirmationAsync(roomId, principal, default))!.Form;
        unsealForm.Note = "Disposable rehearsal complete";
        Assert.Null(await service.ChangeStateAsync(unsealForm, false, principal, default));
        db.ChangeTracker.Clear();

        Assert.False((await db.Rooms.AsNoTracking().SingleAsync(x => x.Id == roomId)).IsSealed);
        Assert.Null(await RoomMovementSealGuard.ValidateAsync(db, [roomId], [otherRoomId], default));
        Assert.Equal(baseline, await ProtectedStateAsync(db, ledger));
        Assert.Equal(initialRoomFingerprint, InventoryFingerprint(await ledger.GetSnapshotsAsync(null, [roomId], default)));
        Assert.Equal(migrationHistory, await MigrationHistoryFingerprintAsync(db));
        Assert.Equal(sealEventsBefore + 2, await db.RoomSealEvents.CountAsync());
        Assert.Equal(sealAuditsBefore + 2, await db.AuditLogs.CountAsync(x => x.Action == "RoomSealed" || x.Action == "RoomUnsealed"));

        Console.WriteLine($"Restored room-sealing rehearsal: room={roomLabel}; bins={initialRoomSnapshots.Sum(x => x.CurrentBins)}; " +
            $"inventoryFingerprint={initialRoomFingerprint}; migrationHistory={migrationHistory}; movementBlocked=true; treatmentApplied=true; unsealed=true.");
    }

    private static async Task<(int AdjustmentCount, long AdjustmentDelta, int ReceiptCount, long ReceiptBins, int TransferCount,
        int BinsRunCount, int ActualRunCount, int ProcessorShipmentCount, int TreatmentMovementCount, int CurrentBins, string InventoryFingerprint)>
        ProtectedStateAsync(CropQcDbContext db, IRoomInventoryLedgerQueryService ledger)
    {
        var snapshots = await ledger.GetSnapshotsAsync(null, null, default);
        return (
            await db.RoomInventoryAdjustments.CountAsync(),
            await db.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount),
            await db.Receipts.CountAsync(),
            await db.Receipts.SumAsync(x => (long)x.BinCount),
            await db.RoomTransfers.CountAsync(),
            await db.BinsRunEntries.CountAsync(),
            await db.ActualRuns.CountAsync(),
            await db.ProcessorShipments.CountAsync(),
            await db.TreatmentLineageMovements.CountAsync(),
            snapshots.Sum(x => x.CurrentBins),
            InventoryFingerprint(snapshots));
    }

    private static string InventoryFingerprint(IEnumerable<RoomInventoryLedgerSnapshot> snapshots)
    {
        var value = string.Join('|', snapshots
            .GroupBy(x => new
            {
                x.RoomId,
                x.CropYear,
                x.GrowerLotId,
                x.FruitProfileId,
                x.InventoryStatus,
                x.StoredVarietyCode,
                x.ProductionType,
                x.PoolStart
            })
            .Select(x => new { x.Key, CurrentBins = x.Sum(y => y.CurrentBins) })
            .OrderBy(x => x.Key.RoomId).ThenBy(x => x.Key.CropYear).ThenBy(x => x.Key.GrowerLotId).ThenBy(x => x.Key.FruitProfileId)
            .ThenBy(x => x.Key.InventoryStatus).ThenBy(x => x.Key.StoredVarietyCode).ThenBy(x => x.Key.ProductionType).ThenBy(x => x.Key.PoolStart)
            .Select(x => $"{x.Key.RoomId}:{x.Key.CropYear}:{x.Key.GrowerLotId}:{x.Key.FruitProfileId}:{x.Key.InventoryStatus}:{x.Key.StoredVarietyCode}:{x.Key.ProductionType}:{x.Key.PoolStart}:{x.CurrentBins}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static Task<string> MigrationHistoryFingerprintAsync(CropQcDbContext db) =>
        db.Database.SqlQueryRaw<string>(
            """
            SELECT count(*)::text || ':' || md5(string_agg("MigrationId" || '=' || "ProductVersion", '|' ORDER BY "MigrationId")) AS "Value"
            FROM "__EFMigrationsHistory"
            """
        ).SingleAsync();

    private sealed class AdminAccess : IUserAccessService
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(PageAccessLevel.Admin);
        public void InvalidateAll() { }
    }

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class ConsoleLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Console.WriteLine($"{typeof(T).Name} {logLevel}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
    }

    private static ClaimsPrincipal Principal(string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Email, Fixture.Email), new Claim(ClaimTypes.Role, role)], "Test"));

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;
    private static string Slice(string source, string from, string to) => source[source.IndexOf(from, StringComparison.Ordinal)..source.IndexOf(to, source.IndexOf(from, StringComparison.Ordinal), StringComparison.Ordinal)];

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
        public const int RoomId = 8111;
        public const int UserId = 8112;
        public const string Email = "room.manager@fruitandland.com";
        private readonly SqliteConnection connection;
        private Fixture(SqliteConnection connection, CropQcDbContext db)
        {
            this.connection = connection;
            Db = db;
            BusinessTime = new PacificBusinessTimeService(new FixedClock(NowUtc));
            Service = new RoomSealingService(db, BusinessTime);
        }
        public static readonly DateTimeOffset NowUtc = DateTimeOffset.Parse("2026-08-22T20:00:00Z");
        public CropQcDbContext Db { get; }
        public IBusinessTimeService BusinessTime { get; }
        public RoomSealingService Service { get; }

        public RoomSealForm Form(bool expectedIsSealed, DateTimeOffset? expectedEffectiveAt = null, string? note = null,
            DateOnly? date = null, TimeOnly? time = null) => new()
            {
                RoomId = RoomId,
                ExpectedIsSealed = expectedIsSealed,
                ExpectedEffectiveAt = expectedEffectiveAt,
                EffectiveDate = date ?? new DateOnly(2026, 8, 22),
                EffectiveTime = time ?? new TimeOnly(13, 0),
                Note = note
            };

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = 8110, Code = "SEAL-WH", Name = "Room Seal Test Warehouse" };
            var room = new Room { Id = RoomId, Warehouse = warehouse, WarehouseId = warehouse.Id, Code = "WP-12", Name = "WP Room 12", CropQcRoomName = "WP-12" };
            var user = new User { Id = UserId, Email = Email, DisplayName = "Room Manager", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var fruit = new FruitProfile { Id = 8113, Name = "Seal Test Gala", VarietyCode = "SEAL-GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var receipt = new Receipt { Id = 8114, CropYear = 2026, CompuTechReceiptId = "TR-SEAL", ReceiptType = "Receiving", ReceivedAt = DateTimeOffset.UtcNow, Warehouse = warehouse, WarehouseId = warehouse.Id, Room = room, RoomId = room.Id, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerName = "Test Grower", GrowerNumber = "100", LotCode = "100", BinCount = 20, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.AddRange(warehouse, room, user, fruit, receipt);
            await db.SaveChangesAsync();
            return new Fixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
