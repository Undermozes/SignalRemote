using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Auth;
using Remotely.Server.Extensions;
using Remotely.Server.Services;
using Remotely.Shared;

namespace Remotely.Server.API;

[Route("api/[controller]")]
[ApiController]
public class FileSharingController : ControllerBase
{
    private readonly IDataService _dataService;

    public FileSharingController(IDataService dataService)
    {
        _dataService = dataService;
    }

    [HttpGet("{id}")]
    [ServiceFilter(typeof(ExpiringTokenFilter))]
    public async Task<IActionResult> Get(string id)
    {
        var sharedFileResult = await _dataService.GetSharedFiled(id);

        if (!sharedFileResult.IsSuccess)
        {
            return NotFound();
        }

        var sharedFile = sharedFileResult.Value;

        // When the request is authenticated via an organization identity (user session or API key),
        // enforce that the file belongs to the caller's organization to prevent IDOR.
        // Expiring-token requests (used by internal agents) do not carry an org identity,
        // so they are allowed to proceed without the ownership check.
        if (Request.Headers.TryGetOrganizationId(out var requestOrgId) &&
            !string.IsNullOrEmpty(requestOrgId) &&
            !string.Equals(sharedFile.OrganizationID, requestOrgId, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var contentType = sharedFile.ContentType ?? "application/octet-stream";
        return File(sharedFile.FileContents, contentType, sharedFile.FileName);
    }

    [HttpPost]
    [ServiceFilter(typeof(ExpiringTokenFilter))]
    [RequestSizeLimit(AppConstants.MaxUploadFileSize)]
    public async Task<IEnumerable<string>> Post()
    {
        if (Request.Form.Files.Count !> 0)
        {
            return Array.Empty<string>();
        }

        var fileIds = new List<string>();

        if (!Request.Headers.TryGetOrganizationId(out var orgId))
        {
            orgId = string.Empty;
        }

        foreach (var file in Request.Form.Files)
        {
            var id = await _dataService.AddSharedFile(file, orgId);
            fileIds.Add(id);
        }
        return fileIds;
    }
}
