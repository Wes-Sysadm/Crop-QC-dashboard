using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;
if (args.Length != 1 || args[0] is not ("forward" or "rollback") || !File.Exists("/proof/isolated-only.marker")) throw new Exception("Named isolated diagnostic only");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var f = new Factory();
await using var db = f.CreateDbContext();
var p = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(null, [990001]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions.Single(x => x.Identity.GrowerLotId == 990001);
var actor = await db.Users.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
var before = await Hash();
object? diagnosis = null;
var observer = new Observer(async stage =>
{
    if (stage != "PersistedSource2") return;
    var current = f.Last!;
    var parent = current.InterCrewTransfers.Local.Single(x => x.OperationKey == "r271-dispatch-probe:load");
    var evidence = await new InventoryEvidenceLoader(current).LoadAsync(new(parent.SourceWarehouseId, [], InventoryCustody.InTransit, parent.Id), DateTimeOffset.UtcNow, default);
    diagnosis = new { parent.Id, parent.BinsLoaded, Positions = evidence.Positions, Resolved = evidence.Positions.Select(x => InventoryAvailabilityResolver.Resolve(x, new(AllowedCustody: InventoryCustody.InTransit))) };
});
var command = new InventoryCommand("r271-dispatch-probe", InventoryCommandKind.InterCompanyDispatch, actor, DateTimeOffset.UtcNow, "Isolated failed-guard diagnosis", p.TreatmentSlices.Select(s => new InventoryCommandLine(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), 15, s.Signature)).ToImmutableArray(), CustodyGroup: "EBS");
var result = await new InventoryCommandExecutor(f, observer).ExecuteAsync(command);
var after = await Hash();
File.WriteAllText("/proof/" + args[0] + "-dispatch-failure.json", JsonSerializer.Serialize(new { Role = args[0], Binaries = new[] { typeof(CropQcDbContext).Assembly.Location, typeof(InventoryCommand).Assembly.Location }.ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))), Before = p, Command = command, Result = result, Diagnosis = diagnosis, AtomicRollback = before == after }, json));
if (result.Status != InventoryCommandStatus.Blocked || diagnosis == null || before != after) throw new Exception("Probe expectation failed");
Console.WriteLine("Blocked dispatch reproduced with transaction-local evidence; atomic rollback verified");
async Task<string> Hash() { await using var c = new NpgsqlConnection(Factory.Connection); await c.OpenAsync(); var values = new List<string>(); foreach (var t in new[] { "Receipts", "RoomInventoryAdjustments", "TreatmentLineageSegments", "TreatmentLineageMovements", "InterCrewTransfers", "InventoryCommands", "AuditLogs" }) { await using var cmd = new NpgsqlCommand($"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) FROM \"{t}\" t", c); values.Add((string)(await cmd.ExecuteScalarAsync())!); } return string.Join(',', values); }
sealed class Factory : IDbContextFactory<CropQcDbContext> { public const string Connection = "Host=localhost;Database=pr271_rollback_test;Username=postgres;Pooling=false"; public CropQcDbContext? Last; public CropQcDbContext CreateDbContext() { var o = new DbContextOptionsBuilder<CropQcDbContext>(); CropQcDatabase.Configure(o, "PostgreSql", Connection); return Last = new(o.Options, new CanonicalInventoryMode(true)); } }
sealed class Observer(Func<string, Task> action) : IInventoryCommandObserver { public Task AtAsync(string stage, int attempt, CancellationToken ct) => action(stage); }
