using CropQc.Api.Dtos;
using CropQc.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class QcSummaryController(IQcSummaryService summaryService, IQcSummaryEmailLogService emailLogService) : ControllerBase
{
    [HttpGet("samples/{sampleId:long}/summary-readiness")]
    public async Task<IActionResult> GetReadiness(long sampleId, CancellationToken cancellationToken)
    {
        var readiness = await summaryService.GetReadinessAsync(sampleId, cancellationToken);
        return readiness is null ? NotFound() : Ok(readiness);
    }

    [HttpPost("receipts/{receiptId:long}/email-logs")]
    public async Task<IActionResult> CreateEmailLog(long receiptId, CreateEmailLogRequest request, CancellationToken cancellationToken)
    {
        var (log, error) = await emailLogService.CreateAsync(receiptId, request, cancellationToken);
        return log is null ? BadRequest(new { error }) : Ok(log);
    }

    [HttpGet("receipts/{receiptId:long}/email-logs")]
    public async Task<IActionResult> GetEmailHistory(long receiptId, CancellationToken cancellationToken) =>
        Ok(await emailLogService.GetHistoryAsync(receiptId, cancellationToken));
}
