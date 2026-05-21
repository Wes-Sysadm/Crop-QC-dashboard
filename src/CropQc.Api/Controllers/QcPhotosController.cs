using CropQc.Api.Dtos;
using CropQc.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api/photos")]
public sealed class QcPhotosController(IQcPhotoService service) : ControllerBase
{
    [HttpPost("metadata")]
    public async Task<IActionResult> CreateMetadata(CreateQcPhotoRequest request, CancellationToken cancellationToken)
    {
        var (photo, error) = await service.CreateAsync(request, cancellationToken);
        return photo is null ? BadRequest(new { error }) : Ok(photo);
    }
}
