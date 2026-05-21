using CropQc.Api.Dtos;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IMasterDataService
{
    Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomDto>> GetRoomsAsync(int warehouseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FruitProfileDto>> GetFruitProfilesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GradeDto>> GetGradesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupDto>> GetDefectTypesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupDto>> GetSampleTypesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StarchScaleValueDto>> GetStarchScaleValuesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FruitSizeThresholdDto>> GetFruitSizeThresholdsAsync(string? fruitType, CancellationToken cancellationToken);
}

public sealed class MasterDataService(CropQcDbContext dbContext) : IMasterDataService
{
    public async Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(CancellationToken cancellationToken) =>
        await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new WarehouseDto(x.Id, x.Code, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RoomDto>> GetRoomsAsync(int warehouseId, CancellationToken cancellationToken) =>
        await dbContext.Rooms.AsNoTracking().Where(x => x.WarehouseId == warehouseId).OrderBy(x => x.Name)
            .Select(x => new RoomDto(x.Id, x.WarehouseId, x.Code, x.Name, x.CapacityBins, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FruitProfileDto>> GetFruitProfilesAsync(CancellationToken cancellationToken) =>
        await dbContext.FruitProfiles.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new FruitProfileDto(x.Id, x.Name, x.Description, x.VarietyCode, x.FruitType, x.ProductionType, x.IsOrganic, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GradeDto>> GetGradesAsync(CancellationToken cancellationToken) =>
        await dbContext.Grades.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new GradeDto(x.Id, x.Code, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupDto>> GetDefectTypesAsync(CancellationToken cancellationToken) =>
        await dbContext.DefectTypes.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new LookupDto(x.Id, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupDto>> GetSampleTypesAsync(CancellationToken cancellationToken) =>
        await dbContext.SampleTypes.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new LookupDto(x.Id, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StarchScaleValueDto>> GetStarchScaleValuesAsync(CancellationToken cancellationToken) =>
        await dbContext.StarchScaleValues.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new StarchScaleValueDto(x.Id, x.StarchScaleId, x.Value, x.SortOrder, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FruitSizeThresholdDto>> GetFruitSizeThresholdsAsync(string? fruitType, CancellationToken cancellationToken)
    {
        var query = dbContext.FruitSizeConversionThresholds.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fruitType))
        {
            query = query.Where(x => x.FruitType == fruitType);
        }

        return await query.OrderBy(x => x.FruitType).ThenByDescending(x => x.MinimumWeightGrams)
            .Select(x => new FruitSizeThresholdDto(x.Id, x.FruitType, x.SizeCategory, x.MinimumWeightGrams, x.IsActive))
            .ToListAsync(cancellationToken);
    }
}
