namespace CropQc.Api.Tests;

public sealed class FujiAutumnGloryCorrectionTests
{
    [Fact]
    public void Preflight_UsesExactReviewedTargetsAndFailsClosedOnFingerprintDrift()
    {
        var sql = Read("preflight-fuji-evans12-autumn-glory-dh1-correction.sql");

        Assert.Contains("BEGIN TRANSACTION READ ONLY", sql);
        Assert.Contains("IN (35,36,37,54,55,66,67,68)", sql);
        Assert.Contains("IN (1,2,13,14,15)", sql);
        Assert.Contains("\"Id\" = 52", sql);
        Assert.Contains("\"Id\" = 37", sql);
        Assert.Contains("2b35e5a3ba2a0618dc721dc853e8608e", sql);
        Assert.Contains("837f988f4045030f80139b6f45f1755d", sql);
        Assert.Contains("Unexpected operational history", sql);
        Assert.Contains("ROLLBACK;", sql);
    }

    [Fact]
    public void Apply_IsSerializableAuthorizedIdempotentAndMutatesOnlyTheExactSourceAndAudit()
    {
        var sql = Read("apply-fuji-evans12-autumn-glory-dh1-correction.sql");

        Assert.Contains("BEGIN ISOLATION LEVEL SERIALIZABLE", sql);
        Assert.Contains("APPLY_FUJI_EVANS12_AUTUMN_GLORY_DH1_CORRECTION", sql);
        Assert.Contains("pg_advisory_xact_lock", sql);
        Assert.Contains("expected_gala_fingerprint", sql);
        Assert.Contains("expected_wp_fingerprint", sql);
        Assert.Contains("UPDATE \"RoomInventoryAdjustments\" a", sql);
        Assert.Contains("WHERE a.\"Id\" = 52", sql);
        Assert.Equal(1, Count(sql, "UPDATE \"RoomInventoryAdjustments\""));
        Assert.Equal(1, Count(sql, "INSERT INTO \"AuditLogs\""));
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"Receipts\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"BinsRunEntries\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"ActualRuns\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"RoomInventoryAdjustments\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Expected zero idempotent updates or one first-run update", sql);
        Assert.Contains("Protected inventory/history changed", sql);
        Assert.Contains("protected_hash_before", sql);
        Assert.Contains("protected_hash_after", sql);
    }

    [Fact]
    public void Verify_IsReadOnlyAndRequiresBothTargetsZeroWithOneAudit()
    {
        var sql = Read("verify-fuji-evans12-autumn-glory-dh1-correction.sql");

        Assert.Contains("BEGIN TRANSACTION READ ONLY", sql);
        Assert.Contains("Autumn Glory DH Room 1 is not zero", sql);
        Assert.Contains("Fuji Evans 12 physical ledger is not zero", sql);
        Assert.Contains("Expected exactly one correction audit marker", sql);
        Assert.Contains("ROLLBACK;", sql);
    }

    private static string Read(string name) => File.ReadAllText(FindRepositoryFile("scripts", "postgresql", name));

    private static int Count(string value, string search)
    {
        var count = 0;
        for (var offset = 0; (offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0; offset += search.Length)
        {
            count++;
        }
        return count;
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
