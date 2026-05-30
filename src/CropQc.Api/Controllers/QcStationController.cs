using CropQc.Api.Dtos;
using CropQc.Api.Services;
using CropQc.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api/qc-station")]
public sealed class QcStationController(IQcStationApiService service, IConfiguration configuration) : ControllerBase
{
    [HttpGet("samples/today")]
    public async Task<IActionResult> GetTodaySamples([FromQuery] string? warehouseCode, CancellationToken cancellationToken)
    {
        var auth = ValidateStationApiKey();
        return auth ?? Ok(await service.GetTodaySamplesAsync(warehouseCode, cancellationToken));
    }

    [HttpGet("samples/{sampleId:long}")]
    [HttpGet("samples/{sampleId:long}/pressure")]
    public async Task<IActionResult> GetSampleDetail(long sampleId, CancellationToken cancellationToken)
    {
        var auth = ValidateStationApiKey();
        if (auth is not null)
        {
            return auth;
        }

        var sample = await service.GetSampleDetailAsync(sampleId, cancellationToken);
        return sample is null ? NotFound() : Ok(sample);
    }

    [HttpPut("samples/{sampleId:long}/pressures")]
    [HttpPut("samples/{sampleId:long}/pressure")]
    [HttpPost("samples/{sampleId:long}/pressure")]
    public async Task<IActionResult> UpdatePressures(long sampleId, UpdateQcStationPressuresRequest request, CancellationToken cancellationToken)
    {
        var auth = ValidateStationApiKey();
        if (auth is not null)
        {
            return auth;
        }

        var (sample, error) = await service.UpdatePressuresAsync(sampleId, request, cancellationToken);
        return sample is null ? BadRequest(new { error }) : Ok(sample);
    }

    private IActionResult? ValidateStationApiKey()
    {
        Request.Headers.TryGetValue(QcStationApiKeyValidator.HeaderName, out var provided);
        var result = QcStationApiKeyValidator.Validate(configuration["QcStation:ApiKey"], provided.FirstOrDefault());
        return result switch
        {
            QcStationApiKeyValidationResult.Valid => null,
            QcStationApiKeyValidationResult.NotConfigured => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "QC Station API key is not configured." }),
            QcStationApiKeyValidationResult.Missing => Unauthorized(new { error = "QC Station API key is required." }),
            _ => Unauthorized(new { error = "QC Station API key is invalid." })
        };
    }
}
