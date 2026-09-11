using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRunSheetReconciliationService
{
    Task<RunSheetReconciliationViewModel?> GetAsync(string facility, int cropYear, CancellationToken cancellationToken);
}

public sealed class RunSheetReconciliationService(
    CropQcDbContext dbContext,
    IRunSheetSnapshotStore snapshotStore,
    RunSheetReconciliationOptions options,
    IBusinessTimeService businessTime) : IRunSheetReconciliationService
{
    public async Task<RunSheetReconciliationViewModel?> GetAsync(
        string facility,
        int cropYear,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled || cropYear != options.CropYear || facility is not (EmploymentFacilities.Wp or EmploymentFacilities.Ebs))
        {
            return null;
        }

        var state = snapshotStore.GetState();
        if (state.Snapshot is null)
        {
            return new RunSheetReconciliationViewModel
            {
                Availability = state.FailureMessage is null
                    ? RunSheetReconciliationStates.Loading
                    : RunSheetReconciliationStates.Unavailable,
                DiagnosticMessage = state.FailureMessage ?? "Run verification is waiting for its first successful Google Sheet refresh.",
                LastSuccessfulRefreshAt = state.LastSuccessfulRefreshAt,
                LastAttemptAt = state.LastAttemptAt
            };
        }

        if (state.IsStale)
        {
            return new RunSheetReconciliationViewModel
            {
                Availability = RunSheetReconciliationStates.Stale,
                DiagnosticMessage = state.FailureMessage
                    is null
                        ? "The last successful Google Sheet snapshot is stale. Current verification is temporarily unavailable."
                        : $"The last successful Google Sheet snapshot is stale. {state.FailureMessage}",
                LastSuccessfulRefreshAt = state.LastSuccessfulRefreshAt,
                LastAttemptAt = state.LastAttemptAt
            };
        }

        var cropRuns = await LoadCropRunsAsync(facility, cancellationToken);
        var sheetRuns = state.Snapshot.Runs
            .Where(x => x.Facility == facility && x.Date.Year == options.CropYear)
            .ToList();
        var matchedItems = RunSheetMatcher.Reconcile(
            facility,
            sheetRuns,
            cropRuns,
            businessTime.UtcNow,
            options.PendingWindow);
        var identityDiagnostics = await LoadIncompleteIdentityDiagnosticsAsync(facility, cancellationToken);
        var items = matchedItems
            .Concat(identityDiagnostics)
            .OrderByDescending(x => x.State == RunSheetReconciliationStates.Attention)
            .ThenByDescending(x => x.State == RunSheetReconciliationStates.Pending)
            .ThenBy(x => x.SheetDate ?? x.CropQcDate)
            .ThenBy(x => x.SheetVariety, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ActualRunIds.FirstOrDefault())
            .ToList();

        return new RunSheetReconciliationViewModel
        {
            Availability = RunSheetReconciliationStates.Available,
            LastSuccessfulRefreshAt = state.LastSuccessfulRefreshAt,
            LastAttemptAt = state.LastAttemptAt,
            AttentionNeededCount = items.Count(x => x.State == RunSheetReconciliationStates.Attention),
            PendingCount = items.Count(x => x.State == RunSheetReconciliationStates.Pending),
            MatchedCount = items.Count(x => x.State == RunSheetReconciliationStates.Match),
            Items = items
        };
    }

    private async Task<IReadOnlyList<CropPhysicalRun>> LoadCropRunsAsync(
        string facility,
        CancellationToken cancellationToken)
    {
        var lines = await AuthoritativeRunReportingQuery.ApplyValidRules(dbContext.BinsRunEntries.AsNoTracking())
            .Where(x => x.ActualRunId != null
                && x.ReportingCropYearSnapshot == options.CropYear
                && x.ActualRun!.RunFacilityCodeSnapshot == facility)
            .Select(x => new RunSheetCropLine(
                x.ActualRunId!.Value,
                x.ActualRun!.RunAt,
                x.ActualRun!.SalesDeskNameSnapshot,
                x.ReportingVarietyCodeSnapshot!,
                x.ProductionTypeSnapshot!,
                x.IsOrganicSnapshot!.Value,
                x.GrowerNumberSnapshot!,
                x.BinsRun))
            .ToListAsync(cancellationToken);

        return RunSheetCropRunBuilder.Build(facility, lines, businessTime);
    }

    private async Task<IReadOnlyList<RunSheetReconciliationItemViewModel>> LoadIncompleteIdentityDiagnosticsAsync(
        string facility,
        CancellationToken cancellationToken)
    {
        var lines = await AuthoritativeRunReportingQuery.ApplyIncompleteGrowerLotIdentityRules(
                AuthoritativeRunReportingQuery.ApplyActiveQuantityRules(dbContext.BinsRunEntries.AsNoTracking()))
            .Where(x => x.ActualRunId != null
                && x.CropYear == options.CropYear
                && x.ActualRun!.RunFacilityCodeSnapshot == facility)
            .Select(x => new
            {
                ActualRunId = x.ActualRunId!.Value,
                RunAt = x.ActualRun!.RunAt,
                x.BinsRun,
                LegacyVariety = x.VarietyCode ?? x.FruitProfile!.VarietyCode
            })
            .ToListAsync(cancellationToken);

        return lines
            .GroupBy(x => x.ActualRunId)
            .Select(group =>
            {
                var first = group.OrderBy(x => x.RunAt).First();
                var varieties = group.Select(x => RunSheetParser.NormalizeCode(x.LegacyVariety))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var variety = varieties.Count == 0 ? "Legacy variety unavailable" : string.Join(" / ", varieties);
                return new RunSheetReconciliationItemViewModel
                {
                    State = RunSheetReconciliationStates.Attention,
                    Facility = facility,
                    CropQcDate = businessTime.PacificDate(first.RunAt),
                    CropQcVariety = variety,
                    CropQcSalesDesk = facility == EmploymentFacilities.Wp ? "See Actual Run" : "N/A",
                    CropQcBins = group.Sum(x => x.BinsRun),
                    Reasons = [RunSheetReconciliationReasons.IncompleteCropQcReportingIdentity],
                    ActualRunIds = [group.Key],
                    DiagnosticMessage = $"Crop QC Actual Run #{group.Key} exists but has incomplete authoritative reporting identity and cannot be reconciled safely."
                };
            })
            .OrderBy(x => x.CropQcDate)
            .ThenBy(x => x.ActualRunIds[0])
            .ToList();
    }
}

public static class RunSheetCropRunBuilder
{
    public static IReadOnlyList<CropPhysicalRun> Build(
        string facility,
        IReadOnlyList<RunSheetCropLine> lines,
        IBusinessTimeService businessTime)
    {
        var atomicRuns = lines
            .GroupBy(x => x.ActualRunId)
            .Select(group =>
            {
                var first = group.First();
                var varieties = group.Select(x => RunSheetParser.NormalizeCode(x.Variety))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var productionTypes = group.Select(x => x.IsOrganic
                        ? RunSheetParser.OrganicProductionType
                        : RunSheetParser.ConventionalProductionType)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new CropPhysicalRun(
                    facility,
                    businessTime.PacificDate(first.RunAt),
                    varieties,
                    productionTypes,
                    facility == EmploymentFacilities.Wp
                        ? first.SalesDesk?.Trim() ?? "Unassigned"
                        : "N/A",
                    group.Sum(x => x.Bins),
                    group.GroupBy(x => RunSheetParser.NormalizeGrowerNumber(x.GrowerNumber), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(x => x.Key, x => x.Sum(y => y.Bins), StringComparer.OrdinalIgnoreCase),
                    [group.Key],
                    group.Max(x => x.RunAt));
            })
            .ToList();

        var homogeneous = atomicRuns.Where(x => x.Varieties.Count == 1
                && x.ProductionTypes.Count == 1
                && !IsUnassignedWpRun(x))
            .GroupBy(x => new
            {
                x.Facility,
                x.Date,
                Variety = x.Varieties[0],
                ProductionType = x.ProductionTypes[0],
                x.SalesDesk
            })
            .Select(group => new CropPhysicalRun(
                group.Key.Facility,
                group.Key.Date,
                [group.Key.Variety],
                [group.Key.ProductionType],
                group.Key.SalesDesk,
                group.Sum(x => x.TotalBins),
                group.SelectMany(x => x.GrowerBins)
                    .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.Value), StringComparer.OrdinalIgnoreCase),
                group.SelectMany(x => x.ActualRunIds).OrderBy(x => x).ToList(),
                group.Max(x => x.LatestRunAt)))
            .ToList();

        return homogeneous
            .Concat(atomicRuns.Where(x => x.Varieties.Count != 1
                || x.ProductionTypes.Count != 1
                || IsUnassignedWpRun(x)))
            .OrderBy(x => x.Date)
            .ThenBy(x => string.Join('/', x.Varieties), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SalesDesk, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ActualRunIds.First())
            .ToList();
    }

    private static bool IsUnassignedWpRun(CropPhysicalRun run) =>
        run.Facility == EmploymentFacilities.Wp
        && string.Equals(run.SalesDesk, "Unassigned", StringComparison.OrdinalIgnoreCase);
}

public static class RunSheetMatcher
{
    public static IReadOnlyList<RunSheetReconciliationItemViewModel> Reconcile(
        string facility,
        IReadOnlyList<ExternalPhysicalRun> sheetRuns,
        IReadOnlyList<CropPhysicalRun> cropRuns,
        DateTimeOffset now,
        TimeSpan pendingWindow)
    {
        var remainingSheet = sheetRuns
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Variety, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SalesDesk ?? x.UnknownSalesDeskCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remainingCrop = cropRuns
            .OrderBy(x => x.Date)
            .ThenBy(x => string.Join('/', x.Varieties), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ActualRunIds.FirstOrDefault())
            .ToList();
        var result = new List<RunSheetReconciliationItemViewModel>();

        PairWhere(remainingSheet, remainingCrop, result, IsExactMatch);
        PairSplitVarieties(remainingSheet, remainingCrop, result);
        PairWhere(remainingSheet, remainingCrop, result, IsExactExceptDate);

        foreach (var sheet in remainingSheet.ToList())
        {
            var candidate = remainingCrop
                .Select(crop => new { Crop = crop, Score = CandidateScore(facility, sheet, crop) })
                .Where(x => x.Score >= 65)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => Math.Abs(x.Crop.Date.DayNumber - sheet.Date.DayNumber))
                .ThenBy(x => x.Crop.ActualRunIds.FirstOrDefault())
                .FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            result.Add(Compare(sheet, candidate.Crop));
            remainingSheet.Remove(sheet);
            remainingCrop.Remove(candidate.Crop);
        }

        result.AddRange(remainingSheet.Select(sheet => MissingFromCropQc(sheet)));
        result.AddRange(remainingCrop.Select(crop => MissingFromSheet(crop, now, pendingWindow)));

        return result
            .OrderByDescending(x => x.State == RunSheetReconciliationStates.Attention)
            .ThenByDescending(x => x.State == RunSheetReconciliationStates.Pending)
            .ThenBy(x => x.SheetDate ?? x.CropQcDate)
            .ThenBy(x => x.SheetVariety, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ActualRunIds.FirstOrDefault())
            .ToList();
    }

    // The parser already aggregates each variety within these exact business dimensions.
    // Choose one physical run per variety, never arbitrary subsets of grower rows/season totals.
    private const int MaximumSplitCombinations = 256;
    private const int MaximumSplitVarieties = 16;

    private sealed record SplitCandidate(
        CropPhysicalRun Crop,
        IReadOnlyList<ExternalPhysicalRun> Candidates,
        IReadOnlyList<ExternalPhysicalRun[]> Matches,
        bool LimitExceeded);

    private static void PairSplitVarieties(
        List<ExternalPhysicalRun> sheets,
        List<CropPhysicalRun> crops,
        List<RunSheetReconciliationItemViewModel> result)
    {
        // Plan against the same unmatched snapshot before consuming anything. Two Crop QC
        // runs competing for a Sheet member must not be resolved by iteration order either.
        var plans = crops.Where(crop => crop.Varieties.Count > 1 && crop.ProductionTypes.Count == 1)
            .Select(crop => FindSplitCandidate(crop, sheets))
            .Where(plan => plan.Matches.Count > 0 || plan.LimitExceeded)
            .ToList();
        foreach (var plan in plans)
        {
            var members = plan.Matches.FirstOrDefault();
            var contested = members is not null && plans.Any(other => !ReferenceEquals(plan, other)
                && (other.LimitExceeded ? other.Candidates : other.Matches.SelectMany(x => x))
                    .Any(sheet => members.Any(member => ReferenceEquals(member, sheet))));
            if (plan.LimitExceeded || plan.Matches.Count != 1 || contested)
            {
                var item = BuildItem(RunSheetReconciliationStates.Attention, null, plan.Crop,
                    [RunSheetReconciliationReasons.AmbiguousSplitVarieties]);
                item.InformationMessage = plan.LimitExceeded
                    ? "Split-variety verification exceeded its bounded candidate limit; no Sheet runs were selected."
                    : "More than one split-variety assignment is possible; no Sheet runs were selected.";
                result.Add(item);
                crops.Remove(plan.Crop); // Keep ambiguity out of the later greedy/fuzzy pass.
                continue;
            }

            var combined = CombineSplit(members!);
            var matched = BuildItem(RunSheetReconciliationStates.Match, combined,
                plan.Crop with { GrowerBins = NormalizeGrowers(plan.Crop.GrowerBins) }, []);
            matched.InformationMessage = $"Google Sheet records this as {members!.Length} variety-specific runs; Crop QC records it as one combined Actual Run.";
            result.Add(matched);
            foreach (var member in members) sheets.Remove(member);
            crops.Remove(plan.Crop);
        }
    }

    private static SplitCandidate FindSplitCandidate(CropPhysicalRun crop, List<ExternalPhysicalRun> sheets)
    {
        var varieties = crop.Varieties.Select(RunSheetParser.NormalizeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (varieties.Length < 2 || varieties.Any(string.IsNullOrWhiteSpace)
            || crop.ActualRunIds.Count != 1
            || (crop.Facility == EmploymentFacilities.Wp
                && (string.IsNullOrWhiteSpace(crop.SalesDesk)
                    || string.Equals(crop.SalesDesk, "Unassigned", StringComparison.OrdinalIgnoreCase))))
            return new(crop, [], [], false);

        var candidates = sheets.Where(sheet => sheet.Facility == crop.Facility
                && sheet.Date == crop.Date
                && string.Equals(sheet.ProductionType, crop.ProductionTypes[0], StringComparison.OrdinalIgnoreCase)
                && (crop.Facility == EmploymentFacilities.Ebs
                    || (sheet.UnknownSalesDeskCode is null && !string.IsNullOrWhiteSpace(sheet.SalesDesk)
                        && string.Equals(sheet.SalesDesk, crop.SalesDesk, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        // Inspect the entire unmatched business peer group, not a subset selected by variety.
        // Legitimate exact one-to-one matches have already removed their Sheet members.
        var peerVarieties = candidates.Select(sheet => RunSheetParser.NormalizeCode(sheet.Variety))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!peerVarieties.SetEquals(varieties)) return new(crop, candidates, [], false);

        var choices = varieties.Select(variety => candidates.Where(sheet =>
            RunSheetParser.NormalizeCode(sheet.Variety) == variety).ToArray()).ToArray();
        if (choices.Any(choice => choice.Length == 0)) return new(crop, candidates, [], false);

        var combinations = 1;
        foreach (var choice in choices)
        {
            if (varieties.Length > MaximumSplitVarieties || choice.Length > MaximumSplitCombinations / combinations)
                return new(crop, candidates, [], true);
            combinations *= choice.Length;
        }

        var cropGrowers = NormalizeGrowers(crop.GrowerBins);
        var matches = new List<ExternalPhysicalRun[]>();
        for (var index = 0; index < combinations; index++)
        {
            var selection = index;
            var members = new ExternalPhysicalRun[choices.Length];
            for (var variety = 0; variety < choices.Length; variety++)
            {
                members[variety] = choices[variety][selection % choices[variety].Length];
                selection /= choices[variety].Length;
            }
            if (members.Sum(x => (long)x.TotalBins) == crop.TotalBins
                && DictionaryEqual(NormalizeGrowers(members.SelectMany(x => x.GrowerBins)), cropGrowers))
                matches.Add(members);
        }
        return new(crop, candidates, matches, false);
    }

    private static Dictionary<string, int> NormalizeGrowers(IEnumerable<KeyValuePair<string, int>> growers) =>
        growers.GroupBy(x => RunSheetParser.NormalizeGrowerNumber(x.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Value), StringComparer.OrdinalIgnoreCase);

    private static ExternalPhysicalRun CombineSplit(IReadOnlyList<ExternalPhysicalRun> members) =>
        members[0] with
        {
            Variety = string.Join(" / ", members.Select(x => RunSheetParser.NormalizeCode(x.Variety))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            TotalBins = members.Sum(x => x.TotalBins),
            GrowerBins = NormalizeGrowers(members.SelectMany(x => x.GrowerBins))
        };

    private static void PairWhere(
        List<ExternalPhysicalRun> sheets,
        List<CropPhysicalRun> crops,
        List<RunSheetReconciliationItemViewModel> result,
        Func<ExternalPhysicalRun, CropPhysicalRun, bool> predicate)
    {
        foreach (var sheet in sheets.ToList())
        {
            var crop = crops.FirstOrDefault(candidate => predicate(sheet, candidate));
            if (crop is null)
            {
                continue;
            }

            result.Add(Compare(sheet, crop));
            sheets.Remove(sheet);
            crops.Remove(crop);
        }
    }

    private static bool IsExactMatch(ExternalPhysicalRun sheet, CropPhysicalRun crop) =>
        sheet.Date == crop.Date
        && ExactDimensions(sheet, crop)
        && sheet.TotalBins == crop.TotalBins
        && DictionaryEqual(sheet.GrowerBins, crop.GrowerBins);

    private static bool IsExactExceptDate(ExternalPhysicalRun sheet, CropPhysicalRun crop) =>
        Math.Abs(sheet.Date.DayNumber - crop.Date.DayNumber) == 1
        && ExactDimensions(sheet, crop)
        && sheet.TotalBins == crop.TotalBins
        && DictionaryEqual(sheet.GrowerBins, crop.GrowerBins);

    private static bool ExactDimensions(ExternalPhysicalRun sheet, CropPhysicalRun crop) =>
        sheet.Facility == crop.Facility
        && crop.Varieties.Count == 1
        && string.Equals(sheet.Variety, crop.Varieties[0], StringComparison.OrdinalIgnoreCase)
        && crop.ProductionTypes.Count == 1
        && string.Equals(sheet.ProductionType, crop.ProductionTypes[0], StringComparison.OrdinalIgnoreCase)
        && (sheet.Facility == EmploymentFacilities.Ebs
            || (sheet.UnknownSalesDeskCode is null
                && string.Equals(sheet.SalesDesk, crop.SalesDesk, StringComparison.OrdinalIgnoreCase)));

    private static int CandidateScore(string facility, ExternalPhysicalRun sheet, CropPhysicalRun crop)
    {
        if (!string.Equals(sheet.Facility, crop.Facility, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }
        var dayDifference = Math.Abs(sheet.Date.DayNumber - crop.Date.DayNumber);
        if (dayDifference > 1)
        {
            return -1;
        }

        var exactDate = dayDifference == 0;
        var exactTotal = sheet.TotalBins == crop.TotalBins;
        var exactGrowers = DictionaryEqual(sheet.GrowerBins, crop.GrowerBins);
        var sharedGrowers = sheet.GrowerBins.Keys
            .Intersect(crop.GrowerBins.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sameGrowerSet = sheet.GrowerBins.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(crop.GrowerBins.Keys);
        var sharedBins = sharedGrowers.Sum(grower => Math.Min(
            sheet.GrowerBins.GetValueOrDefault(grower),
            crop.GrowerBins.GetValueOrDefault(grower)));
        var materialGrowerOverlap = sharedGrowers.Count > 0
            && (sharedGrowers.Count * 2 >= Math.Max(sheet.GrowerBins.Count, crop.GrowerBins.Count)
                || sharedBins * 2 >= Math.Min(sheet.TotalBins, crop.TotalBins));
        var exactVariety = crop.Varieties.Count == 1
            && string.Equals(sheet.Variety, crop.Varieties[0], StringComparison.OrdinalIgnoreCase);
        var exactProduction = crop.ProductionTypes.Count == 1
            && string.Equals(sheet.ProductionType, crop.ProductionTypes[0], StringComparison.OrdinalIgnoreCase);
        var exactSalesDesk = facility == EmploymentFacilities.Ebs
            || (sheet.UnknownSalesDeskCode is null
                && string.Equals(sheet.SalesDesk, crop.SalesDesk, StringComparison.OrdinalIgnoreCase));
        var strongIdentity = exactGrowers
            || sameGrowerSet
            || (exactTotal && materialGrowerOverlap);
        if (!strongIdentity)
        {
            return -1;
        }

        return (exactDate ? 35 : 10)
            + (exactTotal ? 25 : 0)
            + (exactGrowers ? 45 : sameGrowerSet ? 30 : materialGrowerOverlap ? 15 : 0)
            + (exactVariety ? 20 : 0)
            + (exactProduction ? 15 : 0)
            + (exactSalesDesk ? 10 : 0);
    }

    private static RunSheetReconciliationItemViewModel Compare(ExternalPhysicalRun sheet, CropPhysicalRun crop)
    {
        var reasons = new List<string>();
        if (sheet.Date != crop.Date) reasons.Add(RunSheetReconciliationReasons.ProbableDateMismatch);
        if (sheet.TotalBins != crop.TotalBins) reasons.Add(RunSheetReconciliationReasons.BinMismatch);
        if (!DictionaryEqual(sheet.GrowerBins, crop.GrowerBins)) reasons.Add(RunSheetReconciliationReasons.GrowerMismatch);
        if (crop.Varieties.Count != 1 || !string.Equals(sheet.Variety, crop.Varieties[0], StringComparison.OrdinalIgnoreCase))
            reasons.Add(RunSheetReconciliationReasons.VarietyMismatch);
        if (crop.ProductionTypes.Count != 1 || !string.Equals(sheet.ProductionType, crop.ProductionTypes[0], StringComparison.OrdinalIgnoreCase))
            reasons.Add(RunSheetReconciliationReasons.ProductionTypeMismatch);
        if (sheet.Facility == EmploymentFacilities.Wp)
        {
            if (sheet.UnknownSalesDeskCode is not null)
                reasons.Add(RunSheetReconciliationReasons.UnknownSalesDeskCode);
            else if (string.Equals(crop.SalesDesk, "Unassigned", StringComparison.OrdinalIgnoreCase))
                reasons.Add(RunSheetReconciliationReasons.SalesDeskMissing);
            else if (!string.Equals(sheet.SalesDesk, crop.SalesDesk, StringComparison.OrdinalIgnoreCase))
                reasons.Add(RunSheetReconciliationReasons.SalesDeskMismatch);
        }

        return BuildItem(
            reasons.Count == 0 ? RunSheetReconciliationStates.Match : RunSheetReconciliationStates.Attention,
            sheet,
            crop,
            reasons);
    }

    private static RunSheetReconciliationItemViewModel MissingFromCropQc(ExternalPhysicalRun sheet)
    {
        var reasons = new List<string>();
        if (sheet.UnknownSalesDeskCode is not null) reasons.Add(RunSheetReconciliationReasons.UnknownSalesDeskCode);
        reasons.Add(RunSheetReconciliationReasons.MissingFromCropQc);
        return BuildItem(RunSheetReconciliationStates.Attention, sheet, null, reasons);
    }

    private static RunSheetReconciliationItemViewModel MissingFromSheet(
        CropPhysicalRun crop,
        DateTimeOffset now,
        TimeSpan pendingWindow)
    {
        var pending = now - crop.LatestRunAt < pendingWindow;
        return BuildItem(
            pending ? RunSheetReconciliationStates.Pending : RunSheetReconciliationStates.Attention,
            null,
            crop,
            pending ? [] : [RunSheetReconciliationReasons.MissingFromSheet]);
    }

    private static RunSheetReconciliationItemViewModel BuildItem(
        string state,
        ExternalPhysicalRun? sheet,
        CropPhysicalRun? crop,
        IReadOnlyList<string> reasons)
    {
        var growerNumbers = (sheet?.GrowerBins.Keys ?? [])
            .Concat(crop?.GrowerBins.Keys ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new RunSheetReconciliationItemViewModel
        {
            State = state,
            Facility = sheet?.Facility ?? crop?.Facility ?? "",
            SheetDate = sheet?.Date,
            CropQcDate = crop?.Date,
            SheetVariety = sheet?.Variety ?? "—",
            CropQcVariety = crop is null ? "—" : string.Join(" / ", crop.Varieties),
            SheetProductionType = sheet?.ProductionType ?? "—",
            CropQcProductionType = crop is null ? "—" : string.Join(" / ", crop.ProductionTypes),
            SheetSalesDesk = sheet?.Facility == EmploymentFacilities.Wp
                ? sheet.SalesDesk ?? $"Unknown ({sheet.UnknownSalesDeskCode})"
                : "N/A",
            CropQcSalesDesk = crop?.Facility == EmploymentFacilities.Wp ? crop.SalesDesk : "N/A",
            SheetBins = sheet?.TotalBins,
            CropQcBins = crop?.TotalBins,
            Reasons = reasons,
            ActualRunIds = crop?.ActualRunIds ?? [],
            Growers = growerNumbers.Select(grower => new RunSheetGrowerComparisonViewModel(
                grower,
                sheet?.GrowerBins.GetValueOrDefault(grower) ?? 0,
                crop?.GrowerBins.GetValueOrDefault(grower) ?? 0)).ToList()
        };
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right) =>
        left.Count == right.Count
        && left.All(x => right.TryGetValue(x.Key, out var bins) && bins == x.Value);
}
