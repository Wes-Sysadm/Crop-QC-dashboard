using CropQc.Api.Dtos;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IReceiptService
{
    Task<(ReceiptDto? Receipt, string? Error)> CreateAsync(CreateReceiptRequest request, CancellationToken cancellationToken);
    Task<ReceiptDto?> GetAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReceiptDto>> SearchAsync(ReceiptSearchRequest request, CancellationToken cancellationToken);
    Task<(ReceiptDto? Receipt, string? Error)> UpdateSameDayAsync(long id, UpdateReceiptRequest request, CancellationToken cancellationToken);
    Task<bool> MarkNeedsReviewAsync(long receiptId, string reason, CancellationToken cancellationToken);
}

public sealed class ReceiptService(CropQcDbContext dbContext, IAuditService auditService) : IReceiptService
{
    public async Task<(ReceiptDto? Receipt, string? Error)> CreateAsync(CreateReceiptRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateCreate(request);
        if (validation is not null)
        {
            return (null, validation);
        }

        var now = DateTimeOffset.UtcNow;
        var growerName = await ResolveAuthoritativeNameAsync(request.GrowerName, request.LotCode, cancellationToken);
        var receipt = new Receipt
        {
            CropYear = request.CropYear,
            ReceivedAt = request.ReceivedAt,
            CompuTechReceiptId = request.CompuTechReceiptId.Trim(),
            WarehouseId = request.WarehouseId,
            RoomId = request.RoomId,
            FruitProfileId = request.FruitProfileId,
            GrowerName = growerName,
            LotCode = request.LotCode.Trim(),
            BinCount = request.BinCount,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Receipts.Add(receipt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Create", nameof(Receipt), receipt.Id.ToString(), afterValuesJson: "Receipt created.", cancellationToken: cancellationToken);
        return (ToDto(receipt), null);
    }

    public async Task<ReceiptDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (receipt is null) return null;
        receipt.GrowerName = await ResolveAuthoritativeNameAsync(receipt.GrowerName, receipt.GrowerNumber ?? receipt.LotCode, cancellationToken);
        return ToDto(receipt);
    }

    public async Task<IReadOnlyList<ReceiptDto>> SearchAsync(ReceiptSearchRequest request, CancellationToken cancellationToken)
    {
        var query = dbContext.Receipts.AsNoTracking().Where(x => !x.IsDeleted);
        if (request.CropYear is not null) query = query.Where(x => x.CropYear == request.CropYear);
        if (!string.IsNullOrWhiteSpace(request.ReceiptId)) query = query.Where(x => x.CompuTechReceiptId.Contains(request.ReceiptId));
        if (!string.IsNullOrWhiteSpace(request.Grower))
        {
            var growerSearch = request.Grower.Trim();
            var matchingNumbers = await dbContext.CanonicalGrowerNumbers.AsNoTracking()
                .Where(x => x.IsActive && x.CanonicalGrower.IsActive
                    && x.CanonicalGrower.MergedIntoCanonicalGrowerId == null
                    && (x.CanonicalGrower.DisplayName.Contains(growerSearch)
                        || x.CanonicalGrower.Aliases.Any(alias => alias.IsActive && alias.AliasName.Contains(growerSearch))))
                .Select(x => x.GrowerNumber)
                .Distinct()
                .ToListAsync(cancellationToken);
            query = query.Where(x => x.GrowerName.Contains(growerSearch)
                || matchingNumbers.Contains(x.GrowerNumber ?? x.LotCode));
        }
        if (!string.IsNullOrWhiteSpace(request.Lot)) query = query.Where(x => x.LotCode.Contains(request.Lot));
        if (request.WarehouseId is not null) query = query.Where(x => x.WarehouseId == request.WarehouseId);
        if (request.RoomId is not null) query = query.Where(x => x.RoomId == request.RoomId);
        if (request.FruitProfileId is not null) query = query.Where(x => x.FruitProfileId == request.FruitProfileId);

        var receipts = await query.OrderByDescending(x => x.ReceivedAt).Take(200).ToListAsync(cancellationToken);
        var numberKeys = receipts.Select(x => NormalizeGrowerNumber(x.GrowerNumber ?? x.LotCode)).Where(x => x.Length > 0).Distinct().ToList();
        var names = await dbContext.CanonicalGrowerNumbers.AsNoTracking()
            .Where(x => x.IsActive && numberKeys.Contains(x.NormalizedGrowerNumber)
                && x.CanonicalGrower.IsActive && x.CanonicalGrower.MergedIntoCanonicalGrowerId == null)
            .Select(x => new { x.NormalizedGrowerNumber, x.CanonicalGrower.DisplayName })
            .ToListAsync(cancellationToken);
        var uniqueNames = names.GroupBy(x => x.NormalizedGrowerNumber, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Select(y => y.DisplayName).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(x => x.Key, x => x.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var receipt in receipts)
        {
            if (uniqueNames.TryGetValue(NormalizeGrowerNumber(receipt.GrowerNumber ?? receipt.LotCode), out var authoritativeName))
            {
                receipt.GrowerName = authoritativeName;
            }
        }
        return receipts.Select(ToDto).ToList();
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> UpdateSameDayAsync(long id, UpdateReceiptRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return (null, "A reason is required for receipt updates.");
        }

        var receipt = await dbContext.Receipts.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (receipt is null)
        {
            return (null, "Receipt not found.");
        }

        if (receipt.ReceivedAt.Date != DateTimeOffset.UtcNow.Date)
        {
            return (null, "Only same-day receipt fields can be updated.");
        }

        var keyFieldChanged = receipt.WarehouseId != request.WarehouseId
            || receipt.RoomId != request.RoomId
            || receipt.FruitProfileId != request.FruitProfileId
            || receipt.GrowerName != request.GrowerName
            || receipt.LotCode != request.LotCode;

        receipt.CropYear = request.CropYear;
        receipt.ReceivedAt = request.ReceivedAt;
        receipt.WarehouseId = request.WarehouseId;
        receipt.RoomId = request.RoomId;
        receipt.FruitProfileId = request.FruitProfileId;
        receipt.GrowerName = await ResolveAuthoritativeNameAsync(request.GrowerName, request.LotCode, cancellationToken);
        receipt.LotCode = request.LotCode.Trim();
        receipt.BinCount = request.BinCount;
        receipt.UpdatedAt = DateTimeOffset.UtcNow;

        if (keyFieldChanged)
        {
            await MarkSamplesNeedsReviewAsync(receipt.Id, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Edit", nameof(Receipt), receipt.Id.ToString(), afterValuesJson: request.Reason, cancellationToken: cancellationToken);
        return (ToDto(receipt), null);
    }

    public async Task<bool> MarkNeedsReviewAsync(long receiptId, string reason, CancellationToken cancellationToken)
    {
        var changed = await MarkSamplesNeedsReviewAsync(receiptId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (changed)
        {
            await auditService.RecordAsync("Edit", nameof(Receipt), receiptId.ToString(), afterValuesJson: $"Needs Review: {reason}", cancellationToken: cancellationToken);
        }

        return changed;
    }

    private async Task<bool> MarkSamplesNeedsReviewAsync(long receiptId, CancellationToken cancellationToken)
    {
        var samples = await dbContext.QcSamples.Where(x => x.ReceiptId == receiptId).ToListAsync(cancellationToken);
        foreach (var sample in samples)
        {
            sample.Status = "Needs Review";
            sample.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return samples.Count > 0;
    }

    private static string? ValidateCreate(CreateReceiptRequest request)
    {
        if (request.CropYear <= 0) return "CropYear is required.";
        if (string.IsNullOrWhiteSpace(request.CompuTechReceiptId)) return "CompuTechReceiptId is required.";
        if (request.WarehouseId <= 0) return "WarehouseId is required.";
        if (request.RoomId <= 0) return "RoomId is required.";
        if (request.FruitProfileId <= 0) return "FruitProfileId is required.";
        if (string.IsNullOrWhiteSpace(request.GrowerName)) return "GrowerName is required.";
        if (string.IsNullOrWhiteSpace(request.LotCode)) return "LotCode is required.";
        if (request.BinCount <= 0) return "BinCount is required.";
        return null;
    }

    private async Task<string> ResolveAuthoritativeNameAsync(string suppliedName, string? growerNumber, CancellationToken cancellationToken)
    {
        var numberKey = NormalizeGrowerNumber(growerNumber);
        if (numberKey.Length == 0) return suppliedName.Trim();
        var matches = await dbContext.CanonicalGrowerNumbers.AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedGrowerNumber == numberKey
                && x.CanonicalGrower.IsActive && x.CanonicalGrower.MergedIntoCanonicalGrowerId == null)
            .Select(x => x.CanonicalGrower.DisplayName)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : suppliedName.Trim();
    }

    private static string NormalizeGrowerNumber(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static ReceiptDto ToDto(Receipt receipt) => new(
        receipt.Id,
        receipt.CropYear,
        receipt.ReceivedAt,
        receipt.CompuTechReceiptId,
        receipt.WarehouseId,
        receipt.RoomId,
        receipt.FruitProfileId,
        receipt.GrowerName,
        receipt.LotCode,
        receipt.BinCount,
        receipt.CreatedAt,
        receipt.UpdatedAt);
}
