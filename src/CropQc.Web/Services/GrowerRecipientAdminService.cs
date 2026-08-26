using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public interface IGrowerRecipientAdminService
{
    Task<GrowerRecipientMatrixViewModel> GetMatrixAsync(string? search, CancellationToken cancellationToken);
    Task<GrowerRecipientUpsertResult> UpsertAsync(GrowerRecipientUpsertRequest request, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SetEnabledAsync(int id, bool enabled, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> DeleteAsync(int id, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class GrowerRecipientAdminService(CropQcDbContext dbContext) : IGrowerRecipientAdminService
{
    public async Task<GrowerRecipientMatrixViewModel> GetMatrixAsync(string? search, CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim() ?? "";
        var options = await dbContext.CanonicalGrowerNumbers.AsNoTracking()
            .Where(x => x.IsActive && x.CanonicalGrower.IsActive)
            .OrderBy(x => x.GrowerNumber)
            .ThenBy(x => x.CanonicalGrower.DisplayName)
            .Select(x => new GrowerRecipientNumberOption(x.Id, x.GrowerNumber, x.CanonicalGrower.DisplayName))
            .ToListAsync(cancellationToken);

        var rows = await dbContext.GrowerReportRecipients.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.CanonicalGrowerNumber.GrowerNumber)
            .ThenBy(x => x.EmailAddress)
            .Select(x => new GrowerRecipientMatrixRow(
                x.CanonicalGrowerNumberId,
                x.CanonicalGrowerNumber.GrowerNumber,
                x.CanonicalGrowerNumber.CanonicalGrower.DisplayName,
                x.Id,
                x.EmailAddress,
                x.IsActive,
                x.UpdatedAt,
                x.UpdatedByUser == null ? "System" : x.UpdatedByUser.Email))
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            rows = rows.Where(x =>
                    x.GrowerNumber.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || x.GrowerName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || x.EmailAddress.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new GrowerRecipientMatrixViewModel
        {
            Search = normalizedSearch,
            GrowerNumbers = options,
            Rows = rows
        };
    }

    public async Task<GrowerRecipientUpsertResult> UpsertAsync(
        GrowerRecipientUpsertRequest request,
        string changedByEmail,
        CancellationToken cancellationToken)
    {
        var numberExists = await dbContext.CanonicalGrowerNumbers.AsNoTracking()
            .AnyAsync(x => x.Id == request.CanonicalGrowerNumberId && x.IsActive && x.CanonicalGrower.IsActive, cancellationToken);
        if (!numberExists)
        {
            return new GrowerRecipientUpsertResult(false, request.RecipientId, "Select an active Grower Number.");
        }

        var parsed = QcEmailRecipientParser.Parse(request.EmailAddress);
        if (parsed.Recipients.Count != 1 || parsed.InvalidRecipients.Count > 0)
        {
            return new GrowerRecipientUpsertResult(false, request.RecipientId, "Enter one valid email address.");
        }

        var canonicalAddress = parsed.Recipients[0];
        var normalizedAddress = canonicalAddress.ToUpperInvariant();
        var duplicate = await dbContext.GrowerReportRecipients.AnyAsync(x =>
            x.CanonicalGrowerNumberId == request.CanonicalGrowerNumberId
            && x.NormalizedEmailAddress == normalizedAddress
            && !x.IsDeleted
            && x.Id != (request.RecipientId ?? 0), cancellationToken);
        if (duplicate)
        {
            return new GrowerRecipientUpsertResult(false, request.RecipientId, "That email address is already configured for this Grower Number.");
        }

        GrowerReportRecipient recipient;
        string? before = null;
        var now = DateTimeOffset.UtcNow;
        var user = await FindUserAsync(changedByEmail, cancellationToken);
        if (request.RecipientId is null)
        {
            recipient = new GrowerReportRecipient
            {
                CanonicalGrowerNumberId = request.CanonicalGrowerNumberId,
                EmailAddress = canonicalAddress,
                NormalizedEmailAddress = normalizedAddress,
                IsActive = request.IsActive,
                CreatedAt = now,
                CreatedByUserId = user?.Id,
                UpdatedAt = now,
                UpdatedByUserId = user?.Id
            };
            dbContext.GrowerReportRecipients.Add(recipient);
        }
        else
        {
            var existing = await dbContext.GrowerReportRecipients
                .SingleOrDefaultAsync(x => x.Id == request.RecipientId && !x.IsDeleted, cancellationToken);
            if (existing is null)
            {
                return new GrowerRecipientUpsertResult(false, request.RecipientId, "Grower Number recipient not found.");
            }

            recipient = existing;
            before = Snapshot(recipient);
            var wasActive = recipient.IsActive;
            recipient.CanonicalGrowerNumberId = request.CanonicalGrowerNumberId;
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

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AddAudit(request.RecipientId is null ? "create" : "edit", recipient, user, before, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            dbContext.ChangeTracker.Clear();
            return new GrowerRecipientUpsertResult(false, request.RecipientId, "That email address is already configured for this Grower Number.");
        }

        return new GrowerRecipientUpsertResult(true, recipient.Id, null);
    }

    public async Task<string?> SetEnabledAsync(int id, bool enabled, string changedByEmail, CancellationToken cancellationToken)
    {
        var recipient = await dbContext.GrowerReportRecipients.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (recipient is null) return "Grower Number recipient not found.";
        var before = Snapshot(recipient);
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
        var recipient = await dbContext.GrowerReportRecipients.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (recipient is null) return "Grower Number recipient not found.";
        var before = Snapshot(recipient);
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

    private Task<User?> FindUserAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(x => x.Email.ToUpper() == email.ToUpper(), cancellationToken);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static string Snapshot(GrowerReportRecipient recipient) =>
        JsonSerializer.Serialize(new
        {
            recipient.CanonicalGrowerNumberId,
            recipient.EmailAddress,
            recipient.IsActive,
            recipient.IsDeleted
        });

    private void AddAudit(string action, GrowerReportRecipient recipient, User? user, string? before, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(GrowerReportRecipient),
            EntityKey = recipient.Id.ToString(),
            UserId = user?.Id,
            BeforeValuesJson = before,
            AfterValuesJson = Snapshot(recipient),
            SourceApplication = "CropQc.Web",
            CreatedAt = now
        });
}
