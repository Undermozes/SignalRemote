using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Auth;
using Remotely.Server.Extensions;
using Remotely.Server.Services;

namespace Remotely.Server.API;

[Route("api/[controller]")]
[ApiController]
public class ServerLogsController : ControllerBase
{
    private readonly IDataService _dataService;
    private readonly ILogsManager _logsManager;
    private readonly ILogger<ServerLogsController> _logger;

    public ServerLogsController(
        IDataService dataService,
        ILogsManager logsManager,
        ILogger<ServerLogsController> logger)
    {
        _dataService = dataService;
        _logsManager = logsManager;
        _logger = logger;
    }

    [ServiceFilter(typeof(ApiAuthorizationFilter))]
    [HttpGet("Download")]
    public async Task<IActionResult> Download()
    {
        // Server logs contain data from every organization.
        if (!await this.IsServerAdmin(_dataService))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        _logger.LogInformation(
            "Downloading server logs. Remote IP: {ip}",
            HttpContext.Connection.RemoteIpAddress);

        var zipFile = await _logsManager.ZipAllLogs();

        Response.OnCompleted(() =>
        {
            if (zipFile.Directory is null)
            {
                return Task.CompletedTask;
            }

            zipFile.Directory.Delete(true);
            return Task.CompletedTask;
        });

        return File(zipFile.OpenRead(), "application/octet-stream", zipFile.Name);
    }
}
