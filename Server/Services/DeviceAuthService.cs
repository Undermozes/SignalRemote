using Remotely.Server.Data;
using System.Security.Cryptography;
using System.Text;

namespace Remotely.Server.Services;

public enum DeviceAuthResult
{
    /// <summary>The device presented the secret that is registered for it.</summary>
    Authenticated,

    /// <summary>The device had no secret yet and has now been bound to the one it presented.</summary>
    Enrolled,

    /// <summary>
    /// Legacy agent that doesn't send a secret.  Only allowed while
    /// <see cref="Models.SettingsModel.AllowAgentsWithoutDeviceSecret"/> is enabled.
    /// </summary>
    LegacyAllowed,

    Rejected
}

public interface IDeviceAuthService
{
    Task<DeviceAuthResult> Authenticate(string deviceId, string? deviceSecret);
    Task ResetDeviceSecret(string deviceId);
}

/// <summary>
/// Authenticates agents with a per-device secret.  The agent generates the secret
/// on first connect and the server stores only its SHA-256 hash (trust on first use).
/// After that, nobody who merely knows the device ID can connect as that device.
/// Hashes are kept in the KeyValueRecords table, so no schema migration is needed.
/// </summary>
public class DeviceAuthService : IDeviceAuthService
{
    private const int MinSecretLength = 32;
    private static readonly SemaphoreSlim _enrollLock = new(1, 1);

    private readonly IAppDbFactory _appDbFactory;
    private readonly IDataService _dataService;
    private readonly ILogger<DeviceAuthService> _logger;

    public DeviceAuthService(
        IAppDbFactory appDbFactory,
        IDataService dataService,
        ILogger<DeviceAuthService> logger)
    {
        _appDbFactory = appDbFactory;
        _dataService = dataService;
        _logger = logger;
    }

    public async Task<DeviceAuthResult> Authenticate(string deviceId, string? deviceSecret)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return DeviceAuthResult.Rejected;
        }

        var key = GetRecordKey(deviceId);

        await _enrollLock.WaitAsync();
        try
        {
            using var dbContext = _appDbFactory.GetContext();
            var record = await dbContext.KeyValueRecords.FindAsync(key);

            if (record?.Value is { Length: > 0 } storedHash)
            {
                if (string.IsNullOrWhiteSpace(deviceSecret))
                {
                    _logger.LogWarning(
                        "Device {deviceId} has a registered secret, but none was supplied.  Connection rejected.",
                        deviceId);
                    return DeviceAuthResult.Rejected;
                }

                var expected = Convert.FromBase64String(storedHash);
                var actual = HashSecret(deviceSecret);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    _logger.LogWarning(
                        "Device {deviceId} supplied an invalid secret.  Connection rejected.",
                        deviceId);
                    return DeviceAuthResult.Rejected;
                }

                return DeviceAuthResult.Authenticated;
            }

            if (string.IsNullOrWhiteSpace(deviceSecret))
            {
                var settings = await _dataService.GetSettings();
                if (settings.AllowAgentsWithoutDeviceSecret)
                {
                    _logger.LogWarning(
                        "Device {deviceId} connected without a device secret (legacy agent).  " +
                        "Update the agent and disable \"Allow Agents Without Device Secret\" in Server Config.",
                        deviceId);
                    return DeviceAuthResult.LegacyAllowed;
                }

                _logger.LogWarning(
                    "Device {deviceId} connected without a device secret.  Connection rejected.",
                    deviceId);
                return DeviceAuthResult.Rejected;
            }

            if (deviceSecret.Length < MinSecretLength)
            {
                _logger.LogWarning("Device {deviceId} supplied a secret that is too short.", deviceId);
                return DeviceAuthResult.Rejected;
            }

            var hash = Convert.ToBase64String(HashSecret(deviceSecret));
            if (record is null)
            {
                await dbContext.KeyValueRecords.AddAsync(new() { Key = key, Value = hash });
            }
            else
            {
                record.Value = hash;
            }
            await dbContext.SaveChangesAsync();

            _logger.LogInformation("Device {deviceId} enrolled with a new device secret.", deviceId);
            return DeviceAuthResult.Enrolled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while authenticating device {deviceId}.", deviceId);
            return DeviceAuthResult.Rejected;
        }
        finally
        {
            _enrollLock.Release();
        }
    }

    /// <summary>
    /// Removes the stored secret so the next agent that connects with this device ID
    /// can enroll again (e.g. after reinstalling the agent on a machine).
    /// </summary>
    public async Task ResetDeviceSecret(string deviceId)
    {
        using var dbContext = _appDbFactory.GetContext();
        var record = await dbContext.KeyValueRecords.FindAsync(GetRecordKey(deviceId));
        if (record is not null)
        {
            dbContext.KeyValueRecords.Remove(record);
            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Device secret reset for device {deviceId}.", deviceId);
        }
    }

    private static Guid GetRecordKey(string deviceId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"device-secret:{deviceId.Trim().ToLowerInvariant()}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static byte[] HashSecret(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
}
