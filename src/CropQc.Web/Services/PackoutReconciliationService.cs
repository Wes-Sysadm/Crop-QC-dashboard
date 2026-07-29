using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IPackoutReconciliationService
{
    Task<(long? Id, string? Error)> UploadAsync(PackoutUploadForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<PackoutRunViewModel?> GetAsync(long id, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> UpdateLineAsync(PackoutLineReviewForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> UpdateSecondaryOutputsAsync(PackoutSecondaryOutputForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<(byte[]? Workbook, string? FileName, string? Error)> FinalizeAsync(PackoutFinalizeForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> ReopenAsync(PackoutReopenForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<(long ProjectionId, string? Error)> DeletePendingAsync(long id, long concurrencyVersion, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<(byte[]? Workbook, string? FileName)> DownloadAsync(long id, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> SavePackCodeAsync(PackCodeDefinitionForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<string?> SaveConfigurationAsync(PackoutAnalysisConfigurationForm form, ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class PackoutReconciliationService(
    CropQcDbContext dbContext,
    IPackoutReportParser parser,
    IPackoutFeedbackWorkbookService workbookService,
    IQcEmailSender emailSender,
    IUserAccessService accessService,
    IBusinessTimeService businessTime,
    IConfiguration configuration) : IPackoutReconciliationService
{
    private const string SourceApplication = "CropQc.Web";
    private const string FeedbackRecipient = "wes@fruitandland.com";
    private const int MaximumFilesPerUpload = 10;

    public async Task<(long? Id, string? Error)> UploadAsync(
        PackoutUploadForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(principal, cancellationToken))
        {
            return (null, "Bins Run Create access is required to upload actual packout results.");
        }
        if (form.DumpedBins <= 0m) return (null, "Dumped bins must be greater than zero.");
        if (form.PackingDate == default) return (null, "Packing date is required.");
        if (form.RunNumber <= 0) return (null, "Run number must be greater than zero.");
        if (form.Files.Count is < 1 or > MaximumFilesPerUpload)
        {
            return (null, $"Upload between 1 and {MaximumFilesPerUpload} related report files.");
        }

        var projection = await ProjectionQuery()
            .SingleOrDefaultAsync(x => x.Id == form.RunProjectionId, cancellationToken);
        if (projection is null || projection.IsDeleted) return (null, "Projection was not found.");
        if (projection.ProjectionMode != RunProjectionModes.Inventory)
        {
            return (null, "Actual packout reports can be reconciled only to an Inventory projection.");
        }
        if (projection.Sources.Count == 0 || projection.Sources.Any(x => x.Commodity == "Unknown"))
        {
            return (null, "The projection needs resolved inventory sources before actual packout can be uploaded.");
        }
        var profileIds = projection.Sources.Select(x => x.FruitProfileId).Distinct().ToList();
        var profiles = await dbContext.FruitProfiles.AsNoTracking().Where(x => profileIds.Contains(x.Id)).ToListAsync(cancellationToken);
        if (profiles.Select(x => new { x.VarietyCode, x.IsOrganic }).Distinct().Count() != 1)
        {
            return (null, "Actual reconciliation requires projection sources with the same variety and Organic/Conventional status.");
        }
        var facilityCode = projection.FacilityWarehouse?.Code ?? projection.FacilityCodeSnapshot ?? "";
        var existingRunId = await dbContext.PackoutRuns
            .Where(
                x => x.FacilitySnapshot == facilityCode
                    && x.PackingDate == form.PackingDate
                    && x.RunNumber == form.RunNumber)
            .Select(x => (long?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingRunId is long existingId)
        {
            return (existingId, "An actual run already exists for this facility, packing date, and run number. Use the existing run, or reopen and remove its pending replacement before uploading a corrected report.");
        }

        BinsRunEntry? binsRun = null;
        if (form.BinsRunEntryId is long binsRunId)
        {
            binsRun = await dbContext.BinsRunEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == binsRunId, cancellationToken);
            if (binsRun is null || binsRun.IsReversed) return (null, "Select an active Bins Run record.");
            var linkedIds = projection.Sources.Where(x => x.ActualBinsRunEntryId is not null).Select(x => x.ActualBinsRunEntryId!.Value).ToHashSet();
            if (!linkedIds.Contains(binsRun.Id)) return (null, "The selected Bins Run is not linked to this projection.");
        }

        var parsed = new List<PackoutParseResult>();
        foreach (var upload in form.Files)
        {
            await using var stream = new MemoryStream();
            await upload.CopyToAsync(stream, cancellationToken);
            try
            {
                parsed.Add(await parser.ParseAsync(
                    new PackoutUploadFile(upload.FileName, upload.ContentType ?? "application/octet-stream", stream.ToArray()),
                    cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return (null, $"{Path.GetFileName(upload.FileName)}: {exception.Message}");
            }
        }
        if (parsed.Sum(x => x.Lines.Count) == 0)
        {
            return (null, "No packout rows were parsed. Nothing was saved and the uploaded originals were not retained.");
        }

        var userId = await CurrentUserIdAsync(principal, cancellationToken);
        var now = businessTime.UtcNow;
        var configuration = await LoadConfigurationAsync(cancellationToken);
        var isPear = profiles.First().FruitType.Equals("Pear", StringComparison.OrdinalIgnoreCase);
        var run = new PackoutRun
        {
            RunProjectionId = projection.Id,
            BinsRunEntryId = binsRun?.Id,
            Status = PackoutRunStatuses.Review,
            FacilitySnapshot = facilityCode,
            PackingDate = form.PackingDate,
            RunNumber = form.RunNumber,
            LotNumberSnapshot = string.Join(" + ", projection.Sources.Select(x => x.LotSnapshot).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
            VarietySnapshot = profiles.First().Name,
            IsOrganicSnapshot = profiles.First().IsOrganic,
            CropYearSnapshot = projection.CropYear,
            DumpedBins = form.DumpedBins,
            PoundsPerBin = isPear ? configuration.PearBinWeightPounds : configuration.AppleBinWeightPounds,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CalculationVersion = PackoutReconciliationCalculationService.CurrentCalculationVersion,
            ProjectionSnapshotJson = JsonSerializer.Serialize(ProjectionSnapshot(projection)),
            ConfigurationSnapshotJson = JsonSerializer.Serialize(ConfigurationSnapshot(configuration))
        };
        var definitions = await dbContext.PackCodeDefinitions.Where(x => x.IsActive).ToListAsync(cancellationToken);
        foreach (var result in parsed)
        {
            var source = new PackoutReportSource
            {
                OriginalFileName = result.FileName,
                ContentType = result.ContentType,
                FileSizeBytes = result.FileSizeBytes,
                Sha256 = result.Sha256,
                ParserName = result.ParserName,
                ParserVersion = result.ParserVersion,
                Confidence = result.Confidence,
                SafeDiagnostic = result.SafeDiagnostic,
                ParsedAt = now
            };
            run.Sources.Add(source);
            foreach (var parsedLine in result.Lines)
            {
                var classified = PackoutReportParser.ClassifyPackCode(parsedLine.RawPackCode);
                var definition = definitions.SingleOrDefault(x => x.NormalizedCode == classified.NormalizedCode);
                if (definition is null
                    && classified.NormalizedCode.Length > 0
                    && classified.ProductCategory is null)
                {
                    definition = new PackCodeDefinition
                    {
                        Code = parsedLine.RawPackCode?.Trim() ?? classified.NormalizedCode,
                        NormalizedCode = classified.NormalizedCode,
                        DisplayName = parsedLine.RawPackCode?.Trim() ?? classified.NormalizedCode,
                        ProductCategory = PackoutProductCategories.Packed,
                        NetWeightPounds = null,
                        IsActive = true,
                        CreatedAt = now,
                        CreatedByUserId = userId,
                        UpdatedAt = now,
                        UpdatedByUserId = userId
                    };
                    definitions.Add(definition);
                    dbContext.PackCodeDefinitions.Add(definition);
                }
                var category = definition?.ProductCategory ?? classified.ProductCategory;
                var netWeight = definition?.NetWeightPounds ?? classified.NetWeightPounds;
                run.Lines.Add(new PackoutReportLine
                {
                    PackoutReportSource = source,
                    SourceLineNumber = parsedLine.SourceLineNumber,
                    RawText = parsedLine.RawText,
                    RawPackCode = parsedLine.RawPackCode,
                    NormalizedPackCode = classified.NormalizedCode,
                    PackCodeDefinition = definition,
                    Quantity = parsedLine.Quantity,
                    NetWeightPounds = netWeight,
                    ExtendedWeightPounds = parsedLine.Quantity * netWeight,
                    SizeCategory = definition?.SizeCategory,
                    GradeId = definition?.GradeId,
                    ProductCategory = category,
                    Confidence = parsedLine.Confidence,
                    RequiresReview = parsedLine.RequiresReview
                        || definition is null && classified.ProductCategory is null
                        || netWeight is null
                        || parsedLine.Quantity is < 0m,
                    CreatedAt = now
                });
            }
        }
        dbContext.PackoutRuns.Add(run);
        projection.UpdatedAt = now;
        projection.UpdatedByUserId = userId;
        projection.ConcurrencyVersion++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateAsync(run, cancellationToken);
        AddAudit("UploadPackoutReports", run, userId, null, new
        {
            run.RunProjectionId,
            run.DumpedBins,
            SourceFiles = run.Sources.Select(x => new { x.OriginalFileName, x.Sha256, x.ParserName }),
            ParsedLines = run.Lines.Count,
            RequiresReview = run.Lines.Count(x => x.RequiresReview),
            OriginalFilesRetained = false
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return (run.Id, null);
    }

    public async Task<PackoutRunViewModel?> GetAsync(long id, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!await accessService.HasAccessAsync(principal, ApplicationAreas.ProjectionOutcome, PageAccessLevel.View, cancellationToken))
        {
            throw new UnauthorizedAccessException("Projection Outcome View access is required.");
        }
        var run = await RunQuery(asTracking: false).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null) return null;
        var configuration = await LoadConfigurationAsync(cancellationToken);
        var hasEditAccess = await CanEditAsync(principal, cancellationToken);
        var canEdit = hasEditAccess && run.Status != PackoutRunStatuses.Finalized;
        var canAdmin = await accessService.HasAccessAsync(principal, ApplicationAreas.ProjectionOutcome, PageAccessLevel.Admin, cancellationToken);
        return new PackoutRunViewModel
        {
            Id = run.Id,
            RunProjectionId = run.RunProjectionId,
            ProjectionName = run.RunProjection.Name,
            BinsRunEntryId = run.BinsRunEntryId,
            Status = run.Status,
            Facility = run.FacilitySnapshot,
            PackingDate = run.PackingDate,
            RunNumber = run.RunNumber,
            LotNumber = run.LotNumberSnapshot,
            Variety = run.VarietySnapshot,
            IsOrganic = run.IsOrganicSnapshot,
            CropYear = run.CropYearSnapshot,
            DumpedBins = run.DumpedBins,
            PoundsPerBin = run.PoundsPerBin,
            DumpedPounds = run.DumpedPounds,
            PackedProductPounds = run.PackedProductPounds,
            JuicePounds = run.JuicePounds,
            PeelerSlicerPounds = run.PeelerSlicerPounds,
            WastePounds = run.WastePounds,
            SupplementalJuiceBins = run.PoundsPerBin <= 0m ? 0m : (run.SupplementalJuicePounds ?? 0m) / run.PoundsPerBin,
            SupplementalPeelerSlicerBins = run.PoundsPerBin <= 0m ? 0m : (run.SupplementalPeelerSlicerPounds ?? 0m) / run.PoundsPerBin,
            SupplementalWasteBins = run.PoundsPerBin <= 0m ? 0m : (run.SupplementalWastePounds ?? 0m) / run.PoundsPerBin,
            ActualPackoutPercent = run.ActualPackoutPercent,
            OverallAccuracyScore = run.OverallAccuracyScore,
            ReconciliationDifferencePounds = run.ReconciliationDifferencePounds,
            HasReconciliationWarning = run.HasReconciliationWarning,
            ConcurrencyVersion = run.ConcurrencyVersion,
            CanEdit = canEdit,
            CanReopen = hasEditAccess || canAdmin,
            CanAdmin = canAdmin,
            Configuration = new PackoutAnalysisConfigurationForm
            {
                AppleBinWeightPounds = configuration.AppleBinWeightPounds,
                PearBinWeightPounds = configuration.PearBinWeightPounds,
                SizeScoreWeight = configuration.SizeScoreWeight,
                GradeScoreWeight = configuration.GradeScoreWeight,
                PackoutScoreWeight = configuration.PackoutScoreWeight,
                JuiceScoreWeight = configuration.JuiceScoreWeight,
                PeelerSlicerScoreWeight = configuration.PeelerSlicerScoreWeight,
                WasteScoreWeight = configuration.WasteScoreWeight,
                CurrentCropYearHistoryWeight = configuration.CurrentCropYearHistoryWeight,
                PriorCropYearHistoryWeight = configuration.PriorCropYearHistoryWeight
            },
            Sources = run.Sources.Select(x => new PackoutSourceViewModel(x.OriginalFileName, x.ParserName, x.Confidence, x.SafeDiagnostic, x.ParsedAt)).ToList(),
            Lines = run.Lines.OrderBy(x => x.PackoutReportSourceId).ThenBy(x => x.SourceLineNumber).Select(x => new PackoutLineViewModel(
                x.Id, x.SourceLineNumber, x.RawText, x.RawPackCode, x.Quantity, x.NetWeightPounds,
                x.ExtendedWeightPounds, x.SizeCategory, x.GradeId, x.Grade?.Code, x.ProductCategory,
                x.Confidence, x.RequiresReview, x.WasCorrected, x.NegativeQuantityConfirmed)).ToList(),
            PackCodes = await dbContext.PackCodeDefinitions.AsNoTracking().OrderBy(x => x.Code).Select(x => new PackCodeOptionViewModel(
                x.Id, x.Code, x.DisplayName, x.ProductCategory, x.NetWeightPounds, x.SizeCategory, x.GradeId, x.IsActive)).ToListAsync(cancellationToken),
            Grades = await dbContext.Grades.AsNoTracking().OrderBy(x => x.Code).Select(x => new PackoutGradeOptionViewModel(x.Id, x.Code, x.Name)).ToListAsync(cancellationToken)
        };
    }

    public async Task<string?> UpdateLineAsync(PackoutLineReviewForm form, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(principal, cancellationToken)) return "Bins Run Create access is required.";
        var run = await RunQuery().SingleOrDefaultAsync(x => x.Id == form.PackoutRunId, cancellationToken);
        if (run is null) return "Packout reconciliation was not found.";
        var guard = EditableGuard(run, form.ConcurrencyVersion);
        if (guard is not null) return guard;
        var line = run.Lines.SingleOrDefault(x => x.Id == form.LineId);
        if (line is null) return "Parsed line was not found.";
        if (form.Quantity is null or 0m) return "Quantity must be nonzero.";
        if (form.Quantity < 0m && !form.NegativeQuantityConfirmed)
        {
            return "Confirm negative quantity adjustments explicitly before saving.";
        }
        if (form.NetWeightPounds is <= 0m) return "Net weight must be greater than zero.";
        if (string.IsNullOrWhiteSpace(form.ProductCategory) || !PackoutProductCategories.All.Contains(form.ProductCategory))
        {
            return "Select Packed product, Juice, Peeler/Slicer, or Waste.";
        }
        if (string.IsNullOrWhiteSpace(form.CorrectionReason)) return "A correction reason is required.";
        var before = LineSnapshot(line);
        line.RawPackCode = string.IsNullOrWhiteSpace(form.PackCode) ? null : form.PackCode.Trim();
        line.NormalizedPackCode = PackoutReportParser.NormalizePackCode(line.RawPackCode);
        line.Quantity = form.Quantity;
        line.NetWeightPounds = form.NetWeightPounds;
        line.ExtendedWeightPounds = form.Quantity * form.NetWeightPounds;
        line.SizeCategory = form.SizeCategory;
        line.GradeId = form.GradeId;
        line.ProductCategory = form.ProductCategory;
        line.RequiresReview = false;
        line.NegativeQuantityConfirmed = form.Quantity > 0m || form.NegativeQuantityConfirmed;
        line.WasCorrected = true;
        line.CorrectionReason = form.CorrectionReason.Trim();
        line.UpdatedAt = businessTime.UtcNow;
        line.UpdatedByUserId = await CurrentUserIdAsync(principal, cancellationToken);
        Touch(run, line.UpdatedByUserId);
        await RecalculateAsync(run, cancellationToken);
        AddAudit("CorrectPackoutLine", run, line.UpdatedByUserId, before, LineSnapshot(line));
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> UpdateSecondaryOutputsAsync(PackoutSecondaryOutputForm form, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(principal, cancellationToken)) return "Bins Run Create access is required.";
        var run = await RunQuery().SingleOrDefaultAsync(x => x.Id == form.PackoutRunId, cancellationToken);
        if (run is null) return "Packout reconciliation was not found.";
        var guard = EditableGuard(run, form.ConcurrencyVersion);
        if (guard is not null) return guard;
        if (form.JuiceBins < 0 || form.PeelerSlicerBins < 0 || form.WasteBins < 0) return "Secondary-output bins cannot be negative.";
        var before = new { run.SupplementalJuicePounds, run.SupplementalPeelerSlicerPounds, run.SupplementalWastePounds, run.ReviewNotes };
        run.SupplementalJuicePounds = form.JuiceBins * run.PoundsPerBin;
        run.SupplementalPeelerSlicerPounds = form.PeelerSlicerBins * run.PoundsPerBin;
        run.SupplementalWastePounds = form.WasteBins * run.PoundsPerBin;
        run.ReviewNotes = string.IsNullOrWhiteSpace(form.ReviewNotes) ? null : form.ReviewNotes.Trim();
        var userId = await CurrentUserIdAsync(principal, cancellationToken);
        Touch(run, userId);
        await RecalculateAsync(run, cancellationToken);
        AddAudit("UpdatePackoutSecondaryOutputs", run, userId, before, new
        {
            form.JuiceBins,
            form.PeelerSlicerBins,
            form.WasteBins,
            run.PoundsPerBin,
            run.SupplementalJuicePounds,
            run.SupplementalPeelerSlicerPounds,
            run.SupplementalWastePounds,
            run.ReviewNotes
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(byte[]? Workbook, string? FileName, string? Error)> FinalizeAsync(
        PackoutFinalizeForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!await accessService.HasAccessAsync(principal, ApplicationAreas.ProjectionOutcome, PageAccessLevel.Admin, cancellationToken))
        {
            return (null, null, "Projection Outcome Admin access is required to finalize actual-run feedback.");
        }
        var run = await RunQuery().SingleOrDefaultAsync(x => x.Id == form.PackoutRunId, cancellationToken);
        if (run is null) return (null, null, "Packout reconciliation was not found.");
        var guard = EditableGuard(run, form.ConcurrencyVersion);
        if (guard is not null) return (null, null, guard);
        var linkedBinsRunIds = run.RunProjection.Sources
            .Where(x => x.SourceType == RunProjectionSourceTypes.Inventory)
            .Select(x => x.ActualBinsRunEntryId)
            .ToList();
        if (linkedBinsRunIds.Count == 0 || linkedBinsRunIds.Any(x => x is null))
        {
            return (null, null, "Finalize and link every applicable Bins Run component before finalizing packout feedback.");
        }
        var linkedIds = linkedBinsRunIds.Select(x => x!.Value).Distinct().ToList();
        var linkedBinsRuns = await dbContext.BinsRunEntries
            .Where(x => linkedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (linkedBinsRuns.Count != linkedIds.Count || linkedBinsRuns.Any(x => x.IsReversed))
        {
            return (null, null, "One or more linked Bins Run components are missing or reversed.");
        }
        if (!run.RunProjection.IsLocked)
        {
            return (null, null, "The shared projection is not locked yet. Finalize every linked Bins Run component first.");
        }
        if (run.Lines.Count == 0 || run.Lines.Any(x => x.RequiresReview || x.Quantity is null || x.NetWeightPounds is null || string.IsNullOrWhiteSpace(x.ProductCategory)))
        {
            return (null, null, "Review every low-confidence or unmapped packout line before finalization.");
        }

        await RecalculateAsync(run, cancellationToken);
        run.Status = PackoutRunStatuses.PendingFinalization;
        var userId = await CurrentUserIdAsync(principal, cancellationToken);
        var sender = userId is int id
            ? await dbContext.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            : null;
        if (sender is null) return (null, null, "A signed-in user is required to send final packout feedback.");
        var fileName = $"packout-feedback-{run.CropYearSnapshot}-{SafeFileName(run.LotNumberSnapshot)}-{run.Id}.xlsx";
        var workbook = workbookService.Build(run);
        AddAudit("GeneratePackoutFeedbackWorkbook", run, userId, null, new { FileName = fileName, WorksheetCount = 1 });
        var (textBody, htmlBody) = BuildEmailBodies(run);
        var message = new QcEmailMessage(
            sender.Email,
            FeedbackRecipient,
            sender.Email,
            $"{(run.ReopenedAt is null ? "" : "Updated ")}Packout feedback — {run.FacilitySnapshot} — {run.PackingDate:yyyy-MM-dd} Run {run.RunNumber}",
            textBody,
            htmlBody,
            [],
            [new QcEmailAttachment(fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", workbook)]);
        var sent = await emailSender.SendAsync(sender, message, cancellationToken);
        run.EmailAttempts.Add(new PackoutEmailAttempt
        {
            Recipient = FeedbackRecipient,
            SenderUserId = userId,
            AttemptedAt = businessTime.UtcNow,
            Succeeded = sent.Success,
            MessageId = sent.MessageId,
            SafeError = sent.Success ? null : SafeEmailError(sent.Error),
            IsUpdatedAnalysis = run.ReopenedAt is not null
        });
        if (!sent.Success)
        {
            run.Status = PackoutRunStatuses.PendingFinalization;
            AddAudit("FinalizePackoutEmailFailed", run, userId, null, new { Error = SafeEmailError(sent.Error), Recipient = FeedbackRecipient });
            await dbContext.SaveChangesAsync(cancellationToken);
            return (workbook, fileName, $"Feedback was calculated but Gmail delivery failed; the run remains pending finalization. {sent.Error}");
        }

        run.Status = PackoutRunStatuses.Finalized;
        run.FinalizedAt = businessTime.UtcNow;
        run.FinalizedByUserId = userId;
        run.FinalReportFileName = fileName;
        run.FinalReportSha256 = Convert.ToHexString(SHA256.HashData(workbook)).ToLowerInvariant();
        run.FinalEmailMessageId = sent.MessageId;
        run.RunProjection.IsLocked = true;
        run.RunProjection.LockedAt ??= businessTime.UtcNow;
        run.RunProjection.LockedByUserId ??= userId;
        foreach (var linkedBinsRun in linkedBinsRuns)
        {
            linkedBinsRun.IsReconciled = true;
            linkedBinsRun.ReconciledAt = businessTime.UtcNow;
            linkedBinsRun.ReconciledByUserId = userId;
        }
        Touch(run, userId);
        AddAudit("FinalizePackout", run, userId, null, new { run.FinalReportFileName, run.FinalReportSha256, Recipient = FeedbackRecipient, run.FinalEmailMessageId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return (workbook, fileName, null);
    }

    public async Task<string?> ReopenAsync(PackoutReopenForm form, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(principal, cancellationToken)
            && !await accessService.HasAccessAsync(principal, ApplicationAreas.ProjectionOutcome, PageAccessLevel.Admin, cancellationToken))
        {
            return "Projection Outcome Create or Admin access is required to reopen finalized feedback.";
        }
        if (string.IsNullOrWhiteSpace(form.Reason)) return "A reopen reason is required.";
        var run = await RunQuery().SingleOrDefaultAsync(x => x.Id == form.PackoutRunId, cancellationToken);
        if (run is null) return "Packout reconciliation was not found.";
        if (run.ConcurrencyVersion != form.ConcurrencyVersion) return "This packout reconciliation changed after the page loaded. Reload before continuing.";
        if (run.Status != PackoutRunStatuses.Finalized) return "Only finalized packout feedback can be reopened.";
        var userId = await CurrentUserIdAsync(principal, cancellationToken);
        run.Status = PackoutRunStatuses.Reopened;
        run.ReopenedAt = businessTime.UtcNow;
        run.ReopenedByUserId = userId;
        run.ReopenReason = form.Reason.Trim();
        run.RunProjection.IsLocked = false;
        run.RunProjection.LockedAt = null;
        run.RunProjection.LockedByUserId = null;
        var linkedIds = run.RunProjection.Sources
            .Where(x => x.ActualBinsRunEntryId is not null)
            .Select(x => x.ActualBinsRunEntryId!.Value)
            .Distinct()
            .ToList();
        var linkedBinsRuns = await dbContext.BinsRunEntries.Where(x => linkedIds.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var linkedBinsRun in linkedBinsRuns)
        {
            linkedBinsRun.IsReconciled = false;
            linkedBinsRun.ReconciledAt = null;
            linkedBinsRun.ReconciledByUserId = null;
        }
        Touch(run, userId);
        AddAudit("ReopenPackout", run, userId, null, new { run.ReopenReason });
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(long ProjectionId, string? Error)> DeletePendingAsync(
        long id,
        long concurrencyVersion,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(principal, cancellationToken))
        {
            return (0, "Projection Outcome Create access is required to remove a pending actual run.");
        }
        var run = await RunQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null) return (0, "Packout reconciliation was not found.");
        if (run.ConcurrencyVersion != concurrencyVersion)
        {
            return (run.RunProjectionId, "This packout reconciliation changed after the page loaded. Reload before removing it.");
        }
        if (run.Status == PackoutRunStatuses.Finalized)
        {
            return (run.RunProjectionId, "Finalized actual runs cannot be deleted. Reopen it first; finalized email and audit history remain preserved.");
        }
        if (run.BinsRunEntry?.IsReconciled == true)
        {
            return (run.RunProjectionId, "A reconciled Bins Run cannot be removed until the actual run is reopened.");
        }

        var userId = await CurrentUserIdAsync(principal, cancellationToken);
        var projectionId = run.RunProjectionId;
        AddAudit("DeletePendingPackout", run, userId, new
        {
            run.Status,
            run.FacilitySnapshot,
            run.PackingDate,
            run.RunNumber,
            SourceHashes = run.Sources.Select(x => x.Sha256),
            ParsedLineCount = run.Lines.Count
        }, null);
        dbContext.PackoutRuns.Remove(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (projectionId, null);
    }

    public async Task<(byte[]? Workbook, string? FileName)> DownloadAsync(long id, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!await accessService.HasAccessAsync(principal, ApplicationAreas.ProjectionOutcome, PageAccessLevel.Admin, cancellationToken))
        {
            throw new UnauthorizedAccessException("Projection Outcome Admin access is required for protected exports.");
        }
        var run = await RunQuery(asTracking: false).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null) return (null, null);
        return (workbookService.Build(run), run.FinalReportFileName ?? $"packout-feedback-{run.Id}.xlsx");
    }

    public async Task<string?> SavePackCodeAsync(PackCodeDefinitionForm form, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!await accessService.HasAccessAsync(principal, ApplicationAreas.MasterData, PageAccessLevel.Admin, cancellationToken))
        {
            return "Master Data Admin access is required to configure pack codes.";
        }
        var normalized = PackoutReportParser.NormalizePackCode(form.Code);
        if (normalized.Length == 0) return "Pack code is required.";
        if (!PackoutProductCategories.All.Contains(form.ProductCategory)) return "Select a valid product category.";
        if (form.NetWeightPounds is <= 0m) return "Net weight must be greater than zero.";
        var duplicate = await dbContext.PackCodeDefinitions.AnyAsync(x => x.NormalizedCode == normalized && x.Id != form.Id, cancellationToken);
        if (duplicate) return "That normalized pack code already exists.";
        var userId = await CurrentUserIdAsync(principal, cancellationToken);
        var entity = form.Id is int id
            ? await dbContext.PackCodeDefinitions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            : null;
        var action = entity is null ? "CreatePackCode" : "UpdatePackCode";
        var before = entity is null ? null : PackCodeSnapshot(entity);
        entity ??= new PackCodeDefinition
        {
            Code = form.Code.Trim(),
            NormalizedCode = normalized,
            DisplayName = form.DisplayName.Trim(),
            ProductCategory = form.ProductCategory,
            CreatedAt = businessTime.UtcNow,
            CreatedByUserId = userId
        };
        entity.Code = form.Code.Trim();
        entity.NormalizedCode = normalized;
        entity.DisplayName = string.IsNullOrWhiteSpace(form.DisplayName) ? entity.Code : form.DisplayName.Trim();
        entity.ProductCategory = form.ProductCategory;
        entity.NetWeightPounds = form.NetWeightPounds;
        entity.SizeCategory = form.SizeCategory;
        entity.GradeId = form.GradeId;
        entity.IsActive = form.IsActive;
        entity.UpdatedAt = businessTime.UtcNow;
        entity.UpdatedByUserId = userId;
        if (entity.Id == 0) dbContext.PackCodeDefinitions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAudit(action, nameof(PackCodeDefinition), entity.Id.ToString(CultureInfo.InvariantCulture), userId, before, PackCodeSnapshot(entity));
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SaveConfigurationAsync(
        PackoutAnalysisConfigurationForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!await accessService.HasAccessAsync(principal, ApplicationAreas.MasterData, PageAccessLevel.Admin, cancellationToken))
        {
            return "Master Data Admin access is required to configure packout analysis.";
        }
        if (form.AppleBinWeightPounds <= 0m || form.PearBinWeightPounds <= 0m)
        {
            return "Apple and pear bin weights must be greater than zero.";
        }
        var scoreTotal = form.SizeScoreWeight + form.GradeScoreWeight + form.PackoutScoreWeight
            + form.JuiceScoreWeight + form.PeelerSlicerScoreWeight + form.WasteScoreWeight;
        if (scoreTotal != 100m) return "Accuracy-score weights must total exactly 100%.";
        if (form.CurrentCropYearHistoryWeight + form.PriorCropYearHistoryWeight != 100m)
        {
            return "Current/prior crop-year history weights must total exactly 100%.";
        }
        if (new[]
            {
                form.SizeScoreWeight,
                form.GradeScoreWeight,
                form.PackoutScoreWeight,
                form.JuiceScoreWeight,
                form.PeelerSlicerScoreWeight,
                form.WasteScoreWeight,
                form.CurrentCropYearHistoryWeight,
                form.PriorCropYearHistoryWeight
            }.Any(x => x < 0m))
        {
            return "Configuration weights cannot be negative.";
        }

        var configuration = await LoadConfigurationAsync(cancellationToken);
        var before = ConfigurationSnapshot(configuration);
        configuration.AppleBinWeightPounds = form.AppleBinWeightPounds;
        configuration.PearBinWeightPounds = form.PearBinWeightPounds;
        configuration.SizeScoreWeight = form.SizeScoreWeight;
        configuration.GradeScoreWeight = form.GradeScoreWeight;
        configuration.PackoutScoreWeight = form.PackoutScoreWeight;
        configuration.JuiceScoreWeight = form.JuiceScoreWeight;
        configuration.PeelerSlicerScoreWeight = form.PeelerSlicerScoreWeight;
        configuration.WasteScoreWeight = form.WasteScoreWeight;
        configuration.CurrentCropYearHistoryWeight = form.CurrentCropYearHistoryWeight;
        configuration.PriorCropYearHistoryWeight = form.PriorCropYearHistoryWeight;
        configuration.UpdatedAt = businessTime.UtcNow;
        configuration.UpdatedByUserId = await CurrentUserIdAsync(principal, cancellationToken);
        if (dbContext.Entry(configuration).State == EntityState.Detached)
        {
            dbContext.PackoutAnalysisConfigurations.Add(configuration);
        }
        AddAudit(
            "UpdatePackoutAnalysisConfiguration",
            nameof(PackoutAnalysisConfiguration),
            configuration.Id.ToString(CultureInfo.InvariantCulture),
            configuration.UpdatedByUserId,
            before,
            ConfigurationSnapshot(configuration));
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task RecalculateAsync(PackoutRun run, CancellationToken cancellationToken)
    {
        var projectedSize = WeightedProjectedSize(run.RunProjection);
        var projectedGrade = WeightedProjectedGrade(run.RunProjection);
        decimal? expectedPackout = run.RunProjection.TotalProjectedPounds <= 0m
            ? null
            : run.RunProjection.TotalPackedProjectedPounds / run.RunProjection.TotalProjectedPounds * 100m;
        var expectedCull = expectedPackout is null ? null : 100m - expectedPackout;
        var lines = run.Lines
            .Where(x => x.Quantity is not null and not 0m
                && x.NetWeightPounds is > 0m
                && !string.IsNullOrWhiteSpace(x.ProductCategory)
                && (x.Quantity > 0m || x.NegativeQuantityConfirmed))
            .Select(x => new PackoutActualLine(x.Quantity!.Value, x.NetWeightPounds!.Value, x.ProductCategory!, x.SizeCategory, x.Grade?.Code))
            .ToList();
        if (run.SupplementalJuicePounds is > 0m) lines.Add(new(1m, run.SupplementalJuicePounds.Value, PackoutProductCategories.Juice));
        if (run.SupplementalPeelerSlicerPounds is > 0m) lines.Add(new(1m, run.SupplementalPeelerSlicerPounds.Value, PackoutProductCategories.PeelerSlicer));
        if (run.SupplementalWastePounds is > 0m) lines.Add(new(1m, run.SupplementalWastePounds.Value, PackoutProductCategories.Waste));
        var configuration = await LoadConfigurationAsync(cancellationToken);
        var calculation = PackoutReconciliationCalculationService.Calculate(
            run.DumpedBins,
            run.PoundsPerBin,
            lines,
            projectedSize,
            projectedGrade,
            expectedPackout,
            expectedCull * run.RunProjection.JuiceCullShare,
            expectedCull * run.RunProjection.PeelerCullShare,
            expectedCull * run.RunProjection.WasteCullShare,
            new PackoutAccuracyWeights(
                configuration.SizeScoreWeight,
                configuration.GradeScoreWeight,
                configuration.PackoutScoreWeight,
                configuration.JuiceScoreWeight,
                configuration.PeelerSlicerScoreWeight,
                configuration.WasteScoreWeight));
        run.DumpedPounds = calculation.DumpedPounds;
        run.PackedProductPounds = calculation.PackedProductPounds;
        run.JuicePounds = calculation.JuicePounds;
        run.PeelerSlicerPounds = calculation.PeelerSlicerPounds;
        run.WastePounds = calculation.WastePounds;
        run.ActualPackoutPercent = calculation.PackoutPercent;
        run.ActualJuicePercent = calculation.JuicePercent;
        run.ActualPeelerSlicerPercent = calculation.PeelerSlicerPercent;
        run.ActualWastePercent = calculation.WastePercent;
        run.SizeAccuracyScore = calculation.SizeAccuracy;
        run.GradeAccuracyScore = calculation.GradeAccuracy;
        run.PackoutAccuracyScore = calculation.PackoutAccuracy;
        run.JuiceAccuracyScore = calculation.JuiceAccuracy;
        run.PeelerSlicerAccuracyScore = calculation.PeelerSlicerAccuracy;
        run.WasteAccuracyScore = calculation.WasteAccuracy;
        run.OverallAccuracyScore = calculation.OverallAccuracy;
        run.ReconciliationDifferencePounds = calculation.ReconciliationDifferencePounds;
        run.HasReconciliationWarning = calculation.HasReconciliationWarning;
        run.ActualDistributionSnapshotJson = JsonSerializer.Serialize(new { calculation.SizeDistribution, calculation.GradeDistribution });
        run.AccuracySnapshotJson = JsonSerializer.Serialize(new
        {
            calculation.SizeAccuracy,
            calculation.GradeAccuracy,
            calculation.PackoutAccuracy,
            calculation.JuiceAccuracy,
            calculation.PeelerSlicerAccuracy,
            calculation.WasteAccuracy,
            calculation.OverallAccuracy
        });
        await Task.CompletedTask;
    }

    private IQueryable<RunProjection> ProjectionQuery() =>
        dbContext.RunProjections
            .Include(x => x.FacilityWarehouse)
            .Include(x => x.Sources).ThenInclude(x => x.FruitProfile)
            .Include(x => x.Sources).ThenInclude(x => x.SizeResults)
            .Include(x => x.Sources).ThenInclude(x => x.GradeResults);

    private IQueryable<PackoutRun> RunQuery(bool asTracking = true)
    {
        IQueryable<PackoutRun> query = dbContext.PackoutRuns
            .Include(x => x.RunProjection).ThenInclude(x => x.Sources).ThenInclude(x => x.SizeResults)
            .Include(x => x.RunProjection).ThenInclude(x => x.Sources).ThenInclude(x => x.GradeResults)
            .Include(x => x.BinsRunEntry)
            .Include(x => x.Sources)
            .Include(x => x.Lines).ThenInclude(x => x.PackoutReportSource)
            .Include(x => x.Lines).ThenInclude(x => x.Grade);
        return asTracking ? query : query.AsNoTracking();
    }

    private static Dictionary<string, decimal> WeightedProjectedSize(RunProjection projection)
    {
        var total = projection.Sources.Sum(x => x.PackedProjectedPounds);
        if (total <= 0m) return [];
        return projection.Sources
            .SelectMany(source => source.SizeResults.Select(result => new
            {
                Key = result.SizeCategory.ToString(CultureInfo.InvariantCulture),
                Weight = source.PackedProjectedPounds * result.Percentage / 100m
            }))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Weight) / total * 100m, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> WeightedProjectedGrade(RunProjection projection)
    {
        var total = projection.Sources.Sum(x => x.PackedProjectedPounds);
        if (total <= 0m) return [];
        return projection.Sources
            .SelectMany(source => source.GradeResults.Select(result => new
            {
                Key = result.GradeCode,
                Weight = source.PackedProjectedPounds * result.Percentage / 100m
            }))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Weight) / total * 100m, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> CanEditAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        await accessService.HasAccessAsync(principal, ApplicationAreas.BinsRun, PageAccessLevel.Create, cancellationToken)
        && await accessService.HasAccessAsync(principal, ApplicationAreas.ProjectionOutcome, PageAccessLevel.Create, cancellationToken);

    private async Task<PackoutAnalysisConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken) =>
        await dbContext.PackoutAnalysisConfigurations.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
        ?? new PackoutAnalysisConfiguration { UpdatedAt = businessTime.UtcNow };

    private async Task<int?> CurrentUserIdAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking().Where(x => x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
    }

    private static string? EditableGuard(PackoutRun run, long concurrencyVersion)
    {
        if (run.ConcurrencyVersion != concurrencyVersion) return "This packout reconciliation changed after the page loaded. Reload before saving.";
        if (run.Status == PackoutRunStatuses.Finalized) return "Finalized packout feedback is immutable. An administrator must reopen it first.";
        return null;
    }

    private void Touch(PackoutRun run, int? userId)
    {
        run.UpdatedAt = businessTime.UtcNow;
        run.UpdatedByUserId = userId;
        run.ConcurrencyVersion++;
    }

    private void AddAudit(string action, PackoutRun run, int? userId, object? before, object? after) =>
        AddAudit(action, nameof(PackoutRun), run.Id.ToString(CultureInfo.InvariantCulture), userId, before, after);

    private void AddAudit(string action, string entity, string key, int? userId, object? before, object? after) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entity,
            EntityKey = key,
            UserId = userId,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after),
            SourceApplication = SourceApplication,
            CreatedAt = businessTime.UtcNow
        });

    private static object ProjectionSnapshot(RunProjection projection) => new
    {
        projection.Id,
        projection.Name,
        projection.PlannedRunDate,
        projection.CropYear,
        projection.FacilityCodeSnapshot,
        projection.TotalPlannedBins,
        projection.TotalProjectedPounds,
        projection.TotalPackedProjectedPounds,
        Sources = projection.Sources.Select(x => new
        {
            x.Id,
            x.SourceLabelSnapshot,
            x.GrowerLotKeySnapshot,
            x.RoomSnapshot,
            x.LotSnapshot,
            x.VarietySnapshot,
            x.PlannedBins,
            x.AvailableBinsSnapshot,
            x.ReceivedBinsSnapshot,
            x.ContributingReceiptIdsJson,
            x.ContributingSampleIdsJson,
            x.ReceiptWeightingSnapshotJson,
            x.ExpectedPackoutPercent,
            x.TotalDefectPercentageSnapshot,
            x.CalculationVersion
        })
    };

    private static object ConfigurationSnapshot(PackoutAnalysisConfiguration configuration) => new
    {
        configuration.AppleBinWeightPounds,
        configuration.PearBinWeightPounds,
        configuration.SizeScoreWeight,
        configuration.GradeScoreWeight,
        configuration.PackoutScoreWeight,
        configuration.JuiceScoreWeight,
        configuration.PeelerSlicerScoreWeight,
        configuration.WasteScoreWeight,
        configuration.CurrentCropYearHistoryWeight,
        configuration.PriorCropYearHistoryWeight
    };

    private static object LineSnapshot(PackoutReportLine line) => new
    {
        line.Id,
        line.RawPackCode,
        line.NormalizedPackCode,
        line.Quantity,
        line.NetWeightPounds,
        line.ExtendedWeightPounds,
        line.SizeCategory,
        line.GradeId,
        line.ProductCategory,
        line.RequiresReview,
        line.NegativeQuantityConfirmed,
        line.WasCorrected,
        line.CorrectionReason
    };

    private static object PackCodeSnapshot(PackCodeDefinition definition) => new
    {
        definition.Code,
        definition.NormalizedCode,
        definition.DisplayName,
        definition.ProductCategory,
        definition.NetWeightPounds,
        definition.SizeCategory,
        definition.GradeId,
        definition.IsActive
    };

    private (string Text, string Html) BuildEmailBodies(PackoutRun run)
    {
        var title = run.ReopenedAt is null ? "Final packout feedback" : "Updated packout feedback";
        var production = run.IsOrganicSnapshot ? "Organic" : "Conventional";
        var siteBase = configuration["PublicBaseUrl"] ?? configuration["QcStation:ApiBaseUrl"];
        var analysisUrl = Uri.TryCreate(siteBase?.TrimEnd('/'), UriKind.Absolute, out var baseUri)
            ? new Uri(baseUri, $"/BinsRun/Packout/{run.Id}").ToString()
            : null;
        var sources = run.RunProjection.Sources
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => $"{x.GrowerSnapshot ?? "Unknown grower"} / lot {x.LotSnapshot ?? "Unknown"} / {x.RoomSnapshot ?? "Unassigned room"}")
            .ToList();
        var oddity = run.HasReconciliationWarning
            ? $"Oddity: packed plus secondary output differs from dumped weight by {run.ReconciliationDifferencePounds:0.##} lb (more than 10%). This does not reduce the accuracy score."
            : $"Weight reconciliation difference: {run.ReconciliationDifferencePounds:0.##} lb.";

        var text = new StringBuilder()
            .AppendLine(title)
            .AppendLine($"Facility: {run.FacilitySnapshot}")
            .AppendLine($"Packing date / run: {run.PackingDate:yyyy-MM-dd} / {run.RunNumber}")
            .AppendLine($"Variety: {run.VarietySnapshot} ({production})")
            .AppendLine($"Grower lots / rooms: {string.Join("; ", sources)}")
            .AppendLine($"Dumped: {run.DumpedBins:0.##} bins / {run.DumpedPounds:0.##} lb")
            .AppendLine($"Packout: projected {ProjectedPackout(run):0.##}% / actual {run.ActualPackoutPercent:0.##}%")
            .AppendLine($"Secondary outputs: Juice {run.ActualJuicePercent:0.##}%; Peeler/Slicer {run.ActualPeelerSlicerPercent:0.##}%; Waste {run.ActualWastePercent:0.##}%")
            .AppendLine($"Accuracy: Size {run.SizeAccuracyScore:0.##}%; Grade {run.GradeAccuracyScore:0.##}%; Packout {run.PackoutAccuracyScore:0.##}%; Juice {run.JuiceAccuracyScore:0.##}%; Peeler/Slicer {run.PeelerSlicerAccuracyScore:0.##}%; Waste {run.WasteAccuracyScore:0.##}%; Overall {run.OverallAccuracyScore:0.##}%")
            .AppendLine($"QC total-defect context: {WeightedProjectionDefectPercentage(run):0.##}% of represented fruit. This is factual context, not a causal conclusion.")
            .AppendLine(oddity);
        if (analysisUrl is not null) text.AppendLine($"Full analysis: {analysisUrl}");
        text.AppendLine("The attached single-sheet workbook contains projection evidence, parsed and corrected lines, mappings, score inputs, and audit identifiers.");

        static string H(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
        var htmlSources = string.Join("", sources.Select(x => $"<li>{H(x)}</li>"));
        var html = $"""
            <h2>{H(title)}</h2>
            <p><strong>{H(run.FacilitySnapshot)}</strong> · {run.PackingDate:yyyy-MM-dd} · Run {run.RunNumber} · {H(run.VarietySnapshot)} · {production}</p>
            <h3>Grower lots and rooms</h3><ul>{htmlSources}</ul>
            <table>
              <tr><th align="left">Dumped</th><td>{run.DumpedBins:0.##} bins / {run.DumpedPounds:0.##} lb</td></tr>
              <tr><th align="left">Packout</th><td>Projected {ProjectedPackout(run):0.##}% / Actual {run.ActualPackoutPercent:0.##}%</td></tr>
              <tr><th align="left">Secondary output</th><td>Juice {run.ActualJuicePercent:0.##}% · Peeler/Slicer {run.ActualPeelerSlicerPercent:0.##}% · Waste {run.ActualWastePercent:0.##}%</td></tr>
              <tr><th align="left">Component accuracy</th><td>Size {run.SizeAccuracyScore:0.##}% · Grade {run.GradeAccuracyScore:0.##}% · Packout {run.PackoutAccuracyScore:0.##}% · Juice {run.JuiceAccuracyScore:0.##}% · Peeler/Slicer {run.PeelerSlicerAccuracyScore:0.##}% · Waste {run.WasteAccuracyScore:0.##}%</td></tr>
              <tr><th align="left">Official overall score</th><td><strong>{run.OverallAccuracyScore:0.##}%</strong></td></tr>
              <tr><th align="left">QC defect context</th><td>{WeightedProjectionDefectPercentage(run):0.##}% of represented fruit. Possible factual context only; no causal conclusion is claimed.</td></tr>
            </table>
            <p>{H(oddity)}</p>
            {(analysisUrl is null ? "" : $"<p><a href=\"{H(analysisUrl)}\">Open the full site analysis</a></p>")}
            <p>The attached single-sheet workbook contains projection evidence, parsed and corrected lines, mappings, score inputs, and audit identifiers.</p>
            """;
        return (text.ToString(), html);
    }

    private static decimal? ProjectedPackout(PackoutRun run) =>
        run.RunProjection.TotalProjectedPounds <= 0m
            ? null
            : run.RunProjection.TotalPackedProjectedPounds / run.RunProjection.TotalProjectedPounds * 100m;

    private static decimal? WeightedProjectionDefectPercentage(PackoutRun run)
    {
        var represented = run.RunProjection.Sources.Where(x => x.TotalDefectPercentageSnapshot is not null).ToList();
        var denominator = represented.Sum(x => Math.Max(0m, x.PackedProjectedPounds));
        return denominator <= 0m
            ? null
            : represented.Sum(x => x.PackedProjectedPounds * x.TotalDefectPercentageSnapshot!.Value) / denominator;
    }

    private static string SafeFileName(string value) =>
        string.Concat(value.Select(x => char.IsLetterOrDigit(x) || x is '-' or '_' ? x : '-')).Trim('-');

    private static string? SafeEmailError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        var singleLine = error.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return singleLine.Length <= 1000 ? singleLine : singleLine[..1000];
    }
}
