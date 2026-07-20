using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Web.Services;

public interface IFieldSampleService
{
    Task<FieldSampleIndexViewModel> GetIndexAsync(FieldSampleSearchForm search, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<FieldSampleCreatePageViewModel> GetCreateAsync(FieldSampleCreateForm? form, CancellationToken cancellationToken);
    Task<IReadOnlyList<FieldSampleBlockSuggestion>> GetBlockSuggestionsAsync(string orchardName, string blockName, CancellationToken cancellationToken);
    Task<(long? SampleId, string? Error)> CreateAsync(FieldSampleCreateForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<FieldSampleDetailViewModel> GetDetailAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateMetadataAsync(long sampleId, FieldSampleMetadataForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> SaveRowsAsync(long sampleId, SaveFruitReadingsForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class FieldSampleService(CropQcDbContext dbContext, IUserAccessService userAccessService, IConfiguration configuration) : IFieldSampleService
{
    private const string FieldSampleTypeName = "Field Sample";
    private const int FieldSampleSize = 10;
    private const int MaxFieldSampleSize = 50;

    public async Task<FieldSampleIndexViewModel> GetIndexAsync(FieldSampleSearchForm search, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.View, cancellationToken))
        {
            return new FieldSampleIndexViewModel { DataWarning = "Field Samples access is required." };
        }

        var query = dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted && x.SampleType.Name == FieldSampleTypeName);
        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            var normalized = OrchardBlockMatcher.Normalize(term);
            query = query.Where(x =>
                (x.FieldSampleGrowerName != null && x.FieldSampleGrowerName.Contains(term))
                || (x.FieldSampleGrowerNumber != null && x.FieldSampleGrowerNumber.Contains(term))
                || (x.FieldSampleOriginalBlockName != null && x.FieldSampleOriginalBlockName.Contains(term))
                || (x.CanonicalOrchardBlock != null
                    && (x.CanonicalOrchardBlock.CanonicalBlockName.Contains(term)
                        || x.CanonicalOrchardBlock.NormalizedBlockKey == normalized
                        || x.CanonicalOrchardBlock.Aliases.Any(alias => alias.IsActive && alias.NormalizedAliasKey == normalized))));
        }

        if (search.FruitProfileId is not null)
        {
            query = query.Where(x => x.FieldSampleFruitProfileId == search.FruitProfileId);
        }

        if (search.StartDate is not null)
        {
            var start = new DateTimeOffset(search.StartDate.Value.Date, TimeSpan.Zero);
            query = query.Where(x => x.SampleTakenAt >= start);
        }

        if (search.EndDate is not null)
        {
            var end = new DateTimeOffset(search.EndDate.Value.Date.AddDays(1), TimeSpan.Zero);
            query = query.Where(x => x.SampleTakenAt < end);
        }

        var samples = await query
            .OrderByDescending(x => x.SampleTakenAt)
            .ThenBy(x => x.CanonicalOrchardBlock == null ? x.FieldSampleOriginalBlockName : x.CanonicalOrchardBlock.CanonicalBlockName)
            .Take(200)
            .Select(x => new
            {
                x.Id,
                x.FieldSampleGrowerName,
                x.FieldSampleGrowerNumber,
                x.FieldSampleOriginalBlockName,
                BlockName = x.CanonicalOrchardBlock == null ? x.FieldSampleOriginalBlockName : x.CanonicalOrchardBlock.CanonicalBlockName,
                Variety = x.FieldSampleFruitProfile == null ? "" : x.FieldSampleFruitProfile.Name,
                x.SampleTakenAt,
                Rows = x.FruitReadings.Select(row => new
                {
                    row.Pressure1Lbs,
                    row.Pressure2Lbs,
                    row.WeightGrams,
                    row.GradeId,
                    Starch = row.StarchScaleValue == null ? (decimal?)null : row.StarchScaleValue.Value,
                    row.StarchScaleValueId,
                    row.SizeCategory,
                    DefectCount = row.Defects.Count
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var canEdit = await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken);
        var list = samples.Select(sample =>
        {
            var pressures = sample.Rows.Select(x => AverageFlexible(x.Pressure1Lbs, x.Pressure2Lbs)).Where(x => x is not null).Select(x => x!.Value).ToList();
            var weights = sample.Rows.Where(x => x.WeightGrams is not null).Select(x => x.WeightGrams!.Value).ToList();
            var starch = sample.Rows.Where(x => x.Starch is not null).Select(x => x.Starch!.Value).ToList();
            var entered = sample.Rows.Count(x =>
                x.Pressure1Lbs is not null
                || x.Pressure2Lbs is not null
                || x.WeightGrams is not null
                || x.StarchScaleValueId is not null
                || x.SizeCategory is not null
                || x.DefectCount > 0);
            return new FieldSampleListItemViewModel
            {
                Id = sample.Id,
                OrchardName = sample.FieldSampleGrowerName ?? "",
                BlockName = sample.BlockName ?? "",
                OriginalBlockName = sample.FieldSampleOriginalBlockName ?? "",
                Variety = sample.Variety,
                SampleTakenAt = sample.SampleTakenAt,
                EnteredFruitCount = entered,
                AverageWeightGrams = weights.Count == 0 ? null : decimal.Round(weights.Average(), 2),
                AverageStarch = starch.Count == 0 ? null : decimal.Round(starch.Average(), 2),
                AveragePressureLbs = pressures.Count == 0 ? null : decimal.Round(pressures.Average(), 2),
                CompletionStatus = entered == 0 ? "Empty" : entered >= FieldSampleSize ? "Complete" : "Partial",
                CanEdit = canEdit
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(search.CompletionStatus))
        {
            list = list.Where(x => string.Equals(x.CompletionStatus, search.CompletionStatus, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return new FieldSampleIndexViewModel
        {
            Search = search,
            CanCreate = canEdit,
            FruitProfiles = await dbContext.FruitProfiles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken),
            Samples = list
        };
    }

    public async Task<FieldSampleCreatePageViewModel> GetCreateAsync(FieldSampleCreateForm? form, CancellationToken cancellationToken) =>
        new()
        {
            Form = form ?? new FieldSampleCreateForm { SampleTakenAt = DateTimeOffset.UtcNow },
            FruitProfiles = await dbContext.FruitProfiles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken),
            Blocks = await dbContext.CanonicalOrchardBlocks.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.OrchardName).ThenBy(x => x.CanonicalBlockName).ToListAsync(cancellationToken)
        };

    public async Task<IReadOnlyList<FieldSampleBlockSuggestion>> GetBlockSuggestionsAsync(string orchardName, string blockName, CancellationToken cancellationToken)
    {
        var orchardKey = OrchardBlockMatcher.Normalize(orchardName);
        var blocks = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
            .Include(x => x.Aliases)
            .Where(x => x.IsActive && x.NormalizedOrchardKey == orchardKey)
            .ToListAsync(cancellationToken);

        return blocks
            .Select(block =>
            {
                var names = block.Aliases.Where(x => x.IsActive).Select(x => x.AliasName).Append(block.CanonicalBlockName).ToList();
                var best = names.Select(name => OrchardBlockMatcher.Similarity(blockName, name)).DefaultIfEmpty(0m).Max();
                var reason = best >= OrchardBlockMatcher.AutomaticMatchThreshold ? "High-confidence match" : best >= OrchardBlockMatcher.SuggestionThreshold ? "Possible match" : "Low confidence";
                return new FieldSampleBlockSuggestion(block.Id, block.CanonicalBlockName, block.OrchardName, best, reason);
            })
            .Where(x => x.Confidence >= OrchardBlockMatcher.SuggestionThreshold)
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.CanonicalBlockName)
            .Take(5)
            .ToList();
    }

    public async Task<(long? SampleId, string? Error)> CreateAsync(FieldSampleCreateForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Field Samples Edit access is required.");
        }

        if (string.IsNullOrWhiteSpace(form.OrchardName) || string.IsNullOrWhiteSpace(form.BlockName))
        {
            return (null, "Orchard/grower and block are required.");
        }

        if (!await dbContext.FruitProfiles.AsNoTracking().AnyAsync(x => x.Id == form.FruitProfileId && x.IsActive, cancellationToken))
        {
            return (null, "Selected variety was not found.");
        }

        var sampleType = await dbContext.SampleTypes.SingleOrDefaultAsync(x => x.Name == FieldSampleTypeName && x.IsActive, cancellationToken);
        if (sampleType is null)
        {
            return (null, "Field Sample type is not configured.");
        }

        var (block, blockError) = await ResolveBlockAsync(form, user, cancellationToken);
        if (blockError is not null || block is null)
        {
            return (null, blockError ?? "Block could not be resolved.");
        }

        var now = DateTimeOffset.UtcNow;
        var sample = new QcSample
        {
            ReceiptId = null,
            SampleTypeId = sampleType.Id,
            Status = "Data Entry In Progress",
            StarchStatus = "Starch Pending",
            PhotoStatus = "Not Required",
            EmailStatus = "Not Applicable",
            ActualSampleSize = FieldSampleSize,
            SampleTakenAt = form.SampleTakenAt,
            FieldSampleFruitProfileId = form.FruitProfileId,
            CanonicalOrchardBlockId = block.Id,
            FieldSampleGrowerName = form.OrchardName.Trim(),
            FieldSampleGrowerNumber = string.IsNullOrWhiteSpace(form.GrowerNumber) ? null : form.GrowerNumber.Trim(),
            FieldSampleOriginalBlockName = form.BlockName.Trim(),
            FieldSampleBlockResolution = block.CanonicalBlockName.Equals(form.BlockName.Trim(), StringComparison.OrdinalIgnoreCase) ? "ExactOrCreated" : "Resolved",
            Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.QcSamples.Add(sample);
        await dbContext.SaveChangesAsync(cancellationToken);
        for (var rowNumber = 1; rowNumber <= FieldSampleSize; rowNumber++)
        {
            dbContext.QcFruitReadings.Add(new QcFruitReading
            {
                QcSampleId = sample.Id,
                RowNumber = rowNumber,
                SizeStatus = "NotCalculated",
                CreatedAt = now
            });
        }

        await AuditAsync("create", nameof(QcSample), sample.Id.ToString(), user, null, new
        {
            sample.Id,
            SampleType = FieldSampleTypeName,
            sample.FieldSampleGrowerName,
            sample.FieldSampleGrowerNumber,
            sample.FieldSampleOriginalBlockName,
            sample.CanonicalOrchardBlockId,
            sample.FieldSampleFruitProfileId,
            sample.SampleTakenAt
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (sample.Id, null);
    }

    public async Task<FieldSampleDetailViewModel> GetDetailAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.View, cancellationToken))
        {
            return new FieldSampleDetailViewModel { DataWarning = "Field Samples access is required." };
        }

        var sample = await dbContext.QcSamples.AsNoTracking()
            .Include(x => x.SampleType)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.CanonicalOrchardBlock)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted && x.SampleType.Name == FieldSampleTypeName, cancellationToken);
        if (sample is null)
        {
            return new FieldSampleDetailViewModel { DataWarning = "Field Sample not found." };
        }

        var targetSampleSize = ResolveFieldSampleTargetSize(sample);
        var rows = await GetFruitRowsAsync(sample.Id, targetSampleSize, cancellationToken);
        var trendRows = await LoadTrendRowsAsync(sample, cancellationToken);
        var trend = BuildTrend(trendRows);
        var currentTrend = trend.SingleOrDefault(x => x.SampleId == sample.Id);
        var prior = trend.Where(x => x.SampleTakenAt < sample.SampleTakenAt && x.Summary.AveragePressureLbs is not null)
            .OrderByDescending(x => x.SampleTakenAt)
            .ThenByDescending(x => x.SampleId)
            .FirstOrDefault();
        var currentSummary = currentTrend?.Summary ?? BuildSummary(rows);
        if (prior?.Summary.AveragePressureLbs is not null && currentSummary.AveragePressureLbs is not null)
        {
            currentSummary.PriorPressureSampleDate = prior.SampleTakenAt;
            currentSummary.AveragePressureChangeFromPriorLbs = decimal.Round(currentSummary.AveragePressureLbs.Value - prior.Summary.AveragePressureLbs.Value, 2);
            currentSummary.AveragePressureChangeFromPriorPercent = prior.Summary.AveragePressureLbs.Value == 0
                ? null
                : decimal.Round(currentSummary.AveragePressureChangeFromPriorLbs.Value / prior.Summary.AveragePressureLbs.Value * 100m, 2);
        }

        return new FieldSampleDetailViewModel
        {
            SampleId = sample.Id,
            OrchardName = sample.FieldSampleGrowerName ?? sample.CanonicalOrchardBlock?.OrchardName ?? "",
            GrowerNumber = sample.FieldSampleGrowerNumber,
            CanonicalBlockName = sample.CanonicalOrchardBlock?.CanonicalBlockName ?? sample.FieldSampleOriginalBlockName ?? "",
            OriginalBlockName = sample.FieldSampleOriginalBlockName ?? "",
            Variety = sample.FieldSampleFruitProfile?.Name ?? "",
            SampleTakenAt = sample.SampleTakenAt,
            Notes = sample.Notes,
            TargetSampleSize = targetSampleSize,
            CanEdit = await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken),
            DeviceCapture = await GetDeviceCaptureSettingsAsync(cancellationToken),
            MetadataForm = new FieldSampleMetadataForm
            {
                SampleId = sample.Id,
                OrchardName = sample.FieldSampleGrowerName ?? sample.CanonicalOrchardBlock?.OrchardName ?? "",
                GrowerNumber = sample.FieldSampleGrowerNumber,
                BlockName = sample.FieldSampleOriginalBlockName ?? sample.CanonicalOrchardBlock?.CanonicalBlockName ?? "",
                CanonicalOrchardBlockId = sample.CanonicalOrchardBlockId,
                FruitProfileId = sample.FieldSampleFruitProfileId ?? 0,
                SampleTakenAt = sample.SampleTakenAt,
                Notes = sample.Notes
            },
            FruitProfiles = await dbContext.FruitProfiles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken),
            Blocks = await dbContext.CanonicalOrchardBlocks.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.OrchardName).ThenBy(x => x.CanonicalBlockName).ToListAsync(cancellationToken),
            CurrentSummary = currentSummary,
            SizeDistribution = currentTrend?.SizeDistribution ?? BuildSizeDistribution(rows),
            Trend = trend,
            FruitRows = rows,
            StarchScaleValues = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken),
            FruitReadingForm = new SaveFruitReadingsForm
            {
                SampleId = sample.Id,
                TargetSampleSize = targetSampleSize,
                Rows = rows.Select(row => new FruitReadingEditRow
                {
                    RowNumber = row.RowNumber,
                    Pressure1Lbs = row.Pressure1Lbs,
                    Pressure2Lbs = row.Pressure2Lbs,
                    WeightGrams = row.WeightGrams,
                    StarchScaleValueId = row.StarchScaleValueId
                }).ToList()
            }
        };
    }

    public async Task<string?> UpdateMetadataAsync(long sampleId, FieldSampleMetadataForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken))
        {
            return "Field Samples Edit access is required.";
        }

        if (string.IsNullOrWhiteSpace(form.OrchardName) || string.IsNullOrWhiteSpace(form.BlockName))
        {
            return "Orchard/grower and block are required.";
        }

        if (!await dbContext.FruitProfiles.AsNoTracking().AnyAsync(x => x.Id == form.FruitProfileId && x.IsActive, cancellationToken))
        {
            return "Selected variety was not found.";
        }

        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);
        if (sample is null || sample.SampleType.Name != FieldSampleTypeName)
        {
            return "Field Sample not found.";
        }

        var before = new
        {
            sample.FieldSampleGrowerName,
            sample.FieldSampleGrowerNumber,
            sample.FieldSampleOriginalBlockName,
            sample.CanonicalOrchardBlockId,
            sample.FieldSampleFruitProfileId,
            sample.SampleTakenAt,
            sample.Notes
        };

        var (block, blockError) = await ResolveBlockAsync(form, user, cancellationToken);
        if (blockError is not null || block is null)
        {
            return blockError ?? "Block could not be resolved.";
        }

        sample.FieldSampleGrowerName = form.OrchardName.Trim();
        sample.FieldSampleGrowerNumber = string.IsNullOrWhiteSpace(form.GrowerNumber) ? null : form.GrowerNumber.Trim();
        sample.FieldSampleOriginalBlockName = form.BlockName.Trim();
        sample.CanonicalOrchardBlockId = block.Id;
        sample.FieldSampleFruitProfileId = form.FruitProfileId;
        sample.SampleTakenAt = form.SampleTakenAt;
        sample.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        sample.FieldSampleBlockResolution = block.CanonicalBlockName.Equals(form.BlockName.Trim(), StringComparison.OrdinalIgnoreCase) ? "ExactOrCreated" : "Resolved";
        sample.UpdatedAt = DateTimeOffset.UtcNow;

        await AuditAsync("edit-metadata", nameof(QcSample), sample.Id.ToString(), user, before, new
        {
            sample.FieldSampleGrowerName,
            sample.FieldSampleGrowerNumber,
            sample.FieldSampleOriginalBlockName,
            sample.CanonicalOrchardBlockId,
            sample.FieldSampleFruitProfileId,
            sample.SampleTakenAt,
            sample.Notes
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SaveRowsAsync(long sampleId, SaveFruitReadingsForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken))
        {
            return "Field Samples Edit access is required.";
        }

        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .Include(x => x.FieldSampleFruitProfile)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);
        if (sample is null || sample.SampleType.Name != FieldSampleTypeName)
        {
            return "Field Sample not found.";
        }

        if (form.TargetSampleSize < FieldSampleSize || form.TargetSampleSize > MaxFieldSampleSize)
        {
            return $"Field Samples start with 10 fruit and may be expanded up to {MaxFieldSampleSize} rows.";
        }

        var rowsByNumber = form.Rows.GroupBy(x => x.RowNumber).ToList();
        if (rowsByNumber.Any(x => x.Key < 1 || x.Key > form.TargetSampleSize) || rowsByNumber.Any(x => x.Count() > 1))
        {
            return $"Rows must be unique and numbered 1 through {form.TargetSampleSize}.";
        }

        var validStarchIds = await dbContext.StarchScaleValues.AsNoTracking().Select(x => x.Id).ToHashSetAsync(cancellationToken);
        if (form.Rows.Any(x => x.StarchScaleValueId is not null && !validStarchIds.Contains(x.StarchScaleValueId.Value)))
        {
            return "One or more starch values are invalid.";
        }

        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.IsActive && x.FruitType == sample.FieldSampleFruitProfile!.FruitType)
            .ToListAsync(cancellationToken);
        var existing = await dbContext.QcFruitReadings
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        var before = JsonSerializer.Serialize(existing.Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs, x.WeightGrams, x.StarchScaleValueId, x.SizeCategory }));

        foreach (var submitted in form.Rows.OrderBy(x => x.RowNumber))
        {
            var hasAnyValue = submitted.Pressure1Lbs is not null
                || submitted.Pressure2Lbs is not null
                || submitted.WeightGrams is not null
                || submitted.StarchScaleValueId is not null;
            var reading = existing.SingleOrDefault(x => x.RowNumber == submitted.RowNumber);
            if (reading is null && !hasAnyValue)
            {
                continue;
            }

            if (reading is null)
            {
                reading = new QcFruitReading
                {
                    QcSampleId = sample.Id,
                    RowNumber = submitted.RowNumber,
                    SizeStatus = "NotCalculated",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.QcFruitReadings.Add(reading);
            }

            var size = CalculateSize(submitted.WeightGrams, thresholds);
            reading.Pressure1Lbs = submitted.Pressure1Lbs;
            reading.Pressure1Source = submitted.Pressure1Lbs is null ? null : "Manual";
            reading.Pressure2Lbs = submitted.Pressure2Lbs;
            reading.Pressure2Source = submitted.Pressure2Lbs is null ? null : "Manual";
            reading.WeightGrams = submitted.WeightGrams;
            reading.StarchScaleValueId = submitted.StarchScaleValueId;
            reading.SizeCategory = size.SizeCategory;
            reading.SizeStatus = size.SizeStatus;
            reading.IsCompleted = hasAnyValue;
            reading.UpdatedAt = DateTimeOffset.UtcNow;
        }

        sample.ActualSampleSize = Math.Max(FieldSampleSize, form.TargetSampleSize);
        sample.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        var afterRows = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs, x.WeightGrams, x.StarchScaleValueId, x.SizeCategory })
            .ToListAsync(cancellationToken);
        var after = JsonSerializer.Serialize(afterRows);
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            await AuditAsync("edit", nameof(QcSample), sample.Id.ToString(), user, before, new { sample.Id, Rows = afterRows }, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private async Task<(CanonicalOrchardBlock? Block, string? Error)> ResolveBlockAsync(FieldSampleCreateForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (form.CanonicalOrchardBlockId is not null)
        {
            var selected = await dbContext.CanonicalOrchardBlocks.SingleOrDefaultAsync(x => x.Id == form.CanonicalOrchardBlockId && x.IsActive, cancellationToken);
            if (selected is not null && string.Equals(selected.NormalizedOrchardKey, OrchardBlockMatcher.Normalize(form.OrchardName), StringComparison.Ordinal))
            {
                await EnsureAliasAsync(selected, form.BlockName, user, "manual", cancellationToken);
                return (selected, null);
            }

            return (null, "Selected block was not found for this orchard/grower.");
        }

        var orchardKey = OrchardBlockMatcher.Normalize(form.OrchardName);
        var blockKey = OrchardBlockMatcher.Normalize(form.BlockName);
        var exact = await dbContext.CanonicalOrchardBlocks.Include(x => x.Aliases)
            .SingleOrDefaultAsync(x => x.IsActive
                && x.NormalizedOrchardKey == orchardKey
                && (x.NormalizedBlockKey == blockKey || x.Aliases.Any(alias => alias.IsActive && alias.NormalizedAliasKey == blockKey)), cancellationToken);
        if (exact is not null)
        {
            return (exact, null);
        }

        if (!form.ConfirmCreateNewBlock)
        {
            return (null, "Select an existing block or confirm that this is a new canonical block.");
        }

        var now = DateTimeOffset.UtcNow;
        var created = new CanonicalOrchardBlock
        {
            OrchardName = form.OrchardName.Trim(),
            CanonicalBlockName = form.BlockName.Trim(),
            NormalizedOrchardKey = orchardKey,
            NormalizedBlockKey = blockKey,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.CanonicalOrchardBlocks.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("create", nameof(CanonicalOrchardBlock), created.Id.ToString(), user, null, new { created.OrchardName, created.CanonicalBlockName }, cancellationToken);
        return (created, null);
    }

    private async Task EnsureAliasAsync(CanonicalOrchardBlock block, string aliasName, ClaimsPrincipal user, string resolution, CancellationToken cancellationToken)
    {
        var aliasKey = OrchardBlockMatcher.Normalize(aliasName);
        if (string.Equals(block.NormalizedBlockKey, aliasKey, StringComparison.Ordinal))
        {
            return;
        }

        if (await dbContext.OrchardBlockAliases.AnyAsync(x => x.CanonicalOrchardBlockId == block.Id && x.NormalizedAliasKey == aliasKey, cancellationToken))
        {
            return;
        }

        var alias = new OrchardBlockAlias
        {
            CanonicalOrchardBlockId = block.Id,
            AliasName = aliasName.Trim(),
            NormalizedAliasKey = aliasKey,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.OrchardBlockAliases.Add(alias);
        await AuditAsync("alias-create", nameof(OrchardBlockAlias), $"{block.Id}:{aliasKey}", user, null, new { block.Id, alias.AliasName, Resolution = resolution }, cancellationToken);
    }

    private async Task<IReadOnlyList<TrendSampleRow>> LoadTrendRowsAsync(QcSample sample, CancellationToken cancellationToken)
    {
        var start = sample.SampleTakenAt.AddDays(-30);
        var end = sample.SampleTakenAt;
        return await dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.SampleType.Name == FieldSampleTypeName
                && x.CanonicalOrchardBlockId == sample.CanonicalOrchardBlockId
                && x.SampleTakenAt >= start
                && x.SampleTakenAt <= end)
            .OrderBy(x => x.SampleTakenAt)
            .ThenBy(x => x.Id)
            .Select(x => new TrendSampleRow(
                x.Id,
                x.SampleTakenAt,
                x.FieldSampleFruitProfile == null ? "" : x.FieldSampleFruitProfile.Name,
                x.FruitReadings.Select(row => new TrendFruitRow(
                    row.RowNumber,
                    row.Pressure1Lbs,
                    row.Pressure2Lbs,
                    row.WeightGrams,
                    row.StarchScaleValue == null ? null : row.StarchScaleValue.Value,
                    row.StarchScaleValueId,
                    row.SizeCategory)).ToList()))
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyList<FieldSampleTrendPoint> BuildTrend(IReadOnlyList<TrendSampleRow> samples)
    {
        var trend = samples.Select(sample =>
        {
            var rows = sample.Rows.Select(ToFruitReadingRow).ToList();
            return new FieldSampleTrendPoint
            {
                SampleId = sample.SampleId,
                SampleTakenAt = sample.SampleTakenAt,
                Variety = sample.Variety,
                Summary = BuildSummary(rows),
                SizeDistribution = BuildSizeDistribution(rows)
            };
        }).ToList();

        return trend;
    }

    private async Task<IReadOnlyList<FruitReadingRowViewModel>> GetFruitRowsAsync(long sampleId, int targetSampleSize, CancellationToken cancellationToken)
    {
        var rows = await dbContext.QcFruitReadings.AsNoTracking()
            .Include(x => x.StarchScaleValue)
            .Where(x => x.QcSampleId == sampleId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync(cancellationToken);
        var byRow = rows.ToDictionary(x => x.RowNumber);
        var rowCount = Math.Clamp(Math.Max(targetSampleSize, byRow.Count == 0 ? 0 : byRow.Keys.Max()), FieldSampleSize, MaxFieldSampleSize);
        return Enumerable.Range(1, rowCount)
            .Select(rowNumber => byRow.TryGetValue(rowNumber, out var row)
                ? ToFruitReadingRow(row)
                : new FruitReadingRowViewModel { RowNumber = rowNumber, SizeStatus = "NotCalculated", EntryStatus = "Empty" })
            .ToList();
    }

    private static int ResolveFieldSampleTargetSize(QcSample sample) =>
        Math.Clamp(Math.Max(FieldSampleSize, sample.ActualSampleSize ?? FieldSampleSize), FieldSampleSize, MaxFieldSampleSize);

    private async Task<DeviceCaptureSettingsViewModel> GetDeviceCaptureSettingsAsync(CancellationToken cancellationToken)
    {
        var values = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => x.Key.StartsWith("DeviceCapture__"))
            .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

        bool Read(string key)
        {
            if (values.TryGetValue($"DeviceCapture__{key}", out var configured))
            {
                return bool.TryParse(configured, out var parsed) && parsed;
            }

            return configuration.GetValue<bool>($"DeviceCapture:{key}");
        }

        return new DeviceCaptureSettingsViewModel(
            Read("Enabled"),
            Read("BrioEnabled"),
            Read("ObsbotEnabled"),
            Read("ScaleEnabled"));
    }

    private static FieldSampleMetricSummary BuildSummary(IReadOnlyList<FruitReadingRowViewModel> rows)
    {
        var entered = rows.Where(HasEnteredData).ToList();
        var weights = entered.Where(x => x.WeightGrams is not null).Select(x => x.WeightGrams!.Value).ToList();
        var starch = entered.Where(x => decimal.TryParse(x.Starch, out _)).Select(x => decimal.Parse(x.Starch!)).ToList();
        var pressures = entered.Select(x => x.PressureAverageLbs).Where(x => x is not null).Select(x => x!.Value).ToList();

        return new FieldSampleMetricSummary
        {
            EnteredFruitCount = entered.Count,
            AverageWeightGrams = weights.Count == 0 ? null : decimal.Round(weights.Average(), 2),
            PeakWeightGrams = weights.Count == 0 ? null : weights.Max(),
            MinimumWeightGrams = weights.Count == 0 ? null : weights.Min(),
            WeightRepresentedFruitCount = weights.Count,
            MissingWeightCount = entered.Count(x => x.WeightGrams is null),
            AverageStarch = starch.Count == 0 ? null : decimal.Round(starch.Average(), 2),
            StarchRepresentedFruitCount = starch.Count,
            MissingStarchCount = entered.Count(x => x.StarchScaleValueId is null),
            AveragePressureLbs = pressures.Count == 0 ? null : decimal.Round(pressures.Average(), 2),
            PeakPressureLbs = pressures.Count == 0 ? null : pressures.Max(),
            MinimumPressureLbs = pressures.Count == 0 ? null : pressures.Min(),
            PressureStandardDeviationLbs = SampleStandardDeviation(pressures),
            PressureReadingCount = pressures.Count,
            MissingPressureCount = entered.Count(x => x.PressureAverageLbs is null)
        };
    }

    private static IReadOnlyList<FieldSampleSizePoint> BuildSizeDistribution(IReadOnlyList<FruitReadingRowViewModel> rows)
    {
        var represented = rows.Where(x => x.SizeCategory is not null).ToList();
        if (represented.Count == 0)
        {
            return [];
        }

        return ProjectionDistributionMath.SizeDisplayOrder
            .Select(size => new FieldSampleSizePoint(size, decimal.Round(represented.Count(x => x.SizeCategory == size) / (decimal)represented.Count * 100m, 2)))
            .Where(x => x.Percentage > 0)
            .ToList();
    }

    private static FruitReadingRowViewModel ToFruitReadingRow(QcFruitReading row) => new()
    {
        RowNumber = row.RowNumber,
        Pressure1Lbs = row.Pressure1Lbs,
        Pressure2Lbs = row.Pressure2Lbs,
        PressureAverageLbs = AverageFlexible(row.Pressure1Lbs, row.Pressure2Lbs),
        WeightGrams = row.WeightGrams,
        StarchScaleValueId = row.StarchScaleValueId,
        Starch = row.StarchScaleValue?.Value.ToString("0.0"),
        SizeCategory = row.SizeCategory,
        SizeStatus = row.SizeStatus,
        IsCompleted = row.IsCompleted,
        EntryStatus = HasEnteredData(row) ? "In Progress" : "Empty"
    };

    private static FruitReadingRowViewModel ToFruitReadingRow(TrendFruitRow row) => new()
    {
        RowNumber = row.RowNumber,
        Pressure1Lbs = row.Pressure1Lbs,
        Pressure2Lbs = row.Pressure2Lbs,
        PressureAverageLbs = AverageFlexible(row.Pressure1Lbs, row.Pressure2Lbs),
        WeightGrams = row.WeightGrams,
        StarchScaleValueId = row.StarchScaleValueId,
        Starch = row.Starch?.ToString("0.0"),
        SizeCategory = row.SizeCategory,
        SizeStatus = row.SizeCategory is null ? "NotCalculated" : "Sized",
        EntryStatus = HasEnteredData(row) ? "In Progress" : "Empty"
    };

    private static bool HasEnteredData(FruitReadingRowViewModel row) =>
        row.Pressure1Lbs is not null || row.Pressure2Lbs is not null || row.WeightGrams is not null || row.StarchScaleValueId is not null || row.SizeCategory is not null;

    private static bool HasEnteredData(QcFruitReading row) =>
        row.Pressure1Lbs is not null || row.Pressure2Lbs is not null || row.WeightGrams is not null || row.StarchScaleValueId is not null || row.SizeCategory is not null || row.Defects.Count > 0;

    private static bool HasEnteredData(TrendFruitRow row) =>
        row.Pressure1Lbs is not null || row.Pressure2Lbs is not null || row.WeightGrams is not null || row.StarchScaleValueId is not null || row.SizeCategory is not null;

    private static (int? SizeCategory, string SizeStatus) CalculateSize(decimal? weightGrams, IEnumerable<FruitSizeConversionThreshold> thresholds)
    {
        if (weightGrams is null) return (null, "NotCalculated");
        var match = thresholds.OrderByDescending(x => x.MinimumWeightGrams).FirstOrDefault(x => weightGrams.Value >= x.MinimumWeightGrams);
        return match is null ? (null, "Undersized") : (match.SizeCategory, "Sized");
    }

    private static decimal? AverageFlexible(decimal? first, decimal? second) =>
        first is null && second is null ? null :
        first is null ? second :
        second is null ? first :
        decimal.Round((first.Value + second.Value) / 2m, 2);

    private static decimal? SampleStandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2) return null;
        var mean = values.Average();
        var variance = values.Sum(x => (x - mean) * (x - mean)) / (values.Count - 1);
        return decimal.Round((decimal)Math.Sqrt((double)variance), 2);
    }

    private async Task AuditAsync(string action, string entityName, string entityKey, ClaimsPrincipal user, object? before, object? after, CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email);
        var userId = email is null ? null : await dbContext.Users.AsNoTracking().Where(x => x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityKey,
            UserId = userId,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after),
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private sealed record TrendSampleRow(long SampleId, DateTimeOffset SampleTakenAt, string Variety, IReadOnlyList<TrendFruitRow> Rows);
    private sealed record TrendFruitRow(int RowNumber, decimal? Pressure1Lbs, decimal? Pressure2Lbs, decimal? WeightGrams, decimal? Starch, int? StarchScaleValueId, int? SizeCategory);
}
