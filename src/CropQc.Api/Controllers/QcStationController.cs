using CropQc.Api.Dtos;
using CropQc.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api/qc-station")]
public sealed class QcStationController(IQcStationApiService service) : ControllerBase
{
    [HttpGet("samples/today")]
    public async Task<IActionResult> GetTodaySamples([FromQuery] string? warehouseCode, CancellationToken cancellationToken) =>
        Ok(await service.GetTodaySamplesAsync(warehouseCode, cancellationToken));

    [HttpGet("samples/{sampleId:long}")]
    public async Task<IActionResult> GetSampleDetail(long sampleId, CancellationToken cancellationToken)
    {
        var sample = await service.GetSampleDetailAsync(sampleId, cancellationToken);
        return sample is null ? NotFound() : Ok(sample);
    }

    [HttpPut("samples/{sampleId:long}/pressures")]
    public async Task<IActionResult> UpdatePressures(long sampleId, UpdateQcStationPressuresRequest request, CancellationToken cancellationToken)
    {
        var (sample, error) = await service.UpdatePressuresAsync(sampleId, request, cancellationToken);
        return sample is null ? BadRequest(new { error }) : Ok(sample);
    }
}
