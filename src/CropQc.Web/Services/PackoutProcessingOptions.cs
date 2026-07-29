namespace CropQc.Web.Services;

public sealed class PackoutProcessingOptions
{
    public const string SectionName = "PackoutProcessing";

    public long MaximumFileBytes { get; init; } = 20 * 1024 * 1024;
    public long MaximumTotalUploadBytes { get; init; } = 50 * 1024 * 1024;
    public int MaximumFilesPerUpload { get; init; } = 10;
    public int MaximumPdfPages { get; init; } = 25;
    public long MaximumImagePixels { get; init; } = 40_000_000;
    public int MaximumSpreadsheetRows { get; init; } = 25_000;
    public int MaximumParsedRows { get; init; } = 25_000;
    public int MaximumWorkbookRows { get; init; } = 50_000;

    public static PackoutProcessingOptions FromConfiguration(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<PackoutProcessingOptions>() ?? new();
        if (options.MaximumFileBytes is < 1 or > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException($"{SectionName}:MaximumFileBytes must be between 1 byte and 100 MB.");
        }
        if (options.MaximumTotalUploadBytes < options.MaximumFileBytes
            || options.MaximumTotalUploadBytes > 250 * 1024 * 1024)
        {
            throw new InvalidOperationException($"{SectionName}:MaximumTotalUploadBytes must be at least MaximumFileBytes and no more than 250 MB.");
        }
        if (options.MaximumFilesPerUpload is < 1 or > 25
            || options.MaximumPdfPages is < 1 or > 100
            || options.MaximumImagePixels is < 1 or > 100_000_000
            || options.MaximumSpreadsheetRows is < 1 or > 250_000
            || options.MaximumParsedRows is < 1 or > 250_000
            || options.MaximumWorkbookRows is < 1 or > 250_000)
        {
            throw new InvalidOperationException($"{SectionName} limits are outside their supported safe ranges.");
        }
        return options;
    }
}

public static class PackoutUploadLimits
{
    public static string? Validate(IReadOnlyCollection<long> fileLengths, PackoutProcessingOptions options)
    {
        if (fileLengths.Count is < 1 || fileLengths.Count > options.MaximumFilesPerUpload)
        {
            return $"Upload between 1 and {options.MaximumFilesPerUpload} related report files.";
        }
        if (fileLengths.Any(length => length is < 1 || length > options.MaximumFileBytes))
        {
            return $"Each packout report must be between 1 byte and {options.MaximumFileBytes / 1024 / 1024} MB.";
        }
        if (fileLengths.Sum() > options.MaximumTotalUploadBytes)
        {
            return $"The combined upload may not exceed {options.MaximumTotalUploadBytes / 1024 / 1024} MB.";
        }
        return null;
    }

    public static string? ValidatePdfPageCount(int pageCount, PackoutProcessingOptions options) =>
        pageCount is < 1 or > 100_000
            ? "The PDF does not contain a valid page count."
            : pageCount > options.MaximumPdfPages
                ? $"PDF reports may contain at most {options.MaximumPdfPages} pages."
                : null;
}

public interface IPackoutOperationCoordinator
{
    IDisposable? TryEnter(long packoutRunId, string operation);
}

public sealed class PackoutOperationCoordinator : IPackoutOperationCoordinator
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> active = new(StringComparer.Ordinal);

    public IDisposable? TryEnter(long packoutRunId, string operation)
    {
        var key = $"{packoutRunId}:{operation}";
        return active.TryAdd(key, 0) ? new Lease(active, key) : null;
    }

    private sealed class Lease(
        System.Collections.Concurrent.ConcurrentDictionary<string, byte> active,
        string key) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                active.TryRemove(new KeyValuePair<string, byte>(key, 0));
            }
        }
    }
}
