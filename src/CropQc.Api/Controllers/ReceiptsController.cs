using CropQc.Api.Dtos;
using CropQc.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api/receipts")]
public sealed class ReceiptsController(IReceiptService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateReceiptRequest request, CancellationToken cancellationToken)
    {
        var (receipt, error) = await service.CreateAsync(request, cancellationToken);
        return receipt is null ? BadRequest(new { error }) : CreatedAtAction(nameof(GetById), new { id = receipt.Id }, receipt);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var receipt = await service.GetAsync(id, cancellationToken);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ReceiptSearchRequest request, CancellationToken cancellationToken) =>
        Ok(await service.SearchAsync(request, cancellationToken));

    [HttpPut("{id:long}/same-day-fields")]
    public async Task<IActionResult> UpdateSameDay(long id, UpdateReceiptRequest request, CancellationToken cancellationToken)
    {
        var (receipt, error) = await service.UpdateSameDayAsync(id, request, cancellationToken);
        return receipt is null ? BadRequest(new { error }) : Ok(receipt);
    }

    [HttpPost("{id:long}/needs-review")]
    public async Task<IActionResult> MarkNeedsReview(long id, [FromBody] string reason, CancellationToken cancellationToken) =>
        await service.MarkNeedsReviewAsync(id, reason, cancellationToken) ? NoContent() : NotFound();
}
