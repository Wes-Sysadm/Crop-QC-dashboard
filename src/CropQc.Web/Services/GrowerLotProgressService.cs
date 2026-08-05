using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IGrowerLotProgressService
{
    Task<GrowerLotProgressPageViewModel> GetAsync(GrowerLotProgressFilterForm filter, CancellationToken cancellationToken);
}

public sealed class GrowerLotProgressService(
    CropQcDbContext dbContext,
    IBusinessTimeService businessTime,
    IVarietyColorService varietyColorService,
    IConfiguration configuration) : IGrowerLotProgressService
{
    public const int DefaultPageSize = 25;
    public const int SupportingPageSize = 50;
    public const int MaximumLotRunRows = 5000;

    private int StartMonth => Math.Clamp(configuration.GetValue("RunReporting:CropYearStartMonth", 7), 1, 12);
    private int StartDay => Math.Clamp(configuration.GetValue("RunReporting:CropYearStartDay", 15), 1, 28);
    private int AuthoritativeStartCropYear => Math.Clamp(
        configuration.GetValue("RunReporting:AuthoritativeStartCropYear", RunReportingService.DefaultAuthoritativeStartCropYear),
        2000,
        2200);

    public async Task<GrowerLotProgressPageViewModel> GetAsync(
        GrowerLotProgressFilterForm filter,
        CancellationToken cancellationToken)
    {
        var today = businessTime.PacificDate(businessTime.UtcNow);
        var currentCropYear = RunReportingService.CurrentCropYear(today, StartMonth, StartDay);
        var cropYear = Math.Max(AuthoritativeStartCropYear, filter.CropYear ?? currentCropYear);
        var facility = NormalizeFacilityFilter(filter.Facility);
        var page = Math.Max(1, filter.Page);
        filter.CropYear = cropYear;
        filter.Facility = facility;
        filter.Page = page;
        filter.Sort = NormalizeSort(filter.Sort);

        var varietyProfileIds = await ResolveVarietyProfileIdsAsync(filter.VarietyKey, cancellationToken);
        var receipts = ValidReceipts(cropYear, facility, filter, varietyProfileIds);
        var runs = ValidRunLines(cropYear, facility, filter, varietyProfileIds);

        var receiptTotals = receipts
            .GroupBy(x => x.GrowerNumber!)
            .Select(x => new GrowerContribution
            {
                GrowerNumber = x.Key,
                GrowerName = x.Min(y => y.GrowerName) ?? "",
                BinsReceived = x.Sum(y => y.BinCount),
                BinsRun = 0
            });
        var runTotals = runs
            .GroupBy(x => x.GrowerNumberSnapshot!)
            .Select(x => new GrowerContribution
            {
                GrowerNumber = x.Key,
                GrowerName = x.Min(y => y.GrowerName) ?? "",
                BinsReceived = 0,
                BinsRun = x.Sum(y => y.BinsRun)
            });
        var growerTotals = receiptTotals.Concat(runTotals)
            .GroupBy(x => x.GrowerNumber)
            .Select(x => new GrowerContribution
            {
                GrowerNumber = x.Key,
                GrowerName = x.Min(y => y.GrowerName) ?? "",
                BinsReceived = x.Sum(y => y.BinsReceived),
                BinsRun = x.Sum(y => y.BinsRun)
            });

        var growerCount = await growerTotals.CountAsync(cancellationToken);
        var aggregateTotals = await growerTotals
            .GroupBy(_ => 1)
            .Select(x => new { Received = x.Sum(y => y.BinsReceived), Run = x.Sum(y => y.BinsRun) })
            .SingleOrDefaultAsync(cancellationToken);
        var receivedLotCount = await receipts
            .Select(x => new
            {
                x.GrowerNumber,
                x.GrowerLotId,
                x.LotCode,
                x.FruitProfileId,
                x.FruitProfile.ProductionType,
                x.FruitProfile.IsOrganic
            })
            .Distinct()
            .CountAsync(cancellationToken);

        var orderedGrowers = OrderGrowers(growerTotals, filter.Sort);
        var pageGrowers = await orderedGrowers
            .Skip((page - 1) * DefaultPageSize)
            .Take(DefaultPageSize + 1)
            .ToListAsync(cancellationToken);
        var hasNextPage = pageGrowers.Count > DefaultPageSize;
        pageGrowers = pageGrowers.Take(DefaultPageSize).ToList();
        var pageGrowerNumbers = pageGrowers.Select(x => x.GrowerNumber).ToList();

        var receiptRows = await receipts
            .Where(x => pageGrowerNumbers.Contains(x.GrowerNumber!))
            .GroupBy(x => new
            {
                GrowerNumber = x.GrowerNumber!,
                x.FruitProfileId,
                Variety = x.FruitProfile.VarietyCode,
                x.FruitProfile.ProductionType,
                x.FruitProfile.IsOrganic,
                x.GrowerLotId,
                x.LotCode,
                Facility = x.Warehouse.Code
            })
            .Select(x => new ReceiptAggregateRow
            {
                GrowerNumber = x.Key.GrowerNumber,
                FruitProfileId = x.Key.FruitProfileId,
                Variety = x.Key.Variety,
                ProductionType = x.Key.ProductionType,
                IsOrganic = x.Key.IsOrganic,
                GrowerLotId = x.Key.GrowerLotId,
                LotNumber = x.Key.LotCode,
                Facility = x.Key.Facility,
                FirstReceiptAt = x.Min(y => y.ReceivedAt),
                LatestReceiptAt = x.Max(y => y.ReceivedAt),
                ReceiptCount = x.Count(),
                Bins = x.Sum(y => y.BinCount)
            })
            .ToListAsync(cancellationToken);
        var runRows = await runs
            .Where(x => pageGrowerNumbers.Contains(x.GrowerNumberSnapshot!))
            .GroupBy(x => new
            {
                GrowerNumber = x.GrowerNumberSnapshot!,
                FruitProfileId = x.ReportingFruitProfileIdSnapshot!.Value,
                Variety = x.ReportingVarietyCodeSnapshot!,
                ProductionType = x.ProductionTypeSnapshot!,
                IsOrganic = x.IsOrganicSnapshot!.Value,
                x.GrowerLotId,
                x.LotNumber
            })
            .Select(x => new RunAggregateRow
            {
                GrowerNumber = x.Key.GrowerNumber,
                FruitProfileId = x.Key.FruitProfileId,
                Variety = x.Key.Variety,
                ProductionType = x.Key.ProductionType,
                IsOrganic = x.Key.IsOrganic,
                GrowerLotId = x.Key.GrowerLotId,
                LotNumber = x.Key.LotNumber,
                Bins = x.Sum(y => y.BinsRun),
                RunRecordCount = x.Select(y => y.ActualRunId ?? -y.Id).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var canonicalColorKeys = receiptRows.Select(x => x.Variety)
            .Concat(runRows.Select(x => x.Variety))
            .Select(CanonicalVarietyKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var colors = await varietyColorService.GetResolvedColorsReadOnlyAsync(canonicalColorKeys, cancellationToken);
        var canonicalNameRows = await dbContext.CanonicalGrowerNumbers.AsNoTracking()
            .Where(x => x.IsActive && pageGrowerNumbers.Contains(x.GrowerNumber) && x.CanonicalGrower.IsActive)
            .Select(x => new { x.GrowerNumber, x.CropYear, x.Id, x.CanonicalGrower.DisplayName })
            .ToListAsync(cancellationToken);
        var canonicalNames = canonicalNameRows
            .GroupBy(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.CropYear).ThenBy(y => y.Id).First().DisplayName,
                StringComparer.OrdinalIgnoreCase);
        var growerModels = new List<GrowerProgressViewModel>();
        foreach (var total in pageGrowers)
        {
            var growerReceiptRows = receiptRows.Where(x => Same(x.GrowerNumber, total.GrowerNumber)).ToList();
            var growerRunRows = runRows.Where(x => Same(x.GrowerNumber, total.GrowerNumber)).ToList();
            canonicalNames.TryGetValue(total.GrowerNumber, out var canonicalName);
            var grower = new GrowerProgressViewModel
            {
                GrowerNumber = total.GrowerNumber,
                GrowerName = canonicalName ?? total.GrowerName,
                ReceivedLotCount = growerReceiptRows.Select(LotKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                BinsReceived = total.BinsReceived,
                BinsRun = total.BinsRun,
                IsExpanded = Same(filter.ExpandedGrowerNumber, total.GrowerNumber)
            };
            grower.Varieties = BuildVarieties(grower, growerReceiptRows, growerRunRows, colors, filter);
            growerModels.Add(grower);
        }

        var model = new GrowerLotProgressPageViewModel
        {
            AuthoritativeStartCropYear = AuthoritativeStartCropYear,
            CurrentCropYear = currentCropYear,
            Filter = filter,
            CropYears = Enumerable.Range(AuthoritativeStartCropYear, Math.Max(1, currentCropYear - AuthoritativeStartCropYear + 1))
                .OrderByDescending(x => x)
                .ToList(),
            VarietyOptions = await GetVarietyOptionsAsync(cancellationToken),
            GrowerCount = growerCount,
            ReceivedLotCount = receivedLotCount,
            BinsReceived = aggregateTotals?.Received ?? 0,
            BinsRun = aggregateTotals?.Run ?? 0,
            Growers = growerModels,
            Page = page,
            PageSize = DefaultPageSize,
            HasNextPage = hasNextPage,
            ExcludedIssues = await GetExcludedIssuesAsync(cropYear, cancellationToken)
        };

        await PopulateSelectedLotAsync(model, runs, cancellationToken);
        return model;
    }

    private IQueryable<Receipt> ValidReceipts(
        int cropYear,
        string facility,
        GrowerLotProgressFilterForm filter,
        IReadOnlyList<int>? varietyProfileIds)
    {
        var query = dbContext.Receipts.AsNoTracking()
            .Where(x => x.CropYear == cropYear && !x.IsDeleted && !x.IsTestData)
            .Where(x => x.GrowerNumber != null && x.GrowerNumber != ""
                && x.LotCode != ""
                && x.FruitProfile.VarietyCode != ""
                && x.FruitProfile.ProductionType != "");
        if (facility != "All") query = query.Where(x => x.Warehouse.Code == facility);
        if (!string.IsNullOrWhiteSpace(filter.GrowerSearch))
        {
            var search = filter.GrowerSearch.Trim();
            query = query.Where(x => x.GrowerNumber!.Contains(search) || x.GrowerName.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(filter.LotSearch))
        {
            var lot = filter.LotSearch.Trim();
            query = query.Where(x => x.LotCode.Contains(lot));
        }
        if (varietyProfileIds is not null) query = query.Where(x => varietyProfileIds.Contains(x.FruitProfileId));
        if (filter.ProductionType == "Organic") query = query.Where(x => x.FruitProfile.IsOrganic);
        if (filter.ProductionType == "Conventional") query = query.Where(x => !x.FruitProfile.IsOrganic);
        return query;
    }

    private IQueryable<BinsRunEntry> ValidRunLines(
        int cropYear,
        string facility,
        GrowerLotProgressFilterForm filter,
        IReadOnlyList<int>? varietyProfileIds)
    {
        var query = dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ReportingCropYearSnapshot == cropYear)
            .Where(x => x.ReportingFruitProfileIdSnapshot != null
                && x.ReportingVarietyCodeSnapshot != null && x.ReportingVarietyCodeSnapshot != ""
                && x.ProductionTypeSnapshot != null && x.ProductionTypeSnapshot != ""
                && x.IsOrganicSnapshot != null
                && x.GrowerNumberSnapshot != null && x.GrowerNumberSnapshot != ""
                && x.LotNumber != "")
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
        if (facility != "All")
        {
            query = query.Where(x => (x.ActualRunId != null
                ? x.ActualRun!.RunFacilityCodeSnapshot
                : x.ReportingFacilityCodeSnapshot) == facility);
        }
        if (!string.IsNullOrWhiteSpace(filter.GrowerSearch))
        {
            var search = filter.GrowerSearch.Trim();
            query = query.Where(x => x.GrowerNumberSnapshot!.Contains(search) || x.GrowerName.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(filter.LotSearch))
        {
            var lot = filter.LotSearch.Trim();
            query = query.Where(x => x.LotNumber.Contains(lot));
        }
        if (varietyProfileIds is not null) query = query.Where(x => varietyProfileIds.Contains(x.ReportingFruitProfileIdSnapshot!.Value));
        if (filter.ProductionType == "Organic") query = query.Where(x => x.IsOrganicSnapshot == true);
        if (filter.ProductionType == "Conventional") query = query.Where(x => x.IsOrganicSnapshot == false);
        return query;
    }

    private IReadOnlyList<GrowerVarietyProgressViewModel> BuildVarieties(
        GrowerProgressViewModel grower,
        IReadOnlyList<ReceiptAggregateRow> receiptRows,
        IReadOnlyList<RunAggregateRow> runRows,
        IReadOnlyDictionary<string, VarietyColorResolved> colors,
        GrowerLotProgressFilterForm filter)
    {
        var identities = receiptRows.Select(x => new VarietyRow(x.FruitProfileId, x.Variety, x.ProductionType, x.IsOrganic))
            .Concat(runRows.Select(x => new VarietyRow(x.FruitProfileId, x.Variety, x.ProductionType, x.IsOrganic)))
            .GroupBy(x => VarietyKey(x.Variety, x.ProductionType, x.IsOrganic), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => VarietyColorService.NormalizeIdentity(x.Variety, x.Variety).Name)
            .ThenBy(x => x.ProductionType)
            .ThenBy(x => x.IsOrganic)
            .ToList();
        return identities.Select(identity =>
        {
            var varietyKey = VarietyKey(identity.Variety, identity.ProductionType, identity.IsOrganic);
            var matchingReceipts = receiptRows.Where(x => Same(VarietyKey(x.Variety, x.ProductionType, x.IsOrganic), varietyKey)).ToList();
            var matchingRuns = runRows.Where(x => Same(VarietyKey(x.Variety, x.ProductionType, x.IsOrganic), varietyKey)).ToList();
            var canonical = VarietyColorService.NormalizeIdentity(identity.Variety, identity.Variety);
            var color = colors.TryGetValue(canonical.Key, out var resolved)
                ? resolved
                : new VarietyColorResolved(canonical.Key, canonical.Name, VarietyColorService.FallbackColor(canonical.Key), false);
            var isExpanded = grower.IsExpanded && Same(filter.ExpandedVarietyKey, varietyKey);
            return new GrowerVarietyProgressViewModel
            {
                VarietyKey = varietyKey,
                FruitProfileId = identity.FruitProfileId,
                Variety = color.VarietyName,
                ProductionType = identity.ProductionType,
                IsOrganic = identity.IsOrganic,
                BinsReceived = matchingReceipts.Sum(x => x.Bins),
                BinsRun = matchingRuns.Sum(x => x.Bins),
                ReceivedLotCount = matchingReceipts.Select(LotKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                ColorHex = color.HexColor,
                TextColorHex = ReportingColorPresentation.TextColor(color.HexColor),
                IsColorConfigured = color.IsConfigured,
                IsExpanded = isExpanded,
                Lots = isExpanded ? BuildLots(grower.GrowerNumber, color.VarietyName, identity, matchingReceipts, matchingRuns, filter) : []
            };
        }).ToList();
    }

    private static IReadOnlyList<GrowerLotProgressViewModel> BuildLots(
        string growerNumber,
        string canonicalVariety,
        VarietyRow identity,
        IReadOnlyList<ReceiptAggregateRow> receipts,
        IReadOnlyList<RunAggregateRow> runs,
        GrowerLotProgressFilterForm filter)
    {
        var lotKeys = receipts.Select(LotKey).Concat(runs.Select(LotKey)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return lotKeys.Select(key =>
        {
            var received = receipts.Where(x => Same(LotKey(x), key)).ToList();
            var run = runs.Where(x => Same(LotKey(x), key)).ToList();
            var source = received.FirstOrDefault();
            var runSource = run.FirstOrDefault();
            return new GrowerLotProgressViewModel
            {
                LotKey = key,
                GrowerLotId = source?.GrowerLotId ?? runSource?.GrowerLotId,
                FruitProfileId = source?.FruitProfileId ?? runSource?.FruitProfileId ?? identity.FruitProfileId,
                LotNumber = source?.LotNumber ?? runSource?.LotNumber ?? "",
                GrowerNumber = growerNumber,
                Variety = canonicalVariety,
                ProductionType = identity.ProductionType,
                IsOrganic = identity.IsOrganic,
                ReceivingFacilities = string.Join(", ", received.Select(x => x.Facility).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
                FirstReceiptAt = received.Count == 0 ? null : received.Min(x => x.FirstReceiptAt),
                LatestReceiptAt = received.Count == 0 ? null : received.Max(x => x.LatestReceiptAt),
                ReceiptCount = received.Sum(x => x.ReceiptCount),
                BinsReceived = received.Sum(x => x.Bins),
                BinsRun = run.Sum(x => x.Bins),
                RunRecordCount = run.Sum(x => x.RunRecordCount),
                IsSelected = Same(filter.SelectedLotKey, key)
            };
        }).OrderBy(x => x.LotNumber, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.GrowerLotId).ToList();
    }

    private async Task PopulateSelectedLotAsync(
        GrowerLotProgressPageViewModel model,
        IQueryable<BinsRunEntry> runs,
        CancellationToken cancellationToken)
    {
        var selected = model.Growers.SelectMany(x => x.Varieties).SelectMany(x => x.Lots).SingleOrDefault(x => x.IsSelected);
        if (selected is null) return;
        var selectedRuns = FilterLot(runs, selected);
        var rows = await selectedRuns
            .OrderBy(x => x.RunAt)
            .ThenBy(x => x.Id)
            .Take(MaximumLotRunRows + 1)
            .Select(x => new LotRunRow(x.Id, x.ActualRunId, x.RunAt, x.BinsRun))
            .ToListAsync(cancellationToken);
        if (rows.Count > MaximumLotRunRows)
        {
            throw new InvalidOperationException($"Lot run detail exceeds the safe limit of {MaximumLotRunRows} lines. Narrow the facility filter.");
        }
        var cumulative = 0;
        var weeks = new List<GrowerLotWeekProgressViewModel>();
        foreach (var group in rows.GroupBy(x => RunReportingService.WeekStart(businessTime.PacificDate(x.RunAt))).OrderBy(x => x.Key))
        {
            cumulative += group.Sum(x => x.Bins);
            weeks.Add(new GrowerLotWeekProgressViewModel
            {
                WeekStart = group.Key,
                BinsRun = group.Sum(x => x.Bins),
                CumulativeBinsRun = cumulative,
                RunRecordCount = group.Select(x => x.ActualRunId ?? -x.EntryId).Distinct().Count(),
                IsSelected = model.Filter.SelectedWeekStart == group.Key
            });
        }
        if (weeks.Sum(x => x.BinsRun) != selected.BinsRun)
        {
            throw new InvalidOperationException("Lot weekly totals did not reconcile to the authoritative lot total.");
        }
        selected.Weeks = weeks.OrderByDescending(x => x.WeekStart).ToList();
        var selectedWeek = selected.Weeks.SingleOrDefault(x => x.IsSelected);
        if (selectedWeek is null) return;
        var startUtc = businessTime.UtcRangeForPacificDate(selectedWeek.WeekStart).Start;
        var endUtc = businessTime.UtcRangeForPacificDate(selectedWeek.WeekEnd.AddDays(1)).Start;
        var page = Math.Max(1, model.Filter.SupportingPage);
        var records = await selectedRuns
            .Where(x => x.RunAt >= startUtc && x.RunAt < endUtc)
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
        selectedWeek.HasMoreSupportingRecords = records.Count > SupportingPageSize;
        selectedWeek.SupportingRecords = records.Take(SupportingPageSize).ToList();
    }

    private static IQueryable<BinsRunEntry> FilterLot(IQueryable<BinsRunEntry> query, GrowerLotProgressViewModel lot)
    {
        if (lot.GrowerLotId is int growerLotId)
        {
            return query.Where(x => x.GrowerLotId == growerLotId
                && x.GrowerNumberSnapshot == lot.GrowerNumber
                && x.ReportingFruitProfileIdSnapshot == lot.FruitProfileId
                && x.ProductionTypeSnapshot == lot.ProductionType
                && x.IsOrganicSnapshot == lot.IsOrganic);
        }
        return query.Where(x => x.GrowerLotId == null
            && x.GrowerNumberSnapshot == lot.GrowerNumber
            && x.LotNumber == lot.LotNumber
            && x.ReportingFruitProfileIdSnapshot == lot.FruitProfileId
            && x.ProductionTypeSnapshot == lot.ProductionType
            && x.IsOrganicSnapshot == lot.IsOrganic);
    }

    private async Task<IReadOnlyList<int>?> ResolveVarietyProfileIdsAsync(string? varietyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(varietyKey)) return null;
        var canonicalKey = varietyKey.Split('|')[0];
        var profiles = await dbContext.FruitProfiles.AsNoTracking().Select(x => new { x.Id, x.VarietyCode }).ToListAsync(cancellationToken);
        return profiles.Where(x => Same(CanonicalVarietyKey(x.VarietyCode), canonicalKey)).Select(x => x.Id).ToList();
    }

    private async Task<IReadOnlyList<GrowerLotVarietyOptionViewModel>> GetVarietyOptionsAsync(CancellationToken cancellationToken)
    {
        var profiles = await dbContext.FruitProfiles.AsNoTracking()
            .Where(x => x.VarietyCode != "" && x.ProductionType != "")
            .Select(x => new { x.VarietyCode, x.ProductionType, x.IsOrganic })
            .Distinct()
            .ToListAsync(cancellationToken);
        return profiles
            .GroupBy(x => VarietyKey(x.VarietyCode, x.ProductionType, x.IsOrganic), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Select(x => new GrowerLotVarietyOptionViewModel(
                VarietyKey(x.VarietyCode, x.ProductionType, x.IsOrganic),
                VarietyColorService.NormalizeIdentity(x.VarietyCode, x.VarietyCode).Name,
                x.ProductionType,
                x.IsOrganic))
            .OrderBy(x => x.Variety)
            .ThenBy(x => x.ProductionType)
            .ToList();
    }

    private async Task<IReadOnlyList<GrowerLotProgressIssueViewModel>> GetExcludedIssuesAsync(int cropYear, CancellationToken cancellationToken)
    {
        var receiptIssues = await dbContext.Receipts.AsNoTracking()
            .Where(x => x.CropYear == cropYear && !x.IsDeleted && !x.IsTestData)
            .Where(x => x.GrowerNumber == null || x.GrowerNumber == "" || x.LotCode == "" || x.FruitProfile.VarietyCode == "" || x.FruitProfile.ProductionType == "")
            .OrderByDescending(x => x.ReceivedAt)
            .Take(10)
            .Select(x => new GrowerLotProgressIssueViewModel(
                "Receipt identity incomplete",
                "The authoritative receipt is excluded until grower, lot, variety, and production identity are complete.",
                $"/Receipts/{x.Id}"))
            .ToListAsync(cancellationToken);
        var runIssues = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ReportingCropYearSnapshot == cropYear && x.LotNumber == "")
            .OrderByDescending(x => x.RunAt)
            .Take(10)
            .Select(x => new GrowerLotProgressIssueViewModel(
                "Run lot identity incomplete",
                "The authoritative run line is excluded from grower and lot totals until exact lot identity is available.",
                x.ActualRunId == null ? $"/BinsRun?Section=Activity#bins-run-{x.Id}" : $"/BinsRun/ActualRuns/{x.ActualRunId}"))
            .ToListAsync(cancellationToken);
        return receiptIssues.Concat(runIssues).Take(20).ToList();
    }

    private static IOrderedQueryable<GrowerContribution> OrderGrowers(IQueryable<GrowerContribution> query, string sort) => sort switch
    {
        "GrowerName" => query.OrderBy(x => x.GrowerName).ThenBy(x => x.GrowerNumber),
        "BinsReceived" => query.OrderByDescending(x => x.BinsReceived).ThenBy(x => x.GrowerNumber),
        "BinsRun" => query.OrderByDescending(x => x.BinsRun).ThenBy(x => x.GrowerNumber),
        _ => query.OrderBy(x => x.GrowerNumber)
    };

    private static string NormalizeFacilityFilter(string? facility) => facility?.Trim().ToUpperInvariant() switch
    {
        EmploymentFacilities.Wp => EmploymentFacilities.Wp,
        EmploymentFacilities.Ebs => EmploymentFacilities.Ebs,
        _ => "All"
    };

    private static string NormalizeSort(string? sort) => sort switch
    {
        "GrowerName" => "GrowerName",
        "BinsReceived" => "BinsReceived",
        "BinsRun" => "BinsRun",
        _ => "GrowerNumber"
    };

    private static string CanonicalVarietyKey(string variety) => VarietyColorService.NormalizeIdentity(variety, variety).Key;
    private static string VarietyKey(string variety, string productionType, bool organic) =>
        $"{CanonicalVarietyKey(variety)}|{Uri.EscapeDataString(productionType)}|{organic}";
    private static string LotKey(ReceiptAggregateRow row) => LotKey(row.GrowerLotId, row.GrowerNumber, row.LotNumber, row.FruitProfileId, row.ProductionType, row.IsOrganic);
    private static string LotKey(RunAggregateRow row) => LotKey(row.GrowerLotId, row.GrowerNumber, row.LotNumber, row.FruitProfileId, row.ProductionType, row.IsOrganic);
    private static string LotKey(int? growerLotId, string grower, string lot, int fruitProfileId, string productionType, bool organic) =>
        growerLotId is int id
            ? $"G:{id}|{fruitProfileId}|{Uri.EscapeDataString(productionType)}|{organic}"
            : $"C:{Uri.EscapeDataString(grower)}|{Uri.EscapeDataString(lot)}|{fruitProfileId}|{Uri.EscapeDataString(productionType)}|{organic}";
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed class GrowerContribution
    {
        public string GrowerNumber { get; init; } = "";
        public string GrowerName { get; init; } = "";
        public int BinsReceived { get; init; }
        public int BinsRun { get; init; }
    }

    private sealed class ReceiptAggregateRow
    {
        public string GrowerNumber { get; init; } = "";
        public int FruitProfileId { get; init; }
        public string Variety { get; init; } = "";
        public string ProductionType { get; init; } = "";
        public bool IsOrganic { get; init; }
        public int? GrowerLotId { get; init; }
        public string LotNumber { get; init; } = "";
        public string Facility { get; init; } = "";
        public DateTimeOffset FirstReceiptAt { get; init; }
        public DateTimeOffset LatestReceiptAt { get; init; }
        public int ReceiptCount { get; init; }
        public int Bins { get; init; }
    }

    private sealed class RunAggregateRow
    {
        public string GrowerNumber { get; init; } = "";
        public int FruitProfileId { get; init; }
        public string Variety { get; init; } = "";
        public string ProductionType { get; init; } = "";
        public bool IsOrganic { get; init; }
        public int? GrowerLotId { get; init; }
        public string LotNumber { get; init; } = "";
        public int Bins { get; init; }
        public int RunRecordCount { get; init; }
    }

    private sealed record VarietyRow(int FruitProfileId, string Variety, string ProductionType, bool IsOrganic);
    private sealed record LotRunRow(long EntryId, long? ActualRunId, DateTimeOffset RunAt, int Bins);
}

public static class ReportingColorPresentation
{
    public static string TextColor(string colorHex)
    {
        var normalized = VarietyColorService.NormalizeHex(colorHex);
        if (!VarietyColorService.IsValidHexColor(normalized)) return "#FFFFFF";
        var red = Convert.ToInt32(normalized.Substring(1, 2), 16);
        var green = Convert.ToInt32(normalized.Substring(3, 2), 16);
        var blue = Convert.ToInt32(normalized.Substring(5, 2), 16);
        var luminance = (0.2126m * red + 0.7152m * green + 0.0722m * blue) / 255m;
        return luminance > 0.55m ? "#17212B" : "#FFFFFF";
    }
}
