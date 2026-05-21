using CropQc.Api.Dtos;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IQcSummaryService
{
    Task<QcSummaryReadinessDto?> GetReadinessAsync(long sampleId, CancellationToken cancellationToken);
}

public sealed class QcSummaryService(CropQcDbContext dbContext) : IQcSummaryService
{
    public async Task<QcSummaryReadinessDto?> GetReadinessAsync(long sampleId, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.AsNoTracking()
            .Include(x => x.Receipt)
            .Include(x => x.FruitReadings)
            .SingleOrDefaultAsync(x => x.Id == sampleId, cancellationToken);

        if (sample is null)
        {
            return null;
        }

        var receiptPhotos = await dbContext.QcPhotos.AsNoTracking()
            .Where(x => x.ReceiptId == sample.ReceiptId)
            .Select(x => x.PhotoType)
            .ToListAsync(cancellationToken);

        var samplePhotos = await dbContext.QcPhotos.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .Select(x => x.PhotoType)
            .ToListAsync(cancellationToken);

        var rows = sample.FruitReadings.Select(x => new ReadinessFruitRow(
            x.IsCompleted,
            x.Pressure1Lbs is not null,
            x.Pressure2Lbs is not null,
            x.WeightGrams is not null,
            x.GradeId is not null,
            x.StarchScaleValueId is not null)).ToList();

        return ReadinessEvaluator.Evaluate(new ReadinessEvaluationInput(
            true,
            rows,
            receiptPhotos.Contains("BinTruck"),
            samplePhotos.Contains("SampleBeforeCutting"),
            samplePhotos.Contains("CutFruit"),
            samplePhotos.Contains("FruitAfterStarch")));
    }
}
