using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

// UNAPPROVED REHEARSAL: forward currently stops at mixed-cohort dispatch.
// Later phases are prepared, not validated. See docs/investigations/pr271-compensation-rollback.md.
// This executable is test instrumentation, never an application/production entrypoint.
// Run beside the frozen application's DLLs using its deps/runtimeconfig, not rebuilt Data DLLs.
if (args.Length != 2 || args[0] is not ("forward" or "rollback" or "verify"))
    throw new ArgumentException("forward|rollback|verify evidence-directory");
const string connection = "Host=localhost;Database=pr271_rollback_test;Username=postgres;Pooling=false";
if (!File.Exists(Path.Combine(args[1], "isolated-only.marker"))) throw new Exception("Missing isolated rehearsal marker");
ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection);
var role = args[0]; var output = args[1];
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var factory = new Factory();
await using var db = factory.CreateDbContext();
var executor = new InventoryCommandExecutor(factory);
var results = new List<InventoryCommandResult>();
var statePath = Path.Combine(output, "state.json");
var state = role == "forward" ? new State() : JsonSerializer.Deserialize<State>(File.ReadAllText(statePath), json)!;
if (role == "forward")
{
    Check(!await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE schemaname='public' AND tablename='ReceiptCustodyAcknowledgments'").AnyAsync(x => x > 0), "Restore must start before custody schema");
    var columns = await Columns();
    var original = await HashAll(columns);
    var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    Check(migrations.Contains("20261009150448_IsolatedInventoryMovementCohorts"), "Expected CohortKey migration already applied");
    var previous = "20261002031709_BoundedBackupSnapshotProgress";
    foreach (var migration in new[] { "20261007212154_ReceiptHeldTransferCustody", "20261007235352_ReceiptCustodyCompensations", "20261007235822_ReceiptCustodyGuardrails" })
    {
        Check(!migrations.Contains(migration), "Unexpected partial custody schema; stop rather than improvise");
        await db.Database.ExecuteSqlRawAsync(db.GetService<IMigrator>().GenerateScript(previous, migration));
        previous = migration;
    }
    Check(Equal(original, await HashAll(columns)), "Additive migration changed original columns/rows");
    File.WriteAllText(Path.Combine(output, "migration.json"), JsonSerializer.Serialize(new { OriginalTables = original.Count, OriginalDataPreserved = true, Applied = await db.Database.GetAppliedMigrationsAsync() }, json));
    state.Protected = await Protect();
    state.Actor = await db.Users.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
    state.SourceWarehouse = await db.Warehouses.Where(x => x.Code == "WP").Select(x => x.Id).SingleAsync();
    state.DestinationWarehouse = await db.Warehouses.Where(x => x.Code == "EBS").Select(x => x.Id).SingleAsync();
    state.Profile = await db.FruitProfiles.Where(x => x.IsActive && x.VarietyCode == "FUJI" && x.ProductionType == "Conventional" && !x.IsOrganic).Select(x => x.Id).FirstAsync();
    Check(!await db.Rooms.AnyAsync(x => x.Id >= 990001) && !await db.GrowerLots.AnyAsync(x => x.Id >= 990001), "Fixture IDs collide");
    db.Rooms.AddRange(new Room { Id = 990001, WarehouseId = state.SourceWarehouse, Code = "R271SRC", Name = "Isolated rollback source" },
        new Room { Id = 990002, WarehouseId = state.DestinationWarehouse, Code = "R271DST", Name = "Isolated rollback destination" },
        new Room { Id = 990003, WarehouseId = state.DestinationWarehouse, Code = "R271NEXT", Name = "Isolated rollback replacement" },
        new Room { Id = 990004, WarehouseId = state.DestinationWarehouse, Code = "R271SEAL", Name = "Isolated sealed room", IsSealed = true });
    db.GrowerLots.AddRange(new GrowerLot { Id = 990001, Grower = "Isolated rollback", LotNumber = "R271-MIXED" }, new GrowerLot { Id = 990002, Grower = "Isolated rollback", LotNumber = "R271-EXTRA" });
    await db.SaveChangesAsync();
    state.TreatedReceipt = await Receive("r271-treated", 990001, 990001, state.SourceWarehouse, 20);
    var chemical = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Receiving" && x.Crop == "Apples").Select(x => x.Id).FirstAsync();
    var treatment = await Movement(InventoryCommandKind.ReceiptTreatmentAssignment, 990001, 990001, 20, "u", "r271-treatment");
    var applied = await Commit(treatment with { TreatmentChemicalId = chemical, Lines = [treatment.Lines[0] with { ReceiptId = state.TreatedReceipt }] });
    state.Application = applied.Effects.Single().ParentId!.Value;
    state.UntreatedReceipt = await Receive("r271-later-untreated", 990001, 990001, state.SourceWarehouse, 20);
    var treated = await Movement(InventoryCommandKind.InterCompanyDispatch, 990001, 990001, 15, $"u|a:{state.Application}", "r271-dispatch");
    var untreated = await Movement(InventoryCommandKind.InterCompanyDispatch, 990001, 990001, 15, "u", "r271-unused");
    state.Transfer = (await Commit(treated with { Lines = [treated.Lines[0], untreated.Lines[0]], CustodyGroup = "EBS" })).Effects[0].ParentId!.Value;
    db.ChangeTracker.Clear();
    var receipt = new Receipt
    {
        CropYear = 2026,
        CompuTechReceiptId = "R271-EVIDENCE",
        ReceiptType = "Truck receipt",
        IsTransferReceipt = true,
        WarehouseId = state.DestinationWarehouse,
        RoomId = 990002,
        FruitProfileId = state.Profile,
        GrowerLotId = 990001,
        GrowerName = "Isolated rollback",
        LotCode = "TRANSFER",
        BinCount = 30,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    receipt.VarietyLines.Add(new() { FruitProfileId = state.Profile, BinCount = 30 });
    var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == state.Transfer);
    transfer.ReceivingReceipt = receipt; transfer.ConcurrencyVersion++;
    await db.SaveChangesAsync(); state.Receipt = receipt.Id;
    state.TreatedDispatch = await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == state.Transfer && x.MovementType == "InterCrewDispatch" && x.ReceiptId == state.TreatedReceipt).Select(x => x.Id).SingleAsync();
    state.UntreatedDispatch = await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == state.Transfer && x.MovementType == "InterCrewDispatch" && x.ReceiptId == state.UntreatedReceipt).Select(x => x.Id).SingleAsync();
    await Commit(await Custody(InventoryCommandKind.AcknowledgeTransfer, [new(state.TreatedDispatch, 12), new(state.UntreatedDispatch, 10)], "r271-ack"));
    state.TreatedAck = await db.ReceiptCustodyAcknowledgments.Where(x => x.DispatchMovementId == state.TreatedDispatch).Select(x => x.Id).SingleAsync();
    state.UntreatedAck = await db.ReceiptCustodyAcknowledgments.Where(x => x.DispatchMovementId == state.UntreatedDispatch).Select(x => x.Id).SingleAsync();
    await Commit(await Custody(InventoryCommandKind.PlaceReceiptCustody, [new(state.TreatedAck, 8), new(state.UntreatedAck, 6)], "r271-place"));
    state.TreatedPlacement = await db.ReceiptCustodyPlacements.Where(x => x.AcknowledgmentId == state.TreatedAck).Select(x => x.Id).SingleAsync();
    await Commit(await Custody(InventoryCommandKind.ReverseReceiptAcknowledgment, [new(state.UntreatedAck, 2)], "r271-undo-ack"));
    await Commit(await Custody(InventoryCommandKind.ReverseReceiptPlacement, [new(state.TreatedPlacement, 3)], "r271-undo-place"));
    await Commit(await Custody(InventoryCommandKind.PlaceReceiptCustody, [new(state.TreatedAck, 2)], "r271-replace"));
    await Prove(20, 13);
    await Guards();
    await CorrectHistorical(34, "r271-original-correction");
    state.ExtraReceipt = await Receive("r271-depleted", 990002, 990003, state.DestinationWarehouse, 4);
    await Commit(await Movement(InventoryCommandKind.Loss, 990003, 990002, 4, "u", "r271-deplete"));
    var extra = (await new InventoryReceiptAvailability(db).ReadAsync(state.ExtraReceipt, default))!;
    var destination = await Position(990003, 990002);
    var additional = new InventoryCommand("r271-additional", InventoryCommandKind.CorrectReceiptQuantity, state.Actor, DateTimeOffset.UtcNow,
        "Disposable separately authorized additional origin", [], ReceiptChange: new(state.ExtraReceipt, extra.Receipt.ConcurrencyVersion, extra.Fingerprint, 6, [],
            ConfirmAdditionalBinsUntreated: true, AdditionalStock: new(990003, destination.Watermark.Fingerprint, true)));
    await Commit(additional);
    File.WriteAllText(Path.Combine(output, "additional-intent.json"), JsonSerializer.Serialize(additional, json));
    File.WriteAllText(Path.Combine(output, "ordinary-intent.json"), JsonSerializer.Serialize(await Movement(InventoryCommandKind.RoomMove, 990003, 990002, 1, "u", "r271-old-binary-probe", 990002), json));
    state.ForwardImmutable = await Protect(immutableOnly: true);
    state.Handover = await HashAll(await Columns());
    File.WriteAllText(statePath, JsonSerializer.Serialize(state, json));
}
else if (role == "rollback")
{
    Check(Equal(state.Handover, await HashAll(await Columns())), "Database changed during application handover");
    await Prove(20, 13);
    var additional = JsonSerializer.Deserialize<InventoryCommand>(File.ReadAllText(Path.Combine(output, "additional-intent.json")), json)!;
    var beforeReplay = await HashAll(await Columns());
    Check((await executor.ExecuteAsync(additional)).Status == InventoryCommandStatus.Replayed, "Forward AdditionalStock intent must replay under rollback");
    Check(Equal(beforeReplay, await HashAll(await Columns())), "Cross-version replay wrote data");
    var extra = (await new InventoryReceiptAvailability(db).ReadAsync(state.ExtraReceipt, default))!;
    Check(extra.RoomQuantity == 2 && extra.Blocker == null, "Rollback cannot read recognized additional origin");
    // A new unsupported intent must fail without losing any committed origin or journal.
    await Reject(additional with { OperationKey = "r271-fresh-unsupported", ReceiptChange = additional.ReceiptChange! with { ExpectedVersion = extra.Receipt.ConcurrencyVersion, ExpectedFingerprint = extra.Fingerprint, NewQuantity = 7 } }, InventoryCommandStatus.Blocked);
    var race = await Custody(InventoryCommandKind.ReverseReceiptAcknowledgment, [new(state.UntreatedAck, 1)], "r271-race-a");
    var count = 0; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var observer = new Observer(async (stage, attempt) => { if (stage == "Resolved" && attempt == 1) { if (Interlocked.Increment(ref count) == 2) gate.SetResult(); await gate.Task.WaitAsync(TimeSpan.FromSeconds(30)); } });
    var raceOther = race with { OperationKey = "r271-race-b" };
    var outcomes = await Task.WhenAll(new InventoryCommandExecutor(factory, observer).ExecuteAsync(race), new InventoryCommandExecutor(factory, observer).ExecuteAsync(raceOther));
    Check(outcomes.Count(x => x.Status == InventoryCommandStatus.Committed) == 1 && outcomes.Count(x => x.Status == InventoryCommandStatus.Stale) == 1, "Concurrent compensation must commit once, reject stale competitor");
    results.AddRange(outcomes);
    var winner = outcomes[0].Status == InventoryCommandStatus.Committed ? race : raceOther;
    var saved = await HashAll(await Columns());
    Check((await executor.ExecuteAsync(winner)).Status == InventoryCommandStatus.Replayed, "Compensation retry must replay");
    Check(Equal(saved, await HashAll(await Columns())), "Compensation replay changed history");
    await Commit(await Custody(InventoryCommandKind.ReverseReceiptPlacement, [new(state.TreatedPlacement, 1)], "r271-rollback-unplace"));
    await Commit(await Custody(InventoryCommandKind.PlaceReceiptCustody, [new(state.TreatedAck, 1)], "r271-rollback-replace", 990003));
    await Commit(await Custody(InventoryCommandKind.AcknowledgeTransfer, [new(state.UntreatedDispatch, 1)], "r271-rollback-ack"));
    var newAck = await db.ReceiptCustodyAcknowledgments.Where(x => x.OperationKey == "r271-rollback-ack").Select(x => x.Id).SingleAsync();
    await Commit(await Custody(InventoryCommandKind.PlaceReceiptCustody, [new(newAck, 1)], "r271-rollback-new-place"));
    await Prove(20, 14);
    await Guards();
    await CorrectHistorical(35, "r271-rollback-historical-correction");
    await Commit(await Movement(InventoryCommandKind.RoomMove, 990003, 990002, 1, "u", "r271-rollback-move-recognized", 990002));
    await VerifyProtected(state.ForwardImmutable);
    state.Handover = await HashAll(await Columns());
    File.WriteAllText(statePath, JsonSerializer.Serialize(state, json));
}
else
{
    Check(Equal(state.Handover, await HashAll(await Columns())), "Rollback final data changed before independent forward verification");
    await Prove(20, 14);
    await VerifyProtected(state.ForwardImmutable);
}
await VerifyProtected(state.Protected);
var binaries = new[] { typeof(CropQcDbContext).Assembly.Location, typeof(InventoryCommand).Assembly.Location, typeof(CropQc.Web.Services.TruckReceiptReconciliationService).Assembly.Location }
    .ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
File.WriteAllText(Path.Combine(output, role + "-result.json"), JsonSerializer.Serialize(new
{
    Role = role,
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    Binaries = binaries,
    Operations = results,
    HistoricalTablesPreserved = state.Protected.Count,
    ForwardImmutableTablesPreserved = state.ForwardImmutable.Count,
    Custody = await CustodySummary(),
    OriginalTreatmentQuantity = await db.RoomTreatmentApplicationSources.Where(x => x.RoomTreatmentApplicationId == state.Application).SumAsync(x => x.BinsTreated),
    Passed = true
}, json));
Console.WriteLine($"{role} PASS; {results.Count} operation results; original history preserved; exclusive custody and treatment verified");

async Task<InventoryAvailabilityResult> Position(int room, int lot)
{
    await using var read = factory.CreateDbContext();
    return (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(read)).ResolveAsync(new(null, [room]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions.Single(x => x.Identity.GrowerLotId == lot);
}
async Task<InventoryCommand> Movement(InventoryCommandKind kind, int room, int lot, int quantity, string signature, string key, int? destination = null)
{
    var p = await Position(room, lot);
    return new(key, kind, state.Actor, DateTimeOffset.UtcNow, "Disposable rollback transaction rehearsal", [new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), quantity, signature, destination is int id ? new(state.DestinationWarehouse, id) : null)]);
}
async Task<long> Receive(string key, int lot, int room, int warehouse, int quantity) => (await Commit(new(key, InventoryCommandKind.ReceiveStock, state.Actor, DateTimeOffset.UtcNow,
    "Disposable recorded origin", [], Receipt: new(2026, warehouse, room, lot, state.Profile, key, quantity)))).Effects.Single().ParentId!.Value;
async Task<InventoryCommand> Custody(InventoryCommandKind kind, ImmutableArray<InventoryCustodyQuantity> allocations, string key, int room = 990002)
{
    var t = await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == state.Transfer);
    var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == state.Receipt);
    return new(key, kind, state.Actor, DateTimeOffset.UtcNow, "Disposable exact custody correction", [], ReceiptCustody: new(t.Id, r.Id, t.ConcurrencyVersion, r.ConcurrencyVersion,
        allocations, kind == InventoryCommandKind.PlaceReceiptCustody ? new(state.DestinationWarehouse, room) : null));
}
async Task<InventoryCommandResult> Commit(InventoryCommand command)
{
    var result = await executor.ExecuteAsync(command); Check(result.Status == InventoryCommandStatus.Committed, command.OperationKey + ": " + result.Detail);
    results.Add(result); db.ChangeTracker.Clear(); return result;
}
async Task Reject(InventoryCommand command, InventoryCommandStatus expected)
{
    var before = await HashAll(await Columns()); var result = await executor.ExecuteAsync(command); results.Add(result);
    Check(result.Status == expected, command.OperationKey + " unexpected " + result.Status + ": " + result.Detail);
    Check(Equal(before, await HashAll(await Columns())), "Rejected command changed data");
}
async Task Guards()
{
    await Reject(await Custody(InventoryCommandKind.ReverseReceiptAcknowledgment, [new(state.TreatedAck, 100)], role + "-overdraw"), InventoryCommandStatus.Blocked);
    await Reject(await Custody(InventoryCommandKind.PlaceReceiptCustody, [new(state.TreatedAck, 1)], role + "-sealed", 990004), InventoryCommandStatus.Blocked);
    var wrong = await Custody(InventoryCommandKind.PlaceReceiptCustody, [new(state.TreatedAck, 1)], role + "-wrong-company");
    await Reject(wrong with { ReceiptCustody = wrong.ReceiptCustody! with { Destination = new(state.SourceWarehouse, 990001) } }, InventoryCommandStatus.Blocked);
    var before = await HashAll(await Columns());
    try { await Sql("UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=\"ChangeAmount\" WHERE \"Id\"=(SELECT min(\"Id\") FROM \"RoomInventoryAdjustments\")"); throw new Exception("Writer fence accepted legacy SQL"); }
    catch (PostgresException e) { Check(e.MessageText.Contains("compensation", StringComparison.OrdinalIgnoreCase) || e.MessageText.Contains("custody", StringComparison.OrdinalIgnoreCase), "Wrong SQL failure: " + e.MessageText); }
    Check(Equal(before, await HashAll(await Columns())), "Rejected unfenced writer changed data");
}
async Task CorrectHistorical(int quantity, string key)
{
    var s = (await new InventoryReceiptAvailability(db).ReadAsync(2475, default))!;
    Check(s.Blocker == null && s.RoomQuantity == s.Receipt.BinCount, "Historical receipt exact custody not proven");
    await Commit(new(key, InventoryCommandKind.CorrectReceiptQuantity, state.Actor, DateTimeOffset.UtcNow, "Disposable existing historical correction", [],
        ReceiptChange: new(2475, s.Receipt.ConcurrencyVersion, s.Fingerprint, quantity, [new(s.Allocations.Single().Key, Math.Abs(quantity - s.Receipt.BinCount))], ConfirmAdditionalBinsUntreated: true)));
    var correction = await db.ReceiptInventoryOverrides.AsNoTracking().SingleAsync(x => x.OperationKey == key);
    Check(correction.IsComplete && correction.NewReceiptBinCount == quantity && !string.IsNullOrWhiteSpace(correction.BeforeReceiptSnapshotJson), "Correction audit incomplete");
}
async Task<object> CustodySummary()
{
    var acks = await db.ReceiptCustodyAcknowledgments.WithCustodyEvidence().AsNoTracking().Where(x => x.InterCrewTransferId == state.Transfer).ToArrayAsync();
    return new { Dispatched = 30, Acknowledged = acks.Sum(x => x.NetQuantity), Placed = acks.Sum(x => x.PlacedQuantity), Held = acks.Sum(x => x.HeldQuantity), Transit = 30 - acks.Sum(x => x.NetQuantity) };
}
async Task Prove(int acknowledged, int placed)
{
    var before = await HashAll(await Columns());
    var acks = await db.ReceiptCustodyAcknowledgments.WithCustodyEvidence().AsNoTracking().Where(x => x.InterCrewTransferId == state.Transfer).ToArrayAsync();
    Check(acks.All(ReceiptCustodyProof.Valid) && acks.Sum(x => x.NetQuantity) == acknowledged && acks.Sum(x => x.PlacedQuantity) == placed, "Invalid compensated custody");
    var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
    var held = await resolver.ResolveAsync(new(state.DestinationWarehouse, [], InventoryCustody.ReceiptHeld, state.Receipt), new(AllowedCustody: InventoryCustody.ReceiptHeld), DateTimeOffset.UtcNow);
    var transit = await resolver.ResolveAsync(new(state.SourceWarehouse, [], InventoryCustody.InTransit, state.Transfer), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
    Check(held.Positions.Sum(x => x.AuthoritativeQuantity) == acknowledged - placed && transit.Positions.Sum(x => x.AuthoritativeQuantity) == 30 - acknowledged, "Held/transit readers duplicate or lose custody");
    Check(held.Positions.Concat(transit.Positions).Where(x => x.AuthoritativeQuantity > 0).All(x => x.IsOperable), "Custody treatment proof failed");
    var roomQuantity = await db.RoomInventoryAdjustments.Where(x => x.GrowerLotId == 990001).SumAsync(x => x.ChangeAmount);
    Check(roomQuantity + acknowledged - placed + 30 - acknowledged == 40, "Room + held + transit does not conserve 40 origins");
    var segments = await db.TreatmentLineageSegments.Include(x => x.Applications).AsNoTracking().Where(x => x.GrowerLotId == 990001 && x.Disposition == "Current" && x.CurrentBins > 0).ToArrayAsync();
    Check(segments.All(x => x.ReceiptId == state.TreatedReceipt ? x.TreatmentSignature == $"u|a:{state.Application}" && x.Applications.Select(a => a.RoomTreatmentApplicationId).SequenceEqual([state.Application]) : x.ReceiptId == state.UntreatedReceipt && x.TreatmentSignature == "u" && x.Applications.Count == 0), "Treatment leaked or receipt ancestry changed");
    Check(await db.RoomTreatmentApplicationSources.Where(x => x.RoomTreatmentApplicationId == state.Application).SumAsync(x => x.BinsTreated) == 20, "Later arrival inherited earlier application");
    Check(Equal(before, await HashAll(await Columns())), "Read/verification path mutated history");
}
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static bool Equal(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(x => b.GetValueOrDefault(x.Key) == x.Value);
static string Q(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
async Task<string> Sql(string sql)
{
    await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 }; return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
}
async Task<Dictionary<string, string>> Columns()
{
    var value = await Sql("SELECT json_object_agg(t,c)::text FROM (SELECT table_name t,string_agg(quote_ident(column_name),',' ORDER BY ordinal_position) c FROM information_schema.columns WHERE table_schema='public' AND table_name <> '__EFMigrationsHistory' GROUP BY table_name) s");
    return JsonSerializer.Deserialize<Dictionary<string, string>>(value)!;
}
async Task<Dictionary<string, string>> HashAll(Dictionary<string, string> columns)
{
    var result = new Dictionary<string, string>();
    foreach (var item in columns) result[item.Key] = await Sql($"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) FROM (SELECT {item.Value} FROM {Q(item.Key)}) t");
    return result;
}
async Task<Dictionary<string, Protected>> Protect(bool immutableOnly = false)
{
    string[] names = ["Receipts", "RoomInventoryAdjustments", "TreatmentLineageMovements", "TreatmentLineageSegments", "RoomTreatmentApplications", "RoomTreatmentApplicationSources", "InterCrewTransfers", "BinsRunEntries", "ActualRuns", "ActualRunRevisions", "RoomInventoryLosses", "RoomDepletions", "AuditLogs", "ReceiptCustodyAcknowledgments", "ReceiptCustodyPlacements", "ReceiptCustodyReversals"];
    var result = new Dictionary<string, Protected>();
    foreach (var table in names.Where(x => !immutableOnly || x is not ("Receipts" or "TreatmentLineageSegments" or "InterCrewTransfers")))
    {
        var max = await Sql($"SELECT coalesce(max(\"Id\"),0)::text FROM {Q(table)}");
        var filter = $"\"Id\" <= {max}" + (table == "Receipts" ? " AND \"Id\" <> 2475" : table == "TreatmentLineageSegments" ? " AND \"Id\" <> 768" : "");
        result[table] = new(filter, await Hash(table, filter));
    }
    return result;
}
Task<string> Hash(string table, string filter) => Sql($"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) FROM {Q(table)} t WHERE {filter}");
async Task VerifyProtected(Dictionary<string, Protected> protection) { foreach (var item in protection) Check(await Hash(item.Key, item.Value.Filter) == item.Value.Hash, "Protected history changed: " + item.Key); }
sealed record Protected(string Filter, string Hash);
sealed class State
{
    public int Actor { get; set; }
    public int SourceWarehouse { get; set; }
    public int DestinationWarehouse { get; set; }
    public int Profile { get; set; }
    public long TreatedReceipt { get; set; }
    public long UntreatedReceipt { get; set; }
    public long ExtraReceipt { get; set; }
    public long Transfer { get; set; }
    public long Receipt { get; set; }
    public long Application { get; set; }
    public long TreatedDispatch { get; set; }
    public long UntreatedDispatch { get; set; }
    public long TreatedAck { get; set; }
    public long UntreatedAck { get; set; }
    public long TreatedPlacement { get; set; }
    public Dictionary<string, Protected> Protected { get; set; } = []; public Dictionary<string, Protected> ForwardImmutable { get; set; } = []; public Dictionary<string, string> Handover { get; set; } = [];
}
sealed class Factory : IDbContextFactory<CropQcDbContext>
{
    public CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>(); CropQcDatabase.Configure(options, "PostgreSql", "Host=localhost;Database=pr271_rollback_test;Username=postgres;Pooling=false");
        return new(options.Options, new CanonicalInventoryMode(true));
    }
}
sealed class Observer(Func<string, int, Task> action) : IInventoryCommandObserver { public Task AtAsync(string stage, int attempt, CancellationToken ct) => action(stage, attempt); }
