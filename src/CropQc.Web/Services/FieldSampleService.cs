using System.Security.Claims;
using System.Globalization;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
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
    Task<FieldSampleRefreshViewModel?> GetRefreshAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<FieldSampleAutosaveResult> AutosaveAsync(long sampleId, FieldSampleAutosaveRequest request, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateMetadataAsync(long sampleId, FieldSampleMetadataForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> SaveRowsAsync(long sampleId, SaveFruitReadingsForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> MarkCompleteAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class FieldSampleService(
    CropQcDbContext dbContext,
    IUserAccessService userAccessService,
    IConfiguration configuration,
    IBusinessTimeService? businessTime = null) : IFieldSampleService
{
    private const string FieldSampleTypeName = "Field Sample";
    private const int FieldSampleSize = 10;
    private const int MaxFieldSampleSize = 50;
    private IBusinessTimeService BusinessTime { get; } = businessTime ?? new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock());

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
                x.ActualSampleSize,
                MaximumRowNumber = x.FruitReadings.Select(row => (int?)row.RowNumber).Max(),
                x.Status,
                x.EmailStatus,
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
            var pressures = PressureCalculationService.ValidSideReadings(
                sample.Rows.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));
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
                GrowerNumber = sample.FieldSampleGrowerNumber ?? "",
                BlockName = sample.BlockName ?? "",
                OriginalBlockName = sample.FieldSampleOriginalBlockName ?? "",
                Variety = sample.Variety,
                SampleTakenAt = sample.SampleTakenAt,
                EnteredFruitCount = entered,
                TargetSampleSize = Math.Clamp(Math.Max(FieldSampleSize, Math.Max(sample.ActualSampleSize ?? FieldSampleSize, sample.MaximumRowNumber ?? 0)), FieldSampleSize, MaxFieldSampleSize),
                AverageWeightGrams = weights.Count == 0 ? null : decimal.Round(weights.Average(), 2),
                AverageStarch = starch.Count == 0 ? null : decimal.Round(starch.Average(), 2),
                AveragePressureLbs = pressures.Count == 0 ? null : decimal.Round(pressures.Average(), 2),
                CompletionStatus = NormalizeLifecycleStatus(sample.Status, sample.EmailStatus),
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
            Blocks = await GetActiveOrchardBlocksAsync(cancellationToken)
        };

    public async Task<IReadOnlyList<FieldSampleBlockSuggestion>> GetBlockSuggestionsAsync(string orchardName, string blockName, CancellationToken cancellationToken)
    {
        if (OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(orchardName))
        {
            return [];
        }

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
            return (null, "Orchard and block are required.");
        }

        var orchardIdentityError = ValidateAmbiguousOrchardIdentity(form.OrchardName);
        if (orchardIdentityError is not null)
        {
            return (null, orchardIdentityError);
        }

        var selectedProfile = await dbContext.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.FruitProfileId && x.IsActive, cancellationToken);
        if (selectedProfile is null)
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
        var creatorEmail = user.FindFirstValue(ClaimTypes.Email);
        var creatorId = string.IsNullOrWhiteSpace(creatorEmail)
            ? null
            : await dbContext.Users.AsNoTracking().Where(x => x.Email == creatorEmail).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var sample = new QcSample
        {
            ReceiptId = null,
            SampleTypeId = sampleType.Id,
            Status = "Data Entry In Progress",
            StarchStatus = "Starch Pending",
            PhotoStatus = "Not Required",
            EmailStatus = "Not Sent",
            ActualSampleSize = FieldSampleSize,
            TakenByUserId = creatorId,
            SampleTakenAt = form.SampleTakenAt,
            FieldSampleFruitProfileId = form.FruitProfileId,
            CanonicalOrchardBlockId = block.Id,
            FieldSampleGrowerName = form.OrchardName.Trim(),
            FieldSampleGrowerNumber = NormalizeOptionalGrowerNumber(form.GrowerNumber),
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
            .Include(x => x.QcStation)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted && x.SampleType.Name == FieldSampleTypeName, cancellationToken);
        if (sample is null)
        {
            return new FieldSampleDetailViewModel { DataWarning = "Field Sample not found." };
        }

        var targetSampleSize = await ResolveFieldSampleTargetSizeAsync(sample, cancellationToken);
        var rows = await GetFruitRowsAsync(sample.Id, targetSampleSize, cancellationToken);
        var photos = await dbContext.QcPhotos.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id && !x.IsDeleted)
            .OrderByDescending(x => x.CapturedAt)
            .ToListAsync(cancellationToken);
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

        var sendHistory = await dbContext.QcSummaryEmailLogs.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FieldSampleSendHistoryItem(
                x.Id,
                x.Status,
                x.SentAt,
                x.SentByUser == null ? x.FromAddress : x.SentByUser.DisplayName,
                x.ToAddress,
                x.Subject,
                x.IsResend,
                x.Status == "Failed" ? x.ReportSnapshotReference : null))
            .ToListAsync(cancellationToken);
        var lastSent = sendHistory.FirstOrDefault(x => string.Equals(x.Status, "Sent", StringComparison.OrdinalIgnoreCase));
        var missingItems = BuildCompletionMissingItems(sample, rows);
        var canEdit = await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken);
        var changedSinceLastSend = string.Equals(sample.EmailStatus, "Needs Resend", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sample.Status, "Changed Since Last Send", StringComparison.OrdinalIgnoreCase);
        var lifecycleStatus = NormalizeLifecycleStatus(sample.Status, sample.EmailStatus);
        var fruitType = sample.FieldSampleFruitProfile?.FruitType;
        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => fruitType != null && x.IsActive && x.FruitType == fruitType)
            .OrderByDescending(x => x.MinimumWeightGrams)
            .Select(x => new FieldSampleSizeThreshold(x.SizeCategory, x.MinimumWeightGrams))
            .ToListAsync(cancellationToken);

        return new FieldSampleDetailViewModel
        {
            SampleId = sample.Id,
            OrchardName = sample.FieldSampleGrowerName ?? sample.CanonicalOrchardBlock?.OrchardName ?? "",
            GrowerNumber = sample.FieldSampleGrowerNumber,
            CanonicalBlockName = sample.CanonicalOrchardBlock?.CanonicalBlockName ?? sample.FieldSampleOriginalBlockName ?? "",
            OriginalBlockName = sample.FieldSampleOriginalBlockName ?? "",
            Variety = sample.FieldSampleFruitProfile?.Name ?? "",
            FruitType = fruitType ?? "",
            Terminology = FieldSampleCommodityTerminologyService.ForFruitType(fruitType),
            SampleTakenAt = sample.SampleTakenAt,
            Notes = sample.Notes,
            LifecycleStatus = lifecycleStatus,
            EmailStatus = NormalizeEmailStatus(sample.EmailStatus),
            ChangedSinceLastSend = changedSinceLastSend,
            LastSentAt = lastSent?.SentAt,
            LastSentBy = lastSent?.SentBy,
            LastRecipientSnapshot = lastSent?.Recipients,
            CompletionMissingItems = missingItems,
            CanMarkComplete = canEdit && missingItems.Count == 0 && string.Equals(lifecycleStatus, "In Progress", StringComparison.OrdinalIgnoreCase),
            CanSend = canEdit && missingItems.Count == 0 && (string.Equals(lifecycleStatus, "Complete", StringComparison.OrdinalIgnoreCase) || changedSinceLastSend),
            TargetSampleSize = targetSampleSize,
            AutosaveVersion = sample.FieldSampleAutosaveVersion,
            UpdatedAt = sample.UpdatedAt,
            CanEdit = canEdit,
            DeviceCapture = await GetDeviceCaptureSettingsAsync(cancellationToken),
            QcStationStatus = BuildQcStationStatus(sample.QcStation),
            PhotoGroups = GroupPhotos(photos, canDelete: await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken), sample.Id),
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
            Blocks = await GetActiveOrchardBlocksAsync(cancellationToken),
            CurrentSummary = currentSummary,
            SizeDistribution = currentTrend?.SizeDistribution ?? BuildSizeDistribution(rows),
            Trend = trend,
            FruitRows = rows,
            StarchScaleValues = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken),
            Grades = await dbContext.Grades.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(cancellationToken),
            DefectTypes = await dbContext.DefectTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken),
            SizeThresholds = thresholds,
            SendHistory = sendHistory,
            FruitReadingForm = new SaveFruitReadingsForm
            {
                SampleId = sample.Id,
                TargetSampleSize = targetSampleSize,
                Rows = rows.Select(row => new FruitReadingEditRow
                {
                    RowNumber = row.RowNumber,
                    Pressure1Lbs = row.Pressure1Lbs,
                    Pressure2Lbs = row.Pressure2Lbs,
                    OriginalPressure1Lbs = row.Pressure1Lbs,
                    OriginalPressure2Lbs = row.Pressure2Lbs,
                    WeightGrams = row.WeightGrams,
                    OriginalWeightGrams = row.WeightGrams,
                    SizeCategory = row.SizeCategory,
                    OriginalSizeCategory = row.SizeCategory,
                    StarchScaleValueId = row.StarchScaleValueId
                    ,
                    GradeId = row.GradeId
                    ,
                    DefectTypeIds = row.DefectTypeIds.ToList()
                    ,
                    OtherDefectNotes = row.OtherDefectNotes
                    ,
                    DefectsInspected = row.DefectsInspected
                }).ToList()
            }
        };
    }

    public async Task<FieldSampleRefreshViewModel?> GetRefreshAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.View, cancellationToken))
        {
            return null;
        }

        var sample = await dbContext.QcSamples.AsNoTracking()
            .Include(x => x.SampleType)
            .Include(x => x.QcStation)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted && x.SampleType.Name == FieldSampleTypeName, cancellationToken);
        if (sample is null)
        {
            return null;
        }

        var targetSampleSize = await ResolveFieldSampleTargetSizeAsync(sample, cancellationToken);
        var rows = await GetFruitRowsAsync(sample.Id, targetSampleSize, cancellationToken);
        return new FieldSampleRefreshViewModel(
            sample.Id,
            targetSampleSize,
            sample.UpdatedAt,
            sample.FieldSampleAutosaveVersion,
            BuildQcStationStatus(sample.QcStation),
            rows.Select(row => new FieldSampleRefreshRowViewModel(
                row.RowNumber,
                row.Pressure1Lbs,
                row.Pressure2Lbs,
                row.PressureAverageLbs,
                row.WeightGrams,
                row.SizeCategory,
                row.StarchScaleValueId,
                row.GradeId,
                row.DefectsInspected,
                row.DefectTypeIds,
                row.OtherDefectNotes,
                row.FieldVersion)).ToList());
    }

    public async Task<string?> UpdateMetadataAsync(long sampleId, FieldSampleMetadataForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken))
        {
            return "Field Samples Edit access is required.";
        }

        if (string.IsNullOrWhiteSpace(form.OrchardName) || string.IsNullOrWhiteSpace(form.BlockName))
        {
            return "Orchard and block are required.";
        }

        var orchardIdentityError = ValidateAmbiguousOrchardIdentity(form.OrchardName);
        if (orchardIdentityError is not null)
        {
            return orchardIdentityError;
        }

        var selectedProfile = await dbContext.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.FruitProfileId && x.IsActive, cancellationToken);
        if (selectedProfile is null)
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
        var varietyChanged = sample.FieldSampleFruitProfileId != form.FruitProfileId;

        var (block, blockError) = await ResolveBlockAsync(form, user, cancellationToken);
        if (blockError is not null || block is null)
        {
            return blockError ?? "Block could not be resolved.";
        }

        sample.FieldSampleGrowerName = form.OrchardName.Trim();
        sample.FieldSampleGrowerNumber = NormalizeOptionalGrowerNumber(form.GrowerNumber);
        sample.FieldSampleOriginalBlockName = form.BlockName.Trim();
        sample.CanonicalOrchardBlockId = block.Id;
        sample.FieldSampleFruitProfileId = form.FruitProfileId;
        sample.SampleTakenAt = form.SampleTakenAt;
        sample.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        sample.FieldSampleBlockResolution = block.CanonicalBlockName.Equals(form.BlockName.Trim(), StringComparison.OrdinalIgnoreCase) ? "ExactOrCreated" : "Resolved";
        sample.UpdatedAt = DateTimeOffset.UtcNow;

        var after = new
        {
            sample.FieldSampleGrowerName,
            sample.FieldSampleGrowerNumber,
            sample.FieldSampleOriginalBlockName,
            sample.CanonicalOrchardBlockId,
            sample.FieldSampleFruitProfileId,
            sample.SampleTakenAt,
            sample.Notes
        };
        if (!string.Equals(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after), StringComparison.Ordinal))
        {
            if (varietyChanged)
            {
                await RecalculatePersistedSizesAsync(sample.Id, selectedProfile.FruitType, user, "variety-change", cancellationToken);
            }
            sample.FieldSampleAutosaveVersion++;
            await MarkChangedSinceLastSendAsync(sample, "metadata-change", user, before, cancellationToken);
            await AuditAsync("edit-metadata", nameof(QcSample), sample.Id.ToString(), user, before, after, cancellationToken);
        }
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

        if (sample.FieldSampleFruitProfile is null)
        {
            return "Select a valid Field Sample variety before saving fruit rows.";
        }

        if (form.TargetSampleSize < FieldSampleSize || form.TargetSampleSize > MaxFieldSampleSize)
        {
            return $"Field Samples start with 10 fruit and may be expanded up to {MaxFieldSampleSize} rows.";
        }

        var existingMaxRowNumber = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .Select(x => (int?)x.RowNumber)
            .MaxAsync(cancellationToken) ?? 0;
        var maxAllowedRowNumber = Math.Max(form.TargetSampleSize, existingMaxRowNumber);
        var rowsByNumber = form.Rows.GroupBy(x => x.RowNumber).ToList();
        if (rowsByNumber.Any(x => x.Key < 1 || x.Key > maxAllowedRowNumber) || rowsByNumber.Any(x => x.Count() > 1))
        {
            return $"Rows must be unique and numbered 1 through {maxAllowedRowNumber}.";
        }

        var validStarchIds = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        if (form.Rows.Any(x => x.StarchScaleValueId is not null && !validStarchIds.Contains(x.StarchScaleValueId.Value)))
        {
            return "One or more starch values are invalid.";
        }

        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.IsActive && x.FruitType == sample.FieldSampleFruitProfile.FruitType)
            .ToListAsync(cancellationToken);
        var validGradeIds = await dbContext.Grades.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var activeDefects = await dbContext.DefectTypes.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var activeDefectIds = activeDefects.Select(x => x.Id).ToHashSet();
        var otherDefectId = activeDefects.FirstOrDefault(x => x.Name == "Other")?.Id;
        var existing = await dbContext.QcFruitReadings
            .Include(x => x.Defects)
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        var before = JsonSerializer.Serialize(existing.OrderBy(x => x.RowNumber).Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs, x.WeightGrams, x.StarchScaleValueId, x.GradeId, x.SizeCategory, x.DefectsInspected, Defects = x.Defects.Select(d => new { d.DefectTypeId, d.Notes }) }));

        foreach (var submitted in form.Rows.OrderBy(x => x.RowNumber))
        {
            var hasAnyValue = submitted.Pressure1Lbs is not null
                || submitted.Pressure2Lbs is not null
                || submitted.WeightGrams is not null
                || submitted.StarchScaleValueId is not null
                || submitted.GradeId is not null
                || submitted.DefectsInspected
                || submitted.DefectTypeIds.Count > 0;
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

            if (submitted.GradeId is not null && !validGradeIds.Contains(submitted.GradeId.Value))
            {
                return $"Row {submitted.RowNumber} has an invalid grade.";
            }
            var selectedDefectIds = submitted.DefectTypeIds.Distinct().ToList();
            var existingDefectIds = reading.Defects.Select(x => x.DefectTypeId).ToHashSet();
            if (selectedDefectIds.Any(id => !activeDefectIds.Contains(id) && !existingDefectIds.Contains(id)))
            {
                return $"Row {submitted.RowNumber} has an inactive or invalid defect.";
            }
            var size = SizeCalculationService.Calculate(submitted.WeightGrams, thresholds);
            if (submitted.Pressure1Lbs != submitted.OriginalPressure1Lbs)
            {
                reading.Pressure1Lbs = submitted.Pressure1Lbs;
                reading.Pressure1Source = submitted.Pressure1Lbs is null ? null : "Manual";
            }
            if (submitted.Pressure2Lbs != submitted.OriginalPressure2Lbs)
            {
                reading.Pressure2Lbs = submitted.Pressure2Lbs;
                reading.Pressure2Source = submitted.Pressure2Lbs is null ? null : "Manual";
            }
            reading.WeightGrams = submitted.WeightGrams;
            reading.StarchScaleValueId = submitted.StarchScaleValueId;
            reading.GradeId = submitted.GradeId;
            reading.SizeCategory = size.SizeCategory;
            reading.SizeStatus = size.SizeStatus;
            reading.DefectsInspected = submitted.DefectsInspected;
            dbContext.QcFruitDefects.RemoveRange(reading.Defects.Where(x => !selectedDefectIds.Contains(x.DefectTypeId)));
            foreach (var defectTypeId in selectedDefectIds.Where(id => reading.Defects.All(x => x.DefectTypeId != id)))
            {
                reading.Defects.Add(new QcFruitDefect { DefectTypeId = defectTypeId });
            }
            foreach (var defect in reading.Defects)
            {
                defect.Notes = defect.DefectTypeId == otherDefectId ? submitted.OtherDefectNotes?.Trim() : null;
            }
            // This flag retains the receipt-backed meaning enforced by the database constraint.
            // Field Sample lifecycle completion is evaluated separately and remains partial-friendly.
            reading.IsCompleted = reading.Pressure1Lbs is not null
                && reading.Pressure2Lbs is not null
                && reading.WeightGrams is not null
                && reading.GradeId is not null;
            reading.UpdatedAt = DateTimeOffset.UtcNow;
            reading.FieldVersion++;
        }

        sample.ActualSampleSize = Math.Max(FieldSampleSize, maxAllowedRowNumber);
        sample.UpdatedAt = DateTimeOffset.UtcNow;
        sample.FieldSampleAutosaveVersion++;
        await dbContext.SaveChangesAsync(cancellationToken);
        var afterRows = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .OrderBy(x => x.RowNumber)
            .Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs, x.WeightGrams, x.StarchScaleValueId, x.GradeId, x.SizeCategory, x.DefectsInspected, x.FieldVersion })
            .ToListAsync(cancellationToken);
        var after = JsonSerializer.Serialize(afterRows);
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            await MarkChangedSinceLastSendAsync(sample, "fruit-row-change", user, before, cancellationToken);
            await AuditAsync("edit", nameof(QcSample), sample.Id.ToString(), user, before, new { sample.Id, Rows = afterRows }, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    public async Task<FieldSampleAutosaveResult> AutosaveAsync(long sampleId, FieldSampleAutosaveRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken))
        {
            return new FieldSampleAutosaveResult { Error = "Field Samples Edit access is required." };
        }

        if (string.IsNullOrWhiteSpace(request.ChangeId) || request.ChangeId.Length > 100)
        {
            return new FieldSampleAutosaveResult { Error = "A valid autosave change identifier is required." };
        }

        var source = request.Source is "Scale" or "Browser" or "Manual Save Now" or "Conflict Resolution" ? request.Source : "Browser";
        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.CanonicalOrchardBlock)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);
        if (sample is null || sample.SampleType.Name != FieldSampleTypeName)
        {
            return new FieldSampleAutosaveResult { Error = "Field Sample not found." };
        }

        var rows = await dbContext.QcFruitReadings
            .Include(x => x.Defects).ThenInclude(x => x.DefectType)
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        var conflicts = new List<FieldSampleAutosaveConflict>();
        var validation = new List<FieldSampleAutosaveValidationError>();
        var before = new List<object>();
        var after = new List<object>();

        foreach (var change in request.MetadataChanges)
        {
            var current = MetadataFieldValue(sample, change.Field);
            if (current is null && !KnownMetadataField(change.Field))
            {
                validation.Add(new("metadata", null, change.Field, "This Field Sample field cannot be autosaved."));
                continue;
            }
            if (!AutosaveValuesEqual(change.Field, current, change.OriginalValue)
                && !AutosaveValuesEqual(change.Field, current, change.Value))
            {
                conflicts.Add(new("metadata", null, change.Field, change.Value, current, "This field changed after the page loaded."));
            }
        }

        foreach (var rowChange in request.RowChanges)
        {
            if (rowChange.RowNumber < 1 || rowChange.RowNumber > MaxFieldSampleSize)
            {
                validation.Add(new("row", rowChange.RowNumber, "RowNumber", $"Fruit row must be between 1 and {MaxFieldSampleSize}."));
                continue;
            }
            var row = rows.SingleOrDefault(x => x.RowNumber == rowChange.RowNumber);
            foreach (var change in rowChange.Changes)
            {
                if (string.Equals(change.Field, "SizeCategory", StringComparison.OrdinalIgnoreCase))
                {
                    validation.Add(new("row", rowChange.RowNumber, change.Field, "Size is read-only and is calculated from weight."));
                    continue;
                }
                var current = RowFieldValue(row, change.Field);
                if (current is null && !KnownRowField(change.Field))
                {
                    validation.Add(new("row", rowChange.RowNumber, change.Field, "This fruit-row field cannot be autosaved."));
                    continue;
                }
                if (!AutosaveValuesEqual(change.Field, current, change.OriginalValue)
                    && !AutosaveValuesEqual(change.Field, current, change.Value))
                {
                    conflicts.Add(new("row", rowChange.RowNumber, change.Field, change.Value, current,
                        string.Equals(change.Field, "Pressure1Lbs", StringComparison.OrdinalIgnoreCase) || string.Equals(change.Field, "Pressure2Lbs", StringComparison.OrdinalIgnoreCase)
                            ? "QC Station or another user saved a newer pressure value. Choose which value to keep."
                            : "This field changed after the page loaded. Choose which value to keep."));
                }
            }
        }

        if (conflicts.Count > 0 || validation.Count > 0)
        {
            if (conflicts.Count > 0)
            {
                await AuditAsync("autosave-conflict", nameof(QcSample), sample.Id.ToString(), user, null, new
                {
                    request.ChangeId,
                    Conflicts = conflicts.Select(x => new { x.Scope, x.RowNumber, x.Field, x.Message })
                }, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return await AutosaveResultAsync(sample, user, conflicts, validation, cancellationToken);
        }

        if (request.MetadataChanges.Count > 0)
        {
            var metadata = new FieldSampleMetadataForm
            {
                SampleId = sample.Id,
                OrchardName = sample.FieldSampleGrowerName ?? sample.CanonicalOrchardBlock?.OrchardName ?? "",
                GrowerNumber = sample.FieldSampleGrowerNumber,
                BlockName = sample.FieldSampleOriginalBlockName ?? sample.CanonicalOrchardBlock?.CanonicalBlockName ?? "",
                CanonicalOrchardBlockId = sample.CanonicalOrchardBlockId,
                FruitProfileId = sample.FieldSampleFruitProfileId ?? 0,
                SampleTakenAt = sample.SampleTakenAt,
                Notes = sample.Notes
            };
            foreach (var change in request.MetadataChanges)
            {
                if (AutosaveValuesEqual(change.Field, MetadataFieldValue(sample, change.Field), change.Value)) continue;
                if (!ApplyMetadataChange(metadata, change, validation)) continue;
                before.Add(new { Scope = "metadata", change.Field, OldValue = change.OriginalValue });
                after.Add(new { Scope = "metadata", change.Field, NewValue = change.Value });
            }
            if (validation.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(metadata.OrchardName) || string.IsNullOrWhiteSpace(metadata.BlockName))
                {
                    validation.Add(new("metadata", null, "OrchardName", "Orchard and block are required."));
                }
                else if (ValidateAmbiguousOrchardIdentity(metadata.OrchardName) is { } identityError)
                {
                    validation.Add(new("metadata", null, "OrchardName", identityError));
                }
                else
                {
                    var profile = await dbContext.FruitProfiles.SingleOrDefaultAsync(x => x.Id == metadata.FruitProfileId && x.IsActive, cancellationToken);
                    if (profile is null)
                    {
                        validation.Add(new("metadata", null, "FruitProfileId", "Selected variety was not found."));
                    }
                    else
                    {
                        var (block, blockError) = await ResolveBlockAsync(metadata, user, cancellationToken);
                        if (blockError is not null)
                        {
                            validation.Add(new("metadata", null, "BlockName", blockError));
                        }
                        else
                        {
                            sample.FieldSampleGrowerName = metadata.OrchardName.Trim();
                            sample.FieldSampleGrowerNumber = string.IsNullOrWhiteSpace(metadata.GrowerNumber) ? null : metadata.GrowerNumber.Trim();
                            sample.FieldSampleOriginalBlockName = metadata.BlockName.Trim();
                            sample.CanonicalOrchardBlock = block;
                            sample.CanonicalOrchardBlockId = block?.Id;
                            sample.FieldSampleBlockResolution = "Confirmed";
                            sample.FieldSampleFruitProfile = profile;
                            sample.FieldSampleFruitProfileId = profile.Id;
                            sample.SampleTakenAt = metadata.SampleTakenAt;
                            sample.Notes = string.IsNullOrWhiteSpace(metadata.Notes) ? null : metadata.Notes.Trim();
                        }
                    }
                }
            }
        }

        var activeGradeIds = await dbContext.Grades.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var activeStarchIds = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var activeDefects = await dbContext.DefectTypes.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var activeDefectIds = activeDefects.Select(x => x.Id).ToHashSet();
        var otherDefectId = activeDefects.FirstOrDefault(x => x.Name == "Other")?.Id;
        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.IsActive && sample.FieldSampleFruitProfile != null && x.FruitType == sample.FieldSampleFruitProfile.FruitType)
            .ToListAsync(cancellationToken);
        var varietyChanged = request.MetadataChanges.Any(x => string.Equals(x.Field, "FruitProfileId", StringComparison.OrdinalIgnoreCase));

        foreach (var rowChange in request.RowChanges)
        {
            var reading = rows.SingleOrDefault(x => x.RowNumber == rowChange.RowNumber);
            if (reading is null)
            {
                reading = new QcFruitReading
                {
                    QcSampleId = sample.Id,
                    RowNumber = rowChange.RowNumber,
                    SizeStatus = SizeCalculationService.NotCalculated,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                rows.Add(reading);
                dbContext.QcFruitReadings.Add(reading);
            }

            var rowChanged = false;
            foreach (var change in rowChange.Changes)
            {
                if (AutosaveValuesEqual(change.Field, RowFieldValue(reading, change.Field), change.Value)) continue;
                before.Add(new { Scope = "row", Row = rowChange.RowNumber, change.Field, OldValue = change.OriginalValue });
                if (ApplyRowChange(reading, change, activeGradeIds, activeStarchIds, activeDefectIds, otherDefectId, validation))
                {
                    rowChanged = true;
                    after.Add(new { Scope = "row", Row = rowChange.RowNumber, change.Field, NewValue = change.Value });
                }
            }
            if (rowChanged)
            {
                var calculated = SizeCalculationService.Calculate(reading.WeightGrams, thresholds);
                reading.SizeCategory = calculated.SizeCategory;
                reading.SizeStatus = calculated.SizeStatus;
                reading.IsCompleted = reading.Pressure1Lbs is not null && reading.Pressure2Lbs is not null && reading.WeightGrams is not null && reading.GradeId is not null;
                reading.UpdatedAt = DateTimeOffset.UtcNow;
                reading.FieldVersion++;
            }
        }

        if (varietyChanged)
        {
            foreach (var reading in rows)
            {
                var calculated = SizeCalculationService.Calculate(reading.WeightGrams, thresholds);
                if (reading.SizeCategory == calculated.SizeCategory && reading.SizeStatus == calculated.SizeStatus) continue;
                before.Add(new { Scope = "row", Row = reading.RowNumber, Field = "CalculatedSize", OldValue = reading.SizeCategory });
                reading.SizeCategory = calculated.SizeCategory;
                reading.SizeStatus = calculated.SizeStatus;
                reading.UpdatedAt = DateTimeOffset.UtcNow;
                reading.FieldVersion++;
                after.Add(new { Scope = "row", Row = reading.RowNumber, Field = "CalculatedSize", NewValue = reading.SizeCategory });
            }
        }

        if (validation.Count > 0)
        {
            dbContext.ChangeTracker.Clear();
            sample = await dbContext.QcSamples.AsNoTracking().SingleAsync(x => x.Id == sampleId, cancellationToken);
            return await AutosaveResultAsync(sample, user, [], validation, cancellationToken);
        }

        var requestedTarget = request.TargetSampleSize ?? sample.ActualSampleSize ?? FieldSampleSize;
        if (requestedTarget < FieldSampleSize || requestedTarget > MaxFieldSampleSize)
        {
            return await AutosaveResultAsync(sample, user, [], [new("sample", null, "TargetSampleSize", $"Field Samples support {FieldSampleSize} through {MaxFieldSampleSize} fruit rows.")], cancellationToken);
        }
        var originalTarget = sample.ActualSampleSize ?? FieldSampleSize;
        sample.ActualSampleSize = Math.Max(originalTarget, requestedTarget);
        var changed = before.Count > 0 || sample.ActualSampleSize > originalTarget;
        if (changed)
        {
            sample.UpdatedAt = DateTimeOffset.UtcNow;
            sample.FieldSampleAutosaveVersion++;
            await MarkChangedSinceLastSendAsync(sample, "autosave", user, before, cancellationToken);
            await AuditAsync("autosave", nameof(QcSample), sample.Id.ToString(), user, new { request.ChangeId, Source = source, Changes = before }, new { request.ChangeId, Source = source, Changes = after }, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await AutosaveResultAsync(sample, user, [], [], cancellationToken);
    }

    public async Task<string?> MarkCompleteAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken))
        {
            return "Field Samples Edit access is required.";
        }

        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .Include(x => x.CanonicalOrchardBlock)
            .Include(x => x.FruitReadings)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);
        if (sample is null || sample.SampleType.Name != FieldSampleTypeName)
        {
            return "Field Sample not found.";
        }

        var rows = sample.FruitReadings.OrderBy(x => x.RowNumber).Select(ToFruitReadingRow).ToList();
        var missing = BuildCompletionMissingItems(sample, rows);
        if (missing.Count > 0)
        {
            return $"Field Sample cannot be completed: {string.Join("; ", missing)}";
        }

        var before = new { sample.Status, sample.EmailStatus };
        var hasPriorSend = await dbContext.QcSummaryEmailLogs.AsNoTracking()
            .AnyAsync(x => x.QcSampleId == sample.Id && x.Status == "Sent", cancellationToken);
        sample.Status = hasPriorSend ? "Changed Since Last Send" : "Complete";
        sample.EmailStatus = hasPriorSend ? "Needs Resend" : "Not Sent";
        sample.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync("complete", nameof(QcSample), sample.Id.ToString(), user, before, new { sample.Status, sample.EmailStatus }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<(CanonicalOrchardBlock? Block, string? Error)> ResolveBlockAsync(FieldSampleCreateForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (form.CanonicalOrchardBlockId is not null)
        {
            var selected = await dbContext.CanonicalOrchardBlocks.SingleOrDefaultAsync(x => x.Id == form.CanonicalOrchardBlockId && x.IsActive, cancellationToken);
            if (selected is not null
                && !OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(selected.OrchardName)
                && string.Equals(selected.NormalizedOrchardKey, OrchardBlockMatcher.Normalize(form.OrchardName), StringComparison.Ordinal))
            {
                await EnsureAliasAsync(selected, form.BlockName, user, "manual", cancellationToken);
                return (selected, null);
            }

            return (null, "Selected block was not found for this orchard.");
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
        var canonicalOrchard = await dbContext.CanonicalOrchards.SingleOrDefaultAsync(x => x.NormalizedOrchardKey == orchardKey, cancellationToken);
        if (canonicalOrchard is null)
        {
            canonicalOrchard = new CanonicalOrchard
            {
                OrchardName = form.OrchardName.Trim(),
                NormalizedOrchardKey = orchardKey,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.CanonicalOrchards.Add(canonicalOrchard);
        }

        var created = new CanonicalOrchardBlock
        {
            CanonicalOrchard = canonicalOrchard,
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

    private async Task<IReadOnlyList<CanonicalOrchardBlock>> GetActiveOrchardBlocksAsync(CancellationToken cancellationToken)
    {
        var blocks = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.OrchardName)
            .ThenBy(x => x.CanonicalBlockName)
            .ToListAsync(cancellationToken);
        return blocks
            .Where(x => !OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(x.OrchardName))
            .ToList();
    }

    private static string? ValidateAmbiguousOrchardIdentity(string orchardName)
    {
        var identity = OrchardIdentityClassifier.Classify(orchardName, OrchardIdentitySource.AmbiguousOrchardOrGrower);
        return identity.Kind == OrchardIdentityKind.GrowerNumber
            ? $"{identity.Value} looks like a four-digit grower number. Enter the orchard name in Orchard and keep the number in Grower number."
            : null;
    }

    private static string? NormalizeOptionalGrowerNumber(string? value)
    {
        var normalized = OrchardIdentityClassifier.NormalizeGrowerNumber(value);
        return normalized.Length == 0 ? null : normalized;
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
                x.ActualSampleSize,
                x.FruitReadings.Select(row => new TrendFruitRow(
                    row.RowNumber,
                    row.Pressure1Lbs,
                    row.Pressure2Lbs,
                    row.WeightGrams,
                    row.StarchScaleValue == null ? null : row.StarchScaleValue.Value,
                    row.StarchScaleValueId,
                    row.SizeCategory,
                    row.Grade == null ? null : row.Grade.Code,
                    row.DefectsInspected,
                    row.Defects.Select(defect => defect.DefectType.Name).ToList())).ToList()))
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyList<FieldSampleTrendPoint> BuildTrend(IReadOnlyList<TrendSampleRow> samples)
    {
        var trend = samples.Select(sample =>
        {
            var rows = sample.Rows.Select(ToFruitReadingRow).ToList();
            var highestPersistedRow = sample.Rows.Count == 0 ? 0 : sample.Rows.Max(row => row.RowNumber);
            return new FieldSampleTrendPoint
            {
                SampleId = sample.SampleId,
                SampleTakenAt = sample.SampleTakenAt,
                Variety = sample.Variety,
                TargetSampleSize = Math.Clamp(
                    Math.Max(FieldSampleSize, Math.Max(sample.ActualSampleSize ?? FieldSampleSize, highestPersistedRow)),
                    FieldSampleSize,
                    MaxFieldSampleSize),
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
            .Include(x => x.Grade)
            .Include(x => x.Defects).ThenInclude(x => x.DefectType)
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

    private async Task<int> ResolveFieldSampleTargetSizeAsync(QcSample sample, CancellationToken cancellationToken)
    {
        var maximumSavedRow = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .Select(x => (int?)x.RowNumber)
            .MaxAsync(cancellationToken) ?? 0;
        return Math.Clamp(Math.Max(FieldSampleSize, Math.Max(sample.ActualSampleSize ?? FieldSampleSize, maximumSavedRow)), FieldSampleSize, MaxFieldSampleSize);
    }

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
        var pressures = PressureCalculationService.ValidSideReadings(
            entered.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));
        var inspected = rows.Where(x => x.DefectsInspected).ToList();
        var affected = inspected.Where(x => x.Defects.Count > 0).ToList();
        var defectDistribution = inspected.Count == 0
            ? []
            : inspected.SelectMany(x => x.Defects).Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .Select(x => new FieldSampleDefectSummaryPoint(x.Key, x.Count(), decimal.Round(x.Count() / (decimal)inspected.Count * 100m, 2)))
                .ToList();

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
            MissingPressureCount = entered.Count(x => x.Pressure1Lbs is null && x.Pressure2Lbs is null),
            GradeDistribution = BuildDistribution(entered.Select(x => x.Grade)),
            StarchDistribution = BuildDistribution(entered.Select(x => x.Starch)),
            DefectInspectedFruitCount = inspected.Count,
            DefectAffectedFruitCount = affected.Count,
            DefectAffectedPercentage = inspected.Count == 0 ? null : decimal.Round(affected.Count / (decimal)inspected.Count * 100m, 2),
            DefectDistribution = defectDistribution
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
        GradeId = row.GradeId,
        Grade = row.Grade?.Code,
        SizeCategory = row.SizeCategory,
        SizeStatus = row.SizeStatus,
        IsCompleted = row.IsCompleted,
        DefectsInspected = row.DefectsInspected,
        DefectTypeIds = row.Defects.Select(x => x.DefectTypeId).OrderBy(x => x).ToList(),
        Defects = row.Defects.Select(x => x.DefectType.Name).OrderBy(x => x).ToList(),
        OtherDefectNotes = row.Defects.FirstOrDefault(x => x.DefectType.Name == "Other")?.Notes,
        FieldVersion = row.FieldVersion,
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
        Grade = row.Grade,
        SizeCategory = row.SizeCategory,
        SizeStatus = row.SizeCategory is null ? "NotCalculated" : "Sized",
        DefectsInspected = row.DefectsInspected,
        Defects = row.Defects,
        EntryStatus = HasEnteredData(row) ? "In Progress" : "Empty"
    };

    private static bool HasEnteredData(FruitReadingRowViewModel row) =>
        row.Pressure1Lbs is not null || row.Pressure2Lbs is not null || row.WeightGrams is not null || row.StarchScaleValueId is not null || row.SizeCategory is not null || row.GradeId is not null || !string.IsNullOrWhiteSpace(row.Grade) || row.DefectsInspected || row.Defects.Count > 0;

    private static bool HasEnteredData(QcFruitReading row) =>
        row.Pressure1Lbs is not null || row.Pressure2Lbs is not null || row.WeightGrams is not null || row.StarchScaleValueId is not null || row.SizeCategory is not null || row.GradeId is not null || row.DefectsInspected || row.Defects.Count > 0;

    private static bool HasEnteredData(TrendFruitRow row) =>
        row.Pressure1Lbs is not null || row.Pressure2Lbs is not null || row.WeightGrams is not null || row.StarchScaleValueId is not null || row.SizeCategory is not null || !string.IsNullOrWhiteSpace(row.Grade) || row.DefectsInspected || row.Defects.Count > 0;

    private static IReadOnlyList<FieldSampleDistributionPoint> BuildDistribution(IEnumerable<string?> values)
    {
        var represented = values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
        if (represented.Count == 0)
        {
            return [];
        }

        return represented.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key)
            .Select(x => new FieldSampleDistributionPoint(x.Key, decimal.Round(x.Count() / (decimal)represented.Count * 100m, 2)))
            .ToList();
    }

    private static FieldSampleQcStationStatusViewModel BuildQcStationStatus(QcStation? station)
    {
        if (station is null)
        {
            return new FieldSampleQcStationStatusViewModel();
        }

        var lastContact = station.LastSyncAt ?? station.LastSeenAt;
        if (!station.IsActive)
        {
            return new FieldSampleQcStationStatusViewModel
            {
                State = "Error",
                Message = $"{station.StationName} is enrolled but inactive. Ask an administrator to reactivate or replace its configuration.",
                StationCode = station.StationCode,
                StationName = station.StationName,
                LastSeenAt = station.LastSeenAt,
                LastSyncAt = station.LastSyncAt
            };
        }

        var recentlyConnected = lastContact is not null && lastContact >= DateTimeOffset.UtcNow.AddMinutes(-5);
        return new FieldSampleQcStationStatusViewModel
        {
            State = recentlyConnected ? "Connected" : "Disconnected",
            Message = recentlyConnected
                ? $"{station.StationName} recently synchronized this Field Sample."
                : $"{station.StationName} synchronized this Field Sample previously but is not currently reporting. Open QC Station to reconnect.",
            StationCode = station.StationCode,
            StationName = station.StationName,
            LastSeenAt = station.LastSeenAt,
            LastSyncAt = station.LastSyncAt
        };
    }

    private async Task<FieldSampleAutosaveResult> AutosaveResultAsync(
        QcSample sample,
        ClaimsPrincipal user,
        IReadOnlyList<FieldSampleAutosaveConflict> conflicts,
        IReadOnlyList<FieldSampleAutosaveValidationError> validation,
        CancellationToken cancellationToken)
    {
        var refresh = await GetRefreshAsync(sample.Id, user, cancellationToken);
        return new FieldSampleAutosaveResult
        {
            Saved = conflicts.Count == 0 && validation.Count == 0,
            SavedAt = conflicts.Count == 0 && validation.Count == 0 ? sample.UpdatedAt ?? BusinessTime.UtcNow : null,
            AutosaveVersion = refresh?.AutosaveVersion ?? sample.FieldSampleAutosaveVersion,
            MetadataValues = new Dictionary<string, string?>
            {
                ["OrchardName"] = sample.FieldSampleGrowerName,
                ["GrowerNumber"] = sample.FieldSampleGrowerNumber,
                ["BlockName"] = sample.FieldSampleOriginalBlockName,
                ["CanonicalOrchardBlockId"] = AutosaveText(sample.CanonicalOrchardBlockId),
                ["ConfirmCreateNewBlock"] = "false",
                ["FruitProfileId"] = AutosaveText(sample.FieldSampleFruitProfileId),
                ["SampleTakenAt"] = BusinessTime.FormatPacificInput(sample.SampleTakenAt),
                ["Notes"] = sample.Notes
            },
            Rows = refresh?.Rows ?? [],
            Conflicts = conflicts,
            ValidationErrors = validation
        };
    }

    private static bool KnownMetadataField(string field) => field is
        "OrchardName" or "GrowerNumber" or "BlockName" or "CanonicalOrchardBlockId" or "ConfirmCreateNewBlock" or "FruitProfileId" or "SampleTakenAt" or "Notes";

    private string? MetadataFieldValue(QcSample sample, string field) => field switch
    {
        "OrchardName" => sample.FieldSampleGrowerName ?? sample.CanonicalOrchardBlock?.OrchardName,
        "GrowerNumber" => sample.FieldSampleGrowerNumber,
        "BlockName" => sample.FieldSampleOriginalBlockName ?? sample.CanonicalOrchardBlock?.CanonicalBlockName,
        "CanonicalOrchardBlockId" => AutosaveText(sample.CanonicalOrchardBlockId),
        "ConfirmCreateNewBlock" => "false",
        "FruitProfileId" => AutosaveText(sample.FieldSampleFruitProfileId),
        "SampleTakenAt" => BusinessTime.FormatPacificInput(sample.SampleTakenAt),
        "Notes" => sample.Notes,
        _ => null
    };

    private static bool ApplyMetadataChange(FieldSampleMetadataForm form, FieldSampleAutosaveFieldChange change, ICollection<FieldSampleAutosaveValidationError> validation)
    {
        switch (change.Field)
        {
            case "OrchardName": form.OrchardName = change.Value ?? ""; return true;
            case "GrowerNumber": form.GrowerNumber = change.Value; return true;
            case "BlockName": form.BlockName = change.Value ?? ""; return true;
            case "CanonicalOrchardBlockId":
                if (!TryNullableInt(change.Value, out var blockId)) { validation.Add(new("metadata", null, change.Field, "Select a valid canonical block.")); return false; }
                form.CanonicalOrchardBlockId = blockId; return true;
            case "ConfirmCreateNewBlock":
                if (!bool.TryParse(change.Value, out var confirm)) { validation.Add(new("metadata", null, change.Field, "New-block confirmation is invalid.")); return false; }
                form.ConfirmCreateNewBlock = confirm; return true;
            case "FruitProfileId":
                if (!int.TryParse(change.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var profileId)) { validation.Add(new("metadata", null, change.Field, "Select a valid variety.")); return false; }
                form.FruitProfileId = profileId; return true;
            case "SampleTakenAt":
                if (!DateTimeOffset.TryParse(change.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var takenAt)) { validation.Add(new("metadata", null, change.Field, "Enter a valid sample date and time.")); return false; }
                form.SampleTakenAt = takenAt; return true;
            case "Notes": form.Notes = change.Value; return true;
            default: return false;
        }
    }

    private static bool KnownRowField(string field) => field is
        "Pressure1Lbs" or "Pressure2Lbs" or "WeightGrams" or "StarchScaleValueId" or "GradeId" or "DefectsInspected" or "DefectTypeIds" or "OtherDefectNotes";

    private static string? RowFieldValue(QcFruitReading? row, string field)
    {
        if (row is null) return null;
        return field switch
        {
            "Pressure1Lbs" => AutosaveText(row.Pressure1Lbs),
            "Pressure2Lbs" => AutosaveText(row.Pressure2Lbs),
            "WeightGrams" => AutosaveText(row.WeightGrams),
            "StarchScaleValueId" => AutosaveText(row.StarchScaleValueId),
            "GradeId" => AutosaveText(row.GradeId),
            "DefectsInspected" => row.DefectsInspected ? "true" : "false",
            "DefectTypeIds" => string.Join(",", row.Defects.Select(x => x.DefectTypeId).OrderBy(x => x)),
            "OtherDefectNotes" => row.Defects.FirstOrDefault(x => x.DefectType.Name == "Other")?.Notes,
            _ => null
        };
    }

    private bool ApplyRowChange(
        QcFruitReading reading,
        FieldSampleAutosaveFieldChange change,
        IReadOnlySet<int> activeGradeIds,
        IReadOnlySet<int> activeStarchIds,
        IReadOnlySet<int> activeDefectIds,
        int? otherDefectId,
        ICollection<FieldSampleAutosaveValidationError> validation)
    {
        switch (change.Field)
        {
            case "Pressure1Lbs":
                if (!TryNullableDecimal(change.Value, out var p1) || p1 < 0) { validation.Add(new("row", reading.RowNumber, change.Field, "Pressure 1 must be a non-negative number.")); return false; }
                reading.Pressure1Lbs = p1; reading.Pressure1Source = p1 is null ? null : "Manual"; return true;
            case "Pressure2Lbs":
                if (!TryNullableDecimal(change.Value, out var p2) || p2 < 0) { validation.Add(new("row", reading.RowNumber, change.Field, "Pressure 2 must be a non-negative number.")); return false; }
                reading.Pressure2Lbs = p2; reading.Pressure2Source = p2 is null ? null : "Manual"; return true;
            case "WeightGrams":
                if (!TryNullableDecimal(change.Value, out var weight) || weight < 0) { validation.Add(new("row", reading.RowNumber, change.Field, "Weight must be a non-negative number of grams.")); return false; }
                reading.WeightGrams = weight; return true;
            case "StarchScaleValueId":
                if (!TryNullableInt(change.Value, out var starchId) || starchId is not null && !activeStarchIds.Contains(starchId.Value)) { validation.Add(new("row", reading.RowNumber, change.Field, "Select a valid active starch value.")); return false; }
                reading.StarchScaleValueId = starchId; return true;
            case "GradeId":
                if (!TryNullableInt(change.Value, out var gradeId) || gradeId is not null && !activeGradeIds.Contains(gradeId.Value)) { validation.Add(new("row", reading.RowNumber, change.Field, "Select a valid active grade.")); return false; }
                reading.GradeId = gradeId; return true;
            case "DefectsInspected":
                if (!bool.TryParse(change.Value, out var inspected)) { validation.Add(new("row", reading.RowNumber, change.Field, "Defect inspection state is invalid.")); return false; }
                reading.DefectsInspected = inspected; return true;
            case "DefectTypeIds":
                var ids = ParseIds(change.Value);
                var existingIds = reading.Defects.Select(x => x.DefectTypeId).ToHashSet();
                if (ids.Any(id => !activeDefectIds.Contains(id) && !existingIds.Contains(id))) { validation.Add(new("row", reading.RowNumber, change.Field, "One or more defects are inactive or invalid.")); return false; }
                dbContext.QcFruitDefects.RemoveRange(reading.Defects.Where(x => !ids.Contains(x.DefectTypeId)));
                foreach (var id in ids.Where(id => reading.Defects.All(x => x.DefectTypeId != id))) reading.Defects.Add(new QcFruitDefect { DefectTypeId = id });
                if (ids.Count > 0) reading.DefectsInspected = true;
                return true;
            case "OtherDefectNotes":
                if (change.Value?.Length > 500) { validation.Add(new("row", reading.RowNumber, change.Field, "Other defect notes may not exceed 500 characters.")); return false; }
                var other = otherDefectId is null ? null : reading.Defects.FirstOrDefault(x => x.DefectTypeId == otherDefectId.Value);
                if (other is null && !string.IsNullOrWhiteSpace(change.Value)) { validation.Add(new("row", reading.RowNumber, change.Field, "Select the Other defect before entering notes.")); return false; }
                if (other is not null) other.Notes = string.IsNullOrWhiteSpace(change.Value) ? null : change.Value.Trim();
                return true;
            default: return false;
        }
    }

    private static bool AutosaveValuesEqual(string field, string? left, string? right)
    {
        var normalizedLeft = string.IsNullOrWhiteSpace(left) ? null : left.Trim();
        var normalizedRight = string.IsNullOrWhiteSpace(right) ? null : right.Trim();
        if (field is "Pressure1Lbs" or "Pressure2Lbs" or "WeightGrams")
        {
            return TryNullableDecimal(normalizedLeft, out var leftNumber)
                && TryNullableDecimal(normalizedRight, out var rightNumber)
                && leftNumber == rightNumber;
        }
        if (field is "CanonicalOrchardBlockId" or "FruitProfileId" or "StarchScaleValueId" or "GradeId")
        {
            return TryNullableInt(normalizedLeft, out var leftNumber)
                && TryNullableInt(normalizedRight, out var rightNumber)
                && leftNumber == rightNumber;
        }
        if (field == "DefectTypeIds")
        {
            return ParseIds(normalizedLeft).SetEquals(ParseIds(normalizedRight));
        }
        if (field is "DefectsInspected" or "ConfirmCreateNewBlock")
        {
            return bool.TryParse(normalizedLeft ?? "false", out var leftBoolean)
                && bool.TryParse(normalizedRight ?? "false", out var rightBoolean)
                && leftBoolean == rightBoolean;
        }
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? AutosaveText<T>(T? value) where T : struct, IFormattable => value?.ToString(null, CultureInfo.InvariantCulture);
    private static bool TryNullableDecimal(string? value, out decimal? result)
    {
        if (string.IsNullOrWhiteSpace(value)) { result = null; return true; }
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) { result = parsed; return true; }
        result = null; return false;
    }
    private static bool TryNullableInt(string? value, out int? result)
    {
        if (string.IsNullOrWhiteSpace(value)) { result = null; return true; }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) { result = parsed; return true; }
        result = null; return false;
    }
    private static HashSet<int> ParseIds(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(x => x > 0)
            .ToHashSet();

    private static IReadOnlyList<PhotoGroupViewModel> GroupPhotos(IReadOnlyList<QcPhoto> photos, bool canDelete, long sampleId) =>
        photos.GroupBy(x => QcPhotoRequirementPolicy.NormalizePhotoType(x.PhotoType))
            .OrderBy(x => x.Key)
            .Select(group => new PhotoGroupViewModel(
                group.Key,
                group.Select(photo => new PhotoMetadataViewModel(
                    photo.Id,
                    photo.QcSampleId,
                    sampleId,
                    QcPhotoRequirementPolicy.NormalizePhotoType(photo.PhotoType),
                    photo.PhotoSource,
                    photo.FileName,
                    photo.ContentType,
                    photo.FileSizeBytes,
                    $"/FieldSamples/{sampleId}/photos/{photo.Id}/content",
                    photo.CapturedAt,
                    canDelete,
                    $"/FieldSamples/{sampleId}/photos/{photo.Id}/remove",
                    true)).ToList()))
            .ToList();

    private async Task RecalculatePersistedSizesAsync(long sampleId, string fruitType, ClaimsPrincipal user, string source, CancellationToken cancellationToken)
    {
        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.IsActive && x.FruitType == fruitType)
            .ToListAsync(cancellationToken);
        var readings = await dbContext.QcFruitReadings.Where(x => x.QcSampleId == sampleId).ToListAsync(cancellationToken);
        var changes = new List<object>();
        foreach (var reading in readings)
        {
            var calculated = SizeCalculationService.Calculate(reading.WeightGrams, thresholds);
            if (reading.SizeCategory == calculated.SizeCategory && reading.SizeStatus == calculated.SizeStatus) continue;
            changes.Add(new { reading.RowNumber, OldSize = reading.SizeCategory, NewSize = calculated.SizeCategory, calculated.SizeStatus });
            reading.SizeCategory = calculated.SizeCategory;
            reading.SizeStatus = calculated.SizeStatus;
            reading.UpdatedAt = DateTimeOffset.UtcNow;
            reading.FieldVersion++;
        }
        if (changes.Count > 0)
        {
            await AuditAsync("calculate-size", nameof(QcSample), sampleId.ToString(), user, null, new { Source = source, FruitType = fruitType, Changes = changes }, cancellationToken);
        }
    }

    private async Task MarkChangedSinceLastSendAsync(QcSample sample, string reason, ClaimsPrincipal user, object? before, CancellationToken cancellationToken)
    {
        var hasPriorSend = string.Equals(sample.EmailStatus, "Sent", StringComparison.OrdinalIgnoreCase)
            || await dbContext.QcSummaryEmailLogs.AsNoTracking().AnyAsync(x => x.QcSampleId == sample.Id && x.Status == "Sent", cancellationToken);
        if (!hasPriorSend)
        {
            return;
        }

        sample.EmailStatus = "Needs Resend";
        sample.Status = "Changed Since Last Send";
        await AuditAsync("changed-after-send", nameof(QcSample), sample.Id.ToString(), user, before, new { sample.Status, sample.EmailStatus, Reason = reason }, cancellationToken);
    }

    private static IReadOnlyList<string> BuildCompletionMissingItems(QcSample sample, IReadOnlyList<FruitReadingRowViewModel> rows)
    {
        var missing = new List<string>();
        if (sample.CanonicalOrchardBlockId is null || sample.CanonicalOrchardBlock is null)
        {
            missing.Add("Confirm an orchard and canonical block.");
        }
        if (sample.FieldSampleFruitProfileId is null)
        {
            missing.Add("Select a variety.");
        }
        if (sample.SampleTakenAt == default)
        {
            missing.Add("Enter a sample date and time.");
        }
        if (!rows.Any(HasEnteredData))
        {
            missing.Add("Save at least one fruit measurement.");
        }
        return missing;
    }

    private static string NormalizeLifecycleStatus(string? status, string? emailStatus)
    {
        if (string.Equals(emailStatus, "Needs Resend", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Changed Since Last Send", StringComparison.OrdinalIgnoreCase))
        {
            return "Changed Since Last Send";
        }
        if (string.Equals(emailStatus, "Sent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            return "Sent";
        }
        if (string.Equals(status, "Complete", StringComparison.OrdinalIgnoreCase))
        {
            return "Complete";
        }
        return "In Progress";
    }

    private static string NormalizeEmailStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) || string.Equals(status, "Not Applicable", StringComparison.OrdinalIgnoreCase)
            ? "Not Sent"
            : status;

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

    private sealed record TrendSampleRow(long SampleId, DateTimeOffset SampleTakenAt, string Variety, int? ActualSampleSize, IReadOnlyList<TrendFruitRow> Rows);
    private sealed record TrendFruitRow(int RowNumber, decimal? Pressure1Lbs, decimal? Pressure2Lbs, decimal? WeightGrams, decimal? Starch, int? StarchScaleValueId, int? SizeCategory, string? Grade, bool DefectsInspected, IReadOnlyList<string> Defects);
}
