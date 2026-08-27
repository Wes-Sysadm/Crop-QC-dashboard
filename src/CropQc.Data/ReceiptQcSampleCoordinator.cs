using System.Collections.Concurrent;
using System.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data;

public sealed record ReceiptQcSampleOpenResult(
    QcSample? Sample,
    Receipt? Receipt,
    bool Created,
    string? Error,
    bool HistoricalConflict = false);

public sealed record HistoricalReceiptQcSampleAudit(
    string ReceiptNumber,
    long SampleId,
    int SampleSequenceNumber,
    string SampleType,
    int EnteredFruitCount,
    int PhotoCount,
    string EmailStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public static class ReceiptQcSampleCoordinator
{
    public const string HistoricalConflictMessage =
        "This historical receipt has multiple active QC samples and requires reconciliation. No new sample was created.";

    private static readonly ConcurrentDictionary<long, SemaphoreSlim> ReceiptLocks = new();

    public static async Task<ReceiptQcSampleOpenResult> OpenOrCreateAsync(
        CropQcDbContext dbContext,
        long receiptId,
        bool allowCreate,
        int? requestedSampleTypeId,
        int? takenByUserId,
        int? qcStationId,
        int? actualSampleSize,
        DateTimeOffset? sampleTakenAt,
        string? notes,
        CancellationToken cancellationToken)
    {
        var gate = ReceiptLocks.GetOrAdd(receiptId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            var receipt = await LockReceiptAsync(dbContext, receiptId, cancellationToken);
            if (receipt is null)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return new(null, null, false, "Receipt not found.");
            }

            var activeSamples = await dbContext.QcSamples
                .Include(x => x.SampleType)
                .Where(x => x.ReceiptId == receiptId && !x.IsDeleted)
                .OrderBy(x => x.SampleSequenceNumber)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            if (activeSamples.Count > 1)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new(null, receipt, false, HistoricalConflictMessage, HistoricalConflict: true);
            }

            if (activeSamples.Count == 1)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new(activeSamples[0], receipt, false, null);
            }

            if (!allowCreate)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new(null, receipt, false, "You do not have permission to create the QC sample for this Receipt.");
            }

            var expectedSampleTypeName = ExpectedSampleTypeName(receipt.ReceiptType);
            if (expectedSampleTypeName is null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new(null, receipt, false, $"Receipt type '{receipt.ReceiptType}' does not have a configured QC sample mapping.");
            }

            var matchingSampleTypes = await dbContext.SampleTypes
                .Where(x => x.IsActive && x.Name.ToUpper() == expectedSampleTypeName.ToUpper())
                .ToListAsync(cancellationToken);
            if (matchingSampleTypes.Count != 1)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new(null, receipt, false,
                    $"The active Sample Type '{expectedSampleTypeName}' must be configured exactly once before Receiving can be opened.");
            }

            var sampleType = matchingSampleTypes[0];
            if (requestedSampleTypeId is > 0 && requestedSampleTypeId != sampleType.Id)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new(null, receipt, false,
                    $"Receipt type '{receipt.ReceiptType}' requires '{expectedSampleTypeName}'. Another Sample Type cannot be selected.");
            }

            var now = DateTimeOffset.UtcNow;
            var sample = new QcSample
            {
                ReceiptId = receipt.Id,
                SampleTypeId = sampleType.Id,
                SampleSequenceNumber = 1,
                Status = "Data Entry In Progress",
                StarchStatus = "Starch Pending",
                PhotoStatus = "Photo Pending",
                EmailStatus = "Not Sent",
                TakenByUserId = takenByUserId,
                QcStationId = qcStationId,
                ActualSampleSize = actualSampleSize is > 0 ? actualSampleSize : 10,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                SampleTakenAt = sampleTakenAt ?? now,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.QcSamples.Add(sample);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(sample, receipt, true, null);
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task<IReadOnlyList<HistoricalReceiptQcSampleAudit>> GetHistoricalDuplicateAuditAsync(
        CropQcDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var duplicateReceiptIds = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.ReceiptId != null && !x.IsDeleted)
            .GroupBy(x => x.ReceiptId!.Value)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        return await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.ReceiptId != null && duplicateReceiptIds.Contains(x.ReceiptId.Value) && !x.IsDeleted)
            .OrderBy(x => x.Receipt!.CompuTechReceiptId)
            .ThenBy(x => x.SampleSequenceNumber)
            .ThenBy(x => x.Id)
            .Select(x => new HistoricalReceiptQcSampleAudit(
                x.Receipt!.CompuTechReceiptId,
                x.Id,
                x.SampleSequenceNumber,
                x.SampleType.Name,
                x.FruitReadings.Count(r => r.Pressure1Lbs != null
                    || r.Pressure2Lbs != null
                    || r.WeightGrams != null
                    || r.StarchScaleValueId != null
                    || r.GradeId != null
                    || r.DefectsInspected),
                x.Photos.Count(p => !p.IsDeleted) + x.Receipt.Photos.Count(p => !p.IsDeleted),
                x.EmailStatus,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public static string? ExpectedSampleTypeName(string? receiptType)
    {
        var normalized = receiptType?.Trim();
        if (string.Equals(normalized, "Truck receipt", StringComparison.OrdinalIgnoreCase)) return "Receiving Sample";
        if (string.Equals(normalized, "Door sample", StringComparison.OrdinalIgnoreCase)) return "Door Sample";
        if (string.Equals(normalized, "Lot sample", StringComparison.OrdinalIgnoreCase)) return "Lot Sample";
        return null;
    }

    private static Task<Receipt?> LockReceiptAsync(
        CropQcDbContext dbContext,
        long receiptId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return dbContext.Receipts
                .FromSqlInterpolated($"SELECT * FROM \"Receipts\" WHERE \"Id\" = {receiptId} AND NOT \"IsDeleted\" FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
        }

        return dbContext.Receipts.SingleOrDefaultAsync(x => x.Id == receiptId && !x.IsDeleted, cancellationToken);
    }
}
