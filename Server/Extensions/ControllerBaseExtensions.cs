using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Services;

namespace Remotely.Server.Extensions;

public static class ControllerBaseExtensions
{
    /// <summary>
    /// True only for a signed-in user that is a server admin.  API keys belong to an
    /// organization, not to the server, so they never count as server admin.
    /// </summary>
    public static async Task<bool> IsServerAdmin(this ControllerBase controller, IDataService dataService)
    {
        var identity = controller.User.Identity;
        if (identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(identity.Name))
        {
            return false;
        }

        var userResult = await dataService.GetUserByName(identity.Name);
        return userResult.IsSuccess && userResult.Value.IsServerAdmin;
    }
}
