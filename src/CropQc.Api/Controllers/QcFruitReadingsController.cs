using CropQc.Api.Dtos;
using CropQc.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class QcFruitReadingsController(IQcFruitReadingService service) : ControllerBase
{
    [HttpPut("samples/{sampleId:long}/fruit-readings/{rowNumber:int}")]
    public async Task<IActionResult> Upsert(long sampleId, int rowNumber, UpsertQcFruitReadingRequest request, CancellationToken cancellationToken)
    {
        var (reading, error) = await service.UpsertAsync(sampleId, rowNumber, request, cancellationToken);
        return reading is null ? BadRequest(new { error }) : Ok(reading);
    }

    [HttpPost("fruit-readings/{readingId:long}/defects")]
    public async Task<IActionResult> AddDefect(long readingId, CreateQcFruitDefectRequest request, CancellationToken cancellationToken)
    {
        var (defect, error) = await service.AddDefectAsync(readingId, request, cancellationToken);
        return defect is null ? BadRequest(new { error }) : Ok(defect);
    }

    [HttpDelete("fruit-readings/{readingId:long}/defects/{defectId:long}")]
    public async Task<IActionResult> RemoveDefect(long readingId, long defectId, CancellationToken cancellationToken) =>
        await service.RemoveDefectAsync(readingId, defectId, cancellationToken) ? NoContent() : NotFound();
}
