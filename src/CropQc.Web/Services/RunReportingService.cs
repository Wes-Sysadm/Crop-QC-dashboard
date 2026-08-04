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
    public const int DefaultAuthoritativeStartCropYear = 2026;
    public const int MaximumWeeklySourceRows = 5000;
    public const int SupportingPageSize = 50;
    public const int NeedsReviewPageSize = 100;

    private int StartMonth => Math.Clamp(configuration.GetValue("RunReporting:CropYearStartMonth", 7), 1, 12);
    private int StartDay => Math.Clamp(configuration.GetValue("RunReporting:CropYearStartDay", 15), 1, 28);
    private int AuthoritativeStartCropYear => Math.Clamp(
        configuration.GetValue("RunReporting:AuthoritativeStartCropYear", DefaultAuthoritativeStartCropYear),
        2000,
        2200);

    public async Task<RunReportingPageViewModel> GetAsync(
        BinsRunFilterForm filter,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var today = businessTime.PacificDate(businessTime.UtcNow);
        var currentCropYear = CurrentCropYear(today, StartMonth, StartDay);
        var summaryYears = Enumerable.Range(0, 3)
            .Select(offset => currentCropYear - offset)
            .Where(year => year >= AuthoritativeStartCropYear)
            .ToList();
        var summary = new Dictionary<string, List<RunCropYearSummaryViewModel>>(StringComparer.OrdinalIgnoreCase)
        {
            [EmploymentFacilities.Wp] = [],
            [EmploymentFacilities.Ebs] = []
        };
        foreach (var cropYear in summaryYears)
        {
            var summaryCutoff = today < PeriodEnd(cropYear) ? today : PeriodEnd(cropYear);
            var totals = (await GetFacilityTotalsAsync(cropYear, summaryCutoff, cancellationToken))
                .ToDictionary(x => x.Facility, x => x.Bins, StringComparer.OrdinalIgnoreCase);
            foreach (var facility in new[] { EmploymentFacilities.Wp, EmploymentFacilities.Ebs })
            {
                summary[facility].Add(new RunCropYearSummaryViewModel(cropYear, totals.GetValueOrDefault(facility)));
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
            AuthoritativeStartCropYear = AuthoritativeStartCropYear,
            FacilitySummaries = summary.Select(x => new RunFacilitySummaryViewModel(x.Key, x.Value)).ToList(),
            OlderCropYears = await GetOlderYearsAsync(currentCropYear - 2, cancellationToken),
            CanViewNeedsReview = canViewNeedsReview
        };

        if (filter.Section.Equals("RunTotals", StringComparison.OrdinalIgnoreCase)
            && filter.ReportCropYear is int selectedYear
            && selectedYear >= AuthoritativeStartCropYear
            && NormalizeFacility(filter.ReportFacility) is string selectedFacility)
        {
            model.Detail = await GetDetailAsync(selectedFacility, selectedYear, filter, today, cancellationToken);
        }
        else if (filter.Section.Equals("NeedsReview", StringComparison.OrdinalIgnoreCase) && canViewNeedsReview)
        {
            var page = Math.Max(1, filter.ReportPage);
            var review = await GetNeedsReviewPageAsync(page, cancellationToken);
            model.NeedsReviewPage = page;
            model.HasMoreIssues = review.HasMoreRecords;
            model.Issues = review.Issues;
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
        if (cropYear < AuthoritativeStartCropYear)
        {
            return dbContext.BinsRunEntries.AsNoTracking().Where(_ => false);
        }
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
            .Where(x => x.ReportingCropYearSnapshot >= AuthoritativeStartCropYear
                && x.ReportingCropYearSnapshot < newestExcluded)
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
        var priorYear = cropYear > AuthoritativeStartCropYear ? cropYear - 1 : (int?)null;
        var priorStart = priorYear is int authoritativePriorYear ? PeriodStart(authoritativePriorYear) : (DateOnly?)null;
        var priorCutoff = priorYear is not null ? EquivalentPriorCutoff(selectedCutoff) : (DateOnly?)null;

        var selectedGroups = selectedCutoff < selectedStart
            ? new List<VarietyTotalRow>()
            : await VarietyTotalsQuery(facility, cropYear, selectedCutoff).ToListAsync(cancellationToken);
        var priorGroups = priorYear is null || priorCutoff!.Value < priorStart!.Value
            ? new List<VarietyTotalRow>()
            : await VarietyTotalsQuery(facility, priorYear.Value, priorCutoff!.Value).ToListAsync(cancellationToken);
        var selectedLookup = selectedGroups.ToDictionary(x => x.VarietyKey, StringComparer.OrdinalIgnoreCase);
        var priorLookup = priorGroups.ToDictionary(x => x.VarietyKey, x => x.Bins, StringComparer.OrdinalIgnoreCase);
        var varieties = selectedGroups.Concat(priorGroups)
            .GroupBy(x => x.VarietyKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(identity => new RunVarietyTotalViewModel(
                identity.VarietyKey,
                identity.FruitProfileId,
                identity.Variety,
                identity.ProductionType,
                identity.IsOrganic,
                selectedLookup.TryGetValue(identity.VarietyKey, out var selected) ? selected.Bins : 0,
                priorLookup.GetValueOrDefault(identity.VarietyKey)))
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
            PriorBins = priorYear is null ? 0 : priorGroups.Sum(x => x.Bins),
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

    private async Task<NeedsReviewPage> GetNeedsReviewPageAsync(int page, CancellationToken cancellationToken)
    {
        var authoritativeRecordStartUtc = UtcStart(PeriodStart(AuthoritativeStartCropYear));
        var rows = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ReportingCropYearSnapshot >= AuthoritativeStartCropYear
                || (x.ReportingCropYearSnapshot == null
                    && ((x.CropYear
                            ?? (x.Receipt == null ? null : (int?)x.Receipt.CropYear)
                            ?? (x.SourceInventoryAdjustment == null ? null : x.SourceInventoryAdjustment.CropYear)
                            ?? x.InventoryAdjustment.CropYear) >= AuthoritativeStartCropYear
                        || ((x.CropYear
                                ?? (x.Receipt == null ? null : (int?)x.Receipt.CropYear)
                                ?? (x.SourceInventoryAdjustment == null ? null : x.SourceInventoryAdjustment.CropYear)
                                ?? x.InventoryAdjustment.CropYear) == null
                            && x.CreatedAt >= authoritativeRecordStartUtc))))
            .OrderByDescending(x => x.RunAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * NeedsReviewPageSize)
            .Take(NeedsReviewPageSize + 1)
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
                OperationalCropYear = x.CropYear
                    ?? (x.Receipt == null ? null : (int?)x.Receipt.CropYear)
                    ?? (x.SourceInventoryAdjustment == null ? null : x.SourceInventoryAdjustment.CropYear)
                    ?? x.InventoryAdjustment.CropYear,
                FruitProfileId = x.ReportingFruitProfileIdSnapshot,
                Variety = x.ReportingVarietyCodeSnapshot ?? "",
                ProductionType = x.ProductionTypeSnapshot,
                IsOrganic = x.IsOrganicSnapshot,
                GrowerNumber = x.GrowerNumberSnapshot,
                ReceiptGrowerNumber = x.Receipt == null ? null : x.Receipt.GrowerNumber,
                CreatedByUserId = x.CreatedByUserId,
                RecordedUser = x.CreatedByUser == null ? "Unknown" : x.CreatedByUser.DisplayName,
                RunAt = x.RunAt,
                Bins = x.BinsRun
            })
            .ToListAsync(cancellationToken);
        var hasMoreRecords = rows.Count > NeedsReviewPageSize;
        rows = rows.Take(NeedsReviewPageSize).ToList();

        var userIds = rows.Where(x => x.CreatedByUserId is not null)
            .Select(x => x.CreatedByUserId!.Value)
            .Distinct()
            .ToList();
        var employmentUsers = await dbContext.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new EmploymentUser(x.Id, x.EmploymentFacility, x.EmploymentEffectiveAt))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var employmentHistory = await dbContext.UserEmploymentHistory.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.PreviousEmploymentFacility,
                x.EmploymentFacility,
                x.EffectiveAt
            })
            .ToListAsync(cancellationToken);
        var employmentHistoryByUser = employmentHistory
            .GroupBy(x => x.UserId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<EmploymentTransition>)x
                    .OrderBy(item => item.EffectiveAt)
                    .ThenBy(item => item.Id)
                    .Select(item => new EmploymentTransition(
                        item.UserId,
                        item.PreviousEmploymentFacility,
                        item.EmploymentFacility,
                        item.EffectiveAt))
                    .ToList());

        var issues = new List<RunReportingIssueViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var isQuantityLine = row.TransactionType == ActualRunTransactionTypes.Legacy && row.ActualRunId is null && !row.IsReversed
                || row.TransactionType == ActualRunTransactionTypes.Depletion
                    && row.ActualRunId is not null
                    && !row.IsReversed
                    && row.RevisionIsCurrent
                    && row.ActualRunStatus == ActualRunStatuses.Active;

            void Add(string type, string explanation)
            {
                if (!seen.Add($"{row.Id}:{type}")) return;
                issues.Add(new RunReportingIssueViewModel(
                    type,
                    explanation,
                    row.TransactionType == ActualRunTransactionTypes.Reversal ? 0 : row.Bins,
                    row.CropYear ?? row.OperationalCropYear,
                    row.Variety,
                    row.RecordedUser,
                    row.RunAt,
                    row.ActualRunId is null ? $"Legacy Bins Run #{row.Id}" : $"Actual Run #{row.ActualRunId}, line #{row.Id}",
                    row.ActualRunId is null ? $"/BinsRun?Section=Activity#bins-run-{row.Id}" : $"/BinsRun/ActualRuns/{row.ActualRunId}",
                    row.Id));
            }

            if (isQuantityLine && row.CropYear is null && row.OperationalCropYear is null)
            {
                Add("Missing crop year", "The operational line has no authoritative fruit crop year.");
            }
            else if (isQuantityLine && row.CropYear is null && row.OperationalCropYear >= AuthoritativeStartCropYear)
            {
                Add("Missing reporting crop-year snapshot", "The authoritative operational crop year was not persisted in the immutable reporting snapshot.");
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
            var employment = row.CreatedByUserId is int createdByUserId
                && employmentUsers.TryGetValue(createdByUserId, out var employmentUser)
                    ? ResolveEmploymentAt(
                        employmentUser.CurrentFacility,
                        employmentUser.CurrentEffectiveAt,
                        employmentHistoryByUser.GetValueOrDefault(createdByUserId) ?? [],
                        row.RunAt)
                    : EmploymentFacilities.Unassigned;
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
            else if (isQuantityLine
                && employment == EmploymentFacilities.Shared
                && row.FacilityAssignmentSource != RunFacilityAssignmentSources.SharedSelection)
            {
                Add("Shared / Management run missing explicit facility", "A Shared / Management run must preserve an explicit saved WP or EBS choice.");
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
                Add("Missing grower number",
                    string.IsNullOrWhiteSpace(row.ReceiptGrowerNumber)
                        ? "No authoritative grower-number snapshot or receipt grower number is available. Grower Lot lot numbers are not grower numbers."
                        : "The authoritative receipt grower number was not persisted in the reporting snapshot.");
            }
            if (row.IsReversed && !row.HasReversal && row.TransactionType != ActualRunTransactionTypes.Reversal)
            {
                Add("Unresolved correction or reversal", "The line is marked reversed but has no linked reversal entry.");
            }
            if (row.TransactionType == ActualRunTransactionTypes.Reversal && row.ReversesBinsRunEntryId is null)
            {
                Add("Unresolved correction or reversal", "The reversal has no source-line relationship.");
            }
            if (row.TransactionType == ActualRunTransactionTypes.Legacy && row.ActualRunId is not null)
            {
                Add("Legacy Bins Run represented by Actual Run", "The line is marked Legacy while also linked to an Actual Run and could otherwise be counted twice.");
            }
        }

        var pageEntryIds = rows.Select(x => x.Id).ToList();
        var duplicateEntryIds = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => pageEntryIds.Contains(x.Id)
                && x.ReportingCropYearSnapshot >= AuthoritativeStartCropYear
                && !x.IsReversed
                && x.TransactionType == ActualRunTransactionTypes.Depletion
                && x.ActualRunRevisionId != null
                && x.ActualRunRevision!.IsCurrent
                && dbContext.BinsRunEntries.Any(other => other.Id != x.Id
                    && !other.IsReversed
                    && other.TransactionType == ActualRunTransactionTypes.Depletion
                    && other.ActualRunRevisionId == x.ActualRunRevisionId
                    && other.InventoryAdjustmentId == x.InventoryAdjustmentId))
            .Select(x => x.Id)
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
        return new NeedsReviewPage(
            issues.OrderByDescending(x => x.RunAt).ThenBy(x => x.IssueType).ToList(),
            hasMoreRecords);
    }

    public static string ResolveEmploymentAt(
        string? currentFacility,
        DateTimeOffset? currentEffectiveAt,
        IReadOnlyList<EmploymentTransition> history,
        DateTimeOffset runAt)
    {
        var latest = history.LastOrDefault(x => x.EffectiveAt <= runAt);
        if (latest is not null)
        {
            return EmploymentFacilities.Normalize(latest.NewFacility) ?? EmploymentFacilities.Unassigned;
        }

        var earliest = history.FirstOrDefault();
        if (earliest is not null)
        {
            return EmploymentFacilities.Normalize(earliest.PreviousFacility) ?? EmploymentFacilities.Unassigned;
        }

        return currentEffectiveAt is null || currentEffectiveAt <= runAt
            ? EmploymentFacilities.Normalize(currentFacility) ?? EmploymentFacilities.Unassigned
            : EmploymentFacilities.Unassigned;
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
    private sealed record NeedsReviewPage(IReadOnlyList<RunReportingIssueViewModel> Issues, bool HasMoreRecords);
    private sealed record EmploymentUser(int Id, string? CurrentFacility, DateTimeOffset? CurrentEffectiveAt);
    public sealed record EmploymentTransition(
        int UserId,
        string? PreviousFacility,
        string? NewFacility,
        DateTimeOffset EffectiveAt);
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
        public int? OperationalCropYear { get; init; }
        public int? FruitProfileId { get; init; }
        public string Variety { get; init; } = "";
        public string? ProductionType { get; init; }
        public bool? IsOrganic { get; init; }
        public string? GrowerNumber { get; init; }
        public string? ReceiptGrowerNumber { get; init; }
        public int? CreatedByUserId { get; init; }
        public string RecordedUser { get; init; } = "";
        public DateTimeOffset RunAt { get; init; }
        public int Bins { get; init; }
    }
}
