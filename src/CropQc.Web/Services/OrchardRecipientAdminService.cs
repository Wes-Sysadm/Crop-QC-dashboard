using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IOrchardRecipientAdminService
{
    Task<OrchardRecipientMatrixViewModel> GetMatrixAsync(string? search, CancellationToken cancellationToken);
    Task<OrchardRecipientUpsertResult> UpsertAsync(OrchardRecipientUpsertRequest request, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SetEnabledAsync(int id, bool enabled, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> DeleteAsync(int id, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class OrchardRecipientAdminService(CropQcDbContext dbContext) : IOrchardRecipientAdminService
{
    public async Task<OrchardRecipientMatrixViewModel> GetMatrixAsync(string? search, CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim() ?? "";
        var orchards = await dbContext.CanonicalOrchards.AsNoTracking()
            .Where(x => x.IsActive && x.Blocks.Any())
            .Include(x => x.Blocks).ThenInclude(x => x.CanonicalGrower).ThenInclude(x => x!.GrowerNumbers)
            .Include(x => x.ReportRecipients.Where(recipient => !recipient.IsDeleted)).ThenInclude(x => x.UpdatedByUser)
            .OrderBy(x => x.OrchardName)
            .ToListAsync(cancellationToken);
        orchards = orchards
            .Where(x => !OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(x.OrchardName))
            .ToList();

        var orchardIds = orchards.Select(x => x.Id).ToArray();
        var fieldSampleNumbers = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.CanonicalOrchardBlock != null
                && orchardIds.Contains(x.CanonicalOrchardBlock.CanonicalOrchardId)
                && x.ReceiptId == null
                && x.FieldSampleGrowerNumber != null
                && x.FieldSampleGrowerNumber != "")
            .Select(x => new { x.CanonicalOrchardBlock!.CanonicalOrchardId, GrowerNumber = x.FieldSampleGrowerNumber! })
            .ToListAsync(cancellationToken);
        var receiptNumbers = await dbContext.Receipts.AsNoTracking()
            .Where(x => x.CanonicalOrchardBlock != null
                && orchardIds.Contains(x.CanonicalOrchardBlock.CanonicalOrchardId)
                && x.GrowerNumber != null
                && x.GrowerNumber != "")
            .Select(x => new { x.CanonicalOrchardBlock!.CanonicalOrchardId, GrowerNumber = x.GrowerNumber! })
            .ToListAsync(cancellationToken);

        var rows = new List<OrchardRecipientMatrixRow>();
        var options = new List<OrchardRecipientOrchardOption>();
        foreach (var orchard in orchards)
        {
            var growers = string.Join(", ", orchard.Blocks
                .Select(x => x.CanonicalGrower?.DisplayName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x));
            var growerNumbers = string.Join(", ", orchard.Blocks
                .SelectMany(x => x.CanonicalGrower?.GrowerNumbers ?? [])
                .Where(x => x.IsActive)
                .Select(x => x.GrowerNumber)
                .Concat(fieldSampleNumbers.Where(x => x.CanonicalOrchardId == orchard.Id).Select(x => x.GrowerNumber))
                .Concat(receiptNumbers.Where(x => x.CanonicalOrchardId == orchard.Id).Select(x => x.GrowerNumber))
                .Select(OrchardIdentityClassifier.NormalizeGrowerNumber)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x));
            options.Add(new OrchardRecipientOrchardOption(orchard.Id, orchard.OrchardName, growers, growerNumbers));
            var recipients = orchard.ReportRecipients.OrderBy(x => x.EmailAddress).ToList();
            if (recipients.Count == 0)
            {
                rows.Add(new OrchardRecipientMatrixRow(orchard.Id, orchard.OrchardName, growers, growerNumbers, null, "", false, null, "", true));
                continue;
            }

            rows.AddRange(recipients.Select(recipient => new OrchardRecipientMatrixRow(
                orchard.Id,
                orchard.OrchardName,
                growers,
                growerNumbers,
                recipient.Id,
                recipient.EmailAddress,
                recipient.IsActive,
                recipient.UpdatedAt,
                recipient.UpdatedByUser?.Email ?? "System",
                false)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            rows = rows.Where(x =>
                    x.OrchardName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || x.Growers.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || x.GrowerNumbers.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || x.EmailAddress.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new OrchardRecipientMatrixViewModel { Search = normalizedSearch, Rows = rows, Orchards = options };
    }

    public async Task<OrchardRecipientUpsertResult> UpsertAsync(OrchardRecipientUpsertRequest request, string changedByEmail, CancellationToken cancellationToken)
    {
        var orchardResolution = await ResolveOrchardAsync(request.CanonicalOrchardId, request.OrchardIdentity, cancellationToken);
        if (orchardResolution.Error is not null)
        {
            return new OrchardRecipientUpsertResult(false, null, orchardResolution.Error, orchardResolution.Ambiguous, orchardResolution.Unmatched);
        }

        var parsed = QcEmailRecipientParser.Parse(request.EmailAddress);
        if (parsed.Recipients.Count != 1 || parsed.InvalidRecipients.Count > 0)
        {
            return new OrchardRecipientUpsertResult(false, request.RecipientId, "Enter one valid email address.");
        }

        var canonicalAddress = parsed.Recipients[0];
        var normalizedAddress = canonicalAddress.ToUpperInvariant();
        var orchardId = orchardResolution.OrchardId!.Value;
        var duplicate = await dbContext.OrchardReportRecipients.AnyAsync(x =>
            x.CanonicalOrchardId == orchardId
            && x.NormalizedEmailAddress == normalizedAddress
            && !x.IsDeleted
            && x.Id != (request.RecipientId ?? 0), cancellationToken);
        if (duplicate)
        {
            return new OrchardRecipientUpsertResult(false, request.RecipientId, "That email address is already configured for this orchard.");
        }

        OrchardReportRecipient recipient;
        string? before = null;
        var now = DateTimeOffset.UtcNow;
        var user = await FindUserAsync(changedByEmail, cancellationToken);
        if (request.RecipientId is null)
        {
            recipient = new OrchardReportRecipient
            {
                CanonicalOrchardId = orchardId,
                EmailAddress = canonicalAddress,
                NormalizedEmailAddress = normalizedAddress,
                IsActive = request.IsActive,
                CreatedAt = now,
                CreatedByUserId = user?.Id,
                UpdatedAt = now,
                UpdatedByUserId = user?.Id
            };
            dbContext.OrchardReportRecipients.Add(recipient);
        }
        else
        {
            var existing = await dbContext.OrchardReportRecipients.SingleOrDefaultAsync(x => x.Id == request.RecipientId && !x.IsDeleted, cancellationToken);
            if (existing is null)
            {
                return new OrchardRecipientUpsertResult(false, request.RecipientId, "Orchard recipient not found.");
            }

            recipient = existing;
            before = JsonSerializer.Serialize(new { recipient.CanonicalOrchardId, recipient.EmailAddress, recipient.IsActive });
            var wasActive = recipient.IsActive;
            recipient.CanonicalOrchardId = orchardId;
            recipient.EmailAddress = canonicalAddress;
            recipient.NormalizedEmailAddress = normalizedAddress;
            recipient.IsActive = request.IsActive;
            recipient.UpdatedAt = now;
            recipient.UpdatedByUserId = user?.Id;
            if (wasActive != request.IsActive)
            {
                AddAudit(request.IsActive ? "enable" : "disable", recipient, user, before, now);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        AddAudit(request.RecipientId is null ? "create" : "edit", recipient, user, before, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new OrchardRecipientUpsertResult(true, recipient.Id, null);
    }

    public async Task<string?> SetEnabledAsync(int id, bool enabled, string changedByEmail, CancellationToken cancellationToken)
    {
        var recipient = await dbContext.OrchardReportRecipients.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (recipient is null) return "Orchard recipient not found.";
        var before = JsonSerializer.Serialize(new { recipient.EmailAddress, recipient.IsActive });
        var now = DateTimeOffset.UtcNow;
        var user = await FindUserAsync(changedByEmail, cancellationToken);
        recipient.IsActive = enabled;
        recipient.UpdatedAt = now;
        recipient.UpdatedByUserId = user?.Id;
        AddAudit(enabled ? "enable" : "disable", recipient, user, before, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> DeleteAsync(int id, string changedByEmail, CancellationToken cancellationToken)
    {
        var recipient = await dbContext.OrchardReportRecipients.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (recipient is null) return "Orchard recipient not found.";
        var before = JsonSerializer.Serialize(new { recipient.EmailAddress, recipient.IsActive });
        var now = DateTimeOffset.UtcNow;
        var user = await FindUserAsync(changedByEmail, cancellationToken);
        recipient.IsDeleted = true;
        recipient.IsActive = false;
        recipient.DeletedAt = now;
        recipient.DeletedByUserId = user?.Id;
        recipient.UpdatedAt = now;
        recipient.UpdatedByUserId = user?.Id;
        AddAudit("delete", recipient, user, before, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<(int? OrchardId, string? Error, bool Ambiguous, bool Unmatched)> ResolveOrchardAsync(int? orchardId, string? identity, CancellationToken cancellationToken)
    {
        if (orchardId is not null)
        {
            var orchardName = await dbContext.CanonicalOrchards.AsNoTracking()
                .Where(x => x.Id == orchardId && x.IsActive)
                .Select(x => x.OrchardName)
                .SingleOrDefaultAsync(cancellationToken);
            return orchardName is not null && !OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(orchardName)
                ? (orchardId, null, false, false)
                : (null, "Orchard was not found.", false, true);
        }

        if (string.IsNullOrWhiteSpace(identity)) return (null, "Orchard is required.", false, true);
        if (OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(identity))
        {
            return (null, "A four-digit grower number cannot be used as an orchard identity.", false, true);
        }

        var key = OrchardBlockMatcher.Normalize(identity);
        var matches = await dbContext.CanonicalOrchards.AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedOrchardKey == key)
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count switch
        {
            0 => (null, "No existing canonical orchard matched.", false, true),
            > 1 => (null, "More than one canonical orchard matched; select the orchard explicitly.", true, false),
            _ => (matches[0], null, false, false)
        };
    }

    private Task<User?> FindUserAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(x => x.Email.ToUpper() == email.ToUpper(), cancellationToken);

    private void AddAudit(string action, OrchardReportRecipient recipient, User? user, string? before, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(OrchardReportRecipient),
            EntityKey = recipient.Id.ToString(),
            UserId = user?.Id,
            BeforeValuesJson = before,
            AfterValuesJson = JsonSerializer.Serialize(new { recipient.CanonicalOrchardId, recipient.EmailAddress, recipient.IsActive, recipient.IsDeleted }),
            SourceApplication = "CropQc.Web",
            CreatedAt = now
        });
}
