using System.Security.Claims;
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
        Assert.Equal(3, await fixture.Db.HarvestWatchStatusHistories.CountAsync());
    }

    [Theory]
    [InlineData("Working", HarvestWatchStatuses.Working)]
    [InlineData("error failed to read", HarvestWatchStatuses.ErrorFailedToRead)]
    [InlineData("Low Reading!", HarvestWatchStatuses.ErrorLowReading)]
    [InlineData("Working\nError - Low Reading", null)]
    [InlineData("I think it is working", null)]
    public void Reply_parser_is_strict_and_tolerant(string reply, string? expected) => Assert.Equal(expected, HarvestWatchReplyParser.ParseStatus(reply));

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
        public List<long> Verifications { get; } = [];
        public List<long> ErrorNotifications { get; } = [];
        public Task<QcEmailSendResult> SendVerificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken) { Verifications.Add(deployment.Id); return Task.FromResult(FailVerification ? QcEmailSendResult.Failed("temporary") : QcEmailSendResult.Sent($"v{deployment.Id}")); }
        public Task<QcEmailSendResult> SendErrorNotificationAsync(HarvestWatchDeployment deployment, CancellationToken cancellationToken) { ErrorNotifications.Add(deployment.Id); return Task.FromResult(QcEmailSendResult.Sent($"e{deployment.Id}")); }
    }
    private sealed class EmptyLedger : IRoomInventoryLedgerQueryService
    {
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>([]);
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId, CancellationToken cancellationToken) => GetSnapshotsAsync(warehouseId, roomIds, cancellationToken);
    }
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}
