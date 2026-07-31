using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRunExpectationService
{
    Task<RunExpectation> CreateFrozenAsync(
        ActualRun actualRun,
        ActualRunRevision revision,
        IReadOnlyList<BinsRunEntry> activeEntries,
        int userId,
        DateTimeOffset calculatedAt,
        CancellationToken cancellationToken);
}

public sealed class RunExpectationService(
    CropQcDbContext dbContext,
    ILogger<RunExpectationService>? logger = null) : IRunExpectationService
{
    public async Task<RunExpectation> CreateFrozenAsync(
        ActualRun actualRun,
        ActualRunRevision revision,
        IReadOnlyList<BinsRunEntry> activeEntries,
        int userId,
        DateTimeOffset calculatedAt,
        CancellationToken cancellationToken)
    {
        if (activeEntries.Count == 0)
        {
            throw new InvalidOperationException("A Run Expectation requires at least one persisted Actual Run depletion row.");
        }

        if (await dbContext.RunExpectations.AsNoTracking()
            .AnyAsync(x => x.ActualRunRevisionId == revision.Id, cancellationToken))
        {
            throw new InvalidOperationException("This Actual Run revision already has a frozen Run Expectation.");
        }

        var warehouseIds = activeEntries.Select(x => x.WarehouseId).Distinct().ToList();
        if (warehouseIds.Count != 1)
        {
            throw new InvalidOperationException("A Run Expectation may contain only one facility.");
        }

        var roomIds = activeEntries.Select(x => x.RoomId).Distinct().ToList();
        var fruitProfileIds = activeEntries.Where(x => x.FruitProfileId != null).Select(x => x.FruitProfileId!.Value).Distinct().ToList();
        var cropYears = activeEntries.Where(x => x.CropYear != null).Select(x => x.CropYear!.Value).Distinct().ToList();
        var lotNumbers = activeEntries.Select(x => x.LotNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var warehouse = await dbContext.Warehouses.AsNoTracking()
            .Where(x => x.Id == warehouseIds[0])
            .Select(x => new { x.Id, x.Code })
            .SingleAsync(cancellationToken);
        var rooms = await dbContext.Rooms.AsNoTracking()
            .Where(x => roomIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => x.CropQcRoomName ?? x.DisplayName ?? x.Code,
                cancellationToken);
        var profiles = await dbContext.FruitProfiles.AsNoTracking()
            .Where(x => fruitProfileIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        // Filter before materialization and cap the candidate set. The final lot match is exact and never fuzzy.
        var candidateSamples = await dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.ReceiptId != null
                && roomIds.Contains(x.Receipt!.RoomId)
                && fruitProfileIds.Contains(x.Receipt.FruitProfileId)
                && cropYears.Contains(x.Receipt.CropYear)
                && lotNumbers.Contains(x.Receipt.GrowerNumber ?? x.Receipt.LotCode))
            .OrderByDescending(x => x.SampleTakenAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new ExpectationQcCandidate(
                x.Id,
                x.Receipt!.RoomId,
                x.Receipt.FruitProfileId,
                x.Receipt.CropYear,
                x.Receipt.GrowerNumber ?? x.Receipt.LotCode,
                x.SampleTakenAt,
                x.SampleType.Name,
                x.FruitReadings
                    .OrderBy(y => y.RowNumber)
                    .Select(y => new ExpectationReading(
                        y.RowNumber,
                        y.SizeCategory,
                        y.Grade == null ? null : y.Grade.Code,
                        y.WeightGrams,
                        y.Pressure1Lbs,
                        y.Pressure2Lbs))
                    .ToList()))
            .Take(Math.Clamp(activeEntries.Count * 10, 500, 2000))
            .ToListAsync(cancellationToken);

        var settingKeys = new[]
        {
            RunProjectionSettings.ApplePoundsPerBinKey,
            RunProjectionSettings.PearPoundsPerBinKey,
            RunProjectionSettings.DefaultExpectedPackoutPercentKey,
            RunProjectionSettings.MinimumDistributionFruitKey
        };
        var settingValues = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => settingKeys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        var applePoundsPerBin = ReadPositive(settingValues, RunProjectionSettings.ApplePoundsPerBinKey, RunProjectionCalculationService.DefaultApplePoundsPerBin);
        var pearPoundsPerBin = ReadPositive(settingValues, RunProjectionSettings.PearPoundsPerBinKey, RunProjectionCalculationService.DefaultPearPoundsPerBin);
        var expectedPackout = ReadPercent(settingValues, RunProjectionSettings.DefaultExpectedPackoutPercentKey, RunProjectionCalculationService.DefaultExpectedPackoutPercent);
        var minimumFruit = ReadPositiveInt(settingValues, RunProjectionSettings.MinimumDistributionFruitKey, RunProjectionSettings.DefaultMinimumDistributionFruit);
        var totalBins = activeEntries.Sum(x => x.BinsRun);

        var expectation = new RunExpectation
        {
            ActualRunId = actualRun.Id,
            ActualRunRevisionId = revision.Id,
            RevisionNumber = revision.RevisionNumber,
            FacilityWarehouseId = warehouse.Id,
            FacilitySnapshot = warehouse.Code,
            RunAtSnapshot = actualRun.RunAt,
            TotalBins = totalBins,
            ExpectedPackoutPercent = expectedPackout,
            SizeDistributionSnapshotJson = "{}",
            GradeDistributionSnapshotJson = "{}",
            ConfigurationSnapshotJson = JsonSerializer.Serialize(new
            {
                ApplePoundsPerBin = applePoundsPerBin,
                PearPoundsPerBin = pearPoundsPerBin,
                StandardBoxWeightPounds = RunProjectionCalculationService.DefaultStandardBoxWeightPounds,
                ExpectedPackoutPercent = expectedPackout,
                MinimumDistributionFruit = minimumFruit,
                PeelerCullShare = ProjectionOutcomeCalculator.PeelerRate,
                JuiceCullShare = ProjectionOutcomeCalculator.JuiceRate,
                WasteCullShare = ProjectionOutcomeCalculator.WasteRate
            }),
            CalculationVersion = RunExpectationCalculationVersions.Current,
            CalculatedAt = calculatedAt,
            CreatedByUserId = userId
        };

        var sizePounds = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var gradePounds = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in activeEntries.OrderBy(x => x.RoomId).ThenBy(x => x.LotNumber).ThenBy(x => x.Id))
        {
            var profile = entry.FruitProfileId is int profileId && profiles.TryGetValue(profileId, out var resolvedProfile)
                ? resolvedProfile
                : null;
            var sample = candidateSamples.FirstOrDefault(x =>
                x.RoomId == entry.RoomId
                && x.FruitProfileId == entry.FruitProfileId
                && x.CropYear == entry.CropYear
                && string.Equals(x.LotNumber, entry.LotNumber, StringComparison.OrdinalIgnoreCase));
            var readings = sample?.Readings ?? [];
            var calculation = RunProjectionCalculationService.Calculate(
                profile?.FruitType,
                entry.BinsRun,
                applePoundsPerBin,
                pearPoundsPerBin,
                RunProjectionCalculationService.DefaultStandardBoxWeightPounds,
                expectedPackout,
                readings.Where(x => x.SizeCategory != null).Select(x => new RunProjectionSizeObservation(x.SizeCategory!.Value)),
                readings.Where(x => !string.IsNullOrWhiteSpace(x.GradeCode)).Select(x => new RunProjectionGradeObservation(x.GradeCode!)),
                readings.Count(x => x.SizeCategory != null && !string.IsNullOrWhiteSpace(x.GradeCode)),
                minimumFruit);
            var confidence = sample is null
                ? 0m
                : decimal.Round(Math.Min(100m, readings.Count / (decimal)minimumFruit * 100m), 2);
            var contribution = decimal.Round(entry.BinsRun / (decimal)totalBins * 100m, 6);
            foreach (var size in calculation.SizeAllocations)
            {
                sizePounds[size.SizeCategory.ToString()] = sizePounds.GetValueOrDefault(size.SizeCategory.ToString())
                    + calculation.PackedProjectedPounds * size.Percentage / 100m;
            }
            foreach (var grade in calculation.GradeAllocations)
            {
                gradePounds[grade.Key] = gradePounds.GetValueOrDefault(grade.Key)
                    + calculation.PackedProjectedPounds * grade.Percentage / 100m;
            }

            expectation.Sources.Add(new RunExpectationSource
            {
                BinsRunEntryId = entry.Id,
                WarehouseId = entry.WarehouseId,
                RoomId = entry.RoomId,
                FacilitySnapshot = warehouse.Code,
                RoomSnapshot = rooms.GetValueOrDefault(entry.RoomId) ?? entry.RoomId.ToString(),
                CropYearSnapshot = entry.CropYear,
                GrowerLotId = entry.GrowerLotId,
                FruitProfileId = entry.FruitProfileId,
                GrowerSnapshot = entry.GrowerName,
                LotSnapshot = entry.LotNumber,
                VarietySnapshot = profile?.Name ?? entry.VarietyCode ?? "",
                ProductionTypeSnapshot = profile?.ProductionType ?? entry.InventoryStatus ?? "",
                IsOrganicSnapshot = profile?.IsOrganic ?? false,
                BinsContributed = entry.BinsRun,
                ContributionPercent = contribution,
                QcSampleId = sample?.Id,
                QcSampleTakenAtSnapshot = sample?.SampleTakenAt,
                QcFruitCountSnapshot = readings.Count,
                QcMeasurementSnapshotJson = JsonSerializer.Serialize(new
                {
                    SampleType = sample?.SampleType,
                    Readings = readings
                }),
                SizeDistributionSnapshotJson = JsonSerializer.Serialize(calculation.SizeAllocations),
                GradeDistributionSnapshotJson = JsonSerializer.Serialize(calculation.GradeAllocations),
                GrossPounds = calculation.ProjectedPounds,
                ExpectedPackedPounds = calculation.PackedProjectedPounds,
                ExpectedWholeBoxes = calculation.RoundedPackedProjectedBoxes,
                ExpectedCullPounds = calculation.CullProjectedPounds,
                ConfidencePercent = confidence,
                WarningSnapshot = calculation.Warning
            });
        }

        expectation.GrossPounds = expectation.Sources.Sum(x => x.GrossPounds);
        expectation.ExpectedPackedPounds = expectation.Sources.Sum(x => x.ExpectedPackedPounds);
        expectation.ExpectedPackedBoxes = expectation.ExpectedPackedPounds / RunProjectionCalculationService.DefaultStandardBoxWeightPounds;
        expectation.ExpectedWholeBoxes = RunProjectionCalculationService.RoundPlanningBoxes(expectation.ExpectedPackedBoxes);
        expectation.ExpectedCullPounds = expectation.Sources.Sum(x => x.ExpectedCullPounds);
        expectation.ExpectedPeelerPounds = expectation.ExpectedCullPounds * ProjectionOutcomeCalculator.PeelerRate;
        expectation.ExpectedJuicePounds = expectation.ExpectedCullPounds * ProjectionOutcomeCalculator.JuiceRate;
        expectation.ExpectedWastePounds = expectation.ExpectedCullPounds * ProjectionOutcomeCalculator.WasteRate;
        expectation.ConfidencePercent = decimal.Round(
            expectation.Sources.Sum(x => x.ConfidencePercent * x.BinsContributed) / totalBins,
            2);
        expectation.SizeDistributionSnapshotJson = JsonSerializer.Serialize(NormalizeDistribution(sizePounds, expectation.ExpectedPackedPounds));
        expectation.GradeDistributionSnapshotJson = JsonSerializer.Serialize(NormalizeDistribution(gradePounds, expectation.ExpectedPackedPounds));

        dbContext.RunExpectations.Add(expectation);
        logger?.LogInformation(
            "Frozen Run Expectation created. ActualRunId={ActualRunId} Revision={Revision} SourceCount={SourceCount} QcSampleCount={QcSampleCount} TotalBins={TotalBins}",
            actualRun.Id,
            revision.RevisionNumber,
            expectation.Sources.Count,
            expectation.Sources.Count(x => x.QcSampleId != null),
            totalBins);
        return expectation;
    }

    private static IReadOnlyDictionary<string, decimal> NormalizeDistribution(
        IReadOnlyDictionary<string, decimal> pounds,
        decimal totalPackedPounds) =>
        totalPackedPounds <= 0m
            ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : pounds.ToDictionary(
                x => x.Key,
                x => decimal.Round(x.Value / totalPackedPounds * 100m, 4),
                StringComparer.OrdinalIgnoreCase);

    private static decimal ReadPositive(IReadOnlyDictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) && parsed > 0m ? parsed : fallback;

    private static decimal ReadPercent(IReadOnlyDictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) && parsed is >= 0m and <= 100m ? parsed : fallback;

    private static int ReadPositiveInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private sealed record ExpectationQcCandidate(
        long Id,
        int RoomId,
        int FruitProfileId,
        int CropYear,
        string LotNumber,
        DateTimeOffset SampleTakenAt,
        string SampleType,
        IReadOnlyList<ExpectationReading> Readings);

    private sealed record ExpectationReading(
        int RowNumber,
        int? SizeCategory,
        string? GradeCode,
        decimal? WeightGrams,
        decimal? Pressure1Lbs,
        decimal? Pressure2Lbs);
}
