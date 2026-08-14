using System.Security.Cryptography;
using System.Text;

namespace CropQc.Web.Services;

public interface IReviewedGrowerMasterSource
{
    Task<ReviewedGrowerMaster> LoadAsync(CancellationToken cancellationToken);
}

public sealed record ReviewedGrowerMasterRow(
    string GrowerNumber,
    string GrowerName,
    string? Pool,
    bool IsActive,
    string? RedirectToGrowerNumber);

public sealed record ReviewedGrowerMaster(
    string WorkbookFileName,
    long WorkbookSizeBytes,
    string WorkbookSha256,
    string AssetSha256,
    IReadOnlyList<ReviewedGrowerMasterRow> Rows);

public static class ReviewedGrowerMasterConstants
{
    public const string SourceSystem = "Reviewed grower master pool.xlsx 2026-08-13";
    public const string WorkbookFileName = "pool.xlsx";
    public const long WorkbookSizeBytes = 31_013;
    public const string WorkbookSha256 = "dc34005faca9dc241977c4680d9d52b7dc6682efff5246591ff43ff303fd4e6b";
    public const string AssetSha256 = "e49848f40bff96ef256ab5bf51a9ee9cb1c9aa6f88c1b1b4dc51ec712157afb2";
    public const int ExpectedRowCount = 405;
    public const int ExpectedActiveCount = 389;
    public const int ExpectedInactiveCount = 16;
}

public sealed class ReviewedGrowerMasterSource(IHostEnvironment environment) : IReviewedGrowerMasterSource
{
    private const string RelativePath = "Data/ReviewedGrowers/authoritative-growers-2026.csv";

    public async Task<ReviewedGrowerMaster> LoadAsync(CancellationToken cancellationToken)
    {
        var relativePath = RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var contentRootPath = Path.Combine(environment.ContentRootPath, relativePath);
        var outputPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        var path = File.Exists(contentRootPath) ? contentRootPath : outputPath;
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The reviewed grower source asset is missing: {RelativePath}.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!sha256.Equals(ReviewedGrowerMasterConstants.AssetSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The reviewed grower source asset SHA-256 does not match the code-reviewed package.");
        }

        var lines = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 2 || lines[0] != "GrowerNumber,GrowerName,Pool,Status,RedirectToGrowerNumber")
        {
            throw new InvalidOperationException("The reviewed grower source header is missing or changed.");
        }

        var rows = lines.Skip(1).Where(x => x.Length > 0).Select(ParseRow).ToList();
        var duplicateNumbers = rows.GroupBy(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() != 1).Select(x => x.Key).ToList();
        if (rows.Count != ReviewedGrowerMasterConstants.ExpectedRowCount
            || rows.Count(x => x.IsActive) != ReviewedGrowerMasterConstants.ExpectedActiveCount
            || rows.Count(x => !x.IsActive) != ReviewedGrowerMasterConstants.ExpectedInactiveCount
            || duplicateNumbers.Count != 0)
        {
            throw new InvalidOperationException("The reviewed grower source row counts, statuses, or unique grower numbers do not match the reviewed package.");
        }
        if (rows.Any(x => x.GrowerNumber != CanonicalGrowerService.NormalizeGrowerNumber(x.GrowerNumber)
            || (x.IsActive && string.IsNullOrWhiteSpace(x.GrowerName))
            || (!x.IsActive && x.GrowerName.Length != 0)))
        {
            throw new InvalidOperationException("The reviewed grower source contains an invalid number, name, or inactive marker.");
        }

        return new ReviewedGrowerMaster(
            ReviewedGrowerMasterConstants.WorkbookFileName,
            ReviewedGrowerMasterConstants.WorkbookSizeBytes,
            ReviewedGrowerMasterConstants.WorkbookSha256,
            sha256,
            rows);
    }

    private static ReviewedGrowerMasterRow ParseRow(string line)
    {
        var fields = ParseCsv(line);
        if (fields.Count != 5) throw new InvalidOperationException("A reviewed grower source row has an unexpected column count.");
        var isActive = fields[3] switch
        {
            "Active" => true,
            "Inactive" => false,
            _ => throw new InvalidOperationException("A reviewed grower source row has an unexpected status.")
        };
        return new(fields[0], fields[1], EmptyToNull(fields[2]), isActive, EmptyToNull(fields[4]));
    }

    private static IReadOnlyList<string> ParseCsv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var value = line[index];
            if (value == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (value == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else field.Append(value);
        }
        if (quoted) throw new InvalidOperationException("A reviewed grower source row has an unterminated quoted value.");
        fields.Add(field.ToString());
        return fields;
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}
