using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public interface IOrchardContactImportService
{
    Task<OrchardContactImportIndexViewModel> GetIndexAsync(OrchardContactDryRunViewModel? preview, CancellationToken cancellationToken);
    Task<OrchardContactDryRunViewModel> PreviewAsync(IFormFile workbook, CancellationToken cancellationToken);
    Task<byte[]> ExportDryRunCsvAsync(IFormFile workbook, CancellationToken cancellationToken);
    Task<(long? BatchId, string? Error)> StageAsync(IFormFile workbook, string changedByEmail, CancellationToken cancellationToken);
    Task<OrchardContactImportBatchViewModel?> GetBatchAsync(long id, CancellationToken cancellationToken);
    Task<string?> ReviewAsync(OrchardContactImportDecisionForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<OrchardContactImportApplyResult> ApplyAsync(OrchardContactImportApplyForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class OrchardContactImportService(
    CropQcDbContext dbContext,
    IOrchardContactWorkbookParser workbookParser,
    IOrchardRecipientAdminService recipientAdminService,
    IOrchardIdentityResolverService? identityResolverService = null) : IOrchardContactImportService
{
    private const string WorksheetName = OrchardContactWorkbookParser.AuthoritativeWorksheet;
    private const string ProductionConfirmation = "APPLY ORCHARD RECIPIENTS";
    private static readonly TimeSpan MaximumBackupAge = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IOrchardIdentityResolverService IdentityResolver { get; } =
        identityResolverService ?? new OrchardIdentityResolverService(dbContext);

    public async Task<OrchardContactImportIndexViewModel> GetIndexAsync(
        OrchardContactDryRunViewModel? preview,
        CancellationToken cancellationToken)
    {
        var batches = await dbContext.OrchardContactImportBatches.AsNoTracking()
            .Include(x => x.UploadedByUser)
            .Include(x => x.Rows)
            .OrderByDescending(x => x.UploadedAt)
            .Take(25)
            .ToListAsync(cancellationToken);
        return new OrchardContactImportIndexViewModel
        {
            Preview = preview,
            RecentBatches = batches.Select(x => new OrchardContactImportBatchListItem(
                x.Id,
                x.OriginalFileName,
                x.WorkbookSha256,
                x.Status,
                x.OrchardManagerSourceRowCount,
                x.ParsedOrchardTokenCount,
                x.Rows.Count(r => r.ReviewDecision == OrchardContactImportDecisions.Pending),
                x.Rows.Count(r => r.ReviewDecision == OrchardContactImportDecisions.Approved),
                x.Rows.Count(r => r.ReviewDecision == OrchardContactImportDecisions.Rejected),
                x.Rows.Count(r => r.ReviewDecision == OrchardContactImportDecisions.Deferred),
                x.UploadedAt,
                x.UploadedByUser?.Email ?? "System",
                x.AppliedAt)).ToArray()
        };
    }

    public async Task<OrchardContactDryRunViewModel> PreviewAsync(
        IFormFile workbook,
        CancellationToken cancellationToken)
    {
        var parsed = await ParseUploadAsync(workbook, cancellationToken);
        var resolutionSet = await IdentityResolver.LoadAsync(cancellationToken);
        var rows = parsed.Tokens.Select(x => OrchardContactMatcher.Match(x, resolutionSet)).ToArray();
        return new OrchardContactDryRunViewModel
        {
            OriginalFileName = parsed.OriginalFileName,
            WorkbookSha256 = parsed.WorkbookSha256,
            WorksheetName = parsed.WorksheetName,
            OrchardManagerSourceRows = parsed.OrchardManagerSourceRowCount,
            ParsedOrchardTokens = parsed.Tokens.Count,
            Rows = rows
        };
    }

    public async Task<byte[]> ExportDryRunCsvAsync(IFormFile workbook, CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(workbook, cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("Workbook Row,Original Orchard Cell,Parsed Orchard Token,Manager,Email,Email Valid,Phone,Physical Address,Status,Matched Orchard,Match Method,Score,Existing Recipients,Proposed Action,Warning,Candidates");
        foreach (var row in preview.Rows)
        {
            csv.AppendLine(string.Join(",",
                Csv(row.WorkbookRowNumber.ToString(CultureInfo.InvariantCulture)),
                Csv(row.OriginalOrchardCell),
                Csv(row.ParsedOrchardToken),
                Csv(row.ManagerName),
                Csv(row.Email),
                Csv(row.EmailIsValid ? "Yes" : "No"),
                Csv(row.Phone),
                Csv(row.PhysicalAddress),
                Csv(row.ReviewStatus),
                Csv(row.SuggestedCanonicalOrchard),
                Csv(row.MatchMethod),
                Csv(row.MatchScore?.ToString("0.0000", CultureInfo.InvariantCulture)),
                Csv(string.Join("; ", row.ExistingRecipients)),
                Csv(row.ProposedAction),
                Csv(row.Warning),
                Csv(string.Join("; ", row.Candidates.Select(x => $"{x.OrchardName} ({x.SimilarityScore:0.0000}: {x.Reason})")))));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public async Task<(long? BatchId, string? Error)> StageAsync(
        IFormFile workbook,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        var parsed = await ParseUploadAsync(workbook, cancellationToken);
        var existingApplied = await dbContext.OrchardContactImportBatches.AsNoTracking()
            .Where(x => x.WorkbookSha256 == parsed.WorkbookSha256
                && x.WorksheetName == parsed.WorksheetName
                && x.Status == OrchardContactImportStatuses.Applied)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingApplied != 0)
        {
            return (existingApplied, "This exact workbook has already been applied. Review the existing immutable batch instead of staging it again.");
        }

        var resolutionSet = await IdentityResolver.LoadAsync(cancellationToken);
        var matches = parsed.Tokens.Select(x => OrchardContactMatcher.Match(x, resolutionSet)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var user = await FindUserAsync(changedByEmail, cancellationToken);
        var batch = new OrchardContactImportBatch
        {
            OriginalFileName = parsed.OriginalFileName,
            WorkbookSha256 = parsed.WorkbookSha256,
            WorksheetName = parsed.WorksheetName,
            Status = OrchardContactImportStatuses.Reviewing,
            OrchardManagerSourceRowCount = parsed.OrchardManagerSourceRowCount,
            ParsedOrchardTokenCount = parsed.Tokens.Count,
            UploadedAt = now,
            UploadedByUserId = user?.Id
        };
        for (var index = 0; index < parsed.Tokens.Count; index++)
        {
            var source = parsed.Tokens[index];
            var match = matches[index];
            batch.Rows.Add(new OrchardContactImportRow
            {
                WorkbookRowNumber = source.WorkbookRowNumber,
                OriginalOrchardCell = source.OriginalOrchardCell,
                ParsedOrchardToken = source.ParsedOrchardToken,
                ManagerDisplayName = source.ManagerDisplayName,
                NormalizedManagerName = source.NormalizedManagerName,
                EmailAddress = source.EmailAddress,
                NormalizedEmailAddress = source.NormalizedEmailAddress,
                EmailIsValid = source.EmailIsValid,
                Phone = source.Phone,
                NormalizedPhone = source.NormalizedPhone,
                PhysicalAddress = source.PhysicalAddress,
                CommunicationNote = source.CommunicationNote,
                SourceStatusNote = source.SourceStatusNote,
                MatchMethod = match.MatchMethod,
                MatchScore = match.MatchScore,
                SuggestedCanonicalOrchardId = match.SuggestedCanonicalOrchardId,
                CandidateMatchesJson = JsonSerializer.Serialize(match.Candidates, JsonOptions),
                Warning = match.Warning,
                ReviewDecision = OrchardContactImportDecisions.Pending,
                CreateAlias = match.MatchMethod == OrchardContactMatchMethods.ProposedAlias,
                CreateRecipient = source.EmailIsValid
            });
        }

        dbContext.OrchardContactImportBatches.Add(batch);
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAudit("workbook-uploaded", nameof(OrchardContactImportBatch), batch.Id.ToString(CultureInfo.InvariantCulture), user, null, new
        {
            batch.OriginalFileName,
            batch.WorkbookSha256,
            batch.WorksheetName,
            batch.OrchardManagerSourceRowCount,
            batch.ParsedOrchardTokenCount
        }, now);
        foreach (var row in batch.Rows)
        {
            AddAudit("row-parsed", nameof(OrchardContactImportRow), row.Id.ToString(CultureInfo.InvariantCulture), user, null, new
            {
                row.WorkbookRowNumber,
                row.OriginalOrchardCell,
                row.ParsedOrchardToken,
                row.MatchMethod,
                row.SuggestedCanonicalOrchardId
            }, now);
            AddAudit("match-proposed", nameof(OrchardContactImportRow), row.Id.ToString(CultureInfo.InvariantCulture), user, null, new
            {
                row.WorkbookRowNumber,
                row.ParsedOrchardToken,
                row.MatchMethod,
                row.MatchScore,
                row.SuggestedCanonicalOrchardId,
                row.Warning
            }, now);
            if (row.MatchMethod == OrchardContactMatchMethods.ProposedAlias)
            {
                AddAudit("alias-proposed", nameof(OrchardContactImportRow), row.Id.ToString(CultureInfo.InvariantCulture), user, null, new
                {
                    row.WorkbookRowNumber,
                    Alias = row.ParsedOrchardToken,
                    row.SuggestedCanonicalOrchardId
                }, now);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (batch.Id, null);
    }

    public async Task<OrchardContactImportBatchViewModel?> GetBatchAsync(long id, CancellationToken cancellationToken)
    {
        var batch = await dbContext.OrchardContactImportBatches.AsNoTracking()
            .Include(x => x.UploadedByUser)
            .Include(x => x.Rows).ThenInclude(x => x.SuggestedCanonicalOrchard)
            .Include(x => x.Rows).ThenInclude(x => x.ApprovedCanonicalOrchard)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (batch is null) return null;
        var sources = (await IdentityResolver.LoadAsync(cancellationToken)).Orchards;
        var sourceById = sources.ToDictionary(x => x.Id);
        var options = sources.Select(x => new OrchardRecipientOrchardOption(x.Id, x.OrchardName, "", "")).ToArray();
        return new OrchardContactImportBatchViewModel
        {
            Id = batch.Id,
            OriginalFileName = batch.OriginalFileName,
            WorkbookSha256 = batch.WorkbookSha256,
            WorksheetName = batch.WorksheetName,
            Status = batch.Status,
            OrchardManagerSourceRows = batch.OrchardManagerSourceRowCount,
            ParsedTokens = batch.ParsedOrchardTokenCount,
            UploadedAt = batch.UploadedAt,
            UploadedBy = batch.UploadedByUser?.Email ?? "System",
            Orchards = options,
            Rows = batch.Rows
                .OrderBy(x => x.WorkbookRowNumber)
                .ThenBy(x => x.ParsedOrchardToken)
                .Select(x =>
                {
                    var orchardId = x.ApprovedCanonicalOrchardId ?? x.SuggestedCanonicalOrchardId;
                    var existing = orchardId is int matchedId && sourceById.TryGetValue(matchedId, out var matched)
                        ? matched.Recipients.Where(r => !r.IsDeleted).Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToArray()
                        : [];
                    return new OrchardContactImportReviewRowViewModel(
                        x.Id,
                        x.WorkbookRowNumber,
                        x.OriginalOrchardCell,
                        x.ParsedOrchardToken,
                        x.ManagerDisplayName,
                        x.EmailAddress,
                        x.EmailIsValid,
                        x.Phone,
                        x.PhysicalAddress,
                        x.MatchMethod,
                        x.MatchScore,
                        x.SuggestedCanonicalOrchardId,
                        x.SuggestedCanonicalOrchard?.OrchardName,
                        DeserializeCandidates(x.CandidateMatchesJson),
                        existing,
                        x.Warning,
                        x.ReviewDecision,
                        x.ApprovedCanonicalOrchardId,
                        x.CreateAlias,
                        x.CreateRecipient,
                        x.ReactivateDeletedRecipient,
                        x.ReviewNote,
                        x.AppliedAction);
                })
                .ToArray()
        };
    }

    public async Task<string?> ReviewAsync(
        OrchardContactImportDecisionForm form,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        if (!OrchardContactImportDecisions.All.Contains(form.Decision)) return "Select a valid review decision.";
        var row = await dbContext.OrchardContactImportRows
            .Include(x => x.OrchardContactImportBatch)
            .SingleOrDefaultAsync(x => x.Id == form.RowId, cancellationToken);
        if (row is null) return "Import row was not found.";
        if (row.OrchardContactImportBatchId != form.BatchId) return "The import row does not belong to this review batch.";
        if (row.OrchardContactImportBatch.Status != OrchardContactImportStatuses.Reviewing) return "Applied or failed imports cannot be edited.";

        CanonicalOrchard? orchard = null;
        if (form.Decision == OrchardContactImportDecisions.Approved)
        {
            if (form.CanonicalOrchardId is null) return "Choose an existing canonical orchard before approving.";
            orchard = await dbContext.CanonicalOrchards.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == form.CanonicalOrchardId && x.IsActive, cancellationToken);
            if (orchard is null || OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(orchard.OrchardName))
            {
                return "Choose an active canonical orchard. Four-digit grower numbers are not orchard identities.";
            }

            if (form.CreateAlias)
            {
                var aliasKey = OrchardContactNormalization.NormalizeOrchardIdentity(row.ParsedOrchardToken);
                var conflictingAlias = await dbContext.CanonicalOrchardAliases.AsNoTracking()
                    .AnyAsync(x => x.IsActive && x.NormalizedAlias == aliasKey && x.CanonicalOrchardId != orchard.Id, cancellationToken);
                if (conflictingAlias) return "That alias is already active for another orchard.";
            }
        }

        var before = JsonSerializer.Serialize(new
        {
            row.ReviewDecision,
            row.ApprovedCanonicalOrchardId,
            row.CreateAlias,
            row.CreateRecipient,
            row.ReactivateDeletedRecipient,
            row.ReviewNote
        }, JsonOptions);
        var user = await FindUserAsync(changedByEmail, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        row.ReviewDecision = form.Decision;
        row.ApprovedCanonicalOrchardId = form.Decision == OrchardContactImportDecisions.Approved ? orchard!.Id : null;
        row.CreateAlias = form.Decision == OrchardContactImportDecisions.Approved && form.CreateAlias;
        row.CreateRecipient = form.Decision == OrchardContactImportDecisions.Approved && form.CreateRecipient && row.EmailIsValid;
        row.ReactivateDeletedRecipient = row.CreateRecipient && form.ReactivateDeletedRecipient;
        row.ReviewNote = string.IsNullOrWhiteSpace(form.ReviewNote) ? null : form.ReviewNote.Trim();
        row.ReviewedAt = now;
        row.ReviewedByUserId = user?.Id;
        AddAudit(
            form.Decision == OrchardContactImportDecisions.Approved ? "match-approved"
            : form.Decision == OrchardContactImportDecisions.Rejected ? "match-rejected"
            : form.Decision == OrchardContactImportDecisions.Deferred ? "match-deferred"
            : "review-reset",
            nameof(OrchardContactImportRow),
            row.Id.ToString(CultureInfo.InvariantCulture),
            user,
            before,
            new
            {
                row.WorkbookRowNumber,
                row.ParsedOrchardToken,
                row.ReviewDecision,
                row.ApprovedCanonicalOrchardId,
                row.CreateAlias,
                row.CreateRecipient,
                row.ReactivateDeletedRecipient,
                row.ReviewNote
            },
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<OrchardContactImportApplyResult> ApplyAsync(
        OrchardContactImportApplyForm form,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(form.ProductionConfirmation?.Trim(), ProductionConfirmation, StringComparison.Ordinal))
        {
            return Failed($"Type {ProductionConfirmation} exactly.");
        }

        if (form.Workbook is null) return Failed("Select the exact reviewed workbook.");
        if (form.VerifiedBackupRunId is null) return Failed("A recent verified production backup run ID is required.");
        if (string.IsNullOrWhiteSpace(form.ImportReason)) return Failed("Enter the reason for this production import.");

        var parsed = await ParseUploadAsync(form.Workbook, cancellationToken);
        var batch = await dbContext.OrchardContactImportBatches
            .Include(x => x.Rows)
            .SingleOrDefaultAsync(x => x.Id == form.BatchId, cancellationToken);
        if (batch is null) return Failed("Import batch was not found.");
        if (batch.Status == OrchardContactImportStatuses.Applied)
        {
            return new OrchardContactImportApplyResult(true, null, WasAlreadyApplied: true);
        }

        if (batch.Status != OrchardContactImportStatuses.Reviewing) return Failed("Only a reviewing batch can be applied.");
        if (!string.Equals(parsed.WorkbookSha256, batch.WorkbookSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Failed("Workbook checksum mismatch. Apply is blocked because this is not the reviewed file.");
        }

        if (batch.Rows.Any(x => x.ReviewDecision == OrchardContactImportDecisions.Pending))
        {
            return Failed("Every parsed orchard token must be approved, rejected, or explicitly deferred before apply.");
        }

        var cutoff = DateTimeOffset.UtcNow - MaximumBackupAge;
        var backup = await dbContext.BackupRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == form.VerifiedBackupRunId.Value, cancellationToken);
        if (backup is null
            || backup.Status != BackupRunStatuses.Succeeded
            || backup.VerifiedAt is null
            || backup.VerifiedAt < cutoff
            || backup.FileSizeBytes is null or <= 0
            || string.IsNullOrWhiteSpace(backup.Sha256)
            || string.IsNullOrWhiteSpace(backup.PackageStorageKey))
        {
            return Failed("The backup run was not found, is older than 24 hours, or did not pass durable read-back verification.");
        }

        var user = await FindUserAsync(changedByEmail, cancellationToken);
        var counts = new ApplyCounts();
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        try
        {
            foreach (var row in batch.Rows.Where(x => x.ReviewDecision == OrchardContactImportDecisions.Approved))
            {
                await ApplyRowAsync(row, batch, user, counts, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            batch.Status = OrchardContactImportStatuses.Applied;
            batch.AppliedAt = now;
            batch.AppliedByUserId = user?.Id;
            batch.VerifiedBackupRunId = backup.Id;
            batch.ImportReason = form.ImportReason.Trim();
            batch.ApplySummaryJson = JsonSerializer.Serialize(counts, JsonOptions);
            AddAudit("import-applied", nameof(OrchardContactImportBatch), batch.Id.ToString(CultureInfo.InvariantCulture), user, null, new
            {
                batch.WorkbookSha256,
                batch.WorksheetName,
                BackupRunId = backup.Id,
                batch.ImportReason,
                Counts = counts
            }, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new OrchardContactImportApplyResult(
                true,
                null,
                counts.ContactsCreated,
                counts.AssignmentsCreated,
                counts.RecipientsCreated,
                counts.DuplicatesSkipped,
                counts.AliasesCreated,
                counts.ConflictsRetained);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var failedBatch = await dbContext.OrchardContactImportBatches.SingleAsync(x => x.Id == form.BatchId, cancellationToken);
            failedBatch.Status = OrchardContactImportStatuses.Failed;
            AddAudit("import-failed", nameof(OrchardContactImportBatch), failedBatch.Id.ToString(CultureInfo.InvariantCulture), user, null, new
            {
                ErrorType = exception.GetType().Name,
                SafeMessage = "The transaction was rolled back. No approved aliases, contacts, assignments, or recipients were committed."
            }, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Failed("The import transaction failed and was rolled back. Review server logs and the immutable audit record.");
        }
    }

    private async Task ApplyRowAsync(
        OrchardContactImportRow row,
        OrchardContactImportBatch batch,
        User? user,
        ApplyCounts counts,
        CancellationToken cancellationToken)
    {
        if (row.ApprovedCanonicalOrchardId is not int orchardId)
        {
            throw new InvalidOperationException($"Approved row {row.Id} has no canonical orchard.");
        }

        var orchard = await dbContext.CanonicalOrchards.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == orchardId && x.IsActive, cancellationToken);
        if (orchard is null || OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(orchard.OrchardName))
        {
            throw new InvalidOperationException($"Approved row {row.Id} points to an invalid orchard.");
        }

        var now = DateTimeOffset.UtcNow;
        if (row.CreateAlias)
        {
            var aliasKey = OrchardContactNormalization.NormalizeOrchardIdentity(row.ParsedOrchardToken);
            var aliases = await dbContext.CanonicalOrchardAliases
                .Where(x => x.NormalizedAlias == aliasKey)
                .ToListAsync(cancellationToken);
            if (aliases.Any(x => x.CanonicalOrchardId != orchardId && x.IsActive))
            {
                throw new InvalidOperationException($"Approved alias on row {row.Id} conflicts with another orchard.");
            }

            var alias = aliases.SingleOrDefault(x => x.CanonicalOrchardId == orchardId);
            if (alias is null)
            {
                alias = new CanonicalOrchardAlias
                {
                    CanonicalOrchardId = orchardId,
                    AliasText = row.ParsedOrchardToken,
                    NormalizedAlias = aliasKey,
                    Source = $"{batch.OriginalFileName}:{batch.WorksheetName}:row {row.WorkbookRowNumber}",
                    ReviewNote = row.ReviewNote,
                    IsActive = true,
                    CreatedAt = now,
                    CreatedByUserId = user?.Id,
                    UpdatedAt = now,
                    UpdatedByUserId = user?.Id
                };
                dbContext.CanonicalOrchardAliases.Add(alias);
                counts.AliasesCreated++;
                AddAudit("alias-approved", nameof(CanonicalOrchardAlias), $"pending:{row.Id}", user, null, new
                {
                    row.WorkbookRowNumber,
                    row.ParsedOrchardToken,
                    CanonicalOrchardId = orchardId,
                    batch.WorkbookSha256
                }, now);
            }
            else if (!alias.IsActive)
            {
                alias.IsActive = true;
                alias.UpdatedAt = now;
                alias.UpdatedByUserId = user?.Id;
            }
        }

        var contact = await FindOrCreateContactAsync(row, batch, user, counts, now, cancellationToken);
        int? recipientId = null;
        var duplicateSkippedForRow = false;
        if (row.CreateRecipient && row.EmailIsValid && row.EmailAddress is not null)
        {
            var deleted = await dbContext.OrchardReportRecipients
                .Where(x =>
                    x.CanonicalOrchardId == orchardId
                    && x.NormalizedEmailAddress == row.NormalizedEmailAddress
                    && x.IsDeleted)
                .OrderByDescending(x => x.DeletedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (deleted is not null)
            {
                if (!row.ReactivateDeletedRecipient)
                {
                    counts.ConflictsRetained++;
                    AddAudit("conflict-retained", nameof(OrchardContactImportRow), row.Id.ToString(CultureInfo.InvariantCulture), user, null, new
                    {
                        row.WorkbookRowNumber,
                        CanonicalOrchardId = orchardId,
                        row.NormalizedEmailAddress,
                        Reason = "A soft-deleted recipient exists and explicit reactivation was not approved."
                    }, now);
                }
                else
                {
                    deleted.IsDeleted = false;
                    deleted.IsActive = true;
                    deleted.DeletedAt = null;
                    deleted.DeletedByUserId = null;
                    deleted.UpdatedAt = now;
                    deleted.UpdatedByUserId = user?.Id;
                    recipientId = deleted.Id;
                    AddAudit("reactivate-from-reviewed-import", nameof(OrchardReportRecipient), deleted.Id.ToString(), user, null, new
                    {
                        deleted.CanonicalOrchardId,
                        deleted.EmailAddress,
                        row.WorkbookRowNumber,
                        batch.WorkbookSha256
                    }, now);
                }
            }
            else
            {
                var active = await dbContext.OrchardReportRecipients.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CanonicalOrchardId == orchardId
                    && x.NormalizedEmailAddress == row.NormalizedEmailAddress
                    && !x.IsDeleted, cancellationToken);
                if (active is not null)
                {
                    recipientId = active.Id;
                    counts.DuplicatesSkipped++;
                    duplicateSkippedForRow = true;
                    AddAudit("duplicate-skipped", nameof(OrchardContactImportRow), row.Id.ToString(CultureInfo.InvariantCulture), user, null, new
                    {
                        row.WorkbookRowNumber,
                        CanonicalOrchardId = orchardId,
                        row.NormalizedEmailAddress,
                        ExistingRecipientId = active.Id
                    }, now);
                }
                else
                {
                    var result = await recipientAdminService.UpsertAsync(
                        new OrchardRecipientUpsertRequest(null, orchardId, null, row.EmailAddress, true),
                        user?.Email ?? "",
                        cancellationToken);
                    if (!result.Success || result.RecipientId is null)
                    {
                        throw new InvalidOperationException($"Recipient upsert failed for reviewed row {row.Id}: {result.Error}");
                    }

                    recipientId = result.RecipientId;
                    counts.RecipientsCreated++;
                }
            }
        }

        var assignment = await dbContext.OrchardManagerAssignments.SingleOrDefaultAsync(x =>
            x.CanonicalOrchardId == orchardId && x.OrchardManagerContactId == contact.Id, cancellationToken);
        if (assignment is null)
        {
            assignment = new OrchardManagerAssignment
            {
                CanonicalOrchardId = orchardId,
                OrchardManagerContactId = contact.Id,
                OrchardReportRecipientId = recipientId,
                SourceImportRowId = row.Id,
                IsActive = true,
                CreatedAt = now,
                CreatedByUserId = user?.Id,
                UpdatedAt = now,
                UpdatedByUserId = user?.Id
            };
            dbContext.OrchardManagerAssignments.Add(assignment);
            counts.AssignmentsCreated++;
        }
        else
        {
            assignment.IsActive = true;
            assignment.OrchardReportRecipientId ??= recipientId;
            assignment.UpdatedAt = now;
            assignment.UpdatedByUserId = user?.Id;
        }

        row.OrchardManagerContactId = contact.Id;
        row.OrchardReportRecipientId = recipientId;
        row.AppliedAt = now;
        row.AppliedAction = recipientId is null
            ? "Contact and orchard relationship retained; no active email recipient created."
            : duplicateSkippedForRow
                ? "Existing recipient retained; duplicate insert skipped."
                : "Approved orchard manager recipient applied.";
        AddAudit("assignment-applied", nameof(OrchardContactImportRow), row.Id.ToString(CultureInfo.InvariantCulture), user, null, new
        {
            row.WorkbookRowNumber,
            row.ParsedOrchardToken,
            CanonicalOrchardId = orchardId,
            OrchardManagerContactId = contact.Id,
            OrchardReportRecipientId = recipientId,
            row.AppliedAction
        }, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<OrchardManagerContact> FindOrCreateContactAsync(
        OrchardContactImportRow row,
        OrchardContactImportBatch batch,
        User? user,
        ApplyCounts counts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<OrchardManagerContact> matches;
        if (row.EmailIsValid && row.NormalizedEmailAddress is not null)
        {
            matches = await dbContext.OrchardManagerContacts
                .Where(x => x.NormalizedEmailAddress == row.NormalizedEmailAddress)
                .Take(2)
                .ToListAsync(cancellationToken);
        }
        else
        {
            matches = await dbContext.OrchardManagerContacts
                .Where(x => x.NormalizedDisplayName == row.NormalizedManagerName
                    && x.NormalizedPhone == row.NormalizedPhone)
                .Take(2)
                .ToListAsync(cancellationToken);
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"Contact identity for reviewed row {row.Id} is ambiguous.");
        }

        var contact = matches.SingleOrDefault();
        if (contact is null)
        {
            contact = new OrchardManagerContact
            {
                DisplayName = row.ManagerDisplayName,
                NormalizedDisplayName = row.NormalizedManagerName,
                EmailAddress = row.EmailIsValid ? row.EmailAddress : null,
                NormalizedEmailAddress = row.EmailIsValid ? row.NormalizedEmailAddress : null,
                Phone = row.Phone,
                NormalizedPhone = row.NormalizedPhone,
                CommunicationNote = row.CommunicationNote,
                SourceWorkbook = batch.OriginalFileName,
                SourceWorksheet = batch.WorksheetName,
                SourceRowNumber = row.WorkbookRowNumber,
                ImportedAt = now,
                IsActive = true,
                CreatedAt = now,
                CreatedByUserId = user?.Id,
                UpdatedAt = now,
                UpdatedByUserId = user?.Id
            };
            dbContext.OrchardManagerContacts.Add(contact);
            await dbContext.SaveChangesAsync(cancellationToken);
            counts.ContactsCreated++;
            AddAudit("contact-created", nameof(OrchardManagerContact), contact.Id.ToString(CultureInfo.InvariantCulture), user, null, new
            {
                contact.DisplayName,
                contact.EmailAddress,
                contact.Phone,
                contact.SourceWorkbook,
                contact.SourceWorksheet,
                contact.SourceRowNumber
            }, now);
        }

        return contact;
    }

    private async Task<ParsedOrchardContactWorkbook> ParseUploadAsync(IFormFile? workbook, CancellationToken cancellationToken)
    {
        if (workbook is null || workbook.Length == 0) throw new InvalidDataException("Select Master Contact List.xlsx.");
        await using var stream = workbook.OpenReadStream();
        return await workbookParser.ParseAsync(stream, workbook.FileName, cancellationToken);
    }

    private Task<User?> FindUserAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(x => x.Email.ToUpper() == email.ToUpper(), cancellationToken);

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken) =>
        !dbContext.Database.IsRelational()
            ? null
            : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    private void AddAudit(string action, string entityName, string entityKey, User? user, object? before, object? after, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityKey,
            UserId = user?.Id,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
            SourceApplication = "CropQc.Web",
            CreatedAt = now
        });

    private static IReadOnlyList<OrchardMatchCandidateViewModel> DeserializeCandidates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<OrchardMatchCandidateViewModel[]>(json, JsonOptions) ?? [];
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static OrchardContactImportApplyResult Failed(string error) => new(false, error);

    private sealed class ApplyCounts
    {
        public int ContactsCreated { get; set; }
        public int AssignmentsCreated { get; set; }
        public int RecipientsCreated { get; set; }
        public int DuplicatesSkipped { get; set; }
        public int AliasesCreated { get; set; }
        public int ConflictsRetained { get; set; }
    }
}
