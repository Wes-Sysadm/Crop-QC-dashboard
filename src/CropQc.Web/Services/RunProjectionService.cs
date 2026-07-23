using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRunProjectionService
{
    Task<RunProjectionPlannerViewModel> GetPlannerAsync(DateOnly? date, long? projectionId, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunProjectionSourceCandidateViewModel>> SearchSourcesAsync(string? query, int? roomId, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<(long? Id, string? Error)> CreateAsync(RunProjectionCreateForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateHeaderAsync(RunProjectionHeaderForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> AddSourceAsync(RunProjectionAddSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateSourceAsync(RunProjectionUpdateSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> ApplyPackoutToAllAsync(RunProjectionApplyPackoutForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> RefreshSourceAsync(long projectionId, long sourceId, long concurrencyVersion, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> RemoveSourceAsync(long projectionId, long sourceId, long concurrencyVersion, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> MarkReadyAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> CancelAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<(long? Id, string? Error)> DuplicateAsync(RunProjectionDuplicateForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public static class RunProjectionSettings
{
    public const string ApplePoundsPerBinKey = "RunProjection__ApplePoundsPerBin";
    public const string PearPoundsPerBinKey = "RunProjection__PearPoundsPerBin";
    public const string StandardBoxWeightKey = "RunProjection__StandardBoxWeightPounds";
    public const string DraftExpirationDaysKey = "RunProjection__DraftExpirationDays";
    public const string VisibilityPastDaysKey = "RunProjection__VisibilityPastDays";
    public const string VisibilityFutureDaysKey = "RunProjection__VisibilityFutureDays";
    public const string DefaultExpectedPackoutPercentKey = "RunProjection__DefaultExpectedPackoutPercent";
    public const string MinimumDistributionFruitKey = "RunProjection__MinimumDistributionFruit";

    public const int DefaultDraftExpirationDays = 14;
    public const int DefaultVisibilityPastDays = 30;
    public const int DefaultVisibilityFutureDays = 14;
    public const int DefaultMinimumDistributionFruit = 10;
}

public sealed class RunProjectionService(
    CropQcDbContext dbContext,
    IBinsRunService binsRunService,
    IUserAccessService userAccessService,
    ICropYearService cropYearService,
    IBusinessTimeService businessTime) : IRunProjectionService
{
    private const string FieldSampleTypeName = "Field Sample";
    private const string SourceApplication = "CropQc.Web";

    public async Task<RunProjectionPlannerViewModel> GetPlannerAsync(
        DateOnly? date,
        long? projectionId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAsync(user, PageAccessLevel.View, cancellationToken);
        var settings = await LoadSettingsAsync(cancellationToken);
        await ExpireDraftsAsync(settings.DraftExpirationDays, cancellationToken);
        var today = businessTime.PacificDate(businessTime.UtcNow);
        var selectedDate = date ?? today;
        var start = today.AddDays(-settings.VisibilityPastDays);
        var end = today.AddDays(settings.VisibilityFutureDays);

        var calendarCounts = await dbContext.RunProjections.AsNoTracking()
            .Where(x => x.PlannedRunDate >= start && x.PlannedRunDate <= end)
            .GroupBy(x => x.PlannedRunDate)
            .Select(x => new { Date = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count, cancellationToken);

        var records = await dbContext.RunProjections.AsNoTracking()
            .Where(x => x.PlannedRunDate == selectedDate)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new RunProjectionListItemViewModel
            {
                Id = x.Id,
                PlannedRunDate = x.PlannedRunDate,
                Name = x.Name,
                Status = x.Status,
                TotalPlannedBins = x.TotalPlannedBins,
                TotalProjectedBoxes = x.TotalProjectedBoxes,
                TotalRoundedProjectedBoxes = x.TotalRoundedProjectedBoxes,
                Creator = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                UpdatedAt = x.UpdatedAt,
                SourceCount = x.Sources.Count,
                ConvertedSourceCount = x.Sources.Count(source => source.ActualBinsRunEntryId != null)
            })
            .ToListAsync(cancellationToken);

        var selectedId = projectionId ?? records.FirstOrDefault()?.Id;
        var canEdit = await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Edit, cancellationToken);
        var canAdmin = await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Admin, cancellationToken);
        return new RunProjectionPlannerViewModel
        {
            SelectedDate = selectedDate,
            CalendarDays = Enumerable.Range(0, settings.VisibilityPastDays + settings.VisibilityFutureDays + 1)
                .Select(offset => start.AddDays(offset))
                .Select(day => new RunProjectionCalendarDayViewModel(
                    day,
                    calendarCounts.GetValueOrDefault(day),
                    day == selectedDate,
                    day == today))
                .ToList(),
            Projections = records,
            SelectedProjection = selectedId is null ? null : await GetDetailAsync(selectedId.Value, canEdit, cancellationToken),
            CreateForm = new RunProjectionCreateForm
            {
                PlannedRunDate = selectedDate,
                Name = $"Run {records.Count + 1}"
            },
            CanEdit = canEdit,
            CanAdmin = canAdmin,
            VisibilityPastDays = settings.VisibilityPastDays,
            VisibilityFutureDays = settings.VisibilityFutureDays
        };
    }

    public async Task<IReadOnlyList<RunProjectionSourceCandidateViewModel>> SearchSourcesAsync(
        string? query,
        int? roomId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAsync(user, PageAccessLevel.View, cancellationToken);
        var normalized = query?.Trim() ?? "";
        var normalizedUpper = normalized.ToUpperInvariant();
        var activeCropYear = cropYearService.GetCurrentCropYear(businessTime.NowPacific);
        var inventory = await binsRunService.SearchPlanningInventoryAsync(normalized, roomId, 50, cancellationToken);
        var receiptIds = inventory.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).Distinct().ToList();
        var blockProfileKeys = inventory
            .Where(x => x.CanonicalOrchardBlockId != null && x.FruitProfileId != null)
            .Select(x => new { BlockId = x.CanonicalOrchardBlockId!.Value, ProfileId = x.FruitProfileId!.Value })
            .Distinct()
            .ToList();
        var blockIds = blockProfileKeys.Select(x => x.BlockId).Distinct().ToList();
        var profileIds = blockProfileKeys.Select(x => x.ProfileId).Distinct().ToList();

        var receiptQcIds = receiptIds.Count == 0
            ? []
            : await UsableQcSamples(activeCropYear)
                .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                .Select(x => x.ReceiptId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        var fieldMatches = blockIds.Count == 0 || profileIds.Count == 0
            ? []
            : await UsableFieldSamples(activeCropYear)
                .Where(x => blockIds.Contains(x.CanonicalOrchardBlockId!.Value)
                    && profileIds.Contains(x.FieldSampleFruitProfileId!.Value))
                .Select(x => new { BlockId = x.CanonicalOrchardBlockId!.Value, ProfileId = x.FieldSampleFruitProfileId!.Value })
                .Distinct()
                .ToListAsync(cancellationToken);
        var fieldMatchKeys = fieldMatches.Select(x => $"{x.BlockId}:{x.ProfileId}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = inventory.Select(x => new RunProjectionSourceCandidateViewModel(
                x.InventoryKey,
                RunProjectionSourceTypes.Inventory,
                $"{x.Facility} / {x.Room} — {x.Grower}"
                    + (string.IsNullOrWhiteSpace(x.GrowerNumber) ? "" : $" — grower # {x.GrowerNumber}")
                    + (string.IsNullOrWhiteSpace(x.ReceiptReference) ? "" : $" — receipt {x.ReceiptReference}")
                    + $" — lot {x.Lot} — {x.Variety} — {x.CurrentBins} bins",
                x.Facility,
                x.Room,
                x.Lot,
                x.GrowerNumber,
                x.Grower,
                null,
                null,
                x.Variety,
                RunProjectionCalculationService.NormalizeCommodity(x.FruitType),
                x.CurrentBins,
                x.ReceiptId is not null && receiptQcIds.Contains(x.ReceiptId.Value),
                x.CanonicalOrchardBlockId is not null
                    && x.FruitProfileId is not null
                    && fieldMatchKeys.Contains($"{x.CanonicalOrchardBlockId}:{x.FruitProfileId}"),
                x.ReceiptDate))
            .ToList();

        var fieldQuery = UsableFieldSamples(activeCropYear)
            .Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
            .Include(x => x.FieldSampleFruitProfile)
            .Where(x => normalized.Length == 0
                || x.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName.ToUpper().Contains(normalizedUpper)
                || x.CanonicalOrchardBlock.CanonicalBlockName.ToUpper().Contains(normalizedUpper)
                || (x.FieldSampleGrowerName ?? "").ToUpper().Contains(normalizedUpper)
                || (x.FieldSampleGrowerNumber ?? "").ToUpper().Contains(normalizedUpper)
                || x.FieldSampleFruitProfile!.Name.ToUpper().Contains(normalizedUpper)
                || x.FieldSampleFruitProfile.VarietyCode.ToUpper().Contains(normalizedUpper));
        var fieldRows = await fieldQuery
            .OrderByDescending(x => x.SampleTakenAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        candidates.AddRange(fieldRows
            .GroupBy(x => new { x.CanonicalOrchardBlockId, x.FieldSampleFruitProfileId })
            .Select(x => x.OrderByDescending(y => y.SampleTakenAt).ThenByDescending(y => y.Id).First())
            .Take(50)
            .Select(x => new RunProjectionSourceCandidateViewModel(
                $"F:{x.Id}",
                RunProjectionSourceTypes.FieldSample,
                $"{x.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName} — {x.CanonicalOrchardBlock.CanonicalBlockName} — {x.FieldSampleFruitProfile!.Name} — Field Sample {businessTime.FormatPacific(x.SampleTakenAt, "MMM d, yyyy", false)}",
                null,
                null,
                null,
                x.CanonicalOrchardBlock.CanonicalOrchard.OrchardName,
                x.FieldSampleGrowerName,
                x.FieldSampleGrowerNumber,
                x.CanonicalOrchardBlock.CanonicalBlockName,
                x.FieldSampleFruitProfile.Name,
                RunProjectionCalculationService.NormalizeCommodity(x.FieldSampleFruitProfile.FruitType),
                null,
                false,
                true,
                x.SampleTakenAt)));

        return candidates
            .OrderBy(x => x.SourceType)
            .ThenBy(x => x.Facility)
            .ThenBy(x => x.Room)
            .ThenBy(x => x.Orchard)
            .ThenBy(x => x.Block)
            .ThenBy(x => x.Variety)
            .Take(100)
            .ToList();
    }

    public async Task<(long? Id, string? Error)> CreateAsync(
        RunProjectionCreateForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Bins Run Edit access is required to create projections.");
        }

        var name = form.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100)
        {
            return (null, "Projection name is required and must be 100 characters or fewer.");
        }

        var settings = await LoadSettingsAsync(cancellationToken);
        var now = businessTime.UtcNow;
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var projection = new RunProjection
        {
            PlannedRunDate = form.PlannedRunDate,
            Name = name,
            Status = RunProjectionStatuses.Draft,
            CropYear = cropYearService.GetCurrentCropYear(businessTime.NowPacific),
            ApplePoundsPerBin = settings.ApplePoundsPerBin,
            PearPoundsPerBin = settings.PearPoundsPerBin,
            StandardBoxWeightPounds = settings.StandardBoxWeightPounds,
            ExpiresAt = now.AddDays(settings.DraftExpirationDays),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        dbContext.RunProjections.Add(projection);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync("Create", projection, userId, null, ProjectionSnapshot(projection), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (projection.Id, null);
    }

    public async Task<string?> UpdateHeaderAsync(RunProjectionHeaderForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.Id, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var name = form.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100) return "Projection name is required and must be 100 characters or fewer.";
        var before = ProjectionSnapshot(projection);
        projection.Name = name;
        projection.PlannedRunDate = form.PlannedRunDate;
        Touch(projection, userId);
        await AddAuditAsync("UpdateHeader", projection, userId, before, ProjectionSnapshot(projection), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> AddSourceAsync(RunProjectionAddSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        if (form.PlannedBins <= 0) return "Planned bins must be greater than zero.";
        var settings = await LoadSettingsAsync(cancellationToken);
        var source = await ResolveSourceAsync(
            projection,
            form.SourceKey,
            form.SelectedQcSource,
            form.PlannedBins,
            form.ExpectedPackoutPercent ?? settings.DefaultExpectedPackoutPercent,
            settings.MinimumDistributionFruit,
            form.AvailabilityOverrideAcknowledged,
            cancellationToken);
        if (source.Error is not null || source.Entity is null) return source.Error;
        if (projection.Sources.Any(x => string.Equals(x.SourceType, source.Entity.SourceType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.InventoryKey, source.Entity.InventoryKey, StringComparison.OrdinalIgnoreCase)
            && x.FieldSampleId == source.Entity.FieldSampleId))
        {
            return "That source is already part of this projection.";
        }

        source.Entity.SortOrder = projection.Sources.Count == 0 ? 1 : projection.Sources.Max(x => x.SortOrder) + 1;
        projection.Sources.Add(source.Entity);
        RecalculateTotals(projection);
        Touch(projection, userId);
        await AddAuditAsync("AddSource", projection, userId, null, SourceSnapshot(source.Entity), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> UpdateSourceAsync(RunProjectionUpdateSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var source = projection.Sources.SingleOrDefault(x => x.Id == form.SourceId);
        if (source is null) return "Projection source was not found.";
        if (source.ActualBinsRunEntryId is not null) return "A source linked to an actual run cannot be changed.";
        if (form.PlannedBins <= 0) return "Planned bins must be greater than zero.";
        if (form.ExpectedPackoutPercent is < 0 or > 100) return "Expected Packout % must be between 0 and 100.";
        var settings = await LoadSettingsAsync(cancellationToken);

        var before = SourceSnapshot(source);
        source.PlannedBins = form.PlannedBins;
        source.AvailabilityOverrideAcknowledged = form.AvailabilityOverrideAcknowledged;
        source.SortOrder = Math.Max(0, form.SortOrder);
        source.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        if (source.AvailableBinsSnapshot is int available && source.PlannedBins > available && !source.AvailabilityOverrideAcknowledged)
        {
            return $"Planned bins exceed the saved available quantity of {available}. Confirm the planning override to continue.";
        }

        var requestedChoice = string.IsNullOrWhiteSpace(form.SelectedQcSource)
            ? RunProjectionQcSourceTypes.Automatic
            : form.SelectedQcSource.Trim();
        var selectionChanged = !ChoiceMatchesSnapshot(source, requestedChoice);
        source.ExpectedPackoutPercent = form.ExpectedPackoutPercent;
        if (selectionChanged)
        {
            var qc = await ResolveQcSampleAsync(projection.CropYear, source, requestedChoice, cancellationToken);
            if (qc.Error is not null) return qc.Error;
            ApplyQcAndCalculation(projection, source, qc.Sample, qc.SourceType, settings.MinimumDistributionFruit);
        }
        else
        {
            RecalculateFromSnapshot(projection, source, settings.MinimumDistributionFruit);
        }
        source.UpdatedAt = businessTime.UtcNow;
        RecalculateTotals(projection);
        Touch(projection, userId);
        await AddAuditAsync("UpdateSource", projection, userId, before, SourceSnapshot(source), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ApplyPackoutToAllAsync(
        RunProjectionApplyPackoutForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (form.ExpectedPackoutPercent is < 0 or > 100) return "Expected Packout % must be between 0 and 100.";
        var (projection, error, userId) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var before = projection.Sources.Select(x => new { x.Id, x.ExpectedPackoutPercent }).ToList();
        var settings = await LoadSettingsAsync(cancellationToken);
        foreach (var source in projection.Sources.Where(x => x.ActualBinsRunEntryId is null))
        {
            source.ExpectedPackoutPercent = decimal.Round(form.ExpectedPackoutPercent, 2);
            RecalculateFromSnapshot(projection, source, settings.MinimumDistributionFruit);
            source.UpdatedAt = businessTime.UtcNow;
        }
        RecalculateTotals(projection);
        Touch(projection, userId);
        await AddAuditAsync(
            "ApplyPackoutToAll",
            projection,
            userId,
            before,
            new { form.ExpectedPackoutPercent, Sources = projection.Sources.Select(x => new { x.Id, x.ExpectedPackoutPercent }) },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> RefreshSourceAsync(
        long projectionId,
        long sourceId,
        long concurrencyVersion,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(projectionId, concurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var source = projection.Sources.SingleOrDefault(x => x.Id == sourceId);
        if (source is null) return "Projection source was not found.";
        if (source.SelectedQcSampleId is null) return "Select a QC sample before refreshing.";
        var before = SourceSnapshot(source);
        var settings = await LoadSettingsAsync(cancellationToken);
        var choice = source.SelectedQcSourceType == RunProjectionQcSourceTypes.FieldSample
            ? $"FieldSample:{source.SelectedQcSampleId}"
            : $"ReceiptQc:{source.SelectedQcSampleId}";
        var qc = await ResolveQcSampleAsync(projection.CropYear, source, choice, cancellationToken);
        if (qc.Error is not null || qc.Sample is null) return qc.Error ?? "The selected QC sample is no longer available.";
        ApplyQcAndCalculation(projection, source, qc.Sample, qc.SourceType, settings.MinimumDistributionFruit);
        source.UpdatedAt = businessTime.UtcNow;
        RecalculateTotals(projection);
        Touch(projection, userId);
        await AddAuditAsync("RefreshQcSnapshot", projection, userId, before, SourceSnapshot(source), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> RemoveSourceAsync(
        long projectionId,
        long sourceId,
        long concurrencyVersion,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(projectionId, concurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var source = projection.Sources.SingleOrDefault(x => x.Id == sourceId);
        if (source is null) return "Projection source was not found.";
        if (source.ActualBinsRunEntryId is not null) return "A source linked to an actual run cannot be removed.";
        var before = SourceSnapshot(source);
        dbContext.RunProjectionSources.Remove(source);
        RecalculateTotals(projection, sourceId);
        Touch(projection, userId);
        await AddAuditAsync("RemoveSource", projection, userId, before, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> MarkReadyAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.Id, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        if (projection.Sources.Count == 0) return "Add at least one source before marking the projection Ready.";
        if (projection.Sources.Any(x => x.Commodity == "Unknown")) return "Resolve every source commodity before marking the projection Ready.";
        if (projection.Sources.Any(x => x.SizeResults.Count == 0)) return "Every source needs usable calculated size data before the projection can be Ready.";
        if (projection.Sources.Any(x => x.ExpectedPackoutPercent is null))
        {
            await AddAuditAsync(
                "ReadyBlockedMissingPackout",
                projection,
                userId,
                new { projection.Status },
                new { MissingSourceIds = projection.Sources.Where(x => x.ExpectedPackoutPercent is null).Select(x => x.Id) },
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return "Expected Packout % is required for every source before marking the projection Ready.";
        }
        var before = projection.Status;
        projection.Status = RunProjectionStatuses.Ready;
        Touch(projection, userId);
        await AddAuditAsync("MarkReady", projection, userId, new { Status = before }, new { projection.Status }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> CancelAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken)) return "Bins Run Edit access is required to cancel projections.";
        if (string.IsNullOrWhiteSpace(form.Reason)) return "Cancellation reason is required.";
        var projection = await LoadProjectionAsync(form.Id, cancellationToken);
        if (projection is null) return "Projection was not found.";
        if (projection.ConcurrencyVersion != form.ConcurrencyVersion) return ConflictMessage;
        if (!RunProjectionStatuses.Editable.Contains(projection.Status, StringComparer.OrdinalIgnoreCase))
        {
            return $"A {projection.Status} projection cannot be cancelled.";
        }
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var before = projection.Status;
        projection.Status = RunProjectionStatuses.Cancelled;
        projection.CancelReason = form.Reason.Trim();
        projection.CancelledAt = businessTime.UtcNow;
        projection.CancelledByUserId = userId;
        Touch(projection, userId);
        await AddAuditAsync("Cancel", projection, userId, new { Status = before }, new { projection.Status, projection.CancelReason }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(long? Id, string? Error)> DuplicateAsync(
        RunProjectionDuplicateForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Bins Run Edit access is required to duplicate projections.");
        }

        var sourceProjection = await LoadProjectionAsync(form.Id, cancellationToken);
        if (sourceProjection is null) return (null, "Projection was not found.");
        var cloneName = string.IsNullOrWhiteSpace(form.Name)
            ? $"{sourceProjection.Name[..Math.Min(sourceProjection.Name.Length, 95)]} copy"
            : form.Name.Trim();
        if (cloneName.Length > 100) return (null, "Projection name must be 100 characters or fewer.");
        var now = businessTime.UtcNow;
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var clone = new RunProjection
        {
            PlannedRunDate = form.PlannedRunDate,
            Name = cloneName,
            Status = RunProjectionStatuses.Draft,
            CropYear = sourceProjection.CropYear,
            ApplePoundsPerBin = sourceProjection.ApplePoundsPerBin,
            PearPoundsPerBin = sourceProjection.PearPoundsPerBin,
            StandardBoxWeightPounds = sourceProjection.StandardBoxWeightPounds,
            TotalPlannedBins = sourceProjection.TotalPlannedBins,
            TotalProjectedPounds = sourceProjection.TotalProjectedPounds,
            TotalProjectedBoxes = sourceProjection.TotalProjectedBoxes,
            TotalRoundedProjectedBoxes = sourceProjection.TotalRoundedProjectedBoxes,
            TotalPackedProjectedPounds = sourceProjection.TotalPackedProjectedPounds,
            TotalPackedProjectedBoxes = sourceProjection.TotalPackedProjectedBoxes,
            TotalRoundedPackedProjectedBoxes = sourceProjection.TotalRoundedPackedProjectedBoxes,
            TotalCullProjectedPounds = sourceProjection.TotalCullProjectedPounds,
            TotalCullProjectedBoxes = sourceProjection.TotalCullProjectedBoxes,
            TotalRoundedCullProjectedBoxes = sourceProjection.TotalRoundedCullProjectedBoxes,
            ExpiresAt = now.AddDays((await LoadSettingsAsync(cancellationToken)).DraftExpirationDays),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        foreach (var source in sourceProjection.Sources.OrderBy(x => x.SortOrder))
        {
            clone.Sources.Add(CloneSource(source, now));
        }

        dbContext.RunProjections.Add(clone);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync("Duplicate", clone, userId, new { SourceProjectionId = sourceProjection.Id }, ProjectionSnapshot(clone), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (clone.Id, null);
    }

    private async Task<RunProjectionDetailViewModel?> GetDetailAsync(long id, bool canEdit, CancellationToken cancellationToken)
    {
        var projection = await LoadProjectionAsync(id, cancellationToken);
        if (projection is null) return null;
        var sourceModels = new List<RunProjectionSourceViewModel>();
        foreach (var source in projection.Sources.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            var choices = await GetQcChoicesAsync(source, projection.CropYear, cancellationToken);
            sourceModels.Add(new RunProjectionSourceViewModel
            {
                Id = source.Id,
                SourceType = source.SourceType,
                InventoryKey = source.InventoryKey,
                WarehouseId = source.WarehouseId,
                RoomId = source.RoomId,
                SourceLabel = source.SourceLabelSnapshot,
                Facility = source.FacilitySnapshot,
                Room = source.RoomSnapshot,
                Lot = source.LotSnapshot,
                Orchard = source.OrchardSnapshot,
                Grower = source.GrowerSnapshot,
                GrowerNumber = source.GrowerNumberSnapshot,
                Block = source.BlockSnapshot,
                Variety = source.VarietySnapshot,
                Commodity = source.Commodity,
                PlannedBins = source.PlannedBins,
                AvailableBinsSnapshot = source.AvailableBinsSnapshot,
                AvailabilityOverrideAcknowledged = source.AvailabilityOverrideAcknowledged,
                SortOrder = source.SortOrder,
                Notes = source.Notes,
                SelectedQcSourceType = source.SelectedQcSourceType,
                SelectedQcSampleId = source.SelectedQcSampleId,
                QcBasis = QcBasis(source),
                QcSampleDate = source.QcSampleDateSnapshot,
                QcSampleType = source.QcSampleTypeSnapshot,
                QcSampleStatus = source.QcSampleStatusSnapshot,
                QcSampleId = source.SelectedQcSampleId,
                QcFruitCount = source.QcFruitCountSnapshot,
                SizeBasisFruitCount = source.SizeBasisFruitCount,
                GradeBasisFruitCount = source.GradeBasisFruitCount,
                JointSizeGradeBasisFruitCount = source.JointSizeGradeBasisFruitCount,
                AverageWeightGrams = source.AverageWeightGramsSnapshot,
                AveragePressureLbs = source.AveragePressureLbsSnapshot,
                GradeSummary = source.GradeSummarySnapshot,
                DefectSummary = source.DefectSummarySnapshot,
                PoundsPerBin = source.PoundsPerBinUsed,
                ProjectedPounds = source.ProjectedPounds,
                ProjectedBoxes = source.ProjectedBoxes,
                RoundedProjectedBoxes = source.RoundedProjectedBoxes,
                ExpectedPackoutPercent = source.ExpectedPackoutPercent,
                ExpectedCullPercent = source.ExpectedCullPercent,
                PackedProjectedPounds = source.PackedProjectedPounds,
                PackedProjectedBoxes = source.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = source.RoundedPackedProjectedBoxes,
                CullProjectedPounds = source.CullProjectedPounds,
                CullProjectedBoxes = source.CullProjectedBoxes,
                RoundedCullProjectedBoxes = source.RoundedCullProjectedBoxes,
                CalculationVersion = source.CalculationVersion,
                Warning = source.ProjectionWarning,
                ActualBinsRunEntryId = source.ActualBinsRunEntryId,
                SizeResults = source.SizeResults.OrderBy(x => x.SizeCategory)
                    .Select(x => new RunProjectionSizeResultViewModel(
                        x.Commodity, x.SizeCategory, x.SampleCount, x.Percentage,
                        x.UnroundedProjectedBoxes, x.RoundedProjectedBoxes,
                        x.PackedProjectedBoxes, x.RoundedPackedProjectedBoxes,
                        x.CullProjectedBoxes, x.RoundedCullProjectedBoxes))
                    .ToList(),
                GradeResults = source.GradeResults.OrderBy(x => x.GradeCode)
                    .Select(x => new RunProjectionGradeResultViewModel(
                        x.GradeCode, x.SampleCount, x.Percentage,
                        x.GrossProjectedBoxes, x.RoundedGrossProjectedBoxes,
                        x.PackedProjectedBoxes, x.RoundedPackedProjectedBoxes,
                        x.CullProjectedBoxes, x.RoundedCullProjectedBoxes))
                    .ToList(),
                QcChoices = choices
            });
        }

        return new RunProjectionDetailViewModel
        {
            Id = projection.Id,
            PlannedRunDate = projection.PlannedRunDate,
            Name = projection.Name,
            Status = projection.Status,
            CropYear = projection.CropYear,
            ApplePoundsPerBin = projection.ApplePoundsPerBin,
            PearPoundsPerBin = projection.PearPoundsPerBin,
            StandardBoxWeightPounds = projection.StandardBoxWeightPounds,
            TotalPlannedBins = projection.TotalPlannedBins,
            TotalProjectedPounds = projection.TotalProjectedPounds,
            TotalProjectedBoxes = projection.TotalProjectedBoxes,
            TotalRoundedProjectedBoxes = projection.TotalRoundedProjectedBoxes,
            TotalPackedProjectedPounds = projection.TotalPackedProjectedPounds,
            TotalPackedProjectedBoxes = projection.TotalPackedProjectedBoxes,
            TotalRoundedPackedProjectedBoxes = projection.TotalRoundedPackedProjectedBoxes,
            TotalCullProjectedPounds = projection.TotalCullProjectedPounds,
            TotalCullProjectedBoxes = projection.TotalCullProjectedBoxes,
            TotalRoundedCullProjectedBoxes = projection.TotalRoundedCullProjectedBoxes,
            ConcurrencyVersion = projection.ConcurrencyVersion,
            Creator = projection.CreatedByUser?.DisplayName ?? "",
            UpdatedAt = projection.UpdatedAt,
            SourceCount = projection.Sources.Count,
            ConvertedSourceCount = projection.Sources.Count(x => x.ActualBinsRunEntryId != null),
            CancelReason = projection.CancelReason,
            Sources = sourceModels,
            CombinedSizes = sourceModels
                .SelectMany(x => x.SizeResults)
                .GroupBy(x => new { x.Commodity, x.Size })
                .OrderBy(x => x.Key.Commodity)
                .ThenBy(x => x.Key.Size)
                .Select(x => new RunProjectionCombinedSizeViewModel(
                    x.Key.Commodity,
                    x.Key.Size,
                    x.Sum(y => y.UnroundedBoxes),
                    x.Sum(y => y.RoundedBoxes),
                    x.Sum(y => y.PackedBoxes),
                    x.Sum(y => y.RoundedPackedBoxes),
                    x.Sum(y => y.CullBoxes),
                    x.Sum(y => y.RoundedCullBoxes)))
                .ToList(),
            CombinedGrades = sourceModels
                .SelectMany(x => x.GradeResults)
                .GroupBy(x => x.Grade, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .Select(x => new RunProjectionCombinedGradeViewModel(
                    x.Key,
                    x.Sum(y => y.GrossBoxes),
                    x.Sum(y => y.RoundedGrossBoxes),
                    x.Sum(y => y.PackedBoxes),
                    x.Sum(y => y.RoundedPackedBoxes),
                    x.Sum(y => y.CullBoxes),
                    x.Sum(y => y.RoundedCullBoxes)))
                .ToList(),
            CanEditRecord = canEdit && RunProjectionStatuses.Editable.Contains(projection.Status)
        };
    }

    private async Task<(RunProjectionSource? Entity, string? Error)> ResolveSourceAsync(
        RunProjection projection,
        string sourceKey,
        string selectedQcSource,
        int plannedBins,
        decimal? expectedPackoutPercent,
        int minimumDistributionFruit,
        bool availabilityOverride,
        CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        if (sourceKey.StartsWith("F:", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(sourceKey.AsSpan(2), out var fieldSampleId))
        {
            var sample = await UsableFieldSamples(projection.CropYear)
                .Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
                .Include(x => x.FieldSampleFruitProfile)
                .SingleOrDefaultAsync(x => x.Id == fieldSampleId, cancellationToken);
            if (sample is null) return (null, "The selected confirmed Field Sample is no longer available.");
            var source = new RunProjectionSource
            {
                SourceType = RunProjectionSourceTypes.FieldSample,
                CanonicalOrchardBlockId = sample.CanonicalOrchardBlockId,
                FruitProfileId = sample.FieldSampleFruitProfileId!.Value,
                FieldSampleId = sample.Id,
                PlannedBins = plannedBins,
                AvailableBinsSnapshot = null,
                AvailabilityOverrideAcknowledged = false,
                SelectedQcSourceType = RunProjectionQcSourceTypes.FieldSample,
                ExpectedPackoutPercent = expectedPackoutPercent,
                SourceLabelSnapshot = $"Planning-only block — {sample.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName} / {sample.CanonicalOrchardBlock.CanonicalBlockName}",
                OrchardSnapshot = sample.CanonicalOrchardBlock.CanonicalOrchard.OrchardName,
                GrowerSnapshot = sample.FieldSampleGrowerName,
                GrowerNumberSnapshot = sample.FieldSampleGrowerNumber,
                BlockSnapshot = sample.CanonicalOrchardBlock.CanonicalBlockName,
                VarietySnapshot = sample.FieldSampleFruitProfile!.Name,
                Commodity = RunProjectionCalculationService.NormalizeCommodity(sample.FieldSampleFruitProfile.FruitType),
                CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion,
                CreatedAt = now,
                UpdatedAt = now
            };
            var qc = await ResolveQcSampleAsync(projection.CropYear, source, selectedQcSource, cancellationToken, sample);
            if (qc.Error is not null) return (null, qc.Error);
            ApplyQcAndCalculation(projection, source, qc.Sample, qc.SourceType, minimumDistributionFruit);
            return (source, null);
        }

        var inventory = await binsRunService.GetPlanningInventoryAsync(sourceKey, cancellationToken);
        if (inventory is null) return (null, "The selected inventory source is no longer available.");
        if (plannedBins > inventory.CurrentBins && !availabilityOverride)
        {
            return (null, $"Planned bins exceed the current available quantity of {inventory.CurrentBins}. Confirm the planning override to continue.");
        }

        var inventorySource = new RunProjectionSource
        {
            SourceType = RunProjectionSourceTypes.Inventory,
            InventoryKey = inventory.InventoryKey,
            ReceiptId = inventory.ReceiptId,
            SourceInventoryAdjustmentId = inventory.InventoryAdjustmentId,
            WarehouseId = inventory.WarehouseId,
            RoomId = inventory.RoomId,
            CanonicalOrchardBlockId = inventory.CanonicalOrchardBlockId,
            FruitProfileId = inventory.FruitProfileId ?? 0,
            PlannedBins = plannedBins,
            AvailableBinsSnapshot = inventory.CurrentBins,
            AvailabilityOverrideAcknowledged = availabilityOverride,
            SelectedQcSourceType = RunProjectionQcSourceTypes.Automatic,
            ExpectedPackoutPercent = expectedPackoutPercent,
            SourceLabelSnapshot = $"{inventory.Facility} / {inventory.Room} — {inventory.Grower}"
                + (string.IsNullOrWhiteSpace(inventory.GrowerNumber) ? "" : $" — grower # {inventory.GrowerNumber}")
                + (string.IsNullOrWhiteSpace(inventory.ReceiptReference) ? "" : $" — receipt {inventory.ReceiptReference}")
                + $" — lot {inventory.Lot}",
            FacilitySnapshot = inventory.Facility,
            RoomSnapshot = inventory.Room,
            LotSnapshot = inventory.Lot,
            GrowerSnapshot = inventory.Grower,
            GrowerNumberSnapshot = inventory.GrowerNumber,
            VarietySnapshot = inventory.Variety,
            Commodity = RunProjectionCalculationService.NormalizeCommodity(inventory.FruitType),
            CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (inventorySource.FruitProfileId <= 0)
        {
            return (null, "The inventory source has no configured fruit profile. Configure its variety before adding it to a projection.");
        }

        if (inventory.CanonicalOrchardBlockId is int blockId)
        {
            var identity = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
                .Where(x => x.Id == blockId)
                .Select(x => new
                {
                    Orchard = x.CanonicalOrchard.OrchardName,
                    Block = x.CanonicalBlockName,
                    Grower = x.CanonicalGrower == null ? null : x.CanonicalGrower.DisplayName
                })
                .SingleOrDefaultAsync(cancellationToken);
            inventorySource.OrchardSnapshot = identity?.Orchard;
            inventorySource.BlockSnapshot = identity?.Block;
            inventorySource.GrowerSnapshot = identity?.Grower ?? inventorySource.GrowerSnapshot;
        }

        var inventoryQc = await ResolveQcSampleAsync(projection.CropYear, inventorySource, selectedQcSource, cancellationToken);
        if (inventoryQc.Error is not null) return (null, inventoryQc.Error);
        ApplyQcAndCalculation(projection, inventorySource, inventoryQc.Sample, inventoryQc.SourceType, minimumDistributionFruit);
        return (inventorySource, null);
    }

    private async Task<(QcSample? Sample, string SourceType, string? Error)> ResolveQcSampleAsync(
        int cropYear,
        RunProjectionSource source,
        string? requested,
        CancellationToken cancellationToken,
        QcSample? knownFieldSample = null)
    {
        var choice = string.IsNullOrWhiteSpace(requested) ? RunProjectionQcSourceTypes.Automatic : requested.Trim();
        if (choice.Equals(RunProjectionQcSourceTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            return (null, RunProjectionQcSourceTypes.None, null);
        }

        if (choice.StartsWith("ReceiptQc:", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(choice.AsSpan("ReceiptQc:".Length), out var receiptQcId))
        {
            var sample = await LoadUsableSampleAsync(receiptQcId, cropYear, cancellationToken);
            return sample is null || sample.ReceiptId != source.ReceiptId
                ? (null, "", "The selected receipt QC sample is not valid for this source.")
                : (sample, RunProjectionQcSourceTypes.ReceiptQc, null);
        }

        if (choice.StartsWith("FieldSample:", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(choice.AsSpan("FieldSample:".Length), out var fieldId))
        {
            var sample = knownFieldSample?.Id == fieldId ? knownFieldSample : await LoadUsableSampleAsync(fieldId, cropYear, cancellationToken);
            return sample is null
                   || sample.ReceiptId is not null
                   || sample.CanonicalOrchardBlockId != source.CanonicalOrchardBlockId
                   || sample.FieldSampleFruitProfileId != source.FruitProfileId
                   || IsUnconfirmedFieldSample(sample)
                ? (null, "", "The selected Field Sample is not a confirmed same-block, same-variety source.")
                : (sample, RunProjectionQcSourceTypes.FieldSample, null);
        }

        if (!choice.Equals(RunProjectionQcSourceTypes.Automatic, StringComparison.OrdinalIgnoreCase)
            && !choice.Equals(RunProjectionQcSourceTypes.FieldSample, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "", "Select a valid QC data source.");
        }
        var useAutomaticPriority = choice.Equals(RunProjectionQcSourceTypes.Automatic, StringComparison.OrdinalIgnoreCase);

        if (source.SourceType == RunProjectionSourceTypes.FieldSample && knownFieldSample is not null)
        {
            return (await LoadUsableSampleAsync(knownFieldSample.Id, cropYear, cancellationToken), RunProjectionQcSourceTypes.FieldSample, null);
        }

        if (useAutomaticPriority && source.ReceiptId is long receiptId)
        {
            var receiptQc = await UsableQcSamples(cropYear)
                .Where(x => x.ReceiptId == receiptId)
                .Where(x => x.FruitReadings.Any(row => row.SizeCategory != null)
                    && x.FruitReadings.Any(row => row.GradeId != null))
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (receiptQc is not null) return (await LoadUsableSampleAsync(receiptQc.Id, cropYear, cancellationToken), RunProjectionQcSourceTypes.ReceiptQc, null);
        }

        if (source.CanonicalOrchardBlockId is int blockId)
        {
            var field = await UsableFieldSamples(cropYear)
                .Where(x => x.CanonicalOrchardBlockId == blockId && x.FieldSampleFruitProfileId == source.FruitProfileId)
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (field is not null) return (await LoadUsableSampleAsync(field.Id, cropYear, cancellationToken), RunProjectionQcSourceTypes.FieldSample, null);
        }

        return (null, RunProjectionQcSourceTypes.None, null);
    }

    private async Task<QcSample?> LoadUsableSampleAsync(long sampleId, int cropYear, CancellationToken cancellationToken)
    {
        var fieldWindow = FieldSampleCropWindow(cropYear);
        return await dbContext.QcSamples
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
            .Include(x => x.SampleType)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == sampleId
                && !x.IsDeleted
                && ((x.ReceiptId != null
                        && x.Receipt!.CropYear == cropYear
                        && (x.Status == "Complete"
                            || x.Status == "Ready to Send"
                            || x.Status == "Sent"
                            || x.Status == "Needs Resend")
                        && x.SampleType.IsActive
                        && x.SampleType.Name != FieldSampleTypeName)
                    || (x.ReceiptId == null
                        && x.SampleType.Name == FieldSampleTypeName
                        && x.SampleTakenAt >= fieldWindow.Start
                        && x.SampleTakenAt < fieldWindow.End
                        && x.CanonicalOrchardBlockId != null
                        && x.FieldSampleFruitProfileId != null
                        && x.FieldSampleBlockResolution != "Suggested"))
                && x.FruitReadings.Any(row => row.SizeCategory != null || row.GradeId != null), cancellationToken);
    }

    private void ApplyQcAndCalculation(
        RunProjection projection,
        RunProjectionSource source,
        QcSample? sample,
        string selectedSourceType,
        int minimumDistributionFruit)
    {
        source.SelectedQcSourceType = selectedSourceType;
        source.SelectedQcSampleId = sample?.Id;
        source.SizeResults.Clear();
        source.GradeResults.Clear();
        var readings = sample?.FruitReadings.Where(HasEnteredFruitData).ToList() ?? [];
        var calculation = RunProjectionCalculationService.Calculate(
            source.Commodity,
            source.PlannedBins,
            projection.ApplePoundsPerBin,
            projection.PearPoundsPerBin,
            projection.StandardBoxWeightPounds,
            source.ExpectedPackoutPercent,
            readings.Where(x => x.SizeCategory != null).Select(x => new RunProjectionSizeObservation(x.SizeCategory!.Value)),
            readings.Where(x => x.Grade != null).Select(x => new RunProjectionGradeObservation(x.Grade!.Code)),
            readings.Count(x => x.SizeCategory != null && x.Grade != null),
            minimumDistributionFruit);

        source.PoundsPerBinUsed = calculation.PoundsPerBin;
        source.ProjectedPounds = calculation.ProjectedPounds;
        source.ProjectedBoxes = calculation.ProjectedBoxes;
        source.RoundedProjectedBoxes = calculation.RoundedProjectedBoxes;
        source.ExpectedCullPercent = calculation.ExpectedCullPercent;
        source.PackedProjectedPounds = calculation.PackedProjectedPounds;
        source.PackedProjectedBoxes = calculation.PackedProjectedBoxes;
        source.RoundedPackedProjectedBoxes = calculation.RoundedPackedProjectedBoxes;
        source.CullProjectedPounds = calculation.CullProjectedPounds;
        source.CullProjectedBoxes = calculation.CullProjectedBoxes;
        source.RoundedCullProjectedBoxes = calculation.RoundedCullProjectedBoxes;
        source.QcSampleDateSnapshot = sample?.SampleTakenAt;
        source.QcSampleTypeSnapshot = sample?.SampleType.Name;
        source.QcSampleStatusSnapshot = sample?.Status;
        source.QcFruitCountSnapshot = readings.Count;
        source.SizeBasisFruitCount = calculation.SizeBasisFruitCount;
        source.GradeBasisFruitCount = calculation.GradeBasisFruitCount;
        source.JointSizeGradeBasisFruitCount = calculation.JointSizeGradeBasisFruitCount;
        source.AverageWeightGramsSnapshot = Average(readings.Where(x => x.WeightGrams != null).Select(x => x.WeightGrams!.Value));
        source.AveragePressureLbsSnapshot = Average(PressureCalculationService.ValidSideReadings(readings.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs))));
        source.GradeSummarySnapshot = Distribution(readings.Where(x => x.Grade != null).Select(x => x.Grade!.Code));
        var inspected = readings.Where(x => x.DefectsInspected).ToList();
        source.DefectSummarySnapshot = inspected.Count == 0
            ? "Not inspected"
            : $"{inspected.Count(x => x.Defects.Count > 0)} of {inspected.Count} inspected fruit affected; "
              + Distribution(inspected.SelectMany(x => x.Defects).Select(x => x.DefectType.Name));
        source.ProjectionWarning = calculation.Warning;
        source.CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion;
        foreach (var allocation in calculation.SizeAllocations)
        {
            source.SizeResults.Add(new RunProjectionSizeResult
            {
                Commodity = allocation.Commodity,
                SizeCategory = allocation.SizeCategory,
                SampleCount = allocation.SampleCount,
                Percentage = allocation.Percentage,
                UnroundedProjectedBoxes = allocation.UnroundedProjectedBoxes,
                RoundedProjectedBoxes = allocation.RoundedProjectedBoxes,
                PackedProjectedBoxes = allocation.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = allocation.RoundedPackedProjectedBoxes,
                CullProjectedBoxes = allocation.CullProjectedBoxes,
                RoundedCullProjectedBoxes = allocation.RoundedCullProjectedBoxes
            });
        }
        foreach (var allocation in calculation.GradeAllocations)
        {
            source.GradeResults.Add(new RunProjectionGradeResult
            {
                GradeCode = allocation.Key,
                SampleCount = allocation.SampleCount,
                Percentage = allocation.Percentage,
                GrossProjectedBoxes = allocation.GrossBoxes,
                RoundedGrossProjectedBoxes = allocation.RoundedGrossBoxes,
                PackedProjectedBoxes = allocation.PackedBoxes,
                RoundedPackedProjectedBoxes = allocation.RoundedPackedBoxes,
                CullProjectedBoxes = allocation.CullBoxes,
                RoundedCullProjectedBoxes = allocation.RoundedCullBoxes
            });
        }
    }

    private async Task<IReadOnlyList<RunProjectionQcChoiceViewModel>> GetQcChoicesAsync(
        RunProjectionSource source,
        int cropYear,
        CancellationToken cancellationToken)
    {
        var choices = new List<RunProjectionQcChoiceViewModel>
        {
            new(RunProjectionQcSourceTypes.Automatic, "Automatic — latest completed receipt sample with size and grade, then confirmed Field Sample", null, RunProjectionQcSourceTypes.Automatic, null, null, null, 0, null, null, false, false, false, source.SelectedQcSourceType == RunProjectionQcSourceTypes.Automatic),
            new(RunProjectionQcSourceTypes.None, "No usable QC data", null, RunProjectionQcSourceTypes.None, null, null, null, 0, null, null, false, false, false, source.SelectedQcSourceType == RunProjectionQcSourceTypes.None)
        };
        if (source.ReceiptId is long receiptId)
        {
            var receiptSamples = await UsableQcSamples(cropYear)
                .Where(x => x.ReceiptId == receiptId)
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .Include(x => x.SampleType)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Defects)
                .Take(25)
                .ToListAsync(cancellationToken);
            choices.AddRange(receiptSamples.Select(x => QcChoice(x, RunProjectionQcSourceTypes.ReceiptQc, source.SelectedQcSampleId == x.Id)));
        }

        if (source.CanonicalOrchardBlockId is int blockId)
        {
            var fieldSamples = await UsableFieldSamples(cropYear)
                .Where(x => x.CanonicalOrchardBlockId == blockId && x.FieldSampleFruitProfileId == source.FruitProfileId)
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .Include(x => x.SampleType)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Defects)
                .Take(25)
                .ToListAsync(cancellationToken);
            choices.AddRange(fieldSamples.Select(x => QcChoice(x, RunProjectionQcSourceTypes.FieldSample, source.SelectedQcSampleId == x.Id)));
        }

        if (source.SelectedQcSampleId is long savedSampleId && choices.All(x => !x.IsSelected))
        {
            var savedSourceType = source.SelectedQcSourceType == RunProjectionQcSourceTypes.FieldSample
                ? RunProjectionQcSourceTypes.FieldSample
                : RunProjectionQcSourceTypes.ReceiptQc;
            choices.Insert(1, new RunProjectionQcChoiceViewModel(
                $"{savedSourceType}:{savedSampleId}",
                $"Saved snapshot — {source.QcSampleTypeSnapshot ?? savedSourceType} #{savedSampleId} — current sample is unavailable or no longer eligible",
                savedSampleId,
                savedSourceType,
                source.QcSampleDateSnapshot,
                source.QcSampleTypeSnapshot,
                source.QcSampleStatusSnapshot,
                source.QcFruitCountSnapshot ?? 0,
                source.AverageWeightGramsSnapshot,
                source.AveragePressureLbsSnapshot,
                source.SizeBasisFruitCount > 0,
                source.GradeBasisFruitCount > 0,
                !string.IsNullOrWhiteSpace(source.DefectSummarySnapshot),
                true));
        }
        return choices;
    }

    private RunProjectionQcChoiceViewModel QcChoice(QcSample sample, string sourceType, bool selected)
    {
        var rows = sample.FruitReadings.Where(HasEnteredFruitData).ToList();
        var averageWeight = Average(rows.Where(x => x.WeightGrams != null).Select(x => x.WeightGrams!.Value));
        var averagePressure = Average(PressureCalculationService.ValidSideReadings(rows.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs))));
        var hasSize = rows.Any(x => x.SizeCategory != null);
        var hasGrade = rows.Any(x => x.GradeId != null);
        var hasDefects = rows.Any(x => x.DefectsInspected);
        var label = $"{sample.SampleType.Name} #{sample.Id} — {businessTime.FormatPacific(sample.SampleTakenAt, "MMM d, yyyy h:mm tt")} — {sample.Status} — {rows.Count} fruit"
            + $" — avg wt {(averageWeight?.ToString("0.##") ?? "n/a")} g"
            + $" — avg pressure {(averagePressure?.ToString("0.##") ?? "n/a")} lb"
            + $" — size {(hasSize ? "yes" : "no")}, grade {(hasGrade ? "yes" : "no")}, defects {(hasDefects ? "yes" : "no")}";
        return new RunProjectionQcChoiceViewModel(
            $"{sourceType}:{sample.Id}",
            label,
            sample.Id,
            sourceType,
            sample.SampleTakenAt,
            sample.SampleType.Name,
            sample.Status,
            rows.Count,
            averageWeight,
            averagePressure,
            hasSize,
            hasGrade,
            hasDefects,
            selected);
    }

    private IQueryable<QcSample> UsableQcSamples(int cropYear) =>
        dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.ReceiptId != null
                && x.Receipt!.CropYear == cropYear
                && (x.Status == "Complete"
                    || x.Status == "Ready to Send"
                    || x.Status == "Sent"
                    || x.Status == "Needs Resend")
                && x.SampleType.IsActive
                && x.SampleType.Name != FieldSampleTypeName
                && x.FruitReadings.Any(row => row.SizeCategory != null || row.GradeId != null));

    private IQueryable<QcSample> UsableFieldSamples(int cropYear)
    {
        var window = FieldSampleCropWindow(cropYear);
        return dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.ReceiptId == null
                && x.SampleType.Name == FieldSampleTypeName
                && x.SampleTakenAt >= window.Start
                && x.SampleTakenAt < window.End
                && x.CanonicalOrchardBlockId != null
                && x.FieldSampleFruitProfileId != null
                && x.FieldSampleBlockResolution != "Suggested"
                && x.FruitReadings.Any(row => row.SizeCategory != null || row.GradeId != null));
    }

    private UtcDayRange FieldSampleCropWindow(int cropYear)
    {
        // Existing crop-year rules allow early-season samples beginning in May and
        // late-season samples through the following December.
        var start = businessTime.UtcRangeForPacificDate(new DateOnly(cropYear, 5, 1)).Start;
        var end = businessTime.UtcRangeForPacificDate(new DateOnly(cropYear + 2, 1, 1)).Start;
        return new UtcDayRange(start, end);
    }

    private static bool IsUnconfirmedFieldSample(QcSample sample) =>
        sample.CanonicalOrchardBlockId is null
        || string.Equals(sample.FieldSampleBlockResolution, "Suggested", StringComparison.OrdinalIgnoreCase);

    private async Task<RunProjection?> LoadProjectionAsync(long id, CancellationToken cancellationToken) =>
        await dbContext.RunProjections
            .Include(x => x.CreatedByUser)
            .Include(x => x.Sources).ThenInclude(x => x.SizeResults)
            .Include(x => x.Sources).ThenInclude(x => x.GradeResults)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private async Task<(RunProjection? Projection, string? Error, int? UserId)> LoadForEditAsync(
        long id,
        long concurrencyVersion,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Bins Run Edit access is required to change projections.", null);
        }

        var projection = await LoadProjectionAsync(id, cancellationToken);
        if (projection is null) return (null, "Projection was not found.", null);
        if (!RunProjectionStatuses.Editable.Contains(projection.Status)) return (null, $"A {projection.Status} projection cannot be edited.", null);
        if (projection.ConcurrencyVersion != concurrencyVersion) return (null, ConflictMessage, null);
        return (projection, null, await CurrentUserIdAsync(user, cancellationToken));
    }

    private static bool ChoiceMatchesSnapshot(RunProjectionSource source, string choice)
    {
        if (choice.Equals(RunProjectionQcSourceTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            return source.SelectedQcSampleId is null;
        }
        if (choice.Equals(RunProjectionQcSourceTypes.Automatic, StringComparison.OrdinalIgnoreCase))
        {
            return source.SelectedQcSourceType == RunProjectionQcSourceTypes.Automatic;
        }
        var separator = choice.IndexOf(':');
        return separator > 0
            && long.TryParse(choice[(separator + 1)..], out var sampleId)
            && source.SelectedQcSampleId == sampleId;
    }

    private static void RecalculateFromSnapshot(RunProjection projection, RunProjectionSource source, int minimumDistributionFruit)
    {
        var sizeObservations = source.SizeResults
            .SelectMany(x => Enumerable.Repeat(new RunProjectionSizeObservation(x.SizeCategory), x.SampleCount))
            .ToList();
        var gradeObservations = source.GradeResults
            .SelectMany(x => Enumerable.Repeat(new RunProjectionGradeObservation(x.GradeCode), x.SampleCount))
            .ToList();
        var calculation = RunProjectionCalculationService.Calculate(
            source.Commodity,
            source.PlannedBins,
            projection.ApplePoundsPerBin,
            projection.PearPoundsPerBin,
            projection.StandardBoxWeightPounds,
            source.ExpectedPackoutPercent,
            sizeObservations,
            gradeObservations,
            source.JointSizeGradeBasisFruitCount,
            minimumDistributionFruit);
        source.PoundsPerBinUsed = calculation.PoundsPerBin;
        source.ProjectedPounds = calculation.ProjectedPounds;
        source.ProjectedBoxes = calculation.ProjectedBoxes;
        source.RoundedProjectedBoxes = calculation.RoundedProjectedBoxes;
        source.ExpectedCullPercent = calculation.ExpectedCullPercent;
        source.PackedProjectedPounds = calculation.PackedProjectedPounds;
        source.PackedProjectedBoxes = calculation.PackedProjectedBoxes;
        source.RoundedPackedProjectedBoxes = calculation.RoundedPackedProjectedBoxes;
        source.CullProjectedPounds = calculation.CullProjectedPounds;
        source.CullProjectedBoxes = calculation.CullProjectedBoxes;
        source.RoundedCullProjectedBoxes = calculation.RoundedCullProjectedBoxes;
        source.ProjectionWarning = calculation.Warning;
        source.CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion;
        foreach (var result in source.SizeResults)
        {
            var allocation = calculation.SizeAllocations.Single(x => x.SizeCategory == result.SizeCategory);
            result.Percentage = allocation.Percentage;
            result.UnroundedProjectedBoxes = allocation.UnroundedProjectedBoxes;
            result.RoundedProjectedBoxes = allocation.RoundedProjectedBoxes;
            result.PackedProjectedBoxes = allocation.PackedProjectedBoxes;
            result.RoundedPackedProjectedBoxes = allocation.RoundedPackedProjectedBoxes;
            result.CullProjectedBoxes = allocation.CullProjectedBoxes;
            result.RoundedCullProjectedBoxes = allocation.RoundedCullProjectedBoxes;
        }
        foreach (var result in source.GradeResults)
        {
            var allocation = calculation.GradeAllocations.Single(x => x.Key.Equals(result.GradeCode, StringComparison.OrdinalIgnoreCase));
            result.Percentage = allocation.Percentage;
            result.GrossProjectedBoxes = allocation.GrossBoxes;
            result.RoundedGrossProjectedBoxes = allocation.RoundedGrossBoxes;
            result.PackedProjectedBoxes = allocation.PackedBoxes;
            result.RoundedPackedProjectedBoxes = allocation.RoundedPackedBoxes;
            result.CullProjectedBoxes = allocation.CullBoxes;
            result.RoundedCullProjectedBoxes = allocation.RoundedCullBoxes;
        }
    }

    private void RecalculateTotals(RunProjection projection, long? excludingSourceId = null)
    {
        var sources = projection.Sources.Where(x => x.Id != excludingSourceId).ToList();
        projection.TotalPlannedBins = sources.Sum(x => x.PlannedBins);
        projection.TotalProjectedPounds = sources.Sum(x => x.ProjectedPounds);
        projection.TotalProjectedBoxes = sources.Sum(x => x.ProjectedBoxes);
        projection.TotalRoundedProjectedBoxes = sources.Sum(x => x.RoundedProjectedBoxes);
        projection.TotalPackedProjectedPounds = sources.Sum(x => x.PackedProjectedPounds);
        projection.TotalPackedProjectedBoxes = sources.Sum(x => x.PackedProjectedBoxes);
        projection.TotalRoundedPackedProjectedBoxes = sources.Sum(x => x.RoundedPackedProjectedBoxes);
        projection.TotalCullProjectedPounds = sources.Sum(x => x.CullProjectedPounds);
        projection.TotalCullProjectedBoxes = sources.Sum(x => x.CullProjectedBoxes);
        projection.TotalRoundedCullProjectedBoxes = sources.Sum(x => x.RoundedCullProjectedBoxes);
    }

    private void Touch(RunProjection projection, int? userId)
    {
        projection.ConcurrencyVersion++;
        projection.UpdatedAt = businessTime.UtcNow;
        projection.UpdatedByUserId = userId;
    }

    private async Task ExpireDraftsAsync(int expirationDays, CancellationToken cancellationToken)
    {
        var cutoff = businessTime.PacificDate(businessTime.UtcNow).AddDays(-expirationDays);
        var drafts = await dbContext.RunProjections
            .Where(x => x.Status == RunProjectionStatuses.Draft && x.PlannedRunDate < cutoff)
            .ToListAsync(cancellationToken);
        if (drafts.Count == 0) return;
        foreach (var projection in drafts)
        {
            projection.Status = RunProjectionStatuses.Expired;
            projection.UpdatedAt = businessTime.UtcNow;
            projection.ConcurrencyVersion++;
            await AddAuditAsync("Expire", projection, null, new { Status = RunProjectionStatuses.Draft }, new { projection.Status }, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProjectionSettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            RunProjectionSettings.ApplePoundsPerBinKey,
            RunProjectionSettings.PearPoundsPerBinKey,
            RunProjectionSettings.StandardBoxWeightKey,
            RunProjectionSettings.DraftExpirationDaysKey,
            RunProjectionSettings.VisibilityPastDaysKey,
            RunProjectionSettings.VisibilityFutureDaysKey,
            RunProjectionSettings.DefaultExpectedPackoutPercentKey,
            RunProjectionSettings.MinimumDistributionFruitKey
        };
        var values = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        return new ProjectionSettingsSnapshot(
            ReadDecimal(values, RunProjectionSettings.ApplePoundsPerBinKey, RunProjectionCalculationService.DefaultApplePoundsPerBin),
            ReadDecimal(values, RunProjectionSettings.PearPoundsPerBinKey, RunProjectionCalculationService.DefaultPearPoundsPerBin),
            ReadDecimal(values, RunProjectionSettings.StandardBoxWeightKey, RunProjectionCalculationService.DefaultStandardBoxWeightPounds),
            ReadInt(values, RunProjectionSettings.DraftExpirationDaysKey, RunProjectionSettings.DefaultDraftExpirationDays, 1, 365),
            ReadInt(values, RunProjectionSettings.VisibilityPastDaysKey, RunProjectionSettings.DefaultVisibilityPastDays, 1, 365),
            ReadInt(values, RunProjectionSettings.VisibilityFutureDaysKey, RunProjectionSettings.DefaultVisibilityFutureDays, 1, 365),
            ReadPercent(values, RunProjectionSettings.DefaultExpectedPackoutPercentKey, RunProjectionCalculationService.DefaultExpectedPackoutPercent),
            ReadInt(values, RunProjectionSettings.MinimumDistributionFruitKey, RunProjectionSettings.DefaultMinimumDistributionFruit, 1, 50));
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static decimal ReadPercent(IReadOnlyDictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) && parsed is >= 0 and <= 100 ? parsed : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback, int min, int max) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed >= min && parsed <= max ? parsed : fallback;

    private async Task RequireAsync(ClaimsPrincipal user, PageAccessLevel level, CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, level, cancellationToken))
        {
            throw new UnauthorizedAccessException($"Bins Run {level} access is required.");
        }
    }

    private Task<bool> CanAsync(ClaimsPrincipal user, PageAccessLevel level, CancellationToken cancellationToken) =>
        userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, level, cancellationToken);

    private async Task<int?> CurrentUserIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task AddAuditAsync(
        string action,
        RunProjection projection,
        int? userId,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(RunProjection),
            EntityKey = projection.Id.ToString(),
            UserId = userId,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after),
            SourceApplication = SourceApplication,
            CreatedAt = businessTime.UtcNow
        });
        await Task.CompletedTask;
    }

    private static object ProjectionSnapshot(RunProjection x) => new
    {
        x.Id,
        x.PlannedRunDate,
        x.Name,
        x.Status,
        x.CropYear,
        x.ApplePoundsPerBin,
        x.PearPoundsPerBin,
        x.StandardBoxWeightPounds,
        x.TotalPlannedBins,
        x.TotalProjectedPounds,
        x.TotalProjectedBoxes,
        x.TotalPackedProjectedPounds,
        x.TotalPackedProjectedBoxes,
        x.TotalCullProjectedPounds,
        x.TotalCullProjectedBoxes,
        x.ConcurrencyVersion
    };

    private static object SourceSnapshot(RunProjectionSource x) => new
    {
        x.Id,
        x.SourceType,
        x.InventoryKey,
        x.ReceiptId,
        x.CanonicalOrchardBlockId,
        x.FieldSampleId,
        x.SelectedQcSourceType,
        x.SelectedQcSampleId,
        x.PlannedBins,
        x.AvailableBinsSnapshot,
        x.AvailabilityOverrideAcknowledged,
        x.ProjectedPounds,
        x.ProjectedBoxes,
        x.RoundedProjectedBoxes,
        x.ExpectedPackoutPercent,
        x.ExpectedCullPercent,
        x.PackedProjectedPounds,
        x.PackedProjectedBoxes,
        x.CullProjectedPounds,
        x.CullProjectedBoxes,
        x.QcSampleTypeSnapshot,
        x.QcSampleDateSnapshot,
        x.SizeBasisFruitCount,
        x.GradeBasisFruitCount,
        x.CalculationVersion,
        x.ActualBinsRunEntryId
    };

    private static RunProjectionSource CloneSource(RunProjectionSource source, DateTimeOffset now)
    {
        var clone = new RunProjectionSource
        {
            SourceType = source.SourceType,
            InventoryKey = source.InventoryKey,
            ReceiptId = source.ReceiptId,
            SourceInventoryAdjustmentId = source.SourceInventoryAdjustmentId,
            WarehouseId = source.WarehouseId,
            RoomId = source.RoomId,
            CanonicalOrchardBlockId = source.CanonicalOrchardBlockId,
            FruitProfileId = source.FruitProfileId,
            FieldSampleId = source.FieldSampleId,
            SelectedQcSourceType = source.SelectedQcSourceType,
            SelectedQcSampleId = source.SelectedQcSampleId,
            PlannedBins = source.PlannedBins,
            AvailableBinsSnapshot = source.AvailableBinsSnapshot,
            AvailabilityOverrideAcknowledged = source.AvailabilityOverrideAcknowledged,
            SortOrder = source.SortOrder,
            Notes = source.Notes,
            Commodity = source.Commodity,
            PoundsPerBinUsed = source.PoundsPerBinUsed,
            ProjectedPounds = source.ProjectedPounds,
            ProjectedBoxes = source.ProjectedBoxes,
            RoundedProjectedBoxes = source.RoundedProjectedBoxes,
            ExpectedPackoutPercent = source.ExpectedPackoutPercent,
            ExpectedCullPercent = source.ExpectedCullPercent,
            PackedProjectedPounds = source.PackedProjectedPounds,
            PackedProjectedBoxes = source.PackedProjectedBoxes,
            RoundedPackedProjectedBoxes = source.RoundedPackedProjectedBoxes,
            CullProjectedPounds = source.CullProjectedPounds,
            CullProjectedBoxes = source.CullProjectedBoxes,
            RoundedCullProjectedBoxes = source.RoundedCullProjectedBoxes,
            SourceLabelSnapshot = source.SourceLabelSnapshot,
            FacilitySnapshot = source.FacilitySnapshot,
            RoomSnapshot = source.RoomSnapshot,
            LotSnapshot = source.LotSnapshot,
            OrchardSnapshot = source.OrchardSnapshot,
            GrowerSnapshot = source.GrowerSnapshot,
            GrowerNumberSnapshot = source.GrowerNumberSnapshot,
            BlockSnapshot = source.BlockSnapshot,
            VarietySnapshot = source.VarietySnapshot,
            QcSampleDateSnapshot = source.QcSampleDateSnapshot,
            QcSampleTypeSnapshot = source.QcSampleTypeSnapshot,
            QcSampleStatusSnapshot = source.QcSampleStatusSnapshot,
            QcFruitCountSnapshot = source.QcFruitCountSnapshot,
            SizeBasisFruitCount = source.SizeBasisFruitCount,
            GradeBasisFruitCount = source.GradeBasisFruitCount,
            JointSizeGradeBasisFruitCount = source.JointSizeGradeBasisFruitCount,
            AverageWeightGramsSnapshot = source.AverageWeightGramsSnapshot,
            AveragePressureLbsSnapshot = source.AveragePressureLbsSnapshot,
            GradeSummarySnapshot = source.GradeSummarySnapshot,
            DefectSummarySnapshot = source.DefectSummarySnapshot,
            ProjectionWarning = source.ProjectionWarning,
            CalculationVersion = source.CalculationVersion,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var size in source.SizeResults)
        {
            clone.SizeResults.Add(new RunProjectionSizeResult
            {
                Commodity = size.Commodity,
                SizeCategory = size.SizeCategory,
                SampleCount = size.SampleCount,
                Percentage = size.Percentage,
                UnroundedProjectedBoxes = size.UnroundedProjectedBoxes,
                RoundedProjectedBoxes = size.RoundedProjectedBoxes,
                PackedProjectedBoxes = size.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = size.RoundedPackedProjectedBoxes,
                CullProjectedBoxes = size.CullProjectedBoxes,
                RoundedCullProjectedBoxes = size.RoundedCullProjectedBoxes
            });
        }
        foreach (var grade in source.GradeResults)
        {
            clone.GradeResults.Add(new RunProjectionGradeResult
            {
                GradeCode = grade.GradeCode,
                SampleCount = grade.SampleCount,
                Percentage = grade.Percentage,
                GrossProjectedBoxes = grade.GrossProjectedBoxes,
                RoundedGrossProjectedBoxes = grade.RoundedGrossProjectedBoxes,
                PackedProjectedBoxes = grade.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = grade.RoundedPackedProjectedBoxes,
                CullProjectedBoxes = grade.CullProjectedBoxes,
                RoundedCullProjectedBoxes = grade.RoundedCullProjectedBoxes
            });
        }
        return clone;
    }

    private static bool HasEnteredFruitData(QcFruitReading row) =>
        row.Pressure1Lbs is not null
        || row.Pressure2Lbs is not null
        || row.WeightGrams is not null
        || row.GradeId is not null
        || row.StarchScaleValueId is not null
        || row.SizeCategory is not null
        || row.DefectsInspected
        || row.Defects.Count > 0;

    private static decimal? Average(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : decimal.Round(list.Average(), 2);
    }

    private static string Distribution(IEnumerable<string> values)
    {
        var list = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0) return "No data";
        return string.Join(", ", list.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Select(x => $"{x.Key} {x.Count() / (decimal)list.Count:P0}"));
    }

    private static string QcBasis(RunProjectionSource source) =>
        source.SelectedQcSampleId is null
            ? "No usable QC data"
            : $"{source.QcSampleTypeSnapshot ?? (source.SelectedQcSourceType == RunProjectionQcSourceTypes.FieldSample ? "Field Sample" : "Receipt QC")} #{source.SelectedQcSampleId}";

    private const string ConflictMessage = "This projection changed after the page loaded. Reload it before saving so another user's work is not overwritten.";

    private sealed record ProjectionSettingsSnapshot(
        decimal ApplePoundsPerBin,
        decimal PearPoundsPerBin,
        decimal StandardBoxWeightPounds,
        int DraftExpirationDays,
        int VisibilityPastDays,
        int VisibilityFutureDays,
        decimal DefaultExpectedPackoutPercent,
        int MinimumDistributionFruit);
}
