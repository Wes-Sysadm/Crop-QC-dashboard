namespace CropQc.Web.Services;

public sealed record ExternalRunSheetRow(
    string Facility,
    DateOnly Date,
    int Bins,
    string GrowerNumber,
    string GrowerName,
    string Variety,
    string ProductionType,
    string? SalesDesk,
    string? UnknownSalesDeskCode,
    string Pool);

public sealed record ExternalPhysicalRun(
    string Facility,
    DateOnly Date,
    string Variety,
    string ProductionType,
    string? SalesDesk,
    string? UnknownSalesDeskCode,
    int TotalBins,
    IReadOnlyDictionary<string, int> GrowerBins);

public sealed record CropPhysicalRun(
    string Facility,
    DateOnly Date,
    IReadOnlyList<string> Varieties,
    IReadOnlyList<string> ProductionTypes,
    string SalesDesk,
    int TotalBins,
    IReadOnlyDictionary<string, int> GrowerBins,
    IReadOnlyList<long> ActualRunIds,
    DateTimeOffset LatestRunAt);

public sealed record RunSheetCropLine(
    long ActualRunId,
    DateTimeOffset RunAt,
    string? SalesDesk,
    string Variety,
    string ProductionType,
    bool IsOrganic,
    string GrowerNumber,
    int Bins);

public sealed record RunSheetExternalSnapshot(
    IReadOnlyList<ExternalPhysicalRun> Runs,
    DateTimeOffset RefreshedAt);

public sealed record RunSheetSnapshotState(
    RunSheetExternalSnapshot? Snapshot,
    DateTimeOffset? LastSuccessfulRefreshAt,
    DateTimeOffset? LastAttemptAt,
    string? FailureMessage,
    bool IsStale);

public interface IRunSheetSnapshotStore
{
    RunSheetSnapshotState GetState();
    void RecordSuccess(IReadOnlyList<ExternalPhysicalRun> runs, DateTimeOffset refreshedAt);
    void RecordFailure(string safeMessage, DateTimeOffset attemptedAt);
}
