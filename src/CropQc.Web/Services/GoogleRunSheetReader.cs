using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace CropQc.Web.Services;

public interface IRunSheetReader
{
    Task<IReadOnlyList<ExternalPhysicalRun>> ReadAsync(CancellationToken cancellationToken);
}

public sealed class GoogleRunSheetReader(
    RunSheetReconciliationOptions options,
    GoogleDriveStorageOptions googleOptions,
    IPerformanceExternalCallCounter externalCallCounter) : IRunSheetReader
{
    public async Task<IReadOnlyList<ExternalPhysicalRun>> ReadAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        using var service = CreateService();
        var ebsValues = await ReadWorksheetAsync(
            service,
            options.EbsSpreadsheetId,
            options.EbsSheetName,
            cancellationToken);
        var wpValues = await ReadWorksheetAsync(
            service,
            options.WpSpreadsheetId,
            options.WpSheetName,
            cancellationToken);

        return
        [
            .. RunSheetParser.ParseWorksheet(EmploymentFacilities.Ebs, ebsValues, options),
            .. RunSheetParser.ParseWorksheet(EmploymentFacilities.Wp, wpValues, options)
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyList<object?>>> ReadWorksheetAsync(
        SheetsService service,
        string spreadsheetId,
        string sheetName,
        CancellationToken cancellationToken)
    {
        var escapedSheetName = sheetName.Replace("'", "''", StringComparison.Ordinal);
        var request = service.Spreadsheets.Values.Get(
            spreadsheetId,
            $"'{escapedSheetName}'!A1:Z{options.BoundedMaximumRows}");
        request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMATTEDVALUE;
        request.DateTimeRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum.FORMATTEDSTRING;
        externalCallCounter.Increment("GoogleSheets.ReadOnly");
        var response = await request.ExecuteAsync(cancellationToken);
        return response.Values?.Select(row => (IReadOnlyList<object?>)row.Cast<object?>().ToList()).ToList() ?? [];
    }

    private SheetsService CreateService()
    {
        GoogleCredential credential;
        try
        {
            credential = !string.IsNullOrWhiteSpace(googleOptions.ServiceAccountJson)
                ? GoogleCredential.FromJson(googleOptions.ServiceAccountJson)
                : GoogleCredential.FromFile(googleOptions.ServiceAccountJsonPath);
        }
        catch (Exception exception)
        {
            throw new RunSheetConfigurationException($"Google Sheets service-account credentials are invalid: {exception.GetType().Name}.");
        }

        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential.CreateScoped(SheetsService.Scope.SpreadsheetsReadonly),
            ApplicationName = string.IsNullOrWhiteSpace(googleOptions.ApplicationName)
                ? "Crop QC Dashboard"
                : googleOptions.ApplicationName
        });
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(googleOptions.ServiceAccountJson)
            && string.IsNullOrWhiteSpace(googleOptions.ServiceAccountJsonPath))
        {
            throw new RunSheetConfigurationException("Google Sheets service-account credentials are not configured.");
        }
        if (string.IsNullOrWhiteSpace(options.EbsSpreadsheetId)
            || string.IsNullOrWhiteSpace(options.EbsSheetName)
            || string.IsNullOrWhiteSpace(options.WpSpreadsheetId)
            || string.IsNullOrWhiteSpace(options.WpSheetName))
        {
            throw new RunSheetConfigurationException("Run reconciliation spreadsheet IDs and worksheet names must be configured.");
        }
    }
}

public sealed class RunSheetSnapshotStore(
    RunSheetReconciliationOptions options,
    IClock clock) : IRunSheetSnapshotStore
{
    private readonly object sync = new();
    private RunSheetExternalSnapshot? snapshot;
    private DateTimeOffset? lastAttemptAt;
    private string? failureMessage;

    public RunSheetSnapshotState GetState()
    {
        lock (sync)
        {
            var stale = snapshot is not null
                && (failureMessage is not null || clock.UtcNow - snapshot.RefreshedAt > options.RefreshInterval + options.RefreshInterval);
            return new RunSheetSnapshotState(
                snapshot,
                snapshot?.RefreshedAt,
                lastAttemptAt,
                failureMessage,
                stale);
        }
    }

    public void RecordSuccess(IReadOnlyList<ExternalPhysicalRun> runs, DateTimeOffset refreshedAt)
    {
        lock (sync)
        {
            snapshot = new RunSheetExternalSnapshot(runs, refreshedAt);
            lastAttemptAt = refreshedAt;
            failureMessage = null;
        }
    }

    public void RecordFailure(string safeMessage, DateTimeOffset attemptedAt)
    {
        lock (sync)
        {
            lastAttemptAt = attemptedAt;
            failureMessage = safeMessage;
        }
    }
}

public sealed class RunSheetRefreshHostedService(
    IRunSheetReader reader,
    IRunSheetSnapshotStore store,
    RunSheetReconciliationOptions options,
    IClock clock,
    ILogger<RunSheetRefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Run Sheet reconciliation refresh is disabled.");
            return;
        }

        await RefreshAsync(stoppingToken);
        using var timer = new PeriodicTimer(options.RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var attemptedAt = clock.UtcNow;
        try
        {
            var runs = await reader.ReadAsync(cancellationToken);
            store.RecordSuccess(runs, clock.UtcNow);
            logger.LogInformation("Run Sheet reconciliation refreshed {RunCount} normalized physical runs.", runs.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var safeMessage = SafeFailureMessage(exception);
            store.RecordFailure(safeMessage, attemptedAt);
            logger.LogWarning(exception, "Run Sheet reconciliation refresh failed. {SafeMessage}", safeMessage);
        }
    }

    public static string SafeFailureMessage(Exception exception) => exception switch
    {
        RunSheetConfigurationException configuration => configuration.Message,
        GoogleApiException google => $"Google Sheets API request failed ({google.HttpStatusCode}).",
        HttpRequestException => "Google Sheets is temporarily unavailable.",
        _ => $"Run verification refresh failed ({exception.GetType().Name})."
    };
}
