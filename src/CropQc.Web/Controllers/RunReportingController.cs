using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("RunReporting")]
[Authorize(Policy = AccessPolicyNames.BinsRunView)]
public sealed class RunReportingController(IGrowerLotProgressService growerLotProgressService) : Controller
{
    [HttpGet("Growers")]
    public async Task<IActionResult> Growers(
        [FromQuery] GrowerLotProgressFilterForm filter,
        CancellationToken cancellationToken) =>
        View(await growerLotProgressService.GetAsync(filter, cancellationToken));
}
