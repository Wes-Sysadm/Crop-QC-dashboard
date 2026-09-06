using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class HarvestWatchServiceTests
{
    [Theory]
    [InlineData("DH", true, true)]
    [InlineData("EBS", true, true)]
    [InlineData("WP", true, false)]
    [InlineData("McDougall", true, false)]
    [InlineData("DH", false, false)]
    public async Task Deployment_is_limited_to_currently_sealed_DH_and_EBS_rooms(string facility, bool sealedRoom, bool succeeds)
    {
        await using var fixture = await Fixture.CreateAsync(facility, sealedRoom);
        var result = await fixture.Service.DeployAsync(fixture.Form("00042"), fixture.Manager, default);
        Assert.Equal(succeeds, result.Success);
        Assert.Equal(succeeds ? 1 : 0, await fixture.Db.HarvestWatchDeployments.CountAsync());
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("123456")]
    [InlineData("12A45")]
    [InlineData("12-45")]
    public async Task Deployment_requires_exactly_five_numeric_characters(string code)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.DeployAsync(fixture.Form(code), fixture.Manager, default);
        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.HarvestWatchDeployments.ToListAsync());
    }

    [Fact]
    public async Task Leading_zero_is_preserved_and_each_deployment_gets_a_unique_verification_email()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.DeployAsync(fixture.Form("00042", "90731"), fixture.Manager, default);
        Assert.True(result.Success);
        var deployments = await fixture.Db.HarvestWatchDeployments.OrderBy(x => x.HarvestWatchCode).ToListAsync();
        Assert.Equal(["00042", "90731"], deployments.Select(x => x.HarvestWatchCode));
        Assert.Equal(2, fixture.Dispatcher.Verifications.Count);
        Assert.NotEqual(deployments[0].CorrelationToken, deployments[1].CorrelationToken);
    }

    [Fact]
    public async Task More_than_three_requires_confirmation_but_is_not_blocked()
    {
        await using var fixture = await Fixture.CreateAsync();
        var unconfirmed = await fixture.Service.DeployAsync(fixture.Form("10001", "10002", "10003", "10004"), fixture.Manager, default);
        Assert.False(unconfirmed.Success);
        var confirmed = fixture.Form("10001", "10002", "10003", "10004"); confirmed.ConfirmMoreThanThree = true;
        Assert.True((await fixture.Service.DeployAsync(confirmed, fixture.Manager, default)).Success);
        Assert.Equal(4, await fixture.Db.HarvestWatchDeployments.CountAsync());
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(3, 1, true)]
    [InlineData(2, 1, false)]
    public async Task Existing_active_devices_are_included_in_more_than_three_confirmation(int active, int requested, bool requiresConfirmation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var existingCodes = Enumerable.Range(1, active).Select(x => (10000 + x).ToString()).ToArray();
        var existing = fixture.Form(existingCodes); existing.ConfirmMoreThanThree = active > 3;
        Assert.True((await fixture.Service.DeployAsync(existing, fixture.Manager, default)).Success);
        var requestCodes = Enumerable.Range(1, requested).Select(x => (20000 + x).ToString()).ToArray();
        var result = await fixture.Service.DeployAsync(fixture.Form(requestCodes), fixture.Manager, default);
        Assert.Equal(!requiresConfirmation, result.Success);
    }

    [Fact]
    public async Task Active_code_is_unique_across_rooms_and_retirement_preserves_history_for_reuse()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Service.DeployAsync(fixture.Form("12345"), fixture.Manager, default)).Success);
        fixture.Db.Rooms.Add(new Room { Id = 2, WarehouseId = 1, Warehouse = fixture.Warehouse, Code = "D2", Name = "DH Room 2", IsSealed = true, SealedAt = fixture.Now.AddMinutes(-1) });
        await fixture.Db.SaveChangesAsync();
        var second = fixture.Form("12345"); second.RoomId = 2;
        Assert.False((await fixture.Service.DeployAsync(second, fixture.Manager, default)).Success);
        var deployment = await fixture.Db.HarvestWatchDeployments.SingleAsync();
        Assert.Null(await fixture.Service.RetireAsync(1, deployment.Id, new HarvestWatchRetireForm { Note = "replacement" }, fixture.Manager, default));
        Assert.True((await fixture.Service.DeployAsync(second, fixture.Manager, default)).Success);
        Assert.Equal(2, await fixture.Db.HarvestWatchDeployments.CountAsync());
        Assert.Contains(await fixture.Db.HarvestWatchStatusHistories.ToListAsync(), x => x.NewStatus == HarvestWatchStatuses.Removed);
    }

    [Fact]
    public async Task Inbound_reply_is_sender_checked_correlated_idempotent_and_transitions_error_to_working()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.DeployAsync(fixture.Form("12345"), fixture.Manager, default);
        var deployment = await fixture.Db.HarvestWatchDeployments.SingleAsync();
        var marker = $"[HW:{deployment.Id}:{deployment.CorrelationToken}]";
        var rejected = await fixture.Service.ProcessInboundReplyAsync(new HarvestWatchInboundReply("wrong", "other@example.com", marker, "Working", fixture.Now), default);
        Assert.Equal("RejectedSender", rejected.Outcome);
        var error = await fixture.Service.ProcessInboundReplyAsync(new HarvestWatchInboundReply("error", "wes@fruitandland.com", marker, "Error - Failed to Read", fixture.Now), default);
        Assert.Equal("StatusUpdated", error.Outcome);
        fixture.Db.ChangeTracker.Clear(); deployment = await fixture.Db.HarvestWatchDeployments.SingleAsync();
        Assert.Equal(HarvestWatchStatuses.ErrorFailedToRead, deployment.Status);
        Assert.Single(fixture.Dispatcher.ErrorNotifications);
        Assert.Equal("Duplicate", (await fixture.Service.ProcessInboundReplyAsync(new HarvestWatchInboundReply("error", "wes@fruitandland.com", marker, "Error - Failed to Read", fixture.Now), default)).Outcome);
        Assert.Single(fixture.Dispatcher.ErrorNotifications);
        await fixture.Service.ProcessInboundReplyAsync(new HarvestWatchInboundReply("working", "wes@fruitandland.com", marker, "working", fixture.Now), default);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(HarvestWatchStatuses.Working, (await fixture.Db.HarvestWatchDeployments.SingleAsync()).Status);
        Assert.Equal(3, await fixture.Db.HarvestWatchStatusHistories.CountAsync(x => x.Source != "OutboundEmail"));
    }

    [Theory]
    [InlineData("Working", HarvestWatchStatuses.Working)]
    [InlineData("error failed to read", HarvestWatchStatuses.ErrorFailedToRead)]
    [InlineData("Low Reading!", HarvestWatchStatuses.ErrorLowReading)]
    [InlineData("Working\nError - Low Reading", null)]
    [InlineData("I think it is working", null)]
    public void Reply_parser_is_strict_and_tolerant(string reply, string? expected) => Assert.Equal(expected, HarvestWatchReplyParser.ParseStatus(reply));

    [Theory]
    [InlineData("Working\n\nOn Sun, Sep 6, 2026 at 8:00 PM Crop QC wrote:\n> Reply with one of:\n> Working\n> Error - Failed to Read\n> Error - Low Reading", HarvestWatchStatuses.Working)]
    [InlineData("Error - Failed to Read\n\nOn Sun, Sep 6, 2026 at 8:00 PM Crop QC wrote:\n> Working\n> Error - Failed to Read\n> Error - Low Reading", HarvestWatchStatuses.ErrorFailedToRead)]
    [InlineData("Error - Low Reading\n\nOn Sun, Sep 6, 2026 at 8:00 PM Crop QC wrote:\n> Working\n> Error - Failed to Read\n> Error - Low Reading", HarvestWatchStatuses.ErrorLowReading)]
    public void Quoted_Gmail_content_is_excluded_from_status_parsing(string reply, string expected) => Assert.Equal(expected, HarvestWatchReplyParser.ParseStatus(HarvestWatchReplyParser.ExtractNewReplyContent(reply)));

    [Fact]
    public void Ambiguity_in_the_new_reply_content_is_not_hidden_by_quote_extraction()
    {
        var reply = "Working\nError - Low Reading\n\nOn Sun, Sep 6, 2026 at 8:00 PM Crop QC wrote:\n> [HW:123:abcdefghijklmnop]";
        Assert.Null(HarvestWatchReplyParser.ParseStatus(HarvestWatchReplyParser.ExtractNewReplyContent(reply)));
        Assert.NotNull(HarvestWatchReplyParser.ParseCorrelation("", reply));
    }

    [Fact]
    public async Task Retired_deployment_ignores_delayed_reply_and_reused_code_has_new_correlation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.DeployAsync(fixture.Form("12345"), fixture.Manager, default);
        var retired = await fixture.Db.HarvestWatchDeployments.SingleAsync();
        var oldMarker = $"[HW:{retired.Id}:{retired.CorrelationToken}]";
        Assert.Null(await fixture.Service.RetireAsync(1, retired.Id, new HarvestWatchRetireForm(), fixture.Manager, default));
        var delayed = await fixture.Service.ProcessInboundReplyAsync(new HarvestWatchInboundReply("retired", "wes@fruitandland.com", oldMarker, "Working", fixture.Now), default);
        Assert.Equal("IgnoredInactiveDeployment", delayed.Outcome);
        Assert.Equal(HarvestWatchStatuses.Removed, (await fixture.Db.HarvestWatchDeployments.SingleAsync(x => x.Id == retired.Id)).Status);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "HarvestWatchVerificationIgnored" && x.EntityKey == retired.Id.ToString());
        Assert.True((await fixture.Service.DeployAsync(fixture.Form("12345"), fixture.Manager, default)).Success);
        var replacement = await fixture.Db.HarvestWatchDeployments.OrderByDescending(x => x.Id).FirstAsync();
        Assert.NotEqual(retired.CorrelationToken, replacement.CorrelationToken);
        Assert.Equal("StatusUpdated", (await fixture.Service.ProcessInboundReplyAsync(new HarvestWatchInboundReply("replacement", "wes@fruitandland.com", $"[HW:{replacement.Id}:{replacement.CorrelationToken}]", "Working", fixture.Now), default)).Outcome);
    }

    [Fact]
    public async Task Verification_delivery_failure_is_retried_once_and_never_resent_after_success()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Dispatcher.FailVerification = true;
        Assert.True((await fixture.Service.DeployAsync(fixture.Form("12345"), fixture.Manager, default)).Success);
        var deployment = await fixture.Db.HarvestWatchDeployments.SingleAsync();
        var workRow = await fixture.Db.HarvestWatchStatusHistories.SingleAsync(x => x.HarvestWatchDeploymentId == deployment.Id && x.Source == "OutboundEmail");
        var work = DeserializeWork(workRow.Note)!;
        Assert.Equal("FailedRetryable", work.Status);
        fixture.Dispatcher.FailVerification = false;
        work.NextRetryAt = fixture.Now;
        workRow.Note = JsonSerializer.Serialize(work);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ProcessPendingOutboundEmailsAsync([deployment.Id], default);
        fixture.Db.ChangeTracker.Clear();
        workRow = await fixture.Db.HarvestWatchStatusHistories.SingleAsync(x => x.HarvestWatchDeploymentId == deployment.Id && x.Source == "OutboundEmail");
        Assert.Equal("Sent", DeserializeWork(workRow.Note)!.Status);
        Assert.Equal(2, fixture.Dispatcher.Verifications.Count);
        await fixture.Service.ProcessPendingOutboundEmailsAsync([deployment.Id], default);
        Assert.Equal(2, fixture.Dispatcher.Verifications.Count);
    }

    [Fact]
    public async Task Error_notification_failure_is_durable_and_retried_without_duplicate_inbound_processing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Dispatcher.FailErrorNotification = true;
        await fixture.Service.DeployAsync(fixture.Form("12345"), fixture.Manager, default);
        var deployment = await fixture.Db.HarvestWatchDeployments.SingleAsync();
        var reply = new HarvestWatchInboundReply("error-delivery", "wes@fruitandland.com", $"[HW:{deployment.Id}:{deployment.CorrelationToken}]", "Error - Low Reading", fixture.Now);
        Assert.Equal("StatusUpdated", (await fixture.Service.ProcessInboundReplyAsync(reply, default)).Outcome);
        Assert.Equal("Duplicate", (await fixture.Service.ProcessInboundReplyAsync(reply, default)).Outcome);
        var errorWorkRow = await fixture.Db.HarvestWatchStatusHistories.SingleAsync(x => x.HarvestWatchDeploymentId == deployment.Id && x.Source == "OutboundEmail" && x.Note!.Contains("ErrorNotification"));
        var errorWork = DeserializeWork(errorWorkRow.Note)!;
        Assert.Equal("FailedRetryable", errorWork.Status);
        fixture.Dispatcher.FailErrorNotification = false;
        errorWork.NextRetryAt = fixture.Now;
        errorWorkRow.Note = JsonSerializer.Serialize(errorWork);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ProcessPendingOutboundEmailsAsync([deployment.Id], default);
        Assert.Equal("Sent", DeserializeWork(errorWorkRow.Note)!.Status);
        Assert.Equal(2, fixture.Dispatcher.ErrorNotifications.Count);
    }

    [Fact]
    public void Gmail_read_scope_is_limited_to_the_dedicated_HarvestWatch_mailbox_flow()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var ordinaryLogin = program[..program.IndexOf("authenticationBuilder.AddGoogle(HarvestWatchConstants.MailboxAuthenticationScheme", StringComparison.Ordinal)];
        Assert.DoesNotContain("options.Scope.Add(gmailOptions.ReadScope)", ordinaryLogin);
        Assert.Contains("HarvestWatchConstants.MailboxAuthenticationScheme", program);
        Assert.Contains("Only the dedicated HarvestWatch mailbox can grant Gmail read access.", program);
    }

    [Fact]
    public void Mailbox_polling_paginates_and_uses_the_dedicated_read_token()
    {
        var worker = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "HarvestWatchMailboxHostedService.cs"));
        Assert.Contains("GetMailboxAccessTokenAsync", worker);
        Assert.Contains("NextPageToken", worker);
        Assert.Contains("while (!string.IsNullOrWhiteSpace(pageToken))", worker);
        Assert.Contains("cursor will not advance", worker);
    }

    [Fact]
    public async Task Automated_HarvestWatch_email_uses_the_dedicated_mailbox_not_the_deployer_credential()
    {
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase($"harvestwatch-mailbox-{Guid.NewGuid():N}").Options);
        var wes = new User { Id = 1, Email = "wes@fruitandland.com", DisplayName = "Wes", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var manager = new User { Id = 2, Email = "manager@fruitandland.com", DisplayName = "Manager", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        db.Users.AddRange(wes, manager); await db.SaveChangesAsync();
        var sender = new CapturingSender();
        var dispatcher = new HarvestWatchEmailDispatcher(db, sender);
        var deployment = new HarvestWatchDeployment { Id = 12, HarvestWatchCode = "00123", Status = HarvestWatchStatuses.PendingVerification, DeployedAt = DateTimeOffset.UtcNow, DeployedByUserId = manager.Id, DeployedByUser = manager, DeployerEmailSnapshot = manager.Email, WarehouseCodeSnapshot = "DH", RoomCodeSnapshot = "Room 1", VarietySnapshot = "Gala", CorrelationToken = "abcdefghijklmnop", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        Assert.True((await dispatcher.SendVerificationAsync(deployment, default)).Success);
        Assert.Equal(wes.Email, sender.Sender!.Email);
        Assert.Equal(wes.Email, sender.Message!.From);
        Assert.Equal(wes.Email, sender.Message.ReplyTo);
        Assert.Contains("[HW:12:abcdefghijklmnop]", sender.Message.Subject);
    }

    [Fact]
    public async Task Ambiguous_reply_and_outbound_failure_do_not_lose_or_change_a_deployment()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Dispatcher.FailVerification = true;
        Assert.True((await fixture.Service.DeployAsync(fixture.Form("12345"), fixture.Manager, default)).Success);
        var deployment = await fixture.Db.HarvestWatchDeployments.SingleAsync();
        Assert.NotNull(deployment.VerificationEmailError);
        var marker = $"[HW:{deployment.Id}:{deployment.CorrelationToken}]";
        Assert.Equal("IgnoredAmbiguousOrUnknownStatus", (await fixture.Service.ProcessInboundReplyAsync(new HarvestWatchInboundReply("ambiguous", "wes@fruitandland.com", marker, "Working\nError - Low Reading", fixture.Now), default)).Outcome);
        Assert.Equal(HarvestWatchStatuses.PendingVerification, (await fixture.Db.HarvestWatchDeployments.SingleAsync()).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-09-06T02:00:00Z");
        public CropQcDbContext Db { get; private init; } = null!;
        public Warehouse Warehouse { get; private init; } = null!;
        public HarvestWatchService Service { get; private init; } = null!;
        public RecordingDispatcher Dispatcher { get; private init; } = null!;
        public ClaimsPrincipal Manager { get; private init; } = null!;

        public static async Task<Fixture> CreateAsync(string facility = "DH", bool sealedRoom = true)
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase($"harvestwatch-{Guid.NewGuid():N}").Options);
            var warehouse = new Warehouse { Id = 1, Code = facility, Name = facility };
            var user = new User { Id = 1, Email = "manager@fruitandland.com", DisplayName = "Manager", Domain = "fruitandland.com", CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(warehouse); db.Users.Add(user); db.Rooms.Add(new Room { Id = 1, WarehouseId = 1, Warehouse = warehouse, Code = "D1", Name = "DH Room 1", IsSealed = sealedRoom, SealedAt = sealedRoom ? DateTimeOffset.Parse("2026-09-06T01:00:00Z") : null });
            await db.SaveChangesAsync();
            var dispatcher = new RecordingDispatcher();
            var time = new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-09-06T02:00:00Z")));
            return new Fixture { Db = db, Warehouse = warehouse, Dispatcher = dispatcher, Service = new HarvestWatchService(db, new EmptyLedger(), dispatcher, time, NullLogger<HarvestWatchService>.Instance), Manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Role, BuiltInRoleNames.Manager)])) };
        }
        public HarvestWatchDeployForm Form(params string[] codes) => new() { RoomId = 1, Codes = codes.ToList() };
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingDispatcher : IHarvestWatchEmailDispatcher
    {
        public bool FailVerification { get; set; }
        public bool FailErrorNotification { get; set; }
        public List<long> Verifications { get; } = [];
        public List<long> ErrorNotifications { get; } = [];
        public Task<QcEmailSendResult> SendVerificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken) { Verifications.Add(deployment.Id); return Task.FromResult(FailVerification ? QcEmailSendResult.Failed("temporary") : QcEmailSendResult.Sent($"v{deployment.Id}")); }
        public Task<QcEmailSendResult> SendErrorNotificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken) { ErrorNotifications.Add(deployment.Id); return Task.FromResult(FailErrorNotification ? QcEmailSendResult.Failed("temporary") : QcEmailSendResult.Sent($"e{deployment.Id}")); }
    }
    private sealed class EmptyLedger : IRoomInventoryLedgerQueryService
    {
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>([]);
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId, CancellationToken cancellationToken) => GetSnapshotsAsync(warehouseId, roomIds, cancellationToken);
    }
    private sealed class CapturingSender : IQcEmailSender
    {
        public User? Sender { get; private set; }
        public QcEmailMessage? Message { get; private set; }
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) { Sender = sender; Message = message; return Task.FromResult(QcEmailSendResult.Sent("sent")); }
    }
    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
    private static HarvestWatchOutboundEmailWork? DeserializeWork(string? note) => JsonSerializer.Deserialize<HarvestWatchOutboundEmailWork>(note ?? "", new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}
