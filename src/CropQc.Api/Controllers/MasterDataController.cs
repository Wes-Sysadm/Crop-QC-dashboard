using CropQc.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api/master-data")]
public sealed class MasterDataController(IMasterDataService service) : ControllerBase
{
    [HttpGet("warehouses")]
    public async Task<IActionResult> GetWarehouses(CancellationToken cancellationToken) => Ok(await service.GetWarehousesAsync(cancellationToken));

    [HttpGet("warehouses/{warehouseId:int}/rooms")]
    public async Task<IActionResult> GetRooms(int warehouseId, CancellationToken cancellationToken) => Ok(await service.GetRoomsAsync(warehouseId, cancellationToken));

    [HttpGet("fruit-profiles")]
    public async Task<IActionResult> GetFruitProfiles(CancellationToken cancellationToken) => Ok(await service.GetFruitProfilesAsync(cancellationToken));

    [HttpGet("grades")]
    public async Task<IActionResult> GetGrades(CancellationToken cancellationToken) => Ok(await service.GetGradesAsync(cancellationToken));

    [HttpGet("defect-types")]
    public async Task<IActionResult> GetDefectTypes(CancellationToken cancellationToken) => Ok(await service.GetDefectTypesAsync(cancellationToken));

    [HttpGet("sample-types")]
    public async Task<IActionResult> GetSampleTypes(CancellationToken cancellationToken) => Ok(await service.GetSampleTypesAsync(cancellationToken));

    [HttpGet("starch-scale-values")]
    public async Task<IActionResult> GetStarchScaleValues(CancellationToken cancellationToken) => Ok(await service.GetStarchScaleValuesAsync(cancellationToken));

    [HttpGet("fruit-size-thresholds")]
    public async Task<IActionResult> GetFruitSizeThresholds([FromQuery] string? fruitType, CancellationToken cancellationToken) =>
        Ok(await service.GetFruitSizeThresholdsAsync(fruitType, cancellationToken));
}
