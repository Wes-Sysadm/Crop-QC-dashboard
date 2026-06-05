using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface ICropYearService
{
    int GetCurrentCropYear(DateTimeOffset now);
    IReadOnlyList<int> GetCandidateCropYears(DateTimeOffset date);
    bool RequiresConfirmation(DateTimeOffset receivedAt, int selectedCropYear);
    Task<IReadOnlyList<int>> GetAvailableCropYearsAsync(CancellationToken cancellationToken);
}

public sealed class CropYearService(CropQcDbContext dbContext, IConfiguration configuration) : ICropYearService
{
    public int GetCurrentCropYear(DateTimeOffset now)
    {
        var start = GetDefaultStart(now.Year);
        return DateOnly.FromDateTime(now.DateTime) >= start ? now.Year : now.Year - 1;
    }

    public IReadOnlyList<int> GetCandidateCropYears(DateTimeOffset date)
    {
        var dateOnly = DateOnly.FromDateTime(date.DateTime);
        var candidates = new[] { date.Year - 1, date.Year, date.Year + 1 }
            .Where(cropYear => DateOnly.FromDateTime(date.DateTime) >= GetDefaultStart(cropYear)
                && DateOnly.FromDateTime(date.DateTime) <= GetDefaultEnd(cropYear))
            .Distinct()
            .ToList();

        // Crop seasons normally use August-July, but real production can start as early
        // as May and run as late as December of the following calendar year.
        if (dateOnly.Month is >= 5 and <= 7)
        {
            candidates.Add(dateOnly.Year);
        }

        if (dateOnly.Month is >= 8 and <= 12)
        {
            candidates.Add(dateOnly.Year - 1);
        }

        candidates = candidates.Distinct().OrderByDescending(x => x).ToList();

        if (candidates.Count == 0)
        {
            candidates.Add(GetCurrentCropYear(date));
        }

        return candidates;
    }

    public bool RequiresConfirmation(DateTimeOffset receivedAt, int selectedCropYear) =>
        !GetCandidateCropYears(receivedAt).Contains(selectedCropYear);

    public async Task<IReadOnlyList<int>> GetAvailableCropYearsAsync(CancellationToken cancellationToken)
    {
        var current = GetCurrentCropYear(DateTimeOffset.Now);
        var years = await dbContext.Receipts.AsNoTracking()
            .Select(x => x.CropYear)
            .Distinct()
            .ToListAsync(cancellationToken);

        years.Add(current);
        years.Add(current - 1);
        years.Add(current + 1);
        return years.Distinct().OrderByDescending(x => x).ToList();
    }

    private DateOnly GetDefaultStart(int cropYear) =>
        new(cropYear, GetInt("CropYear:DefaultStartMonth", 8), GetInt("CropYear:DefaultStartDay", 1));

    private DateOnly GetDefaultEnd(int cropYear) =>
        new(cropYear + 1, GetInt("CropYear:DefaultEndMonth", 7), GetInt("CropYear:DefaultEndDay", 31));

    private int GetInt(string key, int fallback) =>
        int.TryParse(configuration[key], out var value) ? value : fallback;
}
