namespace CropQc.Shared;

public static class WeightedStatistics
{
    public static decimal? WeightedMean(IEnumerable<(decimal Value, decimal Weight)> values)
    {
        var rows = values.Where(x => x.Weight > 0).ToList();
        var totalWeight = rows.Sum(x => x.Weight);
        return totalWeight <= 0 ? null : rows.Sum(x => x.Value * x.Weight) / totalWeight;
    }

    public static decimal? SampleStandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        var mean = values.Average();
        var sumSquares = values.Sum(x => Math.Pow((double)(x - mean), 2));
        return (decimal)Math.Sqrt(sumSquares / (values.Count - 1));
    }

    public static decimal? WeightedSampleStandardDeviation(IEnumerable<(decimal Value, decimal Weight)> values)
    {
        var rows = values.Where(x => x.Weight > 0).ToList();
        if (rows.Count < 2)
        {
            return null;
        }

        var totalWeight = rows.Sum(x => x.Weight);
        var squaredWeightSum = rows.Sum(x => x.Weight * x.Weight);
        var denominator = totalWeight - squaredWeightSum / totalWeight;
        if (denominator <= 0)
        {
            return null;
        }

        var mean = WeightedMean(rows)!.Value;
        // Reliability-weighted sample variance: sum(w*(x-mean)^2) / (sum(w) - sum(w^2)/sum(w)).
        var weightedSquares = rows.Sum(x => x.Weight * (decimal)Math.Pow((double)(x.Value - mean), 2));
        return (decimal)Math.Sqrt((double)(weightedSquares / denominator));
    }

    public static decimal NormalizeChangeToThirtyDays(decimal latestValue, decimal priorValue, double elapsedDays)
    {
        if (elapsedDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedDays), "Elapsed days must be positive.");
        }

        return (latestValue - priorValue) / (decimal)elapsedDays * 30m;
    }
}
