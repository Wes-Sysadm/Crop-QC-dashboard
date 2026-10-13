using System.Security.Cryptography;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

// Explicit local-only rehearsal. Never starts Web, background jobs or remote integrations.
if (args.Length != 2 || args[0] is not ("candidate" or "rollback")) throw new ArgumentException("candidate|rollback output.json");
var connection = Environment.GetEnvironmentVariable("MOVEMENT_ROLLBACK_TEST_POSTGRES") ?? throw new Exception("Missing disposable connection");
var builder = new NpgsqlConnectionStringBuilder(connection);
if (builder.Host != "127.0.0.1" || builder.Port != 55440 || builder.Database != "pr278_rollback_test")
    throw new Exception("Only the named disposable localhost clone is accepted");
var factory = new Factory(connection);
await using var db = factory.CreateDbContext();
if (args[0] == "candidate")
{
    var sql = db.GetService<IMigrator>().GenerateScript("20261002031709_BoundedBackupSnapshotProgress", "20261009150448_IsolatedInventoryMovementCohorts");
    await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync();
    await using var command = new NpgsqlCommand(sql, conn); await command.ExecuteNonQueryAsync();
}
var original = await Protect();
var originalTotal = await db.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount);
var actor = await db.Users.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
var executor = new InventoryCommandExecutor(factory);
var operations = new List<InventoryCommandResult>();
if (args[0] == "candidate")
{
    var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Receiving" && x.Crop == "Apples");
    var treatment = await Make(InventoryCommandKind.ReceiptTreatmentAssignment, 55, 34, "u", null, "pr278-rehearsal-treatment");
    treatment = treatment with { TreatmentChemicalId = chemical.Id, Lines = [treatment.Lines[0] with { ReceiptId = 2472 }] };
    await Execute(treatment);
    var received = await new CanonicalReceivingService(db, executor).ReceiveAsync("pr278-rehearsal-origin", actor, 2026,
        DateTimeOffset.UtcNow, 3, 55, 14, 430, "", "PR278-ISOLATED-ONLY", 10, "Disposable rollback rehearsal origin", default);
    Check(received.Status == InventoryCommandStatus.Committed, received.Detail); operations.Add(received);
    var app = operations[0].Effects.Single().ParentId!.Value;
    await Execute(await Make(InventoryCommandKind.RoomMove, 55, 10, "u", 73, "pr278-rehearsal-incoming-untreated"));
    await Execute(await Make(InventoryCommandKind.RoomMove, 55, 12, $"u|a:{app}", 73, "pr278-rehearsal-incoming-treated"));
}
else
{
    Check(await db.TreatmentLineageSegments.AnyAsync(x => x.CohortKey != ""), "Candidate cohort rows must already exist");
    var app = await db.RoomTreatmentApplications.SingleAsync(x => x.OperationKey == "pr278-rehearsal-treatment:t:55");
    var move = await Make(InventoryCommandKind.RoomMove, 73, 5, "u", 55, "pr278-rollback-untreated");
    await Execute(move);
    var after = await SnapshotAll();
    var replay = await executor.ExecuteAsync(move);
    Check(replay.Status == InventoryCommandStatus.Replayed && after == await SnapshotAll(), "Retry changed recorded history");
    var stale = await executor.ExecuteAsync(move with { OperationKey = "pr278-rollback-stale" });
    Check(stale.Status == InventoryCommandStatus.Stale && after == await SnapshotAll(), "Stale movement changed recorded history");
    operations.Add(replay); operations.Add(stale);
    await Execute(await Make(InventoryCommandKind.RoomMove, 73, 7, $"u|a:{app.Id}", 55, "pr278-rollback-treated"));
}
var total = await db.RoomInventoryAdjustments.SumAsync(x => (long)x.ChangeAmount);
Check(total == originalTotal + (args[0] == "candidate" ? 10 : 0), "Global quantity changed beyond explicit receiving");
var verified = await Verify(original);
Check(verified.All(x => x.Value), "Protected historical records changed");
var positions = (await new InventoryEvidenceLoader(db).LoadAsync(new(3, [55, 73]), DateTimeOffset.UtcNow, default)).Positions;
var red = positions.Where(x => x.Identity.GrowerLotId == 430 && x.Identity.FruitProfileId == 14).ToArray();
Check(red.Sum(x => x.AuthoritativeQuantity) == 696, "Original 686 plus explicit new receipt 10 must be conserved");
var target = red.Single(x => x.Location.RoomId == 73);
Check(target.AuthoritativeQuantity - target.Projections.Where(x => x.Disposition == "Current").Sum(x => x.Quantity) == 210,
    "The historical 210-bin gap must remain a separate proposal");
Check(await db.RoomTreatmentApplicationSources.Where(x => x.RoomTreatmentApplication.OperationKey == "pr278-rehearsal-treatment:t:55").SumAsync(x => x.BinsTreated) == 34,
    "The later ten bins must not inherit the prior treatment");
Check(positions.Single(x => x.Location.RoomId == 73 && x.Identity.Variety == "ATGL" && x.Identity.Lot == "1242").AuthoritativeQuantity == 63, "Separate ATGL changed");
var binaries = new[] { typeof(CropQcDbContext).Assembly.Location, typeof(InventoryEventReplay).Assembly.Location }
    .ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
File.WriteAllText(args[1], JsonSerializer.Serialize(new
{
    Role = args[0],
    Binaries = binaries,
    OriginalTotal = originalTotal,
    Total = total,
    Operations = operations,
    ProtectedHistory = verified,
    Positions = red.Select(p => InventoryAvailabilityResolver.Resolve(p, new() { AllowIndependentCohorts = true })),
    HistoricalGap = 210,
    SeparateAtgl = 63
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{args[0]} PASS; global {originalTotal}->{total}; original history preserved; RED gap remains 210; ATGL remains 63");

async Task<InventoryCommand> Make(InventoryCommandKind kind, int room, int quantity, string signature, int? destination, string key)
{
    var position = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(3, [room]),
        new(TreatmentSignature: signature) { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions
        .Single(x => x.Identity.GrowerLotId == 430 && x.Identity.FruitProfileId == 14);
    Check(position.IsOperable && position.AvailableQuantity >= quantity, $"Source read failed: {JsonSerializer.Serialize(position.Blockers)}");
    return new(key, kind, actor, DateTimeOffset.UtcNow, "Disposable PR278 rollback rehearsal",
        [new(new(position.Identity, position.Location, position.Watermark.Fingerprint, position.Watermark.Versions), quantity, signature,
            destination is int id ? new(3, id) : null)]);
}
async Task Execute(InventoryCommand command)
{
    var result = await executor.ExecuteAsync(command);
    Check(result.Status == InventoryCommandStatus.Committed, result.Detail); operations.Add(result);
}
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
async Task<Dictionary<string, (long Max, string Hash)>> Protect()
{
    var result = new Dictionary<string, (long, string)>();
    foreach (var table in new[] { "Receipts", "RoomInventoryAdjustments", "TreatmentLineageMovements", "RoomTransfers", "AuditLogs",
        "RoomTreatmentApplications", "RoomTreatmentApplicationSources", "BinsRunEntries", "ActualRuns", "ActualRunRevisions", "RoomInventoryLosses",
        "RoomDepletions", "InterCrewTransfers", "ProcessorShipmentLines", "OutsideWarehouseTransfers" })
    {
        var max = long.Parse(await Sql($"SELECT coalesce(max(\"Id\"),0)::text FROM \"{table}\""));
        result[table] = (max, await Hash(table, max));
    }
    return result;
}
async Task<Dictionary<string, bool>> Verify(Dictionary<string, (long Max, string Hash)> before)
{
    var result = new Dictionary<string, bool>();
    foreach (var (table, original) in before) result[table] = original.Hash == await Hash(table, original.Max);
    return result;
}
Task<string> Hash(string table, long max) => Sql($"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY \"Id\"),'')) FROM \"{table}\" t WHERE \"Id\" <= {max}");
async Task<string> SnapshotAll()
{
    var names = (await Sql("SELECT string_agg(tablename, ',' ORDER BY tablename) FROM pg_tables WHERE schemaname='public'")).Split(',');
    var hashes = new List<string>();
    foreach (var table in names) hashes.Add(await Sql($"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) FROM \"{table}\" t"));
    return string.Join(',', hashes);
}
async Task<string> Sql(string query)
{
    await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync();
    await using var command = new NpgsqlCommand(query, conn); return (string)(await command.ExecuteScalarAsync())!;
}
sealed class Factory(string connection) : IDbContextFactory<CropQcDbContext>
{
    public CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>(); CropQcDatabase.Configure(options, "Postgres", connection);
        return new(options.Options, new CanonicalInventoryMode(true));
    }
}
