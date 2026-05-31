using CropQc.Api.Dtos;
using CropQc.Api.Services;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api/qc-station")]
public sealed class QcStationController(IQcStationApiService service, CropQcDbContext dbContext, ILogger<QcStationController> logger) : ControllerBase
{
    [HttpGet("samples/today")]
    public async Task<IActionResult> GetTodaySamples([FromQuery] string? warehouseCode, CancellationToken cancellationToken)
    {
        var auth = await AuthenticateStationAsync(cancellationToken);
        return auth.Result ?? Ok(await service.GetTodaySamplesAsync(warehouseCode, cancellationToken));
    }

    [HttpGet("samples/{sampleId:long}")]
    [HttpGet("samples/{sampleId:long}/pressure")]
    public async Task<IActionResult> GetSampleDetail(long sampleId, CancellationToken cancellationToken)
    {
        var auth = await AuthenticateStationAsync(cancellationToken);
        if (auth.Result is not null)
        {
            return auth.Result;
        }

        var sample = await service.GetSampleDetailAsync(sampleId, cancellationToken);
        return sample is null ? NotFound() : Ok(sample);
    }

    [HttpPut("samples/{sampleId:long}/pressures")]
    [HttpPut("samples/{sampleId:long}/pressure")]
    [HttpPost("samples/{sampleId:long}/pressure")]
    public async Task<IActionResult> UpdatePressures(long sampleId, UpdateQcStationPressuresRequest request, CancellationToken cancellationToken)
    {
        var auth = await AuthenticateStationAsync(cancellationToken);
        if (auth.Result is not null)
        {
            return auth.Result;
        }

        logger.LogInformation(
            "QC Station pressure save requested. StationCode: {StationCode}; SampleId: {SampleId}; RowCount: {RowCount}.",
            auth.Station!.StationCode,
            sampleId,
            request.Rows?.Count ?? 0);
        var (sample, error) = await service.UpdatePressuresAsync(sampleId, request, auth.Station!, cancellationToken);
        if (sample is null)
        {
            logger.LogWarning(
                "QC Station pressure save rejected. StationCode: {StationCode}; SampleId: {SampleId}; Reason: {Reason}.",
                auth.Station!.StationCode,
                sampleId,
                error);
        }

        return sample is null ? BadRequest(new { error }) : Ok(sample);
    }

    private async Task<(QcStation? Station, IActionResult? Result)> AuthenticateStationAsync(CancellationToken cancellationToken)
    {
        Request.Headers.TryGetValue(QcStationApiKeyValidator.StationCodeHeaderName, out var stationCodeHeader);
        Request.Headers.TryGetValue(QcStationApiKeyValidator.HeaderName, out var apiKeyHeader);
        var stationCode = stationCodeHeader.FirstOrDefault();
        var apiKey = apiKeyHeader.FirstOrDefault();
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(stationCode) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("QC Station auth failed: missing credentials. StationCode: {StationCode}; RemoteIp: {RemoteIp}.", stationCode, remoteIp);
            return (null, Unauthorized(new { error = "QC Station credentials are required." }));
        }

        var station = await dbContext.QcStations.SingleOrDefaultAsync(x => x.StationCode == stationCode.Trim(), cancellationToken);
        if (station is null)
        {
            logger.LogWarning("QC Station auth failed: station not found. StationCode: {StationCode}; StationFound: false; RemoteIp: {RemoteIp}.", stationCode, remoteIp);
            return (null, Unauthorized(new { error = "QC Station credentials are invalid." }));
        }

        if (string.IsNullOrWhiteSpace(station.ApiKeyHash) || !QcStationApiKeyValidator.VerifyHashedApiKey(apiKey, station.ApiKeyHash))
        {
            logger.LogWarning("QC Station auth failed: invalid key. StationCode: {StationCode}; StationFound: true; StationActive: {StationActive}; RemoteIp: {RemoteIp}.", station.StationCode, station.IsActive, remoteIp);
            return (null, Unauthorized(new { error = "QC Station credentials are invalid." }));
        }

        if (!station.IsActive)
        {
            logger.LogWarning("QC Station auth failed: inactive station. StationCode: {StationCode}; StationFound: true; StationActive: false; RemoteIp: {RemoteIp}.", station.StationCode, remoteIp);
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "QC Station is inactive." }));
        }

        station.LastSeenAt = DateTimeOffset.UtcNow;
        station.LastSeenIp = remoteIp;
        await dbContext.SaveChangesAsync(cancellationToken);
        return (station, null);
    }
}
