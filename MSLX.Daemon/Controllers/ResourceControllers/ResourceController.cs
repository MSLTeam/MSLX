using Microsoft.AspNetCore.Mvc;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Resources;

namespace MSLX.Daemon.Controllers.ResourceControllers
{
    [ApiController]
    [Route("api/resource")]
    public class ResourceController : ControllerBase
    {
        private readonly IUnifiedResourceService _unifiedResourceService;

        public ResourceController(IUnifiedResourceService unifiedResourceService)
        {
            _unifiedResourceService = unifiedResourceService;
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] ResourceSearchFilter filter)
        {
            var results = await _unifiedResourceService.SearchAsync(filter);
            return Ok(results);
        }

        [HttpGet("{providerType}/{id}")]
        public async Task<IActionResult> GetResource(ResourceProviderType providerType, string id)
        {
            var resource = await _unifiedResourceService.GetResourceAsync(id, providerType);
            if (resource == null) return NotFound();
            return Ok(resource);
        }

        [HttpGet("{providerType}/{id}/versions")]
        public async Task<IActionResult> GetVersions(ResourceProviderType providerType, string id, [FromQuery] string gameVersion = null, [FromQuery] string loader = null)
        {
            var versions = await _unifiedResourceService.GetVersionsAsync(id, providerType, gameVersion, loader);
            return Ok(versions);
        }
    }
}
