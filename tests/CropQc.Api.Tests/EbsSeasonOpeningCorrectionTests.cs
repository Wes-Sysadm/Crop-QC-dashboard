using System.Globalization;
using System.Text;

namespace CropQc.Api.Tests;

public sealed class EbsSeasonOpeningCorrectionTests
{
    private static readonly IReadOnlyDictionary<int, decimal> CorrectedAmounts =
        new Dictionary<int, decimal>
        {
            [1] = 0m,
            [8] = 0m,
            [22] = 144m,
            [23] = 144m,
            [25] = 101m,
            [26] = 101m
        };

    [Fact]
    public void Evidence_ClassifiesEveryProductionRowOutsideEvans7ExactlyOnce()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");

        Assert.Equal(79, rows.Count);
        Assert.Equal(79, rows.Select(row => row["adjustment_id"]).Distinct().Count());
        Assert.Equal(1, rows.Count(row => row["category_number"] == "1"));
        Assert.Equal(69, rows.Count(row => row["category_number"] == "2"));
        Assert.Equal(1, rows.Count(row => row["category_number"] == "3"));
        Assert.Equal(8, rows.Count(row => row["category_number"] == "4"));
        Assert.DoesNotContain(rows, row => row["category"].Contains("unclear", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evidence_UsesVerifiedDynamicBoundaryAndContainsOnlyPreBoundaryRows()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");

        Assert.All(rows, row => Assert.Equal("99", row["boundary_receipt_id"]));
        Assert.All(rows, row => Assert.Equal("2026-07-28 08:36:00", row["boundary_pacific"]));
        Assert.All(rows, row => Assert.Equal("t", row["before_boundary"]));

        var preflight = ReadRepositoryFile("scripts", "postgresql", "preflight-ebs-2026-season-opening-correction.sql");
        Assert.Contains("ORDER BY receipt_row.\"ReceivedAt\", receipt_row.\"Id\"", preflight);
        Assert.Contains("receipt_row.\"CropYear\" = 2026", preflight);
        Assert.Contains("NOT receipt_row.\"IsDeleted\"", preflight);
        Assert.Contains("NOT receipt_row.\"IsTestData\"", preflight);
        Assert.DoesNotContain("2026-07-28'::", preflight);
    }

    [Fact]
    public void Evidence_Evans7IsExcludedByPersistedRoomIdentity()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");
        Assert.DoesNotContain(rows, row => row["room_id"] == "17");

        foreach (var scriptName in ScriptNames())
        {
            var script = ReadRepositoryFile("scripts", "postgresql", scriptName);
            Assert.Contains("room_row.\"WarehouseId\"", script);
            Assert.Contains("regexp_replace", script);
            Assert.Contains("EVANSSTREET7", script);
        }
    }

    [Fact]
    public void Evidence_ExactlySixRowsAreDirectCorrectionTargets()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");
        var targetIds = rows
            .Where(row => row["correction_target"] == "yes")
            .Select(row => int.Parse(row["adjustment_id"], CultureInfo.InvariantCulture))
            .Order()
            .ToArray();

        Assert.Equal(new[] { 1, 8, 22, 23, 25, 26 }, targetIds);
    }

    [Fact]
    public void ReviewedCorrection_ZerosEveryNonEvansEbsRoomWithoutGenericBalancingEntry()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");
        var finalBalances = rows
            .GroupBy(row => row["room"])
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => CorrectedAmount(row)));

        Assert.All(finalBalances, balance => Assert.Equal(0m, balance.Value));
        Assert.Equal(0m, finalBalances["Bluemountain 4"]);
        Assert.Equal(0m, finalBalances["Evans-01"]);
        Assert.Equal(0m, finalBalances["Lamb Street 14"]);
    }

    [Fact]
    public void ReviewedCorrection_PreservesZeroNetRoomsUnchanged()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");
        var zeroNetRooms = new[] { "BM-1", "BM-6", "Evans-12", "Evans-5", "Lamb-17" };

        foreach (var room in zeroNetRooms)
        {
            var roomRows = rows.Where(row => row["room"] == room).ToArray();
            Assert.NotEmpty(roomRows);
            Assert.Equal(0m, roomRows.Sum(row => ParseDecimal(row["quantity"])));
            Assert.DoesNotContain(roomRows, row => row["correction_target"] == "yes");
        }
    }

    [Fact]
    public void ReviewedCorrection_NeutralizesOnlyTheDuplicateEvans01Carry()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");
        var evans = rows.Where(row => row["room"] == "Evans-01").ToArray();

        Assert.Equal(1039m, evans.Sum(row => ParseDecimal(row["quantity"])));
        Assert.Equal("1039", evans.Single(row => row["adjustment_id"] == "8")["quantity"]);
        Assert.Equal("t", evans.Single(row => row["adjustment_id"] == "8")["receipt_is_deleted"]);
        Assert.Equal(0m, evans.Sum(CorrectedAmount));
    }

    [Fact]
    public void ReviewedCorrection_RepairsLamb14SourceRowsAndPreservesBinsRunDeductions()
    {
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");
        var lamb = rows.Where(row => row["room"] == "Lamb Street 14").ToArray();

        Assert.Equal(-490m, lamb.Sum(row => ParseDecimal(row["quantity"])));
        Assert.Equal(-490m, lamb.Where(row => new[] { "76", "77", "78", "79" }.Contains(row["adjustment_id"]))
            .Sum(row => ParseDecimal(row["quantity"])));
        Assert.All(lamb.Where(row => new[] { "22", "23", "25", "26" }.Contains(row["adjustment_id"])),
            row => Assert.Equal("0", row["quantity"]));
        Assert.Equal(0m, lamb.Sum(CorrectedAmount));
    }

    [Fact]
    public void ApplyScript_IsExplicitlyAuthorizedTransactionalAndIdempotent()
    {
        var apply = ReadRepositoryFile("scripts", "postgresql", "apply-ebs-2026-season-opening-correction.sql");

        Assert.Contains("BEGIN ISOLATION LEVEL SERIALIZABLE", apply);
        Assert.Contains("APPLY_EBS_2026_SEASON_OPENING_CORRECTION", apply);
        Assert.Contains("expected_boundary_receipt_id", apply);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("ApplyEbs2026SeasonOpeningCorrection", apply);
        Assert.Contains("Expected either zero idempotent updates or exactly six first-run updates", apply);
        Assert.Contains("COMMIT;", apply);
    }

    [Fact]
    public void ApplyScript_DoesNotCreateOperationalRecordsOrDeleteHistory()
    {
        var apply = ReadRepositoryFile("scripts", "postgresql", "apply-ebs-2026-season-opening-correction.sql");

        Assert.DoesNotContain("DELETE FROM", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"BinsRunEntries\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"ActualRuns\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"Receipts\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"RoomInventoryAdjustments\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO \"AuditLogs\"", apply);
    }

    [Fact]
    public void ApplyScript_ProtectsEvans7WpOtherFacilitiesAndNonTargetsRowForRow()
    {
        var apply = ReadRepositoryFile("scripts", "postgresql", "apply-ebs-2026-season-opening-correction.sql");

        Assert.Contains("evans7_before", apply);
        Assert.Contains("non_ebs_ledger_before", apply);
        Assert.Contains("preserved_ledger_before", apply);
        Assert.Contains("Protected rows changed (Evans 7 %, non-EBS %, preserved ledger %)", apply);
        Assert.Contains("Evans 7 balance changed from the protected 388 bins", apply);
    }

    [Fact]
    public void Preflight_FailsClosedOnFingerprintDriftOrUnclearRows()
    {
        var preflight = ReadRepositoryFile("scripts", "postgresql", "preflight-ebs-2026-season-opening-correction.sql");

        Assert.Contains("BEGIN TRANSACTION READ ONLY", preflight);
        Assert.Contains("expected 79 rows / 583 bins", preflight);
        Assert.Contains("production fingerprint changed", preflight);
        Assert.Contains("Classification contains % unclear rows", preflight);
        Assert.Contains("ROLLBACK;", preflight);
    }

    [Fact]
    public void VerifyScript_IsReadOnlyAndChecksAllFinalGuards()
    {
        var verify = ReadRepositoryFile("scripts", "postgresql", "verify-ebs-2026-season-opening-correction.sql");

        Assert.Contains("BEGIN TRANSACTION READ ONLY", verify);
        Assert.Contains("non-Evans 7 EBS room still has a nonzero balance", verify);
        Assert.Contains("Evans 7 is not the protected 388-bin balance", verify);
        Assert.Contains("The six reviewed correction rows do not match", verify);
        Assert.Contains("Expected exactly one correction audit record", verify);
        Assert.Contains("ROLLBACK;", verify);
    }

    [Fact]
    public void Generator_RejectsIncompleteOrChangedEvidence()
    {
        var generator = ReadRepositoryFile("scripts", "generate-ebs-season-opening-classification.ps1");

        Assert.Contains("Expected 79 EBS rows outside Evans 7", generator);
        Assert.Contains("boundary receipt 99", generator);
        Assert.Contains("At least one candidate row is not before the verified season boundary", generator);
        Assert.Contains("Category $categoryNumber expected", generator);
    }

    [Fact]
    public void Boundary_IsEbsOnlyAndValid2026ActivityIsNeverAReviewedTarget()
    {
        var preflight = ReadRepositoryFile("scripts", "postgresql", "preflight-ebs-2026-season-opening-correction.sql");
        var apply = ReadRepositoryFile("scripts", "postgresql", "apply-ebs-2026-season-opening-correction.sql");

        Assert.Contains("upper(warehouse_row.\"Code\") = 'EBS'", preflight);
        Assert.Contains("WHEN adjustment_row.\"AdjustmentAt\" >= boundary.received_at_utc THEN 5", preflight);
        Assert.Contains("adjustment_row.\"AdjustmentAt\" < (SELECT received_at_utc FROM season_boundary)", apply);
    }

    [Fact]
    public void PriorSeasonOperationalHistoryRemainsReadable()
    {
        var apply = ReadRepositoryFile("scripts", "postgresql", "apply-ebs-2026-season-opening-correction.sql");
        var rows = ReadCsv("docs", "ebs-2026-season-opening-classification.csv");

        Assert.DoesNotContain("DELETE FROM", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(rows, row => row["bins_run_links"].StartsWith("23:", StringComparison.Ordinal)
            && row["adjustment_id"] == "76");
        Assert.Contains(rows, row => row["receipt_id"] == "26" && row["adjustment_id"] == "1");
    }

    [Fact]
    public void ProtectedDifferenceChecksOccurBeforeCommitAndCauseRollback()
    {
        var apply = ReadRepositoryFile("scripts", "postgresql", "apply-ebs-2026-season-opening-correction.sql");
        var protectionCheck = apply.IndexOf("Protected rows changed", StringComparison.Ordinal);
        var commit = apply.LastIndexOf("COMMIT;", StringComparison.Ordinal);

        Assert.True(protectionCheck >= 0);
        Assert.True(commit > protectionCheck);
        Assert.Contains("RAISE EXCEPTION", apply);
    }

    [Fact]
    public void AuditMarkerIsInsertedOnceAndRepeatedApplyMakesNoSecondRecord()
    {
        var apply = ReadRepositoryFile("scripts", "postgresql", "apply-ebs-2026-season-opening-correction.sql");

        Assert.Equal(1, CountOccurrences(apply, "INSERT INTO \"AuditLogs\""));
        Assert.Contains("NOT EXISTS", apply);
        Assert.Contains("audit_count <> 1", apply);
    }

    [Fact]
    public void UnclearProductionEvidenceFailsClosedInsteadOfBeingGuessed()
    {
        var preflight = ReadRepositoryFile("scripts", "postgresql", "preflight-ebs-2026-season-opening-correction.sql");

        Assert.Contains("ELSE 6", preflight);
        Assert.Contains("Classification contains % unclear rows. Stop and ask Wes.", preflight);
    }

    private static decimal CorrectedAmount(IReadOnlyDictionary<string, string> row)
    {
        var id = int.Parse(row["adjustment_id"], CultureInfo.InvariantCulture);
        return CorrectedAmounts.TryGetValue(id, out var corrected)
            ? corrected
            : ParseDecimal(row["quantity"]);
    }

    private static decimal ParseDecimal(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? 0m
            : decimal.Parse(value, CultureInfo.InvariantCulture);

    private static IEnumerable<string> ScriptNames()
    {
        yield return "preflight-ebs-2026-season-opening-correction.sql";
        yield return "apply-ebs-2026-season-opening-correction.sql";
        yield return "verify-ebs-2026-season-opening-correction.sql";
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(FindRepositoryFile(pathParts));

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadCsv(params string[] pathParts)
    {
        var lines = File.ReadAllLines(FindRepositoryFile(pathParts));
        var headers = ParseCsvLine(lines[0]);
        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var fields = ParseCsvLine(line);
                Assert.Equal(headers.Count, fields.Count);
                return (IReadOnlyDictionary<string, string>)headers
                    .Select((header, index) => (header, value: fields[index]))
                    .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);
            })
            .ToArray();
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        fields.Add(value.ToString());
        return fields;
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
