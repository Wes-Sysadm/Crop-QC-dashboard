using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public sealed record NumericOrchardRelationshipCounts(
    int CanonicalBlocks,
    int FieldSamples,
    int ReceiptBackedSamples,
    int Receipts,
    int ReportRecipients,
    int Photos,
    int EmailLogs,
    int AuditRecords);

public sealed record NumericOrchardBlockPlan(
    int SourceBlockId,
    string BlockName,
    bool SourceIsActive,
    int FieldSamples,
    int Receipts,
    int? ExistingTargetBlockId);

public sealed record NumericOrchardReconciliationPlan(
    int? SourceOrchardId,
    string SourceOrchardName,
    int? TargetOrchardId,
    string? TargetOrchardName,
    string GrowerNumber,
    bool TargetIsAmbiguous,
    string? Error,
    NumericOrchardRelationshipCounts Counts,
    IReadOnlyList<NumericOrchardBlockPlan> Blocks)
{
    public bool CanApply => SourceOrchardId is not null && TargetOrchardId is not null && !TargetIsAmbiguous && Error is null;
}

public sealed record NumericOrchardReconciliationResult(
    bool Applied,
    NumericOrchardReconciliationPlan Plan,
    string? Error);

public interface IOrchardIdentityReconciliationService
{
    Task<IReadOnlyList<CanonicalOrchard>> FindNumericOrchardsAsync(CancellationToken cancellationToken);
    Task<NumericOrchardReconciliationPlan> PlanAsync(string sourceOrchardName, string? targetOrchardName, string growerNumber, CancellationToken cancellationToken);
    Task<NumericOrchardReconciliationResult> ApplyAsync(string sourceOrchardName, string targetOrchardName, string growerNumber, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class OrchardIdentityReconciliationService(CropQcDbContext dbContext) : IOrchardIdentityReconciliationService
{
    public async Task<IReadOnlyList<CanonicalOrchard>> FindNumericOrchardsAsync(CancellationToken cancellationToken)
    {
        var orchards = await dbContext.CanonicalOrchards.AsNoTracking()
            .OrderBy(x => x.OrchardName)
            .ToListAsync(cancellationToken);
        return orchards
            .Where(x => OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(x.OrchardName))
            .ToList();
    }

    public async Task<NumericOrchardReconciliationPlan> PlanAsync(
        string sourceOrchardName,
        string? targetOrchardName,
        string growerNumber,
        CancellationToken cancellationToken)
    {
        var sourceIdentity = OrchardIdentityClassifier.Classify(sourceOrchardName, OrchardIdentitySource.AmbiguousOrchardOrGrower);
        var normalizedGrowerNumber = OrchardIdentityClassifier.NormalizeGrowerNumber(growerNumber);
        if (sourceIdentity.Kind != OrchardIdentityKind.GrowerNumber)
        {
            return ErrorPlan(sourceIdentity.Value, targetOrchardName, normalizedGrowerNumber, "The source is not a standalone four-digit numeric orchard identity.");
        }

        if (!OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(normalizedGrowerNumber))
        {
            return ErrorPlan(sourceIdentity.Value, targetOrchardName, normalizedGrowerNumber, "Grower number must be supplied as exactly four digits for this reconciliation.");
        }

        var sourceKey = OrchardBlockMatcher.Normalize(sourceIdentity.Value);
        var sources = await dbContext.CanonicalOrchards.AsNoTracking()
            .Where(x => x.NormalizedOrchardKey == sourceKey)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (sources.Count != 1)
        {
            return ErrorPlan(sourceIdentity.Value, targetOrchardName, normalizedGrowerNumber, sources.Count == 0
                ? "The numeric source orchard was not found."
                : "More than one source orchard matched; no reconciliation can be applied.");
        }

        var source = sources[0];
        var sourceBlocks = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
            .Where(x => x.CanonicalOrchardId == source.Id)
            .OrderBy(x => x.CanonicalBlockName)
            .ToListAsync(cancellationToken);

        CanonicalOrchard? target = null;
        var targetIsAmbiguous = false;
        string? targetError = null;
        if (string.IsNullOrWhiteSpace(targetOrchardName))
        {
            var blockKeys = sourceBlocks.Select(x => x.NormalizedBlockKey).Distinct().ToArray();
            var candidates = await dbContext.CanonicalOrchards.AsNoTracking()
                .Where(x => x.Id != source.Id && x.Blocks.Any(block => blockKeys.Contains(block.NormalizedBlockKey)))
                .OrderBy(x => x.OrchardName)
                .ToListAsync(cancellationToken);
            candidates = candidates
                .Where(x => !OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(x.OrchardName))
                .ToList();
            if (candidates.Count == 1)
            {
                target = candidates[0];
            }
            else
            {
                targetIsAmbiguous = candidates.Count > 1;
                targetError = candidates.Count == 0
                    ? "No existing orchard could be inferred from matching blocks; specify the target orchard explicitly."
                    : "More than one existing orchard shares a block name with the numeric orchard; specify the target explicitly.";
            }
        }
        else if (OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(targetOrchardName))
        {
            targetError = "The target must be a real orchard name, not a four-digit grower number.";
        }
        else
        {
            var targetKey = OrchardBlockMatcher.Normalize(targetOrchardName);
            var targets = await dbContext.CanonicalOrchards.AsNoTracking()
                .Where(x => x.NormalizedOrchardKey == targetKey && x.Id != source.Id)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (targets.Count == 1)
            {
                target = targets[0];
            }
            else
            {
                targetIsAmbiguous = targets.Count > 1;
                targetError = targets.Count == 0 ? "The target orchard was not found." : "More than one target orchard matched.";
            }
        }

        var sourceBlockIds = sourceBlocks.Select(x => x.Id).ToArray();
        var targetBlocks = target is null
            ? []
            : await dbContext.CanonicalOrchardBlocks.AsNoTracking()
                .Where(x => x.CanonicalOrchardId == target.Id)
                .ToListAsync(cancellationToken);
        var blockPlans = new List<NumericOrchardBlockPlan>();
        foreach (var block in sourceBlocks)
        {
            blockPlans.Add(new NumericOrchardBlockPlan(
                block.Id,
                block.CanonicalBlockName,
                block.IsActive,
                await dbContext.QcSamples.CountAsync(x => x.CanonicalOrchardBlockId == block.Id && x.ReceiptId == null, cancellationToken),
                await dbContext.Receipts.CountAsync(x => x.CanonicalOrchardBlockId == block.Id, cancellationToken),
                targetBlocks.SingleOrDefault(x => x.NormalizedBlockKey == block.NormalizedBlockKey)?.Id));
        }

        var sampleIds = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.CanonicalOrchardBlockId != null && sourceBlockIds.Contains(x.CanonicalOrchardBlockId.Value))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var receiptIds = await dbContext.Receipts.AsNoTracking()
            .Where(x => x.CanonicalOrchardBlockId != null && sourceBlockIds.Contains(x.CanonicalOrchardBlockId.Value))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var auditKeys = sourceBlockIds.Select(x => x.ToString()).Append(source.Id.ToString()).ToArray();
        var counts = new NumericOrchardRelationshipCounts(
            sourceBlocks.Count,
            await dbContext.QcSamples.CountAsync(x => x.CanonicalOrchardBlockId != null && sourceBlockIds.Contains(x.CanonicalOrchardBlockId.Value) && x.ReceiptId == null, cancellationToken),
            await dbContext.QcSamples.CountAsync(x => x.CanonicalOrchardBlockId != null && sourceBlockIds.Contains(x.CanonicalOrchardBlockId.Value) && x.ReceiptId != null, cancellationToken),
            receiptIds.Count,
            await dbContext.OrchardReportRecipients.CountAsync(x => x.CanonicalOrchardId == source.Id && !x.IsDeleted, cancellationToken),
            await dbContext.QcPhotos.CountAsync(x => !x.IsDeleted
                && ((x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value))
                    || (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))), cancellationToken),
            await dbContext.QcSummaryEmailLogs.CountAsync(x => receiptIds.Contains(x.ReceiptId), cancellationToken),
            await dbContext.AuditLogs.CountAsync(x => auditKeys.Contains(x.EntityKey)
                && (x.EntityName == nameof(CanonicalOrchard) || x.EntityName == nameof(CanonicalOrchardBlock)), cancellationToken));

        return new NumericOrchardReconciliationPlan(
            source.Id,
            source.OrchardName,
            target?.Id,
            target?.OrchardName ?? targetOrchardName?.Trim(),
            normalizedGrowerNumber,
            targetIsAmbiguous,
            targetError,
            counts,
            blockPlans);
    }

    public async Task<NumericOrchardReconciliationResult> ApplyAsync(
        string sourceOrchardName,
        string targetOrchardName,
        string growerNumber,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(sourceOrchardName, targetOrchardName, growerNumber, cancellationToken);
        if (!plan.CanApply)
        {
            return new NumericOrchardReconciliationResult(false, plan, plan.Error ?? "The reconciliation plan is not safe to apply.");
        }

        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
        {
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email.ToUpper() == changedByEmail.ToUpper(), cancellationToken);
            var source = await dbContext.CanonicalOrchards
                .Include(x => x.Blocks).ThenInclude(x => x.Aliases)
                .Include(x => x.ReportRecipients)
                .SingleAsync(x => x.Id == plan.SourceOrchardId, cancellationToken);
            var target = await dbContext.CanonicalOrchards
                .Include(x => x.Blocks).ThenInclude(x => x.Aliases)
                .Include(x => x.ReportRecipients)
                .SingleAsync(x => x.Id == plan.TargetOrchardId, cancellationToken);

            foreach (var sourceBlock in source.Blocks.ToList())
            {
                var targetBlock = target.Blocks.SingleOrDefault(x => x.NormalizedBlockKey == sourceBlock.NormalizedBlockKey);
                var samples = await dbContext.QcSamples.Where(x => x.CanonicalOrchardBlockId == sourceBlock.Id).ToListAsync(cancellationToken);
                var receipts = await dbContext.Receipts.Where(x => x.CanonicalOrchardBlockId == sourceBlock.Id).ToListAsync(cancellationToken);
                if (targetBlock is null)
                {
                    var before = JsonSerializer.Serialize(new { sourceBlock.CanonicalOrchardId, sourceBlock.OrchardName, sourceBlock.NormalizedOrchardKey });
                    sourceBlock.CanonicalOrchard = target;
                    sourceBlock.CanonicalOrchardId = target.Id;
                    sourceBlock.OrchardName = target.OrchardName;
                    sourceBlock.NormalizedOrchardKey = target.NormalizedOrchardKey;
                    sourceBlock.UpdatedAt = now;
                    AddAudit("reconcile-orchard", nameof(CanonicalOrchardBlock), sourceBlock.Id.ToString(), user, before, new { sourceBlock.CanonicalOrchardId, sourceBlock.OrchardName, sourceBlock.NormalizedOrchardKey }, now);
                    foreach (var sample in samples.Where(x => x.ReceiptId is null && OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(x.FieldSampleGrowerName)))
                    {
                        var sampleBefore = JsonSerializer.Serialize(new { sample.CanonicalOrchardBlockId, sample.FieldSampleGrowerName, sample.FieldSampleGrowerNumber });
                        sample.FieldSampleGrowerName = target.OrchardName;
                        sample.FieldSampleGrowerNumber = string.IsNullOrWhiteSpace(sample.FieldSampleGrowerNumber) ? plan.GrowerNumber : sample.FieldSampleGrowerNumber.Trim();
                        sample.UpdatedAt = now;
                        AddAudit("reconcile-orchard", nameof(QcSample), sample.Id.ToString(), user, sampleBefore, new { sample.CanonicalOrchardBlockId, sample.FieldSampleGrowerName, sample.FieldSampleGrowerNumber }, now);
                    }

                    continue;
                }

                foreach (var sample in samples)
                {
                    var before = JsonSerializer.Serialize(new { sample.CanonicalOrchardBlockId, sample.FieldSampleGrowerName, sample.FieldSampleGrowerNumber });
                    sample.CanonicalOrchardBlockId = targetBlock.Id;
                    if (sample.ReceiptId is null && OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(sample.FieldSampleGrowerName))
                    {
                        sample.FieldSampleGrowerName = target.OrchardName;
                        sample.FieldSampleGrowerNumber = string.IsNullOrWhiteSpace(sample.FieldSampleGrowerNumber) ? plan.GrowerNumber : sample.FieldSampleGrowerNumber.Trim();
                    }

                    sample.UpdatedAt = now;
                    AddAudit("reconcile-orchard", nameof(QcSample), sample.Id.ToString(), user, before, new { sample.CanonicalOrchardBlockId, sample.FieldSampleGrowerName, sample.FieldSampleGrowerNumber }, now);
                }

                foreach (var receipt in receipts)
                {
                    var before = JsonSerializer.Serialize(new { receipt.CanonicalOrchardBlockId });
                    receipt.CanonicalOrchardBlockId = targetBlock.Id;
                    receipt.UpdatedAt = now;
                    AddAudit("reconcile-orchard", nameof(Receipt), receipt.Id.ToString(), user, before, new { receipt.CanonicalOrchardBlockId }, now);
                }

                foreach (var alias in sourceBlock.Aliases.ToList())
                {
                    if (targetBlock.Aliases.Any(x => x.NormalizedAliasKey == alias.NormalizedAliasKey))
                    {
                        alias.IsActive = false;
                        alias.UpdatedAt = now;
                    }
                    else
                    {
                        alias.CanonicalOrchardBlock = targetBlock;
                        alias.CanonicalOrchardBlockId = targetBlock.Id;
                        alias.UpdatedAt = now;
                    }
                }

                var blockBefore = JsonSerializer.Serialize(new { sourceBlock.IsActive, sourceBlock.Notes });
                sourceBlock.IsActive = false;
                sourceBlock.Notes = AppendReconciliationNote(sourceBlock.Notes, target.OrchardName, now);
                sourceBlock.UpdatedAt = now;
                AddAudit("retire-after-reconcile", nameof(CanonicalOrchardBlock), sourceBlock.Id.ToString(), user, blockBefore, new { sourceBlock.IsActive, sourceBlock.Notes, TargetBlockId = targetBlock.Id }, now);
            }

            foreach (var recipient in source.ReportRecipients.Where(x => !x.IsDeleted).ToList())
            {
                var before = JsonSerializer.Serialize(new { recipient.CanonicalOrchardId, recipient.EmailAddress, recipient.IsActive, recipient.IsDeleted });
                var duplicate = target.ReportRecipients.SingleOrDefault(x => !x.IsDeleted && x.NormalizedEmailAddress == recipient.NormalizedEmailAddress);
                if (duplicate is null)
                {
                    recipient.CanonicalOrchard = target;
                    recipient.CanonicalOrchardId = target.Id;
                }
                else
                {
                    duplicate.IsActive |= recipient.IsActive;
                    duplicate.UpdatedAt = now;
                    duplicate.UpdatedByUserId = user?.Id;
                    recipient.IsActive = false;
                    recipient.IsDeleted = true;
                    recipient.DeletedAt = now;
                    recipient.DeletedByUserId = user?.Id;
                }

                recipient.UpdatedAt = now;
                recipient.UpdatedByUserId = user?.Id;
                AddAudit("reconcile-orchard", nameof(OrchardReportRecipient), recipient.Id.ToString(), user, before, new { recipient.CanonicalOrchardId, recipient.EmailAddress, recipient.IsActive, recipient.IsDeleted }, now);
            }

            var orchardBefore = JsonSerializer.Serialize(new { source.IsActive });
            source.IsActive = false;
            source.UpdatedAt = now;
            AddAudit("retire-after-reconcile", nameof(CanonicalOrchard), source.Id.ToString(), user, orchardBefore, new { source.IsActive, TargetOrchardId = target.Id, plan.GrowerNumber }, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new NumericOrchardReconciliationResult(true, plan, null);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static NumericOrchardReconciliationPlan ErrorPlan(string source, string? target, string growerNumber, string error) =>
        new(null, source, null, target?.Trim(), growerNumber, false, error, new NumericOrchardRelationshipCounts(0, 0, 0, 0, 0, 0, 0, 0), []);

    private static string AppendReconciliationNote(string? notes, string targetOrchardName, DateTimeOffset now)
    {
        var reconciliation = $"Retired after numeric orchard reconciliation to {targetOrchardName} at {now:O}.";
        return string.IsNullOrWhiteSpace(notes) ? reconciliation : $"{notes.Trim()} {reconciliation}";
    }

    private void AddAudit(string action, string entityName, string entityKey, User? user, string? before, object after, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityKey,
            UserId = user?.Id,
            BeforeValuesJson = before,
            AfterValuesJson = JsonSerializer.Serialize(after),
            SourceApplication = "CropQc.OrchardReconciliation",
            CreatedAt = now
        });
}
