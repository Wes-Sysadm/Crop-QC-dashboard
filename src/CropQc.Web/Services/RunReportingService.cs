using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRunReportingService
{
    Task<RunReportingPageViewModel> GetAsync(
        BinsRunFilterForm filter,
        ClaimsPrincipal user,
        CancellationToken cancellationToken);
}

public sealed class RunReportingService(
    CropQcDbContext dbContext,
    IBusinessTimeService businessTime,
    IUserAccessService userAccessService,
    IConfiguration configuration) : IRunReportingService
{
    public const int MaximumWeeklySourceRows = 5000;
    public const int SupportingPageSize = 50;
    public const int NeedsReviewPageSize = 100;
    public const int MaximumNeedsReviewCandidateRows = 2000;

    private int StartMonth => Math.Clamp(configuration.GetValue("RunReporting:CropYearStartMonth", 7), 1, 12);
    private int StartDay => Math.Clamp(configuration.GetValue("RunReporting:CropYearStartDay", 15), 1, 28);

    public async Task<RunReportingPageViewModel> GetAsync(
        BinsRunFilterForm filter,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var today = businessTime.PacificDate(businessTime.UtcNow);
        var currentCropYear = CurrentCropYear(today, StartMonth, StartDay);
        var summaryYears = new[] { currentCropYear, currentCropYear - 1, currentCropYear - 2 };
        var summary = new Dictionary<string, List<RunCropYearSummaryViewModel>>(StringComparer.OrdinalIgnoreCase)
        {
            [EmploymentFacilities.Wp] = [],
            [EmploymentFacilities.Ebs] = []
        };
        foreach (var cropYear in summaryYears)
        {
            var summaryCutoff = today < PeriodEnd(cropYear) ? today : PeriodEnd(cropYear);
            foreach (var row in await GetFacilityTotalsAsync(cropYear, summaryCutoff, cancellationToken))
            {
                if (row.Bins > 0 && summary.TryGetValue(row.Facility, out var values))
                {
                    values.Add(new RunCropYearSummaryViewModel(cropYear, row.Bins));
                }
            }
        }

        var canViewNeedsReview = await userAccessService.HasAccessAsync(
            user,
            ApplicationAreas.BinsRun,
            PageAccessLevel.Edit,
            cancellationToken);
        var model = new RunReportingPageViewModel
        {
            CurrentCropYear = currentCropYear,
            FacilitySummaries = summary.Select(x => new RunFacilitySummaryViewModel(x.Key, x.Value)).ToList(),
            OlderCropYears = await GetOlderYearsAsync(currentCropYear - 2, cancellationToken),
            CanViewNeedsReview = canViewNeedsReview
        };

        if (filter.Section.Equals("RunTotals", StringComparison.OrdinalIgnoreCase)
            && filter.ReportCropYear is int selectedYear
            && NormalizeFacility(filter.ReportFacility) is string selectedFacility)
        {
            model.Detail = await GetDetailAsync(selectedFacility, selectedYear, filter, today, cancellationToken);
        }
        else if (filter.Section.Equals("NeedsReview", StringComparison.OrdinalIgnoreCase) && canViewNeedsReview)
        {
            var page = Math.Max(1, filter.ReportPage);
            var issues = await GetNeedsReviewAsync(cancellationToken);
            var pageRows = issues.Skip((page - 1) * NeedsReviewPageSize).Take(NeedsReviewPageSize + 1).ToList();
            model.NeedsReviewPage = page;
            model.HasMoreIssues = pageRows.Count > NeedsReviewPageSize;
            model.Issues = pageRows.Take(NeedsReviewPageSize).ToList();
        }

        return model;
    }

    public static int CurrentCropYear(DateOnly date, int startMonth = 7, int startDay = 15) =>
        date >= new DateOnly(date.Year, startMonth, startDay) ? date.Year : date.Year - 1;

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);

    public static DateOnly EquivalentPriorCutoff(DateOnly cutoff) => cutoff.AddYears(-1);

    private DateOnly PeriodStart(int cropYear) => new(cropYear, StartMonth, StartDay);

    private static DateOnly PeriodEnd(int cropYear) => new(cropYear + 2, 1, 14);

    private DateTimeOffset UtcStart(DateOnly date) => businessTime.UtcRangeForPacificDate(date).Start;

    private DateTimeOffset UtcEndExclusive(DateOnly date) => businessTime.UtcRangeForPacificDate(date.AddDays(1)).Start;

    private IQueryable<BinsRunEntry> ValidLines(int cropYear, DateOnly cutoff)
    {
        var startUtc = UtcStart(PeriodStart(cropYear));
        var endUtc = UtcEndExclusive(cutoff > PeriodEnd(cropYear) ? PeriodEnd(cropYear) : cutoff);
        return dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ReportingCropYearSnapshot == cropYear && x.RunAt >= startUtc && x.RunAt < endUtc)
            .Where(x => x.ReportingFruitProfileIdSnapshot != null
                && x.ReportingVarietyCodeSnapshot != null && x.ReportingVarietyCodeSnapshot != ""
                && x.ProductionTypeSnapshot != null && x.ProductionTypeSnapshot != ""
                && x.IsOrganicSnapshot != null
                && x.GrowerNumberSnapshot != null && x.GrowerNumberSnapshot != "")
            .Where(x =>
                (x.TransactionType == ActualRunTransactionTypes.Depletion
                    && x.ActualRunId != null
                    && x.ActualRun != null
                    && x.ActualRun.Status == ActualRunStatuses.Active
                    && x.ActualRunRevisionId != null
                    && x.ActualRunRevision != null
                    && x.ActualRunRevision.IsCurrent
                    && !x.IsReversed
                    && (x.ActualRun.RunFacilityCodeSnapshot == EmploymentFacilities.Wp
                        || x.ActualRun.RunFacilityCodeSnapshot == EmploymentFacilities.Ebs))
                || (x.TransactionType == ActualRunTransactionTypes.Legacy
                    && x.ActualRunId == null
                    && !x.IsReversed
                    && (x.ReportingFacilityCodeSnapshot == EmploymentFacilities.Wp
                        || x.ReportingFacilityCodeSnapshot == EmploymentFacilities.Ebs)));
    }

    private async Task<IReadOnlyList<FacilityTotal>> GetFacilityTotalsAsync(
        int cropYear,
        DateOnly cutoff,
        CancellationToken cancellationToken) =>
        await ValidLines(cropYear, cutoff)
            .GroupBy(x => x.ActualRunId != null
                ? x.ActualRun!.RunFacilityCodeSnapshot!
                : x.ReportingFacilityCodeSnapshot!)
            .Select(x => new FacilityTotal(x.Key, x.Sum(y => y.BinsRun)))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<int>> GetOlderYearsAsync(int newestExcluded, CancellationToken cancellationToken)
    {
        var candidateYears = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ReportingCropYearSnapshot != null && x.ReportingCropYearSnapshot < newestExcluded)
            .Where(x => x.ProductionTypeSnapshot != null && x.GrowerNumberSnapshot != null)
            .Select(x => x.ReportingCropYearSnapshot!.Value)
            .Distinct()
            .OrderByDescending(x => x)
            .Take(20)
            .ToListAsync(cancellationToken);
        var result = new List<int>();
        foreach (var year in candidateYears)
        {
            if ((await GetFacilityTotalsAsync(year, PeriodEnd(year), cancellationToken)).Any(x => x.Bins > 0))
            {
                result.Add(year);
            }
        }
        return result;
    }

    private async Task<RunTotalsDetailViewModel> GetDetailAsync(
        string facility,
        int cropYear,
        BinsRunFilterForm filter,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var selectedStart = PeriodStart(cropYear);
        var selectedEnd = PeriodEnd(cropYear);
        var selectedCutoff = today < selectedEnd ? today : selectedEnd;
        if (selectedCutoff < selectedStart)
        {
            selectedCutoff = selectedStart.AddDays(-1);
        }
        var priorYear = cropYear - 1;
        var priorStart = PeriodStart(priorYear);
        var priorCutoff = EquivalentPriorCutoff(selectedCutoff);

        var selectedGroups = selectedCutoff < selectedStart
            ? new List<VarietyTotalRow>()
            : await VarietyTotalsQuery(facility, cropYear, selectedCutoff).ToListAsync(cancellationToken);
        var priorGroups = priorCutoff < priorStart
            ? new List<VarietyTotalRow>()
            : await VarietyTotalsQuery(facility, priorYear, priorCutoff).ToListAsync(cancellationToken);
        var priorLookup = priorGroups.ToDictionary(x => x.VarietyKey, x => x.Bins, StringComparer.OrdinalIgnoreCase);
        var varieties = selectedGroups
            .Select(x => new RunVarietyTotalViewModel(
                x.VarietyKey,
                x.FruitProfileId,
                x.Variety,
                x.ProductionType,
                x.IsOrganic,
                x.Bins,
                priorLookup.GetValueOrDefault(x.VarietyKey)))
            .OrderBy(x => x.Variety)
            .ThenBy(x => x.ProductionType)
            .ToList();

        var sourceRows = selectedCutoff < selectedStart
            ? new List<WeeklySourceRow>()
            : await ValidLines(cropYear, selectedCutoff)
                .Where(x => (x.ActualRunId != null ? x.ActualRun!.RunFacilityCodeSnapshot : x.ReportingFacilityCodeSnapshot) == facility)
                .OrderBy(x => x.RunAt)
                .Select(x => new WeeklySourceRow(
                    x.Id,
                    x.ActualRunId,
                    x.ReportingFruitProfileIdSnapshot!.Value,
                    x.ReportingVarietyCodeSnapshot!,
                    x.ProductionTypeSnapshot!,
                    x.IsOrganicSnapshot!.Value,
                    x.GrowerNumberSnapshot!,
                    x.RunAt,
                    x.BinsRun))
                .Take(MaximumWeeklySourceRows + 1)
                .ToListAsync(cancellationToken);
        if (sourceRows.Count > MaximumWeeklySourceRows)
        {
            throw new InvalidOperationException($"Run reporting detail exceeds the safe limit of {MaximumWeeklySourceRows} lines. Narrow the selected facility and crop year.");
        }

        var weeks = sourceRows
            .GroupBy(x => new
            {
                x.VarietyKey,
                x.Variety,
                x.ProductionType,
                WeekStart = WeekStart(businessTime.PacificDate(x.RunAt))
            })
            .Select(group => new RunWeekTotalViewModel(
                group.Key.VarietyKey,
                group.Key.Variety,
                group.Key.ProductionType,
                group.Key.WeekStart,
                group.Key.WeekStart.AddDays(6),
                group.Sum(x => x.Bins),
                group.GroupBy(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase)
                    .Select(grower => new RunGrowerTotalViewModel(
                        grower.Key,
                        grower.Sum(x => x.Bins),
                        grower.Select(x => x.ActualRunId is null ? $"L:{x.EntryId}" : $"A:{x.ActualRunId}").Distinct().Count()))
                    .OrderBy(grower => grower.GrowerNumber)
                    .ToList()))
            .OrderBy(x => x.Variety)
            .ThenByDescending(x => x.WeekStart)
            .ToList();

        var detail = new RunTotalsDetailViewModel
        {
            Facility = facility,
            CropYear = cropYear,
            TotalBins = varieties.Sum(x => x.Bins),
            PriorCropYear = priorYear,
            PriorBins = priorGroups.Sum(x => x.Bins),
            SelectedStart = selectedStart,
            SelectedCutoff = selectedCutoff,
            PriorStart = priorStart,
            PriorCutoff = priorCutoff,
            Varieties = varieties,
            Weeks = weeks,
            SelectedVarietyKey = filter.ReportVarietyKey,
            SelectedWeekStart = filter.ReportWeekStart,
            SelectedGrowerNumber = filter.ReportGrowerNumber,
            SupportingPage = Math.Max(1, filter.ReportPage)
        };
        if (!string.IsNullOrWhiteSpace(filter.ReportVarietyKey)
            && filter.ReportWeekStart is DateOnly weekStart
            && !string.IsNullOrWhiteSpace(filter.ReportGrowerNumber))
        {
            var records = await GetSupportingRecordsAsync(
                facility,
                cropYear,
                selectedCutoff,
                filter.ReportVarietyKey,
                weekStart,
                filter.ReportGrowerNumber,
                detail.SupportingPage,
                cancellationToken);
            detail.HasMoreSupportingRecords = records.Count > SupportingPageSize;
            detail.SupportingRecords = records.Take(SupportingPageSize).ToList();
        }
        return detail;
    }

    private IQueryable<VarietyTotalRow> VarietyTotalsQuery(string facility, int cropYear, DateOnly cutoff) =>
        ValidLines(cropYear, cutoff)
            .Where(x => (x.ActualRunId != null ? x.ActualRun!.RunFacilityCodeSnapshot : x.ReportingFacilityCodeSnapshot) == facility)
            .GroupBy(x => new
            {
                FruitProfileId = x.ReportingFruitProfileIdSnapshot!.Value,
                Variety = x.ReportingVarietyCodeSnapshot!,
                ProductionType = x.ProductionTypeSnapshot!,
                IsOrganic = x.IsOrganicSnapshot!.Value
            })
            .Select(x => new VarietyTotalRow(
                x.Key.FruitProfileId,
                x.Key.Variety,
                x.Key.ProductionType,
                x.Key.IsOrganic,
                x.Sum(y => y.BinsRun)));

    private async Task<IReadOnlyList<RunSupportingRecordViewModel>> GetSupportingRecordsAsync(
        string facility,
        int cropYear,
        DateOnly cutoff,
        string varietyKey,
        DateOnly weekStart,
        string growerNumber,
        int page,
        CancellationToken cancellationToken)
    {
        if (!TryParseVarietyKey(varietyKey, out var fruitProfileId, out var variety, out var productionType, out var organic))
        {
            return [];
        }
        var weekStartUtc = UtcStart(weekStart);
        var weekEndUtc = UtcEndExclusive(weekStart.AddDays(6));
        return await ValidLines(cropYear, cutoff)
            .Where(x => (x.ActualRunId != null ? x.ActualRun!.RunFacilityCodeSnapshot : x.ReportingFacilityCodeSnapshot) == facility)
            .Where(x => x.ReportingFruitProfileIdSnapshot == fruitProfileId
                && x.ReportingVarietyCodeSnapshot == variety
                && x.ProductionTypeSnapshot == productionType
                && x.IsOrganicSnapshot == organic
                && x.GrowerNumberSnapshot == growerNumber
                && x.RunAt >= weekStartUtc && x.RunAt < weekEndUtc)
            .OrderBy(x => x.RunAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * SupportingPageSize)
            .Take(SupportingPageSize + 1)
            .Select(x => new RunSupportingRecordViewModel(
                x.Id,
                x.ActualRunId,
                x.ActualRunId == null ? "Legacy Bins Run" : "Actual Run",
                x.ActualRunId == null ? $"/BinsRun?Section=Activity#bins-run-{x.Id}" : $"/BinsRun/ActualRuns/{x.ActualRunId}",
                x.RunAt,
                x.CreatedByUser == null ? "Unknown" : x.CreatedByUser.DisplayName,
                x.ActualRunId != null ? x.ActualRun!.RunFacilityCodeSnapshot! : x.ReportingFacilityCodeSnapshot!,
                x.Warehouse.Code,
                x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                x.LotNumber,
                x.GrowerNumberSnapshot!,
                x.ReportingCropYearSnapshot!.Value,
                x.ReportingVarietyCodeSnapshot!,
                x.ProductionTypeSnapshot!,
                x.BinsRun,
                x.ActualRunId == null ? "Legacy active" : "Active"))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<RunReportingIssueViewModel>> GetNeedsReviewAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.BinsRunEntries.AsNoTracking()
            .OrderByDescending(x => x.RunAt)
            .ThenByDescending(x => x.Id)
            .Take(MaximumNeedsReviewCandidateRows)
            .Select(x => new IssueCandidate
            {
                Id = x.Id,
                ActualRunId = x.ActualRunId,
                TransactionType = x.TransactionType,
                IsReversed = x.IsReversed,
                ReversesBinsRunEntryId = x.ReversesBinsRunEntryId,
                HasReversal = dbContext.BinsRunEntries.Any(reversal => reversal.ReversesBinsRunEntryId == x.Id),
                RevisionIsCurrent = x.ActualRunRevision == null || x.ActualRunRevision.IsCurrent,
                ActualRunStatus = x.ActualRun == null ? null : x.ActualRun.Status,
                RunFacility = x.ActualRunId != null ? x.ActualRun!.RunFacilityCodeSnapshot : x.ReportingFacilityCodeSnapshot,
                FacilityAssignmentSource = x.ActualRunId != null ? x.ActualRun!.RunFacilityAssignmentSource : x.ReportingFacilityAssignmentSource,
                SourceFacility = x.Warehouse.Code,
                CropYear = x.ReportingCropYearSnapshot,
                FruitProfileId = x.ReportingFruitProfileIdSnapshot,
                Variety = x.ReportingVarietyCodeSnapshot ?? "",
                ProductionType = x.ProductionTypeSnapshot,
                IsOrganic = x.IsOrganicSnapshot,
                GrowerNumber = x.GrowerNumberSnapshot,
                ReceiptGrowerNumber = x.Receipt == null ? null : x.Receipt.GrowerNumber,
                GrowerLotNumber = x.GrowerLot == null ? null : x.GrowerLot.LotNumber,
                CreatedByUserId = x.CreatedByUserId,
                RecordedUser = x.CreatedByUser == null ? "Unknown" : x.CreatedByUser.DisplayName,
                EmploymentFacility = x.CreatedByUser == null ? null : x.CreatedByUser.EmploymentFacility,
                RunAt = x.RunAt,
                Bins = x.BinsRun
            })
            .ToListAsync(cancellationToken);

        var issues = new List<RunReportingIssueViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var isQuantityLine = (row.TransactionType == ActualRunTransactionTypes.Depletion && row.ActualRunId is not null)
                || (row.TransactionType == ActualRunTransactionTypes.Legacy && row.ActualRunId is null);

            void Add(string type, string explanation)
            {
                if (!seen.Add($"{row.Id}:{type}")) return;
                issues.Add(new RunReportingIssueViewModel(
                    type,
                    explanation,
                    row.TransactionType == ActualRunTransactionTypes.Reversal ? 0 : row.Bins,
                    row.CropYear,
                    row.Variety,
                    row.RecordedUser,
                    row.RunAt,
                    row.ActualRunId is null ? $"Legacy Bins Run #{row.Id}" : $"Actual Run #{row.ActualRunId}, line #{row.Id}",
                    row.ActualRunId is null ? $"/BinsRun?Section=Activity#bins-run-{row.Id}" : $"/BinsRun/ActualRuns/{row.ActualRunId}",
                    row.Id));
            }

            if (isQuantityLine && row.CropYear is null)
            {
                Add("Missing crop year", "The operational line has no authoritative fruit crop year.");
            }
            else if (isQuantityLine && row.CropYear is int cropYear)
            {
                var runDate = businessTime.PacificDate(row.RunAt);
                if (runDate < PeriodStart(cropYear) || runDate > PeriodEnd(cropYear))
                {
                    Add("Crop year outside reporting period", $"The Pacific run date {runDate:MMM d, yyyy} is outside {PeriodStart(cropYear):MMM d, yyyy} through {PeriodEnd(cropYear):MMM d, yyyy}.");
                }
            }
            if (isQuantityLine && NormalizeFacility(row.RunFacility) is null)
            {
                Add("Missing Run Facility", "No immutable WP or EBS reporting facility is persisted.");
                Add("Historical attribution unresolved", "The record cannot be deterministically attributed to WP or EBS.");
            }
            if (isQuantityLine && row.CreatedByUserId is null)
            {
                Add("Unknown recording employee", "The recording user is missing.");
            }
            var employment = EmploymentFacilities.Normalize(row.EmploymentFacility);
            if (isQuantityLine && employment == EmploymentFacilities.Unassigned)
            {
                Add("Employee employment is Unassigned", "The recorded employee currently has no deterministic employment assignment.");
            }
            if (isQuantityLine
                && employment is EmploymentFacilities.Wp or EmploymentFacilities.Ebs
                && NormalizeFacility(row.RunFacility) is string persisted
                && !string.Equals(employment, persisted, StringComparison.OrdinalIgnoreCase))
            {
                Add("Run Facility conflicts with employment", $"The persisted {persisted} Run Facility conflicts with the employee's current {employment} assignment.");
            }
            if (isQuantityLine && employment == EmploymentFacilities.Shared && NormalizeFacility(row.RunFacility) is null)
            {
                Add("Shared / Management run missing explicit facility", "A Shared / Management run has no explicit saved WP or EBS choice.");
            }
            if (isQuantityLine && row.FruitProfileId is null)
            {
                Add("Missing fruit-profile identity", "No canonical fruit-profile identity is persisted.");
            }
            if (isQuantityLine && string.IsNullOrWhiteSpace(row.Variety))
            {
                Add("Missing canonical variety", "The reporting variety snapshot is missing.");
            }
            if (isQuantityLine && (string.IsNullOrWhiteSpace(row.ProductionType) || row.IsOrganic is null))
            {
                Add("Missing production type", "Production type or Organic/Conventional identity is missing.");
            }
            if (isQuantityLine && string.IsNullOrWhiteSpace(row.GrowerNumber))
            {
                var sourceNumbers = new[] { row.ReceiptGrowerNumber, row.GrowerLotNumber }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Add(sourceNumbers.Count > 1 ? "Ambiguous grower number" : "Missing grower number",
                    sourceNumbers.Count > 1
                        ? "Receipt and Grower Lot identify different grower numbers."
                        : "No authoritative grower-number snapshot is persisted.");
            }
            if (isQuantityLine && NormalizeFacility(row.SourceFacility) is null)
            {
                Add("Missing or unknown source facility", "The source warehouse is not a recognized WP or EBS facility.");
            }
            if (row.IsReversed && !row.HasReversal && row.TransactionType != ActualRunTransactionTypes.Reversal)
            {
                Add("Unresolved correction or reversal", "The line is marked reversed but has no linked reversal entry.");
            }
            if (row.TransactionType == ActualRunTransactionTypes.Reversal && row.ReversesBinsRunEntryId is null)
            {
                Add("Unresolved correction or reversal", "The reversal has no source-line relationship.");
            }
            if (row.ActualRunStatus == ActualRunStatuses.Canceled && !row.IsReversed && row.TransactionType == ActualRunTransactionTypes.Depletion)
            {
                Add("Canceled run still contributing quantity", "A canceled run contains an unreversed depletion line.");
            }
            if (row.ActualRunId is not null && !row.RevisionIsCurrent && !row.IsReversed && row.TransactionType == ActualRunTransactionTypes.Depletion)
            {
                Add("Superseded line still contributing quantity", "A non-current Actual Run revision contains an unreversed depletion line.");
            }
            if (row.TransactionType == ActualRunTransactionTypes.Legacy && row.ActualRunId is not null)
            {
                Add("Legacy Bins Run represented by Actual Run", "The line is marked Legacy while also linked to an Actual Run and could otherwise be counted twice.");
            }
        }

        var duplicateEntryIds = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => !x.IsReversed
                && x.TransactionType == ActualRunTransactionTypes.Depletion
                && x.ActualRunRevisionId != null
                && x.ActualRunRevision!.IsCurrent
                && dbContext.BinsRunEntries.Any(other => other.Id != x.Id
                    && !other.IsReversed
                    && other.TransactionType == ActualRunTransactionTypes.Depletion
                    && other.ActualRunRevisionId == x.ActualRunRevisionId
                    && other.InventoryAdjustmentId == x.InventoryAdjustmentId))
            .Select(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var id in duplicateEntryIds)
        {
            var row = rows.SingleOrDefault(x => x.Id == id);
            if (row is null || !seen.Add($"{id}:Duplicate active run quantity")) continue;
            issues.Add(new RunReportingIssueViewModel(
                "Duplicate active run quantity",
                "More than one active depletion line references the same inventory adjustment.",
                row.Bins,
                row.CropYear,
                row.Variety,
                row.RecordedUser,
                row.RunAt,
                $"Actual Run #{row.ActualRunId}, line #{row.Id}",
                $"/BinsRun/ActualRuns/{row.ActualRunId}",
                row.Id));
        }
        return issues.OrderByDescending(x => x.RunAt).ThenBy(x => x.IssueType).ToList();
    }

    private static string? NormalizeFacility(string? facility) => facility?.Trim().ToUpperInvariant() switch
    {
        EmploymentFacilities.Wp => EmploymentFacilities.Wp,
        EmploymentFacilities.Ebs => EmploymentFacilities.Ebs,
        _ => null
    };

    private static string VarietyKey(int fruitProfileId, string variety, string productionType, bool organic) =>
        $"{fruitProfileId}|{Uri.EscapeDataString(variety)}|{Uri.EscapeDataString(productionType)}|{organic}";

    private static bool TryParseVarietyKey(string key, out int fruitProfileId, out string variety, out string productionType, out bool organic)
    {
        fruitProfileId = 0;
        variety = "";
        productionType = "";
        organic = false;
        var parts = key.Split('|');
        return parts.Length == 4
            && int.TryParse(parts[0], out fruitProfileId)
            && (variety = Uri.UnescapeDataString(parts[1])).Length > 0
            && (productionType = Uri.UnescapeDataString(parts[2])).Length > 0
            && bool.TryParse(parts[3], out organic);
    }

    private sealed record FacilityTotal(string Facility, int Bins);
    private sealed record VarietyTotalRow(int FruitProfileId, string Variety, string ProductionType, bool IsOrganic, int Bins)
    {
        public string VarietyKey => RunReportingService.VarietyKey(FruitProfileId, Variety, ProductionType, IsOrganic);
    }
    private sealed record WeeklySourceRow(long EntryId, long? ActualRunId, int FruitProfileId, string Variety, string ProductionType, bool IsOrganic, string GrowerNumber, DateTimeOffset RunAt, int Bins)
    {
        public string VarietyKey => RunReportingService.VarietyKey(FruitProfileId, Variety, ProductionType, IsOrganic);
    }

    private sealed class IssueCandidate
    {
        public long Id { get; init; }
        public long? ActualRunId { get; init; }
        public string TransactionType { get; init; } = "";
        public bool IsReversed { get; init; }
        public long? ReversesBinsRunEntryId { get; init; }
        public bool HasReversal { get; init; }
        public bool RevisionIsCurrent { get; init; }
        public string? ActualRunStatus { get; init; }
        public string? RunFacility { get; init; }
        public string? FacilityAssignmentSource { get; init; }
        public string SourceFacility { get; init; } = "";
        public int? CropYear { get; init; }
        public int? FruitProfileId { get; init; }
        public string Variety { get; init; } = "";
        public string? ProductionType { get; init; }
        public bool? IsOrganic { get; init; }
        public string? GrowerNumber { get; init; }
        public string? ReceiptGrowerNumber { get; init; }
        public string? GrowerLotNumber { get; init; }
        public int? CreatedByUserId { get; init; }
        public string RecordedUser { get; init; } = "";
        public string? EmploymentFacility { get; init; }
        public DateTimeOffset RunAt { get; init; }
        public int Bins { get; init; }
    }
}
