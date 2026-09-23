using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class RoomTransferRestoredPostgreSqlTests
{
    [Fact]
    public async Task Fresh_restore_proves_guarded_repair_and_authenticated_bulk_transfer_when_configured()
    {
        var connection = Environment.GetEnvironmentVariable("ROOM_TRANSFER_RESTORED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection);
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var before = await FingerprintAsync(db);
        await using var factory = new RestoreFactory(connection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string route = "/BinsRun?Section=Transfer&Facility=McDougall&RoomId=66";
        var blocked = await client.GetStringAsync(route);
        Assert.Contains("1058 explicit bins exceed 798", blocked);
        Assert.Contains("Transfer all eligible inventory", blocked);
        Assert.Equal(before, await FingerprintAsync(db));

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CropQc.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var script = (await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts", "postgresql", "repair-mcd14-lineage-20260923.sql")))
            .Replace("\\set ON_ERROR_STOP on", "").Replace(":'actor_user_id'", "'1'");
        await db.Database.ExecuteSqlRawAsync(script);
        db.ChangeTracker.Clear();
        var repaired = await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == 160);
        Assert.Equal(542, repaired.CurrentBins);
        Assert.Equal(256, await db.TreatmentLineageSegments.Where(x => x.Id == 148 || x.Id == 154).SumAsync(x => x.CurrentBins));
        var snapshots = await ledger.GetSnapshotsAsync(null, [66], default);
        Assert.Equal(2520, snapshots.Sum(x => x.CurrentBins));
        Assert.Equal(798, snapshots.Where(x => x.GrowerLotId == 448 && x.FruitProfileId == 17).Sum(x => x.CurrentBins));
        Assert.Equal(before, await FingerprintAsync(db));
        var repairedAuditCount = await db.AuditLogs.CountAsync(x => x.EntityKey == "mcd14-lineage-20260923-segment160");
        Assert.Equal(1, repairedAuditCount);
        await db.Database.ExecuteSqlRawAsync(script);
        Assert.Equal(repaired.UpdatedAt, await db.TreatmentLineageSegments.Where(x => x.Id == 160).Select(x => x.UpdatedAt).SingleAsync());
        Assert.Equal(repairedAuditCount, await db.AuditLogs.CountAsync(x => x.EntityKey == "mcd14-lineage-20260923-segment160"));
        var ready = await client.GetStringAsync(route);
        Assert.DoesNotContain("Treatment lineage requires review", ready);
        Assert.Contains("Transfer all eligible inventory", ready);
        Assert.Contains("ExpectedInventoryToken", ready);
        Assert.Contains("Review", ready);
        foreach (var path in new[] { "/Rooms/66", "/health", "/health/db" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        var destination = new Room { WarehouseId = 3, Code = "PR251-TEST", Name = "Disposable rehearsal destination", IsActive = true };
        db.Rooms.Add(destination);
        await db.SaveChangesAsync();
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Input(ready, "__RequestVerificationToken"),
            ["OperationKey"] = Input(ready, "OperationKey"),
            ["ExpectedInventoryToken"] = Input(ready, "ExpectedInventoryToken"),
            ["TransferAllEligible"] = "true",
            ["FromRoomId"] = "66",
            ["DestinationWarehouseId"] = "3",
            ["DestinationRoomId"] = destination.Id.ToString(),
            ["SourceLotKey"] = "__all__",
            ["BinCount"] = "2520",
            ["TransferAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["Reason"] = "Disposable PR251 rehearsal"
        };
        var response = await client.PostAsync("/BinsRun/Transfer", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, (await ledger.GetSnapshotsAsync(null, [66], default)).Sum(x => x.CurrentBins));
        Assert.Equal(2520, (await ledger.GetSnapshotsAsync(null, [destination.Id], default)).Sum(x => x.CurrentBins));
        var transfers = await db.RoomTransfers.CountAsync();
        var movements = await db.TreatmentLineageMovements.CountAsync();
        response = await client.PostAsync("/BinsRun/Transfer", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(transfers, await db.RoomTransfers.CountAsync());
        Assert.Equal(movements, await db.TreatmentLineageMovements.CountAsync());
        Assert.Equal(0, await db.TreatmentLineageSegments.Where(x => x.RoomId == 66).SumAsync(x => x.CurrentBins));
        Assert.Equal(2520, await db.TreatmentLineageSegments.Where(x => x.RoomId == destination.Id).SumAsync(x => x.CurrentBins));
        Assert.Equal(0, await db.RoomInventoryAdjustments.Where(x => x.RoomTransfer != null && x.RoomTransfer.DestinationRoomId == destination.Id).SumAsync(x => x.ChangeAmount));
    }

    private static string Input(string html, string name)
    {
        var input = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(x => x.Value).First(x => x.Contains($"name=\"{name}\"", StringComparison.Ordinal));
        return WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]*)\"").Groups[1].Value);
    }

    private static async Task<string> FingerprintAsync(CropQcDbContext db)
    {
        var hashes = new List<string>();
        foreach (var table in new[] { "Receipts", "RoomInventoryAdjustments", "TreatmentLineageMovements", "RoomTransfers", "InterCrewTransfers" })
        {
            var sql = $"SELECT md5(string_agg(row_to_json(t)::text, '' ORDER BY t.\"Id\")) AS \"Value\" FROM \"{table}\" t";
            hashes.Add(await db.Database.SqlQueryRaw<string>(sql).SingleAsync());
        }
        return string.Join('|', hashes);
    }

    private sealed class RestoreFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["ConnectionStrings:CropQc"] = connection,
                ["Database:EnsureCreatedOnStartup"] = "false",
                ["Database:SeedMasterDataOnStartup"] = "false",
                ["Backups:Enabled"] = "false",
                ["EbsDailyBinsEmail:Enabled"] = "false",
                ["Email:Provider"] = "None",
                ["TransferCustody:Enabled"] = "true",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft"] = "Error",
                ["RENDER_EXTERNAL_HOSTNAME"] = "pr251-restored.local"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "PR251Test"; o.DefaultChallengeScheme = "PR251Test"; })
                    .AddScheme<AuthenticationSchemeOptions, TestAuth>("PR251Test", _ => { });
            });
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "PR251Test")), "PR251Test")));
    }
}
