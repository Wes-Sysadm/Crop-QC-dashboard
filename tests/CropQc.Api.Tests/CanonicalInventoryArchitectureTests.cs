using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Shared.Inventory;
using Xunit.Abstractions;

namespace CropQc.Api.Tests;

/// <summary>Conservative source inventory plus a reviewed-content lock, not a claim
/// that regex proves control flow. Runtime guards and PostgreSQL workflow tests
/// establish the activation boundary. New/changed candidates require explicit review.</summary>
public sealed class CanonicalInventoryArchitectureTests(ITestOutputHelper output)
{
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CropQc.sln"))) return d.FullName;
        throw new InvalidOperationException("Repository source is required for the writer coverage gate.");
    }
    private static T Read<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Root(), "docs/inventory-architecture", name)))!;
    public sealed record Candidate(string Path, string Classification, string Reason, string Sha256);

    [Fact]
    public void Exceptional_repair_writers_are_not_exposed_by_ordinary_controllers()
    {
        var maintenance = Read<Candidate[]>("phase3-reviewed-write-candidates.json")
            .Where(x => x.Classification == "CLI maintenance")
            .Select(x => Path.GetFileNameWithoutExtension(x.Path))
            .Append("Tr508901InventoryRepairService").ToArray();
        foreach (var project in new[] { "CropQc.Web", "CropQc.Api" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src", project, "Controllers"), "*.cs", SearchOption.AllDirectories))
                foreach (var type in maintenance)
                    Assert.DoesNotContain(type, File.ReadAllText(file));
        // The existing Evans repair also has an explicit configuration-triggered
        // maintenance entry point. It is never ordinary inventory traffic and
        // receives no exemption from the canonical DbContext write guard.
        var host = File.ReadAllText(Path.Combine(Root(), "src/CropQc.Web/Services/RuntimeMemoryTelemetry.cs"));
        Assert.Contains("CROPQC_EVANS11_3152_REPAIR_MODE", host);
        Assert.DoesNotContain("CanonicalCommandTransaction", host);
    }

    [Fact]
    public void All_32_original_workflows_have_explicit_adapter_or_nonphysical_classification()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "docs/inventory-architecture/phase3-workflow-registry.json")));
        var entries = json.RootElement.EnumerateArray().ToArray();
        var original = File.ReadAllLines(Path.Combine(Root(), "docs/inventory-architecture/Inventory-architecture-path-matrix.csv")).Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Split(',')[0]).ToArray();
        Assert.Equal(32, entries.Length);
        Assert.Equal(original, entries.Select(x => x.GetProperty("Workflow").GetString()));
        Assert.Equal(Enumerable.Range(1, 32), entries.Select(x => x.GetProperty("Id").GetInt32()));
        foreach (var entry in entries)
        {
            var path = entry.GetProperty("Source").GetString()!;
            Assert.True(File.Exists(Path.Combine(Root(), path)), path);
            var kind = entry.GetProperty("WritePath").GetString();
            Assert.Contains(kind, new[] { "Canonical", "Non-mutating", "Maintenance" });
            foreach (var test in entry.GetProperty("Tests").EnumerateArray()) Assert.True(File.Exists(Path.Combine(Root(), test.GetString()!)), test.GetString());
            Assert.NotEmpty(entry.GetProperty("Tests").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("Evidence").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("Reversal").GetString()));
            if (kind != "Canonical") continue;
            Assert.Equal("Canonical", entry.GetProperty("ReadPath").GetString());
            Assert.Equal("InventoryCommandExecutor (Serializable)", entry.GetProperty("TransactionOwner").GetString());
            Assert.Contains("CanonicalInventoryEnabled", File.ReadAllText(Path.Combine(Root(), path)));
            Assert.NotEmpty(entry.GetProperty("OperationIntents").EnumerateArray());
            foreach (var intent in entry.GetProperty("OperationIntents").EnumerateArray())
                Assert.True(Enum.TryParse<InventoryCommandKind>(intent.GetString(), out _), intent.GetString());
        }
        output.WriteLine("32 classified workflows; 0 ordinary writers marked Legacy or Unclassified. This structural result is combined with runtime-guard and workflow validation, not a substitute for them.");
    }

    private static bool IsWriteCandidate(string source)
    {
        string[] entities = "RoomInventoryAdjustment TreatmentLineageSegment TreatmentLineageMovement TreatmentLineageSegmentApplication RoomTreatmentApplication RoomTreatmentApplicationSource RoomTransfer RoomDepletion RoomInventoryLoss ReceiptInventoryOverride InventoryIdentityCorrection InventoryCommandRecord ActualRunRevision BinsRunEntry OutsideWarehouseTransfer ProcessorShipmentLine ProcessorShipment ActualRun InterCrewTransfer Receipt".Split(' ');
        var names = entities.Concat(entities.Select(x => x.EndsWith('y') ? x[..^1] + "ies" : x + "s")).Append("InventoryCommands");
        return Regex.IsMatch(source, @"\b(?:" + string.Join("|", names) + @")\b")
            && Regex.IsMatch(source, "\\.\\s*(?:Add(?:Async|Range|RangeAsync)?|Remove(?:Range)?|Update(?:Range)?|ExecuteUpdate(?:Async)?|ExecuteDelete(?:Async)?|ExecuteSql\\w*|SaveChanges(?:Async)?)\\s*[(<]|\\b(?:INSERT\\s+INTO|UPDATE|DELETE\\s+FROM)\\s+[\"\\[]");
    }

    [Fact]
    public void Every_physical_write_candidate_matches_its_reviewed_source_and_allowlist()
    {
        var reviewed = Read<Candidate[]>("phase3-reviewed-write-candidates.json").ToDictionary(x => x.Path);
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(Root(), file).Replace('\\', '/');
            if (path.Split('/').Any(x => x is "bin" or "obj" or "Migrations")) continue;
            var source = File.ReadAllText(file).Replace("\r\n", "\n");
            if (!IsWriteCandidate(source)) continue;
            found.Add(path);
            Assert.True(reviewed.TryGetValue(path, out var candidate), $"UNREVIEWED writer candidate: {path}. Route physical changes through the executor and classify this path.");
            Assert.False(string.IsNullOrWhiteSpace(candidate!.Reason));
            Assert.Equal(candidate.Sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
            output.WriteLine($"{candidate.Classification}: {path}");
        }
        Assert.Equal(reviewed.Keys.Order(), found.Order());
    }

    [Fact]
    public void Only_canonical_Data_infrastructure_can_open_the_physical_write_capability()
    {
        var assignments = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(Root(), file).Replace('\\', '/');
            if (path.Split('/').Any(x => x is "bin" or "obj" or "Migrations")) continue;
            if (Regex.IsMatch(File.ReadAllText(file), @"\.CanonicalCommandTransaction\s*=")) assignments.Add(path);
        }
        Assert.Equal(new[] { "src/CropQc.Data/Inventory/InventoryCommandExecutor.cs", "src/CropQc.Data/Inventory/InventoryCommandOperations.cs" }, assignments.Order());
        var context = File.ReadAllText(Path.Combine(Root(), "src/CropQc.Data/CropQcDbContext.cs"));
        Assert.Contains("internal bool CanonicalCommandTransaction", context);
        Assert.Contains("CanonicalInventoryWriteGuard.Check(this)", context);
        Assert.Contains("CanonicalInventorySqlGuard", context);
    }

    [Theory]
    [InlineData("db.RoomInventoryAdjustments.Add(new());")]
    [InlineData("var row = new RoomInventoryAdjustment(); db.Add(row);")]
    [InlineData("db.BinsRunEntries.ExecuteDeleteAsync();")]
    [InlineData("var r = await db.Receipts.SingleAsync(); r.BinCount = 99; await db.SaveChangesAsync();")]
    [InlineData("await db.Database.ExecuteSqlRawAsync(sqlForReceipts); // Receipts")]
    public void Scanner_flags_new_direct_tracked_generic_and_bulk_write_shapes(string source) => Assert.True(IsWriteCandidate(source));
}
