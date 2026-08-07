using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class EndOfDayFillProductionRestoreBenchmarkTests
{
    [Fact]
    public async Task CompleteFakeSendAndStaleRecovery_RecordBoundedProductionRestoreProfile_WhenConfigured()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("CROPQC_EOD_BENCHMARK_DATABASE_URL");
        var outputPath = Environment.GetEnvironmentVariable("CROPQC_EOD_BENCHMARK_OUTPUT");
        if (string.IsNullOrWhiteSpace(databaseUrl) || string.IsNullOrWhiteSpace(outputPath)) return;

        await using var factory = new BenchmarkFactory(databaseUrl);
        var seed = await SeedAsync(factory.Services, 240);
        var phases = new List<Phase>();
        var exactSendQueryCount = 0;

        phases.Add(await RunPhaseAsync("fake-send-sequential-100", 100, 1, async index =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var recipient = await db.EndOfDayFillReportRecipients.SingleAsync(x => x.NormalizedEmailAddress == "ROB@EARLBROWNANDSONS.COM");
            recipient.IsActive = index % 2 == 0;
            await db.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<IEndOfDayFillService>();
            var preview = await service.GetPreviewAsync(ApplicationAreas.OwnerEmail, seed.PrimaryGroupId, default);
            var counter = scope.ServiceProvider.GetRequiredService<IPerformanceQueryCounter>();
            counter.Reset();
            var result = await service.SendAsync(ApplicationAreas.OwnerEmail, new EndOfDayFillSendForm
            {
                GroupId = seed.PrimaryGroupId,
                PreviewToken = preview.PreviewToken!,
                PhysicalCountConfirmed = true
            }, default);
            Assert.True(result.Success, result.Message);
            if (index == 0) exactSendQueryCount = counter.Count;
        }));

        var groupOffset = 0;
        foreach (var concurrency in new[] { 2, 4, 8 })
        {
            var requestCount = concurrency * 10;
            var groupIds = seed.ConcurrencyGroupIds.Skip(groupOffset).Take(requestCount).ToArray();
            groupOffset += requestCount;
            phases.Add(await RunPhaseAsync($"fake-send-concurrency-{concurrency}", requestCount, concurrency, async index =>
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IEndOfDayFillService>();
                var groupId = groupIds[index];
                var preview = await service.GetPreviewAsync(ApplicationAreas.OwnerEmail, groupId, default);
                var result = await service.SendAsync(ApplicationAreas.OwnerEmail, new EndOfDayFillSendForm
                {
                    GroupId = groupId,
                    PreviewToken = preview.PreviewToken!,
                    PhysicalCountConfirmed = true
                }, default);
                Assert.True(result.Success, result.Message);
            }));
        }

        var recoveryQueryCount = 0;
        phases.Add(await RunPhaseAsync("stale-recovery-sequential-100", 100, 1, async index =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var counter = scope.ServiceProvider.GetRequiredService<IPerformanceQueryCounter>();
            counter.Reset();
            var service = scope.ServiceProvider.GetRequiredService<IEndOfDayFillAdminService>();
            var error = await service.ResolvePendingSendAsync(new EndOfDayFillRecoveryForm
            {
                SendAttemptId = seed.StaleAttemptIds[index],
                Resolution = "confirmed-not-sent",
                Reason = "Disposable benchmark verified as not delivered.",
                Confirmed = true
            }, ApplicationAreas.OwnerEmail, default);
            Assert.Null(error);
            if (index == 0) recoveryQueryCount = counter.Count;
        }));

        Assert.All(phases.Where(x => x.Concurrency == 8), x => Assert.True(x.PeakWorkingSetBytes < 384L * 1024 * 1024));
        Assert.True(phases[^1].PostIdleWorkingSetBytes <= phases.Max(x => x.PeakWorkingSetBytes));
        var report = new { ExactSendEfCommandCount = exactSendQueryCount, RecoveryEfCommandCount = recoveryQueryCount, FakeMessages = factory.Sender.Count, Phases = phases };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<SeedResult> SeedAsync(IServiceProvider services, int concurrentGroups)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email.ToLower() == ApplicationAreas.OwnerEmail);
        var primary = await db.EndOfDayFillReportGroups.SingleAsync(x => x.Name == "WP End of Day Fill");
        var warehouseId = await db.EndOfDayFillReportGroupRooms
            .Where(x => x.ReportGroupId == primary.Id)
            .Select(x => x.Room.WarehouseId)
            .FirstAsync();
        var rooms = Enumerable.Range(1, concurrentGroups).Select(index => new Room
        {
            WarehouseId = warehouseId,
            Code = $"EOD-BENCH-{index:D3}",
            Name = $"Disposable EOD benchmark room {index:D3}",
            DisplayName = $"Disposable EOD benchmark room {index:D3}",
            CapacityBins = 1,
            SortOrder = 10000 + index,
            IsActive = true
        }).ToList();
        db.Rooms.AddRange(rooms);
        var groups = Enumerable.Range(1, concurrentGroups).Select(index => new EndOfDayFillReportGroup
        {
            Name = $"EOD disposable benchmark {index:D3}",
            Facility = "WP",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }).ToList();
        db.EndOfDayFillReportGroups.AddRange(groups);
        await db.SaveChangesAsync();
        for (var index = 0; index < groups.Count; index++)
        {
            db.EndOfDayFillReportGroupRooms.Add(new EndOfDayFillReportGroupRoom { ReportGroupId = groups[index].Id, RoomId = rooms[index].Id, CreatedAt = DateTimeOffset.UtcNow });
            db.EndOfDayFillUserGroupAssignments.Add(new EndOfDayFillUserGroupAssignment { UserId = user.Id, ReportGroupId = groups[index].Id, CreatedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();

        var staleAttempts = new List<EndOfDayFillReportSend>();
        foreach (var group in groups.Take(100))
        {
            var attempt = new EndOfDayFillReportSend
            {
                ReportGroupId = group.Id,
                ReportGroupName = group.Name,
                Facility = "WP",
                PacificReportDate = DateOnly.FromDateTime(DateTime.UtcNow),
                RevisionNumber = 0,
                SenderUserId = user.Id,
                SenderEmail = user.Email,
                SenderDisplayName = user.DisplayName,
                RecipientsJson = "[]",
                PhysicalCountConfirmed = true,
                SnapshotHash = new string('a', 64),
                SnapshotJson = "{}",
                Subject = "Disposable stale benchmark",
                HtmlBody = "<p>benchmark</p>",
                TextBody = "benchmark",
                Status = EndOfDayFillSendStatuses.Pending,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                AttemptedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
            };
            staleAttempts.Add(attempt);
            db.EndOfDayFillReportSends.Add(attempt);
        }
        await db.SaveChangesAsync();
        foreach (var attempt in staleAttempts)
            db.EndOfDayFillSendReservations.Add(new EndOfDayFillSendReservation { ReportGroupId = attempt.ReportGroupId, PacificReportDate = attempt.PacificReportDate, RevisionNumber = 0, SnapshotHash = attempt.SnapshotHash, SendAttemptId = attempt.Id, CreatedAt = attempt.CreatedAt });
        await db.SaveChangesAsync();
        return new(primary.Id, groups.Skip(100).Select(x => x.Id).ToArray(), staleAttempts.Select(x => x.Id).ToArray());
    }

    private static async Task<Phase> RunPhaseAsync(string name, int requests, int concurrency, Func<int, Task> action)
    {
        ForceCollection();
        var process = Process.GetCurrentProcess(); process.Refresh();
        var startWorkingSet = process.WorkingSet64;
        var startAllocated = GC.GetTotalAllocatedBytes(true);
        var peak = startWorkingSet;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () => { while (!sampling.IsCancellationRequested) { process.Refresh(); Max(ref peak, process.WorkingSet64); await Task.Delay(20, sampling.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); } });
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, requests).Select(async index => { await gate.WaitAsync(); try { await action(index); } finally { gate.Release(); } }));
        stopwatch.Stop(); sampling.Cancel(); await sampler.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        process.Refresh(); var end = process.WorkingSet64; ForceCollection(); await Task.Delay(500); process.Refresh();
        return new(name, requests, concurrency, stopwatch.Elapsed.TotalMilliseconds, startWorkingSet, end, peak, process.WorkingSet64, GC.GetTotalAllocatedBytes(true) - startAllocated);
    }

    private static void Max(ref long target, long value) { var current = Interlocked.Read(ref target); while (value > current) { var observed = Interlocked.CompareExchange(ref target, value, current); if (observed == current) return; current = observed; } }
    private static void ForceCollection() { GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true); }

    private sealed class BenchmarkFactory(string databaseUrl) : WebApplicationFactory<Program>
    {
        public FakeEmailSender Sender { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_PROVIDER"] = "PostgreSql",
                ["ConnectionStrings:CropQc"] = databaseUrl,
                ["Database:EnsureCreatedOnStartup"] = "false",
                ["Database:SeedMasterDataOnStartup"] = "false",
                ["Backups:Enabled"] = "false",
                ["EbsDailyBinsEmail:Enabled"] = "false",
                ["Email:Provider"] = EmailProviders.GmailUser,
                ["FileStorage:Provider"] = "Local",
                ["DataProtection:PersistKeysToFileSystem"] = "false",
                ["PerformanceDiagnostics:Enabled"] = "true",
                ["PerformanceDiagnostics:EfQueryCountingEnabled"] = "true",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<PerformanceDiagnosticsOptions>();
                services.AddSingleton(new PerformanceDiagnosticsOptions
                {
                    Enabled = true,
                    EfQueryCountingEnabled = true
                });
                services.RemoveAll<DbContextOptions<CropQcDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>(); services.RemoveAll<CropQcDbContext>();
                services.AddDbContext<CropQcDbContext>((provider, options) => { CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, databaseUrl, sql => sql.CommandTimeout(3)); options.AddInterceptors(provider.GetRequiredService<PerformanceDbCommandInterceptor>()); });
                services.RemoveAll<IQcEmailSender>(); services.AddSingleton<IQcEmailSender>(Sender);
                foreach (var hosted in services.Where(x => x.ServiceType == typeof(IHostedService)).ToList()) services.Remove(hosted);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        }
    }

    private sealed class FakeEmailSender : IQcEmailSender
    {
        private int count; public int Count => count;
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) { Interlocked.Increment(ref count); return Task.FromResult(QcEmailSendResult.Sent($"fake-{count}")); }
    }

    private sealed record SeedResult(int PrimaryGroupId, int[] ConcurrencyGroupIds, long[] StaleAttemptIds);
    private sealed record Phase(string Name, int Requests, int Concurrency, double ElapsedMilliseconds, long StartWorkingSetBytes, long EndWorkingSetBytes, long PeakWorkingSetBytes, long PostIdleWorkingSetBytes, long TotalAllocatedBytes);
}
