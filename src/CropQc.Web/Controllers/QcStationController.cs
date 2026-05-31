using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CropQc.Web.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/qc-station")]
public sealed class QcStationController(CropQcDbContext dbContext, ILogger<QcStationController> logger) : ControllerBase
{
    [HttpGet("samples/today")]
    public async Task<IActionResult> GetTodaySamples([FromQuery] string? warehouseCode, CancellationToken cancellationToken)
    {
        var auth = await AuthenticateStationAsync(cancellationToken);
        if (auth.Result is not null)
        {
            return auth.Result;
        }

        var today = DateTimeOffset.UtcNow.Date;
        var query = dbContext.QcSamples.AsNoTracking()
            .Include(x => x.Receipt).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt).ThenInclude(x => x.Room)
            .Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .Include(x => x.FruitReadings)
            .Where(x => x.SampleTakenAt.Date == today);

        if (!string.IsNullOrWhiteSpace(warehouseCode))
        {
            query = query.Where(x => x.Receipt.Warehouse.Code == warehouseCode);
        }

        var samples = await query
            .OrderByDescending(x => x.SampleTakenAt)
            .Select(x => new QcStationSampleListItem(
                x.Id,
                x.ReceiptId,
                x.SampleSequenceNumber <= 1 ? x.Receipt.CompuTechReceiptId : x.Receipt.CompuTechReceiptId + "(" + x.SampleSequenceNumber + ")",
                x.Receipt.Warehouse.Code,
                x.Receipt.Room.Code,
                x.Receipt.GrowerName,
                x.Receipt.LotCode,
                x.Receipt.FruitProfile.VarietyCode,
                x.Status,
                x.StarchStatus,
                x.EmailStatus,
                x.FruitReadings.Count(row => row.Pressure1Lbs != null && row.Pressure2Lbs != null),
                x.SampleTakenAt))
            .ToListAsync(cancellationToken);

        return Ok(samples);
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

        var sample = await LoadSampleAsync(sampleId, asTracking: false, cancellationToken);
        return sample is null ? NotFound() : Ok(ToDetail(sample));
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
        var station = auth.Station!;

        var sample = await LoadSampleAsync(sampleId, asTracking: true, cancellationToken);
        if (sample is null)
        {
            return NotFound(new { error = "QC sample not found." });
        }

        if (request.Rows is null)
        {
            return BadRequest(new { error = "Rows are required." });
        }

        var stationName = station.StationName == "" ? station.Name : station.StationName;
        var before = sample.FruitReadings
            .OrderBy(x => x.RowNumber)
            .Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs })
            .ToList();
        var existingRows = sample.FruitReadings.ToDictionary(x => x.RowNumber);

        foreach (var row in request.Rows)
        {
            if (row.RowNumber is < 1 or > 25)
            {
                return BadRequest(new { error = $"RowNumber {row.RowNumber} must be between 1 and 25." });
            }

            if (!existingRows.TryGetValue(row.RowNumber, out var reading))
            {
                reading = new QcFruitReading
                {
                    QcSampleId = sampleId,
                    RowNumber = row.RowNumber,
                    SizeStatus = "NotCalculated",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.QcFruitReadings.Add(reading);
                sample.FruitReadings.Add(reading);
                existingRows[row.RowNumber] = reading;
            }

            ApplyPressureOnlyUpdate(reading, row);
        }

        sample.UpdatedAt = DateTimeOffset.UtcNow;
        station.LastSyncAt = DateTimeOffset.UtcNow;
        station.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "Edit",
            EntityName = nameof(QcFruitReading),
            EntityKey = sampleId.ToString(),
            BeforeValuesJson = JsonSerializer.Serialize(before),
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                StationId = station.Id,
                station.StationCode,
                StationName = stationName,
                SampleId = sampleId,
                Rows = request.Rows.Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs })
            }),
            SourceApplication = $"CropQc.QcStation:{stationName}",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("QC Station {StationName} saved pressure-only rows for sample {SampleId}. RowCount: {RowCount}.", stationName, sampleId, request.Rows.Count);

        var updated = await LoadSampleAsync(sampleId, asTracking: false, cancellationToken);
        return Ok(ToDetail(updated!));
    }

    public static void ApplyPressureOnlyUpdate(QcFruitReading reading, UpdateQcStationPressureRow row)
    {
        reading.Pressure1Lbs = row.Pressure1Lbs;
        reading.Pressure1Source = row.Pressure1Lbs is null ? null : "FTA";
        reading.Pressure2Lbs = row.Pressure2Lbs;
        reading.Pressure2Source = row.Pressure2Lbs is null ? null : "FTA";
        reading.IsCompleted = reading.Pressure1Lbs is not null
            && reading.Pressure2Lbs is not null
            && reading.WeightGrams is not null
            && reading.GradeId is not null;
        reading.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private Task<QcSample?> LoadSampleAsync(long sampleId, bool asTracking, CancellationToken cancellationToken)
    {
        var query = dbContext.QcSamples
            .Include(x => x.Receipt).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt).ThenInclude(x => x.Room)
            .Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.StarchScaleValue)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
            .Where(x => x.Id == sampleId);

        if (!asTracking)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<(QcStation? Station, IActionResult? Result)> AuthenticateStationAsync(CancellationToken cancellationToken)
    {
        Request.Headers.TryGetValue(QcStationApiKeyValidator.StationCodeHeaderName, out var codeHeader);
        Request.Headers.TryGetValue(QcStationApiKeyValidator.HeaderName, out var keyHeader);
        var stationCode = codeHeader.FirstOrDefault();
        var apiKey = keyHeader.FirstOrDefault();
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

    private static QcStationSampleDetail ToDetail(QcSample sample) =>
        new(
            sample.Id,
            sample.ReceiptId,
            sample.GetDisplayReceiptId(),
            sample.Receipt.CompuTechReceiptId,
            sample.Receipt.GrowerName,
            sample.Receipt.LotCode,
            sample.Receipt.FruitProfile.Name,
            sample.Receipt.FruitProfile.VarietyCode,
            sample.Receipt.Warehouse.Code,
            sample.Receipt.Room.Code,
            sample.Status,
            sample.StarchStatus,
            sample.EmailStatus,
            sample.SampleTakenAt,
            Enumerable.Range(1, 25)
                .Select(rowNumber => ToFruitReading(rowNumber, sample.FruitReadings.SingleOrDefault(x => x.RowNumber == rowNumber)))
                .ToList());

    private static QcStationFruitReading ToFruitReading(int rowNumber, QcFruitReading? reading)
    {
        if (reading is null)
        {
            return new QcStationFruitReading(0, 0, rowNumber, null, null, null, null, null, null, null, null, null, null, null, "NotCalculated", false, []);
        }

        return new QcStationFruitReading(
            reading.Id,
            reading.QcSampleId,
            reading.RowNumber,
            reading.Pressure1Lbs,
            reading.Pressure1Source,
            reading.Pressure2Lbs,
            reading.Pressure2Source,
            Average(reading.Pressure1Lbs, reading.Pressure2Lbs),
            reading.WeightGrams,
            reading.GradeId,
            reading.Grade?.Code,
            reading.StarchScaleValueId,
            reading.StarchScaleValue?.Value.ToString("0.####"),
            reading.SizeCategory,
            reading.SizeStatus,
            reading.IsCompleted,
            reading.Defects.Select(x => string.IsNullOrWhiteSpace(x.Notes) ? x.DefectType.Name : $"{x.DefectType.Name}: {x.Notes}").ToList());
    }

    private static decimal? Average(decimal? pressure1Lbs, decimal? pressure2Lbs) =>
        pressure1Lbs is null || pressure2Lbs is null ? null : decimal.Round((pressure1Lbs.Value + pressure2Lbs.Value) / 2m, 2);
}

public sealed record QcStationSampleListItem(
    long SampleId,
    long ReceiptId,
    string DisplayReceiptId,
    string WarehouseCode,
    string RoomCode,
    string GrowerName,
    string LotCode,
    string VarietyCode,
    string Status,
    string StarchStatus,
    string EmailStatus,
    int CompletedPressureRows,
    DateTimeOffset SampleTakenAt);

public sealed record QcStationSampleDetail(
    long SampleId,
    long ReceiptId,
    string DisplayReceiptId,
    string OriginalReceiptId,
    string GrowerName,
    string LotCode,
    string VarietyName,
    string VarietyCode,
    string WarehouseCode,
    string RoomCode,
    string Status,
    string StarchStatus,
    string EmailStatus,
    DateTimeOffset SampleTakenAt,
    IReadOnlyList<QcStationFruitReading> FruitReadings);

public sealed record QcStationFruitReading(
    long Id,
    long QcSampleId,
    int RowNumber,
    decimal? Pressure1Lbs,
    string? Pressure1Source,
    decimal? Pressure2Lbs,
    string? Pressure2Source,
    decimal? PressureAverageLbs,
    decimal? WeightGrams,
    int? GradeId,
    string? Grade,
    int? StarchScaleValueId,
    string? Starch,
    int? SizeCategory,
    string SizeStatus,
    bool IsCompleted,
    IReadOnlyList<string> Defects);

public sealed record UpdateQcStationPressureRow(int RowNumber, decimal? Pressure1Lbs, decimal? Pressure2Lbs);

public sealed record UpdateQcStationPressuresRequest(IReadOnlyList<UpdateQcStationPressureRow>? Rows);
