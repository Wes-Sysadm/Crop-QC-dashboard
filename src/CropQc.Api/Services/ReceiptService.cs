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

public sealed class ReceiptService(CropQcDbContext dbContext, IAuditService auditService,
    CropQc.Data.Inventory.CanonicalReceivingService? canonicalReceiving = null, IHttpContextAccessor? httpContext = null,
    CropQc.Shared.Inventory.IInventoryCommandExecutor? canonicalCommands = null) : IReceiptService
{
    public async Task<(ReceiptDto? Receipt, string? Error)> CreateAsync(CreateReceiptRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateCreate(request);
        var profile = await dbContext.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.FruitProfileId, cancellationToken);
        if (profile != null && !CropQc.Data.Inventory.OrasProductDefinition.IsValid(profile)) return (null, CropQc.Data.Inventory.OrasProductDefinition.Error);
        if (validation is not null)
        {
            return (null, validation);
        }

        if (dbContext.CanonicalInventoryEnabled)
        {
            var principal = httpContext?.HttpContext?.User;
            var email = principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(email))
                return (null, "An authenticated receiving operator is required.");
            if (!await CropQc.Data.Authentication.OperatorSession.CanReceiveAsync(dbContext, principal, cancellationToken))
                return (null, "Receiving create permission is required.");
            var actor = await dbContext.Users.Where(x => x.IsActive && x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
            if (actor == null || canonicalReceiving == null || string.IsNullOrWhiteSpace(request.OperationKey))
                return (null, "An active operator, canonical receiving service and operation key are required.");
            var result = await canonicalReceiving.ReceiveAsync(request.OperationKey, actor.Value, request.CropYear, request.ReceivedAt,
                request.WarehouseId, request.RoomId, request.FruitProfileId, request.GrowerLotId, request.LotCode,
                request.CompuTechReceiptId, request.BinCount, System.Text.Json.JsonSerializer.Serialize(request), cancellationToken);
            var error = CropQc.Shared.Inventory.CanonicalInventoryMessages.Result(result);
            return error != null ? (null, error) : (await GetAsync(result.Effects[0].ParentId!.Value, cancellationToken), null);
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
        var profile = await dbContext.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.FruitProfileId, cancellationToken);
        if (profile != null && !CropQc.Data.Inventory.OrasProductDefinition.IsValid(profile)) return (null, CropQc.Data.Inventory.OrasProductDefinition.Error);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return (null, "A reason is required for receipt updates.");
        }

        var receipt = await dbContext.Receipts.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (receipt is null)
        {
            return (null, "Receipt not found.");
        }

        if (receipt.IsTransferReceipt) return (null, "Use the Truck Receipt reconciliation workflow to edit this receipt.");

        if (dbContext.CanonicalInventoryEnabled)
        {
            var principal = httpContext?.HttpContext?.User;
            if (principal == null || !await CropQc.Data.Authentication.OperatorSession.CanReceiveAsync(dbContext, principal, cancellationToken))
                return (null, "An active authenticated receiving operator is required.");
            if (canonicalCommands == null || string.IsNullOrWhiteSpace(request.OperationKey) || request.ExpectedVersion == null)
                return (null, "An operation key and reviewed receipt version are required.");
            if (!string.Equals(request.LotCode.Trim(), receipt.LotCode, StringComparison.OrdinalIgnoreCase))
                return (null, "Identity changes require an administrator inventory correction.");
            var email = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value;
            var actor = await dbContext.Users.Where(x => x.IsActive && x.Email == email).Select(x => x.Id).SingleAsync(cancellationToken);
            var metadata = new CropQc.Shared.Inventory.InventoryReceiptMetadata(id, request.ExpectedVersion.Value, request.ReceivedAt,
                receipt.CompuTechReceiptId, await ResolveAuthoritativeNameAsync(request.GrowerName, request.LotCode, cancellationToken),
                new(request.CropYear, request.WarehouseId, request.RoomId, receipt.GrowerLotId ?? 0, request.FruitProfileId, receipt.CompuTechReceiptId, request.BinCount, receipt.ReceiptType), SameDayOnly: true);
            var result = await new CropQc.Data.Inventory.CanonicalReceiptMetadataService(dbContext, canonicalCommands).UpdateAsync(request.OperationKey,
                actor, metadata, request.Reason, System.Text.Json.JsonSerializer.Serialize(request), cancellationToken);
            var error = CropQc.Shared.Inventory.CanonicalInventoryMessages.Result(result);
            return error == null ? (await GetAsync(id, cancellationToken), null) : (null, error);
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
        receipt.UpdatedAt, receipt.ConcurrencyVersion);
}
