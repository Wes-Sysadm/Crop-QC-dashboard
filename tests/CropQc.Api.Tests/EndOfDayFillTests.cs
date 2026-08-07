using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class EndOfDayFillTests
{
    [Fact]
    public async Task Preview_UsesOnlyConfiguredRooms_AndReconcilesRoomVarietyGrowerTotals()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);

        Assert.True(preview.CanSend);
        var room = Assert.Single(preview.Rooms);
        Assert.Equal(145, room.CurrentBins);
        var variety = Assert.Single(room.Varieties);
        Assert.Equal("Gala", variety.Name);
        Assert.Equal(145, variety.Bins);
        Assert.Equal(145, variety.Growers.Sum(x => x.Bins));
        Assert.Equal([Fixture.RoomId], fixture.Inventory.RequestedRoomIds);
        Assert.DoesNotContain(preview.Rooms, x => x.RoomId == Fixture.UnconfiguredRoomId);
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task Send_RequiresConfirmation_AndRejectsAStalePreview()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);

        var uncheckedResult = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = false }, default);
        Assert.False(uncheckedResult.Success);
        Assert.Empty(fixture.Sender.Messages);

        fixture.Inventory.Bins = 146;
        var staleResult = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(staleResult.Success);
        Assert.True(staleResult.StalePreview);
        Assert.Empty(fixture.Sender.Messages);
        Assert.Empty(await fixture.Db.EndOfDayFillReportSends.ToListAsync());
    }

    [Fact]
    public async Task SuccessfulSend_UsesLoggedInGmailBoundary_StoresExactHistory_AndBlocksDuplicate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var form = new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true };

        var result = await fixture.Service.SendAsync(Fixture.SenderEmail, form, default);

        Assert.True(result.Success);
        var message = Assert.Single(fixture.Sender.Messages);
        Assert.Equal(Fixture.SenderEmail, message.Message.From);
        Assert.Equal("wes@fruitandland.com, jorge@wp-packing.com, rob@earlbrownandsons.com", message.Message.To);
        Assert.Equal("End of Day Fill Report — WP — August 6, 2026", message.Message.Subject);
        Assert.Contains("End of Day Fill Report as of August 6, 2026 — 9:22 PM Pacific", message.Message.HtmlBody);
        Assert.Contains("Grower 1084 — Smith Orchards — 145 bins", message.Message.TextBody);
        Assert.DoesNotContain("pressure", message.Message.TextBody, StringComparison.OrdinalIgnoreCase);

        var stored = Assert.Single(await fixture.Db.EndOfDayFillReportSends.AsNoTracking().ToListAsync());
        Assert.Equal(EndOfDayFillSendStatuses.Succeeded, stored.Status);
        Assert.True(stored.PhysicalCountConfirmed);
        Assert.Equal("fake-gmail-id", stored.GmailMessageId);
        Assert.Equal(message.Message.Subject, stored.Subject);
        Assert.Equal(message.Message.HtmlBody, stored.HtmlBody);
        Assert.Contains("\"currentBins\":145", stored.SnapshotJson);
        Assert.Empty(await fixture.Db.EndOfDayFillSendReservations.ToListAsync());

        var duplicate = await fixture.Service.SendAsync(Fixture.SenderEmail, form, default);
        Assert.False(duplicate.Success);
        Assert.Contains("No report data has changed", duplicate.Message);
        Assert.Single(fixture.Sender.Messages);
    }

    [Fact]
    public async Task MeaningfulInventoryChange_SendsRevision_ButClockChangeAloneDoesNot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.True((await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = first.PreviewToken!, PhysicalCountConfirmed = true }, default)).Success);

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(20);
        var clockOnly = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var blocked = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = clockOnly.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(blocked.Success);

        fixture.Inventory.Bins = 146;
        var revisionPreview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var revision = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = revisionPreview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.True(revision.Success);
        Assert.StartsWith("REVISION 1 — End of Day Fill Report", fixture.Sender.Messages.Last().Message.Subject);
        Assert.Contains("REVISION 1", fixture.Sender.Messages.Last().Message.HtmlBody);
        Assert.Equal([0, 1], await fixture.Db.EndOfDayFillReportSends.Where(x => x.Status == EndOfDayFillSendStatuses.Succeeded).OrderBy(x => x.RevisionNumber).Select(x => x.RevisionNumber).ToListAsync());
    }

    [Fact]
    public async Task ActiveRecipientChange_IsMeaningfulAndProducesRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.True((await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = first.PreviewToken!, PhysicalCountConfirmed = true }, default)).Success);

        (await fixture.Db.EndOfDayFillReportRecipients.SingleAsync(x => x.EmailAddress == "rob@earlbrownandsons.com")).IsActive = false;
        await fixture.Db.SaveChangesAsync();
        var changed = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var revision = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = changed.PreviewToken!, PhysicalCountConfirmed = true }, default);

        Assert.True(revision.Success);
        Assert.StartsWith("REVISION 1", fixture.Sender.Messages.Last().Message.Subject);
        Assert.DoesNotContain("rob@earlbrownandsons.com", fixture.Sender.Messages.Last().Message.To);
    }

    [Fact]
    public async Task FailedSend_IsAudited_AndDoesNotAdvanceSuccessfulRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Sender.FailNext = true;
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var failed = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(failed.Success);
        Assert.Equal(EndOfDayFillSendStatuses.Failed, (await fixture.Db.EndOfDayFillReportSends.SingleAsync()).Status);

        var retryPreview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var retry = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = retryPreview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.True(retry.Success);
        Assert.Equal(0, (await fixture.Db.EndOfDayFillReportSends.SingleAsync(x => x.Status == EndOfDayFillSendStatuses.Succeeded)).RevisionNumber);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "send-failure");
    }

    [Fact]
    public async Task ChangedThenReturnedSnapshot_IsAllowedRelativeToImmediatelyPreviousSuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        async Task<EndOfDayFillSendResult> SendCurrentAsync()
        {
            var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
            return await fixture.Service.SendAsync(Fixture.SenderEmail,
                new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        }

        Assert.True((await SendCurrentAsync()).Success);
        fixture.Inventory.Bins = 146;
        Assert.True((await SendCurrentAsync()).Success);
        fixture.Inventory.Bins = 145;
        Assert.True((await SendCurrentAsync()).Success);

        Assert.Equal([0, 1, 2], await fixture.Db.EndOfDayFillReportSends.OrderBy(x => x.RevisionNumber).Select(x => x.RevisionNumber).ToListAsync());
    }

    [Fact]
    public async Task ActiveReservation_BlocksConcurrentDuplicateBeforeGmail()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var user = await fixture.Db.Users.SingleAsync(x => x.Email == Fixture.SenderEmail);
        var attempt = new EndOfDayFillReportSend
        {
            ReportGroupId = 1,
            ReportGroupName = "WP End of Day Fill",
            Facility = "WP",
            PacificReportDate = new DateOnly(2026, 8, 6),
            RevisionNumber = 0,
            SenderUserId = user.Id,
            SenderEmail = user.Email,
            SenderDisplayName = user.DisplayName,
            RecipientsJson = "[]",
            PhysicalCountConfirmed = true,
            SnapshotHash = new string('a', 64),
            SnapshotJson = "{}",
            Subject = "reserved",
            HtmlBody = "reserved",
            TextBody = "reserved",
            Status = EndOfDayFillSendStatuses.Pending,
            CreatedAt = fixture.Clock.UtcNow,
            AttemptedAt = fixture.Clock.UtcNow
        };
        fixture.Db.EndOfDayFillReportSends.Add(attempt);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.EndOfDayFillSendReservations.Add(new EndOfDayFillSendReservation { ReportGroupId = 1, PacificReportDate = new DateOnly(2026, 8, 6), RevisionNumber = 0, SnapshotHash = attempt.SnapshotHash, SendAttemptId = attempt.Id, CreatedAt = fixture.Clock.UtcNow });
        await fixture.Db.SaveChangesAsync();

        var blocked = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(blocked.Success);
        Assert.Contains("already in progress", blocked.Message);
        Assert.Empty(fixture.Sender.Messages);
    }

    [Fact]
    public async Task MissingIdentityCapacityRecipientsOrGmail_FailsClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Inventory.GrowerNumber = "";
        fixture.Inventory.CanonicalName = "";
        (await fixture.Db.Rooms.SingleAsync(x => x.Id == Fixture.RoomId)).CapacityBins = 0;
        foreach (var recipient in await fixture.Db.EndOfDayFillReportRecipients.ToListAsync()) recipient.IsActive = false;
        foreach (var credential in await fixture.Db.UserGoogleCredentials.ToListAsync()) credential.Scope = "openid email";
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.False(preview.CanSend);
        Assert.Contains(preview.Issues, x => x.Code == "missing-grower");
        Assert.Contains(preview.Issues, x => x.Code == "missing-variety");
        Assert.Contains(preview.Issues, x => x.Code == "invalid-capacity");
        Assert.Contains(preview.Issues, x => x.Code == "no-recipients");
        Assert.Contains(preview.Issues, x => x.Code == "gmail");
    }

    [Fact]
    public async Task AdminConfiguration_RejectsCrossFacilityAndDuplicateActiveMembership_AndAuditsAssignments()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = new EndOfDayFillAdminService(fixture.Db);
        var crossFacility = await admin.SaveGroupAsync(new EndOfDayFillGroupForm { Name = "Bad", Facility = "EBS", IsActive = true, RoomIds = [Fixture.RoomId] }, Fixture.SenderEmail, default);
        Assert.Contains("does not belong", crossFacility);

        var duplicate = await admin.SaveGroupAsync(new EndOfDayFillGroupForm { Name = "Other WP", Facility = "WP", IsActive = true, RoomIds = [Fixture.RoomId] }, Fixture.SenderEmail, default);
        Assert.Contains("already belongs", duplicate);

        var user = await fixture.Db.Users.SingleAsync(x => x.Email == Fixture.UnassignedEmail);
        Assert.Null(await admin.SaveUserAssignmentsAsync(new EndOfDayFillUserAssignmentsForm { UserId = user.Id, GroupIds = [1] }, Fixture.SenderEmail, default));
        Assert.True(await fixture.Db.EndOfDayFillUserGroupAssignments.AnyAsync(x => x.UserId == user.Id && x.ReportGroupId == 1));
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "update-assignments" && x.EntityKey == user.Id.ToString());
    }

    [Fact]
    public async Task AdminConfiguration_CreatesAndEditsGroupsAndRecipientsWithAuditHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = new EndOfDayFillAdminService(fixture.Db);

        Assert.Null(await admin.SaveGroupAsync(new EndOfDayFillGroupForm
        {
            Name = "Second WP scope",
            Facility = "WP",
            IsActive = true,
            RoomIds = [Fixture.UnconfiguredRoomId]
        }, Fixture.SenderEmail, default));
        var group = await fixture.Db.EndOfDayFillReportGroups.Include(x => x.Rooms).SingleAsync(x => x.Name == "Second WP scope");
        Assert.Equal([Fixture.UnconfiguredRoomId], group.Rooms.Select(x => x.RoomId));

        Assert.Null(await admin.SaveGroupAsync(new EndOfDayFillGroupForm
        {
            Id = group.Id,
            Name = "Renamed WP scope",
            Facility = "WP",
            IsActive = false,
            RoomIds = [Fixture.UnconfiguredRoomId]
        }, Fixture.SenderEmail, default));
        Assert.False((await fixture.Db.EndOfDayFillReportGroups.SingleAsync(x => x.Id == group.Id)).IsActive);

        Assert.Null(await admin.SaveRecipientAsync(new EndOfDayFillRecipientForm
        {
            EmailAddress = "QC@FruitAndLand.com",
            IsActive = true,
            SortOrder = 5
        }, Fixture.SenderEmail, default));
        var recipient = await fixture.Db.EndOfDayFillReportRecipients.SingleAsync(x => x.NormalizedEmailAddress == "QC@FRUITANDLAND.COM");
        Assert.Equal("qc@fruitandland.com", recipient.EmailAddress);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityName == "end-of-day-fill-report-group" && x.Action == "create");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityName == "end-of-day-fill-report-group" && x.Action == "update");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityName == "end-of-day-fill-report-recipient" && x.Action == "create");
    }

    [Fact]
    public async Task AssignmentChecks_FailClosedForUnassignedUsersAndUnassignedGroups()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.True(await fixture.Service.HasActiveAssignmentAsync(Fixture.SenderEmail, default));
        Assert.True(await fixture.Service.HasGroupAssignmentAsync(Fixture.SenderEmail, 1, default));
        Assert.False(await fixture.Service.HasActiveAssignmentAsync(Fixture.UnassignedEmail, default));
        Assert.False(await fixture.Service.HasGroupAssignmentAsync(Fixture.SenderEmail, 2, default));
        Assert.False(await fixture.Service.HasGroupAssignmentAsync(Fixture.UnassignedEmail, 1, default));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string SenderEmail = "sender@fruitandland.com";
        public const string UnassignedEmail = "unassigned@fruitandland.com";
        public const int RoomId = 910;
        public const int UnconfiguredRoomId = 911;
        private readonly SqliteConnection connection;
        public CropQcDbContext Db { get; }
        public MutableInventorySource Inventory { get; }
        public FakeEmailSender Sender { get; }
        public MutableClock Clock { get; }
        public EndOfDayFillService Service { get; }

        private Fixture(SqliteConnection connection, CropQcDbContext db, MutableInventorySource inventory, FakeEmailSender sender, MutableClock clock)
        {
            this.connection = connection;
            Db = db;
            Inventory = inventory;
            Sender = sender;
            Clock = clock;
            Service = new EndOfDayFillService(db, inventory, sender, new EmailOptions { Provider = EmailProviders.GmailUser }, new PacificBusinessTimeService(clock), new EphemeralDataProtectionProvider(), NullLogger<EndOfDayFillService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var sender = new User { Email = SenderEmail, DisplayName = "Current Sender", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var unassigned = new User { Email = UnassignedEmail, DisplayName = "No Access", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var warehouse = await db.Warehouses.SingleAsync(x => x.Code == "WP");
            db.AddRange(sender, unassigned);
            await db.SaveChangesAsync();
            var room = new Room { Id = RoomId, WarehouseId = warehouse.Id, Code = "DH-1", Name = "DH Room 1", DisplayName = "DH-1", CapacityBins = 900, IsActive = true };
            var unconfigured = new Room { Id = UnconfiguredRoomId, WarehouseId = warehouse.Id, Code = "DH-2", Name = "DH Room 2", CapacityBins = 900, IsActive = true };
            db.AddRange(room, unconfigured);
            db.EndOfDayFillReportGroupRooms.Add(new EndOfDayFillReportGroupRoom { ReportGroupId = 1, RoomId = RoomId, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = sender.Id });
            db.EndOfDayFillUserGroupAssignments.Add(new EndOfDayFillUserGroupAssignment { UserId = sender.Id, ReportGroupId = 1, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = sender.Id });
            db.UserGoogleCredentials.Add(new UserGoogleCredential { UserId = sender.Id, Provider = "Google", RefreshTokenEncrypted = "test-only", Scope = GmailScopes.Send, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            var inventory = new MutableInventorySource();
            var fakeSender = new FakeEmailSender();
            var clock = new MutableClock { UtcNow = new DateTimeOffset(2026, 8, 7, 4, 22, 0, TimeSpan.Zero) };
            return new Fixture(connection, db, inventory, fakeSender, clock);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class MutableInventorySource : IEndOfDayFillInventorySource
    {
        public int Bins { get; set; } = 145;
        public string GrowerNumber { get; set; } = "1084";
        public string CanonicalName { get; set; } = "Gala";
        public IReadOnlyList<int> RequestedRoomIds { get; private set; } = [];
        public Task<IReadOnlyList<RoomLotSummaryViewModel>> GetCurrentLotsAsync(IReadOnlyCollection<int> roomIds, CancellationToken cancellationToken)
        {
            RequestedRoomIds = roomIds.Order().ToList();
            IReadOnlyList<RoomLotSummaryViewModel> result =
            [
                new() { RoomId = Fixture.RoomId, RoomCode = "DH-1", CurrentBins = Bins, GrowerNumber = GrowerNumber, GrowerName = "Smith Orchards", CanonicalVarietyKey = CanonicalName.Length == 0 ? "" : "gala", CanonicalVarietyName = CanonicalName, ProductionType = "Fresh", IsOrganic = false, VarietyHexColor = "#c62828", InventoryKey = "canonical-398", GrowerLotId = 398 },
                new() { RoomId = Fixture.UnconfiguredRoomId, RoomCode = "DH-2", CurrentBins = 999, GrowerNumber = "9999", GrowerName = "Excluded", CanonicalVarietyKey = "fuji", CanonicalVarietyName = "Fuji", ProductionType = "Fresh", IsOrganic = false, InventoryKey = "excluded" }
            ];
            return Task.FromResult<IReadOnlyList<RoomLotSummaryViewModel>>(result.Where(x => roomIds.Contains(x.RoomId)).ToList());
        }
    }

    private sealed class FakeEmailSender : IQcEmailSender
    {
        public bool FailNext { get; set; }
        public List<(User Sender, QcEmailMessage Message)> Messages { get; } = [];
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add((sender, message));
            if (FailNext) { FailNext = false; return Task.FromResult(QcEmailSendResult.Failed("fake failure")); }
            return Task.FromResult(QcEmailSendResult.Sent("fake-gmail-id"));
        }
    }

    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } }
}
