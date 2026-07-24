using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("MasterData/OrchardIdentity")]
[Authorize(Policy = AccessPolicyNames.MasterDataView)]
public sealed class OrchardIdentityController(IOrchardIdentityResolverService identityResolver) : Controller
{
    [HttpGet("Search")]
    public async Task<IActionResult> Search(string? query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return Ok(Array.Empty<OrchardIdentitySearchResult>());
        }

        return Ok(await identityResolver.SearchAsync(query, 30, cancellationToken));
    }
}
