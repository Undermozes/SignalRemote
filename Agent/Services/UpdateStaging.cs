using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Remotely.Agent.Services;

/// <summary>
/// Creates a fresh, unpredictable directory for staging update files.
/// The agent runs the downloaded installer as SYSTEM/root, so the files must not live
/// at a fixed path in a shared temp folder where a local user could plant or swap them.
/// </summary>
public static class UpdateStaging
{
    public static string CreateStagingDirectory()
    {
        var name = $"RemotelyUpdate-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
        var path = Path.Combine(Path.GetTempPath(), name);

        if (Directory.Exists(path))
        {
            throw new IOException($"Update staging directory already exists: {path}");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Under the SYSTEM account the temp path is C:\Windows\Temp.  Subfolders
            // created there by SYSTEM aren't writable by standard users.
            Directory.CreateDirectory(path);
        }
        else
        {
            // Owner-only (0700) so other local users can't write into it.
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
